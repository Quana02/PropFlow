# Chuẩn bị triển khai PropFlow

1. Chốt môi trường, người giữ tài khoản và ngày dự kiến theo [hướng dẫn triển khai cùng chủ dự án](guided-deployment-plan.md).
2. Rà điều kiện trước khi mở staging theo [checklist](deployment-preflight-checklist.md) và [tham chiếu cấu hình](deployment-configuration-reference.md).
3. Chuẩn bị database và thử khôi phục theo [migration và phục hồi](database-migration-and-recovery.md).
4. Đóng gói và kiểm chứng theo [runbook](authorization-hardening-runbook.md).
5. Nghiệm thu từng tích hợp theo [bảng kiểm provider](external-provider-verification.md).

Source hiện nhắm .NET 8. Microsoft kết thúc hỗ trợ .NET 8 ngày 10/11/2026. Chốt nâng cấp lên runtime LTS còn hỗ trợ và chạy hồi quy trước staging/pilot có dữ liệu thật. Một bản Release chạy được trên loopback chỉ chứng minh khả năng chạy của source hiện tại. [Chính sách hỗ trợ .NET](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)

Lượt kiểm chứng gần nhất đã thấy runtime 8.0.33 trong PID Release, thuộc dải ảnh hưởng của advisory SignalR. Chủ dự án phê duyệt phương án xử lý runtime riêng trước khi mở ingress công khai. Bộ hướng dẫn hiện tại tiếp tục dùng cho rehearsal cô lập bằng dữ liệu tổng hợp. [Đánh giá runtime](../audits/propflow-runtime-security-risk-assessment.md)
