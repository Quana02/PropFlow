namespace PropFlow.Modules.Residents.Contracts;

// Read-only directory filters; separate from onboarding relationship choices.
public enum ResidentDirectoryRelationshipFilter
{
    OWNER_ONLY,
    RESIDENT_ONLY,
    OWNER_AND_RESIDENT,
    ANY_OWNER,
    ANY_RESIDENT,
    NONE
}
