# PropFlow — Quy tắc Backend dành cho AI Agent

**Kiến trúc Backend:** Modular Monolith
**Kiến trúc nội bộ module:** Clean Architecture Principles
**Tổ chức Application/Presentation:** Vertical Slice theo Use Case đã phê duyệt
**Deployment:** Một ASP.NET Core Web API process/deployable
**Database:** Một PostgreSQL database, MUST sử dụng schema-per-module cho các business module có persistence.
**Composition Root:** `PropFlow.Api`
**Giao tiếp xuyên module:** Public Contracts + Integration Events khi phù hợp
**Cấu trúc vật lý mặc định:** Một main `.csproj` cho mỗi business module
**Công nghệ:** ASP.NET Core Web API, PostgreSQL, JWT, Swagger/OpenAPI; Frontend: Blazor Web App + Global Interactive WebAssembly, `prerender: false` cho application shell/routes
**Ngôn ngữ website:** Tiếng Việt

**Phạm vi tài liệu:** Đây là coding/architecture rule cho AI Agent, không phải DevOps runbook. Chỉ giữ deployment concern khi nó ảnh hưởng trực tiếp đến code architecture, configuration, API compatibility, security boundary hoặc khả năng FE–BE tích hợp sau publish.

## 1. Thứ tự ưu tiên
1. Quy định pháp luật hiện hành có hiệu lực và áp dụng cho hoạt động cụ thể; khi chưa rõ phải yêu cầu xác minh, không tự kết luận tuân thủ.
2. Đặc tả tính năng và quy tắc nghiệp vụ đã phê duyệt.
3. Tài liệu này.
4. Quy ước repository không mâu thuẫn với các mục trên.
5. Quy ước ASP.NET Core.
6. Đề xuất của Agent.

Không tự phát minh nghiệp vụ, actor, trạng thái, quyền hoặc phạm vi. Khi có xung đột phải báo cáo và đề xuất phương án phù hợp.

### 1.1. Phân loại mức độ áp dụng quy tắc

Toàn bộ tài liệu dùng thống nhất 4 mức sau; mọi chữ "bắt buộc" từ đây trở đi được hiểu theo MUST trừ khi ghi rõ khác:

- **MUST / BẮT BUỘC** — phải thực hiện khi thuộc phạm vi, không tự bỏ qua.
- **SHOULD / KHUYẾN NGHỊ** — mặc định nên làm; nếu không áp dụng phải ghi rõ lý do.
- **OPTIONAL / TÙY CHỌN** — chỉ triển khai khi có giá trị thực sự và không mở rộng scope trái phép.
- **DECISION REQUIRED / CẦN QUYẾT ĐỊNH** — Agent không được tự chốt; phải có đặc tả/ADR/quyết định được phê duyệt trước khi merge, và không hard-code như thể đã final trong lúc chờ quyết định.

Không biến khuyến nghị production thành scope đồ án mới; không biến một mục DECISION REQUIRED thành mặc định ngầm chỉ vì thiếu thời gian chờ phê duyệt.

### 1.2. Định tuyến đọc rule theo loại task (tránh đọc toàn bộ tài liệu mỗi lần)

Tài liệu này dài; không phải task nào cũng cần đọc hết. Agent nên:

1. Luôn đọc phần **core** (ngắn, áp dụng cho mọi task): mục 1 (ưu tiên/phân loại), 4 (ngôn ngữ), 5 (PII), 7 (multi-building scope), 8 (dependency/ownership tổng quát), 10 (phân quyền), 22–24 (truy vết UC, kiểm soát thay đổi, quy tắc cuối).
2. Chỉ đọc thêm đúng mục liên quan vùng code đang đụng tới, theo bảng dưới — dùng `view` với line range hoặc tìm theo số mục thay vì đọc tuần tự từ đầu tới cuối.

| Task đụng tới... | Đọc thêm mục |
|---|---|
| Tạo module mới, đổi cấu trúc project/solution, cross-module call, CQRS/MediatR | 2 (đặc biệt 2.3, 2.6, 2.8 — xem ghi chú bên dưới) |
| Thiết kế/đổi API contract, DTO, error format cho FE, CORS ở composition root | 3 |
| Login, JWT, session, password, refresh token, rate limit/lockout | 9 (+ 25.2 nếu đụng số liệu TTL/lockout/retention cụ thể) |
| Ranh giới nghiệp vụ giữa các FE/module (vd. Service Request vs Complaint) | 6, 11 |
| Query/transaction PostgreSQL, EF Core, tiền tệ, concurrency | 12 |
| Gọi AI provider (classification/recommendation/chatbot), quota/cost | 13, 14 |
| Thêm/đổi endpoint, validation, ProblemDetails | 15 |
| Domain event, outbox, notification liên module | 16 |
| Upload/download file, attachment | 17 |
| Soft-delete, xóa/ẩn dữ liệu, yêu cầu xóa dữ liệu cá nhân | 18, 19 |
| Reporting, logging, health check | 20 |
| Viết test, Definition of Done | 21 |

Mục 2 là phần kiến trúc chi tiết và phần lớn chỉ cần khi **tạo mới hoặc thay đổi cấu trúc module/project**. Nếu chỉ sửa logic bên trong một module đã tồn tại đúng cấu trúc, agent chỉ cần nắm ranh giới ở 2.3 (dependency), 2.6 (giao tiếp xuyên module) và 2.8 (database boundary) — không cần đọc lại toàn bộ cây thư mục và các ví dụ ở 2.1/2.4/2.5/2.9 mỗi lần.

## 2. Kiến trúc Backend bắt buộc

PropFlow Backend sử dụng **Modular Monolith**. Đây là kiến trúc cấp hệ thống và là quyết định bắt buộc. Không tự chuyển sang microservices, distributed system, service mesh hoặc external broker architecture nếu chưa có ADR và yêu cầu phạm vi mới được phê duyệt.

Bên trong mỗi business module, áp dụng **Clean Architecture Principles** để giữ dependency hướng vào Domain/Application, nhưng **không mặc định tách mỗi layer thành project riêng**. Application và Presentation tổ chức theo **Vertical Slice theo Use Case đã phê duyệt**, để mỗi use case có command/query/request, handler/use-case service, validator, result/response và tests gần nhau về mặt logic.

Kiến trúc được hiểu theo ba tầng quyết định:

```text
PropFlow Backend
│
├── System Architecture
│   └── Modular Monolith
│
├── Internal Module Architecture
│   └── Clean Architecture Principles
│
└── Use-case Organization
    └── Vertical Slice by Approved Use Case
```

### 2.1. Solution baseline

Cấu trúc mặc định:

