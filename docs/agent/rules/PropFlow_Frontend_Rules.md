# PropFlow — Quy tắc Frontend dành cho AI Agent

**Frontend Architecture:** Feature-Based Architecture  
**Framework:** Blazor Web App + Interactive WebAssembly  
**Backend:** ASP.NET Core Web API  
**API Contract:** REST `/api/v1/...` + Swagger/OpenAPI  
**Ngôn ngữ giao diện:** Tiếng Việt

> **Phạm vi tài liệu:** Đây là coding/architecture rule dành cho AI Agent khi triển khai frontend PropFlow. Không phải Git workflow, commit convention, CI/CD runbook hay tài liệu vận hành server. Chỉ giữ các deployment concern khi chúng ảnh hưởng trực tiếp đến code, configuration, authentication, authorization hoặc khả năng Frontend–Backend gọi được nhau sau publish.

---

## 0. Thứ tự ưu tiên

Khi triển khai frontend, AI Agent phải ưu tiên theo thứ tự:

1. Đặc tả Feature/Use Case đã được phê duyệt.
2. API contract / Swagger/OpenAPI hiện hành của Backend.
3. Permission matrix / authorization contract đã được phê duyệt.
4. Tài liệu này.
5. Convention hiện có của repository nếu không mâu thuẫn.
6. Đề xuất riêng của Agent.

Không tự phát minh Feature, Use Case, actor, role, permission, business status, API endpoint, request/response field hoặc business rule.

Nếu thiếu contract cần thiết để hoàn thành chức năng, Agent phải báo rõ dependency còn thiếu thay vì tự mock hoặc tự thiết kế API mới.

---

### 0.1. Phân loại mức độ áp dụng quy tắc

Toàn bộ tài liệu (cả FE và BE) dùng thống nhất 4 mức sau; mọi chữ "bắt buộc"/"BẮT BUỘC" từ đây trở đi được hiểu theo MUST trừ khi ghi rõ khác:

- **MUST / BẮT BUỘC** — phải thực hiện khi thuộc phạm vi, không tự bỏ qua.
- **SHOULD / KHUYẾN NGHỊ** — mặc định nên làm; nếu không áp dụng phải ghi rõ lý do.
- **OPTIONAL / TÙY CHỌN** — chỉ triển khai khi có giá trị thực sự và không mở rộng scope trái phép.
- **DECISION REQUIRED / CẦN QUYẾT ĐỊNH** — Agent không được tự chốt; phải có đặc tả/ADR/quyết định được phê duyệt trước khi merge, và không hard-code như thể đã final trong lúc chờ quyết định.

Không biến khuyến nghị thành scope mới; không biến một mục DECISION REQUIRED thành mặc định ngầm chỉ vì thiếu thời gian chờ phê duyệt.

### 0.2. Định tuyến đọc rule theo loại task (tránh đọc toàn bộ tài liệu mỗi lần)

Tài liệu này dài; không phải task nào cũng cần đọc hết. Agent nên:

1. Luôn đọc phần **core** (ngắn, áp dụng cho mọi task): mục 0–0.2 (ưu tiên/phân loại/định tuyến), 1 (kiến trúc tổng quan), 7 (ranh giới FE–BE), 13.1 (`ApiResult` convention), 19 (ngôn ngữ), 26 (không fake implementation), 31 (feature completion), 33 (phạm vi tài liệu), 34 (quy tắc cuối cùng).
2. Chỉ đọc thêm đúng mục liên quan vùng code đang đụng tới, theo bảng dưới.

| Task đụng tới... | Đọc thêm mục |
|---|---|
| Tạo feature mới, đổi cấu trúc thư mục | 1, 3, 4, 5, 6 |
| Render mode, global interactivity, prerender/DI | 2, 2.1, 2.2, 2.3 |
| Login/session UI, token, refresh flow | 8, 8.1, 8.2 |
| Ẩn/hiện UI theo quyền, bảo vệ route | 9 |
| Current-building context UI | 10 |
| Global/shared state | 11 |
| Model dùng chung | 12 |
| Xử lý lỗi API trong Feature Service | 13 |
| Loading/empty/error state | 14 |
| Form, validation | 15 |
| Gọi HTTP, cancellation | 16 |
| Danh sách lớn, phân trang/filter/search | 17 |
| Route, navigation | 18 |
| Ngày giờ, tiền tệ, format hiển thị | 20 |
| Invoice/Payment/công nợ UI | 20, 20.1 |
| JS interop | 21 |
| Lưu dữ liệu phía client (storage) | 22 |
| Accessibility, responsive | 23, 24 |
| Viết test | 25 |
| Đặt tên class/method/component | 29 |
| Tính năng liên quan AI | 30 |

---

## 1. Kiến trúc Frontend bắt buộc

PropFlow Frontend sử dụng **Feature-Based Architecture**.

Code nghiệp vụ phải được tổ chức theo feature thay vì gom toàn bộ Page, Component, Service hoặc Model của hệ thống vào các thư mục kỹ thuật chung.

