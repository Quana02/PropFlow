# FE-03 Thông số mặc định của loại căn hộ

Triển khai ngày 02/11/2026 theo yêu cầu bổ sung của người dùng. Yêu cầu này mở lại quyền Manager tạo/sửa loại căn hộ, thay hành vi fixed catalog của source trước đó. Đặc tả FE-03 mục 16 vốn đã cho phép CRUD loại; database thực tế ban đầu chỉ có id/name/created_at/updated_at, chưa có defaults.

## Dữ liệu và validation

`ApartmentUnitType` bổ sung ba giá trị nullable: `DefaultUsableAreaM2` (`numeric(10,2)`), `DefaultBedroomCount`, `DefaultBathroomCount`. Migration không backfill và không thay đổi các căn hộ hay loại đã tồn tại. Null nghĩa là chưa thiết lập, khác 0 phòng ngủ của Studio.

Giữ giới hạn diện tích FE-03 đang có trong source: nếu nhập defaults thì từ 25 đến 99.999.999,99 m², tối đa hai chữ số thập phân; số phòng nguyên không âm. Không đổi validation của thông số thực tế căn hộ. Ràng buộc PostgreSQL bảo vệ diện tích tối thiểu và số phòng không âm; API kiểm tra precision trước khi ghi. Tên loại vẫn unique theo normalized lower/trim; loại đã được căn hộ sử dụng không thể xóa.

`apartment_units` giữ nguyên `apartment_unit_type_id`, `usable_area_m2`, `bedroom_count`, `bathroom_count`. Sửa defaults của loại không ghi vào các căn hộ.

## API

Mở rộng endpoint có sẵn `/api/v1/apartment-unit-types`, không thêm route:

- GET: danh sách gồm id, name và ba default fields.
- POST: tên và ba default fields, trả 201.
- PUT `/{id}`: tên và ba default fields, trả 200; null xóa giá trị mặc định tương ứng.
- DELETE `/{id}`: chỉ loại chưa dùng, trả 204 hoặc 409 nếu đang dùng.

Tên JSON mới: `defaultUsableAreaM2`, `defaultBedroomCount`, `defaultBathroomCount`. Cả ba optional/nullable; request cũ chỉ có name vẫn hợp lệ. Các response được Swagger sinh trực tiếp từ DTO; test Swagger kiểm tra đủ ba trường.

Read policies của Staff/Accountant/Manager và manage policy chỉ dành cho Manager được giữ nguyên. Admin không được thêm business privilege.

## Giao diện

Nút Loại căn hộ mở modal `ApartmentUnitTypeManager`, có danh sách thông số, thêm, sửa và xóa với confirmation. Có loading, empty/error/retry, validation, chống double-submit và toast. Giao diện tiếng Việt, dùng màu design system và shared confirmation/toast.

Khi bấm Thêm/Sửa, form thay thế danh sách ngay trong modal và focus vào ô tên sau khi render. Hủy hoặc lưu thành công quay lại danh sách. Cách này sửa lỗi form từng được nối sau danh sách dài, nằm ngoài vùng nhìn thấy khiến nút Sửa trông như không hoạt động. Đã kiểm tra form sửa loại đầu/cuối danh sách, thêm mới, tự focus, hủy và lưu qua API.

Form Thêm căn hộ mới giữ các nhóm trường và vị trí cũ. Khi chọn loại, `ApartmentDefaultSelection` chỉ copy ba thông số thực sự có giá trị từ response đã tải. Không hard-code số liệu theo tên loại, không gọi API riêng mỗi lần chọn và không sửa mã/tầng/ngày bàn giao/mô tả.

Manager có thể sửa giá trị tự điền. Khi đổi loại, nếu một default mới sẽ ghi đè thay đổi thủ công, shared confirmation xuất hiện. Hủy giữ loại cũ và toàn bộ form; đồng ý chỉ áp các defaults có dữ liệu. Null không biến thành 0 và không xóa thông số đang nhập. Mỗi lần mở form tạo tải lại danh sách để nhận defaults mới nhất. Mở hoặc đổi loại trong form chỉnh sửa căn hộ không tự ghi đè thông số thực tế đã lưu.

## Migration

`20261102160128_AddApartmentUnitTypeDefaults` (kèm Designer và model snapshot) chỉ thêm ba cột nullable cùng ba check constraints. Đã áp dụng vào database local `propflow` sau khi xác minh host/database và migration baseline. So sánh dữ liệu trước/sau xác nhận toàn bộ apartment rows và thông tin loại cũ giữ nguyên.

