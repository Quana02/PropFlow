namespace PropFlow.Web.Client.Features.Resident.Portal.Services;

public enum AssistantIdleState { Stopped, Listening, Warning, Expired }

public sealed class ResidentAssistantIdleWatch(TimeProvider? time = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private long lastActivity;
    private bool wasOccupied;
    public AssistantIdleState State { get; private set; }
    public int SecondsRemaining { get; private set; }
    public void Start() { State = AssistantIdleState.Listening; wasOccupied = false; Reset(); }
    public void Stop() { State = AssistantIdleState.Stopped; SecondsRemaining = 0; wasOccupied = false; }
    private void Reset() { lastActivity = clock.GetTimestamp(); SecondsRemaining = 0; State = AssistantIdleState.Listening; }

    public bool Activity(bool occupied = false)
    {
        if (State is AssistantIdleState.Stopped or AssistantIdleState.Expired) return false;
        if (!occupied && clock.GetElapsedTime(lastActivity).TotalSeconds >= 90) { Expire(); return false; }
        Reset(); wasOccupied = occupied; return true;
    }
    public AssistantIdleState Tick(bool occupied)
    {
        if (State is AssistantIdleState.Stopped or AssistantIdleState.Expired) return State;
        // Never leave while an answer, lookup, draft review or confirmed submission is in progress.
        if (occupied) { Reset(); wasOccupied = true; return State; }
        if (wasOccupied) { Reset(); wasOccupied = false; }
        var elapsed = clock.GetElapsedTime(lastActivity).TotalSeconds;
        if (elapsed >= 90) { Expire(); return State; }
        if (elapsed >= 60) { State = AssistantIdleState.Warning; SecondsRemaining = (int)Math.Ceiling(90 - elapsed); }
        return State;
    }
    private void Expire() { State = AssistantIdleState.Expired; SecondsRemaining = 0; }
}
