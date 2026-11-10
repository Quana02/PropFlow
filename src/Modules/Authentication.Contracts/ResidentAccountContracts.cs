using System.ComponentModel.DataAnnotations;

namespace PropFlow.Modules.Authentication.Contracts;

public static class ResidentAccountPolicies
{
    public const string OwnEmailRoles = "ADMIN,MANAGER,STAFF,ACCOUNTANT";
    public const string Read = "READ_RESIDENT_ACCOUNTS";
    public const string RequestEmailChange = "REQUEST_RESIDENT_EMAIL_CHANGE";
}

public sealed record ResidentAccountItem(Guid ResidentId, string ResidentCode, string FullName, string? ContactEmail,
    IReadOnlyList<string> Apartments, string? Username, string? AccountEmail, string? AccountStatus, bool EmailVerified,
    bool CanRequestEmailChange);

public sealed record ResidentAccountPage(IReadOnlyList<ResidentAccountItem> Items, int TotalCount, int PageIndex, int PageSize);

public enum ResidentEmailChangeReason { LostMailbox = 1, IncorrectAddress, ReplacingMailbox }

public sealed record RequestResidentEmailChange(
    [Required, EmailAddress, StringLength(255)] string NewEmail,
    [EnumDataType(typeof(ResidentEmailChangeReason))] ResidentEmailChangeReason Reason,
    bool IdentityCheckedInPerson);

public sealed record VerifyResidentEmailChange(
    Guid ChallengeId,
    [Required, EmailAddress, StringLength(255)] string NewEmail,
    [Required, StringLength(128)] string CurrentPassword,
    [Required, RegularExpression(@"[0-9]{6}")] string Code);

public sealed record ResidentEmailChangePending(Guid? ChallengeId, DateTimeOffset? ExpiresAt);
