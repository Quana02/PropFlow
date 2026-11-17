using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Administration.Domain.UserRoleAssignments;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Domain.Residents;
using PropFlow.Modules.Residents.Domain.ResidentApartments;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTasks;
using PropFlow.Modules.Maintenance.Domain.MaintenanceAssignments;
using PropFlow.Modules.Maintenance.Domain.MaintenanceResults;
using PropFlow.Modules.Complaints.Domain.Complaints;
using PropFlow.Modules.Complaints.Domain.ComplaintFollowups;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestAssignments;

var owner = Environment.GetEnvironmentVariable("PROPFLOW_FIXTURE_OWNER") ?? "";
if (args.Length != 2 || !System.Text.RegularExpressions.Regex.IsMatch(args[1], "\\Apropflow_phase11_[a-f0-9]{32}\\z"))
    throw new InvalidOperationException("Cần tên database fixture của lượt chạy.");
var name = args[1];
TestDatabaseSafety.RequireFixtureName(name, owner);

if (args is ["migrate" or "status" or "verify", _])
{
    try
    {
        var target = Environment.GetEnvironmentVariable("PROPFLOW_MIGRATION_DATABASE")
            ?? throw new InvalidOperationException("Thiếu PROPFLOW_MIGRATION_DATABASE.");
        await TestDatabaseSafety.VerifyFixtureAsync(target, name, owner);
        if (args[0] != "verify")
            await new DatabaseMigration(target).RunAsync(apply: args[0] == "migrate");
    }
    catch (Exception error)
    {
        Console.Error.WriteLine($"Migration dừng ({error.GetType().Name}, SQLSTATE={(error as PostgresException)?.SqlState ?? "-"})");
        return 1;
    }
    return 0;
}
if (args is not [("create" or "create-empty" or "drop"), _])
    throw new ArgumentException("Dùng create, create-empty, drop, status hoặc migrate.");

var source = TestDatabaseSafety.Dedicated();
source.Database = "postgres";
source.Pooling = false;
await using (var admin = new NpgsqlConnection(source.ConnectionString))
{
    await admin.OpenAsync();
    if (args[0] == "drop")
        await TestDatabaseSafety.DropAsync(admin, name, owner);
    else
        await TestDatabaseSafety.CreateAsync(admin, name, owner);
}
if (args[0] != "create") return 0;
source.Database = name;
await TestDatabaseSafety.VerifyFixtureAsync(source.ConnectionString, name, owner);
var migrator = new DatabaseMigration(source.ConnectionString);
await migrator.RunAsync(apply: true);
DbContext Open(string module, string typeName, string schema) => migrator.Open(module, typeName, schema);
await using var auth = (AuthenticationDbContext)Open("Authentication", "AuthenticationDbContext", "auth");
await using var residents = (ResidentsDbContext)Open("Residents", "ResidentsDbContext", "residents");
await using var adminDb = (AdministrationDbContext)Open("Administration", "AdministrationDbContext", "administration");
await using var assets = (PropertyAssetsDbContext)Open("PropertyAssets", "PropertyAssetsDbContext", "property_assets");
var now = DateTimeOffset.UtcNow;
assets.Buildings.Add(new Building("Chung cư kiểm thử Phase 1.1", "Địa chỉ tổng hợp", now, numberOfFloors: 10));
await assets.SaveChangesAsync();
var actors = new Dictionary<string, Guid>();
var roles = await adminDb.Roles.ToDictionaryAsync(role => role.Code, role => role.Id);
var noticeScenario = Environment.GetEnvironmentVariable("PROPFLOW_NOTIFICATION_GROUP");
var fixtureRoles = new List<string> { "ADMIN", "MANAGER", "STAFF", "STAFF2", "ACCOUNTANT", "RESIDENT", "RESIDENT2" };
if (noticeScenario == "Communication") fixtureRoles.Add("RESIDENT3");
foreach (var role in fixtureRoles)
{
    var user = new UserAccount("phase11_" + role.ToLowerInvariant(), "pending", "Kiểm thử " + role, now,
        $"phase11-{role.ToLowerInvariant()}@example.invalid", status: AccountStatus.ACTIVE, emailVerified: true);
    user.ChangePasswordHash(new PasswordHasher<UserAccount>().HashPassword(user, Environment.GetEnvironmentVariable("PHASE11_PASSWORD")!), null, now);
    actors.Add(role, user.Id);
    auth.UserAccounts.Add(user);
    await auth.SaveChangesAsync();
    adminDb.UserRoleAssignments.Add(new UserRoleAssignment(user.Id, roles[role.TrimEnd('2', '3')], now));
    if (role == "RESIDENT")
        residents.Residents.Add(new Resident("PHASE11-RES", "Cư dân kiểm thử Phase 1.1", now, user.Id,
            "+84900110011", "contact-phase11@example.invalid"));
    if (role == "RESIDENT2")
        residents.Residents.Add(new Resident("PHASE11-RES2", "Cư dân kiểm thử B", now, user.Id,
            "+84900110012", "contact-phase11-b@example.invalid"));
}
await adminDb.SaveChangesAsync();
await residents.SaveChangesAsync();

