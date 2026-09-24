using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using PropFlow.Modules.AiClassification.Domain.Classifications;
using PropFlow.Modules.AiClassification.Infrastructure.Persistence;
using PropFlow.Modules.AiRecommendation.Domain.Recommendations;
using PropFlow.Modules.AiRecommendation.Infrastructure.Persistence;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Domain.ResidentVerifications;
using PropFlow.Modules.Authentication.Domain.Tokens;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Billing.Domain.FeeRateRules;
using PropFlow.Modules.Billing.Domain.FeeTypes;
using PropFlow.Modules.Billing.Domain.InvoiceItems;
using PropFlow.Modules.Billing.Domain.Invoices;
using PropFlow.Modules.Billing.Domain.InvoiceStatusHistories;
using PropFlow.Modules.Billing.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Domain.Announcements;
using PropFlow.Modules.Communication.Domain.Notifications;
using PropFlow.Modules.Communication.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Domain.Payments;
using PropFlow.Modules.Payments.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.PropertyAssets.Domain.Facilities;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Domain.ResidentApartments;
using PropFlow.Modules.Residents.Domain.Residents;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.Administration.Domain.AuditLogs;
using PropFlow.Modules.Administration.Domain.Permissions;
using PropFlow.Modules.Administration.Domain.Roles;
using PropFlow.Modules.Administration.Domain.SystemConfigurations;
using PropFlow.Modules.Administration.Domain.UserAccessHistories;

using PropFlow.Modules.Administration.Domain.UserRoleAssignments;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Complaints.Domain.ComplaintActivities;
using PropFlow.Modules.Complaints.Domain.ComplaintFollowups;
using PropFlow.Modules.Complaints.Domain.Complaints;
using PropFlow.Modules.Complaints.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestAssignments;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestCategories;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;
using PropFlow.Modules.Maintenance.Domain.MaintenanceAssignments;
using PropFlow.Modules.Maintenance.Domain.MaintenanceResults;
using PropFlow.Modules.Maintenance.Domain.MaintenanceSchedules;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTaskActivities;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTasks;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;
using EquipmentEntity = PropFlow.Modules.PropertyAssets.Domain.Equipment.Equipment;

namespace PropFlow.ArchitectureTests;

public class EfCoreMetadataTests
{
    [Fact]
    public void PropertyAssetsDbContext_ShouldHaveCorrectSchemaAndEntityMappings()
    {
        var options = new DbContextOptionsBuilder<PropertyAssetsDbContext>()
            .UseNpgsql("Host=localhost;Database=dummy;Username=dummy;Password=dummy")
            .Options;

        using var context = new PropertyAssetsDbContext(options);
        var model = context.Model;

        Assert.Equal("property_assets", model.GetDefaultSchema());

        // Building
        var buildingEntity = model.FindEntityType(typeof(Building));
        Assert.NotNull(buildingEntity);
        Assert.Equal("buildings", buildingEntity.GetTableName());
        Assert.Equal("property_assets", buildingEntity.GetSchema());
        Assert.NotNull(buildingEntity.FindPrimaryKey());
        Assert.Equal("id", buildingEntity.FindPrimaryKey()!.Properties.Single().GetColumnName());
        Assert.True(buildingEntity.FindProperty("Code")!.IsIndex());

        // Facility
        var facilityEntity = model.FindEntityType(typeof(Facility));
        Assert.NotNull(facilityEntity);
        Assert.Equal("facilities", facilityEntity.GetTableName());
        Assert.Equal("property_assets", facilityEntity.GetSchema());

        // Equipment
        var equipmentEntity = model.FindEntityType(typeof(EquipmentEntity));
        Assert.NotNull(equipmentEntity);
        Assert.Equal("equipment", equipmentEntity.GetTableName());
        Assert.Equal("property_assets", equipmentEntity.GetSchema());

        // No external entities mapped
        Assert.Null(model.FindEntityType(typeof(ApartmentUnit)));
        Assert.Null(model.FindEntityType(typeof(UserAccount)));
        Assert.Null(model.FindEntityType(typeof(Resident)));
    }

    [Fact]
    public void ApartmentsDbContext_ShouldHaveCorrectSchemaAndEntityMappings()
    {
        var options = new DbContextOptionsBuilder<ApartmentsDbContext>()
            .UseNpgsql("Host=localhost;Database=dummy;Username=dummy;Password=dummy")
            .Options;

        using var context = new ApartmentsDbContext(options);
        var model = context.Model;

        Assert.Equal("apartments", model.GetDefaultSchema());

        var apartmentUnitEntity = model.FindEntityType(typeof(ApartmentUnit));
        Assert.NotNull(apartmentUnitEntity);
        Assert.Equal("apartment_units", apartmentUnitEntity.GetTableName());
        Assert.Equal("apartments", apartmentUnitEntity.GetSchema());

        Assert.Empty(apartmentUnitEntity.GetNavigations());
        var unitNumberProp = apartmentUnitEntity.FindProperty("UnitNumber");
        Assert.NotNull(unitNumberProp);
        Assert.Equal("unit_number", unitNumberProp.GetColumnName());
        var uniqueIndex = apartmentUnitEntity.GetIndexes().SingleOrDefault(idx => idx.IsUnique);
        Assert.NotNull(uniqueIndex);
        Assert.Equal("IX_apartment_units_unit_number", uniqueIndex.GetDatabaseName());

        // No external entities
        Assert.Null(model.FindEntityType(typeof(Building)));
        Assert.Null(model.FindEntityType(typeof(UserAccount)));
        Assert.Null(model.FindEntityType(typeof(Resident)));
    }

