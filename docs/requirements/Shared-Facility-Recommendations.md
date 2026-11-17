# Gợi ý tiện ích theo nhu cầu cư dân — Task 2

Phạm vi được người dùng làm rõ ngày 16/11/2026. Cư dân mô tả nhu cầu bằng ngôn ngữ tự nhiên, AI tìm các tiện ích **có thật trong chung cư**, trả lịch hoạt động, người phụ trách, liên hệ và điều kiện sử dụng. Không triển khai dự báo mức đông hoặc yêu cầu Staff ghi số người.

## Ví dụ và giới hạn

Cập nhật theo phản hồi cư dân ngày 17/11/2026:

- Lời chào trung lập, không suy đoán cư dân có con. “Tôi hơi chán” là mở đầu trò chuyện: hỏi sở thích, đi cùng ai và dự định thời gian; chưa đưa thẻ tiện ích. Chỉ hỏi tuổi/giám sát khi cư dân thực sự nhắc nhu cầu cho trẻ.
- `CLARIFY` trả `suggestions=[]` và câu hỏi ở `followUp`. Gemini có thể trả mã cần làm rõ (`INTEREST`, `PARTY`, `TIME`, `AGE`); máy chủ dựng câu hỏi cố định, không để model tự tạo dữ kiện hoặc đọc lại thông tin riêng tư. Thiếu tuổi trẻ thì hỏi trước khi chọn nơi giới hạn tuổi.
- Client giữ ngữ cảnh các lượt bổ sung trong phiên, tối đa 1.200 ký tự; **Tìm cho nhu cầu khác** xóa ngữ cảnh. Chấp nhận câu trả lời ngắn từ hai ký tự. Không lấy hồ sơ gia đình để suy ra nhu cầu.
- Chỉ hỏi lại chiều thông tin còn thiếu. “Chill/relax” được hiểu là thư giãn, “1 mình” là một mình, “trong hôm nay” đã đủ thông tin ngày; không bắt cư dân lặp lại người đi cùng hoặc cung cấp giờ chính xác. Câu mở đầu mơ hồ không được kích hoạt lại bộ câu hỏi khi đã có câu bổ sung. Gemini nhận các cờ ngữ cảnh đã nhận diện; máy chủ chặn câu hỏi về thông tin đã biết, dùng tra cứu hồ sơ có ghi rõ chế độ nếu model vẫn yêu cầu hỏi lặp. Phiên bản cache tăng khi sửa quy tắc này.
- Tính ngày/giờ theo timezone chung cư. Gemini nhận giờ địa phương và các khung mở cửa hợp lệ; máy chủ vẫn lọc ngày đóng cửa, khung đã qua, đối tượng/tuổi và điều kiện giám sát. Câu bổ sung thời gian mới thay cho lịch cũ.
- Trong các lựa chọn phù hợp, ưu tiên đang trong giờ mở cửa, rồi khung gần nhất. Nếu lịch được yêu cầu không còn lựa chọn, hiển thị **Lịch thay thế**, ngày/giờ tiếp theo (tìm trong 14 ngày), không coi đó là đáp ứng lịch ban đầu. `availability`/`isOpenNow` do máy chủ tính; không đồng nghĩa còn chỗ hay có sự kiện. Cache thay đổi khi tiện ích chuyển trạng thái mở/đóng cửa.

- “Khu vực cuối tuần cho trẻ em”: hỏi thêm tuổi và điều kiện đi cùng trước, sau đó tìm hồ sơ dành cho trẻ em/gia đình vào thứ Bảy/Chủ nhật còn hoạt động.
- “Bé 6 tuổi, thứ Bảy bố mẹ phải đi làm, cần người trông”: chỉ xem hồ sơ có hoạt động giám sát được BQL khai báo. Nếu không có, nói rõ chưa tìm thấy; không coi khu vui chơi/hồ bơi là dịch vụ trông trẻ.
- “Tìm nơi đọc sách yên tĩnh”: AI đối chiếu ngữ nghĩa mô tả để chọn nơi phù hợp; không tự thêm thư viện khi chung cư không có.
- Kết quả tối đa ba tiện ích, kèm địa điểm, mô tả, đối tượng/tuổi, ngày và giờ mở–đóng, người phụ trách, đầu mối công việc và nội quy. Các thông tin thực tế được máy chủ lấy từ hồ sơ BQL, không cho AI tự viết lại giờ hoặc tên người.
- Gợi ý không phải đặt chỗ, giữ chỗ hoặc xác nhận tiếp nhận trẻ. Chương trình có giám sát vẫn cần liên hệ người phụ trách để xác nhận điều kiện, lịch riêng và khả năng tiếp nhận.

