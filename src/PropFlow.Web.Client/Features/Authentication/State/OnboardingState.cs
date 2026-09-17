using PropFlow.Modules.Authentication.Contracts;

namespace PropFlow.Web.Client.Features.Authentication.State;

// Only non-secret challenge context and short-lived reset proof. Never persisted in browser storage.
public sealed class OnboardingState
{
    public ChallengeResponse? Challenge { get; set; }
    public bool Recovery { get; set; }
    public ResetProofResponse? ResetProof { get; set; }
    public void Clear() { Challenge = null; ResetProof = null; Recovery = false; }
}
