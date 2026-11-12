# AI hỗ trợ vận hành nội bộ PropFlow

## Cơ sở thực tế

- **Phân loại phiếu trước khi phân công.** IBM Maximo Work Order Intelligence phân tích mô tả phiếu công việc để đề xuất mã sự cố. Manager dùng gợi ý để chọn mã phù hợp và phân công kỹ thuật viên. Đây là ví dụ ứng dụng trong sản phẩm. Hiệu quả trên dữ liệu tiếng Việt của PropFlow cần đo riêng. [IBM Maximo Work Order Intelligence](https://www.ibm.com/new/announcements/maximo-ai-alm)
- **Ưu tiên theo ảnh hưởng và độ thiết yếu của thiết bị.** IFMA lưu ý rằng số phiếu bảo trì phòng ngừa quá hạn có ý nghĩa hơn khi xét mức độ thiết yếu của thiết bị. PropFlow có thể đưa thiết bị, lịch sử liên quan và số lần tái diễn vào phần căn cứ để Manager xem xét. [IFMA, Using KPIs to Drive Results](https://fmj.ifma.org/using-kpis-to-drive-results)
- **Bảo trì dự báo cần dữ liệu điều kiện vận hành.** Một triển khai thực tế thu thập dữ liệu như độ rung, áp suất hoặc nhiệt độ, xây dựng đường cơ sở theo thiết bị và theo dõi sự sai lệch. DOE cũng mô tả cách hệ thống tòa nhà phát hiện lỗi bằng dữ liệu thu thập liên tục. Với mô tả phiếu, thời điểm và lịch sử xử lý, phạm vi phù hợp của đồ án là phát hiện sự cố lặp và đề xuất kiểm tra. [IBM, Predictive Maintenance](https://www.ibm.com/think/topics/predictive-maintenance), [DOE, Energy Management Information System Capabilities](https://www.energy.gov/cmei/femp/energy-management-information-system-capabilities)
- **Người phụ trách duyệt và nhóm đánh giá bằng dữ liệu có đáp án.** NIST khuyến nghị ghi rõ giới hạn sử dụng đầu ra, trách nhiệm giám sát và so kết quả với dữ liệu có đáp án đã biết. Phiếu gợi ý có thể giữ nhãn AI và nhãn Manager chốt để so sánh. [NIST AI 600-1, MAP 2.2 và MP-2.3-001](https://nvlpubs.nist.gov/nistpubs/ai/NIST.AI.600-1.pdf)

## Phương án cho đồ án

1. **FE-10, phân loại yêu cầu.** Dùng mô tả tiếng Việt để đề xuất một loại yêu cầu từ danh mục đã duyệt. Manager thấy câu hoặc dấu hiệu dẫn đến gợi ý và có thể chấp nhận, sửa hoặc bỏ qua. So sánh bộ quy tắc từ khóa và mô hình trên cùng tập kiểm thử độc lập.
2. **FE-11, ưu tiên và gợi ý xử lý.** Tách mô tả thành triệu chứng, vị trí và dấu hiệu ảnh hưởng. Backend áp dụng các quy tắc nghiệp vụ đã duyệt để xác định mức cảnh báo và truy xuất căn cứ. Manager duyệt độ ưu tiên, nhân sự hoặc hướng bảo trì theo luồng hiện hữu. Các dấu hiệu cần chuyển người xử lý ngay được kiểm tra bằng quy tắc trước khi dùng gợi ý AI.
3. **Sự cố lặp theo tài sản.** Thống kê phiếu liên quan bằng mã tài sản và khoảng thời gian. Dùng AI tìm mô tả tương tự hoặc tóm tắt triệu chứng từ các phiếu đã truy xuất. Hiển thị mã phiếu, mốc thời gian và lịch sử sửa chữa để Manager quyết định có kiểm tra chuyên sâu hay không. Khi chưa xác định được tài sản, hiển thị kết quả như ứng viên cần xác minh.
4. **Bàn giao ca hoặc hướng dẫn kỹ thuật nội bộ.** Sau khi hoàn thiện FE-10 và FE-11, có thể tạo tóm tắt các việc đang mở từ dữ liệu có quyền đọc, hoặc gợi ý checklist từ quy trình bảo trì có phiên bản và trích dẫn. Backend tính số lượng và trạng thái, AI hỗ trợ diễn giải để người phụ trách duyệt.

PropFlow đã có đối tượng lưu kết quả phân loại và khuyến nghị. Màn hình Manager có nguồn đọc các gợi ý đã lưu. Việc sinh gợi ý từ phiếu mới và luồng duyệt cần được nối vào quy trình. Yêu cầu dịch vụ và công việc bảo trì có thể tham chiếu cùng thiết bị hoặc cơ sở vật chất. [Mô hình phân loại](../../src/Modules/AiClassification/Domain/Classifications/AiRequestClassification.cs), [mô hình khuyến nghị](../../src/Modules/AiRecommendation/Domain/Recommendations/AiRequestRecommendation.cs), [nguồn đọc gợi ý](../../src/PropFlow.Api/Composition/ManagerServiceRequestInsightSource.cs), [yêu cầu dịch vụ](../../src/Modules/ServiceRequests/Domain/ServiceRequests/ServiceRequest.cs), [công việc bảo trì](../../src/Modules/Maintenance/Domain/MaintenanceTasks/MaintenanceTask.cs)

## Demo và đánh giá

- Dùng các phiếu mẫu đã gán nhãn, có ca diễn đạt khác nhau cho cùng lỗi, mô tả thiếu vị trí, hai vị trí dễ nhầm và các trường hợp cần chuyển người xử lý ngay. Giữ một nhóm phiếu để đo sau khi chốt cấu hình.
- Demo từ một phiếu mới đến quyết định của Manager. Mở từng phiếu bằng chứng khi hệ thống phát hiện lỗi lặp. Cho thấy Manager sửa một gợi ý và tiếp tục xử lý phiếu khi AI hết thời gian phản hồi.
- Báo cáo F1 theo từng nhóm yêu cầu hoặc macro-F1, tỷ lệ bỏ sót ca ưu tiên cao, tỷ lệ Manager chấp nhận hoặc sửa gợi ý và thời gian xử lý trung vị. Ghi số mẫu và chỉ diễn giải mức cải thiện trong phạm vi dữ liệu đã đo. [NIST AI 600-1, MP-2.3-001](https://nvlpubs.nist.gov/nistpubs/ai/NIST.AI.600-1.pdf)
