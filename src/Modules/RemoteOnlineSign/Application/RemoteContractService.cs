using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using PdfSharpCore.Pdf.IO;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.RemoteOnlineSign.Domain;
using PropFlow.Modules.RemoteOnlineSign.Infrastructure.Persistence;
using PropFlow.Modules.RemoteOnlineSign.Infrastructure.Storage;
using PropFlow.Modules.Residents.Contracts;

namespace PropFlow.Modules.RemoteOnlineSign.Application;

public sealed record CreateRemoteContract(DateTimeOffset? Deadline, string ContractName);
public sealed record SelectRemoteSigningMode(RemoteSigningMode SigningMode, bool ConfirmFieldReset, uint Version);
public sealed record UpdateRemoteContractMetadata(string ContractName, DateTimeOffset? Deadline, uint Version);
public sealed record RenewRemoteDeadline(DateTimeOffset Deadline, uint Version);
public sealed record AssignResident(Guid ResidentUserId);
public sealed record SignatureFieldInput(SignatureRole Role, int Page, decimal X, decimal Y, decimal Width, decimal Height);
public sealed record SendRemoteContract();
public sealed record ReassignRemoteResident(Guid ResidentUserId, uint Version);
public sealed record RejectRemoteContract(string? Reason);
public sealed record BeginSignature(SignatureMethod Method, byte[]? Image, string? Text, string? Font);
public sealed record VerifySignature(Guid SessionId, string Code);
public sealed record RemoteContractSummary(Guid Id, string ContractCode, string ContractName, Guid CreatorId, Guid? ResidentId, string? ResidentName, string? ResidentCode, string Status, string SigningMode, DateTimeOffset? Deadline, DateTimeOffset MetadataUpdatedAt, int Revision, uint Version, bool ResidentSigned);
public sealed record RemoteDocumentMetadata(Guid Id, string Sha256, long Size, int PageCount, string PageMetadata, bool IsFinal);
public sealed record SigningChallenge(Guid SessionId, Guid AuthChallengeId, DateTimeOffset ExpiresAt, DateTimeOffset ResendAfter);
public sealed record RemoteAuditEvent(Guid Id, Guid? ActorId, string Type, DateTimeOffset OccurredAt, string Metadata);
public sealed record RemoteSignatureField(Guid Id, string Role, int Page, decimal X, decimal Y, decimal Width, decimal Height);
public sealed record RemoteResidentCandidate(Guid UserId, Guid ResidentId, string FullName, string ResidentCode, IReadOnlyList<string> Apartments);