## Dữ liệu và vai trò

1. Manager tạo cơ sở vật chất tại danh mục hiện có, sau đó vào **Hồ sơ tiện ích** (`/operations/facility-guides`).
2. Bổ sung mô tả hoạt động, đối tượng, độ tuổi nếu có, chính sách trẻ em, lịch mở–đóng theo thứ, ngày đóng cửa ngoại lệ, nội quy, Staff đang hoạt động làm người phụ trách và đầu mối liên hệ công việc công khai.
3. Xuất bản khi đủ dữ liệu và cơ sở đang `ACTIVE`. Hồ sơ nháp, cơ sở ngừng hoạt động hoặc người phụ trách không còn hoạt động không xuất hiện cho cư dân.
4. Resident có hồ sơ cư trú hợp lệ chọn **Tiện ích AI** trong **Dịch vụ cư dân** (`/resident/facilities`). Trợ lý có lõi chuyển động khi nghe/nói/xử lý; nhận nhu cầu bằng micro hoặc chữ, đọc phản hồi và hiển thị thẻ tiện ích gọn. Câu trả lời bổ sung giữ ngữ cảnh nhu cầu trước. Trên máy tính, trợ lý và kết quả nằm cạnh nhau; chọn giữa tối đa ba gợi ý để xem giờ, tuổi, người phụ trách và liên hệ. Mô tả/nội quy nằm trong phần mở rộng.
5. Staff không có màn hình nhập số người hoặc quyền sửa hồ sơ tiện ích. Người phụ trách được Manager chỉ định; không phải lựa chọn của AI.

Mỗi thứ hiện hỗ trợ một khoảng mở cửa trong cùng ngày; không hỗ trợ lịch qua đêm. Ngày cụ thể nằm trong ba mươi ngày tới; nếu không chọn ngày, hệ thống tìm lịch trong hai tuần tới. Các từ “hôm nay”, “ngày mai”, thứ cụ thể và “cuối tuần” được đối chiếu theo múi giờ chung cư. Độ tuổi do cư dân nêu được kiểm tra với giới hạn tuổi hồ sơ.

## Luồng xử lý và phân chia module

Nhận diện giọng nói tiếng Việt dùng trình duyệt. Phần đọc sử dụng Gemini TTS phía máy chủ với key Gemini hiện có; bản triển khai đã kiểm tra `gemini-3.1-flash-tts-preview`, giọng Kore, PCM 24 kHz chuyển WAV mono 16 bit trước khi phát bằng Audio. Không dùng SpeechSynthesis hoặc giọng Anh/Nhật cài trên máy. Micro chỉ bật sau thao tác cư dân; dừng ghi/đọc khi rời hoặc ẩn trang. Trình duyệt thiếu nhận diện hoặc người dùng từ chối micro vẫn nhập được. Có nút tắt/bật tiếng, nghe lại và bắt đầu nhu cầu khác. Giảm chuyển động theo thiết lập hệ điều hành. Chatbot tài liệu nổi được ẩn trong trang trợ lý để tránh hai AI cùng che giao diện.

Chỉ người dùng thay đổi trạng thái bật/tắt tiếng. Nếu provider, hạn mức hoặc autoplay lỗi, UI báo để nghe lại và giữ nguyên trạng thái tiếng; kết quả chữ vẫn hiển thị. Khi ngắt lời/bấm tắt tiếng, yêu cầu tải audio đang chờ được hủy. Hiển thị kết quả ngay trước khi chờ audio để giọng nói không làm chậm việc xem gợi ý.

Lời chào `WELCOME` và câu bắt đầu nhu cầu mới `NEW_NEED` dùng bản ghi tiếng Việt tạo sẵn trong `wwwroot/audio/facility`, không gọi TTS lúc mở trang. Dashboard tải trước lời chào, không phát tiếng hoặc mở micro; trang trợ lý dùng lại cùng lượt tải và cache của trình duyệt. Khi rời trang giữa lúc tải, bản ghi không tự phát muộn. Phản hồi riêng theo nhu cầu vẫn dùng TTS phía server và vé đọc. Các câu cố định trong API được giữ để tương thích client cũ.

