using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

public sealed partial class ServiceRequestAuthorizationHttpTests
{
    [Fact]
    public async Task Manager_cost_review_checks_effective_permission_and_commits_one_approval_under_concurrency()
    {
        var id = await Start();
        var proposal = await Call(staffB, id, "additional-cost", new ProposeAdditionalCostRequest(30_000, "Vật tư đã kiểm tra"));
        var input = new ApproveAdditionalCostProposalRequest(proposal.GetProperty("proposalId").GetGuid());
        await Denied("MANAGER", id, "additional-cost/approve", input);
        await Reject(manager, id, "additional-cost/approve", HttpStatusCode.Conflict, input with { ProposalId = Guid.NewGuid() });
        await Reject(manager, Guid.NewGuid(), "additional-cost/approve", HttpStatusCode.NotFound, input);
        using (var scope = host.Services.CreateScope())
        {
            var access = scope.ServiceProvider.GetRequiredService<AdministrationDbContext>();
            var permission = await access.RolePermissions.Include(row => row.Role).Include(row => row.Permission)
                .SingleAsync(row => row.Role.Code == "MANAGER" && row.Permission.Code == "MANAGE_OPERATIONS");
            access.RolePermissions.Remove(permission);
            await access.SaveChangesAsync();
            try { await Reject(manager, id, "additional-cost/approve", HttpStatusCode.Unauthorized, input); }
            finally { access.RolePermissions.Add(permission); await access.SaveChangesAsync(); }
        }
        var current = await Login("MANAGER");
        var manipulated = new { input.ProposalId, ManagerUserId = staffA.Id, ResidentId = homeB.ResidentId, Status = "ACCEPTED" };
        var approvals = await Task.WhenAll(Send(current.Client, "manager", id, "additional-cost/approve", manipulated),
            Send(current.Client, "manager", id, "additional-cost/approve", manipulated));
        try { Assert.All(approvals, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode)); }
        finally { foreach (var response in approvals) response.Dispose(); }
        var state = await Read(id);
        Assert.Equal("PENDING", state.AdditionalCostConsentStatus);
        Assert.Equal(current.Id, Assert.Single(state.Activities, row => row.ActivityType == ServiceActivityType.ADDITIONAL_COST_MANAGER_APPROVED).PerformedBy);
        using var finalScope = host.Services.CreateScope();
        var db = finalScope.ServiceProvider.GetRequiredService<ServiceRequestsDbContext>();
        Assert.Single(await db.ResidentEvents.Where(row => row.SourceId == id && row.Name == "ServiceRequestAdditionalCostProposed").ToArrayAsync());
        var after = await Snapshot();
        await Call(current, id, "additional-cost/approve", input);
        Assert.Equal(after, await Snapshot());
        await AssertNoPaymentOrInvoice();
    }
}
