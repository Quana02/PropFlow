namespace PropFlow.Modules.Administration.Domain.UserAccessHistories;

public enum AccessActionType
{
    CREATED,
    ACTIVATED,
    LOCKED,
    UNLOCKED,
    SUSPENDED,
    DISABLED,
    RE_ENABLED,
    ROLE_CHANGED,
    BUILDING_ACCESS_GRANTED,
    BUILDING_ACCESS_REVOKED
}