    [Fact]
    public void AuthenticationDbContext_ShouldHaveCorrectSchemaAndEntityMappings()
    {
        var options = new DbContextOptionsBuilder<AuthenticationDbContext>()
            .UseNpgsql("Host=localhost;Database=dummy;Username=dummy;Password=dummy")
            .Options;

        using var context = new AuthenticationDbContext(options);
        var model = context.Model;

        Assert.Equal("auth", model.GetDefaultSchema());

        // UserAccount mapped to "users" table
        var userAccountEntity = model.FindEntityType(typeof(UserAccount));
        Assert.NotNull(userAccountEntity);
        Assert.Equal("users", userAccountEntity.GetTableName());
        Assert.Equal("auth", userAccountEntity.GetSchema());

        // Tokens
        var refreshTokenEntity = model.FindEntityType(typeof(RefreshToken));
        Assert.NotNull(refreshTokenEntity);
        Assert.Equal("refresh_tokens", refreshTokenEntity.GetTableName());

        var passwordResetTokenEntity = model.FindEntityType(typeof(PasswordResetToken));
        Assert.NotNull(passwordResetTokenEntity);
        Assert.Equal("password_reset_tokens", passwordResetTokenEntity.GetTableName());

        // ResidentVerification
        var residentVerificationEntity = model.FindEntityType(typeof(ResidentVerification));
        Assert.NotNull(residentVerificationEntity);
        Assert.Equal("resident_verifications", residentVerificationEntity.GetTableName());

        // Scalar cross-module IDs
        var residentIdProp = residentVerificationEntity.FindProperty("ResidentId");
        Assert.NotNull(residentIdProp);
        Assert.Equal("resident_id", residentIdProp.GetColumnName());
        Assert.False(residentIdProp.IsNullable);

        var apartmentUnitIdProp = residentVerificationEntity.FindProperty("ApartmentUnitId");
        Assert.NotNull(apartmentUnitIdProp);
        Assert.Equal("apartment_unit_id", apartmentUnitIdProp.GetColumnName());

        var verificationCodeHashProp = residentVerificationEntity.FindProperty("VerificationCodeHash");
        Assert.NotNull(verificationCodeHashProp);
        Assert.Equal("verification_code_hash", verificationCodeHashProp.GetColumnName());
        Assert.False(verificationCodeHashProp.IsNullable);

        var expiresAtProp = residentVerificationEntity.FindProperty("ExpiresAt");
        Assert.NotNull(expiresAtProp);
        Assert.Equal("expires_at", expiresAtProp.GetColumnName());
        Assert.False(expiresAtProp.IsNullable);

        Assert.Null(userAccountEntity.FindProperty("PhoneVerified"));
        Assert.Null(residentVerificationEntity.FindProperty("MethodCode"));
        Assert.Null(residentVerificationEntity.FindProperty("ReviewedBy"));
        Assert.Null(residentVerificationEntity.FindProperty("FailureReason"));
        Assert.Null(residentVerificationEntity.FindNavigation("Reviewer"));
        Assert.DoesNotContain(residentVerificationEntity.GetForeignKeys(), fk =>
            fk.Properties.Any(property => property.GetColumnName() == "reviewed_by"));

        // Navigations within module only (User, RefreshTokens, etc.)
        foreach (var nav in residentVerificationEntity.GetNavigations())
        {
            Assert.Contains("Authentication", nav.TargetEntityType.ClrType.Namespace);
        }

        // No external entities
        Assert.Null(model.FindEntityType(typeof(Resident)));
        Assert.Null(model.FindEntityType(typeof(ApartmentUnit)));
    }

    [Fact]
    public void ResidentsDbContext_ShouldHaveCorrectSchemaAndEntityMappings()
    {
        var options = new DbContextOptionsBuilder<ResidentsDbContext>()
            .UseNpgsql("Host=localhost;Database=dummy;Username=dummy;Password=dummy")
            .Options;

        using var context = new ResidentsDbContext(options);
        var model = context.Model;

        Assert.Equal("residents", model.GetDefaultSchema());

        // Resident
        var residentEntity = model.FindEntityType(typeof(Resident));
        Assert.NotNull(residentEntity);
        Assert.Equal("residents", residentEntity.GetTableName());
        Assert.Equal("residents", residentEntity.GetSchema());

        var userIdProp = residentEntity.FindProperty("UserId");
        Assert.NotNull(userIdProp);
        Assert.Equal("user_id", userIdProp.GetColumnName());

        // ResidentApartment
        var residentApartmentEntity = model.FindEntityType(typeof(ResidentApartment));
        Assert.NotNull(residentApartmentEntity);
        Assert.Equal("resident_apartments", residentApartmentEntity.GetTableName());

        var apartmentUnitIdProp = residentApartmentEntity.FindProperty("ApartmentUnitId");
        Assert.NotNull(apartmentUnitIdProp);
        Assert.Equal("apartment_unit_id", apartmentUnitIdProp.GetColumnName());

        // Navigations within module only (Resident -> ResidentApartments)
        foreach (var nav in residentEntity.GetNavigations())
        {
            Assert.Contains("Residents", nav.TargetEntityType.ClrType.Namespace);
        }

        foreach (var nav in residentApartmentEntity.GetNavigations())
        {
            Assert.Contains("Residents", nav.TargetEntityType.ClrType.Namespace);
        }

        // No external entities
        Assert.Null(model.FindEntityType(typeof(UserAccount)));
        Assert.Null(model.FindEntityType(typeof(ApartmentUnit)));
    }

