# Class Diagram và Sequence Diagram cho hai AI mới

Bốn file PlantUML mô tả code hiện tại, đối chiếu ngày 19/11/2026:

| Chức năng | Class Diagram | Sequence Diagram |
| --- | --- | --- |
| Phân tích bất thường điện nước | `01-utility-consumption-class.puml` | `02-utility-consumption-sequence.puml` |
| Gợi ý tiện ích theo nhu cầu | `03-facility-recommendation-class.puml` | `04-facility-recommendation-sequence.puml` |

Mở file `.puml` để xem hoặc sao chép toàn bộ code vào công cụ PlantUML. Bản báo cáo này dùng bố cục Graphviz, cùng kiểu chữ và màu với sơ đồ Service Request. Chọn SVG khi đưa vào tài liệu để giữ chữ sắc nét; dùng trang ngang cho sơ đồ nhiều thành phần.

## Quy ước Class Diagram của bản báo cáo

- Phân biệt `entity`, `service`, `record`, `interface` và lớp static. Chỉ chọn các thành phần chính, không khẳng định đã liệt kê toàn bộ lớp hoặc phương thức của module.
- Phương thức được giữ đầy đủ tên, tham số, kiểu trả về và CancellationToken. Chuỗi `\n` chỉ xuống dòng chữ ký khi vẽ, không lược bỏ tham số.
- Mũi tên nét đứt `..>` là dependency. Nét đứt với tam giác rỗng `..|>` là implementation của interface. Đường liền là association có bội số ở hai đầu.
- Không dùng hình thoi aggregation/composition để mô tả DbContext lưu entity hoặc DTO chứa một danh sách khi chưa có căn cứ về vòng đời.
- `UtilityInsight` — `UtilityInsightReview` là 1 — 0..*: nhiều bản ghi lịch sử qua các revision; unique `(InsightId, Revision)` chỉ giới hạn mỗi revision tối đa một quyết định.
- `UserAccount` — `UtilityInsightReview` là 1 — 0..* qua `Actor`; người xét duyệt giữ role MANAGER tại thời điểm thao tác.
- `UserAccount` — `UtilityInsight` là 0..* — 0..* qua mảng `Recipients`: nhiều người nhận một kỳ, một người nhận nhiều kết quả. Đây là snapshot người nhận của kỳ, không phải quan hệ theo người đang cư trú hiện tại.
- `UserAccount` — `FacilityRecommendationRun` là 1 — 0..* qua `Actor` (role RESIDENT khi yêu cầu). Chỉ tham chiếu scalar ID, không thêm FK xuyên module.
- `FacilityRecommendationView` — `FacilitySuggestion` có 0..3 gợi ý trên mỗi view; phía ngược 0..* biểu thị record gợi ý có thể được tham chiếu bởi các response. Đây là mô hình DTO, không phải bảng dữ liệu hoặc quyền sở hữu vòng đời.
- `ResultJson` của run lưu response tuần tự hóa; không vẽ thành association persistence với `FacilityRecommendationView`/`FacilitySuggestion`.
- Manager, Staff và Resident là vai trò của tài khoản. Sơ đồ không tạo lớp Manager/Staff giả hoặc quan hệ kế thừa không tồn tại. Staff chỉ là người phụ trách được khai báo trong hồ sơ tiện ích, không có UI quản trị AI riêng.

## AI 1: điện nước