```text
PropFlow.sln
│
├── src/
│   ├── PropFlow.Api/
│   │   ├── Program.cs
│   │   ├── Middleware/
│   │   ├── Composition/
│   │   └── Configuration/
│   │
│   ├── Modules/
│   │   ├── Authentication/
│   │   │   ├── Domain/            (logical folder, KHÔNG phải project riêng)
│   │   │   ├── Application/       (logical folder, KHÔNG phải project riêng)
│   │   │   ├── Infrastructure/    (logical folder, KHÔNG phải project riêng)
│   │   │   ├── Presentation/      (logical folder, KHÔNG phải project riêng)
│   │   │   ├── Contracts/         (logical folder, KHÔNG phải project riêng)
│   │   │   └── Authentication.csproj   ← MỘT project chính chứa tất cả các folder trên (xem 2.2)
│   │   │
│   │   ├── Residents/
│   │   ├── Apartments/
│   │   ├── PropertyAssets/
│   │   ├── ServiceRequests/
│   │   ├── Complaints/
│   │   ├── Maintenance/
│   │   ├── Billing/
│   │   ├── Payments/
│   │   ├── AiClassification/
│   │   ├── AiRecommendation/
│   │   ├── AiChatbot/
│   │   ├── Communication/
│   │   ├── Reporting/
│   │   └── Administration/
│   │
│   └── Shared/
│       ├── Kernel/
│       ├── Infrastructure/
│       └── AI/
│
└── tests/
    ├── ArchitectureTests/
    ├── IntegrationTests/
    └── Modules/
```

Tên project thực tế có thể theo convention repository, nhưng **hình dạng kiến trúc và dependency rule không được thay đổi** chỉ vì đổi tên folder/project.

### 2.2. Một main project cho mỗi business module

Baseline PropFlow là **một main `.csproj` cho mỗi business module**. Các vùng `Domain`, `Application`, `Infrastructure`, `Presentation`, `Contracts` là logical boundaries bên trong module.

Không mặc định tạo:

```text
Authentication.Domain.csproj
Authentication.Application.csproj
Authentication.Infrastructure.csproj
Authentication.Presentation.csproj
Authentication.Contracts.csproj
```

cho mọi module.

Chỉ tạo project `Modules.<Name>.Contracts` riêng khi có compile-time cross-module dependency thực sự hoặc ADR của solution chuẩn hóa cách này. Không tạo project rỗng chỉ để “đúng Clean Architecture”.

### 2.3. Dependency rule bên trong module

Dependency logic phải tuân thủ:

```text
Presentation
      │
      ▼
Application
      │
      ▼
Domain

Infrastructure
   └── implements abstractions/ports required by
       Application hoặc Domain
```

Quy tắc bắt buộc:
- Domain không phụ thuộc ASP.NET Core, EF Core, provider SDK, Infrastructure hoặc module khác.
- Application không phụ thuộc implementation Infrastructure.
- Presentation không chứa business workflow; chỉ bind input, authorize ở boundary phù hợp, gọi use case và map response.
- Infrastructure chứa implementation kỹ thuật như EF Core, external provider adapter, file storage, email, AI provider integration.
- `PropFlow.Api` là **Composition Root**, được phép reference/register các module để cấu hình DI, middleware, authentication, authorization, OpenAPI và startup; không chứa business rule của FE.

### 2.4. Vertical Slice theo Use Case

Application/Presentation phải ưu tiên tổ chức theo actor goal/use case thay vì gom toàn bộ class theo technical type.

Ví dụ phù hợp:

```text
Application/
└── SubmitServiceRequest/
    ├── SubmitServiceRequestCommand.cs
    ├── SubmitServiceRequestHandler.cs
    ├── SubmitServiceRequestValidator.cs
    └── SubmitServiceRequestResult.cs
```

Không khuyến khích biến Application thành dumping ground kiểu:

```text
Application/
├── Services/
├── Commands/
├── Queries/
├── Handlers/
├── Validators/
└── DTOs/
```

nếu cách tổ chức này làm mất traceability giữa FE/UC và implementation.

Vertical Slice **không đồng nghĩa bắt buộc dùng MediatR**, không đồng nghĩa Event Sourcing, không yêu cầu hai database đọc/ghi. Command/query separation có thể chỉ là logical separation trong cùng ASP.NET Core process.

### 2.5. CQRS và MediatR

- **CQRS vật lý/distributed:** không phải baseline.
- **Logical command/query separation:** được khuyến nghị khi làm rõ use case.
- **MediatR:** OPTIONAL/ADR-based, không phải yêu cầu kiến trúc bắt buộc.
- Không thêm MediatR chỉ vì “Vertical Slice thường đi với MediatR”.
- Nếu không dùng MediatR, handler/use-case service thuần C# + ASP.NET Core DI là hợp lệ.
- Nếu dùng MediatR, không để pipeline behavior trở thành nơi chứa business logic khó truy vết.

### 2.6. Giao tiếp xuyên module

Main project của module A **không được reference main project của module B** để truy cập Domain/Application/Infrastructure nội bộ.

Hợp lệ:

```text
ServiceRequests
    ├──→ Maintenance.Contracts
    └──→ Integration Event
```

Không hợp lệ:

```text
ServiceRequests ──X──> Maintenance.Application
ServiceRequests ──X──> Maintenance.Domain
ServiceRequests ──X──> Maintenance.Infrastructure
ServiceRequests ──X──> MaintenanceDbContext
```

Synchronous cross-module call chỉ qua public contract/abstraction được phê duyệt. Asynchronous/durable side effect có thể dùng integration event + transactional outbox theo mục 16. Không dùng event để né một invariant cần đồng bộ.

### 2.7. Shared không phải nơi chứa nghiệp vụ chung chung

`Shared` chỉ chứa technical/cross-cutting primitives thực sự dùng chung và ổn định, ví dụ:
- result/error primitives;
- clock/time abstraction;
- correlation/context abstraction;
- file/storage abstraction kỹ thuật;
- AI technical client abstraction;
- shared infrastructure bootstrap thật sự cross-cutting.

Không đưa vào `Shared`:
- business entity;
- business repository;
- business service;
- DTO nghiệp vụ chỉ vì hai module cùng cần;
- permission/business rule thuộc một module cụ thể;
- mutable master data.

Nếu hai module cần cùng một business capability, phải xác định **module owner** và expose qua Contracts/event, không “giải quyết” bằng cách chuyển logic vào Shared.

### 2.8. Database boundary

Một PostgreSQL database duy nhất cho toàn backend. Ưu tiên **schema-per-module**. Mỗi module sở hữu persistence boundary và migration history của mình. Không có global `AppDbContext` chứa toàn bộ entity hệ thống.

Cross-module reference dùng stable scalar ID; EF Core navigation xuyên module không được phép. Database FK xuyên module chỉ dùng khi là stable master reference và không chuyển ownership.

### 2.9. Kiểm soát kiến trúc bằng test

`ArchitectureTests` phải kiểm tra tối thiểu:
- Domain không reference ASP.NET Core/EF Core/Infrastructure;
- main module project không reference main module project khác trái phép;
- Contracts/public abstraction không lộ Infrastructure/Application implementation DTO;
- không có global `AppDbContext`;
- không có cross-module EF navigation;
- `PropFlow.Api` chỉ làm composition/startup, không chứa business handler/domain rule.

Architecture rule là **MUST**. Nếu implementation cần phá một rule, Agent phải dừng và yêu cầu ADR/approval thay vì tự “linh hoạt” kiến trúc.

## 3. Hợp đồng tích hợp Frontend–Backend

PropFlow frontend dùng **Blazor Web App với Global Interactive WebAssembly và `prerender: false` cho application shell/routes** (`PropFlow.Web` là server host/composition root; `PropFlow.Web.Client` là WASM client chứa routed business UI) và giao tiếp với `PropFlow.Api` chỉ qua HTTP API contract. `PropFlow.Api` không tham gia render Blazor; render-mode policy thuộc frontend nhưng là contract kiến trúc đã chốt mà backend integration không được giả định ngược lại.