```text
PropFlow.Web/
│
├── Components/
│   └── App.razor
├── Program.cs
└── wwwroot/

PropFlow.Web.Client/
│
├── Routes.razor
│
├── Features/
│   ├── Authentication/      (FE-01)
│   ├── Resident/            (FE-02)
│   ├── Apartment/           (FE-03)
│   ├── Facility/            (FE-04 — Building, Facility & Equipment)
│   ├── ServiceRequest/      (FE-05)
│   ├── Complaint/           (FE-06)
│   ├── Maintenance/         (FE-07)
│   ├── Finance/             (FE-08 Fee & Invoice + FE-09 Payment & Debt)
│   ├── AiClassification/    (FE-10)
│   ├── AiRecommendation/    (FE-11)
│   ├── AiChatbot/           (FE-12)
│   ├── Communication/       (FE-13 — Notification & Announcement)
│   ├── Reporting/           (FE-14 — Dashboards, Reports & Operational Analytics)
│   └── Administration/      (FE-15)
│
├── Shared/
│   ├── Components/
│   ├── Layouts/
│   │   ├── MainLayout.razor
│   │   └── AuthLayout.razor
│   ├── Forms/
│   ├── Tables/
│   ├── Dialogs/
│   └── Navigation/
│
├── Services/
│   ├── Api/
│   ├── Authentication/
│   ├── Notification/
│   └── Storage/
│
├── State/
├── Models/
├── Helpers/
└── Program.cs
```

`PropFlow.Web` là server host/composition root: cấu hình Razor Components, Interactive WebAssembly endpoint, middleware và environment-backed configuration. `PropFlow.Web.Client` là client application chạy WebAssembly và chứa toàn bộ routed business UI, layout/navigation dùng cho application shell, feature services, client state và browser-facing infrastructure.

Với **Global Interactive WebAssembly**, các routed page/layout/navigation của application phải ưu tiên nằm trong `.Client`; không để cùng một business page tồn tại song song ở cả server project và `.Client`.

Cây `Features/` ở trên tương ứng đúng **15 FE trong catalogue đã phê duyệt** (FE-01–FE-15, 56 UC); `Finance` gộp FE-08 (Fee & Invoice) và FE-09 (Payment & Debt) chỉ vì mục đích UI cho Accountant — backend vẫn giữ Billing và Payments là hai module/API riêng, Feature Service bên trong `Finance` phải gọi đúng endpoint của từng module, không coi đó là một ownership gộp. Không tự thêm feature ngoài 15 FE này (vd. Amenity Booking, Vehicle/Parking, Vendor Management) khi chưa có scope update được phê duyệt; không tự tách thêm hoặc gộp thêm feature khác ngoài cách gộp Finance đã nêu nếu chưa có quyết định tương đương.

Không tự chuyển kiến trúc sang MVC, Razor Pages, Blazor Server-only, MAUI, React/Vue/Angular, Clean Architecture nhiều project cho frontend hoặc microfrontend nếu chưa có quyết định kiến trúc mới.

---

## 2. Render Mode — Global Interactive WebAssembly

Baseline frontend của PropFlow là:

```text
Blazor Web App
    +
Global Interactive WebAssembly
    +
prerender: false cho application shell/routes
```

Toàn bộ routed business application chạy theo **Interactive WebAssembly**. Agent không được tự đổi riêng một feature/page sang `InteractiveServer` hoặc `InteractiveAuto` để xử lý lỗi state, DI, authentication hoặc API.

Render mode được áp dụng ở mức cao nhất phù hợp của application shell, không lặp `@rendermode` ở từng feature page nếu không có yêu cầu kiến trúc riêng được phê duyệt.

Trong `PropFlow.Web/Components/App.razor`, baseline:

```razor
<HeadOutlet @rendermode="new InteractiveWebAssemblyRenderMode(prerender: false)" />
<Routes @rendermode="new InteractiveWebAssemblyRenderMode(prerender: false)" />
```

Không gắn `@rendermode` trực tiếp lên root `App` component.

`Routes.razor` thuộc `.Client` khi application dùng global client-side interactivity. Business pages, layouts và navigation interactive phải được tổ chức trong `.Client` theo mục 1.

### 2.1. Prerender và Authentication — ĐÃ CHỐT

PropFlow **tắt prerender cho application shell/routes** bằng `InteractiveWebAssemblyRenderMode(prerender: false)`.

Lý do coding/architecture:

- access token baseline chỉ tồn tại trong memory của WASM client;
- tránh cùng component phải chạy cả server-prerender và client runtime;
- tránh double initialization/API call do prerender + hydrate;
- tránh dependency client-only bị resolve trên server;
- giảm độ phức tạp khi đồng bộ authentication state giữa server host và WASM client.

Vì prerender đã tắt ở top-level `Routes`/`HeadOutlet`, Agent **không được tự bật lại prerender ở feature page/component con**. Nếu sau này cần prerender/SEO cho một vùng public cụ thể, phải có quyết định kiến trúc riêng và không được làm thay đổi auth baseline của application authenticated.

Không dùng refresh-token cookie trên server host để tự suy ra user đã đăng nhập. Authentication state của application được xác lập theo auth flow ở mục 8.

