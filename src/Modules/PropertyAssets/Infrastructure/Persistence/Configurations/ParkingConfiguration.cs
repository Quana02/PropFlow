using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.PropertyAssets.Domain.Parking;

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.Configurations;

public sealed class ParkingAreaConfiguration:IEntityTypeConfiguration<ParkingArea>
{
    public void Configure(EntityTypeBuilder<ParkingArea> b)
    {
        b.ToTable("parking_areas");b.HasKey(x=>x.Id);b.Property(x=>x.Name).HasMaxLength(120).IsRequired();
        b.Property(x=>x.Version).IsConcurrencyToken();b.Property(x=>x.DraftJson).HasColumnType("jsonb");b.Property(x=>x.PublishedJson).HasColumnType("jsonb");
        b.Ignore(x=>x.Draft);b.Ignore(x=>x.Published);
        b.HasMany(x=>x.Spaces).WithOne().HasForeignKey(x=>x.AreaId).OnDelete(DeleteBehavior.Restrict);
        Names.Apply(b);
    }
}
public sealed class ParkingSpaceConfiguration:IEntityTypeConfiguration<ParkingSpace>
{
    public void Configure(EntityTypeBuilder<ParkingSpace> b)
    {
        b.ToTable("parking_spaces",x=>x.HasCheckConstraint("ck_parking_space_status","status IN ('AVAILABLE','IN_USE','MAINTENANCE')"));
        b.HasKey(x=>x.Id);b.Property(x=>x.Id).ValueGeneratedNever();b.HasIndex(x=>new{x.AreaId,x.Archived});b.Property(x=>x.Status).HasMaxLength(20).IsRequired();Names.Apply(b);
    }
}
public sealed class ParkingSourceConfiguration:IEntityTypeConfiguration<ParkingSource>
{
    public void Configure(EntityTypeBuilder<ParkingSource> b)
    {
        b.ToTable("parking_sources",x=>{
            x.HasCheckConstraint("ck_parking_source_size","octet_length(content) BETWEEN 1 AND 8388608");
            x.HasCheckConstraint("ck_parking_source_type","content_type IN ('image/png','image/jpeg','application/pdf')");
            x.HasCheckConstraint("ck_parking_source_page","page BETWEEN 1 AND 1000");});
        b.HasKey(x=>x.Id);b.HasOne<ParkingArea>().WithMany().HasForeignKey(x=>x.AreaId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x=>x.FileName).HasMaxLength(180);b.Property(x=>x.ContentType).HasMaxLength(40);Names.Apply(b);
    }
}
public sealed class ParkingActionConfiguration:IEntityTypeConfiguration<ParkingAction>
{
    public void Configure(EntityTypeBuilder<ParkingAction> b)
    {
        b.ToTable("parking_actions");b.HasKey(x=>x.Id);b.HasIndex(x=>new{x.AreaId,x.At});
        b.HasOne<ParkingArea>().WithMany().HasForeignKey(x=>x.AreaId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x=>x.Action).HasMaxLength(40);b.Property(x=>x.Note).HasMaxLength(500);Names.Apply(b);
    }
}
public sealed class ParkingRevisionConfiguration:IEntityTypeConfiguration<ParkingRevision>
{
    public void Configure(EntityTypeBuilder<ParkingRevision> b)
    {
        b.ToTable("parking_revisions");b.HasKey(x=>x.Id);b.HasIndex(x=>new{x.AreaId,x.PublishedAt});
        b.HasOne<ParkingArea>().WithMany().HasForeignKey(x=>x.AreaId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x=>x.DrawingJson).HasColumnType("jsonb");Names.Apply(b);
    }
}
internal static class Names
{
    public static void Apply<T>(EntityTypeBuilder<T> b) where T:class
    {foreach(var property in b.Metadata.GetProperties()) property.SetColumnName(System.Text.RegularExpressions.Regex.Replace(property.Name,"([a-z0-9])([A-Z])","$1_$2").ToLowerInvariant());}
}