if (int.TryParse(Environment.GetEnvironmentVariable("PROPFLOW_AUTHZ_LOAD_USERS"), out var users))
{
    ArgumentOutOfRangeException.ThrowIfGreaterThan(users, 200);
    var hasher = new PasswordHasher<UserAccount>();
    var fixturePassword = Environment.GetEnvironmentVariable("PHASE11_PASSWORD")!;
    for (var index = 1; index <= users; index++)
    {
        var actor = new UserAccount($"phase11_load{index:D3}", "pending", $"Tải thử {index}", now,
            $"phase11-load{index:D3}@example.invalid", status: AccountStatus.ACTIVE, emailVerified: true);
        actor.ChangePasswordHash(hasher.HashPassword(actor, fixturePassword), null, now);
        auth.UserAccounts.Add(actor);
        adminDb.UserRoleAssignments.Add(new UserRoleAssignment(actor.Id, roles["STAFF"], now));
    }
    await auth.SaveChangesAsync();
    await adminDb.SaveChangesAsync();
}

var residentId = await residents.Residents.Where(x => x.ResidentCode == "PHASE11-RES").Select(x => x.Id).SingleAsync();
var resident2 = await residents.Residents.Where(x => x.ResidentCode == "PHASE11-RES2").Select(x => x.Id).SingleAsync();
await using var requests = (PropFlow.Modules.ServiceRequests.Infrastructure.Persistence.ServiceRequestsDbContext)Open("ServiceRequests","ServiceRequestsDbContext","service_requests");
await using var apartmentDb = (PropFlow.Modules.Apartments.Infrastructure.Persistence.ApartmentsDbContext)Open("Apartments", "ApartmentsDbContext", "apartments");
var unitType = new PropFlow.Modules.Apartments.Domain.ApartmentUnits.ApartmentUnitType("Loại thử nghiệm", now);
var unit = new PropFlow.Modules.Apartments.Domain.ApartmentUnits.ApartmentUnit("PHASE11-A1", 1, now, unitType.Id, 70, 2);
var unit2 = new PropFlow.Modules.Apartments.Domain.ApartmentUnits.ApartmentUnit("PHASE11-B2", 2, now, unitType.Id, 60, 2);
apartmentDb.AddRange(unitType, unit, unit2);
await apartmentDb.SaveChangesAsync();
var residency = new PropFlow.Modules.Residents.Domain.ResidentApartments.ResidentApartment(residentId, unit.Id,
    PropFlow.Modules.Residents.Domain.ResidentApartments.HouseholdRole.HOUSEHOLD_HEAD,
    PropFlow.Modules.Residents.Domain.ResidentApartments.ResidencyType.TENANT, DateOnly.FromDateTime(now.UtcDateTime).AddMonths(-3), now);
var residency2 = new ResidentApartment(resident2, unit2.Id, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.TENANT,
    DateOnly.FromDateTime(now.UtcDateTime).AddMonths(-3), now);
