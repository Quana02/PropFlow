namespace PropFlow.Modules.PropertyAssets.Contracts;

// Implemented by the modules that own assignments, without exposing their entities.
public interface IAssignedAssetSource
{
    Task<IReadOnlyList<AssignedAssetReference>> GetActiveAssignmentsAsync(Guid staffUserId, CancellationToken ct);
}

public sealed record AssignedAssetReference(Guid? FacilityId, Guid? EquipmentId);