```text
PropFlow.Web.Client → API Client / Feature Service → /api/v1/... → PropFlow.Api → Application Use Case → Domain
```

### 3.1. API contract
- Baseline route: `/api/v1/...`; không tùy tiện breaking change trong cùng version.
- Request/response dùng API DTO; không expose Domain Entity, EF Core Entity hoặc DbContext cho frontend.
- Swagger/OpenAPI phải khớp implementation về endpoint, schema, status code và authentication requirement; đây là nguồn tích hợp chính cho frontend.
- Pagination, filtering, sorting, validation/business error và permission code phải theo convention chung.
- Không tạo endpoint chỉ để khớp mock UI nếu không ánh xạ Feature/Use Case đã phê duyệt.

### 3.2. Error và authentication contract
- `400`: request/validation invalid.
- `401`: chưa xác thực hoặc access token thiếu/không hợp lệ/hết hạn.
- `403`: đã xác thực nhưng không đủ quyền.
- `404`: resource không tồn tại hoặc không được phép tiết lộ sự tồn tại.
- `409`: conflict/idempotency/concurrency/business-state conflict khi phù hợp.
- `5xx`: server/dependency failure.

Ưu tiên ASP.NET Core `ProblemDetails` hoặc contract tương đương với `code`, message tiếng Việt an toàn, `traceId` và field validation khi có. Frontend không phải parse exception text.

Với Blazor Web App + Global Interactive WebAssembly, business API nhận access token qua `Authorization: Bearer`, chỉ gửi từ `PropFlow.Web.Client` sau khi WASM client đã khởi tạo; không truyền token qua query string. Application shell/routes dùng `prerender: false`, vì vậy `PropFlow.Web` không prerender authenticated business UI, không gọi business API thay client và không suy ra authentication state từ refresh cookie. Refresh token dùng Secure/HttpOnly cookie theo auth baseline. Refresh/logout/revoke phải tương thích cookie/CSRF strategy. API auth failure trả JSON/ProblemDetails `401/403`, không redirect HTML login.

### 3.3. Configuration-safe integration
Không hard-code `localhost`, development port, frontend domain, production API domain, origin, external service URL, connection string hoặc secret trong business module/controller/application handler. Giá trị phụ thuộc môi trường phải đi qua ASP.NET Core configuration/options và được cấu hình tập trung tại Composition/Infrastructure boundary.

Code phải hỗ trợ same-origin (`/api/...`) hoặc separate-origin bằng configuration mà không sửa business feature code. Coding rule này không yêu cầu Agent thiết kế domain, certificate, CI/CD, backup schedule hay server infrastructure.

### 3.4. CORS boundary
CORS thuộc `PropFlow.Api`/Composition Root và lấy từ configuration. Business module không tự thêm CORS policy; không wildcard origin cùng credentials; CORS không thay authentication/authorization/Building scope; không thêm workaround CORS trong controller chỉ để môi trường dev chạy được.

### 3.5. Compatibility rule
Khi thay đổi endpoint, DTO, permission, auth flow hoặc error contract frontend đang sử dụng, Agent phải xác định contract bị ảnh hưởng, tránh breaking change chưa được phê duyệt, cập nhật OpenAPI/tests và không âm thầm đổi semantics chỉ vì refactor.

Mục tiêu là FE và BE có thể cấu hình để gọi nhau sau publish/deploy mà không phụ thuộc URL development hoặc implementation nội bộ của nhau.

## 4. Ngôn ngữ sản phẩm — BẮT BUỘC
PropFlow là website sử dụng tiếng Việt. Toàn bộ menu, nút, nhãn, trạng thái, validation, thông báo lỗi, email, thông báo trong ứng dụng, dashboard, báo cáo, nội dung trợ giúp và chatbot phải hiển thị tiếng Việt tự nhiên, nhất quán với nghiệp vụ quản lý chung cư Việt Nam.

Tên class, method, namespace, database, API route, permission code, event name và error code giữ bằng tiếng Anh. Không dùng tiếng Việt có dấu làm định danh lập trình. Không hard-code thông điệp tiếng Anh để hiển thị cho người dùng. Backend trả mã lỗi ổn định và thông điệp tiếng Việt an toàn; không trả exception, SQL hoặc stack trace.

Dữ liệu tiếng Việt phải được lưu và tìm kiếm đúng Unicode, không tự ý bỏ dấu hoặc sửa nội dung gốc. **SHOULD:** chuẩn hóa Unicode NFC tại input boundary cho các trường văn bản nghiệp vụ có search, comparison, uniqueness hoặc nguy cơ nhận input NFC/NFD khác nhau; không bắt buộc normalization cho mọi chuỗi. Nếu normalization ảnh hưởng uniqueness/search semantics thì phải áp dụng nhất quán và có test. Không tự động chuẩn hóa password, token, secret, opaque external identifier hoặc signed payload; các giá trị này tuân theo chính sách xử lý riêng. Ngày giờ hiển thị phù hợp Việt Nam, ví dụ dd/MM/yyyy; thời gian nghiệp vụ theo múi giờ tòa nhà (Asia/Ho_Chi_Minh trừ khi cấu hình khác). Tiền tệ và làm tròn theo quy tắc tài chính đã duyệt. Chatbot mặc định trả lời tiếng Việt.

Ví dụ:
```json
{"code":"PAYMENT_ALREADY_CONFIRMED","message":"Khoản thanh toán này đã được xác nhận.","traceId":"..."}
```

Frontend **Blazor Web App + Interactive WebAssembly** chịu trách nhiệm trình bày tiếng Việt; backend cung cấp dữ liệu, mã trạng thái và hợp đồng ổn định. Ẩn nút trên frontend không thay thế phân quyền backend.

## 5. Bảo vệ dữ liệu cá nhân — BẮT BUỘC

PropFlow xử lý dữ liệu cá nhân của cư dân và nhân viên. Phải tuân thủ quy định pháp luật Việt Nam hiện hành áp dụng tại thời điểm xử lý, bao gồm các quy định về bảo vệ dữ liệu cá nhân và nghĩa vụ lưu giữ hồ sơ liên quan. Không coi việc tuân thủ riêng một văn bản là bảo đảm tuân thủ toàn bộ pháp luật. Khi chưa rõ căn cứ hoặc nghĩa vụ, Agent phải báo cáo để xác minh, không tự đưa ra kết luận pháp lý.