public sealed class RemoteContractService(
    RemoteOnlineSignDbContext db,
    IRemotePdfStorage storage,
    IPrivilegedActionOtp otp,
    IPdfSignatureStamper stamper,
    ILogger<RemoteContractService> logger,
    IAccountAccess accountAccess,
    IInternalAccountDirectory accounts,
    IResidentAccountScope residents, IRemoteContractCodeGenerator codes,
    IRemoteContractStatusNotifier? statusNotifier = null)
{
    private const string OtpPurpose = "REMOTE_ONLINE_SIGN";
    private const int MaxPdfBytes = 20 * 1024 * 1024;
    private const int MaxSignatureBytes = 5 * 1024 * 1024;

    public async Task<RemoteContractSummary> CreateAsync(Guid adminId, CreateRemoteContract input, CancellationToken ct)
    {
        if (input.Deadline is { } deadline && deadline <= DateTimeOffset.UtcNow) throw new RemoteContractException(400, "deadline_must_be_future", "Hạn ký phải ở tương lai.");
        var contractName = NormalizeContractName(input.ContractName);
        await using var transaction = await db.Database.BeginTransactionAsync(ct); var createdAt = DateTimeOffset.UtcNow; var item = new RemoteContract { CreatorId = adminId, SigningMode = RemoteSigningMode.UNSELECTED, Deadline = input.Deadline?.ToUniversalTime(), CreatedAt = createdAt, MetadataUpdatedAt = createdAt, ContractName = contractName, ContractCode = await codes.NextAsync(createdAt, ct) };
        db.Contracts.Add(item); await AuditAsync(item.Id, adminId, "CONTRACT_CREATED"); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return ToSummary(item);
    }
    public async Task<RemoteContractSummary> SelectSigningModeAsync(Guid actor, Guid id, SelectRemoteSigningMode input, CancellationToken ct)
    {
        if (input.SigningMode is not (RemoteSigningMode.INVESTOR_AND_RESIDENT or RemoteSigningMode.PRE_SIGNED_INVESTOR)) throw new RemoteContractException(400, "signing_mode_invalid", "Chế độ ký không hợp lệ.");
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var contract = await LockAccessibleAsync(actor, true, id, ct);
        if (contract.Version != input.Version) throw new RemoteContractException(409, "signing_mode_concurrency_conflict", "Hợp đồng đã thay đổi. Vui lòng tải lại.");
        if (contract.Status != RemoteContractStatus.DRAFT) throw new RemoteContractException(409, "signing_mode_not_editable", "Chỉ có thể thiết lập chế độ ký khi hợp đồng còn là bản nháp.");
        if (await db.SigningSessions.AnyAsync(x => x.ContractId == id && (x.FinalizedAt != null || (x.ExpiresAt != null && x.ExpiresAt > DateTimeOffset.UtcNow)), ct)) throw new RemoteContractException(409, "signing_mode_not_editable", "Hợp đồng đang có hoặc đã có phiên ký nên không thể đổi chế độ.");
        if (contract.SigningMode == input.SigningMode) { await transaction.CommitAsync(ct); return ToSummary(contract); }
        var fields = await db.SignatureFields.Where(x => x.ContractId == id).ToListAsync(ct);
        if (fields.Count != 0 && !input.ConfirmFieldReset) throw new RemoteContractException(409, "signing_mode_field_reset_required", "Đổi chế độ ký sẽ xóa các vị trí chữ ký hiện có. Cần xác nhận thay đổi.");
        if (fields.Count != 0) db.SignatureFields.RemoveRange(fields);
        var previous = contract.SigningMode;
        contract.SigningMode = input.SigningMode;
        await AuditAsync(id, actor, "SIGNING_MODE_SELECTED", JsonSerializer.Serialize(new { before = previous.ToString(), after = input.SigningMode.ToString(), fieldsReset = fields.Count != 0 }));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ToSummary(contract);
    }
    public async Task<RemoteContractSummary> UpdateMetadataAsync(Guid actor, Guid id, UpdateRemoteContractMetadata input, CancellationToken ct)
    {
        var contractName = NormalizeContractName(input.ContractName);
        if (input.Deadline is { } deadline && deadline <= DateTimeOffset.UtcNow) throw new RemoteContractException(400, "deadline_must_be_future", "Hạn ký phải ở tương lai.");
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var item = await LockAccessibleAsync(actor, true, id, ct);
        if (item.Version != input.Version) throw new RemoteContractException(409, "metadata_concurrency_conflict", "Dữ liệu hợp đồng đã thay đổi. Vui lòng tải lại trước khi chỉnh sửa.");
        if (item.Status is RemoteContractStatus.COMPLETED or RemoteContractStatus.REJECTED or RemoteContractStatus.CANCELLED or RemoteContractStatus.EXPIRED || item.Deadline is { } currentDeadline && currentDeadline <= DateTimeOffset.UtcNow) throw new RemoteContractException(409, "metadata_edit_not_allowed", "Hợp đồng không còn đủ điều kiện chỉnh sửa thông tin.");
        if (await db.SigningSessions.AnyAsync(x => x.ContractId == id && x.Role == SignatureRole.RESIDENT && x.FinalizedAt != null, ct)) throw new RemoteContractException(409, "metadata_edit_not_allowed", "Cư dân đã ký hợp đồng nên không thể chỉnh sửa thông tin.");
        if (await db.SigningSessions.AnyAsync(x => x.ContractId == id && x.FinalizedAt == null && x.ExpiresAt != null && x.ExpiresAt > DateTimeOffset.UtcNow, ct)) throw new RemoteContractException(409, "signing_in_progress", "Hợp đồng đang có phiên ký hoạt động. Vui lòng thử lại sau khi phiên ký kết thúc hoặc hết hạn.");
        var normalizedDeadline = input.Deadline?.ToUniversalTime();
        if (item.ContractName == contractName && item.Deadline == normalizedDeadline) { await transaction.CommitAsync(ct); return ToSummary(item); }
        var before = new { item.ContractName, item.Deadline };
        var wasSentToResident = item.Status == RemoteContractStatus.PENDING_RESIDENT_SIGNATURE;
        item.ContractName = contractName; item.Deadline = normalizedDeadline; item.MetadataUpdatedAt = DateTimeOffset.UtcNow;
        await AuditAsync(id, actor, "CONTRACT_METADATA_UPDATED", JsonSerializer.Serialize(new { before, after = new { item.ContractName, item.Deadline }, visibleToResident = wasSentToResident }));
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); } catch (DbUpdateConcurrencyException) { throw new RemoteContractException(409, "metadata_concurrency_conflict", "Dữ liệu hợp đồng đã thay đổi. Vui lòng tải lại trước khi chỉnh sửa."); }
        return ToSummary(item);
    }
    public async Task<IReadOnlyList<RemoteResidentCandidate>> SearchEligibleResidentsAsync(string? search, CancellationToken ct)
    {
        if (search?.Length > 100) throw new RemoteContractException(400, "resident_search_invalid", "Từ khóa tìm kiếm tối đa 100 ký tự.");
        const int sourcePageSize = 100;
        var candidates = new List<RemoteResidentCandidate>();
        var seen = new HashSet<Guid>();
        for (var sourcePage = 1; candidates.Count < 20; sourcePage++)
        {
            var page = await residents.SearchAsync(search?.Trim(), sourcePage, sourcePageSize, ct);
            if (page.Items.Count == 0 || page.PageIndex < sourcePage) break;
            var items = page.Items.Where(x => x.IsActive && x.UserId is not null).ToArray();
            var userIds = items.Select(x => x.UserId!.Value).Distinct().ToArray();
            var accountRows = await accounts.GetAsync(userIds, ct);
            var accountById = accountRows.ToDictionary(x => x.Id);
            var residentIds = (await accountAccess.GetResidentUserIdsAsync(userIds, ct)).ToHashSet();
            foreach (var candidate in items.Where(x => accountById.TryGetValue(x.UserId!.Value, out var account)
                    && string.Equals(account.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase)
                    && residentIds.Contains(x.UserId.Value))
                .Select(x => new RemoteResidentCandidate(x.UserId!.Value, x.ResidentId, x.FullName, x.ResidentCode, x.Apartments))
                .Where(x => seen.Add(x.UserId)).Take(20 - candidates.Count))
                candidates.Add(candidate);
            if ((long)page.PageIndex * sourcePageSize >= page.TotalCount) break;
        }
        return candidates;
    }
    public async Task<IReadOnlyList<RemoteContractSummary>> ListAsync(Guid actor, bool admin, CancellationToken ct)
    {
        if (!admin) await EnsureEligibleResidentAsync(actor, false, ct);
        await ExpireOverdueAsync(actor, admin, ct);
        var contracts = await db.Contracts.AsNoTracking().Where(x => admin ? x.CreatorId == actor : x.ResidentId == actor && (x.Status == RemoteContractStatus.PENDING_RESIDENT_SIGNATURE || x.Status == RemoteContractStatus.COMPLETED || x.Status == RemoteContractStatus.REJECTED || x.Status == RemoteContractStatus.EXPIRED)).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        return await ToSummariesAsync(contracts, ct);
    }
    public async Task<RemoteContractSummary> GetAsync(Guid actor, bool admin, Guid id, CancellationToken ct) => (await ToSummariesAsync([await AccessibleAsync(actor, admin, id, ct)], ct)).Single();
    public async Task<RemoteDocumentMetadata> GetDocumentMetadataAsync(Guid actor, bool admin, Guid id, CancellationToken ct)
    {
        var contract = await AccessibleAsync(actor, admin, id, ct);
        var document = await db.Documents.AsNoTracking().SingleOrDefaultAsync(x => x.ContractId == id && x.IsFinal == (contract.Status == RemoteContractStatus.COMPLETED), ct)
            ?? await db.Documents.AsNoTracking().SingleOrDefaultAsync(x => x.ContractId == id && !x.IsFinal, ct)
            ?? throw new RemoteContractException(404, "document_not_found", "Không tìm thấy PDF.");
        return ToDocument(document);
    }
    public async Task<IReadOnlyList<RemoteSignatureField>> GetFieldsAsync(Guid actor, bool admin, Guid id, CancellationToken ct)
    {
        await AccessibleAsync(actor, admin, id, ct);
        return await db.SignatureFields.AsNoTracking().Where(x => x.ContractId == id).OrderBy(x => x.Role)
            .Select(x => new RemoteSignatureField(x.Id, x.Role.ToString(), x.Page, x.X, x.Y, x.Width, x.Height)).ToListAsync(ct);
    }
    public async Task<RemoteContractSummary> AssignAsync(Guid actor, Guid id, AssignResident input, CancellationToken ct)
    {
        if (input.ResidentUserId == Guid.Empty) throw new RemoteContractException(400, "resident_required", "Cần chọn cư dân.");
        var item = await db.Contracts.SingleOrDefaultAsync(x => x.Id == id && x.CreatorId == actor, ct) ?? throw new RemoteContractException(404, "contract_not_found", "Không tìm thấy hợp đồng."); EnsureModeSelected(item);
        if (item.SigningMode == RemoteSigningMode.INVESTOR_AND_RESIDENT && item.Status != RemoteContractStatus.INVESTOR_SIGNED_DRAFT) throw new RemoteContractException(409, "investor_signature_required", "Chủ đầu tư cần ký hợp đồng trước khi chọn cư dân.");
        if (item.SigningMode == RemoteSigningMode.PRE_SIGNED_INVESTOR && item.Status != RemoteContractStatus.DRAFT) throw new RemoteContractException(409, "contract_not_editable", "Hợp đồng không còn đủ điều kiện gán cư dân.");
        await EnsureEligibleResidentAsync(input.ResidentUserId, true, ct);
        if (item.SigningMode == RemoteSigningMode.PRE_SIGNED_INVESTOR)
        {
            if (!await db.Documents.AnyAsync(x => x.ContractId == id && !x.IsFinal, ct)) throw new RemoteContractException(400, "original_document_required", "Cần tải PDF gốc trước khi chọn cư dân.");
            var roles = await db.SignatureFields.Where(x => x.ContractId == id).Select(x => x.Role).ToListAsync(ct);
            if (roles.Count != 1 || roles[0] != SignatureRole.RESIDENT) throw new RemoteContractException(400, "resident_field_required", "PDF ký sẵn cần đúng một vùng ký cư dân trước khi chọn cư dân.");
        }
        item.ResidentId = input.ResidentUserId;
        await AuditAsync(id, actor, "RESIDENT_ASSIGNED");
        await db.SaveChangesAsync(ct);
        return (await ToSummariesAsync([item], ct)).Single();
    }

    public async Task SetFieldsAsync(Guid actor, Guid id, IReadOnlyList<SignatureFieldInput> fields, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var item = await LockAccessibleAsync(actor, true, id, ct);
        if (item.Status != RemoteContractStatus.DRAFT) throw new RemoteContractException(409, "contract_not_draft", "Hợp đồng không còn ở bản nháp.");
        EnsureModeSelected(item);
        if (await db.SigningSessions.AnyAsync(x => x.ContractId == id && (x.FinalizedAt != null || x.ExpiresAt > DateTimeOffset.UtcNow), ct))
            throw new RemoteContractException(409, "signing_in_progress", "Hợp đồng đang có hoặc đã có phiên ký. Không thể thay đổi vị trí ký.");
        if (fields.Count == 0 || fields.Any(x => x.Page < 1 || x.X < 0 || x.Y < 0 || x.Width <= 0 || x.Height <= 0) || fields.Select(x => x.Role).Distinct().Count() != fields.Count) throw new RemoteContractException(400, "invalid_signature_fields", "Vị trí ký không hợp lệ.");
        if (item.SigningMode == RemoteSigningMode.INVESTOR_AND_RESIDENT && fields.Select(x => x.Role).Distinct().Count() != 2) throw new RemoteContractException(400, "both_fields_required", "Cần vị trí ký của hai bên.");
        if (item.SigningMode == RemoteSigningMode.PRE_SIGNED_INVESTOR && (fields.Count != 1 || fields[0].Role != SignatureRole.RESIDENT)) throw new RemoteContractException(400, "investor_field_not_allowed", "PDF ký sẵn chỉ có vị trí cư dân.");
        await ValidateCoordinatesAsync(id, fields, ct); db.SignatureFields.RemoveRange(db.SignatureFields.Where(x => x.ContractId == id)); db.SignatureFields.AddRange(fields.Select(x => new ContractSignatureField { ContractId = id, Role = x.Role, Page = x.Page, X = x.X, Y = x.Y, Width = x.Width, Height = x.Height })); await AuditAsync(id, actor, "SIGNATURE_FIELDS_UPDATED"); await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
    public async Task SendAsync(Guid actor, Guid id, SendRemoteContract input, CancellationToken ct)
    {
        var c = await db.Contracts.SingleOrDefaultAsync(x => x.Id == id && x.CreatorId == actor, ct) ?? throw new RemoteContractException(404, "contract_not_found", "Không tìm thấy hợp đồng."); EnsureModeSelected(c); if (c.ResidentId is null) throw new RemoteContractException(400, "resident_required", "Cần chọn cư dân trước khi gửi."); if (c.SigningMode == RemoteSigningMode.INVESTOR_AND_RESIDENT && c.Status != RemoteContractStatus.INVESTOR_SIGNED_DRAFT) throw new RemoteContractException(409, "investor_signature_required", "Chủ đầu tư cần ký hợp đồng trước khi gửi."); if (c.SigningMode == RemoteSigningMode.PRE_SIGNED_INVESTOR && c.Status != RemoteContractStatus.DRAFT) throw new RemoteContractException(409, "contract_not_draft", "Hợp đồng không còn ở bản nháp."); if (!await db.Documents.AnyAsync(x => x.ContractId == id && !x.IsFinal, ct)) throw new RemoteContractException(400, "original_document_required", "Cần tải PDF gốc trước khi gửi.");
        var roles = await db.SignatureFields.Where(x => x.ContractId == id).Select(x => x.Role).ToListAsync(ct);
        if (c.SigningMode == RemoteSigningMode.INVESTOR_AND_RESIDENT && !(roles.Contains(SignatureRole.INVESTOR) && roles.Contains(SignatureRole.RESIDENT))) throw new RemoteContractException(400, "both_fields_required", "Cần vị trí ký của hai bên.");
        if (c.SigningMode == RemoteSigningMode.PRE_SIGNED_INVESTOR && (roles.Count != 1 || roles[0] != SignatureRole.RESIDENT)) throw new RemoteContractException(400, "resident_field_required", "PDF ký sẵn cần đúng một vùng ký cư dân.");
        c.Status = RemoteContractStatus.PENDING_RESIDENT_SIGNATURE; await AuditAsync(id, actor, c.SigningMode == RemoteSigningMode.INVESTOR_AND_RESIDENT ? "CONTRACT_SENT_FOR_RESIDENT_SIGNATURE" : "CONTRACT_SENT_FOR_INVESTOR_SIGNATURE"); await db.SaveChangesAsync(ct);
    }
    public async Task DeleteDraftAsync(Guid actor, Guid id, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var contract = await LockAccessibleAsync(actor, true, id, ct);
        if (contract.Status != RemoteContractStatus.DRAFT) throw new RemoteContractException(409, "contract_not_draft", "Chỉ hợp đồng bản nháp mới có thể xóa.");
        if (await db.SigningSessions.AnyAsync(x => x.ContractId == id && x.FinalizedAt != null, ct)
            || await db.Documents.AnyAsync(x => x.ContractId == id && x.IsFinal, ct))
            throw new RemoteContractException(409, "contract_not_draft", "Hợp đồng đã có chữ ký hoặc tài liệu hoàn tất, không thể xóa.");
        var documentKeys = await db.Documents.Where(x => x.ContractId == id).Select(x => x.StorageKey).ToListAsync(ct);
        await AuditAsync(id, actor, "CONTRACT_DRAFT_DELETED");
        db.SigningSessions.RemoveRange(await db.SigningSessions.Where(x => x.ContractId == id).ToListAsync(ct));
        db.SignatureFields.RemoveRange(await db.SignatureFields.Where(x => x.ContractId == id).ToListAsync(ct));
        db.Documents.RemoveRange(await db.Documents.Where(x => x.ContractId == id).ToListAsync(ct));
        db.Contracts.Remove(contract);
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new RemoteContractException(409, "metadata_concurrency_conflict", "Dữ liệu hợp đồng đã thay đổi. Vui lòng tải lại."); }
        foreach (var key in documentKeys) await CleanupAsync(key, ct);
    }
    public async Task<SigningChallenge> BeginSigningAsync(Guid actor, bool isAdmin, Guid id, BeginSignature input, CancellationToken ct)
    {
        ValidateSignature(input); var snapshot = await AccessibleAsync(actor, isAdmin, id, ct); EnsureModeSelected(snapshot); EnsureSigningDeadline(snapshot); var role = RequiredRole(snapshot, isAdmin); var source = await LoadVerifiedSourceAsync(snapshot, ct); Guid sessionId; string binding; uint contractVersion; DateTimeOffset requestMarker;
        db.ChangeTracker.Clear();
        await using (var transaction = await db.Database.BeginTransactionAsync(ct)) { var c = await LockAccessibleAsync(actor, isAdmin, id, ct); EnsureSigningDeadline(c); role = RequiredRole(c, isAdmin); var field = await db.SignatureFields.SingleOrDefaultAsync(x => x.ContractId == id && x.Role == role, ct) ?? throw new RemoteContractException(409, "signature_field_missing", "Thiếu vị trí ký."); var session = await db.SigningSessions.SingleOrDefaultAsync(x => x.ContractId == id && x.Role == role, ct); if (session?.FinalizedAt is not null) throw new RemoteContractException(409, "already_signed", "Bên này đã ký hợp đồng."); session ??= new ContractSigningSession { ContractId = id, Role = role }; session.SignerId = actor; session.Method = input.Method; session.SignatureImage = input.Image; session.TypedSignatureText = input.Text?.Trim(); session.TypedSignatureFont = input.Font?.Trim(); session.SignatureSha256 = SignatureHash(input); session.BindingHash = Binding(c, (source.Key, source.Sha256), field, session); session.AuthChallengeId = null; session.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2); session.OtpVerifiedAt = null; requestMarker = UtcNowAtMicrosecondPrecision(); session.FinalizationStartedAt = requestMarker; if (db.Entry(session).State == EntityState.Detached) db.SigningSessions.Add(session); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); sessionId = session.Id; binding = session.BindingHash; contractVersion = c.Version; }
        var challenge = await otp.RequestAsync(actor, OtpPurpose, binding, new PrivilegedActionEmailContext(snapshot.ContractName), ct); var expiresAt = challenge.ExpiresAt.ToUniversalTime(); var resendAfter = challenge.ResendAfter.ToUniversalTime();
        db.ChangeTracker.Clear();
        await using (var transaction = await db.Database.BeginTransactionAsync(ct)) { var c = await LockAccessibleAsync(actor, isAdmin, id, ct); EnsureSigningDeadline(c); role = RequiredRole(c, isAdmin); if (c.Version != contractVersion) throw new RemoteContractException(409, "signing_context_changed", "Thông tin hợp đồng đã thay đổi. Vui lòng yêu cầu mã OTP mới."); var session = await db.SigningSessions.SingleOrDefaultAsync(x => x.Id == sessionId && x.ContractId == id && x.SignerId == actor, ct) ?? throw new RemoteContractException(409, "signing_session_replaced", "Phiên ký đã thay đổi. Vui lòng yêu cầu OTP mới."); if (session.BindingHash != binding || session.FinalizedAt is not null || session.FinalizationStartedAt != requestMarker) throw new RemoteContractException(409, "signing_session_replaced", "Phiên ký đã thay đổi. Vui lòng yêu cầu mã OTP mới."); session.AuthChallengeId = challenge.ChallengeId; session.ExpiresAt = expiresAt; session.FinalizationStartedAt = null; await AuditAsync(id, actor, "SIGNATURE_OTP_REQUESTED"); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        return new(sessionId, challenge.ChallengeId, expiresAt, resendAfter);
    }
    public async Task<RemoteContractSummary> VerifyAndFinalizeAsync(Guid actor, bool isAdmin, Guid id, VerifySignature input, CancellationToken ct)
    {
        var c = await AccessibleAsync(actor, isAdmin, id, ct); EnsureSigningDeadline(c); var s = await db.SigningSessions.SingleOrDefaultAsync(x => x.Id == input.SessionId && x.ContractId == id && x.SignerId == actor, ct) ?? throw new RemoteContractException(404, "signing_session_not_found", "Không tìm thấy phiên ký."); if (s.FinalizedAt is not null) return ToSummary(c); var role = RequiredRole(c, isAdmin); if (s.Role != role) throw new RemoteContractException(404, "signing_session_not_found", "Không tìm thấy phiên ký."); if (s.AuthChallengeId is null) throw new RemoteContractException(409, "otp_not_requested", "Chưa yêu cầu mã OTP."); if (s.ExpiresAt is null || s.ExpiresAt <= DateTimeOffset.UtcNow) throw new RemoteContractException(409, "signing_session_expired", "Phiên ký đã hết hạn. Vui lòng yêu cầu mã OTP mới.");
        var field = await db.SignatureFields.SingleAsync(x => x.ContractId == id && x.Role == role, ct);
        var verifiedSource = await LoadVerifiedSourceAsync(c, ct);
        EnsureBinding(c, verifiedSource, field, s);
        if (s.OtpVerifiedAt is null) { await otp.VerifyAsync(actor, s.AuthChallengeId.Value, OtpPurpose, s.BindingHash, input.Code, ct); s.OtpVerifiedAt = DateTimeOffset.UtcNow; await AuditAsync(id, actor, "SIGNATURE_OTP_VERIFIED"); await db.SaveChangesAsync(ct); }
        return await FinalizeAsync(c, s, role, ct);
    }
    public async Task RejectAsync(Guid actor, Guid id, string? reason, CancellationToken ct)
    {
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed)) throw new RemoteContractException(400, "reject_reason_required", "Cần nhập lý do từ chối ký.");
        if (trimmed.Length > 500) throw new RemoteContractException(400, "reject_reason_too_long", "Lý do từ chối tối đa 500 ký tự.");
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var contract = await LockAccessibleAsync(actor, false, id, ct);
        if (contract.Status != RemoteContractStatus.PENDING_RESIDENT_SIGNATURE) throw new RemoteContractException(409, "contract_not_pending", "Hợp đồng chưa sẵn sàng để từ chối.");
        EnsureSigningDeadline(contract);
        contract.Status = RemoteContractStatus.REJECTED;
        contract.MetadataUpdatedAt = DateTimeOffset.UtcNow;
        await InvalidateUnfinishedSessionsAsync(id, ct);
        await AuditAsync(id, actor, "CONTRACT_REJECTED", JsonSerializer.Serialize(new { reason = trimmed, residentUserId = actor, previousStatus = RemoteContractStatus.PENDING_RESIDENT_SIGNATURE.ToString(), deadline = contract.Deadline }));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await NotifyStatusChangedAsync(contract, RemoteContractStatus.PENDING_RESIDENT_SIGNATURE, ct);
    }
    public async Task<RemoteContractSummary> ReassignResidentAsync(Guid actor, Guid id, ReassignRemoteResident input, CancellationToken ct)
    {
        if (input.ResidentUserId == Guid.Empty) throw new RemoteContractException(400, "resident_required", "Cần chọn cư dân.");
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var contract = await LockAccessibleAsync(actor, true, id, ct);
        if (contract.Version != input.Version) throw new RemoteContractException(409, "metadata_concurrency_conflict", "Dữ liệu hợp đồng đã thay đổi. Vui lòng tải lại trước khi chỉnh sửa.");
        if (contract.Status != RemoteContractStatus.REJECTED) throw new RemoteContractException(409, "contract_not_rejected", "Chỉ hợp đồng bị từ chối mới có thể chuyển cư dân ký.");
        if (contract.Deadline is { } deadline && deadline <= DateTimeOffset.UtcNow) throw new RemoteContractException(409, "deadline_expired", "Hạn ký đã hết. Vui lòng cập nhật hạn ký trước khi gửi lại.");
        EnsureModeSelected(contract);
        await EnsureEligibleResidentAsync(input.ResidentUserId, true, ct);
        if (!await db.Documents.AnyAsync(x => x.ContractId == id && !x.IsFinal, ct)) throw new RemoteContractException(400, "original_document_required", "Cần tải PDF gốc trước khi gửi lại.");
        var roles = await db.SignatureFields.Where(x => x.ContractId == id).Select(x => x.Role).ToListAsync(ct);
        if (contract.SigningMode == RemoteSigningMode.INVESTOR_AND_RESIDENT)
        {
            var investorSigned = !string.IsNullOrWhiteSpace(contract.WorkingSha256)
                || await db.SigningSessions.AnyAsync(x => x.ContractId == id && x.Role == SignatureRole.INVESTOR && x.FinalizedAt != null, ct);
            if (!investorSigned) throw new RemoteContractException(409, "investor_signature_required", "Chữ ký chủ đầu tư không còn hợp lệ. Không thể chuyển cư dân ký.");
            if (!(roles.Contains(SignatureRole.INVESTOR) && roles.Contains(SignatureRole.RESIDENT))) throw new RemoteContractException(400, "both_fields_required", "Cần vị trí ký của hai bên.");
        }
        else if (roles.Count != 1 || roles[0] != SignatureRole.RESIDENT) throw new RemoteContractException(400, "resident_field_required", "PDF ký sẵn cần đúng một vùng ký cư dân.");
        var previousResident = contract.ResidentId;
        var sameResident = previousResident == input.ResidentUserId;
        contract.ResidentId = input.ResidentUserId;
        contract.Status = RemoteContractStatus.PENDING_RESIDENT_SIGNATURE;
        contract.Revision++;
        contract.MetadataUpdatedAt = DateTimeOffset.UtcNow;
        await InvalidateUnfinishedSessionsAsync(id, ct);
        await AuditAsync(id, actor, sameResident ? "CONTRACT_RESENT_TO_RESIDENT" : "RESIDENT_REASSIGNED", JsonSerializer.Serialize(new { previousResidentId = previousResident, newResidentId = input.ResidentUserId, sameResident, previousStatus = RemoteContractStatus.REJECTED.ToString(), deadline = contract.Deadline }));
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new RemoteContractException(409, "metadata_concurrency_conflict", "Dữ liệu hợp đồng đã thay đổi. Vui lòng tải lại trước khi chỉnh sửa."); }
        return ToSummary(contract);
    }
    public async Task<RemoteDocumentMetadata> UploadOriginalAsync(Guid actor, Guid id, Stream input, long length, CancellationToken ct)
    {
        var c = await OwnedDraftAsync(actor, id, ct); if (await db.Documents.AnyAsync(x => x.ContractId == id && !x.IsFinal, ct)) throw new RemoteContractException(409, "original_already_uploaded", "PDF gốc đã được tải lên."); if (length is <= 0 or > MaxPdfBytes) throw new RemoteContractException(400, "pdf_size_invalid", "PDF có dung lượng tối đa 20 MB."); await using var copy = new MemoryStream(); await input.CopyToAsync(copy, ct); var bytes = copy.ToArray(); if (bytes.Length != length || bytes.Length < 5 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8)) throw new RemoteContractException(400, "invalid_pdf", "Tệp không phải PDF hợp lệ.");
        if (HasPotentialCryptographicSignature(bytes)) throw new RemoteContractException(400, "cryptographic_signature_unsupported", "PDF có dấu hiệu chữ ký số hoặc cấu trúc không thể kiểm tra an toàn trước khi chỉnh sửa.");
        int pages; string metadata; try { using var parsed = PdfReader.Open(new MemoryStream(bytes, false), PdfDocumentOpenMode.Import); pages = parsed.PageCount; if (pages is < 1 or > 100) throw new RemoteContractException(400, "pdf_page_limit", "PDF phải có từ 1 đến 100 trang."); metadata = JsonSerializer.Serialize(Enumerable.Range(0, pages).Select(i => new { page = i + 1, width = parsed.Pages[i].Width, height = parsed.Pages[i].Height, rotation = parsed.Pages[i].Rotate })); } catch (RemoteContractException) { throw; } catch { throw new RemoteContractException(400, "invalid_pdf", "PDF bị lỗi, mã hóa hoặc không thể đọc."); }
        StoredPdf stored;
        try { stored = await storage.PutOriginalAsync(c.Id, new MemoryStream(bytes, false), bytes.LongLength, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is TaskCanceledException or TimeoutException) { throw new RemoteContractException(503, "pdf_upload_timeout", "Kho lưu trữ PDF phản hồi quá chậm. Vui lòng thử lại."); }
        catch (Exception e) when (e is IOException or HttpRequestException) { throw new RemoteContractException(503, "pdf_upload_failed", "Không thể tải PDF lên kho lưu trữ. Vui lòng thử lại."); }
        var doc = new ContractDocument { ContractId = id, StorageKey = stored.Key, Sha256 = stored.Sha256, Size = stored.Length, PageCount = pages, PageMetadata = metadata }; db.Documents.Add(doc); await AuditAsync(id, actor, "ORIGINAL_PDF_UPLOADED"); try { await db.SaveChangesAsync(ct); } catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_contract_documents_ContractId" }) { db.ChangeTracker.Clear(); await CleanupAsync(stored.Key, ct); throw new RemoteContractException(409, "original_already_uploaded", "PDF gốc đã được tải lên."); } catch { db.ChangeTracker.Clear(); await CleanupAsync(stored.Key, ct); throw; } return ToDocument(doc);
    }
    public async Task<(RemoteDocumentMetadata Metadata, Stream Content)> DownloadOriginalAsync(Guid actor, bool admin, Guid id, CancellationToken ct)
    {
        var c = await AccessibleAsync(actor, admin, id, ct);
        var requiresWorkingDocument = c.SigningMode == RemoteSigningMode.INVESTOR_AND_RESIDENT
            && c.Status is RemoteContractStatus.INVESTOR_SIGNED_DRAFT or RemoteContractStatus.PENDING_RESIDENT_SIGNATURE;
        if (requiresWorkingDocument && (string.IsNullOrWhiteSpace(c.WorkingStorageKey) || string.IsNullOrWhiteSpace(c.WorkingSha256)))
            throw new RemoteContractException(409, "working_document_missing", "Không tìm thấy PDF đã ký của chủ đầu tư.");
        var d = await db.Documents.AsNoTracking().SingleOrDefaultAsync(x => x.ContractId == id && x.IsFinal == (c.Status == RemoteContractStatus.COMPLETED), ct) ?? await db.Documents.AsNoTracking().SingleOrDefaultAsync(x => x.ContractId == id && !x.IsFinal, ct) ?? throw new RemoteContractException(404, "document_not_found", "Không tìm thấy PDF.");
        var key = (c.Status is RemoteContractStatus.INVESTOR_SIGNED_DRAFT or RemoteContractStatus.PENDING_RESIDENT_SIGNATURE) && !string.IsNullOrWhiteSpace(c.WorkingStorageKey) ? c.WorkingStorageKey : d.StorageKey; var expected = (c.Status is RemoteContractStatus.INVESTOR_SIGNED_DRAFT or RemoteContractStatus.PENDING_RESIDENT_SIGNATURE) && !string.IsNullOrWhiteSpace(c.WorkingSha256) ? c.WorkingSha256 : d.Sha256;
        Stream stream;
        try { stream = await storage.OpenReadAsync(key, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (FileNotFoundException) { throw new RemoteContractException(404, "document_not_found", "Không tìm thấy PDF."); }
        catch (Exception e) when (e is TaskCanceledException or TimeoutException) { throw new RemoteContractException(503, "pdf_download_timeout", "Kho lưu trữ PDF phản hồi quá chậm. Vui lòng thử lại."); }
        catch (Exception e) when (e is IOException or HttpRequestException) { throw new RemoteContractException(503, "pdf_download_failed", "Không thể tải PDF từ kho lưu trữ. Vui lòng thử lại."); }
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant(); stream.Position = 0; if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) { stream.Dispose(); throw new RemoteContractException(409, "document_integrity_failed", "Không kiểm tra được tính toàn vẹn PDF."); } await AuditAsync(id, actor, "DOCUMENT_VIEWED"); await db.SaveChangesAsync(ct); return (ToDocument(d), stream);
    }
    public async Task<IReadOnlyList<RemoteAuditEvent>> HistoryAsync(Guid actor, bool admin, Guid id, CancellationToken ct) { await AccessibleAsync(actor, admin, id, ct); var events = db.Events.AsNoTracking().Where(x => x.ContractId == id); if (!admin) events = events.Where(x => x.Type != "CONTRACT_REJECTED" || x.ActorId == actor); return await events.OrderByDescending(x => x.OccurredAt).Select(x => new RemoteAuditEvent(x.Id, x.ActorId, x.Type, x.OccurredAt, x.Metadata)).ToListAsync(ct); }
    private async Task<RemoteContractSummary> FinalizeAsync(RemoteContract c, ContractSigningSession s, SignatureRole role, CancellationToken ct)
    {
        EnsureSigningDeadline(c);
        var field = await db.SignatureFields.SingleAsync(x => x.ContractId == c.Id && x.Role == role, ct);
        var source = await LoadVerifiedSourceAsync(c, ct);
        EnsureBinding(c, source, field, s);
        var bytes = await stamper.StampAsync(new MemoryStream(source.Bytes, writable: false), field, s, ct);
        StoredPdf stored;
        try { stored = await storage.PutDerivedAsync(c.Id, role == SignatureRole.INVESTOR ? "investor-signed" : "completed", new MemoryStream(bytes, false), bytes.Length, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is TaskCanceledException or TimeoutException) { throw new RemoteContractException(503, "pdf_storage_timeout", "Kho lưu trữ PDF phản hồi quá chậm. Vui lòng thử lại."); }
        catch (Exception e) when (e is IOException or HttpRequestException) { throw new RemoteContractException(503, "pdf_storage_failed", "Không thể lưu PDF đã ký. Vui lòng thử lại."); }

        try
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var lockedContract = await LockAccessibleAsync(s.SignerId, role == SignatureRole.INVESTOR, c.Id, ct);
            var lockedRole = RequiredRole(lockedContract, role == SignatureRole.INVESTOR);
            if (lockedRole != role) throw new RemoteContractException(409, "signing_context_changed", "Trạng thái hợp đồng đã thay đổi. Vui lòng tải lại.");
            EnsureSigningDeadline(lockedContract);

            var lockedSession = await db.SigningSessions.SingleOrDefaultAsync(x => x.Id == s.Id && x.ContractId == c.Id && x.SignerId == s.SignerId, ct)
                ?? throw new RemoteContractException(409, "signing_session_replaced", "Phiên ký đã thay đổi. Vui lòng yêu cầu mã OTP mới.");
            if (lockedSession.FinalizedAt is not null)
            {
                await transaction.CommitAsync(ct);
                await CleanupAsync(stored.Key, ct);
                return ToSummary(lockedContract);
            }

            var lockedField = await db.SignatureFields.SingleOrDefaultAsync(x => x.ContractId == c.Id && x.Role == role, ct)
                ?? throw new RemoteContractException(409, "signature_field_missing", "Thiếu vị trí ký.");
            EnsureBinding(lockedContract, source, lockedField, lockedSession);
            lockedSession.FinalizationStartedAt = DateTimeOffset.UtcNow;

            if (role == SignatureRole.INVESTOR)
            {
                lockedContract.WorkingStorageKey = stored.Key;
                lockedContract.WorkingSha256 = stored.Sha256;
                lockedContract.Status = lockedContract.Status == RemoteContractStatus.PENDING_INVESTOR_SIGNATURE ? RemoteContractStatus.PENDING_RESIDENT_SIGNATURE : RemoteContractStatus.INVESTOR_SIGNED_DRAFT;
                await AuditAsync(lockedContract.Id, lockedSession.SignerId, "INVESTOR_SIGNATURE_FINALIZED");
            }
            else
            {
                db.Documents.Add(new ContractDocument { ContractId = lockedContract.Id, StorageKey = stored.Key, Sha256 = stored.Sha256, Size = stored.Length, MimeType = "application/pdf", IsFinal = true });
                lockedContract.Status = RemoteContractStatus.COMPLETED;
                lockedContract.MetadataUpdatedAt = DateTimeOffset.UtcNow;
                await AuditAsync(lockedContract.Id, lockedSession.SignerId, "RESIDENT_SIGNATURE_FINALIZED");
                await AuditAsync(lockedContract.Id, lockedSession.SignerId, "CONTRACT_COMPLETED");
            }

            lockedSession.FinalizedAt = DateTimeOffset.UtcNow;
            lockedSession.ClearSignaturePayload();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            if (role == SignatureRole.RESIDENT)
                await NotifyStatusChangedAsync(lockedContract, RemoteContractStatus.PENDING_RESIDENT_SIGNATURE, ct);
            return ToSummary(lockedContract);
        }
        catch
        {
            db.ChangeTracker.Clear();
            await CleanupAsync(stored.Key, ct);
            throw;
        }
    }
    private async Task ValidateCoordinatesAsync(Guid id, IReadOnlyList<SignatureFieldInput> fields, CancellationToken ct) { var doc = await db.Documents.AsNoTracking().SingleOrDefaultAsync(x => x.ContractId == id && !x.IsFinal, ct); if (doc is null) return; await using var input = await storage.OpenReadAsync(doc.StorageKey, ct); using var parsed = PdfReader.Open(input, PdfDocumentOpenMode.Import); foreach (var f in fields) { if (f.Page > parsed.PageCount) throw new RemoteContractException(400, "invalid_signature_coordinates", "Trang ký không tồn tại."); var p = parsed.Pages[f.Page - 1]; if (f.X + f.Width > (decimal)p.Width.Point || f.Y + f.Height > (decimal)p.Height.Point) throw new RemoteContractException(400, "invalid_signature_coordinates", "Vị trí ký nằm ngoài trang PDF."); } }
    private async Task<RemoteContract> AccessibleAsync(Guid actor, bool admin, Guid id, CancellationToken ct)
    {
        if (!admin) await EnsureEligibleResidentAsync(actor, false, ct);
        var contract = await db.Contracts.SingleOrDefaultAsync(x => x.Id == id && (admin ? x.CreatorId == actor : x.ResidentId == actor && (x.Status == RemoteContractStatus.PENDING_RESIDENT_SIGNATURE || x.Status == RemoteContractStatus.COMPLETED || x.Status == RemoteContractStatus.REJECTED || x.Status == RemoteContractStatus.EXPIRED)), ct)
            ?? throw new RemoteContractException(404, "contract_not_found", "Không tìm thấy hợp đồng.");
        await ExpireIfOverdueAsync(contract, ct);
        return contract;
    }
    private async Task ExpireIfOverdueAsync(RemoteContract contract, CancellationToken ct)
    {
        if (contract.Status is not (RemoteContractStatus.PENDING_INVESTOR_SIGNATURE or RemoteContractStatus.PENDING_RESIDENT_SIGNATURE)
            || contract.Deadline is not { } deadline || deadline > DateTimeOffset.UtcNow) return;
        var previous = contract.Status;
        contract.Status = RemoteContractStatus.EXPIRED;
        contract.MetadataUpdatedAt = DateTimeOffset.UtcNow;
        await AuditAsync(contract.Id, contract.CreatorId, "CONTRACT_EXPIRED", JsonSerializer.Serialize(new { previousStatus = previous.ToString(), deadline }));
        await InvalidateUnfinishedSessionsAsync(contract.Id, ct);
        await db.SaveChangesAsync(ct);
        await NotifyStatusChangedAsync(contract, previous, ct);
    }
    private async Task ExpireOverdueAsync(Guid actor, bool admin, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var overdue = await db.Contracts.Where(x => x.Deadline != null && x.Deadline <= now
            && (x.Status == RemoteContractStatus.PENDING_INVESTOR_SIGNATURE || x.Status == RemoteContractStatus.PENDING_RESIDENT_SIGNATURE)
            && (admin ? x.CreatorId == actor : x.ResidentId == actor)).ToListAsync(ct);
        var changes = new List<(RemoteContract Contract, RemoteContractStatus Previous)>();
        foreach (var contract in overdue)
        {
            var previous = contract.Status;
            contract.Status = RemoteContractStatus.EXPIRED;
            contract.MetadataUpdatedAt = DateTimeOffset.UtcNow;
            changes.Add((contract, previous));
            await AuditAsync(contract.Id, contract.CreatorId, "CONTRACT_EXPIRED", JsonSerializer.Serialize(new { previousStatus = previous.ToString(), contract.Deadline }));
            await InvalidateUnfinishedSessionsAsync(contract.Id, ct);
        }
        if (overdue.Count != 0)
        {
            await db.SaveChangesAsync(ct);
            foreach (var (contract, previous) in changes)
                await NotifyStatusChangedAsync(contract, previous, ct);
        }
    }
    public async Task<int> ExpireDueContractsAsync(int batchSize, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var ids = await db.Contracts.AsNoTracking()
            .Where(x => x.Deadline != null && x.Deadline <= now
                && (x.Status == RemoteContractStatus.PENDING_INVESTOR_SIGNATURE || x.Status == RemoteContractStatus.PENDING_RESIDENT_SIGNATURE))
            .OrderBy(x => x.Deadline).Select(x => x.Id).Take(batchSize).ToListAsync(ct);
        var expired = 0;
        foreach (var id in ids)
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var contract = db.Database.IsRelational()
                ? await db.Contracts.FromSqlInterpolated($"SELECT c.*, c.xmin FROM remote_online_sign.contracts AS c WHERE c.\"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct)
                : await db.Contracts.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (contract is null || contract.Deadline is not { } deadline || deadline > DateTimeOffset.UtcNow
                || contract.Status is not (RemoteContractStatus.PENDING_INVESTOR_SIGNATURE or RemoteContractStatus.PENDING_RESIDENT_SIGNATURE))
            {
                await transaction.CommitAsync(ct);
                continue;
            }
            var previous = contract.Status;
            contract.Status = RemoteContractStatus.EXPIRED;
            contract.MetadataUpdatedAt = DateTimeOffset.UtcNow;
            await AuditAsync(contract.Id, contract.CreatorId, "CONTRACT_EXPIRED", JsonSerializer.Serialize(new { previousStatus = previous.ToString(), contract.Deadline }));
            await InvalidateUnfinishedSessionsAsync(contract.Id, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            expired++;
            await NotifyStatusChangedAsync(contract, previous, ct);
        }
        return expired;
    }
    public async Task<RemoteContractSummary> RenewDeadlineAsync(Guid actor, Guid id, RenewRemoteDeadline input, CancellationToken ct)
    {
        if (input.Deadline <= DateTimeOffset.UtcNow) throw new RemoteContractException(400, "deadline_must_be_future", "Hạn ký phải ở tương lai.");
        await ExpireOverdueAsync(actor, true, ct);
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var contract = await LockAccessibleAsync(actor, true, id, ct);
        if (contract.Version != input.Version) throw new RemoteContractException(409, "metadata_concurrency_conflict", "Dữ liệu hợp đồng đã thay đổi. Vui lòng tải lại trước khi chỉnh sửa.");
        if (contract.Status != RemoteContractStatus.EXPIRED) throw new RemoteContractException(409, "contract_not_expired", "Chỉ hợp đồng hết hạn mới có thể gia hạn.");
        var previousStatus = await StatusBeforeExpiryAsync(contract, ct);
        var previousDeadline = contract.Deadline;
        contract.Status = previousStatus;
        contract.Deadline = input.Deadline.ToUniversalTime();
        contract.Revision++;
        contract.MetadataUpdatedAt = DateTimeOffset.UtcNow;
        await InvalidateUnfinishedSessionsAsync(id, ct);
        await AuditAsync(id, actor, "CONTRACT_DEADLINE_RENEWED", JsonSerializer.Serialize(new { previousStatus = previousStatus.ToString(), previousDeadline, newDeadline = contract.Deadline }));
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new RemoteContractException(409, "metadata_concurrency_conflict", "Dữ liệu hợp đồng đã thay đổi. Vui lòng tải lại trước khi chỉnh sửa."); }
        return ToSummary(contract);
    }
    public async Task<int> CleanupExpiredSignaturePayloadsAsync(int batchSize, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var candidates = await db.SigningSessions.AsNoTracking()
            .Where(x => (x.SignatureImage != null || x.TypedSignatureText != null || x.TypedSignatureFont != null)
                && (x.FinalizedAt != null || x.ExpiresAt == null || x.ExpiresAt <= now))
            .OrderBy(x => x.Id).Select(x => x.Id).Take(batchSize).ToListAsync(ct);
        if (candidates.Count == 0) return 0;
        if (db.Database.IsRelational())
        {
            return await db.SigningSessions
                .Where(x => candidates.Contains(x.Id)
                    && (x.SignatureImage != null || x.TypedSignatureText != null || x.TypedSignatureFont != null)
                    && (x.FinalizedAt != null || x.ExpiresAt == null || x.ExpiresAt <= now))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.SignatureImage, (byte[]?)null)
                    .SetProperty(x => x.TypedSignatureText, (string?)null)
                    .SetProperty(x => x.TypedSignatureFont, (string?)null), ct);
        }
        var cleaned = 0;
        foreach (var id in candidates)
        {
            var session = await db.SigningSessions.SingleOrDefaultAsync(x => x.Id == id
                && (x.SignatureImage != null || x.TypedSignatureText != null || x.TypedSignatureFont != null)
                && (x.FinalizedAt != null || x.ExpiresAt == null || x.ExpiresAt <= now), ct);
            if (session is null) continue;
            session.ClearSignaturePayload();
            cleaned++;
        }
        if (cleaned > 0) await db.SaveChangesAsync(ct);
        return cleaned;
    }
    private async Task InvalidateUnfinishedSessionsAsync(Guid contractId, CancellationToken ct)
    {
        var staleSessions = await db.SigningSessions.Where(x => x.ContractId == contractId && x.FinalizedAt == null).ToListAsync(ct);
        foreach (var session in staleSessions) { session.ExpiresAt = DateTimeOffset.UtcNow; session.ClearSignaturePayload(); }
    }
    private async Task NotifyStatusChangedAsync(RemoteContract contract, RemoteContractStatus previous, CancellationToken ct)
    {
        if (statusNotifier is null || previous == contract.Status) return;
        try
        {
            await statusNotifier.NotifyAsync(new(contract.Id, previous.ToString(), contract.Status.ToString(), contract.Revision, contract.MetadataUpdatedAt.ToUniversalTime()), ct);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Remote contract status notification deferred for {ContractId}.", contract.Id);
        }
    }
    private async Task<RemoteContractStatus> StatusBeforeExpiryAsync(RemoteContract contract, CancellationToken ct)
    {
        var expiryEvent = await db.Events.AsNoTracking().Where(x => x.ContractId == contract.Id && x.Type == "CONTRACT_EXPIRED").OrderByDescending(x => x.OccurredAt).FirstOrDefaultAsync(ct);
        if (expiryEvent is not null)
        {
            try
            {
                using var json = JsonDocument.Parse(expiryEvent.Metadata);
                if (json.RootElement.TryGetProperty("previousStatus", out var value)
                    && Enum.TryParse<RemoteContractStatus>(value.GetString(), out var parsed)
                    && parsed is RemoteContractStatus.PENDING_INVESTOR_SIGNATURE or RemoteContractStatus.PENDING_RESIDENT_SIGNATURE)
                    return parsed;
            }
            catch (JsonException) { }
        }
        var hasProgress = contract.ResidentId is not null
            || await db.SigningSessions.AnyAsync(x => x.ContractId == contract.Id && x.FinalizedAt != null, ct);
        return hasProgress ? RemoteContractStatus.PENDING_RESIDENT_SIGNATURE : RemoteContractStatus.PENDING_INVESTOR_SIGNATURE;
    }
    private async Task<RemoteContract> LockAccessibleAsync(Guid actor, bool admin, Guid id, CancellationToken ct)
    {
        if (!admin) await EnsureEligibleResidentAsync(actor, false, ct);
        var contract = (db.Database.IsRelational()
            ? await db.Contracts.FromSqlInterpolated($"SELECT c.*, c.xmin FROM remote_online_sign.contracts AS c WHERE c.\"Id\" = {id} FOR UPDATE")
                .SingleOrDefaultAsync(ct)
            : await db.Contracts.SingleOrDefaultAsync(x => x.Id == id, ct))
            ?? throw new RemoteContractException(404, "contract_not_found", "Không tìm thấy hợp đồng.");
        if ((admin && contract.CreatorId != actor) || (!admin && (contract.ResidentId != actor || contract.Status is not (RemoteContractStatus.PENDING_RESIDENT_SIGNATURE or RemoteContractStatus.COMPLETED or RemoteContractStatus.REJECTED or RemoteContractStatus.EXPIRED)))) throw new RemoteContractException(404, "contract_not_found", "Không tìm thấy hợp đồng.");
        return contract;
    }

    private async Task EnsureEligibleResidentAsync(Guid userId, bool assignment, CancellationToken ct)
    {
        var account = (await accounts.GetAsync([userId], ct)).SingleOrDefault();
        var hasResidentRole = (await accountAccess.GetResidentUserIdsAsync([userId], ct)).Contains(userId);
        var hasActiveResidentProfile = await residents.IsActiveAccountAsync(userId, ct);
        if (account is not null && string.Equals(account.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase)
            && hasResidentRole && hasActiveResidentProfile)
            return;

        if (assignment)
            throw new RemoteContractException(400, "resident_not_eligible", "Tài khoản được chọn không phải cư dân đang hoạt động đủ điều kiện ký.");
        throw new RemoteContractException(403, "resident_access_unavailable", "Tài khoản cư dân không còn đủ điều kiện truy cập hợp đồng.");
    }
    private async Task<RemoteContract> OwnedDraftAsync(Guid actor, Guid id, CancellationToken ct) { var x = await db.Contracts.SingleOrDefaultAsync(x => x.Id == id && x.CreatorId == actor, ct) ?? throw new RemoteContractException(404, "contract_not_found", "Không tìm thấy hợp đồng."); if (x.Status != RemoteContractStatus.DRAFT) throw new RemoteContractException(409, "contract_not_draft", "Hợp đồng không còn ở bản nháp."); return x; }
    private static void EnsureModeSelected(RemoteContract contract) { if (contract.SigningMode == RemoteSigningMode.UNSELECTED) throw new RemoteContractException(409, "signing_mode_required", "Hãy chọn chế độ ký trước khi tiếp tục thiết lập hợp đồng."); }
    private static SignatureRole RequiredRole(RemoteContract c, bool admin)
    {
        EnsureModeSelected(c);
        if (admin && ((c.SigningMode == RemoteSigningMode.INVESTOR_AND_RESIDENT && c.Status == RemoteContractStatus.DRAFT) ||
            c.Status == RemoteContractStatus.PENDING_INVESTOR_SIGNATURE))
            return SignatureRole.INVESTOR;
        if (!admin && c.Status == RemoteContractStatus.PENDING_RESIDENT_SIGNATURE)
            return SignatureRole.RESIDENT;
        throw new RemoteContractException(409, "signing_not_available", "Chưa đến lượt ký của tài khoản.");
    }
    private static void EnsureSigningDeadline(RemoteContract contract)
    {
        if (contract.Deadline is { } deadline && deadline <= DateTimeOffset.UtcNow)
            throw new RemoteContractException(409, "contract_expired", "Hạn ký đã hết.");
    }
    private static DateTimeOffset UtcNowAtMicrosecondPrecision()
    {
        var now = DateTimeOffset.UtcNow;
        return new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero);
    }
    private async Task<VerifiedSource> LoadVerifiedSourceAsync(RemoteContract c, CancellationToken ct)
    {
        var source = await SourceDocumentAsync(c, ct);
        byte[] bytes;
        try
        {
            await using var stream = await storage.OpenReadAsync(source.Key, ct);
            bytes = await ReadBoundedAsync(stream, MaxPdfBytes, ct);
        }
        catch (RemoteContractException) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Remote signing source PDF cannot be read for contract {ContractId}", c.Id);
            throw new RemoteContractException(409, "document_integrity_failed", "Không thể đọc PDF nguồn để kiểm tra tính toàn vẹn.");
        }
        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(actual, source.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new RemoteContractException(409, "document_integrity_failed", "Không kiểm tra được tính toàn vẹn PDF trước khi ký.");
        return new(source.Key, source.Sha256, bytes);
    }
    private async Task<(string Key, string Sha256)> SourceDocumentAsync(RemoteContract c, CancellationToken ct) { var original = await db.Documents.AsNoTracking().SingleOrDefaultAsync(x => x.ContractId == c.Id && !x.IsFinal, ct) ?? throw new RemoteContractException(409, "original_document_required", "Thiếu PDF gốc."); return (!string.IsNullOrWhiteSpace(c.WorkingStorageKey) ? c.WorkingStorageKey! : original.StorageKey, !string.IsNullOrWhiteSpace(c.WorkingSha256) ? c.WorkingSha256! : original.Sha256); }
    private static async Task<byte[]> ReadBoundedAsync(Stream source, int maximumBytes, CancellationToken ct)
    {
        await using var copy = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) != 0)
        {
            if (copy.Length + read > maximumBytes) throw new RemoteContractException(409, "document_integrity_failed", "PDF nguồn vượt quá giới hạn cho phép.");
            await copy.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return copy.ToArray();
    }
    private static void EnsureBinding(RemoteContract c, VerifiedSource source, ContractSignatureField field, ContractSigningSession session)
    {
        if (!string.Equals(session.BindingHash, Binding(c, (source.Key, source.Sha256), field, session), StringComparison.OrdinalIgnoreCase))
            throw new RemoteContractException(409, "signing_context_changed", "Tài liệu hoặc ngữ cảnh ký đã thay đổi. Vui lòng yêu cầu mã OTP mới.");
    }
    // PdfSharpCore does not expose a cryptographic signature validator. This is
    // intentionally a conservative rejection policy, not signature verification:
    // a visual/image signature remains allowed, while recognizable signature
    // dictionaries are not modified because stamping could invalidate them.
    private static bool HasPotentialCryptographicSignature(byte[] bytes)
    {
        var pdf = Encoding.Latin1.GetString(bytes);
        return pdf.Contains("/ByteRange", StringComparison.Ordinal)
            || pdf.Contains("/Type/Sig", StringComparison.Ordinal)
            || pdf.Contains("/Type /Sig", StringComparison.Ordinal)
            || pdf.Contains("/FT/Sig", StringComparison.Ordinal)
            || pdf.Contains("/FT /Sig", StringComparison.Ordinal)
            || pdf.Contains("/SubFilter", StringComparison.Ordinal) && pdf.Contains("/adbe.", StringComparison.OrdinalIgnoreCase)
            // PdfSharpCore does not expose a signature graph from compressed
            // object streams. Reject them rather than silently modifying an
            // unverifiable cryptographically signed document.
            || pdf.Contains("/ObjStm", StringComparison.Ordinal);
    }
    private static void ValidateSignature(BeginSignature x) { if (x.Method == SignatureMethod.TYPE && x.Image is null) { if (string.IsNullOrWhiteSpace(x.Text) || x.Text.Trim().Length > 150 || x.Font is not ("Arial" or "Times New Roman" or "Courier New")) throw new RemoteContractException(400, "invalid_typed_signature", "Chữ ký gõ không hợp lệ."); return; } if (x.Image is null || x.Image.Length is 0 or > MaxSignatureBytes || !(x.Image.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47 }) || x.Image.AsSpan().StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }))) throw new RemoteContractException(400, "invalid_signature_image", "Chữ ký phải là ảnh PNG hoặc JPEG hợp lệ, tối đa 5 MB."); try { using var image = PdfSharpCore.Drawing.XImage.FromStream(() => new MemoryStream(x.Image, writable: false)); } catch { throw new RemoteContractException(400, "invalid_signature_image", "Ảnh chữ ký không thể đọc."); } }
    private static string SignatureHash(BeginSignature x) => Convert.ToHexString(SHA256.HashData(x.Method == SignatureMethod.TYPE && x.Image is null ? Encoding.UTF8.GetBytes($"{x.Text}|{x.Font}") : x.Image!)).ToLowerInvariant();
    private static string Binding(RemoteContract c, (string Key, string Sha256) source, ContractSignatureField field, ContractSigningSession s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{c.Id:N}|{c.Revision}|{source.Sha256}|{field.Id:N}|{field.Role}|{s.SignerId:N}|{s.SignatureSha256}|{s.Id:N}"))).ToLowerInvariant();
    private sealed record VerifiedSource(string Key, string Sha256, byte[] Bytes);
    private async Task CleanupAsync(string key, CancellationToken ct) { try { await storage.DeleteIfUnreferencedAsync(key, ct); } catch (Exception e) { logger.LogError(e, "Remote contract orphan cleanup failed for uploaded artifact"); } }
    private static string NormalizeContractName(string? value) { var name = value?.Trim(); if (string.IsNullOrWhiteSpace(name) || name.Length > 200) throw new RemoteContractException(400, "contract_name_invalid", "Tên hợp đồng là bắt buộc và tối đa 200 ký tự."); return name; }
    private Task AuditAsync(Guid id, Guid actor, string type, string metadata = "{}") { db.Events.Add(new ContractEvent { ContractId = id, ActorId = actor, Type = type, Metadata = metadata }); return Task.CompletedTask; }
    private async Task<IReadOnlyList<RemoteContractSummary>> ToSummariesAsync(IReadOnlyCollection<RemoteContract> contracts, CancellationToken ct)
    {
        var residentByUserId = await residents.GetByAccountIdsAsync(contracts.Where(x => x.ResidentId is not null).Select(x => x.ResidentId!.Value).ToArray(), ct);
        var contractIds = contracts.Select(x => x.Id).ToArray();
        var residentSignedContractIds = (await db.SigningSessions.AsNoTracking()
            .Where(x => contractIds.Contains(x.ContractId) && x.Role == SignatureRole.RESIDENT && x.FinalizedAt != null)
            .Select(x => x.ContractId).Distinct().ToListAsync(ct)).ToHashSet();
        return contracts.Select(x => residentByUserId.TryGetValue(x.ResidentId ?? Guid.Empty, out var resident)
            ? new RemoteContractSummary(x.Id, x.ContractCode, x.ContractName, x.CreatorId, x.ResidentId, resident.FullName, resident.ResidentCode, x.Status.ToString(), x.SigningMode.ToString(), x.Deadline, x.MetadataUpdatedAt, x.Revision, x.Version, residentSignedContractIds.Contains(x.Id))
            : new RemoteContractSummary(x.Id, x.ContractCode, x.ContractName, x.CreatorId, x.ResidentId, null, null, x.Status.ToString(), x.SigningMode.ToString(), x.Deadline, x.MetadataUpdatedAt, x.Revision, x.Version, residentSignedContractIds.Contains(x.Id))).ToArray();
    }
    private RemoteContractSummary ToSummary(RemoteContract x) => new(x.Id, x.ContractCode, x.ContractName, x.CreatorId, x.ResidentId, null, null, x.Status.ToString(), x.SigningMode.ToString(), x.Deadline, x.MetadataUpdatedAt, x.Revision, x.Version, false);
    private static RemoteDocumentMetadata ToDocument(ContractDocument d) => new(d.Id, d.Sha256, d.Size, d.PageCount, d.PageMetadata, d.IsFinal);
}
public sealed class RemoteContractException(int status, string code, string message) : Exception(message) { public int Status { get; } = status; public string Code { get; } = code; }
