"""Independent XLSX fixture writer/reader; never starts Excel or touches user workbooks."""
import json
import sys
import warnings
from datetime import datetime
from pathlib import Path

import openpyxl

mode, filename, output, kind = sys.argv[1:]
root, source = Path(output).resolve(), Path(filename).resolve()
assert source.is_relative_to(root) and root.name.startswith("run-"), "Owned fixture files only"
warnings.simplefilter("error")

def load():
    return openpyxl.load_workbook(source, data_only=False)

def report(name, value):
    (root / name).write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")

book = load()
if mode == "prepare":
    assert book.sheetnames == ["Hướng dẫn", "Danh sách căn hộ"]
    sheet = book["Danh sách căn hộ"]
    assert sheet.cell(1, 1).value == "Mã căn hộ *"
    assert sheet.freeze_panes == "A2" and sheet.auto_filter.ref == "A1:I1"
    assert sheet.column_dimensions["A"].number_format == "@"
    assert sheet.column_dimensions["D"].number_format == "0.00"
    assert sheet.column_dimensions["G"].number_format == "dd/mm/yyyy"
    assert any(v.type == "list" and v.formula1 == '"ACTIVE,INACTIVE"' and "I2:I201" in str(v.sqref)
               for v in sheet.data_validations.dataValidation)
    for index in range(1, 26):
        values = [f"XLS-E2E-{index:04}", 2, "Loại Excel E2E", 75.5, 2, 2,
                  datetime(2026, 11, 24), "Dữ liệu tổng hợp kiểm thử Excel", "ACTIVE"]
        for column, value in enumerate(values, 1):
            cell = sheet.cell(index + 1, column, value)
            cell.number_format = "dd/mm/yyyy" if column == 7 else "0.00" if column == 4 else "0" if column in (2, 5, 6) else "@"
    book.save(root / "input-valid.xlsx")
    sheet.delete_rows(4, 23)
    sheet.cell(3, 1, sheet.cell(2, 1).value)
    sheet.cell(2, 2, 999)
    sheet.cell(2, 4, "not-area")
    book.save(root / "input-invalid.xlsx")
    (root / "input-empty.xlsx").write_bytes(b"")
    (root / "input-broken.xlsx").write_bytes(b"not a ZIP workbook")
    (root / "input-oversized.xlsx").write_bytes(bytes(2 * 1024 * 1024 + 1))
    report("library-prepare.json", {"reader": "openpyxl", "version": openpyxl.__version__,
           "templateRead": True, "rowsPrepared": 25, "dropdownVerified": True,
           "nativeExcel": False})
elif kind == "security":
    sheet = book["Định dạng kiểm thử"]
    assert sheet.max_row == 9
    for index, prefix in enumerate(["=1+1", "+SUM(1,2)", "-1+2", "@SUM(A1)"], 6):
        assert sheet.cell(index, 1).value == "00001234567890123456"
        assert sheet.cell(index, 2).value == "00123456789012345678"
        assert sheet.cell(index, 1).number_format == sheet.cell(index, 2).number_format == "@"
        assert sheet.cell(index, 3).value == datetime(2026, 11, 24)
        assert sheet.cell(index, 3).number_format == "dd/mm/yyyy"
        assert sheet.cell(index, 4).value == 75.5 and sheet.cell(index, 4).number_format == "0.00"
        assert sheet.cell(index, 5).value == prefix and sheet.cell(index, 5).data_type == "s"
    assert all(c.data_type != "f" for row in sheet for c in row)
    report("library-security-inspection.json", {"reader": "openpyxl", "leadingZeros": True,
           "longPhoneText": True, "dateAndDecimalTyped": True, "safeFormulaPrefixes": 4,
           "nativeExcel": False})
else:
    name = "Danh sách căn hộ" if kind == "apartments" else "Danh sách cư dân"
    assert book.sheetnames == [name]
    sheet = book[name]
    rows, columns = sheet.max_row - 5, sheet.max_column
    assert rows == (25 if kind == "apartments" else 1 if kind == "related" else 101)
    assert columns == (6 if kind == "redacted" else 8)
    assert sheet.cell(1, 1).value.startswith("PropFlow — DANH SÁCH")
    assert "UTC" in sheet.cell(2, 1).value and "Bộ lọc:" in sheet.cell(3, 1).value
    if kind == "related":
        for value in ["OTHER-E2E", "XLS-E2E-0001", "Đang hoạt động", "Ngừng hoạt động", "Có sở hữu căn hộ", "Đang cư trú", "1, 2"]:
            assert value in sheet.cell(3, 1).value
    else:
        assert "XLS-E2E-" in sheet.cell(3, 1).value if kind == "apartments" else "Excel Nguyễn" in sheet.cell(3, 1).value
    assert sheet.freeze_panes == "A6" and sheet.auto_filter.ref == f"A5:{openpyxl.utils.get_column_letter(columns)}{sheet.max_row}"
    code = sheet.cell(6, 1)
    assert code.value == ("XLS-E2E-0001" if kind == "apartments" else "OTHER-PERSON" if kind == "related" else "EXCEL-0000")
    assert code.data_type == "s" and code.number_format == "@"
    assert all(c.alignment.horizontal == "left" and c.alignment.vertical == "center" for row in sheet for c in row if c.value is not None)
    if rows > 1:
        assert sheet.cell(6, 1).fill.fgColor.rgb != sheet.cell(7, 1).fill.fgColor.rgb
    assert sheet.cell(5, 1).font.bold and sheet.cell(5, 1).fill.fgColor.rgb in ("FF0784A0", "FF417E8C")
    assert sheet.cell(6, 1).border.bottom.style == "hair"
    if kind == "apartments":
        assert sheet.cell(6, 4).value == 75.5 and sheet.cell(6, 4).number_format == "0.00"
        for value in ["Loại Excel E2E phụ", "Loại Excel E2E", "Đang quản lý", "Ngừng quản lý", "1, 2"]:
            assert value in sheet.cell(3, 1).value, "Apartment export metadata lost a multi-select choice"
    elif kind != "related":
        assert "Nguyễn" in sheet.cell(6, 2).value
    if kind == "residents":
        assert sheet.cell(6, 7).value == "0900000000" and sheet.cell(6, 7).data_type == "s"
    if kind == "redacted":
        assert all("@example.invalid" not in str(c.value) and "0900000" not in str(c.value)
                   for row in sheet for c in row)
    assert all(c.data_type != "f" for row in sheet for c in row)
    oracle_name = "expected-apartments.json" if kind == "apartments" else "expected-residents.json"
    expected = [["OTHER-PERSON", "Không thuộc bộ lọc", "Đang hoạt động", "Chưa có tài khoản", "OTHER-E2E", "OTHER-E2E", "", ""]] if kind == "related" else json.loads((root / oracle_name).read_text(encoding="utf-8"))
    if kind == "redacted":
        expected = [row[:6] for row in expected]
    actual = [[sheet.cell(row, col).value or "" for col in range(1, columns + 1)]
              for row in range(6, sheet.max_row + 1)]
    assert actual == expected, "Workbook values/order differ from PostgreSQL oracle"
    report(f"library-{kind}-inspection.json", {"reader": "openpyxl", "version": openpyxl.__version__,
           "rows": rows, "columns": columns, "identifierText": True,
           "formulaCells": 0, "reportFormatVerified": True, "allRowsMatchPostgreSQL": True, "nativeExcel": False})
book.close()