- **Mục đích và tối thiểu hóa:** Agent không được tự thêm trường PII ngoài đặc tả/use case đã phê duyệt. Với PII đã có trong đặc tả, phải áp dụng data minimization, authorization, logging restriction và retention theo policy/quy định đã chốt; không yêu cầu Agent xin phê duyệt lại từng field đã có trong đặc tả, trừ khi phát hiện xung đột hoặc khoảng trống. Không thu thập dữ liệu chỉ để dự phòng.
- **Quyền chủ thể dữ liệu:** thiết kế quy trình tiếp nhận, xác minh, đánh giá và xử lý yêu cầu xem, sửa, xóa, rút đồng ý hoặc quyền khác theo quy định áp dụng. FE-02 sở hữu dữ liệu cư dân; FE-15 có thể điều phối yêu cầu quản trị; FE-01 sở hữu dữ liệu tài khoản. Không tự thêm quyền xóa trực tiếp mọi hồ sơ.
- **Bên thứ ba và chuyển dữ liệu:** trước khi gửi dữ liệu cho AI provider hoặc dịch vụ ngoài, phải đánh giá loại dữ liệu, mục đích, nơi xử lý, căn cứ pháp lý, hợp đồng và nghĩa vụ liên quan. Không mặc định mọi dữ liệu gửi tới provider nước ngoài đều có cùng chế độ pháp lý. Mặc định không gửi PII thô; chỉ gửi dữ liệu tối thiểu đã loại bỏ/giả danh hóa phù hợp, trừ khi có phê duyệt rõ ràng cho use case.
- **Retention:** xác định thời hạn lưu giữ theo loại dữ liệu, mục đích và nghĩa vụ pháp lý. Không tự đặt số năm hoặc xóa dữ liệu khi chưa có chính sách được phê duyệt. Schema phải hỗ trợ retention, hạn chế xử lý và xóa/ẩn danh hóa khi hợp pháp.
- **Logging:** không ghi password, token, secret, CCCD, số thẻ ngân hàng hoặc payload PII nhạy cảm vào log. Chỉ ghi metadata cần thiết, có kiểm soát truy cập và thời hạn lưu giữ.
- **Sự cố dữ liệu:** có quy trình ghi nhận, phân loại, cô lập, điều tra và thông báo sự cố theo nghĩa vụ pháp lý áp dụng; không tự công bố hoặc gửi dữ liệu nhạy cảm cho bên thứ ba.

## 6. Kiến trúc và module
Một backend deployable, một PostgreSQL database. Giữ 15 module: Authentication, Residents, Apartments, PropertyAssets, ServiceRequests, Complaints, Maintenance, Billing, Payments, AiClassification, AiRecommendation, AiChatbot, Communication, Reporting, Administration.

Mỗi module tuân thủ blueprint ở mục 2: một main module project với các logical boundaries Domain, Application, Infrastructure, Presentation và Contracts; Application/Presentation ưu tiên Vertical Slice theo Use Case.

Chỉ tạo assembly/project `Modules.<Name>.Contracts` riêng khi có **compile-time dependency xuyên module thực sự** hoặc ADR của solution chuẩn hóa cách này. Nếu contract chỉ dùng nội bộ module thì để trong namespace/thư mục `Contracts` của main module project; không tạo project rỗng chỉ để tăng số lượng. Main project của module A không được reference main project của module B. Cross-module synchronous dependency chỉ đi qua contract/public abstraction đã được phê duyệt.

Không tạo global Controllers/Services/Repositories/Models, GodService, CommonRepository hoặc AppDbContext chung. Không tự chuyển sang microservices hay thêm external message broker. Implementation không được lộ qua public contract. ArchitectureTests và các module tests liên quan phải được Agent chạy/xác minh trước khi coi thay đổi hoàn thành.

## 7. Multi-building và cách ly dữ liệu — BẮT BUỘC

PropFlow hỗ trợ phạm vi nhiều tòa nhà theo đặc tả. Multi-building không tự động đồng nghĩa SaaS multi-tenancy. Không tự thêm Tenant/Organization, subscription hoặc tenant isolation model khi chưa có quyết định kiến trúc và đặc tả riêng.

- Mỗi aggregate nghiệp vụ có phạm vi tòa nhà phải xác định được BuildingId trực tiếp hoặc qua quan hệ sở hữu được kiểm soát. Không bắt buộc mọi bảng đều có `building_id NOT NULL`; identity, role, permission, cấu hình toàn hệ thống và một số bảng con có thể không có cột này.
- Application layer phải enforce scope bằng access assignment và ownership thực tế. Không tin building_id do client gửi. Với truy vấn theo ID, phải kiểm tra tài nguyên thuộc phạm vi được phép trước khi trả dữ liệu hoặc thay đổi trạng thái.
- Administration sở hữu User–Building access assignment; PropertyAssets sở hữu Building master data. Không sao chép master entity để phân quyền.
- Reporting phải enforce scope trước khi trả dữ liệu. Quyền tổng hợp liên tòa nhà phải được đặc tả và cấp rõ ràng; không mặc định Admin có quyền xem toàn bộ dữ liệu cư dân/tài chính.
- PostgreSQL RLS là lớp phòng vệ bổ sung theo ADR riêng. Nếu triển khai, phải thiết kế database role, session/transaction context, connection pooling, reset context, migration/background jobs và Reporting; không bật RLS nửa vời.
- Test bắt buộc: người dùng tòa nhà A không đọc/sửa dữ liệu tòa nhà B bằng guessed ID, filter, export hoặc API khác; kiểm tra cả assignment và ownership cá nhân.

## 8. Phụ thuộc và ownership
Dependency rule nội bộ module (Presentation → Application → Domain, Infrastructure implement abstraction) và quy tắc giao tiếp xuyên module (chỉ qua public contract/stable ID/integration event, không inject Repository/DbContext/handler nội bộ/EF navigation xuyên module) đã quy định đầy đủ ở mục 2.3 và 2.6; áp dụng nguyên vẹn ở đây, không lặp lại.

Phần dưới đây là ranh giới **ownership khái niệm** giữa các entity/module dễ gây nhầm lẫn khi code:

User Account khác Resident và Apartment. Service Request khác Complaint và Maintenance Task. Maintenance Schedule khác Maintenance Task. Invoice khác Payment. Notification khác Announcement. FE-01 (Authentication — identity/credential/login) đến FE-15 (Administration — role/permission) đều có nghiệp vụ riêng, không được gộp; FE-10, FE-11 và FE-12 (AiClassification, AiRecommendation, AiChatbot theo thứ tự) có nghiệp vụ riêng.

## 9. Authentication và Administration
Authentication sở hữu identity, credential, password hash, refresh token, xác minh email, reset password và trạng thái bảo mật đăng nhập. Administration sở hữu role assignment, permission mapping, User–Building access assignment (mục 7/10) và quy trình cấp tài khoản nội bộ.

Administration có thể gọi Authentication contract để tạo identity rồi gán quyền. Authentication lấy access claims qua abstraction/projection không tạo circular dependency. Không truy vấn trực tiếp bảng nội bộ của Administration hoặc sao chép logic role assignment.

**Ownership tạo tài khoản cư dân — ĐÃ CHỐT:** Authentication sở hữu self-registration của Resident theo FE-01; Residents (FE-02) sở hữu Resident record và Resident–Apartment relationship, đồng thời cung cấp contract kiểm tra eligibility/liên kết hồ sơ hợp lệ. Administration chỉ provisioning tài khoản nội bộ và role/permission/access assignment. Resident không được tự tạo Resident record, tự claim Apartment hoặc tự tạo/chỉnh quan hệ cư trú để hoàn tất đăng ký.