Nếu sau này PropFlow triển khai server-side authentication-state serialization/persistence cho prerender, thay đổi đó phải có ADR/rule riêng; không tự thêm chỉ để “tối ưu Blazor”.

### 2.2. DI giữa Server Host và `.Client`

Baseline hiện tại tắt prerender cho application shell/routes, vì vậy **business feature service/client-only service không cần được duplicate registration ở server chỉ để phục vụ prerender**.

Tuy nhiên Agent phải phân biệt rõ hai composition root:

```text
PropFlow.Web/Program.cs
    → server-host services / Razor Components / middleware / endpoint configuration

PropFlow.Web.Client/Program.cs
    → WASM client services / API client / auth state / browser-facing services
```

Quy tắc:

- Service chỉ chạy trong browser phải đăng ký ở `.Client`.
- Server-host service chỉ phục vụ hosting/composition thì đăng ký ở `PropFlow.Web`.
- Không duplicate business logic giữa server và client chỉ để “cho DI chạy”.
- Nếu một component sau này được phép prerender và inject service client-only, phải chọn một trong các hướng được phê duyệt: cung cấp abstraction/implementation phù hợp ở cả hai runtime, hoặc tắt prerender cho component/nhánh đó.
- Không inject `IWebAssemblyHostEnvironment` hoặc browser-only dependency vào code có khả năng chạy server-side trừ khi lifecycle/architecture đã bảo đảm nó chỉ chạy trong WASM.

### 2.3. Framework setup baseline

`PropFlow.Web` phải bật Razor Components và Interactive WebAssembly support ở composition root:

```csharp
builder.Services
    .AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();
```

Endpoint mapping baseline:

```csharp
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode();
```

Nếu server-side route/component discovery thực sự cần quét assembly của `.Client` theo cấu trúc repository hiện tại, dùng `AddAdditionalAssemblies(...)` tại composition root theo convention của solution; không rải assembly discovery vào feature code.

Ví dụ khi cần:

```csharp
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(PropFlow.Web.Client._Imports).Assembly);
```

Nếu `Routes` và toàn bộ routable business components đã nằm trong `.Client` theo global Interactive WebAssembly và không cần external route discovery bổ sung, không thêm `AdditionalAssemblies` chỉ vì copy template cũ.

Không tự thêm Interactive Server support (`AddInteractiveServerComponents`, `AddInteractiveServerRenderMode`) khi baseline của PropFlow là Interactive WebAssembly.

---

## 3. Tổ chức một Feature

Một feature có thể có cấu trúc:

```text
Features/
└── Resident/
    ├── Pages/
    │   ├── ResidentList.razor
    │   ├── ResidentList.razor.cs
    │   └── ResidentList.razor.css
    ├── Components/
    ├── Services/
    ├── Models/
    ├── State/
    └── Validation/
```

Không bắt buộc tạo tất cả thư mục nếu feature không cần.

Quy tắc:

- Page chỉ orchestration UI.
- Component chỉ phục vụ feature thì để trong feature.
- Service chỉ phục vụ feature thì để trong `Features/<Feature>/Services`.
- Model chỉ phục vụ feature thì để trong `Features/<Feature>/Models`.
- State chỉ phục vụ feature thì để trong feature.
- Chỉ đưa code lên `Shared`, root `Services`, root `Models` khi thực sự dùng chung.
- Không tạo folder/file rỗng chỉ để “đúng kiến trúc”.

---

## 4. Quy tắc `.razor`

Mỗi `.razor` tập trung vào rendering, binding, event, điều phối state, gọi feature service và authorization UI.

Không đặt business logic phức tạp trong `.razor`. Không gọi `HttpClient` trực tiếp rải rác trong Page nếu đã có feature service/API abstraction. Không viết SQL hoặc truy cập database từ frontend. Không gọi trực tiếp provider AI từ browser. Không lưu secret trong frontend.

Khi component lớn, ưu tiên:

```text
ResidentList.razor
ResidentList.razor.cs
ResidentList.razor.css
```

Code-behind `.razor.cs` dùng khi giúp component dễ đọc; không bắt buộc mọi component phải có code-behind.

### 4.1 CSS Isolation

CSS chỉ phục vụ một Razor component nên dùng `ComponentName.razor.css`.

```text
ResidentList.razor
ResidentList.razor.css
```

Không cần import thủ công `.razor.css`. CSS dùng toàn hệ thống đặt ở vùng style chung trong `wwwroot`. Không tạo `.razor.css` nếu component không có style riêng. Không dùng `!important` hàng loạt để sửa lỗi kiến trúc CSS.

---

## 5. Shared

`Shared` chỉ chứa UI primitive hoặc component có tính tái sử dụng thật sự.

Ví dụ hợp lệ:

```text
Shared/
├── Components/
│   ├── LoadingSpinner.razor
│   ├── EmptyState.razor
│   └── StatusBadge.razor
├── Tables/
│   ├── Pagination.razor
│   └── TableToolbar.razor
├── Dialogs/
│   └── ConfirmDialog.razor
└── Navigation/
    ├── Sidebar.razor
    └── Breadcrumb.razor
```