`tools/audio/New-FacilityGreetings.ps1` tạo các bản ghi qua Gemini với biến môi trường `PROPFLOW_GEMINI_API_KEY`; không truyền key qua dòng lệnh. `greetings.json` ghi transcript, model, voice, dung lượng và SHA-256 để kiểm tra tài nguyên. Mặc định script giữ file đã có; dùng `-Regenerate WELCOME` khi chủ động thay lời chào, cập nhật phiên bản URL asset và kiểm tra khớp transcript trên UI. Không tạo âm thanh lúc ứng dụng khởi động.

Máy chủ tạo vé đọc ngẫu nhiên theo cư dân và phản hồi, hết hạn sau mười phút; không nhận nội dung tùy ý từ client để gọi TTS. Transcript chỉ gồm tên tiện ích công khai, lịch, điều kiện, câu hỏi bổ sung có sẵn; không gửi câu nói cư dân, tên nhân viên, số điện thoại hoặc đầu mối liên hệ cho TTS. Cache WAV theo transcript ba mươi phút, giới hạn bộ nhớ 32 MiB, mặc định hai mươi lần tạo âm thanh/cư dân/giờ và hai trăm/toàn hệ thống/ngày. Nghe lại trúng cache không tạo âm thanh thêm. Cấu hình tùy chọn `FacilityRecommendations:Speech:Model`, `PerResidentHour`, `PerDay`; API key chỉ ở máy chủ. Model TTS mặc định là bản preview, có thể đổi cấu hình theo model hỗ trợ của tài khoản và định dạng provider tương ứng.

`ResidentFacilities → FacilityRecommendationsApi → FacilityRecommendationController → FacilityRecommendations → IFacilityGuideSource → PropertyAssets`.

- PropertyAssets sở hữu hồ sơ và lịch, đọc danh mục cơ sở vật chất hiện có và thông tin Staff qua public contracts. Token chống ghi đè khi hai Manager sửa đồng thời.
- AiRecommendation kiểm tra cư trú, thời gian và điều kiện bắt buộc trước; `GeminiFacilityNeedAdvisor` chọn key ứng viên theo ngữ nghĩa. Không gửi mã tài khoản/căn hộ, tên người phụ trách hoặc liên hệ cho AI; nội dung nhu cầu được che email/số điện thoại và một số mẫu tên trước khi gửi, không lưu nguyên văn.
- Gemini trả key ứng viên, cờ nhu cầu và mã hỏi thêm. Máy chủ không chấp nhận cờ trẻ em do model suy đoán cho yêu cầu không liên quan đến trẻ; kiểm tra key và dựng kết quả từ dữ liệu gốc. Không nhận tên tiện ích/giờ mở cửa tự tạo từ AI.
- AI chưa cấu hình hoặc lỗi: tra cứu hồ sơ theo từ khóa phù hợp, ghi rõ chế độ dự phòng. Không khẳng định đã phân tích ngữ nghĩa bằng AI.
- Hạn mức mặc định: hai mươi yêu cầu AI/người và hai trăm toàn hệ thống trong hai mươi bốn giờ; cache mười phút, theo tài khoản, nhu cầu đã che thông tin, lịch và fingerprint nguồn. Kiểm tra lại nguồn và cư trú sau khi gọi AI; không giữ transaction trong lúc chờ provider. Dùng cấu hình Gemini máy chủ hiện có.

## API

- `GET /api/v1/facility-guides`: Manager đọc hồ sơ/danh mục người phụ trách.
- `GET /api/v1/resident/facility-recommendations/today?facilityId={id}`: Resident có cư trú hiện hành xem lịch còn lại hôm nay của tiện ích được nhắc. Dữ liệu lấy lại từ hồ sơ đang xuất bản; lịch đã đóng/hết giờ trả danh sách rỗng và lời giải thích.
- `PUT /api/v1/facility-guides/{id}`: Manager lưu và xuất bản/thu hồi hồ sơ.
- `GET /api/v1/resident/facility-recommendations`: Resident đọc danh mục đã xuất bản.
- `POST /api/v1/resident/facility-recommendations`: `{ "need": "Khu vực cuối tuần cho trẻ em", "preferredDate": null }`.
- `POST /api/v1/resident/facility-recommendations/speech`: `{ "speechId": "vé từ phản hồi", "kind": "RESULT" }`; `WELCOME`/`NEW_NEED` là các câu cố định không cần vé. Trả audio WAV dạng base64; vẫn kiểm tra cư trú hiện hành trước khi phát.

