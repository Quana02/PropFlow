using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PropFlow.Shared.Spreadsheets;

public sealed record XlsxColumn(string Title, double Width = 24, string Format = "text", IReadOnlyList<string>? Choices = null);
public sealed record XlsxSheet(string Name, IReadOnlyList<XlsxColumn> Columns, IReadOnlyList<object?[]> Rows,
    string? Title = null, string? ExportedAt = null, string? Filters = null, int InputRows = 0);
public sealed record XlsxRow(int ExcelRow, IReadOnlyList<string> Cells);
public sealed record XlsxInput(IReadOnlyList<XlsxRow> Rows, bool Date1904);
public sealed class XlsxFormatException(string message) : Exception(message);

// Small, bounded tabular codec. No calculations, macros, external links or disk uploads.
public static class XlsxTable
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const int MaximumFileBytes = 2 * 1024 * 1024;
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Relationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static XElement E(string name, params object?[] content) => new(Main + name, content);
    private static XAttribute A(string name, object value) => new(name, value);
    private static string XmlText(string text)
    {
        var result=new StringBuilder(text.Length);
        for(var i=0;i<text.Length;i++)
        {
            if(char.IsHighSurrogate(text[i])&&i+1<text.Length&&char.IsLowSurrogate(text[i+1])){result.Append(text[i]).Append(text[++i]);}
            else if(XmlConvert.IsXmlChar(text[i]))result.Append(text[i]);
        }
        return result.ToString();
    }
    private static string Column(int n) { var value=""; for(n++;n>0;n=(n-1)/26)value=(char)('A'+(n-1)%26)+value;return value; }

    public static byte[] Write(params XlsxSheet[] sheets)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            void Save(string name, XElement element)
            {
                using var stream=zip.CreateEntry(name,CompressionLevel.Fastest).Open();
                using var writer=XmlWriter.Create(stream,new XmlWriterSettings {Encoding=new UTF8Encoding(false)});
                new XDocument(element).Save(writer);
            }
            XNamespace ct="http://schemas.openxmlformats.org/package/2006/content-types";
            XNamespace package="http://schemas.openxmlformats.org/package/2006/relationships";
            XElement Override(string name,string kind)=>new(ct+"Override",A("PartName",name),A("ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml."+kind+"+xml"));
            Save("[Content_Types].xml",new XElement(ct+"Types",
                new XElement(ct+"Default",A("Extension","rels"),A("ContentType","application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(ct+"Default",A("Extension","xml"),A("ContentType","application/xml")),
                Override("/xl/workbook.xml","sheet.main"),Override("/xl/styles.xml","styles"),
                sheets.Select((_,i)=>Override($"/xl/worksheets/sheet{i+1}.xml","worksheet"))));
            XElement Link(string id,string kind,string target)=>new(package+"Relationship",A("Id",id),A("Type",Relationship+"/"+kind),A("Target",target));
            Save("_rels/.rels",new XElement(package+"Relationships",Link("rId1","officeDocument","xl/workbook.xml")));
            Save("xl/workbook.xml",E("workbook",E("bookViews",E("workbookView")),E("sheets",sheets.Select((s,i)=>E("sheet",A("name",s.Name),A("sheetId",i+1),new XAttribute(XName.Get("id",Relationship),"rId"+(i+1)))))));
            Save("xl/_rels/workbook.xml.rels",new XElement(package+"Relationships",sheets.Select((_,i)=>Link("rId"+(i+1),"worksheet",$"worksheets/sheet{i+1}.xml")),Link("styles","styles","styles.xml")));
            XElement Xf(int format,int font=0,int fill=0,int border=1)=>E("xf",A("numFmtId",format),A("fontId",font),A("fillId",fill),A("borderId",border),A("xfId",0),A("applyNumberFormat",1),A("applyFill",1),A("applyFont",1),A("applyBorder",1),A("applyAlignment",1),E("alignment",A("horizontal","left"),A("vertical","center"),A("wrapText",1)));
            Save("xl/styles.xml",E("styleSheet",
                E("numFmts",A("count",1),E("numFmt",A("numFmtId",164),A("formatCode","dd/mm/yyyy"))),
                E("fonts",A("count",3),E("font",E("sz",A("val",11)),E("name",A("val","Calibri"))),E("font",E("b"),E("color",A("rgb","FFFFFFFF")),E("sz",A("val",11)),E("name",A("val","Calibri"))),E("font",E("b"),E("color",A("rgb","FF036980")),E("sz",A("val",18)),E("name",A("val","Calibri")))),
                E("fills",A("count",5),E("fill",E("patternFill",A("patternType","none"))),E("fill",E("patternFill",A("patternType","gray125"))),E("fill",E("patternFill",A("patternType","solid"),E("fgColor",A("rgb","FF0784A0")),E("bgColor",A("indexed",64)))),E("fill",E("patternFill",A("patternType","solid"),E("fgColor",A("rgb","FFF0FAFC")))),E("fill",E("patternFill",A("patternType","solid"),E("fgColor",A("rgb","FF417E8C"))))),
                E("borders",A("count",2),E("border"),E("border",E("bottom",A("style","hair"),E("color",A("rgb","FFD5E5E9"))))),
                E("cellStyleXfs",A("count",1),Xf(0,border:0)),
                E("cellXfs",A("count",12),Xf(0),Xf(49),Xf(49,1,2),Xf(164),Xf(2),Xf(0,fill:3),Xf(49,fill:3),Xf(164,fill:3),Xf(2,fill:3),Xf(49,2,border:0),Xf(49,border:0),Xf(49,1,4)),
                E("cellStyles",A("count",1),E("cellStyle",A("name","Normal"),A("xfId",0),A("builtinId",0)))));
            for(var i=0;i<sheets.Length;i++)
            {
                var sheet=sheets[i];
                var headerRow=sheet.Title is null?1:5;
                double TextHeight(object? value,int column)
                {
                    var width=Math.Max(1,sheet.Columns[Math.Min(column,sheet.Columns.Count-1)].Width*.85);
                    return (value?.ToString()??"").Replace("\r\n","\n").Split('\n').Sum(line=>Math.Max(1,Math.Ceiling(line.Length/width)))*15+8;
                }
                XElement Cell(object? value,int row,int column,bool header=false)
                {
                    var cell=E("c",A("r",Column(column)+row));
                    var alternate=row>headerRow&&(row-headerRow)%2==0;
                    if(value is DateOnly date)cell.Add(A("s",alternate?7:3),E("v",date.ToDateTime(TimeOnly.MinValue).ToOADate().ToString(CultureInfo.InvariantCulture)));
                    else if(value is int or long or decimal or double)cell.Add(A("s",value is decimal or double?(alternate?8:4):(alternate?5:0)),E("v",Convert.ToString(value,CultureInfo.InvariantCulture)));
                    else cell.Add(A("s",header?(sheet.Columns[column].Title.EndsWith('*')?2:11):(row<headerRow?(row==1?9:10):(alternate?6:1))),A("t","inlineStr"),E("is",E("t",new XAttribute(XNamespace.Xml+"space","preserve"),XmlText(value?.ToString()??""))));
                    // Every string is an explicit text cell, including values beginning =,+,-,@.
                    return cell;
                }
                Save($"xl/worksheets/sheet{i+1}.xml",E("worksheet",
                    E("sheetViews",E("sheetView",A("workbookViewId",0),A("showGridLines",0),E("pane",A("ySplit",headerRow),A("topLeftCell","A"+(headerRow+1)),A("activePane","bottomLeft"),A("state","frozen")))),
                    E("cols",sheet.Columns.Select((c,n)=>E("col",A("min",n+1),A("max",n+1),A("width",c.Width),A("customWidth",1),A("style",c.Format switch {"integer"=>0,"decimal"=>4,"date"=>3,_=>1})))),
                    E("sheetData",sheet.Title is null?null:new[]{E("row",A("r",1),A("ht",30),A("customHeight",1),Cell(sheet.Title,1,0)),E("row",A("r",2),A("ht",24),A("customHeight",1),Cell(sheet.ExportedAt,2,0)),E("row",A("r",3),A("ht",45),A("customHeight",1),Cell(sheet.Filters,3,0))},
                        E("row",A("r",headerRow),A("ht",32),A("customHeight",1),sheet.Columns.Select((c,n)=>Cell(c.Title,headerRow,n,true))),sheet.Rows.Select((r,n)=>E("row",A("r",n+headerRow+1),A("ht",Math.Clamp(r.Select((v,c)=>TextHeight(v,c)).DefaultIfEmpty(24).Max(),24,409)),A("customHeight",1),r.Select((v,col)=>Cell(v,n+headerRow+1,col))))),
                    E("autoFilter",A("ref",$"A{headerRow}:{Column(sheet.Columns.Count-1)}{sheet.Rows.Count+headerRow}")),
                    sheet.Title is null?null:E("mergeCells",A("count",3),Enumerable.Range(1,3).Select(r=>E("mergeCell",A("ref",$"A{r}:{Column(sheet.Columns.Count-1)}{r}")))),
                    sheet.InputRows==0?null:E("dataValidations",A("count",sheet.Columns.Count(c=>c.Choices is {Count:>0})),sheet.Columns.Select((c,n)=>(c,n)).Where(x=>x.c.Choices is {Count:>0}).Select(x=>E("dataValidation",A("type","list"),A("allowBlank",0),A("showErrorMessage",1),A("showInputMessage",1),A("errorStyle","stop"),A("errorTitle","Giá trị không hợp lệ"),A("error","Chọn giá trị trong danh sách; backend vẫn kiểm tra khi nhập."),A("promptTitle","Trạng thái căn hộ"),A("prompt","Chọn ACTIVE hoặc INACTIVE."),A("sqref",$"{Column(x.n)}2:{Column(x.n)}{sheet.InputRows+1}"),E("formula1","\""+string.Join(',',x.c.Choices!)+"\""))))));
            }
        }
        return output.ToArray();
    }

    public static XlsxInput Read(byte[] bytes,IReadOnlyList<string> headers,int maximumRows=200,string sheetName="DuLieu",int headerRow=1)
    {
        if(bytes.Length==0||bytes.Length>MaximumFileBytes)throw new XlsxFormatException("File Excel phải có dữ liệu và không vượt quá 2 MB.");
        try
        {
            using var input=new MemoryStream(bytes);using var zip=new ZipArchive(input,ZipArchiveMode.Read);
            if(zip.Entries.Count>128||zip.Entries.Sum(x=>x.Length)>16*1024*1024||zip.Entries.GroupBy(x=>x.FullName,StringComparer.Ordinal).Any(x=>x.Count()>1))throw new XlsxFormatException("File vượt giới hạn giải nén hoặc có cấu trúc trùng lặp.");
            if(zip.Entries.Any(x=>x.FullName.Contains("vbaProject",StringComparison.OrdinalIgnoreCase)||x.FullName.StartsWith("xl/externalLinks/",StringComparison.OrdinalIgnoreCase)||x.FullName.StartsWith("xl/embeddings/",StringComparison.OrdinalIgnoreCase)))throw new XlsxFormatException("Không hỗ trợ macro, liên kết ngoài hoặc đối tượng nhúng.");
            XDocument Xml(string name)
            {
                var entry=zip.GetEntry(name)??throw new XlsxFormatException("File không đúng cấu trúc .xlsx hoặc không đúng mẫu.");
                using var stream=entry.Open();using var reader=XmlReader.Create(stream,new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=16*1024*1024});
                return XDocument.Load(reader);
            }
            if(Xml("[Content_Types].xml").ToString().Contains("macroEnabled",StringComparison.OrdinalIgnoreCase))throw new XlsxFormatException("Không hỗ trợ workbook có macro.");
            foreach(var entry in zip.Entries.Where(x=>x.FullName.EndsWith(".rels",StringComparison.OrdinalIgnoreCase)))
                if(Xml(entry.FullName).Descendants().Any(x=>string.Equals((string?)x.Attribute("TargetMode"),"External",StringComparison.OrdinalIgnoreCase)))
                    throw new XlsxFormatException("Không nhập workbook có liên kết ngoài, kể cả trong sheet hướng dẫn.");
            foreach(var entry in zip.Entries.Where(x=>x.FullName.StartsWith("xl/worksheets/",StringComparison.OrdinalIgnoreCase)&&x.FullName.EndsWith(".xml",StringComparison.OrdinalIgnoreCase)))
                if(Xml(entry.FullName).Descendants(Main+"f").Any())
                    throw new XlsxFormatException("Không nhập công thức Excel, kể cả trong sheet hướng dẫn. Vui lòng dán giá trị thuần.");
            var workbook=Xml("xl/workbook.xml");var date1904=(string?)workbook.Root?.Element(Main+"workbookPr")?.Attribute("date1904") is "1" or "true";
            var sheet=workbook.Descendants(Main+"sheet").SingleOrDefault(x=>(string?)x.Attribute("name")==sheetName)??throw new XlsxFormatException($"Không tìm thấy sheet {sheetName}. Giữ nguyên tên sheet trong file mẫu.");
            var relations=Xml("xl/_rels/workbook.xml.rels").Root!;
            if(relations.Elements().Any(x=>(string?)x.Attribute("TargetMode")=="External"))throw new XlsxFormatException("Không hỗ trợ liên kết workbook ngoài.");
            var relative=(string?)relations.Elements().SingleOrDefault(x=>(string?)x.Attribute("Id")== (string?)sheet.Attribute(XName.Get("id",Relationship)))?.Attribute("Target");
            if(relative is null||relative.Contains("..")||relative.Contains('\\'))throw new XlsxFormatException("Đường dẫn worksheet không hợp lệ.");
            var strings=zip.GetEntry("xl/sharedStrings.xml") is null?[]:Xml("xl/sharedStrings.xml").Root!.Elements(Main+"si").Select(x=>string.Concat(x.Descendants(Main+"t").Select(t=>t.Value))).ToArray();
            if(strings.Length>10000)throw new XlsxFormatException("File có quá nhiều chuỗi dùng chung.");
            var data=Xml(relative.StartsWith('/')?relative.TrimStart('/'):"xl/"+relative);
            if(data.Descendants(Main+"f").Any())throw new XlsxFormatException("Không nhập công thức Excel. Vui lòng dán giá trị thuần.");
            var rows=new List<XlsxRow>();var seen=new HashSet<int>();
            foreach(var row in data.Descendants(Main+"sheetData").Elements(Main+"row"))
            {
                if(!int.TryParse((string?)row.Attribute("r"),out var number)||number<1||!seen.Add(number))throw new XlsxFormatException("Số dòng Excel không hợp lệ hoặc trùng lặp.");
                if(number<headerRow)continue;
                var cells=Enumerable.Repeat("",headers.Count).ToArray();var columns=new HashSet<int>();
                foreach(var cell in row.Elements(Main+"c"))
                {
                    var address=(string?)cell.Attribute("r")??"";var column=0;var n=0;
                    while(n<address.Length&&address[n] is >= 'A' and <= 'Z'){column=checked(column*26+address[n]-'A'+1);n++;}
                    if(column<1||!int.TryParse(address[n..],out var cellRow)||cellRow!=number||!columns.Add(column))throw new XlsxFormatException("Địa chỉ ô Excel không hợp lệ.");
                    var value=(string?)cell.Element(Main+"v")??"";var kind=(string?)cell.Attribute("t");
                    if(kind=="s"){if(!int.TryParse(value,out var index)||index<0||index>=strings.Length)throw new XlsxFormatException("Chuỗi Excel không hợp lệ.");value=strings[index];}
                    else if(kind=="inlineStr")value=string.Concat(cell.Descendants(Main+"t").Select(x=>x.Value));
                    else if(kind is "e" or "b")throw new XlsxFormatException("Không nhập ô lỗi hoặc giá trị boolean.");
                    if(value.Length>2000)throw new XlsxFormatException("Một ô vượt quá 2.000 ký tự.");
                    if(column>headers.Count){if(value.Length>0)throw new XlsxFormatException("Không thêm cột ngoài file mẫu.");continue;}
                    cells[column-1]=value;
                }
                if(number==headerRow){if(!cells.Select(x=>x.Trim()).SequenceEqual(headers))throw new XlsxFormatException("Tên hoặc thứ tự cột không đúng file mẫu.");}
                else if(cells.Any(x=>!string.IsNullOrWhiteSpace(x)))rows.Add(new(number,cells));
                if(rows.Count>maximumRows)throw new XlsxFormatException($"Mỗi file chỉ hỗ trợ tối đa {maximumRows} dòng dữ liệu.");
            }
            if(!seen.Contains(headerRow)||rows.Count==0)throw new XlsxFormatException("File chưa có dòng dữ liệu để nhập.");
            return new(rows.OrderBy(x=>x.ExcelRow).ToArray(),date1904);
        }
        catch(Exception error) when(error is InvalidDataException or XmlException or OverflowException or ArgumentException or InvalidOperationException)
        {throw new XlsxFormatException("File .xlsx bị hỏng hoặc không đúng cấu trúc mẫu.");}
    }
}