Không đưa vào `Shared` chỉ vì “có thể sau này dùng”. Component business-specific phải ở feature owner tương ứng.

---

## 6. Services

Root `Services` chỉ chứa infrastructure/client concern dùng chung:

```text
Services/
├── Api/
├── Authentication/
├── Notification/
└── Storage/
```

Ví dụ: API client abstraction, auth state provider, bearer token handler, refresh-session handling, notification/toast, browser storage abstraction.

Feature-specific API logic đặt trong feature:

```text
Features/Resident/Services/ResidentService.cs
Features/Finance/Services/InvoiceService.cs
```

Không tạo một `ApiService`, `DataService` hoặc God Service chứa toàn bộ endpoint.

---

## 7. Frontend–Backend Integration — BẮT BUỘC

Frontend chỉ giao tiếp backend qua API contract.

```text
Page / Component
       ↓
Feature Service
       ↓
API Client
       ↓
/api/v1/...
       ↓
PropFlow.Api
```

Frontend không phụ thuộc vào Backend Domain Entity, EF Core Entity, DbContext, Repository, internal Application Handler hoặc internal module implementation.

DTO frontend phải phản ánh API contract, không phản ánh database schema nội bộ.

### 7.1 Không hard-code localhost

Không hard-code `https://localhost:xxxx` hoặc `http://localhost:xxxx` trong Page, Component, Feature Service, API client, helper hoặc business logic.

API base address phải lấy từ configuration hoặc cơ chế host configuration đã thống nhất.

Code phải hỗ trợ cả:

```text
Same-Origin
https://app.example.com/api/v1/...
```

và:

```text
Separate-Origin
https://app.example.com
https://api.example.com
```

mà không cần sửa source code của từng feature.

Không nối URL bằng string tùy tiện trong UI component.

### 7.2 API configuration

Một nơi duy nhất chịu trách nhiệm cấu hình API base address.

Ví dụ concept:

```csharp
public sealed class ApiOptions
{
    public string BaseUrl { get; set; } = string.Empty;
}
```

Feature service chỉ sử dụng configured client. Không đọc environment-specific URL ở nhiều nơi. Không để fallback production về localhost. Nếu configuration bắt buộc bị thiếu, phải fail rõ ràng thay vì âm thầm dùng development URL.

### 7.3 Không chứa secret trong WebAssembly

Blazor WebAssembly chạy trong browser, vì vậy mọi nội dung trong client bundle phải được xem là có thể đọc được bởi người dùng.

Không đặt trong frontend: database connection string, JWT signing key, refresh token secret, API private key, SMTP credential, AI provider secret, storage private secret hoặc backend service credential.

`wwwroot/appsettings*.json` phía WASM không phải nơi lưu secret.

### 7.4 API version

Baseline API route là `/api/v1/...`.

Không tự đổi endpoint version. Khi backend contract thay đổi, Agent phải cập nhật DTO, feature service, validation/error handling, affected pages/components và tests liên quan.

Không “fix nhanh” bằng dynamic JSON hoặc bỏ type safety nếu không cần thiết.

---

## 8. Authentication

Baseline đã chốt:

```text
Access Token
    → lưu trong memory của WASM
    → gửi qua Authorization: Bearer

Refresh Token
    → Secure + HttpOnly cookie
    → frontend không đọc giá trị token
```

Không mặc định lưu access token lâu dài vào `localStorage` hoặc `sessionStorage`. Không truyền token qua query string. Không tự decode JWT rồi coi dữ liệu client-side là nguồn authorization đáng tin cậy.

Sau reload, frontend phải dùng auth/session flow được backend cung cấp để khôi phục phiên thay vì phụ thuộc persisted bearer token.

### 8.1 401 và 403

Frontend phải phân biệt:

- `401 Unauthorized`: chưa xác thực, token thiếu/invalid/hết hạn hoặc session không khôi phục được.
- `403 Forbidden`: đã xác thực nhưng không có quyền.

Không xử lý mọi `403` bằng redirect Login. Không hiển thị raw backend exception.

### 8.2 Global 401 Refresh + Retry Flow — BẮT BUỘC

Logic refresh access token **không được viết riêng trong từng Feature Service/Page**. Phải có một auth HTTP handler/coordinator dùng chung cho toàn bộ business API client.

Flow chuẩn:

```text
Feature Service gửi request
        ↓
Attach Access Token trong memory
        ↓
Backend trả 401
        ↓
Global Auth Handler
        ↓
Refresh session đúng 1 lần
        ↓
├── Refresh thành công → cập nhật access token → retry request gốc đúng 1 lần
└── Refresh thất bại   → clear auth state → chuyển về flow đăng nhập/session expired
```

Quy tắc bắt buộc:

