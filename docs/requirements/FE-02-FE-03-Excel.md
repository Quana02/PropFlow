# FE-02 / FE-03 — Excel directory extension

Phạm vi owner duyệt ngày 24/11/2026: không thay đổi onboarding cư dân, quan hệ sở hữu/cư trú, accounts, tài chính hoặc quyền hiện hữu. Đây là extension kỹ thuật của danh sách FE-02/FE-03 và luồng tạo căn hộ FE-03, không thêm business workflow hoặc actor mới.

## FE-02

- Export: `GET /api/v1/residents/excel/export`, policy `residents.read`, cùng các bộ lọc của danh sách; lấy toàn bộ kết quả, tối đa 10.000 dòng, không xuất một phần khi vượt giới hạn.
- Cột chung: Mã cư dân, Họ và tên, Trạng thái, Tài khoản (chỉ trạng thái đã liên kết), Căn hộ sở hữu, Căn hộ đang cư trú.
- MANAGER/STAFF có thêm Số điện thoại và Email liên hệ; ACCOUNTANT không có hai cột này và không được tìm bằng chúng. Không xuất giấy tờ, ngày sinh, UserId hoặc dữ liệu tài khoản nội bộ ngoài projection danh sách.
- Import **NOT IN SCOPE**: owner hủy kế hoạch ngày 25/11/2026. `CreateResidentRequest` bắt buộc căn hộ và `OWNER_ONLY`, `RESIDENT_ONLY` hoặc `OWNER_AND_RESIDENT`; không có tạo hồ sơ độc lập. Không tạo template/endpoint/nút import cư dân giả.
- Không phát triển Import cư dân, không tách lõi onboarding và không tạo template/API/UI Import cư dân. Giữ nguyên workflow thủ công.

## FE-03

- Cập nhật được duyệt 25/11/2026: Trạng thái quản lý, Tầng và Loại căn hộ hỗ trợ chọn nhiều thật trên UI, API và Export. Trong cùng trường dùng OR, giữa các trường dùng AND. Tham số mới lặp `statuses`, `floorNumbers`, `apartmentUnitTypeIds`; tham số scalar cũ vẫn dùng được và hợp với plural cùng trường thành tập OR không trùng. Mỗi trường tối đa 100 giá trị. `GET /api/v1/apartments/floors` dùng quyền đọc căn hộ, trả tầng từ toàn bộ danh sách kể cả căn ngừng quản lý. Sở hữu/cư trú giữ single-select và diện tích giữ khoảng. Không thay đổi schema, quyền hay workflow.
- Export: `GET /api/v1/apartments/excel/export`, policy `apartments.read`, cùng search/status/floorNumber/apartmentUnitTypeId/occupancy của danh sách và cùng thứ tự tầng/mã. Tối đa 10.000 kết quả; vượt giới hạn báo lỗi, không cắt file âm thầm.
- Export gồm Mã căn hộ, Tầng, Loại căn hộ, Diện tích (m²), Chủ sở hữu, Cư dân đang cư trú, Trạng thái, Tình trạng cư trú hiện tại (owner duyệt bổ sung ngày 25/11/2026). Hai cột người chỉ xuất họ tên theo quan hệ hiện hành từ cùng API danh sách, nhiều tên xuống dòng; không xuất liên hệ, giấy tờ hoặc lịch sử. Giao diện danh sách căn hộ cũng tách riêng hai cột này.
- Template: `GET /api/v1/apartments/excel/template`, policy `apartments.manage` (MANAGER như create hiện tại).
- Preview: `POST /api/v1/apartments/excel/preview`, multipart field `file`, cùng manage policy. `.xlsx` ≤2 MB, ≤200 dòng dữ liệu; cấu trúc giải nén ≤16 MB/128 entries. XML cấm DTD; từ chối formula, macro, external workbook links, embedded objects và cột thừa.
- Commit: `POST /api/v1/apartments/excel/commit`, JSON `{ "ticket": "..." }`, cùng manage policy. Không nhận lại rows/UUID từ client.
- Template giữ sheet `Danh sách căn hộ` và cột đúng thứ tự: Mã căn hộ *, Tầng *, Loại căn hộ *, Diện tích (m²) *, Số phòng ngủ, Số phòng tắm, Ngày bàn giao, Mô tả, Trạng thái *. Sheet `Hướng dẫn` có hướng dẫn và tên loại căn hộ hiện có; không có PII thật làm ví dụ.
- Loại căn hộ được nhập bằng tên, tra duy nhất không phân biệt hoa/thường; không tự tạo loại. Mã căn hộ trim và so trùng chính xác như create hiện tại; không upsert hoặc merge. Ngày dd/MM/yyyy, yyyy-MM-dd hoặc serial date Excel; số thập phân có dấu chấm/phẩy, không phân cách hàng nghìn. ACTIVE/INACTIVE; diện tích và giới hạn tầng/rooms dùng đúng validation create.
- Preview không ghi DB. Ticket mã hóa/xác thực bởi Data Protection, bind actor và logical session (`sid`), hạn 10 phút, không lưu file gốc lên đĩa. Dòng được phân loại thành hợp lệ / trùng / lỗi không trùng; dòng trùng vẫn liệt kê mọi lỗi khác trong bảng.
- Commit kiểm lại tất cả dưới transaction/advisory lock `71241006` dùng chung create/update căn hộ; dùng chung `ApartmentCreation` để stage, một SaveChanges cùng audit trong transaction. Lỗi/trùng/stale đều không tạo một phần. Replay/double-submit sau lần thành công trả 409 do mã đã tồn tại; không tạo thêm căn hộ/event/audit. Ticket khác actor hoặc khác phiên đăng nhập trả 403, sửa/hết hạn trả 409.
- Không giữ transaction trong khi người dùng xem trước. Không ownership/residency/accounts/email/OTP/notifications từ import.