**Chi tiết bảo mật — phần BẮT BUỘC TUYỆT ĐỐI (không tự thay đổi, không cần chờ đặc tả):**
- **Password hashing:** với ASP.NET Core, mặc định ưu tiên `Microsoft.AspNetCore.Identity.PasswordHasher<TUser>` hoặc `IPasswordHasher<TUser>` tương đương đã được framework hỗ trợ, vì có format marker/versioning và hỗ trợ nâng cấp hash khi policy thay đổi. Argon2id/bcrypt chỉ dùng khi ADR yêu cầu thư viện ngoài và đã đánh giá dependency/operational cost. **Không tự triển khai hashing/KDF, không dùng MD5/SHA1/SHA256 trần**, không lưu plaintext dưới bất kỳ hình thức nào (kể cả log, cache, tạm thời).
- **JWT:** không cho phép `alg=none` hoặc thuật toán không được phê duyệt; không tự verify JWT trùng lặp ở từng module trong cùng process (verify một lần ở lớp middleware chung). Signing key phải được quản lý qua configuration/secret provider và code không được khóa cứng vào một key bất biến trong source. **SHOULD:** hỗ trợ chiến lược key rotation khi môi trường triển khai/yêu cầu bảo mật cần; không bắt Agent tự xây key-ring, scheduler hoặc hạ tầng rotation nếu chưa có ADR/phạm vi được phê duyệt.
- Refresh token: bắt buộc xoay vòng (rotation) mỗi lần dùng, có thể thu hồi (revocable), **chỉ lưu hash trong database, không lưu plaintext**.
- Reset/verification token: bắt buộc ngẫu nhiên (CSPRNG), có hạn, dùng một lần, lưu hash khi persisted.
- Login, forgot password và registration bắt buộc có rate limiting; login bắt buộc có lockout/backoff.
- **Chống user-enumeration:** outward response cho credential không hợp lệ, tài khoản không tồn tại hoặc tài khoản bị khóa không được tiết lộ trạng thái tồn tại của tài khoản. Flow phải tránh timing difference rõ ràng có thể trở thành oracle thực tế; **không yêu cầu toàn bộ HTTP endpoint đạt constant-time tuyệt đối**. Không tự viết constant-time cryptographic primitive hoặc padding delay tùy tiện; dùng password hasher/framework đã được kiểm chứng và thiết kế error handling thống nhất.
- **CORS:** cấu hình và ràng buộc chung xem mục 3.4. Riêng cho auth: nếu refresh token dùng cookie xuyên origin, phải bật credentials chỉ cho origin được phép trong allow-list đã cấu hình cho **Blazor Web App + Interactive WebAssembly** (`PropFlow.Web`/`PropFlow.Web.Client`).
- **Rate limiting tổng quát:** ngoài login/forgot-password/registration, các endpoint ghi dữ liệu (đặc biệt Payments, Complaints) cũng cần rate limit hợp lý để chống abuse, không chỉ giới hạn ở nhóm auth.
- Bootstrap Admin và role/permission catalog qua seed/operational step có kiểm soát, idempotent; không có public bootstrap endpoint luôn mở.

**Chi tiết bảo mật — phần MẶC ĐỊNH ĐỀ XUẤT, chờ đặc tả/ADR chốt số liệu hoặc phương án cuối cùng (Agent không tự chốt và không code cứng như thể đã final):**
- **Password hasher/work factor:** baseline ASP.NET Core dùng `PasswordHasher<TUser>`/`IPasswordHasher<TUser>`. Nếu cần thay đổi iteration/work factor hoặc chuyển sang Argon2id/bcrypt, phải thực hiện qua cấu hình/ADR và có chiến lược rehash khi user đăng nhập; Agent không hard-code tham số tùy ý và không tự thêm package crypto chỉ vì “mạnh hơn”.
- Thuật toán ký JWT cụ thể: ưu tiên RS256/ES256 khi cần phân phối public key hoặc xác minh giữa nhiều service; HS256 chỉ dùng khi secret đủ mạnh và có quy trình quản lý/rotation — cần chốt loại nào trước khi implement.
- **Thời gian sống access token/refresh token:** mặc định đề xuất access token 15 phút; con số chính xác (access + refresh) phải do đặc tả/ADR quyết định, không tự đặt số rồi coi là final.
- **Ngưỡng lockout/rate-limit (số lần thử sai, thời gian khóa, thuật toán backoff):** đây là tham số nghiệp vụ-bảo mật cụ thể, chưa được đặc tả ở tài liệu này. Agent **không được tự phát minh con số** (ví dụ tự chọn "5 lần/15 phút") rồi code như một quyết định đã duyệt; phải báo cáo thiếu thông tin và đề xuất giá trị tạm để xác nhận trước khi merge, đồng thời tham số này phải cấu hình được (không hard-code) để đổi khi có quyết định chính thức.
- **Cơ chế lưu/truyền token ở client — ĐÃ CHỐT cho Blazor Web App + Global Interactive WebAssembly:** frontend gồm `PropFlow.Web` (server host/composition root, cấu hình Razor Components, Interactive WebAssembly endpoint, middleware và environment-backed configuration) và `PropFlow.Web.Client` (WASM client chứa application shell, routed business UI, auth state và browser-facing infrastructure); cả hai là project khác với `PropFlow.Api` — `PropFlow.Api` chỉ đóng vai trò backend JSON API, không tham gia render Blazor. Sau khi WASM client khởi tạo, access token mặc định chỉ giữ **trong memory của `PropFlow.Web.Client`** và gửi qua `Authorization: Bearer <token>`; không mặc định lưu access token dài hạn trong `localStorage`/`sessionStorage`.
- **Prerender và access token — ĐÃ CHỐT:** application shell/routes (bao gồm các route yêu cầu authentication) dùng `InteractiveWebAssemblyRenderMode(prerender: false)`. `PropFlow.Web` không prerender authenticated business UI, không gọi business API thay client, không dùng refresh cookie để tự suy ra user đã đăng nhập và không triển khai placeholder/loading chỉ để chờ chuyển từ server-side prerender sang WASM. Authentication state được xác lập trong `PropFlow.Web.Client` theo refresh flow đã phê duyệt. Agent không được tự bật lại prerender ở feature page/component con hoặc thêm server-side authentication-state serialization/persistence chỉ để “tối ưu Blazor”. Nếu sau này cần prerender/SEO cho một vùng public riêng, phải có ADR/rule mới được phê duyệt và không được làm thay đổi auth baseline của application authenticated.
- Refresh token mặc định được lưu trong **Secure + HttpOnly cookie**, browser tự gửi tới đúng endpoint refresh/logout/revoke theo cookie scope; `PropFlow.Web.Client` không được đọc giá trị refresh token bằng JavaScript/.NET phía client. Nếu frontend và API khác site/origin, cấu hình cookie/CORS phải tương thích (`SameSite=None; Secure` khi thực sự cần cross-site) và tuyệt đối không dùng wildcard origin với credentials.
- Các endpoint dựa trên cookie để thay đổi trạng thái như refresh/logout/revoke phải có **CSRF protection** phù hợp SPA (ví dụ ASP.NET Core Antiforgery với token/header hoặc cơ chế tương đương đã được ADR phê duyệt). Các API nghiệp vụ dùng Bearer token trong `Authorization` header không coi CSRF là cơ chế bảo vệ chính.
- Sau reload/tab restore, frontend lấy phiên mới qua refresh endpoint thay vì phụ thuộc vào persisted bearer token. Nếu sau này dùng BFF, thay đổi mô hình token phải có ADR riêng; Agent không tự chuyển sang BFF.
- **Phạm vi revoke khi Logout:** tài liệu này chỉ yêu cầu refresh token "có thể thu hồi", không quy định Logout thu hồi 1 phiên hiện tại hay toàn bộ phiên trên mọi thiết bị. Đây là hành vi nghiệp vụ cần map về đúng UC (mục 22); nếu UC không nói rõ, Agent phải hỏi lại thay vì tự chọn (mặc định "chỉ revoke phiên hiện tại" là lựa chọn phổ biến nhưng không được coi là ngầm định đã duyệt).
- **Retention của security/audit log liên quan login (lịch sử đăng nhập, số lần thử sai, IP, lockout event):** đây là log vận hành/bảo mật, có mục đích khác PII nghiệp vụ ở mục 5 (hồ sơ cư dân, tài chính...). Vẫn phải tuân thủ mục 5 về nguyên tắc chung (không tự đặt retention tùy tiện, không log password/token), nhưng cần xin xác nhận riêng một retention ngắn hạn hợp lý cho nhóm log này (ví dụ phục vụ điều tra brute-force/incident) thay vì để trống hoặc lưu vô thời hạn — không tự suy diễn theo retention của dữ liệu nghiệp vụ dài hạn khác.