- Chỉ refresh khi **business API trả `401`**; `403` không refresh và không retry.
- Request refresh/login/session bootstrap không được tự đi qua chính refresh handler theo cách gây recursion.
- Mỗi request chỉ được auto-refresh/retry tối đa **1 lần**; không tạo retry loop vô hạn.
- Khi nhiều request đồng thời cùng nhận `401`, chỉ cho phép **một refresh request đang chạy tại một thời điểm** (single-flight/async lock). Các request còn lại chờ kết quả refresh chung. Điều này đặc biệt quan trọng vì refresh token có rotation.
- Sau refresh thành công, request gốc phải được replay với access token mới và giữ nguyên method, URI, body, headers cần thiết và `Idempotency-Key` nếu contract sử dụng.
- Chỉ auto-retry request có thể replay an toàn. Với request/body không thể replay, handler phải trả failure chuẩn thay vì tự gửi lại mù quáng.
- Không refresh khi gặp network error, timeout hoặc `5xx`; các lỗi này đi qua error convention ở mục 13.
- Khi refresh thất bại, việc clear authentication state/điều hướng login phải được xử lý tập trung; Feature Service không tự implement logout/redirect riêng.

Feature Service chỉ nhận kết quả cuối cùng sau auth handler. Không feature nào được tự gọi refresh endpoint để xử lý `401`.

---

## 9. Authorization trong Razor Components

PropFlow ưu tiên **Permission/Policy-Based Authorization** thay vì hard-code Role trực tiếp trong từng page.

### 9.1 Bảo vệ Route/Page

Trang cần authenticated user:

```razor
@attribute [Authorize]
```

Trang yêu cầu permission/policy:

```razor
@attribute [Authorize(Policy = "resident.view")]
```

Ví dụ:

```razor
@page "/residents"
@attribute [Authorize(Policy = "resident.view")]
```

Không chỉ ẩn menu rồi cho rằng route đã được bảo vệ.

### 9.2 Phân quyền từng vùng UI bằng `AuthorizeView`

```razor
<AuthorizeView Policy="resident.create">
    <Authorized>
        <button class="btn btn-primary" @onclick="CreateResident">
            Thêm cư dân
        </button>
    </Authorized>
</AuthorizeView>
```

Áp dụng tương tự cho Create, Update, Delete, Approve, Assign, Export, Manage, Close, Confirm Payment theo permission catalogue thực tế.

### 9.3 Permission constants

Không rải magic string permission tùy ý trong code.

```csharp
public static class Permissions
{
    public static class Residents
    {
        public const string View = "resident.view";
        public const string Create = "resident.create";
        public const string Update = "resident.update";
        public const string Delete = "resident.delete";
    }
}
```

Tên permission phải khớp backend/permission catalogue. Frontend không tự phát minh permission.

### 9.4 Policy

Policy có thể ánh xạ tới permission claim hoặc custom authorization requirement theo thiết kế đã chốt.

```csharp
options.AddPolicy(
    Permissions.Residents.View,
    policy => policy.RequireClaim(
        "permission",
        Permissions.Residents.View));
```

Nếu permission catalogue lớn, có thể dùng custom policy provider/handler phù hợp thay vì đăng ký hàng trăm policy thủ công. Agent không tự tạo authorization framework mới nếu framework ASP.NET Core/Blazor đã đáp ứng được.

### 9.5 `AuthorizeView` không phải security boundary

`AuthorizeView` chỉ điều khiển UI. Ẩn nút Delete/Approve/Assign/Payment Confirm không thay thế authorization phía backend.

Backend vẫn phải kiểm tra:

```text
Authentication
    +
Permission / Policy
    +
Resource Ownership / Assignment
    +
Resource Ownership / Assignment
    +
Business Rules
```

Frontend không được tin Role, Permission, ResidentId hoặc ownership chỉ vì giá trị đó đang tồn tại trong browser state.

---

## 10. Bối cảnh chung cư hiện hành

Mỗi deployment PropFlow vận hành cho một chung cư hiện hành. Frontend hiển thị hồ sơ chung cư đó khi feature cần, nhưng không tạo Building selector, không giữ `SelectedBuildingId`, không gửi building filter và không cung cấp UX chuyển hoặc so sánh giữa nhiều chung cư. Nếu vận hành chung cư khác, người dùng truy cập deployment tương ứng.

Thông tin Building hiện tại là hồ sơ hệ thống singleton do PropertyAssets cung cấp. Backend vẫn enforce role/permission, resource ownership và assignment nghiệp vụ; frontend phải xử lý đúng `403` hoặc resource không accessible.

---

## 11. State Management

Root `State/` chỉ chứa global/client state thật sự cần chia sẻ nhiều feature.

Ví dụ:

```text
CurrentUserState
CurrentBuildingProfileState
NotificationState
```

Feature-local state phải ở feature. Không biến toàn bộ application data thành global state. Không giữ bản sao lớn dữ liệu server ở global state nếu không cần.

Không tự thêm Redux/Fluxor hoặc state-management package nếu scoped service/state container đơn giản đã đủ.

---

## 12. Models

Root `Models/` chỉ chứa model dùng chung thực sự, ví dụ `PagedResult<T>`, `ApiError`, `SelectOption`, `CurrentUser`.

Model riêng của Resident đặt trong `Features/Resident/Models/`. Model riêng của Finance đặt trong `Features/Finance/Models/`.

Không tạo root `Models` thành dumping ground chứa toàn bộ DTO hệ thống.

---

## 13. Error Contract