residents.ResidentApartments.AddRange(residency, residency2);
await residents.SaveChangesAsync();
await using var communication = (PropFlow.Modules.Communication.Infrastructure.Persistence.CommunicationDbContext)Open("Communication", "CommunicationDbContext", "communication");
var conversation = new PropFlow.Modules.Communication.Domain.SupportChat.SupportConversation(actors["RESIDENT"], residentId, "Cư dân kiểm thử", now);
conversation.Assign(actors["MANAGER"], "Manager kiểm thử", now);
communication.Add(conversation);
communication.Add(conversation.Message(actors["MANAGER"], "Manager kiểm thử", "MANAGER", Guid.NewGuid(), "Tin nhắn kiểm thử", now));
var conversation2 = new PropFlow.Modules.Communication.Domain.SupportChat.SupportConversation(actors["RESIDENT2"], resident2, "Cư dân kiểm thử B", now);
communication.Add(conversation2);
await communication.SaveChangesAsync();
await using var billing = (PropFlow.Modules.Billing.Infrastructure.Persistence.BillingDbContext)Open("Billing", "BillingDbContext", "billing");
var period = new DateOnly(now.Year, now.Month, 1).AddMonths(-1);
var invoice = new PropFlow.Modules.Billing.Domain.Invoices.Invoice("PHASE11-INVOICE", unit.Id, period, period.AddMonths(1).AddDays(-1), actors["ACCOUNTANT"], now);
invoice.AddItem("SERVICE", "Phí dịch vụ kiểm thử", "AREA", 70, 10000, actors["ACCOUNTANT"], now);
invoice.Issue(actors["ACCOUNTANT"], period, now, period.AddDays(9));
billing.Invoices.Add(invoice);
billing.InvoiceRecipients.Add(new(invoice.Id, actors["RESIDENT"], "Cư dân kiểm thử", "contact-phase11@example.invalid", unit.UnitNumber, 70, residentId));
var invoice2 = new PropFlow.Modules.Billing.Domain.Invoices.Invoice("PHASE11-INVOICE2", unit2.Id, period, period.AddMonths(1).AddDays(-1), actors["ACCOUNTANT"], now);
invoice2.AddItem("SERVICE", "Phí dịch vụ kiểm thử B", "AREA", 60, 10000, actors["ACCOUNTANT"], now);
invoice2.Issue(actors["ACCOUNTANT"], period, now, period.AddDays(9));
billing.Invoices.Add(invoice2);
billing.InvoiceRecipients.Add(new(invoice2.Id, actors["RESIDENT2"], "Cư dân kiểm thử B", "contact-phase11-b@example.invalid", unit2.UnitNumber, 60, resident2));
await billing.SaveChangesAsync();
var billingNoticeScenario = noticeScenario == "Finance";
var request = new PropFlow.Modules.ServiceRequests.Domain.ServiceRequests.ServiceRequest(
    "PHASE11-EVIDENCE", residentId, residency.Id, "Ảnh kiểm thử tổng hợp", "Yêu cầu giả lập cho xác minh ảnh.", now,
    apartmentUnitId: billingNoticeScenario ? unit.Id : null, submittedByUserId: billingNoticeScenario ? actors["RESIDENT"] : null);
request.MarkUnderReview(now);
if (billingNoticeScenario) request.SetBaseServiceCost(50000, now);
request.MarkAssigned(now);
request.MarkInProgress(now);
var assignment = new PropFlow.Modules.ServiceRequests.Domain.ServiceRequestAssignments.ServiceRequestAssignment(request.Id, actors["STAFF"], actors["MANAGER"], now);
assignment.Start(now);
var activity = new PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities.ServiceRequestActivity(request.Id, PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities.ServiceActivityType.WORK_RESULT_ADDED, now, assignment.Id, workResult: "Đã tạo ảnh tổng hợp.");
var image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aG1sAAAAASUVORK5CYII=");
var key = $"phase11-{name}/synthetic.png";
var imagePath = Path.Combine("src","PropFlow.Api","App_Data","service-request-evidence",key);
Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
await File.WriteAllBytesAsync(imagePath, image);
var evidence = new PropFlow.Modules.ServiceRequests.Domain.ServiceRequestEvidence.ServiceRequestEvidenceFile(request.Id, activity.Id, "synthetic.png", "image/png", image.Length, key, actors["STAFF"], now);
requests.AddRange(request, assignment, activity, evidence);
var request2 = new ServiceRequest("PHASE11-REQ-B", resident2, residency2.Id, "Yêu cầu kiểm thử B", "Mô tả dữ liệu giả.", now);
request2.MarkUnderReview(now);
request2.MarkAssigned(now);
requests.AddRange(request2, new ServiceRequestAssignment(request2.Id, actors["STAFF2"], actors["MANAGER"], now));
await requests.SaveChangesAsync();