## 10. Phân quyền
Mọi protected operation phải kiểm tra Authentication → Role/Permission → Business Scope (bao gồm scope tòa nhà theo mục 7). JWT và role không chứng minh quyền sở hữu tài nguyên.

**Scope model:**
- **Resident:** `Authenticated UserId` → Account–Resident linkage → `ResidentId` → Resident–Apartment relationship hợp lệ → `ApartmentId` → `BuildingId`. Backend **không được giả định `UserId == ResidentId`**, kể cả khi hiện tại cùng dùng kiểu UUID/GUID. Không tạo User–Building assignment cho Resident chỉ để đồng nhất mô hình.
- **Staff / Accountant / Manager:** building scope đến từ **User–Building access assignment** do Administration sở hữu; mỗi use case vẫn phải kiểm tra assignment/ownership nghiệp vụ cụ thể.
- **Admin:** có administrative scope nhưng **không mặc định là business superuser** và không mặc định được đọc toàn bộ dữ liệu cư dân/tài chính/vận hành.

PropertyAssets vẫn sở hữu Building master data. Không tin ID, role, permission hoặc `building_id` do frontend gửi.

## 11. Quy tắc nghiệp vụ
FE-01 sở hữu identity, đăng nhập, phiên làm việc và bảo mật tài khoản (không chứa role/permission — thuộc FE-15). FE-02 sở hữu hồ sơ cư dân và quan hệ cư dân–căn hộ, bảo toàn lịch sử. FE-03 sở hữu căn hộ; FE-04 sở hữu tòa nhà, cơ sở vật chất và thiết bị. Không sao chép master data.

FE-05 sở hữu vòng đời Service Request; Manager assign/reassign và đóng cuối cùng, Staff cập nhật công việc được giao. FE-06 sở hữu Complaint riêng; Manager phản hồi chính thức và đóng, Staff chỉ follow-up. FE-07 sở hữu lịch, task, kết quả và lịch sử bảo trì; không biến Service Request thành Maintenance Task.

FE-08 sở hữu phí và hóa đơn. FE-09 sở hữu payment, settlement và debt. Chỉ confirmed payment giảm công nợ; pending/rejected không giảm. Một payment thuộc một invoice, invoice có thể có nhiều payment. Không tự thêm overpayment, refund, credit balance hoặc multi-invoice allocation.

FE-10 sở hữu AI-assisted Service Request Classification, chỉ áp dụng cho Service Request hợp lệ; không phân loại Complaint trong baseline hiện tại. FE-11 sở hữu AI recommendation. FE-12 sở hữu AI chatbot. FE-13 sở hữu Notification và Announcement riêng. FE-14 chỉ đọc/tổng hợp, không ghi thay module nguồn. FE-15 quản trị quyền nhưng Admin không tự động sở hữu nghiệp vụ Manager/Accountant.

## 12. PostgreSQL và giao dịch
Ưu tiên một schema/module: auth, residents, apartments, property_assets, service_requests, complaints, maintenance, billing, payments, ai_classification, ai_recommendation, ai_chatbot, communication, reporting, administration.

Mỗi module sở hữu persistence boundary, thông thường một DbContext riêng và migration history riêng. Không map writable entity của module khác. Cross-module reference dùng scalar stable ID; database FK có thể dùng cho master reference ổn định, không dùng EF navigation xuyên module.

Với EF Core, ưu tiên `AsNoTracking()` cho query read-only, compiled query chỉ khi profiling chứng minh cần thiết, `ExecuteUpdate/ExecuteDelete` chỉ khi không phá invariant/domain behavior. Không dùng lazy loading mặc định cho aggregate/module boundary. Interceptor, SaveChanges hook hoặc global query filter chỉ dùng cho concern thật sự cross-cutting và phải tránh che giấu business rule.

Dùng migration, constraints, indexes, server-side pagination, async I/O và CancellationToken. Không ToList trước khi lọc dữ liệu lớn.

**Money baseline đã chốt:** hệ thống baseline chỉ dùng **VND**; multi-currency và exchange-rate conversion nằm ngoài 56 UC. Mọi tính toán tiền trong .NET dùng `decimal`; PostgreSQL dùng `numeric(18,0)` cho monetary amount và không dùng `float`/`double`. Billing quantity cần phần lẻ dùng `numeric(18,4)`. Mỗi `InvoiceItem.LineAmount` phải được làm tròn về 0 chữ số thập phân bằng `MidpointRounding.AwayFromZero` trước khi cộng tổng; `Invoice.Subtotal`/`TotalAmount` là tổng các `LineAmount` đã lưu. Invoice đã `ISSUED` giữ snapshot tài chính và không được tự tính lại từ `FeeRateRule` mới. Tổng payment `CONFIRMED` không được vượt outstanding/payable balance trong baseline đã duyệt.

**Timezone baseline đã chốt:** instant/event/audit timestamp dùng PostgreSQL `timestamptz` và được xử lý/lưu như UTC instant; calendar-only business value dùng PostgreSQL `date`. Mỗi Building sở hữu `time_zone_id` theo chuẩn **IANA**, baseline mặc định `Asia/Ho_Chi_Minh`. Due/overdue boundary, daily business reporting và lịch nghiệp vụ theo local time phải tính theo timezone của Building liên quan, không theo server local timezone và không dùng trực tiếp UTC calendar date. API trả timestamp dạng ISO-8601; frontend chuyển instant sang Building timezone khi hiển thị. Không lưu localized display string hoặc raw UTC offset như `+07:00` làm timezone identifier.

Financial state transition phải concurrency-safe và idempotent. Cùng Idempotency-Key + payload trả cùng logical result; cùng key khác payload trả conflict. Không giữ transaction khi gọi AI. Không tự dùng distributed transaction; yêu cầu atomic cross-module phải được phân tích và phê duyệt.