Frontend phải hỗ trợ error contract thống nhất từ backend. Ưu tiên `ProblemDetails` hoặc contract tương đương có:

```text
code
message
traceId
validation errors
```

Không parse exception text để xác định business error.

Không hiển thị stack trace, SQL error, internal exception, token, secret hoặc raw provider error cho người dùng.

Thông báo người dùng phải bằng tiếng Việt tự nhiên. Nếu có `traceId`, có thể hiển thị/mang theo để hỗ trợ tra cứu lỗi.

### 13.1 Convention trả lỗi từ Feature Service — BẮT BUỘC

Feature Service phải dùng **`ApiResult<T>` / `ApiResult` thống nhất** thay vì mỗi feature tự chọn giữa `throw`, `null`, tuple hoặc custom response khác nhau.

Baseline:

```csharp
public sealed record ApiResult<T>(
    bool IsSuccess,
    T? Data,
    int? StatusCode,
    string? Code,
    string? Message,
    string? TraceId,
    IReadOnlyDictionary<string, string[]>? ValidationErrors);
```

Có thể mở rộng type/error-kind tập trung khi repository thực sự cần, nhưng **không được tạo một Result type khác cho từng feature**.

Feature Service method mặc định:

```csharp
Task<ApiResult<ResidentDetailResponse>> GetResidentAsync(...);
Task<ApiResult> UpdateResidentAsync(...);
```

Quy tắc xử lý:

- HTTP response hợp lệ nhưng thất bại theo contract (`400`, `401`, `403`, `404`, `409`, `5xx`...) → map thành `ApiResult` failure, không throw để Page phải bắt exception.
- Validation errors từ backend → map vào `ValidationErrors`.
- `code`, `message`, `traceId`, HTTP status phải được giữ lại khi backend cung cấp để debug/truy vết.
- Network failure, timeout, serialization/contract failure phải được xử lý **tập trung tại API client layer**, log technical detail bằng `ILogger` và trả failure chuẩn phù hợp cho Feature Service/UI.
- `OperationCanceledException` do user/navigation cancellation không được hiển thị như system error thông thường.
- **Không `catch (Exception)` trong Feature Service rồi đổi mọi lỗi thành `ApiResult.Fail("Có lỗi xảy ra")`.** Programming bug/unexpected exception không thuộc API failure contract phải được để global error boundary/logging bắt để không che mất bug.
- Page/Component không tự parse `HttpResponseMessage`, `ProblemDetails` hoặc JSON error nếu Feature Service/API client đã có trách nhiệm này.
- Page chỉ quyết định UX dựa trên `ApiResult`: success, validation error, forbidden, not found, conflict hoặc lỗi hệ thống.

Mục tiêu là tất cả feature có cùng một đường đi:

```text
HTTP/API error
     ↓
Central API Client mapping/logging
     ↓
ApiResult<T>
     ↓
Feature Service
     ↓
Page/Component UX
```

---

## 14. Loading / Empty / Error State

Mỗi page tải dữ liệu phải xử lý tối thiểu:

```text
Loading
Success
Empty
Error
Unauthorized / Forbidden khi phù hợp
```

Không để page trắng khi API lỗi. Không hiển thị fake data để lấp chỗ trống. Không tự sinh sample record trong production UI. Không hiển thị KPI giả khi backend chưa trả dữ liệu.

---

## 15. Form và Validation

Frontend validation phục vụ UX. Backend validation là nguồn quyết định cuối cùng.

Form phải:

- validate required/format cơ bản;
- hiển thị server validation error;
- disable hoặc chống double-submit khi request đang xử lý;
- giữ trạng thái hợp lý khi request thất bại;
- không tự thay business validation của backend.

Không chỉ validate ở frontend rồi giả định request an toàn.

---

## 16. HTTP Request

Ưu tiên async API. Request có thể hủy theo lifecycle phù hợp nên truyền `CancellationToken`.

Không tạo request lặp không cần thiết khi component rerender. Không gọi API trong property getter. Không trigger network call trực tiếp từ render logic. Không tạo polling ngắn liên tục nếu feature không yêu cầu realtime.

---

## 17. Pagination / Filtering / Sorting

Danh sách dữ liệu lớn phải ưu tiên server-side pagination, filtering, sorting và search theo API contract.

Không tải toàn bộ resident/invoice/payment/... về browser chỉ để phân trang bằng client. Không gọi API mỗi keypress không debounce khi search realtime.

---

## 18. Navigation

Route phải rõ ràng và ổn định, ví dụ:

```text
/residents
/residents/{id}
/maintenance
/service-requests
/finance/invoices
```

Không encode secret/token vào route. Không dùng query string cho bearer token.

Navigation theo permission chỉ phục vụ UX; route vẫn phải được authorization bảo vệ.

---

## 19. Ngôn ngữ giao diện

Toàn bộ nội dung hướng tới người dùng phải bằng tiếng Việt: menu, button, title, label, validation, notification, empty state, error, dialog, dashboard và report.

Technical identifier giữ tiếng Anh: class, method, namespace, route, DTO property, permission, policy và error code.

