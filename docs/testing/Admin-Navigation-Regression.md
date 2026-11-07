# Kiểm tra hồi quy điều hướng quản trị

## Lỗi đã xác nhận và nguyên nhân

Chuyển từ `activity` sang `payment` lần đầu đổi nội dung nhưng icon vẫn ở nhật ký. Console báo `LocationChangeException` với inner exception `Collection was modified` trong `SupplyParameterFromQueryValueProvider.OnLocationChanged`.

`PaymentDestinationSettings` được mount có điều kiện trong lúc trang cha nhận query `tab`. Component con cũng đăng ký `SupplyParameterFromQuery`, làm thay đổi tập subscriber đang được duyệt trên .NET 8. Sự kiện bị ngắt trước khi các link nhận cập nhật. Test menu riêng không bao phủ lỗi này.

Cách sửa: trang `AdminDashboard` nhận cả `tab` và `paymentEnvironment`, truyền môi trường xuống component thanh toán bằng parameter thông thường. Component con không tự đăng ký query.

## Kịch bản kiểm tra trên ứng dụng WebAssembly thật

1. Đăng nhập ADMIN, mở `/admin/dashboard?tab=payment`.
2. Mỗi mục chỉ bấm một lần, theo thứ tự: Nhật ký → Thanh toán → Tài khoản → Thanh toán → Tổng quan → Nhật ký → Thanh toán.
3. Sau mỗi lần bấm, kiểm tra URL, nội dung, duy nhất một `.admin-nav-link.is-selected` và duy nhất một link có `aria-current="page"`; tất cả phải trùng mục vừa chọn.
4. Back về Nhật ký, Forward về Thanh toán; icon phải đổi theo.
5. Console không được phát sinh `LocationChangeException` hoặc `Collection was modified`.
6. Liên kết trả về từ SePay `/admin/dashboard?tab=payment&paymentEnvironment=live` vẫn phải mở phần SePay thật. Không nhập token hoặc thay đổi cấu hình để kiểm tra điều hướng.

Ngày 05/11/2026: đã xác nhận lỗi trước sửa; bước 1–5 đạt sau sửa trên ứng dụng thật tại cổng Web hiện có. 24 unit test liên quan menu, overview và payment settings đạt. Bản thử menu độc lập không được dùng thay thế kiểm tra trang thật ở trên.