## 13. AI
AI chỉ gợi ý; Manager xác nhận/override gợi ý nghiệp vụ FE-10/11. FE-12 được tự động trả lời hướng dẫn và dữ liệu được phép, nhưng không tự thực hiện hành động nghiệp vụ hoặc đưa ra quyết định có thẩm quyền. Không tự assign Staff, tạo MaintenanceTask, đóng ServiceRequest, phân loại Complaint bằng FE-10 hoặc tự ghi nghiệp vụ qua chatbot. AI lỗi không chặn core workflow.

Provider SDK nằm sau abstraction. Shared/AI có thể chứa client, options, timeout/retry và serialization; prompt, output schema, validation và business fallback thuộc module tương ứng. Chỉ gửi dữ liệu cần thiết, được phép, và tuân thủ mục 5 (không gửi PII thô mặc định). Không cho LLM truy cập database không giới hạn. Chatbot trả lời tiếng Việt và không bịa thông tin có tính thẩm quyền.

## 14. Kiểm soát chi phí AI
Ba module AI (AiClassification, AiRecommendation, AiChatbot) gọi provider SDK bên ngoài phát sinh chi phí theo usage. Bắt buộc:
- Có giới hạn quota/rate limit gọi AI theo tòa nhà hoặc theo user, cấu hình được, để tránh chi phí không kiểm soát do abuse hoặc lỗi lặp (loop) ở FE.
- Timeout và retry (đã có ở mục 13) phải có giới hạn số lần rõ ràng, không retry vô hạn.
- Log số lượng request/token usage AI (không log nội dung prompt nhạy cảm — xem mục 20) để phục vụ giám sát chi phí vận hành.

## 15. API và validation
REST-style /api/v1, DTO riêng, validation server-side, error contract thống nhất và Swagger cập nhật. Controller chỉ bind input, gọi use case và map response. Không chứa business workflow, DbContext query hoặc SDK AI.

Phân biệt request validation, business validation và authorization. Dùng HTTP status theo bảng mã đã quy định ở mục 3.2 (không định nghĩa lại ở đây); lỗi hiển thị bằng tiếng Việt, không lộ thông tin kỹ thuật. Không dùng exception cho query miss thông thường nếu result type rõ hơn.

Trong ASP.NET Core, ưu tiên middleware/filter/policy chuẩn của framework cho authentication, authorization, exception handling, rate limiting và request pipeline. Không tự viết framework mini cho các concern mà ASP.NET Core đã cung cấp sẵn. Dùng DI container mặc định trừ khi có ADR khác. `ProblemDetails`/error contract có thể được chuẩn hóa ở lớp Presentation nhưng phải giữ `code`, message tiếng Việt an toàn và `traceId` ổn định theo quy ước PropFlow.

Với Blazor Web App + Interactive WebAssembly, Swagger/OpenAPI contract phải đủ ổn định để `PropFlow.Web.Client` tạo typed client hoặc viết API client theo feature. Backend không trả HTML redirect cho API authentication failure; trả `401/403` JSON/ProblemDetails phù hợp để WASM tự điều hướng UI. Không phụ thuộc server-side Blazor circuit/session state.

## 16. Events, Outbox và Notification
Source module sở hữu business event; Communication sở hữu delivery/history. Không truy cập NotificationDbContext từ module nguồn.

**Transactional outbox là MUST khi một business transaction đã commit nhưng side effect/event liên module không được phép bị mất nếu process crash.** Outbox record phải được ghi trong cùng database transaction với thay đổi nguồn. Ví dụ điển hình: `InvoiceIssued`, `PaymentConfirmed`, `PaymentRejected` khi chúng kích hoạt Notification, Reporting projection hoặc xử lý liên module cần delivery guarantee.

Với `ServiceRequestClosed`, `ComplaintClosed`, `MaintenanceTaskClosed` và event tương tự, dùng outbox khi có side effect liên module cần bảo đảm delivery; nếu chỉ là domain event nội bộ được xử lý đồng bộ trong cùng module/transaction thì không bắt buộc outbox.

Event có ID, name, version, payload, CorrelationId, timestamps, attempt count và status. Dispatcher thuộc module sở hữu outbox; consumer phải idempotent. Retry/backoff có giới hạn, poison event chuyển Failed và được giám sát. Không dùng event để che giấu invariant đồng bộ và không tạo event hàng loạt chỉ để “event-driven hóa” hệ thống.

## 17. Attachments
File I/O qua abstraction kỹ thuật chung; attachment metadata và ownership thuộc module nghiệp vụ. Storage key do server tạo. Kiểm tra kích thước, allow-list MIME cụ thể và file signature. Download phải kiểm tra quyền qua authenticated stream hoặc signed URL ngắn hạn; không public bucket.

Không xóa evidence lịch sử khi parent deactivated. Có cleanup temporary/orphan uploads, không xóa file đã gắn hồ sơ. Malware scanning được khuyến nghị; nếu chưa triển khai phải ghi rõ giới hạn.

## 18. Lịch sử và xóa dữ liệu
Bảo toàn lịch sử cư trú, yêu cầu, khiếu nại, bảo trì, hóa đơn, thanh toán và thay đổi quyền theo đặc tả. Không generic hard-delete hồ sơ cần truy vết.

Entity bảo vệ invariant bằng domain behavior, không public setter tùy tiện. IsActive/DeactivatedAt/DeactivatedBy chỉ dùng cho master/reference entity thực sự hỗ trợ deactivation; không áp generic soft-delete cho Invoice, Payment, ServiceRequest, Complaint hoặc MaintenanceTask.

## 19. Xử lý yêu cầu xóa dữ liệu cá nhân

Không mặc định xóa cứng hoặc ẩn danh hóa toàn bộ hồ sơ khi nhận yêu cầu. Phải thực hiện quy trình:

1. Tiếp nhận và xác minh danh tính, thẩm quyền, phạm vi yêu cầu.
2. Xác định loại dữ liệu, mục đích, căn cứ xử lý và nghĩa vụ lưu giữ.
3. Phân loại: được phép xóa → xóa an toàn; có thể ẩn danh → ẩn danh thực sự; phải tiếp tục lưu → hạn chế xử lý/truy cập theo quy định.
4. Kiểm tra ảnh hưởng tới chứng từ, audit, quan hệ dữ liệu, backup và hệ thống liên quan.
5. Thực hiện bởi người có quyền, ghi nhận quyết định/kết quả và phản hồi theo quy định.

Pseudonymization không đồng nghĩa anonymization. Nếu còn khóa hoặc thông tin cho phép liên kết ngược tới cá nhân thì dữ liệu vẫn có thể là dữ liệu cá nhân. Không thay thế PII trên chứng từ cần giữ nguyên tính toàn vẹn hoặc giá trị pháp lý khi chưa có căn cứ và phương án được phê duyệt. Stable ID, số liệu và lịch sử chỉ được giữ ở mức cần thiết, hợp pháp; không cam kết dữ liệu đã ẩn danh sẽ không thể liên kết ngược nếu chưa kiểm chứng.

## 20. Reporting và vận hành
Reporting dùng query contract, read model hoặc read-only database view/query được tài liệu hóa; không ghi source tables. Scope phải được enforce trước khi trả dữ liệu (bao gồm scope tòa nhà theo mục 7), không chỉ là UI filter.

