# Chatbot RAG — chạy thử trên PropFlow

## Phạm vi đã triển khai

FE-12.1–12.4, FE-12.6–12.7: hỏi đáp tài liệu công khai, dẫn nguồn và fallback an toàn. Kho tài liệu là scope extension đã được người dùng chấp thuận trong FE-12-Knowledge-Documents.md. Chưa tích hợp dữ liệu cá nhân theo FE-12.5, chat/call với Manager hoặc lưu hội thoại lâu dài.

- Admin/Manager: mở **Tài liệu chung cư**, lưu nội dung, chọn **Cư dân và nhân sự**, rồi xuất bản. Xem trạng thái chia đoạn/embedding trong phần **Nguồn chatbot cư dân**.
- Resident có cư trú đang hiệu lực: mở trang `/resident`, bấm **Hỏi PropFlow**, nhập câu hỏi. Câu trả lời AI có số dẫn nguồn; mở **Đọc tài liệu nguồn** để xem bản đang xuất bản.
- Khi chưa bật OpenAI, hệ thống vẫn tìm các đoạn văn thật theo từ khóa. UI báo rõ chưa cấu hình AI; không tạo câu trả lời AI giả.
- Không tự xuất bản nội quy mẫu. Ban quản lý phải rà soát và điền thông tin chính thức trước khi dùng làm nguồn.

## Cấu hình máy chủ

API key chỉ đặt trong User Secrets/biến môi trường API, không đặt ở Web.Client hoặc gửi trong chat:

```powershell
dotnet user-secrets set "AiChatbot:OpenAI:ApiKey" "THAY_BANG_KEY_CUA_BAN" --project src/PropFlow.Api
dotnet user-secrets set "AiChatbot:OpenAI:Enabled" "true" --project src/PropFlow.Api
```

Hoặc biến môi trường `AiChatbot__OpenAI__ApiKey`, `AiChatbot__OpenAI__Enabled=true`. Chạy lại API sau khi thay đổi cấu hình. Key cần được cấp quyền sử dụng các model đã cấu hình và tài khoản API có khả năng gọi dịch vụ; PropFlow không xác nhận được quyền/quota đó nếu chưa gọi thực tế.

Mặc định dùng `gpt-4.1-mini` cho Responses API và `text-embedding-3-small`, 512 chiều cho embedding. Có thể thay model qua cấu hình server. Chỉ model hỗ trợ Responses + Structured Outputs mới dùng được với adapter trả lời này. Thay embedding model/số chiều sẽ đưa các đoạn vào hàng đợi xử lý lại; tìm kiếm không trộn vector khác model.