Không hard-code câu tiếng Anh hiển thị cho người dùng nếu sản phẩm đã chốt tiếng Việt.

---

## 20. Date, Time và Currency

Frontend chỉ format dữ liệu để hiển thị. Không tự thay đổi semantic của timestamp backend.

Ngày hiển thị mặc định phù hợp người dùng Việt Nam, ví dụ `dd/MM/yyyy`. Business time theo building timezone nếu API cung cấp.

Tiền phải dùng giá trị decimal từ backend contract. Không dùng JS floating-point để tự thực hiện nghiệp vụ tài chính quan trọng. Không tự tính công nợ, settlement hoặc payment state phía client rồi gửi kết quả như nguồn đáng tin cậy.

### 20.1 Cấm Optimistic UI cho trạng thái tài chính — BẮT BUỘC

Với `Invoice`, `Payment`, công nợ, settlement, paid amount, outstanding amount, refund/confirmation hoặc mọi trạng thái tài chính có tính authoritative, frontend **không được optimistic update thành trạng thái thành công trước khi backend xác nhận**.

Ví dụ khi người dùng bấm **Xác nhận thanh toán**:

```text
Sai:
Pending → user click → UI lập tức hiện Confirmed → gọi API sau

Đúng:
Pending → user click → IsSubmitting/Processing → gọi API
                                      ↓
                         Backend success response
                                      ↓
                           UI cập nhật Confirmed
```

Quy tắc:

- Trong lúc request chạy, chỉ được hiển thị trạng thái UI tạm như `Đang xử lý...`, spinner hoặc disable action; không đổi business state thật trên UI.
- Chỉ cập nhật `PaymentStatus`, `InvoiceStatus`, công nợ, số tiền đã thanh toán, số dư hoặc KPI tài chính từ response/state được backend xác nhận.
- Nếu request thất bại, giữ/khôi phục state server gần nhất và hiển thị lỗi; không để UI ở trạng thái thành công giả.
- Không cộng/trừ tiền client-side để giả lập kết quả cuối trước response.
- Action tài chính phải chống double-submit trong lúc request đang chạy.
- Nếu API hỗ trợ `Idempotency-Key`, retry do auth refresh hoặc người dùng retry phải giữ đúng idempotency semantics của backend; frontend không tự sinh request duplicate làm thay đổi tài chính hai lần.
- Sau mutation thành công, ưu tiên dùng response authoritative từ backend; chỉ refetch khi contract yêu cầu hoặc response không đủ dữ liệu cần hiển thị.

Ngoài Finance, optimistic UI cho business state có hậu quả quan trọng chỉ được dùng khi Feature Specification/API contract cho phép rõ ràng. Mặc định không tự áp dụng.

---

## 21. JavaScript Interop

Chỉ dùng JS interop khi Blazor/.NET không đáp ứng tốt nhu cầu cụ thể.

JS interop phải được encapsulate, không rải `IJSRuntime.InvokeAsync` tùy tiện trong mọi component.

Không dùng JavaScript để bypass authorization, lưu secret, sửa access token trái auth design, gọi private backend credential hoặc thay backend business validation.

Nếu JS module có lifecycle, phải dispose đúng.

---

## 22. Browser Storage

Storage service chỉ dùng cho dữ liệu phù hợp phía client như UI preference, non-sensitive filter, theme hoặc non-sensitive draft khi được cho phép.

Không lưu password, refresh token, signing key, secret, sensitive PII không cần thiết hoặc access token dài hạn theo baseline hiện tại.

---

## 23. Accessibility và UX cơ bản

Component tương tác phải có label rõ ràng, keyboard usability hợp lý, semantic button/link, disabled/loading state khi submit, confirmation cho destructive action, error message đọc được và focus behavior hợp lý với dialog/form khi cần.

Không tạo button không hoạt động. Không tạo menu dẫn tới page placeholder nếu feature chưa được triển khai và đặc tả không yêu cầu expose.

---

## 24. Responsive UI

PropFlow là web application. Page phải hoạt động hợp lý trên desktop, tablet và viewport nhỏ theo design system đã chốt.

Không hard-code width khiến layout vỡ. Table lớn cần strategy responsive phù hợp thay vì ép toàn bộ viewport ngang không kiểm soát.

---

## 25. Testability

Code phải cho phép test phần logic quan trọng.

Ưu tiên tách API service, mapping, state transition UI, authorization-dependent behavior, formatter và validator khỏi markup nếu logic đủ phức tạp.

Các flow quan trọng nên có test phù hợp cho auth state, unauthorized/forbidden, feature service request/response, validation, mapping, permission-dependent UI, building selection/state và error handling.

Không test implementation detail vô nghĩa chỉ để tăng coverage.

---

## 26. Không fake implementation

Agent không được fake API, resident, invoice, payment, dashboard KPI, AI response, notification, authorization hoặc success sau khi API thất bại.

Mock chỉ được dùng trong test hoặc môi trường development/test đã tách biệt rõ ràng. Production code phải dùng API thật.

---

## 27. Không over-engineering

