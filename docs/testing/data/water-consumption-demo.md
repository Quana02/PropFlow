# Bộ dữ liệu mẫu phân tích nước

Dùng cho Task 1 với cấu hình mặc định: ít nhất sáu kỳ tham chiếu hợp lệ. File `water-consumption-demo.json` chứa các body API và kết quả kiểm tra. Đây là dữ liệu giả lập cho một căn hộ demo.

## Đã nạp vào database local

**Cập nhật:** đã bổ sung cả điện theo yêu cầu tiếp theo. Dùng [hướng dẫn điện và nước](utility-consumption-demo.md) để xem kết quả hiện tại; phần bên dưới mô tả bước nạp nước ban đầu.

Theo yêu cầu ngày 16/11/2026, đã nạp mười kỳ 01–10/2026 vào căn hộ **DEMO-1801** (`00000000-0000-4000-8000-00000000d002`) trong database local. Trước khi nạp đã sao lưu; giữ nguyên các hóa đơn yêu cầu dịch vụ đang có và chỉ số của căn hộ khác. Không tạo hóa đơn hoặc thay đổi thanh toán.

Vào **Manager → Phân tích điện nước**, chọn **DEMO-1801**, rồi chọn kỳ **08/2026**, **09/2026**, **10/2026** để so sánh bình thường, tăng cao và giảm mạnh. Không cần nhập lại bảng bên dưới. Script thao tác là `tools/database/seeds/20261116_water_consumption_demo.sql`; script kiểm tra dữ liệu hiện có, từ chối ghi đè và ghi lịch sử nhập mẫu riêng.

## Nhập trên giao diện

1. Đăng nhập **Manager → Quản lý căn hộ** (`/apartments`). Chọn một căn hộ demo đang hoạt động, chưa có chỉ số và chưa có hóa đơn trong các kỳ bên dưới.
2. Bấm biểu tượng **Nhập điện và nước đã dùng**. Chọn tháng sử dụng, nhập cột **Chỉ số nước hiện tại**, rồi lưu. Nhập lần lượt từ tháng 01 đến tháng 10/2026. Điện có thể để trống khi chỉ kiểm tra phân tích nước.
3. Không nhập cột “Nước dùng trong tháng” vào ô công tơ: đây là lượng hệ thống tự tính bằng chỉ số hiện tại trừ chỉ số trước.
4. Vào **Phân tích điện nước** (`/operations/utility-insights`), chọn từng kỳ 08, 09 và 10/2026, chọn đúng căn hộ rồi xem biểu đồ. Bấm **AI giải thích thêm** để kiểm tra phần diễn giải Gemini.

| Tháng sử dụng | Chỉ số nước trước (m³) | Chỉ số nước hiện tại cần nhập (m³) | Nước dùng trong tháng (m³) | Kết quả dự kiến |
|---|---:|---:|---:|---|
| 01/2026 | 0 | **0** | 0 | Mốc công tơ ban đầu; chưa dùng làm kỳ tham chiếu |
| 02/2026 | 0 | **11,2** | 11,2 | Chưa đủ lịch sử |
| 03/2026 | 11,2 | **23,6** | 12,4 | Chưa đủ lịch sử |
| 04/2026 | 23,6 | **35,6** | 12 | Chưa đủ lịch sử |
| 05/2026 | 35,6 | **48** | 12,4 | Chưa đủ lịch sử |
| 06/2026 | 48 | **60** | 12 | Chưa đủ lịch sử |
| 07/2026 | 60 | **72,4** | 12,4 | Chưa đủ lịch sử: mới có năm kỳ hợp lệ trước tháng này |
| 08/2026 | 72,4 | **84,8** | 12,4 | **Trong mức thông thường**; đủ sáu kỳ tham chiếu |
| 09/2026 | 84,8 | **124,8** | 40 | **Tăng bất thường cao**, cao hơn mức tham chiếu 12 m³ khoảng **233,3%** |
| 10/2026 | 124,8 | **127,9** | 3,1 | **Cần theo dõi**, thấp hơn mức tham chiếu 12,4 m³ khoảng **75%** |

Tháng có số ngày khác nhau nên lượng dùng bình thường không giống nhau hoàn toàn. Dữ liệu từ tháng 02 đến tháng 08 tương ứng 0,4 m³/ngày. Tháng 09 tăng mạnh, còn tháng 10 giảm mạnh nhưng chỉ số công tơ vẫn tăng.

## Gửi bằng Swagger

Đăng nhập bằng tài khoản Manager và dùng xác thực Bearer của API. Gọi lần lượt:

```http
PUT /api/v1/billing/cycle/apartments/{apartmentId}/utilities
```

Mỗi phần tử trong `requests` của file JSON là một body hoàn chỉnh. Ví dụ cho tháng 09:

```json
{
  "usageMonth": "2026-09-01",
  "currentElectricityReading": null,
  "currentWaterReading": 124.8
}
```

JSON dùng dấu chấm thập phân. Giao diện hiển thị theo định dạng Việt Nam. Chưa có chức năng tải file JSON/CSV trực tiếp trên form công tơ; có thể nhập theo bảng hoặc gửi từng request qua Swagger.

## Điều kiện để kết quả khớp

- Chọn cùng một căn hộ, không bỏ tháng mốc 01/2026 và không nhảy tháng. Chỉ số ban đầu giả định bằng không. Nếu căn hộ đã có công tơ, dùng căn hộ demo khác để tránh làm thay đổi dữ liệu thật.
- Không cần phát hành hóa đơn để Manager xem phân tích. Đừng phát hành hóa đơn giả chỉ để chạy thử. Những kỳ đã có hóa đơn bị khóa sẽ không cho sửa chỉ số.
- Cư dân chỉ xem được kỳ có hóa đơn phát hành cho tài khoản của mình. Lịch sử tham chiếu cũng phải thuộc quyền xem của người nhận kỳ đó; chỉ nhập công tơ chưa đủ để xuất hiện ở trang cư dân.
- Phân tích thống kê chạy khi không có Gemini. Phần giải thích phụ thuộc cấu hình, hạn mức và khả năng đáp ứng của Gemini. Nội dung câu trả lời có thể thay đổi; các số liệu và mức cảnh báo nằm trong `expectedResults` là phần kiểm tra chính.
- Bộ mẫu kiểm tra mức tiêu thụ bất thường, không chứng minh căn hộ bị rò rỉ nước.
