using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.AiClassification.Infrastructure.Persistence;
using PropFlow.Modules.AiRecommendation.Infrastructure.Persistence;
using PropFlow.Modules.Billing.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Infrastructure.Persistence;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Presentation;

namespace PropFlow.IntegrationTests;

public sealed partial class AiAuthorizationHttpTests
{
    private const string RequestRoot = "manager/service-requests/";

    [Fact, Trait("Feature", "FE-10")]
    public async Task Classification_review_requires_manager_current_suggestion_and_preserves_request_ownership()
    {
        var request = await SeedRequest("Phân loại B7");
        var other = await SeedRequest("Phân loại khác");
        var classification = RequestRoot + request.Id + "/classification";
        var review = classification + "/review";
        var original = (await Call<ManagerServiceRequestClassificationView>(Manager, HttpMethod.Post, classification)).Suggestion!;
        var foreign = (await Call<ManagerServiceRequestClassificationView>(Manager, HttpMethod.Post, RequestRoot + other.Id + "/classification")).Suggestion!;
        using var scope = Database.Factory.Services.CreateScope();
        var requests = scope.ServiceProvider.GetRequiredService<ServiceRequestsDbContext>();
        var cleaning = await requests.ServiceRequestCategories.Where(x => x.Code == "CLEANING").Select(x => x.Id).SingleAsync();
        var accept = new ReviewManagerServiceRequestClassification(original.Id, original.SuggestedCategoryId!.Value);
        var restore = new { suggestionId = original.Id, categoryId = cleaning, reviewedBy = Admin.Id, residentId = other.ResidentId };
        await Reject(HttpMethod.Put, review, restore, RequestState, fixture.ManagerDenials.ToArray());
        await Reject(HttpMethod.Put, review, accept with { SuggestionId = foreign.Id }, RequestState, (Manager.Client, HttpStatusCode.Conflict));
        await Reject(HttpMethod.Put, review, accept with { CategoryId = Guid.NewGuid() }, RequestState, (Manager.Client, HttpStatusCode.BadRequest));
        var business = await DownstreamState();
        var calls = fixture.Gemini.Calls.Count;
        using var start = await Staff.Client.GetAsync($"/api/v1/staff/service-requests/{request.RequestNumber}");
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var results = await Task.WhenAll(Send(Manager.Client, HttpMethod.Put, review, restore),
            Send(Manager.Client, HttpMethod.Put, review, accept));
        using var first = results[0];
        using var second = results[1];
        await Expect(results.OrderBy(x => (int)x.StatusCode).First(), HttpStatusCode.OK);
        await Expect(results.OrderBy(x => (int)x.StatusCode).Last(), HttpStatusCode.Conflict);
        var db = scope.ServiceProvider.GetRequiredService<AiClassificationDbContext>();
        var stored = await db.AiRequestClassifications.AsNoTracking().Include(x => x.Review).SingleAsync(x => x.Id == original.Id);
        Assert.Equal(Manager.Id, stored.Review!.ReviewedBy);
        Assert.Equal(original.SuggestedCategoryId, stored.PredictedCategoryId);
        var persisted = await requests.ServiceRequests.AsNoTracking().SingleAsync(x => x.Id == request.Id);
        Assert.Equal(stored.Review.FinalCategoryId, persisted.CategoryId);
        Assert.Equal(request.ResidentId, persisted.ResidentId);
        Assert.Equal(ServiceRequestStatus.ASSIGNED, persisted.Status);
        Assert.Null(await requests.ServiceRequests.Where(x => x.Id == other.Id).Select(x => x.CategoryId).SingleAsync());
        var recorded = new ReviewManagerServiceRequestClassification(original.Id, stored.Review.FinalCategoryId!.Value);
        await Reject(HttpMethod.Put, review, recorded, RequestState, (Manager.Client, HttpStatusCode.OK));
        Assert.Equal(calls, fixture.Gemini.Calls.Count);
        Assert.Equal(business, await DownstreamState());
        Assert.Single(fixture.Realtime.Requests, x => x == request.Id);
    }