## Files và vận hành

XLSX của ứng dụng dùng `ZipArchive`/XML có sẵn trong .NET 8, không thêm package. Template có sheet `Hướng dẫn` trước `Danh sách căn hộ`; dấu `*` và header cyan phân biệt cột bắt buộc, cột tùy chọn dùng xanh dịu hơn. Dropdown ACTIVE/INACTIVE áp dụng I2:I201; kiểu mặc định của cột mã là text, tầng/phòng là số nguyên, diện tích `0.00`, ngày `dd/mm/yyyy`. Dropdown không thay thế validation backend. Chỉ sheet dữ liệu đúng tên/cấu trúc được đọc.

Export có tiêu đề PropFlow ở dòng 1, thời điểm UTC/số kết quả ở dòng 2, bộ lọc ở dòng 3, header dòng 5 và dữ liệu từ dòng 6. Header xanh/cyan, chữ trắng, hàng xen cyan nhạt, border mảnh, dòng tự tính chiều cao theo nội dung; AutoFilter tại header và freeze tới dòng dữ liệu đầu. Template freeze dòng 1. Chuỗi là cell text/format `@`, không sinh `<f>` nên giữ số 0 đầu, mã/điện thoại dài và không thực thi formula injection. Download có filename server và no-store; CORS expose duy nhất Content-Disposition để Web khác origin đọc được tên file.

Kiểm thử file dùng openpyxl 3.1.5 có sẵn trong runtime của môi trường kiểm thử (không thêm dependency ứng dụng) để tạo input từ template tải thật và đọc lại output thực. Renderer độc lập kiểm tra hình thức workbook. Kiểm tra mở trực tiếp bằng Microsoft Excel được ghi riêng **UNVERIFIED** khi COM bị chặn/bận; không suy ra đã nghiệm thu Microsoft Excel chỉ từ HTTP 200 hoặc thư viện đọc được.

Không migration mới. Không apply trên DB local chính/demo/AWS. Kiểm thử chỉ dùng fixture PostgreSQL có guard và tự dọn; E2E cần API/Web publish trong artifact riêng. FE-15.8 và các workflow ngoài phạm vi giữ nguyên.
# Chuẩn drawer dùng chung FE-02/FE-03 — 25/11/2026

Cả hai màn hình chọn `PropFlowFilterPopover Variant="Directory"`. Các caller khác giữ variant mặc định. Chuẩn chung: width416px giới hạn theo viewport, header66px/footer77px, control44px, gap trường16px/nhãn8px, radius control11px/drawer18px; padding chung và bù chiều rộng scrollbar để căn lề field với nút footer. FE-03 có ba multi-select theo cập nhật trên, khoảng diện tích min/max thành cặp hai cột cân đối. FE-02 giữ bốn multi-select thật. Hai màn hình tái sử dụng `Shared/Forms/PropFlowFilterMultiSelect`.

Menu single/multi dùng cùng hàm đặt vị trí trong module filter: top layer, giới hạn trong body và visual viewport để header/footer truy cập được. Theme dùng token PropFlow, scrollbar trung tính, Tab/Escape/focus behavior chung trong variant Directory. Icon của drawer dùng SVG/CSS cục bộ, không phụ thuộc font icon từ mạng. API, filter contract, query, applied/draft semantics, pagination và Excel processing giữ nguyên trong đợt UI thống nhất này.

