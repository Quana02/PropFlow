# Chatbot RAG — chạy thử trên PropFlow

## Phạm vi đã triển khai

FE-12.1–12.4, FE-12.6–12.7: hỏi đáp tài liệu công khai, dẫn nguồn và fallback an toàn. Kho tài liệu là scope extension đã được người dùng chấp thuận trong FE-12-Knowledge-Documents.md. Chưa tích hợp dữ liệu cá nhân theo FE-12.5, chat/call với Manager hoặc lưu hội thoại lâu dài.

- Admin/Manager: mở **Tài liệu chung cư**, lưu nội dung, chọn **Cư dân và nhân sự**, rồi xuất bản. Xem trạng thái chia đoạn/embedding trong phần **Nguồn chatbot cư dân**.
- Resident có cư trú đang hiệu lực: mở trang `/resident`, bấm **Hỏi PropFlow**, nhập câu hỏi. Câu trả lời AI có số dẫn nguồn; mở **Đọc tài liệu nguồn** để xem bản đang xuất bản.
- Khi chưa bật Gemini, hệ thống vẫn tìm các đoạn văn thật theo từ khóa. UI báo rõ chưa cấu hình AI; không tạo câu trả lời AI giả.
- Không tự xuất bản nội quy mẫu. Ban quản lý phải rà soát và điền thông tin chính thức trước khi dùng làm nguồn.

## Cấu hình máy chủ

