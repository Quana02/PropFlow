using Microsoft.EntityFrameworkCore;
using Npgsql;
using PropFlow.Api.Composition;
using PropFlow.Modules.Complaints.Application;
using PropFlow.Modules.Complaints.Domain.Complaints;
using PropFlow.Modules.Complaints.Domain.ComplaintActivities;
using PropFlow.Modules.Complaints.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Domain.Announcements;
using PropFlow.Modules.Communication.Infrastructure;
using PropFlow.Modules.Communication.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Domain.Residents;
using PropFlow.Modules.Residents.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

public sealed class ComplaintCommunicationPostgresTests
{
    [Fact, Trait("Feature", "FE-06.9 / FE-13")]
    public async Task Closure_and_notification_commit_atomically_and_competing_updates_are_rejected()
    {
        var database = "propflow_complaint_test_" + Guid.NewGuid().ToString("N");
        var configuration = new NpgsqlConnectionStringBuilder(TestDatabaseConfiguration.GetConnectionString()) { Database = "postgres" };
        await using var admin = new NpgsqlConnection(configuration.ConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            configuration.Database = database;
            await using var connection = new NpgsqlConnection(configuration.ConnectionString);
            var complaintOptions = new DbContextOptionsBuilder<ComplaintsDbContext>().UseNpgsql(connection).Options;
            var communicationOptions = new DbContextOptionsBuilder<CommunicationDbContext>().UseNpgsql(connection).Options;
            var residentOptions = new DbContextOptionsBuilder<ResidentsDbContext>().UseNpgsql(connection).Options;
            await using var complaints = new ComplaintsDbContext(complaintOptions);
            await using var communication = new CommunicationDbContext(communicationOptions);
            await using var residents = new ResidentsDbContext(residentOptions);
            foreach (var db in new DbContext[] { complaints, communication, residents })
                await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript());

            var now = DateTimeOffset.UtcNow;
            var userId = Guid.NewGuid();
            var resident = new Resident("COMPLAINT-TEST", "Cư dân thử nghiệm", now, userId);
            residents.Residents.Add(resident); await residents.SaveChangesAsync();
            var complaint = new Complaint("COMPLAINT-ATOMIC", resident.Id, Guid.NewGuid(), "Phản ánh", "Nội dung", now);
            complaint.MarkUnderReview(now.AddSeconds(1)); complaint.AddOfficialResponse("Đã xử lý", now.AddSeconds(2)); complaint.Resolve(now.AddSeconds(2));
            complaints.Complaints.Add(complaint); await complaints.SaveChangesAsync();

            // A delivery-store failure after saving the source must roll back the source too.
            await using (var failedDb = new FailingCommunication(communicationOptions))
            {
                complaint.Close(Guid.NewGuid(), now.AddSeconds(3));
                var activity = new ComplaintActivity(complaint.Id, ComplaintActivityType.CLOSED, now.AddSeconds(3));
                complaints.ComplaintActivities.Add(activity);
                var failing = new ComplaintCommitter(complaints, failedDb, residents, new ComplaintClosureNotice(failedDb));
                await Assert.ThrowsAsync<InvalidOperationException>(() => failing.SaveAsync(new(activity.Id, complaint.Id, resident.Id, activity.CreatedAt), default));
            }
            complaints.ChangeTracker.Clear();
            complaint = await complaints.Complaints.SingleAsync();
            Assert.Equal(ComplaintStatus.RESOLVED, complaint.Status);
            Assert.Empty(await complaints.ComplaintActivities.ToArrayAsync());
            Assert.Empty(await communication.Notifications.ToArrayAsync());

            var stale = new ComplaintsDbContext(new DbContextOptionsBuilder<ComplaintsDbContext>().UseNpgsql(configuration.ConnectionString).Options);
            await using (stale)
            {
                var staleComplaint = await stale.Complaints.SingleAsync();
                complaint.Close(Guid.NewGuid(), now.AddSeconds(4));
                var activity = new ComplaintActivity(complaint.Id, ComplaintActivityType.CLOSED, now.AddSeconds(4));
                complaints.ComplaintActivities.Add(activity);
                var committer = new ComplaintCommitter(complaints, communication, residents, new ComplaintClosureNotice(communication));
                await committer.SaveAsync(new(activity.Id, complaint.Id, resident.Id, activity.CreatedAt), default);
                var notification = Assert.Single(await communication.Notifications.ToArrayAsync());
                Assert.Equal(userId, notification.RecipientUserId);
                Assert.Equal(activity.Id, notification.SourceEventId);
                Assert.DoesNotContain("Nội dung", notification.Message);
                staleComplaint.AddOfficialResponse("Stale response", now.AddSeconds(5));
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
                await new ComplaintClosureNotice(communication).AddAsync(activity.Id, complaint.Id, userId, activity.CreatedAt, default);
                await communication.SaveChangesAsync();
                Assert.Single(await communication.Notifications.ToArrayAsync());
            }

            // Execute audience queries and persistence on PostgreSQL, not only the memory provider.
            var announcement = new Announcement("Thông báo", "Nội dung chung", Guid.NewGuid(), now);
            announcement.AddAllResidentsAudience(now); announcement.Publish(Guid.NewGuid(), now);
            var store = new EfAnnouncementStore(communication);
            await store.SaveAsync(announcement, default);
            var audience = new PropFlow.Modules.Residents.Contracts.ActiveResidentResidence(resident.Id, Guid.NewGuid(), Guid.NewGuid());
            Assert.Single((await store.ListAsync(audience, false, 1, default)).Items);
            Assert.NotNull(await store.GetAsync(announcement.Id, audience, false, default));
            Assert.Single((await new EfComplaintStore(complaints).ListAsync(resident.Id, null, "phản", ComplaintStatus.CLOSED, 1, default)).Items);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class FailingCommunication(DbContextOptions<CommunicationDbContext> options) : CommunicationDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken ct = default) => throw new InvalidOperationException("Simulated communication write failure");
    }
}