Không tự thêm generic repository ở frontend, MediatR cho frontend, CQRS framework phía client, event bus phức tạp, Redux/Fluxor khi chưa cần, reflection mapping framework không cần thiết, base class nhiều tầng, abstract factory, service locator, microfrontend hoặc custom DI container chỉ vì pattern phổ biến.

Ưu tiên code đơn giản, typed, dễ trace từ Feature → Page → Service → API.

---

## 28. Dependency Rule

Feature được phép phụ thuộc theo hướng:

```text
Feature
  ↓
Shared UI / Shared Models
  ↓
Common Client Infrastructure
```

Feature không nên phụ thuộc implementation nội bộ của feature khác.

Nếu Resident cần dữ liệu Apartment, dùng API contract/model phù hợp thay vì reference sâu vào internal component/service của `Apartment`.

Không tạo circular dependency giữa features.

---

## 29. Naming

C#: `PascalCase`.

Private field: `_camelCase`.

Async method: `GetResidentsAsync()`.

Component:

```text
ResidentTable.razor
InvoiceStatusBadge.razor
```

Page:

```text
ResidentList.razor
ResidentDetail.razor
```

Service:

```text
IResidentService
ResidentService
```

Request/Response:

```text
CreateResidentRequest
ResidentDetailResponse
```

Không dùng tên chung chung như `Helper1`, `CommonService`, `Manager`, `Utils2`, `DataModel`.

---

## 30. AI Features

Frontend AI chỉ hiển thị/gửi request theo API contract.

Không gọi trực tiếp AI provider bằng secret từ WASM. Không tự cho AI quyết định hành động nghiệp vụ cuối cùng.

UI phải phân biệt rõ AI suggestion, user/manager confirmation và final persisted business state.

Nếu AI unavailable, core workflow không được bị khóa trừ khi Feature Specification nói rõ khác. Không fake AI suggestion.

---

## 31. Feature completion

Một frontend feature chỉ được coi là hoàn thành khi:

- đúng Feature/Use Case;
- route hoạt động;
- authorization đúng;
- permission đúng contract;
- API thật được gọi;
- request/response đúng OpenAPI;
- loading state có;
- empty state có;
- error state có;
- validation có;
- Feature Service dùng `ApiResult<T>/ApiResult` theo convention chung;
- không tự xử lý refresh token trong từng feature;
- trạng thái tài chính không optimistic update trước backend success;
- không fake data;
- không button chết;
- không localhost hard-code;
- không secret phía WASM;
- không tự đổi render mode hoặc bật lại prerender trái baseline;
- responsive hợp lý;
- build thành công;
- test liên quan pass.

---

## 32. Quy trình AI Agent trước khi code

Trước khi triển khai một task, Agent phải:

1. Xác định Feature/Use Case liên quan.
2. Xác định actor.
3. Xác định permission/policy.
4. Xác định building/resource scope nếu có.
5. Đọc API contract/OpenAPI tương ứng.
6. Kiểm tra component/service/model hiện có để tái sử dụng đúng chỗ.
7. Xác định feature owner của code.
8. Thực hiện thay đổi nhỏ nhất nhưng hoàn chỉnh.
9. Không phá API compatibility.
10. Chạy build/test phù hợp.
11. Báo rõ dependency backend còn thiếu nếu API chưa tồn tại.

Không tự tạo mock endpoint để “cho UI chạy”. Không sửa backend contract từ frontend task nếu chưa được yêu cầu.

---

## 33. Những việc KHÔNG thuộc rule này

AI Agent frontend không cần quyết định hoặc mô tả:

- Git branch strategy;
- commit convention;
- commit message;
- pull request process;
- release workflow;
- CI/CD pipeline;
- server provisioning;
- Docker host topology;
- domain thật;
- DNS;
- TLS certificate issuance;
- backup schedule;
- production server monitoring.

Ngoại lệ: nếu một vấn đề deployment ảnh hưởng trực tiếp đến cách code FE–BE tích hợp, Agent phải code theo nguyên tắc configuration-safe, không hard-code development environment.

**Phải quan tâm trong code:** API Base URL, authentication token transport, cookie behavior, CORS-compatible request behavior, API version, error contract, Global Interactive WebAssembly, prerender policy và configuration.

**Không cần quan tâm trong rule coding:** server deploy bằng lệnh nào, domain mua ở đâu, GitHub Actions viết ra sao, commit format hoặc Nginx config cụ thể.

---

## 34. Quy tắc cuối cùng

PropFlow Frontend bắt buộc giữ:

```text
Feature-Based Architecture
+
Blazor Web App
+
Global Interactive WebAssembly
+
prerender: false cho application shell/routes
+
Typed API Integration
+
Permission/Policy Authorization
+
Configuration-Safe FE–BE Integration
```

Mục tiêu coding:

```text
Code ở môi trường development
        ↓
Publish
        ↓
Thay configuration
        ↓
Frontend gọi Backend bình thường
```

mà không phải sửa source code trong từng feature.

Không hard-code localhost. Không frontend-controlled security. Không fake backend/data. Không tự phát minh nghiệp vụ. Không over-engineering.

Mọi UI hướng tới người dùng phải bằng tiếng Việt; technical identifier tiếp tục dùng tiếng Anh.
