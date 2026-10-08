using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.Residents.Domain.ResidentApartments;
using PropFlow.Modules.Residents.Domain.Residents;
using PropFlow.Modules.Residents.Infrastructure.Persistence;

namespace PropFlow.Modules.Residents.Application;

/// <summary>Owns FE-02 onboarding orchestration; all writes are committed by one local transaction.</summary>
public sealed class ResidentOnboardingService(
    ResidentsDbContext residents,
    ResidentCodeGenerator codes,
    ResidentDuplicatePolicy duplicates,
    ResidentResidencyService residency,
    IApartmentOwnershipCommand ownership,
    IAtomicTransactionCoordinator transaction,
    TimeProvider clock)
{
    public async Task<ResidentOnboardingResult> OnboardAsync(ResidentOnboardingCommand command, Guid? actorId, CancellationToken ct)
    {
        try
        {
            return await transaction.ExecuteAsync(async token =>
            {
                await duplicates.EnsureUniqueAsync(command.IdentityType, command.IdentityNumber, command.Email, null, token);
                var now = clock.GetUtcNow();
                var resident = new Resident(await codes.NextAsync(token), command.FullName, command.DateOfBirth, command.Gender, command.Nationality, command.IdentityType, command.IdentityNumber, command.IdentityIssuedDate, command.IdentityExpiryDate, now, phoneNumber: command.PhoneNumber, email: command.Email, note: command.Note, createdBy: actorId);
                residents.Residents.Add(resident);
                await residents.SaveChangesAsync(token);

                if (command.RelationshipKind is OnboardingRelationshipKind.OWNER_ONLY or OnboardingRelationshipKind.OWNER_AND_RESIDENT)
                    await ownership.AddOwnerAsync(command.Residency.ApartmentUnitId, resident.Id, command.Residency.StartDate, actorId, token);

                ResidentApartment? relation = null;
                if (command.RelationshipKind is OnboardingRelationshipKind.RESIDENT_ONLY or OnboardingRelationshipKind.OWNER_AND_RESIDENT)
                {
                    var residencyType = command.RelationshipKind == OnboardingRelationshipKind.OWNER_AND_RESIDENT ? ResidencyType.OWNER_OCCUPIED : command.Residency.ResidencyType;
                    relation = await residency.CreateAsync(resident.Id, new(command.Residency.ApartmentUnitId, command.Residency.HouseholdRole, residencyType, command.Residency.HouseholdHeadResidencyId, command.Residency.RelationshipToHead, command.Residency.StartDate, command.Residency.EndDate, command.Residency.Note), actorId, token);
                }
                return new ResidentOnboardingResult(resident, relation);
            }, ct);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException exception)
        {
            var translated = duplicates.TranslateDatabaseException(exception);
            if (translated is not null) throw translated;
            throw;
        }
    }
}

public enum OnboardingRelationshipKind { OWNER_ONLY, RESIDENT_ONLY, OWNER_AND_RESIDENT }
public sealed record ResidentOnboardingCommand(string FullName, DateOnly? DateOfBirth, string? Gender, string? Nationality, string? IdentityType, string? IdentityNumber, DateOnly? IdentityIssuedDate, DateOnly? IdentityExpiryDate, string? PhoneNumber, string? Email, string? Note, OnboardingRelationshipKind RelationshipKind, OnboardingResidency Residency);
public sealed record OnboardingResidency(Guid ApartmentUnitId, HouseholdRole HouseholdRole, ResidencyType ResidencyType, Guid? HouseholdHeadResidencyId, HouseholdRelationship? RelationshipToHead, DateOnly StartDate, DateOnly? EndDate, string? Note);
public sealed record ResidentOnboardingResult(Resident Resident, ResidentApartment? Residency);