    [Fact, Trait("Feature", "FE-11")]
    public async Task Recommendation_generation_and_review_require_manager_and_keep_advice_separate_from_business_state()
    {
        var request = await SeedRequest("Bơm B7");
        var other = await SeedRequest("Bơm B7 khác");
        var cancelled = await SeedRequest("Đã hủy", cancelled: true);
        var secondManager = await fixture.Login("MANAGER");
        var path = RequestRoot + request.Id + "/recommendation";
        var otherPath = RequestRoot + other.Id + "/recommendation";
        await Reject(HttpMethod.Post, path, null, RequestState, fixture.ManagerDenials.ToArray());
        await Reject(HttpMethod.Post, RequestRoot + cancelled.Id + "/recommendation", null, RequestState, (secondManager.Client, HttpStatusCode.Conflict));
        var business = await DownstreamState();
        var providerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Gemini.Override = async (call, ct) =>
        {
            providerEntered.TrySetResult();
            await releaseProvider.Task.WaitAsync(ct);
            return AiAuthorizationFixture.GeminiTransport.Reply(call);
        };
        ManagerServiceRequestRecommendationView analyzed;
        var first = Call<ManagerServiceRequestRecommendationView>(Manager, HttpMethod.Post, path);
        try
        {
            await providerEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Reject(HttpMethod.Post, path, null, RequestState, (Manager.Client, HttpStatusCode.Conflict));
        }
        finally
        {
            releaseProvider.TrySetResult();
            fixture.Gemini.Override = null;
        }
        analyzed = await first;
        Assert.Equal("URGENT", analyzed.Suggestion!.Proposed!.PriorityCode);
        Assert.Equal("NORMAL", analyzed.CurrentPriority);
        await Reject(HttpMethod.Post, path, null, RequestState, (Manager.Client, HttpStatusCode.OK));
        Assert.Equal(business, await DownstreamState());
        var beforeReview = await Call<StaffServiceRequestDetailResponse>(Staff, HttpMethod.Get, $"staff/service-requests/{request.RequestNumber}");
        Assert.Null(beforeReview.ApprovedRecommendation);
        Assert.Equal("NORMAL", beforeReview.PriorityCode);

        fixture.Gemini.Override = (call, _) => Task.FromResult(AiAuthorizationFixture.GeminiTransport.Reply(call, """{"priorityCode":"URGENT"}"""));
        ManagerServiceRequestRecommendationView failed;
        try { failed = await Call<ManagerServiceRequestRecommendationView>(secondManager, HttpMethod.Post, otherPath); }
        finally { fixture.Gemini.Override = null; }
        Assert.Equal("FAILED", failed.Suggestion!.Status);
        Assert.Equal("NORMAL", failed.CurrentPriority);
        Assert.Null(failed.Suggestion.Approved);
        var otherAdvice = (await Call<ManagerServiceRequestRecommendationView>(secondManager, HttpMethod.Post, otherPath)).Suggestion!;
        Assert.NotEqual(failed.Suggestion.Id, otherAdvice.Id);
        var proposal = analyzed.Suggestion.Proposed;
        var accept = new ReviewManagerServiceRequestRecommendation(analyzed.Suggestion.Id, proposal.PriorityCode, proposal.MaintenanceRequired, proposal.Action, proposal.Resources);
        var correct = new { recommendationId = analyzed.Suggestion.Id, priorityCode = "HIGH", maintenanceRequired = true,
            action = "Kiểm tra áp suất", resources = "Kỹ thuật", reviewNote = "Đã xác minh", reviewedBy = Admin.Id };
        var review = path + "/review";
        await Reject(HttpMethod.Put, review, correct, RequestState, fixture.ManagerDenials.ToArray());
        await Reject(HttpMethod.Put, review, accept with { RecommendationId = otherAdvice.Id }, RequestState, (Manager.Client, HttpStatusCode.Conflict));
        await Reject(HttpMethod.Put, otherPath + "/review", accept with { RecommendationId = failed.Suggestion.Id }, RequestState, (Manager.Client, HttpStatusCode.Conflict));
        await Reject(HttpMethod.Put, review, accept with { PriorityCode = "BAD" }, RequestState, (Manager.Client, HttpStatusCode.BadRequest));
        await Reject(HttpMethod.Put, review, accept with { PriorityCode = "HIGH" }, RequestState, (Manager.Client, HttpStatusCode.BadRequest));
        var calls = fixture.Gemini.Calls.Count;
        var race = await Task.WhenAll(Send(Manager.Client, HttpMethod.Put, review, correct),
            Send(Manager.Client, HttpMethod.Put, review, accept));
        using var left = race[0];
        using var right = race[1];
        await Expect(race.OrderBy(x => (int)x.StatusCode).First(), HttpStatusCode.OK);
        await Expect(race.OrderBy(x => (int)x.StatusCode).Last(), HttpStatusCode.Conflict);
        using var scope = Database.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AiRecommendationDbContext>();
        var stored = await db.AiRequestRecommendations.AsNoTracking().Include(x => x.Review).SingleAsync(x => x.Id == analyzed.Suggestion.Id);
        Assert.Equal(Manager.Id, stored.Review!.ReviewedBy);
        Assert.Equal("URGENT", stored.SuggestedPriorityCode);
        var reviewed = await Call<StaffServiceRequestDetailResponse>(Staff, HttpMethod.Get, $"staff/service-requests/{request.RequestNumber}");
        Assert.Equal(stored.Review.FinalPriorityCode, reviewed.PriorityCode);
        Assert.Equal(stored.Review.FinalAction, reviewed.ApprovedRecommendation!.Action);
        Assert.Equal("ASSIGNED", reviewed.Status);
        var replay = new ReviewManagerServiceRequestRecommendation(stored.Id, stored.Review.FinalPriorityCode!,
            stored.Review.FinalMaintenanceRequired, stored.Review.FinalAction, stored.Review.FinalResourcePlan);
        await Reject(HttpMethod.Put, review, replay, RequestState, (Manager.Client, HttpStatusCode.OK));
        Assert.Equal(calls, fixture.Gemini.Calls.Count);
        Assert.Equal(business, await DownstreamState());
        Assert.Single(fixture.Realtime.Requests, x => x == request.Id);
    }