Máy khác: cấu hình `ConnectionStrings__PropFlowDatabase` qua môi trường/secret provider cho EF design-time factory rồi chạy tại root:

```powershell
dotnet ef database update 20261102160128_AddApartmentUnitTypeDefaults --project src/Modules/Apartments
```

Không ghi connection string hoặc credentials vào source. Không tự động migrate database khác khi ứng dụng khởi động.

## File chính

- Backend: `ApartmentUnitType.cs`, `ApartmentUnitTypeConfiguration.cs`, `ApartmentUnitTypeService.cs`, `ApartmentUnitTypesController.cs`, DI trong `PropFlow.Api/Program.cs`.
- Frontend: `ApartmentModels.cs`, `ApartmentDefaultSelection.cs`, `ApartmentManagement.razor` và code-behind; component/CSS mới `ApartmentUnitTypeManager`.
- Database: migration/Designer/snapshot của Apartments; `PropFlow.dbml` và ownership mapping.
- Tests: `ApartmentTypeDefaultsTests`, mở rộng `ApartmentManagementHttpTests` và `SwaggerDocumentTests`. `AuthDatabaseFixture` seed current Building trong database kiểm thử để thỏa prerequisite giới hạn tầng hiện hành.

## Bộ loại căn hộ tham khảo cho chung cư thương mại

Ngày nghiên cứu: 02/11/2026. Mục đích: thiết lập khoảng năm loại căn hộ và thông số tự điền cho dữ liệu local PropFlow theo yêu cầu người dùng.

Chọn Studio, 1 phòng ngủ, 1 phòng ngủ + 1 đa năng, 2 phòng ngủ và 3 phòng ngủ. Đây là bộ đại diện thực dụng từ cơ cấu sản phẩm của các dự án chung cư thương mại, không phải bảng xếp hạng năm loại có thị phần cao nhất toàn quốc. Các nguồn được đọc không cung cấp thống kê toàn thị trường để xác lập thứ hạng đó.

### Bộ thông số chốt cho setup

| Tên loại | Diện tích sử dụng mặc định | Phòng ngủ | Phòng tắm |
| --- | ---: | ---: | ---: |
| Căn hộ Studio | 35 m² | 0 | 1 |
| Căn hộ 1 phòng ngủ | 45 m² | 1 | 1 |
| Căn hộ 1 phòng ngủ +1 | 50 m² | 1 | 1 |
| Căn hộ 2 phòng ngủ | 75 m² | 2 | 2 |
| Căn hộ 3 phòng ngủ | 95 m² | 3 | 2 |

Các diện tích trên là số mẫu làm tròn do PropFlow lựa chọn để giảm nhập liệu. Chúng không phải diện tích trung bình thị trường, định mức bắt buộc hay số đo của căn hộ đang tồn tại. Manager vẫn sửa theo diện tích thực tế khi tạo căn hộ; chỉnh defaults không cập nhật hàng loạt các căn đã lưu.

### Cơ sở từ nguồn của chủ đầu tư