Có structured logging, CorrelationId, module/entity ID; không log token, secret, full sensitive payload, raw AI prompt hoặc dữ liệu cá nhân nhạy cảm (CCCD, số thẻ ngân hàng — xem mục 5). **SHOULD:** technical infrastructure nên cung cấp `/health/live`, `/health/ready` và metrics cơ bản khi task/phạm vi hạ tầng liên quan; không bắt mọi feature task phải tự triển khai các concern này. Nếu health checks được triển khai, AI outage chỉ làm degraded, không làm core backend unready. Secret không nằm trong source code, đọc qua configuration/secret provider theo môi trường.

## 21. Kiểm thử và Definition of Done
Bắt buộc unit/integration tests cho domain rules, state transitions, authorization, scope (bao gồm cách ly theo tòa nhà — mục 7), finance, concurrency, AI fallback, PostgreSQL và cross-module contracts.

Kiểm thử cư dân không xem dữ liệu người khác, Staff không xử lý ngoài assignment, Manager/Accountant không vượt building scope (mục 7/10) kể cả khi đoán đúng ID, payment không giảm nợ khi pending/rejected, xác nhận trùng không double-apply, AI lỗi không chặn workflow, outbox không mất event sau crash, consumer không duplicate, attachment không bị truy cập bằng guessed URL, seed chạy lại không tạo trùng. Với các trường đã áp dụng NFC normalization theo mục 4 (có search/uniqueness): input đến ở NFD vẫn phải khớp/không tạo bản ghi trùng với dữ liệu đã lưu ở NFC; không yêu cầu test này cho các trường không thuộc phạm vi normalization ở mục 4.

Riêng Login/Logout: outward response cho credential không hợp lệ, tài khoản không tồn tại và tài khoản bị lockout không được tiết lộ trạng thái tồn tại của tài khoản; test phải kiểm tra message/status/body không tạo enumeration oracle và implementation không có timing difference rõ ràng có thể khai thác, **không yêu cầu HTTP response constant-time tuyệt đối**. Refresh token cũ bị revoke sau khi rotate hoặc logout không dùng lại được; vượt ngưỡng lockout thì các lần thử tiếp theo bị chặn đúng cấu hình; token hết hạn bị từ chối đúng hạn.

Feature chỉ hoàn thành khi đúng FE/UC, đúng boundary, endpoint hoạt động thật, validation/auth đầy đủ, migration/history đúng, Swagger cập nhật, tests/build pass, không fake data hoặc placeholder nghiệp vụ bắt buộc.

## 22. Truy vết 56 Use Case
Mọi business endpoint/handler phải truy vết về FE và UC đã phê duyệt:
Feature Specification → Consolidated UC → Handler/API → Domain Rules → Acceptance Tests.

Không tự tạo capability ngoài catalogue, không biến mỗi nút CRUD thành UC mới. Class giữ tên tiếng Anh; **UC ID phải có trong documentation/test trait** (bắt buộc, không phải khuyến nghị). Nếu task không map được UC, báo rõ technical requirement hay scope extension.

Trước khi code: đọc đặc tả, xác định FE/UC, actor, permission, scope (bao gồm building scope), dependency; kiểm tra code hiện có; thực hiện thay đổi nhỏ nhất hoàn chỉnh; viết test; chạy build/test; báo cáo file thay đổi, quyết định, giả định và khoảng trống.

## 23. Quy tắc áp dụng và kiểm soát thay đổi

Phân loại mức độ áp dụng (MUST/SHOULD/OPTIONAL/DECISION REQUIRED) xem mục 1.1; áp dụng thống nhất cho toàn tài liệu.

Mọi thay đổi business scope phải đối chiếu catalogue 56 UC đã phê duyệt. Quy tắc kỹ thuật không thay thế đặc tả chi tiết từng FE. Nếu bản rút gọn thiếu thông tin, Agent phải đọc đặc tả nguồn và ADR liên quan, không tự suy diễn.

## 24. Quy tắc cuối cùng
Modular Monolith là bắt buộc. Business ownership và module boundary không được phá. Không frontend-controlled authorization, không AI-controlled final decision, không fake backend, không tự phát minh nghiệp vụ, không vi phạm quy định bảo vệ dữ liệu cá nhân (mục 5). Mọi thay đổi phải có traceability, kiểm thử và build thành công. **Mọi nội dung website hướng tới người dùng phải bằng tiếng Việt; định danh kỹ thuật tiếp tục dùng tiếng Anh.**

## 25. Các quyết định kỹ thuật đã chốt và còn mở

### 25.1. Đã chốt

- Frontend là **Blazor Web App + Global Interactive WebAssembly**, với `InteractiveWebAssemblyRenderMode(prerender: false)` cho application shell/routes. `PropFlow.Web` là server host/composition root; routed business UI và authentication state chạy trong `PropFlow.Web.Client`. Baseline token transport: access token trong memory + Bearer header; refresh token trong Secure/HttpOnly cookie; refresh/logout/revoke tuân theo CSRF protection phù hợp khi dựa trên cookie. Chỉ thay đổi render mode hoặc mô hình token khi có ADR mới được phê duyệt.
- Baseline là **multi-building trong cùng hệ thống**, không mặc định SaaS multi-tenancy. Không tự thêm Tenant/Organization, subscription hoặc tenant isolation model.
- Backend là **Modular Monolith**, một ASP.NET Core Web API deployable, một PostgreSQL database, ưu tiên schema-per-module.
- FE–BE tích hợp qua HTTP API contract `/api/v1/...`; URL/origin phụ thuộc môi trường phải đi qua configuration, không hard-code trong business feature code.
- Money baseline: **VND only**; monetary amount dùng .NET `decimal` + PostgreSQL `numeric(18,0)`; billing quantity cần phần lẻ dùng `numeric(18,4)`; round từng invoice line bằng `MidpointRounding.AwayFromZero` về 0 decimal trước khi cộng tổng; multi-currency/exchange-rate nằm ngoài baseline.
- Timezone baseline: instant dùng PostgreSQL `timestamptz`/UTC; calendar-only value dùng `date`; mỗi Building có IANA `time_zone_id`, mặc định `Asia/Ho_Chi_Minh`; due/overdue/daily business calculation theo timezone của Building.

### 25.2. DECISION REQUIRED

Agent phải yêu cầu đặc tả/ADR/quyết định trước khi hard-code hoặc coi các nội dung sau là final:

- permission catalogue chi tiết và Building scope/User–Building access assignment chưa được đặc tả đầy đủ cho từng use case;
- retention schedule;
- AI provider, dữ liệu được phép gửi, quota/budget, timeout và fallback;
- access token TTL và refresh token TTL chính xác;
- ngưỡng lockout/rate-limit và backoff;
- phạm vi revoke khi Logout;
- retention của security/audit log liên quan login.

Catalogue 56 UC phải tiếp tục được đối chiếu với đặc tả FE nguồn trước khi thêm capability mới.

## 26. Ghi chú về phạm vi tài liệu

Đây là rule kiến trúc và coding implementation cho AI Agent, không phải tài liệu DevOps/deployment và không thay thế đặc tả FE/UC chi tiết, ADR, permission matrix hoặc tài liệu pháp lý đã được phê duyệt. Agent phải đọc các tài liệu đó khi triển khai từng tính năng. Không coi ví dụ trong rule là yêu cầu nghiệp vụ mới.
