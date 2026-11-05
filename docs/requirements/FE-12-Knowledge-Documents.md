# FE-12 — Kho tài liệu cho chatbot RAG

Mở rộng phạm vi theo yêu cầu người dùng ngày 04/11/2026. Đây là phần chuẩn bị nguồn tài liệu của FE-12, không tự tạo UC trong catalogue cũ.

## Phân quyền và phạm vi

- Admin có MANAGE_INTERNAL_ACCOUNTS và Manager có MANAGE_OPERATIONS: tạo, đọc, sửa bản nháp, xuất bản, ngừng xuất bản và xem lịch sử.
- Staff có PERFORM_ASSIGNED_OPERATIONS: chỉ đọc bản đang xuất bản, bao gồm tài liệu nội bộ. Không đọc nháp, lịch sử nháp hoặc gọi API chỉnh sửa.
- Resident và Accountant không truy cập màn hình/API quản lý kho tài liệu.
- Một deployment phục vụ một chung cư; không có bộ chọn tòa nhà.

## Nội dung và xuất bản

Trang `/knowledge/documents`, menu **Tài liệu chung cư** trên Admin/Manager/Staff. Nhập tiêu đề, nội dung văn bản hoặc nhập tệp UTF-8 `.txt`/`.md` tối đa 256 KB. Nội dung tối đa 60.000 ký tự, tiêu đề 200 ký tự, chuẩn hóa Unicode NFC. Tệp được đọc thành văn bản, không lưu tệp gốc hoặc mở URL do người dùng cung cấp. PDF/DOCX và OCR chưa thuộc lần triển khai này.

Mỗi lần lưu tạo một phiên bản bất biến. Xuất bản có hiệu lực ngay, trỏ tới phiên bản mới nhất đã lưu. Bản nháp mới không thay thế bản đang xuất bản; thay đổi phạm vi trong bản nháp cũng chỉ có hiệu lực sau khi xuất bản bản đó. Có thể ngừng xuất bản ngay để thu hồi nguồn cũ trước khi sửa. Không xóa lịch sử. Mọi thay đổi lưu actor, thời gian UTC và hoạt động trong cùng giao dịch. Token phiên bản bắt buộc khi sửa/đổi xuất bản; cập nhật đồng thời trả 409, không ghi đè.

Phạm vi `RESIDENT`: cư dân và nhân sự; `INTERNAL`: nhân sự. Staff không sửa phạm vi. Văn bản được Razor mã hóa khi hiển thị, không thực thi HTML hoặc Markdown nhúng.

API `/api/v1/knowledge/documents` có GET phân trang 12 mục/tìm tiêu đề và POST tạo. `/{id}` có GET/PUT. POST `/{id}/publication` xuất bản; POST `/{id}/unpublish` ngừng xuất bản. API và UI cùng sử dụng policy `knowledge.read`/`knowledge.manage`, ánh xạ vào các quyền vai trò sẵn có, không thêm quyền tài chính hay vận hành khác cho Admin.

## Ranh giới RAG

`IPublishedKnowledgeSource.FindResidentSourceAsync` chỉ trả bản đang xuất bản có phạm vi RESIDENT. Nháp, tài liệu nội bộ và tài liệu ngừng xuất bản không được dùng làm nguồn cư dân. Khi bổ sung chỉ mục vector, phải kiểm tra lại publication/audience qua nguồn này trước khi đưa đoạn trích vào câu trả lời; không tin chỉ mục cũ.

Luồng chatbot RAG được bổ sung sau kho tài liệu, xem `docs/chatbot-rag-setup.md`. Xuất bản tạo các đoạn nguồn trong cùng giao dịch; worker tạo embedding ngoài giao dịch khi OpenAI đã được cấu hình. UI hiển thị số đoạn thực tế và không báo đã xử lý nếu chưa có vector. Nội quy mẫu trong `docs/knowledge-drafts/noi-quy-chung-cu.md` vẫn là bản nháp, không tự nhập hoặc xuất bản.

## Dữ liệu và triển khai

AiChatbot sở hữu schema `ai_chatbot`, ba bảng knowledge_documents, knowledge_revisions, knowledge_activity và migration history riêng. Áp dụng migration `AddKnowledgeDocuments` trước khi dùng trang. Không cần API key AI để quản lý tài liệu.

Kiểm thử bao gồm phân quyền theo role + permission, giấu nháp khỏi Staff, nguồn cư dân loại nội bộ, lịch sử bất biến, Unicode, thu hồi xuất bản, conflict và transaction PostgreSQL. Migration không chạm dữ liệu tài chính.
