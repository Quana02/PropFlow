using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.Communication.Application;
using PropFlow.Modules.Communication.Infrastructure.Persistence;
using PropFlow.Modules.Complaints.Application;
using PropFlow.Modules.Complaints.Domain.ComplaintActivities;
using PropFlow.Modules.Complaints.Domain.Complaints;
using PropFlow.Modules.Complaints.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Domain.Equipment;
using PropFlow.Modules.PropertyAssets.Domain.Facilities;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

public sealed partial class PropertyOperationsAuthorizationHttpTests
{
    [Fact]
    [Trait("Feature", "FE-06")]
    public async Task Complaint_lifecycle_requires_owner_manager_and_assigned_staff_and_delivers_only_to_reporter()
    {
        using var scope = Database.Factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var complaints = services.GetRequiredService<ComplaintsDbContext>();
        var communication = services.GetRequiredService<CommunicationDbContext>();
        await complaints.Database.MigrateAsync();
        await communication.Database.MigrateAsync();
        await services.GetRequiredService<ServiceRequestsDbContext>().Database.MigrateAsync();
        var assets = services.GetRequiredService<PropertyAssetsDbContext>();
        var facility = new Facility("B6-C-FAC", "Phòng kỹ thuật", Now);
        var otherFacility = new Facility("B6-C-OTHER", "Khu khác", Now);
        var equipment = new Equipment("B6-C-EQ", "Bơm cứu hỏa", Now, facilityId: facility.Id);
        assets.AddRange(facility, otherFacility, equipment);
        await assets.SaveChangesAsync();
        var ownerSeed = await Database.SeedResidentAsync(ContactEmail());
        var otherSeed = await Database.SeedResidentAsync(ContactEmail());
        var owner = await fixture.Login("RESIDENT", residentId: ownerSeed.ResidentId);
        var other = await fixture.Login("RESIDENT", residentId: otherSeed.ResidentId);
        var staff = fixture.Actor("STAFF");
        var otherStaff = await fixture.Login("STAFF");
        var unrelatedStaff = await fixture.Login("STAFF");
        var revokedResident = await fixture.Login("RESIDENT", revoked: true, residentId: (await Database.SeedResidentAsync(ContactEmail())).ResidentId);
        var revokedStaff = await fixture.Login("STAFF", revoked: true);
        var unlinked = fixture.Actor("RESIDENT");
        var anonymous = fixture.Client();
        var forbiddenBusinessRoles = new[] { fixture.Actor("ADMIN").Client, fixture.Actor("ACCOUNTANT").Client, Manager.Client };

        await using var ownerHub = AuthenticationTestSessions.ConnectWebSocket(Database.Factory, "resident-notifications", owner.Token);
        await using var otherHub = AuthenticationTestSessions.ConnectWebSocket(Database.Factory, "resident-notifications", other.Token);
        var deliveries = new ConcurrentQueue<FinanceNotificationUpdate>();
        var otherDeliveries = new ConcurrentQueue<FinanceNotificationUpdate>();
        var received = new TaskCompletionSource<FinanceNotificationUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = ownerHub.On<FinanceNotificationUpdate>("FinanceNotificationCreated", notice =>
        {
            deliveries.Enqueue(notice);
            received.TrySetResult(notice);
        });
        using var otherSubscription = otherHub.On<FinanceNotificationUpdate>("FinanceNotificationCreated", otherDeliveries.Enqueue);
        await ownerHub.StartAsync();
        await otherHub.StartAsync();

        const string root = "complaints";
        var input = new
        {
            subject = "Tiếng ồn B6", description = "Phản ánh từ căn hộ kiểm thử.",
            residentId = otherSeed.ResidentId, residentApartmentId = otherSeed.ResidencyId,
            apartmentUnitId = otherSeed.ApartmentId, status = "CLOSED", createdBy = other.Id
        };
        var submitDenials = forbiddenBusinessRoles.Append(staff.Client).Append(unlinked.Client)
            .Select(client => (client, HttpStatusCode.Forbidden)).Concat(
                new[] { (anonymous, HttpStatusCode.Unauthorized), (revokedResident.Client, HttpStatusCode.Unauthorized) }).ToArray();
        await Denied(HttpMethod.Post, root, input, Snapshot, submitDenials);
        var first = await Complaint(owner, HttpMethod.Post, root, input, HttpStatusCode.Created);
        var second = await Complaint(other, HttpMethod.Post, root, new SubmitComplaint("Phản ánh thứ hai", "Thuộc cư dân khác."), HttpStatusCode.Created);
        var route = $"{root}/{first.Id}";
        var otherRoute = $"{root}/{second.Id}";
        var submitted = await complaints.Complaints.AsNoTracking().SingleAsync(x => x.Id == first.Id);
        Assert.Equal(ownerSeed.ResidentId, submitted.ResidentId);
        Assert.Equal(ownerSeed.ResidencyId, submitted.ResidentApartmentId);
        Assert.Equal(ownerSeed.ApartmentId, submitted.ApartmentUnitId);
        Assert.Equal(ComplaintStatus.SUBMITTED, submitted.Status);
        await Check(other.Client, HttpMethod.Get, route, null, HttpStatusCode.NotFound);
        await Check(owner.Client, HttpMethod.Get, otherRoute, null, HttpStatusCode.NotFound);

        await Denied(HttpMethod.Post, route + "/review", null, Snapshot);
        await OneTransition(HttpMethod.Post, route + "/review", null, route + "/review", null, HttpStatusCode.OK);
        var review = Assert.Single(await complaints.ComplaintActivities.AsNoTracking().Where(x => x.ComplaintId == first.Id && x.ActivityType == ComplaintActivityType.MANAGER_REVIEWED).ToArrayAsync());
        Assert.Equal(Manager.Id, review.PerformedBy);
        await Check(Manager.Client, HttpMethod.Post, otherRoute + "/review", null, HttpStatusCode.OK);

        var context = new ComplaintContext(ownerSeed.ApartmentId, null, facility.Id, equipment.Id);
        await Denied(HttpMethod.Put, route + "/context", context, Snapshot);
        await Denied(HttpMethod.Put, route + "/context", context with { FacilityId = otherFacility.Id }, Snapshot, [(Manager.Client, HttpStatusCode.BadRequest)]);
        var contextualized = await Complaint(Manager, HttpMethod.Put, route + "/context", context);
        Assert.Equal(context, contextualized.Context);
        var associated = await complaints.Complaints.AsNoTracking().SingleAsync(x => x.Id == first.Id);
        Assert.Equal(equipment.Id, associated.EquipmentId);
        Assert.Equal(ownerSeed.ResidentId, associated.ResidentId);
        Assert.Equal(ownerSeed.ResidencyId, associated.ResidentApartmentId);

        var assignment = new AssignComplaintFollowup(staff.Id, "Kiểm tra B6", "Chi tiết nội bộ B6");
        await Denied(HttpMethod.Post, route + "/followups", assignment, Snapshot);
        await Denied(HttpMethod.Post, route + "/followups", assignment with { StaffUserId = fixture.Actor("ACCOUNTANT").Id },
            Snapshot, [(Manager.Client, HttpStatusCode.BadRequest)]);
        var assigned = await Complaint(Manager, HttpMethod.Post, route + "/followups", assignment);
        var followup = Assert.Single(assigned.Followups);
        var sameComplaintOther = await Complaint(Manager, HttpMethod.Post, route + "/followups", assignment with { StaffUserId = otherStaff.Id });
        var otherFollowup = Assert.Single(sameComplaintOther.Followups, x => x.StaffUserId == otherStaff.Id);
        var differentComplaint = await Complaint(Manager, HttpMethod.Post, otherRoute + "/followups", assignment);
        var crossed = Assert.Single(differentComplaint.Followups);
        Assert.Equal(staff.Id, followup.StaffUserId);
        var followupRoute = $"{route}/followups/{followup.Id}";
        var complete = new FollowupUpdate(true, "Kết quả nội bộ B6");
        var updateDenials = forbiddenBusinessRoles.Append(owner.Client).Select(client => (client, HttpStatusCode.Forbidden)).Concat(
            new[] { (anonymous, HttpStatusCode.Unauthorized), (revokedStaff.Client, HttpStatusCode.Unauthorized),
                (otherStaff.Client, HttpStatusCode.NotFound), (unrelatedStaff.Client, HttpStatusCode.NotFound) }).ToArray();
        await Denied(HttpMethod.Put, followupRoute, complete, Snapshot, updateDenials);
        await Denied(HttpMethod.Put, $"{route}/followups/{crossed.Id}", complete, Snapshot, [(staff.Client, HttpStatusCode.NotFound)]);
        await Denied(HttpMethod.Post, route + "/resolve", new OfficialComplaintResponse("Đã xử lý"), Snapshot, [(Manager.Client, HttpStatusCode.Conflict)]);
        await Denied(HttpMethod.Post, route + "/close", null, Snapshot, [(Manager.Client, HttpStatusCode.Conflict)]);
        await Check(staff.Client, HttpMethod.Put, followupRoute, new FollowupUpdate(false, null), HttpStatusCode.OK);
        var completed = await Complaint(staff, HttpMethod.Put, followupRoute, new
        {
            complete = true, result = complete.Result, staffUserId = otherStaff.Id, status = "CLOSED"
        });
        Assert.Equal("COMPLETED", Assert.Single(completed.Followups).Status);
        Assert.Equal(staff.Id, Assert.Single(completed.Followups).StaffUserId);
        await Denied(HttpMethod.Put, followupRoute, complete, Snapshot, [(staff.Client, HttpStatusCode.Conflict)]);
        Assert.Contains(await complaints.ComplaintActivities.AsNoTracking().Where(x => x.FollowupId == followup.Id).ToArrayAsync(),
            x => x.ActivityType == ComplaintActivityType.FOLLOWUP_UPDATED && x.PerformedBy == staff.Id && x.Detail == complete.Result);
        await Check(otherStaff.Client, HttpMethod.Put, $"{route}/followups/{otherFollowup.Id}", complete, HttpStatusCode.OK);

        var resolution = new { response = "Ban quản lý đã xử lý.", resolvedBy = staff.Id, status = "CLOSED" };
        await Denied(HttpMethod.Post, route + "/resolve", resolution, Snapshot);
        await OneTransition(HttpMethod.Post, route + "/resolve", resolution, route + "/resolve", resolution, HttpStatusCode.OK);
        await Denied(HttpMethod.Post, route + "/close", null, Snapshot);
        Assert.Empty(deliveries);
        await OneTransition(HttpMethod.Post, route + "/close", null, route + "/close", null, HttpStatusCode.OK);
        await Denied(HttpMethod.Post, route + "/close", null, Snapshot, [(Manager.Client, HttpStatusCode.Conflict)]);
        var stored = await complaints.Complaints.AsNoTracking().SingleAsync(x => x.Id == first.Id);
        Assert.Equal(ComplaintStatus.CLOSED, stored.Status);
        Assert.Equal(Manager.Id, stored.ClosedBy);
        Assert.Equal(ownerSeed.ResidentId, stored.ResidentId);
        Assert.NotNull(stored.ClosedAt);
        Assert.Equal(resolution.response, stored.OfficialResponse);
        var notification = Assert.Single(await communication.Notifications.AsNoTracking().Where(x => x.SourceId == first.Id).ToArrayAsync());
        Assert.Equal(owner.Id, notification.RecipientUserId);
        Assert.Equal(notification.Id, (await received.Task.WaitAsync(TimeSpan.FromSeconds(8))).Id);
        Assert.Single(deliveries);
        Assert.Empty(otherDeliveries);
        var residentView = await owner.Client.GetStringAsync("/api/v1/" + route);
        Assert.DoesNotContain("nội bộ", residentView);
        Assert.Empty((await owner.Client.GetFromJsonAsync<ComplaintDetail>("/api/v1/" + route))!.Followups);

        async Task<string> Snapshot() => JsonSerializer.Serialize(new
        {
            Complaints = await complaints.Complaints.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Followups = await complaints.ComplaintFollowups.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Activities = await complaints.ComplaintActivities.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Notifications = await communication.Notifications.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Deliveries = deliveries.ToArray(),
            OtherDeliveries = otherDeliveries.ToArray()
        });
    }

    private static async Task<ComplaintDetail> Complaint(Actor actor, HttpMethod method, string route, object? input, HttpStatusCode status = HttpStatusCode.OK)
    {
        using var response = await Send(actor.Client, method, route, input);
        await Expect(response, status);
        return (await response.Content.ReadFromJsonAsync<ComplaintDetail>())!;
    }
}