    [Fact]
    public void AdministrationDbContext_ShouldHaveCorrectSchemaAndEntityMappings()
    {
        var options = new DbContextOptionsBuilder<AdministrationDbContext>()
            .UseNpgsql("Host=localhost;Database=dummy;Username=dummy;Password=dummy")
            .Options;

        using var context = new AdministrationDbContext(options);
        var model = context.Model;

        Assert.Equal("administration", model.GetDefaultSchema());

        // Role
        var roleEntity = model.FindEntityType(typeof(Role));
        Assert.NotNull(roleEntity);
        Assert.Equal("roles", roleEntity.GetTableName());
        Assert.Equal("administration", roleEntity.GetSchema());
        Assert.Equal("id", roleEntity.FindPrimaryKey()!.Properties.Single().GetColumnName());
        Assert.True(roleEntity.FindProperty("Code")!.IsIndex());

        // Permission
        var permEntity = model.FindEntityType(typeof(Permission));
        Assert.NotNull(permEntity);
        Assert.Equal("permissions", permEntity.GetTableName());
        Assert.Equal("administration", permEntity.GetSchema());
        Assert.True(permEntity.FindProperty("Code")!.IsIndex());

        // RolePermission
        var rolePermEntity = model.FindEntityType(typeof(RolePermission));
        Assert.NotNull(rolePermEntity);
        Assert.Equal("role_permissions", rolePermEntity.GetTableName());
        Assert.Equal("administration", rolePermEntity.GetSchema());
        var compositeKey = rolePermEntity.FindPrimaryKey();
        Assert.NotNull(compositeKey);
        Assert.Equal(2, compositeKey.Properties.Count);

        // UserRoleAssignment
        var uraEntity = model.FindEntityType(typeof(UserRoleAssignment));
        Assert.NotNull(uraEntity);
        Assert.Equal("user_role_assignments", uraEntity.GetTableName());
        Assert.Equal("administration", uraEntity.GetSchema());
        var userIdProp = uraEntity.FindProperty("UserId");
        Assert.NotNull(userIdProp);
        Assert.Equal("user_id", userIdProp.GetColumnName());

        
        // UserAccessHistory
        var uahEntity = model.FindEntityType(typeof(UserAccessHistory));
        Assert.NotNull(uahEntity);
        Assert.Equal("user_access_history", uahEntity.GetTableName());
        Assert.Equal("administration", uahEntity.GetSchema());

        // SystemConfiguration
        var scEntity = model.FindEntityType(typeof(SystemConfiguration));
        Assert.NotNull(scEntity);
        Assert.Equal("system_configurations", scEntity.GetTableName());
        Assert.Equal("administration", scEntity.GetSchema());
        Assert.True(scEntity.FindProperty("ConfigKey")!.IsIndex());

        // AuditLog
        var alEntity = model.FindEntityType(typeof(AuditLog));
        Assert.NotNull(alEntity);
        Assert.Equal("audit_logs", alEntity.GetTableName());
        Assert.Equal("administration", alEntity.GetSchema());

        // Ensure exactly 7 entities mapped; obsolete UserBuildingAccess is not part of the live model.
        Assert.Equal(7, model.GetEntityTypes().Count());

        Assert.Contains(rolePermEntity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(Role) &&
            fk.Properties.Single().Name == "RoleId");
        Assert.Contains(rolePermEntity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(Permission) &&
            fk.Properties.Single().Name == "PermissionId");
        Assert.Contains(uraEntity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(Role) &&
            fk.Properties.Single().Name == "RoleId");
        Assert.Contains(uahEntity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(Role) &&
            fk.Properties.Single().Name == "OldRoleId");
        Assert.Contains(uahEntity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(Role) &&
            fk.Properties.Single().Name == "NewRoleId");

        Assert.DoesNotContain(model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()), fk =>
            fk.Properties.Any(p => p.Name is "UserId" or "AssignedBy" or "UpdatedBy" or "TargetUserId" or "PerformedBy" or "ActorUserId" or "CreatedBy"));

        var designTimeModel = context.GetService<IDesignTimeModel>().Model;
        var designTimeRoleEntity = designTimeModel.FindEntityType(typeof(Role));
        Assert.NotNull(designTimeRoleEntity);
        var roleSeedData = designTimeRoleEntity.GetSeedData();
        Assert.Equal(5, roleSeedData.Count());
        Assert.Contains(roleSeedData, row => string.Equals(row["Code"] as string, SystemRoleCodes.Resident, StringComparison.Ordinal));
        Assert.Contains(roleSeedData, row => string.Equals(row["Code"] as string, SystemRoleCodes.Staff, StringComparison.Ordinal));
        Assert.Contains(roleSeedData, row => string.Equals(row["Code"] as string, SystemRoleCodes.Accountant, StringComparison.Ordinal));
        Assert.Contains(roleSeedData, row => string.Equals(row["Code"] as string, SystemRoleCodes.Manager, StringComparison.Ordinal));
        Assert.Contains(roleSeedData, row => string.Equals(row["Code"] as string, SystemRoleCodes.Admin, StringComparison.Ordinal));
        var designTimePermissionEntity = designTimeModel.FindEntityType(typeof(Permission));
        Assert.NotNull(designTimePermissionEntity);
        Assert.Equal(7, designTimePermissionEntity.GetSeedData().Count());
        var designTimeRolePermissionEntity = designTimeModel.FindEntityType(typeof(RolePermission));
        Assert.NotNull(designTimeRolePermissionEntity);
        Assert.Equal(7, designTimeRolePermissionEntity.GetSeedData().Count());

