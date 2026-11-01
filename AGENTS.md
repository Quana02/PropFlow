# Quy tắc làm việc trong PropFlow

- Luôn giao tiếp với người dùng bằng tiếng Việt.
- Áp dụng các quy tắc dự án trong `.agents/rules/propflow-core.md`.

## Dọn tiến trình chạy nền

- Sau khi kiểm tra hoặc chạy thử xong, luôn dừng mọi tiến trình chạy nền do agent tạo trong tác vụ, bao gồm API, Web, `dotnet run`, `dotnet watch`, máy chủ xem trước và các tiến trình khởi chạy liên quan.
- Không để tiến trình tiếp tục chạy sau khi kết thúc lượt làm việc, trừ khi người dùng yêu cầu rõ ràng giữ chạy.
- Ghi nhận PID khi khởi chạy; chỉ dừng đúng tiến trình do agent tạo, không dừng tiến trình của người dùng hoặc ứng dụng khác.
- Trước khi báo hoàn tất, kiểm tra các tiến trình đã dừng và các cổng do chúng sử dụng đã được giải phóng. Không khởi động lại ứng dụng chỉ để rồi để nó chạy nền.
- Khi người dùng muốn tự chạy lệnh trong terminal, để cổng và file build được giải phóng cho họ.