## Chuyển đổi và kiểm thử

### Lời nhắc tiện ích cuối tuần

Theo yêu cầu ngày 17/11/2026: thông báo trong chuông cư dân vào **thứ Bảy và Chủ nhật**, bắt đầu **8h**, tiếp theo **12h, 16h, 20h**, theo múi giờ chung cư. Mỗi lượt giới thiệu **một tiện ích khác nhau**, không lặp cùng tiện ích trong một cuối tuần. Cuối tuần kế tiếp có thể giới thiệu lại.

- Chọn từ hồ sơ đã xuất bản, cơ sở đang hoạt động, có người phụ trách và còn giờ sử dụng trong ngày; tôn trọng ngày nghỉ ngoại lệ. Lời mời chung ưu tiên cư dân nói chung, không suy đoán có con hoặc nhu cầu trông trẻ. Nếu hết lựa chọn chưa giới thiệu, bỏ qua lượt nhắc.
- Tiêu đề thân thiện thay đổi theo ngày; nội dung có tên tiện ích và giờ mở–đóng từ BQL. Đây là thông báo theo lịch, tạo trực tiếp từ dữ liệu công bố, không phát sinh lượt gọi Gemini nền. Cư dân có thể mở trang để xem thông tin hoặc chủ động nói/nhập nhu cầu cho AI phân tích.
- Gửi trong vòng 15 phút sau mỗi mốc; API cần hoạt động trong khoảng này. Nếu API ngừng chạy quá khoảng đó, bỏ qua lượt đã lỡ, không dồn thông báo cũ khi khởi động lại. Worker kiểm tra mỗi 60 giây.
- Chỉ gửi đến tài khoản ACTIVE, giữ vai trò RESIDENT hiện hành, hồ sơ cư dân hoạt động và còn cư trú. Chỉ thông báo trong ứng dụng; không gửi email hoặc tự phát tiếng nói nền.
- Nhãn chuông là **Gợi ý cuối tuần**, nguồn `FACILITY_WEEKEND`, loại `ANNOUNCEMENT`. Bấm mở `/resident/facilities?facility={id}` để xem đúng tiện ích, giờ, vị trí, người phụ trách và liên hệ; trang tải lại dữ liệu hiện hành khi mở.
- Bảng `ai_recommendation.facility_weekend_notice_runs` lưu từng ngày/mốc, tiện ích và lease. Unique `(local_date, slot)` ngăn hai lượt cho cùng mốc; unique `(weekend_start, facility_id)` ngăn lặp trong cuối tuần. Communication dùng khóa sự kiện/recipient sẵn có để không tạo hai thông báo khi thử lại sau lỗi hoặc API khởi động lại.
- Cấu hình máy chủ: `FacilityRecommendations:WeekendNotice:Enabled` (mặc định `true`), `StartsAt` (`08:00:00`), `EndsAt` (`20:00:00`), `PollSeconds` (`60`). Khoảng cách giữa các mốc là bốn giờ.

Migration `AddFacilityWeekendNotices` chỉ thêm bảng theo dõi lời nhắc và các chỉ mục; không sửa dữ liệu cư dân, hóa đơn hoặc lịch tiện ích.

Migration `ReplaceOccupancyWithFacilityGuides` bỏ bảng ghi số người và cấu hình dự báo cũ. Giữ lịch, nội quy, ngày đóng cửa và metadata chỉnh sửa để chuyển sang hồ sơ nháp; Manager bổ sung mô tả, đối tượng, người phụ trách, liên hệ rồi xuất bản lại. Các migration trước được giữ làm lịch sử cho database đã áp dụng.

Không thêm tiện ích hay dịch vụ trông trẻ giả vào database thật. Các kiểm thử sử dụng fixtures và database riêng, bao gồm nâng cấp từ schema cũ, quyền Manager/Staff/Resident, xung đột chỉnh sửa, tuổi, lịch đóng cửa, giám sát trẻ, kiểm tra phản hồi Gemini, cache/quota và hiển thị responsive sáng/tối.

Để test: tạo cơ sở có thật → Manager điền hồ sơ và xuất bản → Resident nhập các ví dụ ở trên. Nếu không có chương trình giám sát, ví dụ bố mẹ vắng nhà phải trả về thông báo thiếu dịch vụ phù hợp, thay vì gợi ý gửi trẻ vào khu vui chơi thông thường.