        // No external entities
        Assert.Null(model.FindEntityType(typeof(UserAccount)));
        Assert.Null(model.FindEntityType(typeof(Building)));
        Assert.Null(model.FindEntityType(typeof(ApartmentUnit)));
        Assert.Null(model.FindEntityType(typeof(Resident)));
    }

    [Fact]
    public void ImplementedModules_ShouldMapExactly45BusinessEntitiesAfterFe13()
    {
        var businessEntities = new[]
        {
            typeof(Building),
            typeof(Facility),
            typeof(EquipmentEntity),
            typeof(ApartmentUnit),
            typeof(UserAccount),
            typeof(RefreshToken),
            typeof(PasswordResetToken),
            typeof(ResidentVerification),
            typeof(Resident),
            typeof(ResidentApartment),
            typeof(Role),
            typeof(Permission),
            typeof(RolePermission),
            typeof(UserRoleAssignment),
            
            typeof(UserAccessHistory),
            typeof(SystemConfiguration),
            typeof(AuditLog),
            typeof(ServiceRequestCategory),
            typeof(ServiceRequest),
            typeof(ServiceRequestAssignment),
            typeof(ServiceRequestActivity),
            typeof(Complaint),
            typeof(ComplaintFollowup),
            typeof(ComplaintActivity),
            typeof(MaintenanceSchedule),
            typeof(MaintenanceTask),
            typeof(MaintenanceAssignment),
            typeof(MaintenanceTaskActivity),
            typeof(MaintenanceResult),
            typeof(FeeType),
            typeof(FeeRateRule),
            typeof(Invoice),
            typeof(InvoiceItem),
            typeof(InvoiceStatusHistory),
            typeof(Payment),
            typeof(PaymentStatusHistory),
            typeof(AiRequestClassification),
            typeof(AiClassificationReview),
            typeof(AiRequestRecommendation),
            typeof(AiRecommendationReview),
            typeof(Notification),
            typeof(Announcement),
            typeof(AnnouncementVersion),
            typeof(AnnouncementAudience)
        };

        Assert.Equal(44, businessEntities.Distinct().Count());
    }

    [Fact]
    public void ServiceRequestsDbContext_ShouldMapFe05ModelOnly()
    {
        var options = new DbContextOptionsBuilder<ServiceRequestsDbContext>()
            .UseNpgsql("Host=localhost;Database=dummy;Username=dummy;Password=dummy")
            .Options;

        using var context = new ServiceRequestsDbContext(options);
        var model = context.Model;

        Assert.Equal("service_requests", model.GetDefaultSchema());
        Assert.Equal(4, model.GetEntityTypes().Count());

        var categoryEntity = model.FindEntityType(typeof(ServiceRequestCategory));
        Assert.NotNull(categoryEntity);
        Assert.Equal("service_request_categories", categoryEntity.GetTableName());
        Assert.Equal("service_requests", categoryEntity.GetSchema());
        Assert.Contains(categoryEntity.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(ServiceRequestCategory.Code));

        var requestEntity = model.FindEntityType(typeof(ServiceRequest));
        Assert.NotNull(requestEntity);
        Assert.Equal("service_requests", requestEntity.GetTableName());
        Assert.Equal("service_requests", requestEntity.GetSchema());
        Assert.Contains(requestEntity.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(ServiceRequest.RequestNumber));
        Assert.Contains(requestEntity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(ServiceRequestCategory) &&
            fk.Properties.Single().Name == nameof(ServiceRequest.CategoryId));

        var assignmentEntity = model.FindEntityType(typeof(ServiceRequestAssignment));
        Assert.NotNull(assignmentEntity);
        Assert.Equal("service_request_assignments", assignmentEntity.GetTableName());
        Assert.Contains(assignmentEntity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(ServiceRequest) &&
            fk.Properties.Single().Name == nameof(ServiceRequestAssignment.ServiceRequestId));
        Assert.Contains(assignmentEntity.GetIndexes(), index =>
            index.IsUnique &&
            index.GetFilter() == "\"status\" IN ('ASSIGNED', 'IN_PROGRESS')" &&
            index.Properties.Single().Name == nameof(ServiceRequestAssignment.ServiceRequestId));

        var activityEntity = model.FindEntityType(typeof(ServiceRequestActivity));
        Assert.NotNull(activityEntity);
        Assert.Equal("service_request_activities", activityEntity.GetTableName());
        Assert.Contains(activityEntity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(ServiceRequest) &&
            fk.Properties.Single().Name == nameof(ServiceRequestActivity.ServiceRequestId));
        Assert.Contains(activityEntity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(ServiceRequestAssignment) &&
            fk.Properties.Single().Name == nameof(ServiceRequestActivity.AssignmentId));

        Assert.DoesNotContain(model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()), fk =>
            fk.Properties.Any(p => p.Name is "ResidentId" or "ResidentApartmentId" or "ApartmentUnitId" or "FacilityId" or "EquipmentId" or "StaffUserId" or "AssignedBy" or "ClosedBy" or "PerformedBy"));
    }

    [Fact]
    public void ComplaintsDbContext_ShouldMapFe06ModelOnly()
    {
        var options = new DbContextOptionsBuilder<ComplaintsDbContext>()
            .UseNpgsql("Host=localhost;Database=dummy;Username=dummy;Password=dummy")
            .Options;

        using var context = new ComplaintsDbContext(options);
        var model = context.Model;

        Assert.Equal("complaints", model.GetDefaultSchema());
        Assert.Equal(3, model.GetEntityTypes().Count());

        var complaintEntity = model.FindEntityType(typeof(Complaint));
        Assert.NotNull(complaintEntity);
        Assert.Equal("complaints", complaintEntity.GetTableName());
        Assert.Equal("complaints", complaintEntity.GetSchema());
        Assert.Contains(complaintEntity.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(Complaint.ComplaintNumber));

        var followupEntity = model.FindEntityType(typeof(ComplaintFollowup));
        Assert.NotNull(followupEntity);
        Assert.Equal("complaint_followups", followupEntity.GetTableName());
        Assert.Contains(followupEntity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(Complaint) &&
            fk.Properties.Single().Name == nameof(ComplaintFollowup.ComplaintId));

        var activityEntity = model.FindEntityType(typeof(ComplaintActivity));
        Assert.NotNull(activityEntity);
        Assert.Equal("complaint_activities", activityEntity.GetTableName());
        Assert.Contains(activityEntity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(Complaint) &&
            fk.Properties.Single().Name == nameof(ComplaintActivity.ComplaintId));
        Assert.Contains(activityEntity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(ComplaintFollowup) &&
            fk.Properties.Single().Name == nameof(ComplaintActivity.FollowupId));

        Assert.DoesNotContain(model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()), fk =>
            fk.Properties.Any(p => p.Name is "ResidentId" or "ResidentApartmentId" or "ApartmentUnitId" or "RelatedServiceRequestId" or "FacilityId" or "EquipmentId" or "StaffUserId" or "AssignedBy" or "ClosedBy" or "PerformedBy"));
    }

    [Fact]
    public void MaintenanceDbContext_ShouldMapFe07ModelOnly()
    {
        var options = new DbContextOptionsBuilder<MaintenanceDbContext>()
            .UseNpgsql("Host=localhost;Database=dummy;Username=dummy;Password=dummy")
            .Options;

        using var context = new MaintenanceDbContext(options);
        var model = context.Model;
        var designTimeModel = context.GetService<IDesignTimeModel>().Model;

        Assert.Equal("maintenance", model.GetDefaultSchema());
        Assert.Equal(5, model.GetEntityTypes().Count());

        var scheduleEntity = model.FindEntityType(typeof(MaintenanceSchedule));
        Assert.NotNull(scheduleEntity);
        Assert.Equal("maintenance_schedules", scheduleEntity.GetTableName());
        Assert.Equal("maintenance", scheduleEntity.GetSchema());
        Assert.Contains(scheduleEntity.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(MaintenanceSchedule.ScheduleCode));
        var designTimeScheduleEntity = designTimeModel.FindEntityType(typeof(MaintenanceSchedule));
        Assert.NotNull(designTimeScheduleEntity);
        Assert.Contains(designTimeScheduleEntity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_maintenance_schedules_planned_end_at");

        var taskEntity = model.FindEntityType(typeof(MaintenanceTask));
        Assert.NotNull(taskEntity);
        Assert.Equal("maintenance_tasks", taskEntity.GetTableName());
        Assert.Equal("maintenance", taskEntity.GetSchema());
        Assert.Contains(taskEntity.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(MaintenanceTask.TaskNumber));
        Assert.Contains(taskEntity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(MaintenanceSchedule) &&
            fk.Properties.Single().Name == nameof(MaintenanceTask.ScheduleId));
        var designTimeTaskEntity = designTimeModel.FindEntityType(typeof(MaintenanceTask));
        Assert.NotNull(designTimeTaskEntity);
        Assert.Contains(designTimeTaskEntity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_maintenance_tasks_due_at");
        Assert.Contains(designTimeTaskEntity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_maintenance_tasks_completed_at");
        Assert.Contains(designTimeTaskEntity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_maintenance_tasks_closed_at");

        var assignmentEntity = model.FindEntityType(typeof(MaintenanceAssignment));
        Assert.NotNull(assignmentEntity);
        Assert.Equal("maintenance_assignments", assignmentEntity.GetTableName());
        Assert.Contains(assignmentEntity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(MaintenanceTask) &&
            fk.Properties.Single().Name == nameof(MaintenanceAssignment.MaintenanceTaskId));
        Assert.Contains(assignmentEntity.GetIndexes(), index =>
            index.IsUnique &&
            index.GetFilter() == "\"status\" IN ('ASSIGNED', 'IN_PROGRESS')" &&
            index.Properties.Single().Name == nameof(MaintenanceAssignment.MaintenanceTaskId));

        var activityEntity = model.FindEntityType(typeof(MaintenanceTaskActivity));
        Assert.NotNull(activityEntity);
        Assert.Equal("maintenance_task_activities", activityEntity.GetTableName());
        Assert.Contains(activityEntity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(MaintenanceTask) &&
            fk.Properties.Single().Name == nameof(MaintenanceTaskActivity.MaintenanceTaskId));
        Assert.Contains(activityEntity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(MaintenanceAssignment) &&
            fk.Properties.Single().Name == nameof(MaintenanceTaskActivity.AssignmentId));

        var resultEntity = model.FindEntityType(typeof(MaintenanceResult));
        Assert.NotNull(resultEntity);
        Assert.Equal("maintenance_results", resultEntity.GetTableName());
        Assert.Contains(resultEntity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(MaintenanceTask) &&
            fk.Properties.Single().Name == nameof(MaintenanceResult.MaintenanceTaskId));
        Assert.Contains(resultEntity.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(MaintenanceResult.MaintenanceTaskId),
                nameof(MaintenanceResult.AttemptNo)
            ]));
        var designTimeResultEntity = designTimeModel.FindEntityType(typeof(MaintenanceResult));
        Assert.NotNull(designTimeResultEntity);
        Assert.Contains(designTimeResultEntity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_maintenance_results_attempt_no");

        Assert.DoesNotContain(model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()), fk =>
            fk.Properties.Any(p => p.Name is "FacilityId" or "EquipmentId" or "SourceServiceRequestId" or "SourceComplaintId" or "StaffUserId" or "AssignedBy" or "ClosedBy" or "CreatedBy" or "UpdatedBy" or "PerformedBy" or "SubmittedBy" or "ReviewedBy"));
    }

    [Fact]
    public void BillingDbContext_ShouldMapFe08ModelOnly()
    {
        var options = new DbContextOptionsBuilder<BillingDbContext>()
            .UseNpgsql("Host=localhost;Database=dummy;Username=dummy;Password=dummy")
            .Options;

        using var context = new BillingDbContext(options);
        var model = context.Model;
        var designTimeModel = context.GetService<IDesignTimeModel>().Model;

        Assert.Equal("billing", model.GetDefaultSchema());
        Assert.Equal(5, model.GetEntityTypes().Count());

        var feeTypeEntity = model.FindEntityType(typeof(FeeType));
        Assert.NotNull(feeTypeEntity);
        Assert.Equal("fee_types", feeTypeEntity.GetTableName());
        Assert.Equal("billing", feeTypeEntity.GetSchema());
        Assert.Contains(feeTypeEntity.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(FeeType.Code));

        var feeRateRuleEntity = model.FindEntityType(typeof(FeeRateRule));
        Assert.NotNull(feeRateRuleEntity);
        Assert.Equal("fee_rate_rules", feeRateRuleEntity.GetTableName());
        Assert.Contains(feeRateRuleEntity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(FeeType) &&
            fk.Properties.Single().Name == nameof(FeeRateRule.FeeTypeId));
        Assert.Equal("numeric(18,0)", feeRateRuleEntity.FindProperty(nameof(FeeRateRule.UnitRate))!.GetColumnType());
        Assert.Equal("date", feeRateRuleEntity.FindProperty(nameof(FeeRateRule.EffectiveFrom))!.GetColumnType());
        var designTimeFeeRateRuleEntity = designTimeModel.FindEntityType(typeof(FeeRateRule));
        Assert.NotNull(designTimeFeeRateRuleEntity);
        Assert.Contains(designTimeFeeRateRuleEntity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_fee_rate_rules_effective_dates");
        Assert.Contains(designTimeFeeRateRuleEntity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_fee_rate_rules_amount_range");

        var invoiceEntity = model.FindEntityType(typeof(Invoice));
        Assert.NotNull(invoiceEntity);
        Assert.Equal("invoices", invoiceEntity.GetTableName());
        Assert.Contains(invoiceEntity.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(Invoice.InvoiceNumber));
        Assert.Contains(invoiceEntity.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(Invoice.ApartmentUnitId),
                nameof(Invoice.BillingPeriodStart),
                nameof(Invoice.BillingPeriodEnd)
            ]));
        Assert.Equal("numeric(18,0)", invoiceEntity.FindProperty(nameof(Invoice.Subtotal))!.GetColumnType());
        Assert.Equal("numeric(18,0)", invoiceEntity.FindProperty(nameof(Invoice.TotalAmount))!.GetColumnType());
        var designTimeInvoiceEntity = designTimeModel.FindEntityType(typeof(Invoice));
        Assert.NotNull(designTimeInvoiceEntity);
        Assert.Contains(designTimeInvoiceEntity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_invoices_billing_period");
        Assert.Contains(designTimeInvoiceEntity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_invoices_total_matches_subtotal");

        var itemEntity = model.FindEntityType(typeof(InvoiceItem));
        Assert.NotNull(itemEntity);
        Assert.Equal("invoice_items", itemEntity.GetTableName());
        Assert.Contains(itemEntity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(Invoice) &&
            fk.Properties.Single().Name == nameof(InvoiceItem.InvoiceId));
        Assert.Contains(itemEntity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(FeeType) &&
            fk.Properties.Single().Name == nameof(InvoiceItem.FeeTypeId));
        Assert.Contains(itemEntity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(FeeRateRule) &&
            fk.Properties.Single().Name == nameof(InvoiceItem.FeeRateRuleId));
        Assert.Equal("numeric(18,4)", itemEntity.FindProperty(nameof(InvoiceItem.Quantity))!.GetColumnType());
        Assert.Equal("numeric(18,0)", itemEntity.FindProperty(nameof(InvoiceItem.UnitRate))!.GetColumnType());
        Assert.Equal("numeric(18,0)", itemEntity.FindProperty(nameof(InvoiceItem.LineAmount))!.GetColumnType());

        var historyEntity = model.FindEntityType(typeof(InvoiceStatusHistory));
        Assert.NotNull(historyEntity);
        Assert.Equal("invoice_status_history", historyEntity.GetTableName());
        Assert.Contains(historyEntity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(Invoice) &&
            fk.Properties.Single().Name == nameof(InvoiceStatusHistory.InvoiceId));

        Assert.DoesNotContain(model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()), fk =>
            fk.Properties.Any(p => p.Name is "ApartmentUnitId" or "CreatedBy" or "UpdatedBy" or "IssuedBy" or "CancelledBy" or "ChangedBy"));
    }

    [Fact]
    public void PaymentsDbContext_ShouldMapFe09ModelOnly()
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseNpgsql("Host=localhost;Database=dummy;Username=dummy;Password=dummy")
            .Options;

        using var context = new PaymentsDbContext(options);
        var model = context.Model;
        var designTimeModel = context.GetService<IDesignTimeModel>().Model;

        Assert.Equal("payments", model.GetDefaultSchema());
        Assert.Equal(2, model.GetEntityTypes().Count());

        var paymentEntity = model.FindEntityType(typeof(Payment));
        Assert.NotNull(paymentEntity);
        Assert.Equal("payments", paymentEntity.GetTableName());
        Assert.Equal("payments", paymentEntity.GetSchema());
        Assert.Equal("numeric(18,0)", paymentEntity.FindProperty(nameof(Payment.Amount))!.GetColumnType());
        Assert.Equal("timestamp with time zone", paymentEntity.FindProperty(nameof(Payment.PaymentDate))!.GetColumnType());
        Assert.Contains(paymentEntity.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(Payment.PaymentNumber));
        Assert.Contains(paymentEntity.GetIndexes(), index =>
            index.IsUnique &&
            index.GetFilter() == "reference_number IS NOT NULL" &&
            index.Properties.Single().Name == nameof(Payment.ReferenceNumber));

        var designTimePaymentEntity = designTimeModel.FindEntityType(typeof(Payment));
        Assert.NotNull(designTimePaymentEntity);
        Assert.Contains(designTimePaymentEntity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_payments_amount_positive");
        Assert.Contains(designTimePaymentEntity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_payments_confirmed_fields");
        Assert.Contains(designTimePaymentEntity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_payments_rejected_fields");

        var historyEntity = model.FindEntityType(typeof(PaymentStatusHistory));
        Assert.NotNull(historyEntity);
        Assert.Equal("payment_status_history", historyEntity.GetTableName());
        Assert.Contains(historyEntity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(Payment) &&
            fk.Properties.Single().Name == nameof(PaymentStatusHistory.PaymentId));
        Assert.Contains(historyEntity.GetIndexes(), index =>
            index.Properties.Single().Name == nameof(PaymentStatusHistory.ChangedAt));

        Assert.DoesNotContain(model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()), fk =>
            fk.Properties.Any(p => p.Name is "InvoiceId" or "SubmittedBy" or "ConfirmedBy" or "RejectedBy" or "ChangedBy"));
    }

    [Fact]
    public void AiClassificationDbContext_ShouldMapFe10ModelOnly()
    {
        var options = new DbContextOptionsBuilder<AiClassificationDbContext>()
            .UseNpgsql("Host=localhost;Database=dummy;Username=dummy;Password=dummy")
            .Options;

        using var context = new AiClassificationDbContext(options);
        var model = context.Model;
        var designTimeModel = context.GetService<IDesignTimeModel>().Model;

        Assert.Equal("ai_classification", model.GetDefaultSchema());
        Assert.Equal(2, model.GetEntityTypes().Count());

        var classificationEntity = model.FindEntityType(typeof(AiRequestClassification));
        Assert.NotNull(classificationEntity);
        Assert.Equal("ai_request_classifications", classificationEntity.GetTableName());
        Assert.Equal("ai_classification", classificationEntity.GetSchema());
        Assert.Equal("numeric(5,4)", classificationEntity.FindProperty(nameof(AiRequestClassification.ConfidenceScore))!.GetColumnType());
        Assert.Contains(classificationEntity.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(AiRequestClassification.ServiceRequestId),
                nameof(AiRequestClassification.AttemptNo)
            ]));

        var designTimeClassificationEntity = designTimeModel.FindEntityType(typeof(AiRequestClassification));
        Assert.NotNull(designTimeClassificationEntity);
        Assert.Contains(designTimeClassificationEntity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_ai_request_classifications_attempt_no");
        Assert.Contains(designTimeClassificationEntity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_ai_request_classifications_confidence_score");
        Assert.Contains(designTimeClassificationEntity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_ai_request_classifications_tokens");
        Assert.Contains(designTimeClassificationEntity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_ai_request_classifications_success_fields" &&
            constraint.Sql!.Contains("error_message IS NULL", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(designTimeClassificationEntity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_ai_request_classifications_failed_fields" &&
            constraint.Sql!.Contains("predicted_category_id IS NULL", StringComparison.OrdinalIgnoreCase) &&
            constraint.Sql!.Contains("confidence_score IS NULL", StringComparison.OrdinalIgnoreCase));

        var reviewEntity = model.FindEntityType(typeof(AiClassificationReview));
        Assert.NotNull(reviewEntity);
        Assert.Equal("ai_classification_reviews", reviewEntity.GetTableName());
        Assert.Contains(reviewEntity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(AiRequestClassification) &&
            fk.Properties.Single().Name == nameof(AiClassificationReview.ClassificationId));
        Assert.Contains(reviewEntity.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(AiClassificationReview.ClassificationId));

        var designTimeReviewEntity = designTimeModel.FindEntityType(typeof(AiClassificationReview));
        Assert.NotNull(designTimeReviewEntity);
        Assert.Contains(designTimeReviewEntity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_ai_classification_reviews_final_category");

        Assert.DoesNotContain(model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()), fk =>
            fk.Properties.Any(p => p.Name is "ServiceRequestId" or "PredictedCategoryId" or "ReviewedBy" or "FinalCategoryId"));
    }

    [Fact]
    public void AiRecommendationDbContext_ShouldMapFe11ModelOnly()
    {
        var options = new DbContextOptionsBuilder<AiRecommendationDbContext>()
            .UseNpgsql("Host=localhost;Database=dummy;Username=dummy;Password=dummy")
            .Options;

        using var context = new AiRecommendationDbContext(options);
        var model = context.Model;
        var designTimeModel = context.GetService<IDesignTimeModel>().Model;

        Assert.Equal("ai_recommendation", model.GetDefaultSchema());
        Assert.Equal(2, model.GetEntityTypes().Count());

        var recommendationEntity = model.FindEntityType(typeof(AiRequestRecommendation));
        Assert.NotNull(recommendationEntity);
        Assert.Equal("ai_request_recommendations", recommendationEntity.GetTableName());
        Assert.Equal("ai_recommendation", recommendationEntity.GetSchema());
        Assert.Equal("jsonb", recommendationEntity.FindProperty(nameof(AiRequestRecommendation.RecommendedResourcesJson))!.GetColumnType());
        Assert.Contains(recommendationEntity.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(AiRequestRecommendation.ServiceRequestId),
                nameof(AiRequestRecommendation.AttemptNo)
            ]));

        var designTimeRecommendationEntity = designTimeModel.FindEntityType(typeof(AiRequestRecommendation));
        Assert.NotNull(designTimeRecommendationEntity);
        Assert.Contains(designTimeRecommendationEntity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_ai_request_recommendations_attempt_no");
        Assert.Contains(designTimeRecommendationEntity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_ai_request_recommendations_tokens");
        Assert.Contains(designTimeRecommendationEntity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_ai_request_recommendations_success_fields" &&
            constraint.Sql!.Contains("suggested_priority_code IS NOT NULL", StringComparison.OrdinalIgnoreCase) &&
            constraint.Sql!.Contains("error_message IS NULL", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(designTimeRecommendationEntity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_ai_request_recommendations_failed_fields" &&
            constraint.Sql!.Contains("suggested_priority_code IS NULL", StringComparison.OrdinalIgnoreCase) &&
            constraint.Sql!.Contains("maintenance_recommended IS NULL", StringComparison.OrdinalIgnoreCase));

        var reviewEntity = model.FindEntityType(typeof(AiRecommendationReview));
        Assert.NotNull(reviewEntity);
        Assert.Equal("ai_recommendation_reviews", reviewEntity.GetTableName());
        Assert.Contains(reviewEntity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(AiRequestRecommendation) &&
            fk.Properties.Single().Name == nameof(AiRecommendationReview.RecommendationId));
        Assert.Contains(reviewEntity.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(AiRecommendationReview.RecommendationId));

        var designTimeReviewEntity = designTimeModel.FindEntityType(typeof(AiRecommendationReview));
        Assert.NotNull(designTimeReviewEntity);
        Assert.Contains(designTimeReviewEntity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_ai_recommendation_reviews_final_fields");
        Assert.Contains(designTimeReviewEntity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_ai_recommendation_reviews_corrected_note");

        Assert.DoesNotContain(model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()), fk =>
            fk.Properties.Any(p => p.Name is "ServiceRequestId" or "ReviewedBy"));
    }

    [Fact]
    public void CommunicationDbContext_ShouldMapFe13ModelOnly()
    {
        var options = new DbContextOptionsBuilder<CommunicationDbContext>()
            .UseNpgsql("Host=localhost;Database=dummy;Username=dummy;Password=dummy")
            .Options;

        using var context = new CommunicationDbContext(options);
        var model = context.Model;
        var designTimeModel = context.GetService<IDesignTimeModel>().Model;

        Assert.Equal("communication", model.GetDefaultSchema());
        Assert.Equal(4, model.GetEntityTypes().Count());

        var notificationEntity = model.FindEntityType(typeof(Notification));
        Assert.NotNull(notificationEntity);
        Assert.Equal("notifications", notificationEntity.GetTableName());
        Assert.Equal("communication", notificationEntity.GetSchema());
        Assert.Empty(notificationEntity.GetForeignKeys());
        Assert.Contains(notificationEntity.GetIndexes(), index =>
            index.IsUnique &&
            index.GetFilter() == "source_event_id IS NOT NULL" &&
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(Notification.RecipientUserId),
                nameof(Notification.SourceEventId)
            ]));
        var designTimeNotificationEntity = designTimeModel.FindEntityType(typeof(Notification));
        Assert.NotNull(designTimeNotificationEntity);
        Assert.Contains(designTimeNotificationEntity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_notifications_read_state");
        Assert.Contains(designTimeNotificationEntity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_notifications_source_reference");

        var announcementEntity = model.FindEntityType(typeof(Announcement));
        Assert.NotNull(announcementEntity);
        Assert.Equal("announcements", announcementEntity.GetTableName());
        Assert.Contains(announcementEntity.GetIndexes(), index => index.Properties.Single().Name == nameof(Announcement.Status));
        Assert.Contains(announcementEntity.GetNavigations(), navigation =>
            navigation.TargetEntityType.ClrType == typeof(AnnouncementAudience));
        Assert.Contains(announcementEntity.GetNavigations(), navigation =>
            navigation.TargetEntityType.ClrType == typeof(AnnouncementVersion));
        var designTimeAnnouncementEntity = designTimeModel.FindEntityType(typeof(Announcement));
        Assert.NotNull(designTimeAnnouncementEntity);
        Assert.Contains(designTimeAnnouncementEntity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_announcements_published_state");

        var audienceEntity = model.FindEntityType(typeof(AnnouncementAudience));
        Assert.NotNull(audienceEntity);
        Assert.Equal("announcement_audiences", audienceEntity.GetTableName());
        Assert.Contains(audienceEntity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(Announcement) &&
            fk.Properties.Single().Name == nameof(AnnouncementAudience.AnnouncementId));
        var designTimeAudienceEntity = designTimeModel.FindEntityType(typeof(AnnouncementAudience));
        Assert.NotNull(designTimeAudienceEntity);
        Assert.Contains(designTimeAudienceEntity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_announcement_audiences_target");
        Assert.Contains(audienceEntity.GetIndexes(), index =>
            index.IsUnique &&
            index.GetDatabaseName() == "UX_announcement_audiences_announcement_type_global" &&
            index.GetFilter() == "audience_type IN ('ALL_USERS', 'ALL_RESIDENTS')" &&
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(AnnouncementAudience.AnnouncementId),
                nameof(AnnouncementAudience.AudienceType)
            ]));
        Assert.Contains(audienceEntity.GetIndexes(), index =>
            index.IsUnique &&
            index.GetDatabaseName() == "UX_announcement_audiences_announcement_role" &&
            index.GetFilter() == "audience_type = 'ROLE' AND role_id IS NOT NULL" &&
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(AnnouncementAudience.AnnouncementId),
                nameof(AnnouncementAudience.RoleId)
            ]));
        Assert.Contains(audienceEntity.GetIndexes(), index =>
            index.IsUnique &&           
            index.GetDatabaseName() == "UX_announcement_audiences_announcement_apartment" &&
            index.GetFilter() == "audience_type = 'APARTMENT' AND apartment_unit_id IS NOT NULL" &&
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(AnnouncementAudience.AnnouncementId),
                nameof(AnnouncementAudience.ApartmentUnitId)
            ]));
        Assert.Contains(audienceEntity.GetIndexes(), index =>
            index.IsUnique &&
            index.GetDatabaseName() == "UX_announcement_audiences_announcement_resident" &&
            index.GetFilter() == "audience_type = 'RESIDENT' AND resident_id IS NOT NULL" &&
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(AnnouncementAudience.AnnouncementId),
                nameof(AnnouncementAudience.ResidentId)
            ]));

        var versionEntity = model.FindEntityType(typeof(AnnouncementVersion));
        Assert.NotNull(versionEntity);
        Assert.Equal("announcement_versions", versionEntity.GetTableName());
        Assert.Equal("jsonb", versionEntity.FindProperty(nameof(AnnouncementVersion.AudienceSnapshotJson))!.GetColumnType());
        Assert.Contains(versionEntity.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(Announcement) &&
            fk.Properties.Single().Name == nameof(AnnouncementVersion.AnnouncementId));
        Assert.Contains(versionEntity.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(AnnouncementVersion.AnnouncementId),
                nameof(AnnouncementVersion.VersionNo)
            ]));
        var designTimeVersionEntity = designTimeModel.FindEntityType(typeof(AnnouncementVersion));
        Assert.NotNull(designTimeVersionEntity);
        Assert.Contains(designTimeVersionEntity.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_announcement_versions_version_no");

        Assert.DoesNotContain(model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()), fk =>
            fk.Properties.Any(p => p.Name is "RecipientUserId" or "CreatedBy" or "UpdatedBy" or "ChangedBy" or "RoleId" or "ApartmentUnitId" or "ResidentId" or "SourceId"));
    }
}