Đây là tích hợp HTTP phía Infrastructure, sau `IChatAi`. Payload Responses dùng `store:false`, không tools hoặc previous_response_id. Câu hỏi và đoạn nguồn được coi là dữ liệu, không phải chỉ thị. Kiểm tra payload và adapter theo [tài liệu Structured Outputs](https://developers.openai.com/api/docs/guides/structured-outputs) và [tài liệu embedding chính thức](https://developers.openai.com/api/docs/guides/embeddings).

## Dữ liệu tìm kiếm và migration

Áp dụng migration `AddResidentChatKnowledgeChunks` của `AiChatbotDbContext`, schema history `ai_chatbot`, trước khi chạy phiên bản này. Migration mới chỉ tạo bảng `knowledge_chunks`; không sửa hóa đơn hoặc thanh toán.

Xuất bản tạo các đoạn khoảng 1.200 ký tự, chồng lấn khoảng 140 ký tự trong cùng giao dịch lưu hoạt động xuất bản. Không chia đoạn bản nháp hoặc tài liệu nội bộ. Nội dung gốc giữ nguyên Unicode; trường tìm kiếm riêng chuẩn hóa dấu để hỗ trợ tiếng Việt.

Máy local hiện chưa cài pgvector. Bản xem trước dùng `real[]` PostgreSQL và cosine chính xác kết hợp full-text search; không yêu cầu cài extension. Đây là tìm kiếm vector thật khi đã có embeddings, nhưng chưa có chỉ mục ANN/HNSW, nên chi phí truy vấn tăng theo số đoạn. Với kho lớn, chuyển adapter lưu/tìm vector sang pgvector và đo chất lượng/độ trễ trước triển khai rộng. Không âm thầm cắt bớt kho tài liệu để lấy kết quả.

Worker mỗi 15 giây: bổ sung đoạn cho tối đa một tài liệu công khai cũ, sau đó xử lý tối đa tám embedding đang chờ. Khóa advisory PostgreSQL giới hạn một worker lập chỉ mục cùng lúc giữa các instance; không giữ transaction trong khi gọi OpenAI. Lần xử lý lỗi được thử lại sau ít nhất một giờ. Không báo “đã xử lý” nếu chưa nhận vector hợp lệ. Model/chiều thay đổi được kiểm tra bằng khóa embedding.

## Giới hạn chi phí và bảo mật

- Mặc định 10 câu hỏi/phút/cư dân; `AiChatbot:QuestionsPerMinute` điều chỉnh trong khoảng 1–100.
- Tất cả lời gọi OpenAI, bao gồm worker, chia sẻ ngân sách mặc định 60 request/giờ/instance (`AiChatbot:OpenAI:RequestsPerHour`). Mỗi câu hỏi AI có thể cần một embedding và một Responses request. Giới hạn request không thay cho giới hạn chi phí bằng tiền trên tài khoản nhà cung cấp.
- Timeout mỗi lời gọi 30 giây, tối đa 900 output tokens, không tự retry lời gọi bị lỗi. Lỗi/quota/đầu ra thiếu căn cứ chuyển về trích đoạn, không khóa các chức năng khác.
- Chỉ log trạng thái HTTP, loại lỗi và số token. Không log câu hỏi, nội dung tài liệu, API key hoặc payload lỗi từ nhà cung cấp.
- Không lấy hồ sơ cư dân, căn hộ, hóa đơn hoặc thông tin ngân hàng đưa vào prompt. Email/chuỗi số điện thoại dài được che trong câu hỏi và đoạn nguồn trước khi gửi ra ngoài. Việc che theo mẫu không thể phát hiện mọi PII: người dùng không nhập dữ liệu nhạy cảm; Ban quản lý chỉ xuất bản tài liệu phù hợp và phải rà soát tên/địa chỉ/thông tin cá nhân trước khi cho phép xử lý ngoài hệ thống.
- Tài liệu/người hỏi không được thêm tool hoặc thao tác nghiệp vụ. Prompt injection bị coi là dữ liệu; không có quyền ghi nghiệp vụ hay truy cập database từ mô hình. LLM vẫn có thể trả lời sai; đối chiếu nguồn và đánh giá chất lượng trước khi dùng rộng.
- Retrieval JOIN trực tiếp bản đang xuất bản và audience RESIDENT. Trước khi trả câu trả lời còn kiểm tra lại phiên bản. Nguồn bị thu hồi/đổi nội bộ trong lúc gọi AI khiến câu trả lời bị loại. Endpoint đọc nguồn cũng kiểm tra lại cư trú và phiên bản hiện hành.
- Hội thoại chỉ nằm trong bộ nhớ trang, tối đa 20 lượt; đóng/mở widget cùng trang vẫn giữ, **Xóa hội thoại** hoặc tải lại trang sẽ xóa. Mỗi câu hỏi độc lập; không gửi toàn bộ lịch sử tới OpenAI.

## Kiểm tra nhanh

1. Xuất bản tài liệu RESIDENT có một quy định cụ thể, hỏi đúng nội dung bằng tài khoản Resident.
2. Trước khi thêm key: phải thấy thông báo chưa cấu hình và trích đoạn đúng tài liệu.
3. Sau khi thêm key/bật API và worker đã tạo embedding: phải có câu trả lời kèm dẫn nguồn và mở được nguồn.
4. Hỏi nội dung không có trong tài liệu: báo không đủ nguồn, không bịa quy định.
5. Lưu nháp mới: vẫn dùng bản xuất bản cũ. Xuất bản bản mới: dùng phiên bản mới. Ngừng xuất bản: không còn tìm/đọc nguồn qua chatbot.
6. Manager/Staff/Admin/Accountant hoặc Resident thiếu cư trú không được gọi API chatbot cư dân.

Không tự gọi OpenAI thật trong kiểm thử; dùng adapter giả chỉ trong tests. Cần kiểm tra trực tiếp sau khi người vận hành tự thêm API key.

## Phân biệt lỗi AI và tài liệu chưa đủ thông tin

API key hợp lệ hoặc đọc được danh sách model không chứng minh tài khoản còn credit để tạo câu trả lời. Trạng thái **Đã cấu hình AI** chỉ cho biết key đã được cung cấp và tính năng đã bật.

- `credit_balance_exhausted`/`insufficient_quota`: kiểm tra credit và giới hạn sử dụng của tổ chức/project OpenAI API. Không giải quyết bằng cách thêm tài liệu hoặc gửi lại liên tục. Xem [mã lỗi OpenAI chính thức](https://developers.openai.com/api/docs/guides/error-codes).
- Khi lời gọi AI thất bại, cư dân thấy **Dịch vụ AI hiện không khả dụng**, kèm các trích đoạn đã tìm được nếu có.
- Khi gọi được AI nhưng câu trả lời không có dẫn nguồn hợp lệ hoặc nguồn chưa đủ, hệ thống báo chưa tạo được câu trả lời có đủ căn cứ.
- Log phía API ghi HTTP status và mã lỗi thuộc danh sách an toàn; không ghi raw payload, câu hỏi hoặc giá trị key. Chi tiết quota của đơn vị vận hành không hiển thị cho cư dân.