    private async Task<ServiceRequest> SeedRequest(string title, bool cancelled = false, string? description = null)
    {
        var resident = await Database.SeedResidentAsync(PiiEmail());
        using var scope = Database.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceRequestsDbContext>();
        var now = Database.Factory.Clock.GetUtcNow();
        var row = new ServiceRequest("B7-" + Guid.NewGuid().ToString("N")[..12], resident.ResidentId, resident.ResidencyId!.Value,
            title, description ?? "Máy bơm chạy yếu tại căn hộ B-409.", now, apartmentUnitId: resident.ApartmentId, finalPriorityCode: "NORMAL");
        if (cancelled) row.Cancel(now);
        else
        {
            row.MarkUnderReview(now);
            row.MarkAssigned(now);
            db.ServiceRequestAssignments.Add(new(row.Id, Staff.Id, Manager.Id, now));
        }
        db.ServiceRequests.Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    private async Task<string> RequestState()
    {
        using var scope = Database.Factory.Services.CreateScope();
        var s = scope.ServiceProvider;
        var requests = s.GetRequiredService<ServiceRequestsDbContext>();
        var classification = s.GetRequiredService<AiClassificationDbContext>();
        var recommendation = s.GetRequiredService<AiRecommendationDbContext>();
        return JsonSerializer.Serialize(new
        {
            Requests = await Rows(requests.ServiceRequests),
            Classification = await Rows(classification.AiRequestClassifications),
            ClassificationReviews = await Rows(classification.AiClassificationReviews),
            Recommendations = await Rows(recommendation.AiRequestRecommendations),
            RecommendationReviews = await Rows(recommendation.AiRecommendationReviews),
            Effects = await DownstreamState()
        });
    }

    private async Task<string> DownstreamState()
    {
        using var scope = Database.Factory.Services.CreateScope();
        var s = scope.ServiceProvider;
        var requests = s.GetRequiredService<ServiceRequestsDbContext>();
        var maintenance = s.GetRequiredService<MaintenanceDbContext>();
        var assets = s.GetRequiredService<PropertyAssetsDbContext>();
        return JsonSerializer.Serialize(new
        {
            Assignments = await Rows(requests.ServiceRequestAssignments),
            RequestActivities = await Rows(requests.ServiceRequestActivities),
            RequestOutbox = await Rows(requests.ResidentEvents),
            Maintenance = await Rows(maintenance.MaintenanceTasks),
            Schedules = await Rows(maintenance.MaintenanceSchedules),
            MaintenanceOutbox = await Rows(maintenance.ResidentEvents),
            Equipment = await Rows(assets.Equipment),
            Facilities = await Rows(assets.Facilities),
            AssetOutbox = await Rows(assets.ResidentEvents),
            Notices = await Rows(s.GetRequiredService<CommunicationDbContext>().Notifications),
            Invoices = await Rows(s.GetRequiredService<BillingDbContext>().Invoices),
            Payments = await Rows(s.GetRequiredService<PaymentsDbContext>().Payments)
        });
    }
}