# FE-02 multi-select và drawer — 25/11/2026

Người dùng đã duyệt mở rộng API và Export cho multi-select, thay giới hạn UI-only ban đầu. FE-02 có bốn trường dưới đây; FE-03 được mở rộng thêm theo mục cập nhật FE-03 phía trên. Giao diện hai trang đã thống nhất.

- Bốn trường chọn nhiều: trạng thái cư dân, căn hộ, tầng, quan hệ với căn hộ. Liên kết tài khoản, hình thức cư trú và vai trò hộ giữ single-select.
- GET `/api/v1/residents` và `/api/v1/residents/excel/export` nhận thêm các query lặp `statuses`, `apartmentUnitIds`, `floorNumbers`, `relationshipKinds`. Ví dụ `statuses=ACTIVE&statuses=INACTIVE&floorNumbers=2&floorNumbers=3`.
- OR trong cùng trường, AND giữa các trường. Căn hộ và tầng xác định cùng một phạm vi căn hộ giao nhau; quan hệ được xét trong phạm vi đó theo nghĩa hiện hành. Người có nhiều liên kết chỉ xuất hiện một lần; total và pagination tính trên cư dân, không tính số liên kết.
- Tham số scalar cũ (`status`, `apartmentUnitId`, `floorNumber`, `relationshipKind`) vẫn hoạt động như trước. Nếu gửi cả scalar và plural cùng trường, hợp các giá trị theo OR và loại trùng. Giá trị không hợp lệ bị từ chối, không âm thầm bỏ qua. Tối đa 100 lựa chọn mỗi trường để giới hạn độ dài GET; UI hiện tìm kiếm/xóa chọn, chỉ hiện Chọn tất cả nếu danh sách không vượt 100 mục. Không chọn mục nào nghĩa là không giới hạn trường đó.
- Snapshot đã áp dụng dùng chung cho list, pagination và Export. Chọn trong menu không tự gọi API. Reset chỉ sửa draft; cần Áp dụng. Đóng/mở lại hủy thay đổi chưa áp dụng, giống behavior trước.
- Drawer FE-02 opt-in 416px, header/footer cố định, controls/nút 44px, body cuộn riêng. Menu multi-select dùng top layer và vị trí theo visual viewport; có checkbox, đếm lựa chọn, tìm căn hộ, điều khiển bàn phím. Không đổi tạo/sửa, ownership/residency, schema hoặc quyền.

# Bổ sung danh bạ và bộ lọc — 25/11/2026

- FE-03 tìm theo mã căn hộ, loại căn hộ, tên/mã chủ sở hữu và người cư trú hiện hành. Bộ lọc gồm tầng, loại, trạng thái quản lý, có/chưa có chủ sở hữu, có/chưa có cư dân và khoảng diện tích bao gồm hai đầu.
- FE-02 tìm theo mã/tên và liên hệ trong phạm vi quyền. Bộ lọc gồm trạng thái, căn hộ, tầng, quan hệ hiện hành, loại cư trú, vai trò hộ và liên kết tài khoản. ACCOUNTANT không được tìm hoặc xuất liên hệ bị hạn chế.
- Từ khóa cách nhau bằng khoảng trắng kết hợp AND, không phân biệt hoa/thường; không cam kết tìm không dấu. Tối đa 200 ký tự/12 từ. Các bộ lọc kết hợp AND; bỏ trống nghĩa là không giới hạn.
- Quan hệ của FE-02 được xét trong phạm vi căn hộ/tầng đã chọn. Loại cư trú và vai trò hộ phải khớp trên cùng quan hệ cư trú hiện hành. Quan hệ kết thúc/tương lai bị loại. Bộ lọc quan hệ danh bạ dùng enum đọc riêng, không thay workflow tạo cư dân.
- UI kiểm số tầng, diện tích không âm và khoảng hợp lệ; backend kiểm lại enum, kiểu và giới hạn. Hủy không áp dụng bản nháp; đặt lại xóa đầy đủ tiêu chí. Export dùng từ khóa/bộ lọc đã áp dụng thành công, không dùng nội dung chưa gửi trong ô tìm kiếm.
- Excel căn hộ có hai cột riêng Chủ sở hữu/Cư dân đang cư trú; tên/mã người ở, trạng thái và nhãn trống đồng bộ Web. Thời điểm xuất theo múi giờ tòa nhà. Dữ liệu cư trú thiếu/không nhất quán phải báo chưa tải được và khóa export, không suy diễn thành căn hộ trống.
- Không thay schema/dữ liệu nghiệp vụ. FE-02 Import vẫn ngoài phạm vi theo quyết định đã chốt.

