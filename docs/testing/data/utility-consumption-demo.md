# Dữ liệu mẫu điện và nước — DEMO-1801

Theo yêu cầu ngày 16/11/2026, đã bổ sung điện vào mười kỳ mẫu nước của căn hộ **DEMO-1801** trong database local. Dữ liệu nước và hóa đơn cũ giữ nguyên. Đây là dữ liệu giả lập để thử Task 1; không dùng làm số đo thực tế.

## Xem kết quả

Vào **Manager → Phân tích điện nước**, chọn căn **DEMO-1801**, rồi chọn kỳ và bấm **Xem phân tích**:

| Kỳ sử dụng | Điện dùng | Nước dùng | Kết quả cho cả hai |
|---|---:|---:|---|
| 07/2026 | 310 kWh | 12,4 m³ | Chưa đủ sáu kỳ tham chiếu |
| 08/2026 | 310 kWh | 12,4 m³ | Trong mức thông thường |
| 09/2026 | 900 kWh | 40 m³ | Tăng bất thường cao |
| 10/2026 | 62 kWh | 3,1 m³ | Giảm mạnh, cần theo dõi |

Tháng 09: điện tăng 200% so với tham chiếu 300 kWh; nước tăng khoảng 233,3% so với tham chiếu 12 m³. Tháng 10: điện giảm 80% so với 310 kWh; nước giảm 75% so với 12,4 m³. Mô hình đã quy đổi theo số ngày trong tháng.

## Chỉ số công tơ tích lũy

| Kỳ | Điện hiện tại (kWh) | Nước hiện tại (m³) |
|---|---:|---:|
| 01/2026 | 0 | 0 |
| 02/2026 | 280 | 11,2 |
| 03/2026 | 590 | 23,6 |
| 04/2026 | 890 | 35,6 |
| 05/2026 | 1200 | 48 |
| 06/2026 | 1500 | 60 |
| 07/2026 | 1810 | 72,4 |
| 08/2026 | 2120 | 84,8 |
| 09/2026 | 3020 | 124,8 |
| 10/2026 | 3082 | 127,9 |

File `utility-consumption-demo.json` chứa body API và kết quả dự kiến. Mẫu nước cũ được giữ làm bản riêng; script nạp nước cũ chỉ dành cho bước nạp nước ban đầu. Sau khi đã có điện, không chạy lại script nước để ghi đè.

Script bổ sung điện: `tools/database/seeds/20261116_add_electricity_to_water_demo.sql`. Script chỉ nhận các dòng mẫu nước đã biết, từ chối thay chỉ số điện khác mẫu hoặc kỳ đã khóa, lưu lịch sử nhập và không tạo trùng khi chạy lại. Trước khi nạp đã sao lưu local.

Bấm **AI giải thích thêm** để thử Gemini. Mô hình thống kê vẫn chạy nếu Gemini chưa sẵn sàng. Dữ liệu chưa phát hành hóa đơn được kiểm tra ở màn hình Manager; quyền xem của cư dân vẫn theo hóa đơn và lịch sử người nhận hiện có.