- **Studio và 1PN+1:** Vinhomes công bố tại S7.05 Grand Park, Studio có khoảng 30,3–39,4 m² và một WC; 1PN+1 có khoảng 46,1–51,8 m², một phòng ngủ riêng cùng một WC. Vì vậy 35 m² và 50 m² là các giá trị mẫu nằm trong dải dự án này. [Vinhomes Market — S7.05 Grand Park](https://market.vinhomes.vn/blog/toa-s705-vinhomes-grand-park).
- **1PN và 3PN:** Tài liệu Smart City mô tả 1PN khoảng 34–47 m² với một WC; 3PN khoảng 75–98 m² với hai WC. Các default 45 m² và 95 m² được chọn trong các dải này. [Vinhomes Market — Bản đồ Vinhomes Smart City](https://market.vinhomes.vn/blog/ban-do-vinhomes-smart-city).
- **2PN:** Bài mặt bằng Symphony liệt kê căn 2PN S1 khoảng 56,9–75,2 m² và S2 khoảng 55,8–77,7 m². Bài giới thiệu Symphony xác nhận cấu hình 2PN có hai phòng vệ sinh. Default 75 m²/2PN/2WC là lựa chọn tham khảo cho nhóm căn gia đình, không phải tuyên bố một căn mẫu cụ thể có chính xác bộ số đó. [Vinhomes — Mặt bằng Symphony](https://vinhomes.vn/vi/kham-pha-chi-tiet-mat-bang-vinhomes-symphony), [Vinhomes — Giới thiệu căn hộ Symphony](https://vinhomes.vn/vi/gioi-thieu-can-ho-vinhomes-symphony).
- **Không tự quy đổi diện tích:** Trang tổng hợp Vinhomes nêu rõ ví dụ 2PN+1/1WC có diện tích thông thủy khoảng 55–62 m², còn 2PN+1/2WC khoảng 63–66 m². Điều này cho thấy cùng nhóm phòng ngủ vẫn có nhiều layout và cách ghi diện tích. Những bài khác không ghi rõ thông thủy/tim tường không được mặc nhiên coi là cùng một cách đo. Dữ liệu mẫu PropFlow phải được đối chiếu hồ sơ/bản vẽ của căn thực khi nhập. [Vinhomes Market — Các loại hình căn hộ](https://market.vinhomes.vn/blog/cac-loai-hinh-can-ho-tai-vinhomes).

Studio lưu `DefaultBedroomCount = 0` theo nghĩa không có phòng ngủ tách biệt; vẫn có khu vực ngủ trong không gian mở. `+1` là không gian đa năng, không tự động được đếm thành phòng ngủ thứ hai. Các nguồn Vinhomes mô tả cách tổ chức không gian này. [Vinhomes Market — Các loại hình căn hộ](https://market.vinhomes.vn/blog/cac-loai-hinh-can-ho-tai-vinhomes).

Các nguồn trên là trang sản phẩm/dự án chính thức, nhiều bài mang ngày phát hành 2021. Chúng hữu ích cho cơ cấu và mặt bằng đã công bố, không đủ để kết luận về thứ hạng giao dịch hoặc thị phần năm 2026. Không dùng thông tin giá bán hay khuyến nghị đầu tư để chọn defaults.

### Phương án chuẩn hóa dữ liệu local

Sau kiểm tra dữ liệu hiện có, phương án setup đã thống nhất:

1. Giữ GUID của loại `Căn hộ 3 phòng ngủ`, chỉ bổ sung defaults 95/3/2; căn đang tham chiếu giữ thông số thực tế.
2. Chỉ chuẩn hóa loại tên `ok` chưa được căn hộ hoặc dữ liệu Billing tham chiếu thành Studio 35/0/1 sau precondition kiểm tra.
3. Bổ sung các loại 1PN, 1PN+1 và 2PN theo bảng; đối chiếu normalized name để tránh trùng.
4. Giữ `Căn hộ tiêu chuẩn` như loại legacy vì các căn đang tham chiếu có thông số thực tế khác nhau, gồm cả 2 và 3 phòng ngủ. Không tự remap các căn đó sang một loại chuẩn bằng suy đoán.
5. Danh mục đã thiết lập gồm năm loại chuẩn và một loại legacy đang sử dụng. Không xóa lịch sử, không sửa FK hoặc thông số căn hộ để ép tổng số danh mục về đúng năm.

Đây là thay đổi dữ liệu local có kiểm soát, không bổ sung fixed seed khi ứng dụng khởi động và không biến năm loại này thành danh mục khóa cứng. Manager tiếp tục quản lý danh mục qua FE-03.



Script thiết lập dữ liệu: `tools/database/seeds/20261102_commercial_apartment_types.sql`. Đã chạy trên database local ngày 02/11/2026, kiểm tra giữ nguyên căn hộ, FK và đơn giá Billing; chạy lại không đổi dữ liệu/timestamps. Không tự chạy script trên database khác. Đây không phải fixed seed khi ứng dụng khởi động.

## Phạm vi kiểm chứng

Đợt triển khai đã build và chạy unit, integration FE-03/Swagger, architecture; đã kiểm tra browser sửa/lưu/hủy defaults, tự điền khi tạo mới, xác nhận trước khi ghi đè thay đổi thủ công và loại legacy có default null. Chi tiết từng run/artifact là bằng chứng lịch sử, không thay thế nghiệm thu môi trường đích. Không bổ sung giá thuê, nội thất, occupancy, bulk update hoặc live synchronization trong yêu cầu defaults.
