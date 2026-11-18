using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Api.Composition;
using PropFlow.Modules.Billing.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Contracts;

namespace PropFlow.IntegrationTests;

public sealed partial class ServiceRequestAuthorizationHttpTests
{
    [Fact]
    public async Task Invoice_ready_notice_preserves_the_issued_recipient_snapshot_when_resident_link_changes()
    {
        var id = await Start();
        var proposal = await Call(staffB, id, "additional-cost", new ProposeAdditionalCostRequest(30_000, "Vật tư đã khảo sát"));
        var proposalId = proposal.GetProperty("proposalId").GetGuid();
        await Call(manager, id, "additional-cost/approve", new ApproveAdditionalCostProposalRequest(proposalId));
        await Call(residentA, id, "additional-cost/response", new RespondAdditionalCostRequest(proposalId, true));
        await Call(staffB, id, "complete", Work with { AdditionalCost = 30_000, AdditionalCostNote = "Vật tư đã khảo sát" });
        await Call(staffB, id, "evidence");
        await Call(manager, id, "resolution/approve", Resolution with { ApprovedAdditionalCost = 30_000 });

        var replacement = await Login("RESIDENT");
        using var scope = host.Services.CreateScope();
        var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var recipient = await billing.InvoiceRecipients.SingleAsync();
        Assert.Equal(residentA.Id, recipient.UserId);
        Assert.Equal(residentA.Id, (await Read(id)).SubmittedByUserId);
        var residents = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        var resident = await residents.Residents.SingleAsync(row => row.Id == homeA.ResidentId);
        // Synthetic historical/link drift: an issued invoice must keep its addressed account.
        resident.LinkUserAccount(replacement.Id, manager.Id, Now);
        await residents.SaveChangesAsync();

        var dispatcher = scope.ServiceProvider.GetRequiredService<ServiceRequestCostNoticeDispatcher>();
        await dispatcher.DispatchAsync(default);
        await dispatcher.DispatchAsync(default);
        var notices = await scope.ServiceProvider.GetRequiredService<CommunicationDbContext>().Notifications
            .Where(row => row.SourceId == id && row.Title == "Hóa đơn dịch vụ đã sẵn sàng").ToArrayAsync();
        Assert.Equal(recipient.UserId, Assert.Single(notices).RecipientUserId);
        Assert.DoesNotContain(notices, row => row.RecipientUserId == replacement.Id);
    }
}