- `UtilityConsumptionAnalyzer` chuẩn hóa lượng dùng theo số ngày, tính median/MAD và kết hợp `MlNetUtilitySpikeModel` (`DetectIidSpike`). Mặc định cần sáu kỳ lịch sử hợp lệ. Thiếu dữ liệu trả `INSUFFICIENT`, không kết luận là bất thường.
- Gemini chỉ giải thích các tín hiệu đã tính, không quyết định severity hoặc chỉnh hóa đơn. Chỉ gọi khi người có quyền yêu cầu giải thích; có quota, cache và kiểm tra fingerprint sau khi gọi.
- Manager xét duyệt khi kỳ đã chốt bằng hóa đơn và có bất thường. Quyết định `CONFIRMED` tạo `OutboxEvent` trong cùng transaction với bản ghi xét duyệt; worker giao thông báo sau khi kiểm tra lại phiên bản và người nhận. Đây là outbox của cảnh báo điện nước, không phải finance-email queue của giao dịch thanh toán.
- Resident chỉ xem các kỳ đã chốt mà mình là người nhận hợp lệ. Xem phân tích và nhận thông báo là hai việc khác nhau; chỉ thông báo cảnh báo sau khi Manager xác nhận.

Nguồn đối chiếu: `UtilityConsumptionController`, `UtilityInsights`, `UtilityConsumptionAnalyzer`, `MlNetUtilitySpikeModel`, `GeminiUtilityExplanation`, `UtilityNoticeWorker`, `UtilityInsight` và hợp đồng `IUtilityConsumptionSource`.

## AI 2: tiện ích

- Cư dân nói hoặc nhập nhu cầu. Nhận diện giọng nói ở trình duyệt; ngữ cảnh bổ sung được giữ trong phiên ở client, không phải bảng hội thoại phía máy chủ.
- `FacilityNeedMatcher` nhận diện nhu cầu và thông tin còn thiếu, kiểm tra lịch theo múi giờ chung cư, độ tuổi, điều kiện giám sát và ngày nghỉ. Nếu lịch yêu cầu không phù hợp có thể tìm lịch thay thế và ghi rõ điều đó.
- `GeminiFacilityNeedAdvisor` chỉ chọn key ứng viên hoặc mã hỏi bổ sung. Máy chủ kiểm chứng, dựng thẻ từ hồ sơ thật, tối đa ba tiện ích. Khi AI lỗi hoặc chưa sẵn sàng, trả chế độ tra cứu tương ứng, không giả kết quả Gemini.
- `FacilitySpeech` tạo vé đọc theo cư dân và `GeminiFacilitySpeech` sinh tiếng Việt từ transcript phía máy chủ. Lời chào mở trang dùng audio tĩnh để tránh chờ TTS.
- Không có đặt chỗ tự động, dự báo mức đông hoặc dịch vụ trông trẻ mặc định.
- Nhắc tiện ích cuối tuần do `FacilityWeekendNotices`/worker riêng thực hiện theo lịch 08:00, 12:00, 16:00, 20:00 thứ Bảy và Chủ nhật. Luồng này không gọi Gemini, nên không trộn vào sequence hội thoại. Manager quản lý hồ sơ tiện ích trong PropertyAssets; Staff không có UI quản trị riêng cho task này.

Nguồn đối chiếu: `FacilityRecommendationController`, `FacilityRecommendations`, `FacilityNeedMatcher`, `GeminiFacilityNeedAdvisor`, `FacilitySpeech`, `GeminiFacilitySpeech`, `FacilityRecommendationRun` và hợp đồng `IFacilityGuideSource`.

## Giới hạn cách đọc sequence

Sequence điện nước tập trung các tương tác tầng application; UI và HTTP controller được lược bỏ, quyền truy cập được ghi ở đầu hình. Lifeline `UtilityConsumptionAnalyzer + ML.NET` và `UtilityNoticeWorker + notification publisher` là nhóm cộng tác để giảm chiều rộng, không phải lớp mới trong code. Phần tiện ích giữ controller vì controller thực sự tạo vé đọc từ kết quả service rồi trả SpeechId.

Các nhóm thể hiện những hành động có thể xảy ra ở các request khác nhau, không phải một transaction kéo dài từ UI đến hết worker. Các bước khóa, lưu, commit được gom để sơ đồ dễ đọc. Các nhánh từ chối hoặc lỗi provider được rút gọn; không thay thế API contract hoặc đặc tả kiểm thử.
