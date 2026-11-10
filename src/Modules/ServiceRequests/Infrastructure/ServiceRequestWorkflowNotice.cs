using System.Text.Json;
using PropFlow.Shared.Messaging;

namespace PropFlow.Modules.ServiceRequests.Infrastructure;

public sealed record ServiceRequestWorkflowNotice(Guid SenderUserId, Guid? RecipientUserId, string RequestNumber)
{
    public const string Submitted = "ServiceRequestSubmitted";
    public const string Assigned = "ServiceRequestAssigned";

    public OutboxEvent ToEvent(string name, Guid requestId, DateTimeOffset now) =>
        new(name, requestId, JsonSerializer.Serialize(this), now);
}
