using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PropFlow.Modules.Apartments.Contracts;

public sealed record ApartmentExcelIssue(string Column,string Message,bool Duplicate=false);
public sealed record ApartmentExcelPreviewRow(int ExcelRow,IReadOnlyList<string> Values,IReadOnlyList<ApartmentExcelIssue> Issues);
public sealed record ApartmentExcelPreview(int TotalRows,int ValidRows,int ErrorRows,int DuplicateRows,IReadOnlyList<string> Columns,IReadOnlyList<ApartmentExcelPreviewRow> Rows,string? Ticket,DateTimeOffset? ExpiresAt);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CommitApartmentExcel
{
    [Required,MaxLength(2*1024*1024)] public string Ticket {get;set;}="";
}
public sealed record ApartmentExcelResult(int CreatedCount);
