using PropFlow.Modules.PropertyAssets.Application;
using PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos;
using PropFlow.Modules.PropertyAssets.Application.Facilities.Dtos;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.PropertyAssets.Domain.Facilities;
using PropFlow.Shared;

namespace PropFlow.Modules.PropertyAssets.Application.Facilities.Services;

public class FacilityService : IFacilityService
{
    private readonly IPropertyAssetsStore _store;

    public FacilityService(IPropertyAssetsStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<PagedResult<FacilityDto>> GetFacilitiesAsync(FacilityFilterQuery query, CancellationToken cancellationToken = default)
    {
        var page = await _store.FacilitiesAsync(query, cancellationToken);
        var items = page.Items.Select(f => new FacilityDto(
                f.Id,
                f.Code,
                f.Name,
                f.FacilityType,
                f.LocationDescription,
                f.Description,
                f.Status,
                f.CreatedBy,
                f.UpdatedBy,
                f.CreatedAt,
                f.UpdatedAt)).ToList();

        return new PagedResult<FacilityDto>(items, page.TotalCount, page.PageIndex, page.PageSize);
    }

    public async Task<FacilityDetailDto?> GetFacilityByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var facility = await _store.FacilityAsync(id, false, true, cancellationToken);

        if (facility == null)
            return null;

        var totalEq = facility.Equipment.Count;
        var activeEq = facility.Equipment.Count(e => e.Status == PropFlow.Modules.PropertyAssets.Domain.Equipment.EquipmentStatus.ACTIVE);

        return new FacilityDetailDto(
            facility.Id,
            facility.Code,
            facility.Name,
            facility.FacilityType,
            facility.LocationDescription,
            facility.Description,
            facility.Status,
            totalEq,
            activeEq,
            facility.CreatedBy,
            facility.UpdatedBy,
            facility.CreatedAt,
            facility.UpdatedAt);
    }

    public async Task<FacilityDto> CreateFacilityAsync(CreateFacilityCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var type = IdentifierCodeNormalizer.NormalizeRequired(command.FacilityType ?? command.Name, "Loại cơ sở vật chất");
        var location = IdentifierCodeNormalizer.NormalizeRequired(command.LocationDescription ?? string.Empty, "Vị trí cơ sở vật chất");
        var code = $"CSVC-{type}-{location}";
        EnsureCodeLength(code);

        try
        {
            return await _store.WithAdvisoryLockAsync($"facility:{code}", async token =>
            {
                if (await _store.FacilityCodeExistsAsync(code, token))
                    throw new InvalidOperationException($"Đã tồn tại cơ sở vật chất cùng loại và vị trí với mã '{code}'.");

                var facility = new Facility(code, command.Name, DateTimeOffset.UtcNow, command.FacilityType,
                    command.LocationDescription, command.Description, command.CreatedBy, MasterDataStatus.ACTIVE);
                _store.Add(facility);
                await _store.SaveGeneratedCodeAsync(token);
                return await MapToDtoAsync(facility, token);
            }, cancellationToken);
        }
        catch (IdentifierCodeConflictException)
        {
            throw new InvalidOperationException($"Đã tồn tại cơ sở vật chất cùng loại và vị trí với mã '{code}'.");
        }
    }

    public async Task<FacilityDto> UpdateFacilityAsync(Guid id, UpdateFacilityCommand command, CancellationToken cancellationToken = default)
    {
        var facility = await _store.FacilityAsync(id, true, true, cancellationToken);

        if (facility == null)
        {
            throw new KeyNotFoundException("Không tìm thấy tiện ích.");
        }

        facility.Update(
            command.Name,
            command.FacilityType,
            command.LocationDescription,
            command.Description,
            null,
            command.UpdatedBy,
            DateTimeOffset.UtcNow);

        await _store.SaveAsync(cancellationToken);

        return await MapToDtoAsync(facility, cancellationToken);
    }

    public async Task<FacilityDto> SetFacilityStatusAsync(Guid id, SetFacilityStatusCommand command, CancellationToken cancellationToken = default)
    {
        var facility = await _store.FacilityAsync(id, true, false, cancellationToken);

        if (facility == null)
        {
            throw new KeyNotFoundException("Không tìm thấy tiện ích.");
        }

        facility.SetStatus(command.Status, command.UpdatedBy, DateTimeOffset.UtcNow);

        await _store.SaveAsync(cancellationToken);

        return await MapToDtoAsync(facility, cancellationToken);
    }

    private Task<FacilityDto> MapToDtoAsync(Facility facility, CancellationToken cancellationToken)
    {
        return Task.FromResult(new FacilityDto(
            facility.Id,
            facility.Code,
            facility.Name,
            facility.FacilityType,
            facility.LocationDescription,
            facility.Description,
            facility.Status,
            facility.CreatedBy,
            facility.UpdatedBy,
            facility.CreatedAt,
            facility.UpdatedAt));
    }

    private static void EnsureCodeLength(string code)
    {
        if (code.Length > 512)
            throw new ArgumentException("Mã cơ sở vật chất sau chuẩn hóa vượt quá 512 ký tự.");
    }
}
