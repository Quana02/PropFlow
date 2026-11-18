using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Communication.Application.Announcements;
using PropFlow.Modules.Maintenance.Application;
using PropFlow.Modules.Maintenance.Domain.MaintenanceAssignments;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTasks;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Application;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestAssignments;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

public sealed partial class AuthenticationFlowTests
{
    private sealed class RealtimeBusinessProof(Func<bool, string, Task> emit, IDisposable[] resources) : IDisposable
    {
        public Task Emit(bool after, string cause) => emit(after, cause);
        public void Dispose() { foreach (var resource in resources) resource.Dispose(); }
    }

    private async Task<RealtimeBusinessProof?> PrepareRealtimeBusiness(string hub, string role, HubConnection[] connections,
        AccountResponse target, AccountResponse control, string? foreignRole = null)
    {
        if (hub is not ("maintenance" or "service-requests" or "announcements")) return null;
        var queues = connections.Select(_ => Channel.CreateUnbounded<Guid>()).ToArray();
        var eventName = hub switch { "maintenance" => "MaintenanceTaskUpdated", "service-requests" => "ServiceRequestUpdated", _ => "AnnouncementPublished" };
        var subscriptions = connections.Select((connection, i) => connection.On<Guid>(eventName, id => queues[i].Writer.TryWrite(id))).ToArray();
        var manager = await SeedRealtimeAccount("MANAGER");
        var producer = Client();
        var actor = await SignIn(producer, manager.Username);
        producer.DefaultRequestHeaders.Authorization = new("Bearer", actor.AccessToken);
        await using var scope = database.Factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var taskId = Guid.Empty;
        var staffIds = new[] { target, control }.Where(account => account.Role == "STAFF").Select(account => account.Id).ToArray();
        var requestIds = new List<Guid>();
        if (hub == "maintenance")
        {
            var db = services.GetRequiredService<MaintenanceDbContext>();
            await db.Database.MigrateAsync();
            var task = new MaintenanceTask("PF04-" + Guid.NewGuid().ToString("N")[..20], "PF04 persisted task", actor.User.Id, DateTimeOffset.UtcNow);
            db.MaintenanceTasks.Add(task);
            taskId = task.Id;
            if (staffIds.Length > 0)
            {
                db.MaintenanceAssignments.AddRange(staffIds.Select(id => new MaintenanceAssignment(task.Id, id, actor.User.Id, DateTimeOffset.UtcNow)));
                task.MarkAssigned(DateTimeOffset.UtcNow);
            }
            await db.SaveChangesAsync();
            for (var i = 0; i < 4; i++)
                if ((i == 3 ? control.Role : target.Role) == "MANAGER") await connections[i].InvokeAsync("JoinTask", taskId);
                else await connections[i].InvokeAsync("JoinMaintenanceUpdates");
        }
        if (hub == "service-requests")
        {
            var db = services.GetRequiredService<ServiceRequestsDbContext>();
            var residents = services.GetRequiredService<ResidentsDbContext>();
            foreach (var recipient in new[] { target, control })
            {
                Guid residentId, residencyId, apartmentId;
                if (recipient.Role == "RESIDENT")
                {
                    residentId = await residents.Residents.Where(item => item.UserId == recipient.Id).Select(item => item.Id).SingleAsync();
                    var residence = await residents.ResidentApartments.Where(item => item.ResidentId == residentId)
                        .Select(item => new { item.Id, item.ApartmentUnitId }).FirstAsync();
                    residencyId = residence.Id; apartmentId = residence.ApartmentUnitId;
                }
                else
                {
                    var seed = await database.SeedResidentAsync(Guid.NewGuid().ToString("N") + "@example.invalid");
                    residentId = seed.ResidentId; residencyId = seed.ResidencyId!.Value; apartmentId = seed.ApartmentId;
                }
                var request = new ServiceRequest("PF04-" + Guid.NewGuid().ToString("N")[..20], residentId, residencyId,
                    "PF04 persisted request", "Synthetic authorized request", DateTimeOffset.UtcNow, apartmentUnitId: apartmentId,
                    submittedByUserId: recipient.Role == "RESIDENT" ? recipient.Id : null);
                db.ServiceRequests.Add(request);
                if (recipient.Role == "STAFF")
                {
                    request.MarkUnderReview(DateTimeOffset.UtcNow); request.MarkAssigned(DateTimeOffset.UtcNow);
                    db.ServiceRequestAssignments.Add(new ServiceRequestAssignment(request.Id, recipient.Id, actor.User.Id, DateTimeOffset.UtcNow));
                }
                requestIds.Add(request.Id);
            }
            await db.SaveChangesAsync();
        }
        return new RealtimeBusinessProof(async (after, cause) =>
        {
            await using var dispatchScope = database.Factory.Services.CreateAsyncScope();
            var current = dispatchScope.ServiceProvider;
            var emittedIds = new List<Guid>();
            if (hub == "announcements")
            {
                // A controlled fixed-role permission change also invalidates the producer's old JWT.
                var currentProducer = await SignIn(producer, manager.Username);
                producer.DefaultRequestHeaders.Authorization = new("Bearer", currentProducer.AccessToken);
                using var published = await producer.PostAsJsonAsync("api/v1/announcements",
                    new PublishAnnouncement("PF04 " + Guid.NewGuid(), "Synthetic publication after=" + after));
                Assert.Equal(HttpStatusCode.Created, published.StatusCode);
                emittedIds.Add(Guid.Empty); // The production contract sends no announcement content or private ID.
            }
            else if (hub == "maintenance")
            {
                var db = current.GetRequiredService<MaintenanceDbContext>();
                var task = await db.MaintenanceTasks.SingleAsync(item => item.Id == taskId);
                task.UpdateDetails("PF04 changed " + Guid.NewGuid(), DateTimeOffset.UtcNow);
                await db.SaveChangesAsync();
                await current.GetRequiredService<IMaintenanceNotifier>().NotifyTaskUpdatedAsync(taskId,
                    staffIds, default);
                emittedIds.Add(taskId);
            }
            else
            {
                var db = current.GetRequiredService<ServiceRequestsDbContext>();
                foreach (var id in requestIds)
                {
                    var request = await db.ServiceRequests.SingleAsync(item => item.Id == id);
                    request.UpdateServiceArea("COMMON", DateTimeOffset.UtcNow);
                    await db.SaveChangesAsync();
                    await current.GetRequiredService<IServiceRequestRealtimeNotifier>().NotifyChangedAsync(id, ct: default);
                    emittedIds.Add(id);
                }
            }
            var allowed = after ? (cause == "logout" ? new[] { 2, 3 } : new[] { 3 }) : new[] { 0, 1, 2, 3 };
            // Empty announcement invalidation and manager workspace invalidation are authorized broadcasts;
            // an unrelated but eligible manager/resident may receive them, unlike private message content.
            if (hub == "announcements" || hub == "service-requests" && (foreignRole ?? role) == "MANAGER") allowed = [.. allowed, 4];
            foreach (var i in allowed)
            {
                var recipientRole = i == 4 ? foreignRole ?? role : i == 3 ? control.Role : target.Role;
                var ids = hub == "service-requests" && recipientRole != "MANAGER" ? new[] { requestIds[i == 3 ? 1 : 0] } : emittedIds.ToArray();
                foreach (var id in ids)
                    Assert.Equal(id, await queues[i].Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
            }
            Assert.All(queues, queue => Assert.False(queue.Reader.TryRead(out _)));
        }, [.. subscriptions, producer]);
    }
}