Mở [Google AI Studio — API keys](https://aistudio.google.com/apikey), đăng nhập, chọn hoặc tạo project rồi tạo Gemini API key. Xem [hướng dẫn API key chính thức của Google](https://ai.google.dev/gemini-api/docs/api-key). Key OpenAI đã lưu trước đây không dùng được cho Gemini.

API key chỉ đặt trong User Secrets/biến môi trường API, không đặt ở Web.Client, appsettings được commit hoặc gửi trong chat:

```powershell
dotnet user-secrets set "AiChatbot:Gemini:ApiKey" "THAY_BANG_GEMINI_KEY_CUA_BAN" --project src/PropFlow.Api
dotnet user-secrets set "AiChatbot:Gemini:Enabled" "true" --project src/PropFlow.Api
```

Hoặc biến môi trường `AiChatbot__Gemini__ApiKey`, `AiChatbot__Gemini__Enabled=true`. `Enabled` mặc định là `false`; cần cung cấp key và bật cấu hình. Chạy lại API sau khi thay đổi key hoặc cấu hình. Key cần được cấp quyền sử dụng các model đã cấu hình và project Google còn hạn mức; PropFlow không xác nhận được quyền/quota đó nếu chưa gọi thực tế. Phiên bản này chỉ đọc cấu hình Gemini, không tiếp tục gọi provider từ cấu hình OpenAI cũ.

Mặc định dùng `gemini-3.1-flash-lite` để trả lời và `gemini-embedding-001`, 768 chiều cho embedding. Câu hỏi dùng `RETRIEVAL_QUERY`, đoạn tài liệu dùng `RETRIEVAL_DOCUMENT`; vector được chuẩn hóa để tính độ tương đồng. Có thể thay qua `AiChatbot:Gemini:AnswerModel`, `EmbeddingModel`, `EmbeddingDimensions`; model trả lời phải hỗ trợ `generateContent` và structured JSON theo schema, model embedding phải hỗ trợ số chiều đã chọn. Khi kiểm tra kết nối thật, model `gemini-2.5-flash` trả `404 NOT_FOUND` với key đã cấu hình, còn `gemini-3.1-flash-lite` trả lời có dẫn nguồn thành công. Nếu từng đặt `AnswerModel` trong User Secrets/biến môi trường, cập nhật giá trị đó vì nó ghi đè mặc định trong appsettings.

Đây là tích hợp HTTP native Gemini phía Infrastructure, sau `IChatAi`: `batchEmbedContents` cho embedding, `generateContent` cho trả lời, named HTTP client `ResidentChatGemini`. Key được truyền bằng header `x-goog-api-key`, không đưa vào URL. Câu hỏi và đoạn nguồn được coi là dữ liệu, không phải chỉ thị; không cung cấp tools hay quyền ghi nghiệp vụ cho model. Câu trả lời phải có JSON hợp lệ và số dẫn nguồn nằm trong các đoạn thực tế đã lấy được. Tham khảo [Structured Outputs](https://ai.google.dev/gemini-api/docs/structured-output), [embedding](https://ai.google.dev/gemini-api/docs/embeddings) và [REST API embedding](https://ai.google.dev/api/embeddings).

Gemini có Free Tier theo model/project, với giới hạn request và token; không bảo đảm miễn phí hoặc không giới hạn cho mọi model. Kiểm tra [bảng giá](https://ai.google.dev/gemini-api/docs/pricing) và [rate limits](https://ai.google.dev/gemini-api/docs/rate-limits) trước khi bật. Theo bảng giá Google, nội dung gửi qua Free Tier có thể được dùng để cải thiện sản phẩm; chỉ dùng tài liệu demo không nhạy cảm khi thử miễn phí và rà soát chế độ xử lý dữ liệu trước khi triển khai thật.

## Dữ liệu tìm kiếm và migration

Áp dụng migration `AddResidentChatKnowledgeChunks` của `AiChatbotDbContext`, schema history `ai_chatbot`, trước khi chạy chatbot. Migration này tạo bảng `knowledge_chunks`; không sửa hóa đơn hoặc thanh toán. Chuyển provider từ OpenAI sang Gemini không cần thêm migration hoặc xóa tài liệu.

Khóa embedding mới bao gồm provider, model, số chiều và cặp task retrieval. Worker tự xử lý lại các đoạn có vector cũ khi Gemini đã được bật; tìm kiếm không trộn vector OpenAI với vector Gemini. Đổi model được thử ngay một lần dù lần xử lý OpenAI gần đây thất bại; nếu Gemini cũng lỗi, các lần thử tiếp theo vẫn chờ ít nhất một giờ. Đánh dấu đã thử không được tính là đã xử lý thành công khi chưa có vector hợp lệ. Trong lúc chờ, tìm kiếm từ khóa vẫn dùng được. Các tài liệu đã xuất bản và lịch sử giữ nguyên.

Xuất bản tạo các đoạn khoảng 1.200 ký tự, chồng lấn khoảng 140 ký tự trong cùng giao dịch lưu hoạt động xuất bản. Không chia đoạn bản nháp hoặc tài liệu nội bộ. Nội dung gốc giữ nguyên Unicode; trường tìm kiếm riêng chuẩn hóa dấu để hỗ trợ tiếng Việt.

Máy local hiện chưa cài pgvector. Bản xem trước dùng `real[]` PostgreSQL và cosine chính xác kết hợp full-text search; không yêu cầu cài extension. Đây là tìm kiếm vector thật khi đã có embeddings, nhưng chưa có chỉ mục ANN/HNSW, nên chi phí truy vấn tăng theo số đoạn. Với kho lớn, chuyển adapter lưu/tìm vector sang pgvector và đo chất lượng/độ trễ trước triển khai rộng. Không âm thầm cắt bớt kho tài liệu để lấy kết quả.

Worker mỗi 15 giây: bổ sung đoạn cho tối đa một tài liệu công khai cũ, sau đó xử lý tối đa tám embedding đang chờ. Khóa advisory PostgreSQL giới hạn một worker lập chỉ mục cùng lúc giữa các instance; không giữ transaction trong khi gọi Gemini. Lần xử lý lỗi được thử lại sau ít nhất một giờ. Không báo “đã xử lý” nếu chưa nhận vector hợp lệ. Provider/model/chiều thay đổi được kiểm tra bằng khóa embedding.

## Giới hạn chi phí và bảo mật

- Mặc định 10 câu hỏi/phút/cư dân; `AiChatbot:QuestionsPerMinute` điều chỉnh trong khoảng 1–100.
- Tất cả lời gọi Gemini, bao gồm worker, chia sẻ ngân sách mặc định 60 request/giờ/instance (`AiChatbot:Gemini:RequestsPerHour`). Mỗi câu hỏi AI có thể cần một embedding request và một `generateContent` request. Giới hạn request không thay cho quota hoặc giới hạn chi phí bằng tiền trên project Google.
- Timeout mỗi lời gọi 30 giây, tối đa 900 output tokens, không tự retry lời gọi bị lỗi. Model mặc định `gemini-3.1-flash-lite` dùng cấu hình thinking của nhà cung cấp. Chỉ khi người vận hành chọn nhóm `gemini-2.5-flash` thì adapter gửi `thinkingBudget=0`; không gửi tùy chọn này cho model 3.x. Đầu ra bị cắt ngắn hoặc bị chặn không được dùng làm câu trả lời. Lỗi/quota/đầu ra thiếu căn cứ chuyển về trích đoạn, không khóa các chức năng khác.
- Chỉ log trạng thái HTTP, loại lỗi và số token. Không log câu hỏi, nội dung tài liệu, API key hoặc payload lỗi từ nhà cung cấp.
- Không lấy hồ sơ cư dân, căn hộ, hóa đơn hoặc thông tin ngân hàng đưa vào prompt. Email/chuỗi số điện thoại dài được che trong câu hỏi và đoạn nguồn trước khi gửi ra ngoài. Việc che theo mẫu không thể phát hiện mọi PII: người dùng không nhập dữ liệu nhạy cảm; Ban quản lý chỉ xuất bản tài liệu phù hợp và phải rà soát tên/địa chỉ/thông tin cá nhân trước khi cho phép xử lý ngoài hệ thống.
- Tài liệu/người hỏi không được thêm tool hoặc thao tác nghiệp vụ. Prompt injection bị coi là dữ liệu; không có quyền ghi nghiệp vụ hay truy cập database từ mô hình. LLM vẫn có thể trả lời sai; đối chiếu nguồn và đánh giá chất lượng trước khi dùng rộng.
- Retrieval JOIN trực tiếp bản đang xuất bản và audience RESIDENT. Trước khi trả câu trả lời còn kiểm tra lại phiên bản. Nguồn bị thu hồi/đổi nội bộ trong lúc gọi AI khiến câu trả lời bị loại. Endpoint đọc nguồn cũng kiểm tra lại cư trú và phiên bản hiện hành.
- Hội thoại chỉ nằm trong bộ nhớ trang, tối đa 20 lượt; đóng/mở widget cùng trang vẫn giữ, **Xóa hội thoại** hoặc tải lại trang sẽ xóa. Mỗi câu hỏi độc lập; không gửi toàn bộ lịch sử tới Gemini.

## Kiểm tra nhanh

1. Xuất bản tài liệu RESIDENT có một quy định cụ thể, hỏi đúng nội dung bằng tài khoản Resident.
2. Trước khi thêm key: phải thấy thông báo chưa cấu hình và trích đoạn đúng tài liệu.
3. Sau khi thêm key/bật API và worker đã tạo embedding: phải có câu trả lời kèm dẫn nguồn và mở được nguồn.
4. Hỏi nội dung không có trong tài liệu: báo không đủ nguồn, không bịa quy định.
5. Lưu nháp mới: vẫn dùng bản xuất bản cũ. Xuất bản bản mới: dùng phiên bản mới. Ngừng xuất bản: không còn tìm/đọc nguồn qua chatbot.
6. Manager/Staff/Admin/Accountant hoặc Resident thiếu cư trú không được gọi API chatbot cư dân.

Không tự gọi Gemini thật trong kiểm thử; dùng adapter giả chỉ trong tests. Cần kiểm tra trực tiếp sau khi người vận hành tự thêm Gemini API key.

## Phân biệt lỗi AI và tài liệu chưa đủ thông tin

API key hợp lệ hoặc đọc được danh sách model không chứng minh project còn quota để tạo embedding hoặc câu trả lời. Trạng thái **Đã cấu hình Gemini** chỉ cho biết key đã được cung cấp và tính năng đã bật.

- HTTP 429 / `RESOURCE_EXHAUSTED`: kiểm tra quota còn lại của đúng model/project trong Google AI Studio; chờ quota phục hồi theo giới hạn đang áp dụng hoặc điều chỉnh tier/quota phù hợp. Không giải quyết bằng cách thêm tài liệu hoặc đổi key trong cùng project rồi gửi lại liên tục. Free Tier vẫn có thể gặp lỗi này. Xem [hướng dẫn lỗi Gemini](https://ai.google.dev/gemini-api/docs/troubleshooting) và [rate limits](https://ai.google.dev/gemini-api/docs/rate-limits).
- HTTP 400/401/403: kiểm tra key, quyền sử dụng Gemini API/model, giới hạn của key và trạng thái project. Không đưa key hoặc thông báo lỗi thô vào UI cư dân.
- Khi lời gọi AI thất bại, cư dân thấy **Dịch vụ AI hiện không khả dụng**, kèm các trích đoạn đã tìm được nếu có.
- Khi gọi được AI nhưng câu trả lời không có dẫn nguồn hợp lệ hoặc nguồn chưa đủ, hệ thống báo chưa tạo được câu trả lời có đủ căn cứ.
- Log phía API ghi HTTP status và mã lỗi thuộc danh sách an toàn; không ghi raw payload, câu hỏi hoặc giá trị key. Chi tiết quota của đơn vị vận hành không hiển thị cho cư dân.
