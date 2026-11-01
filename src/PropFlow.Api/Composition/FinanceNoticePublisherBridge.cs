using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.Communication.Application;
namespace PropFlow.Api.Composition;
public sealed class FinanceNoticePublisherBridge(IFinanceNotificationStore store) : IFinanceNoticePublisher
{
    public Task PublishAsync(FinanceNotice n, CancellationToken ct) => store.PublishAsync(new(
        n.EventId, n.UserId, n.Email, n.Title, n.Message, n.Html, n.SourceType, n.SourceId, n.ActionPath, n.AttachmentFileName, n.PreviousRecipientEventId), ct);
}
