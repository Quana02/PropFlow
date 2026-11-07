using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace PropFlow.Modules.Communication.Presentation;

// Only invalidates the list. Content and audience checks remain in the HTTP use cases.
[Authorize(Policy = "announcements.access")]
public sealed class AnnouncementHub : Hub { }
