using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.Modules.Apartments.Infrastructure;

public sealed record NewApartment(string UnitNumber,int FloorNumber,Guid TypeId,decimal Area,int? Bedrooms,int? Bathrooms,DateOnly? HandoverDate,string? Description,string Status);
public sealed class ApartmentCreationException(int status,string code,string message):Exception(message)
{ public int Status {get;}=status;public string Code {get;}=code; }

// The normal create action and Excel commit use this same validation/staging path.
public sealed class ApartmentCreation(ApartmentsDbContext db,ICurrentBuildingFloorLimit floorLimit,TimeProvider clock)
{
    public static (string Code,string Message)? Validate(string number,int floor,Guid type,decimal area,int? bedrooms,int? bathrooms)
    {
        if(string.IsNullOrWhiteSpace(number)||number.Trim().Length>30)return("APARTMENT_UNIT_NUMBER_REQUIRED","Mã căn hộ là bắt buộc và tối đa 30 ký tự.");
        if(floor<0)return("APARTMENT_FLOOR_NUMBER_INVALID","Tầng phải lớn hơn hoặc bằng 0.");
        if(type==Guid.Empty)return("APARTMENT_UNIT_TYPE_REQUIRED","Loại căn hộ là bắt buộc.");
        if(area<25||area>99999999.99m||decimal.Round(area,2)!=area)return("APARTMENT_USABLE_AREA_INVALID","Diện tích sử dụng phải từ 25 m² trở lên, tối đa 99.999.999,99 m² và không quá 2 chữ số thập phân.");
        if(bedrooms is <0||bathrooms is <0)return("APARTMENT_ROOM_COUNT_INVALID","Số phòng ngủ và phòng tắm phải lớn hơn hoặc bằng 0.");
        return null;
    }
    public async Task<ApartmentUnit> StageAsync(NewApartment input,Guid? actor,CancellationToken ct)
    {
        if(db.Database.CurrentTransaction is null)throw new InvalidOperationException("Apartment creation requires the caller's transaction and floor lock.");
        var maximum=await floorLimit.GetMaximumFloorAsync(ct);
        if(maximum is null or <=0)throw new ApartmentCreationException(400,"BUILDING_NOT_CONFIGURED","Vui lòng thiết lập số tầng chung cư trước khi lưu căn hộ.");
        if(input.FloorNumber>maximum)throw new ApartmentCreationException(400,"APARTMENT_FLOOR_OUT_OF_RANGE",$"Tầng căn hộ không được vượt quá {maximum} tầng của chung cư.");
        if(Validate(input.UnitNumber,input.FloorNumber,input.TypeId,input.Area,input.Bedrooms,input.Bathrooms) is { } error)throw new ApartmentCreationException(400,error.Code,error.Message);
        if(!await db.ApartmentUnitTypes.AnyAsync(x=>x.Id==input.TypeId,ct))throw new ApartmentCreationException(400,"APARTMENT_UNIT_TYPE_NOT_FOUND","Loại căn hộ không tồn tại.");
        if(!Enum.TryParse<MasterDataStatus>(input.Status,true,out var status)||!Enum.IsDefined(status))throw new ApartmentCreationException(400,"APARTMENT_STATUS_INVALID","Trạng thái căn hộ không hợp lệ.");
        if(await db.ApartmentUnits.AnyAsync(x=>x.UnitNumber==input.UnitNumber.Trim(),ct))throw new ApartmentCreationException(409,"APARTMENT_UNIT_NUMBER_DUPLICATE","Mã căn hộ đã tồn tại.");
        var entity=new ApartmentUnit(input.UnitNumber,input.FloorNumber,clock.GetUtcNow(),input.TypeId,input.Area,input.Bedrooms,input.Bathrooms,input.HandoverDate,input.Description,actor);
        if(status==MasterDataStatus.INACTIVE)entity.Deactivate(actor,clock.GetUtcNow());
        db.ApartmentUnits.Add(entity);return entity;
    }
}