await using var complaints = Open("Complaints", "ComplaintsDbContext", "complaints");
var complaint = new Complaint("PHASE11-C1", residentId, residency.Id, "Phản ánh kiểm thử A", "Mô tả kiểm thử", now, unit.Id);
var complaint2 = new Complaint("PHASE11-C2", resident2, residency2.Id, "Phản ánh kiểm thử B", "Mô tả kiểm thử", now, unit2.Id);
complaints.AddRange(complaint, complaint2,
    new ComplaintFollowup(complaint.Id, actors["STAFF"], actors["MANAGER"], "Xử lý A", now),
    new ComplaintFollowup(complaint2.Id, actors["STAFF2"], actors["MANAGER"], "Xử lý B", now));
await complaints.SaveChangesAsync();

await using var maintenance = Open("Maintenance", "MaintenanceDbContext", "maintenance");
var task = new MaintenanceTask("MT-AUDIT-A", "Công việc kiểm thử A", actors["MANAGER"], now);
var task2 = new MaintenanceTask("MT-AUDIT-B", "Công việc kiểm thử B", actors["MANAGER"], now);
task.MarkAssigned(now);
task2.MarkAssigned(now);
maintenance.AddRange(task, task2,
    new MaintenanceAssignment(task.Id, actors["STAFF"], actors["MANAGER"], now),
    new MaintenanceAssignment(task2.Id, actors["STAFF2"], actors["MANAGER"], now));
var history = new MaintenanceTask("MT-AUDIT-HISTORY", "Kiểm tra lịch sử hai đợt", actors["MANAGER"], now.AddMinutes(-60));
history.MarkAssigned(now.AddMinutes(-50));
var first = new MaintenanceAssignment(history.Id, actors["STAFF"], actors["MANAGER"], now.AddMinutes(-50));
var gap = new MaintenanceAssignment(history.Id, actors["STAFF2"], actors["MANAGER"], now.AddMinutes(-40));
var second = new MaintenanceAssignment(history.Id, actors["STAFF"], actors["MANAGER"], now.AddMinutes(-30));
var active = new MaintenanceAssignment(history.Id, actors["STAFF2"], actors["MANAGER"], now.AddMinutes(-20));
first.Reassign(now.AddMinutes(-40));
gap.Reassign(now.AddMinutes(-30));
second.Reassign(now.AddMinutes(-20));
maintenance.AddRange(history, first, gap, second, active,
    new MaintenanceResult(history.Id, 1, actors["STAFF"], "Kết quả được phép đợt một", now.AddMinutes(-45)),
    new MaintenanceResult(history.Id, 2, actors["STAFF2"], "Nội dung khoảng trống", now.AddMinutes(-35)),
    new MaintenanceResult(history.Id, 3, actors["STAFF"], "Kết quả được phép đợt hai", now.AddMinutes(-25)),
    new MaintenanceResult(history.Id, 4, actors["STAFF2"], "Nội dung sau lần gỡ cuối", now.AddMinutes(-15)));
await maintenance.SaveChangesAsync();

await File.WriteAllTextAsync("artifacts/authz/browser-resources.json", System.Text.Json.JsonSerializer.Serialize(new {
    requestId = request.Id, otherRequestId = request2.Id, evidenceId = evidence.Id,
    invoiceId = invoice.Id, otherInvoiceId = invoice2.Id, conversationId = conversation.Id, otherConversationId = conversation2.Id,
    apartmentId = unit.Id, residentId,
    complaintId = complaint.Id, otherComplaintId = complaint2.Id,
    taskId = task.Id, otherTaskId = task2.Id
}));
return 0;

