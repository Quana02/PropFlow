using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.Payments.Application.PaymentSettings;
using PropFlow.Modules.Payments.Contracts;
using PropFlow.Modules.Payments.Domain.Payments;
using PropFlow.Modules.Payments.Domain.PaymentDestinations;
using PropFlow.Modules.Payments.Domain.PaymentInstructions;
using PropFlow.Modules.Payments.Infrastructure.Persistence;
using QRCoder;
using Microsoft.Extensions.Options;

namespace PropFlow.Modules.Payments.Infrastructure.ResidentPayments;

public sealed class ResidentPaymentFailure(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public sealed class ResidentServiceRequestPayments(
    PaymentsDbContext db,
    IServiceRequestBilling billing,
    IPaymentAccountProtector protector,
    TimeProvider clock,
    IOptions<SePayOptions>? options = null,
    IOptions<PropFlow.Modules.Payments.Infrastructure.PaymentSettings.SePayOAuthOptions>? oauth = null,
    PropFlow.Modules.Payments.Infrastructure.PaymentSettings.SePaySandbox? sandboxSetup = null)
{
    public async Task<ResidentServiceRequestPaymentView> GetAsync(Guid serviceRequestId, Guid userId, CancellationToken ct)
    {
        var settlement = await OwnedSettlementAsync(serviceRequestId, userId, ct);
        var sandbox = await db.SePaySandboxConnections.AsNoTracking().SingleOrDefaultAsync(ct);
        if (sandbox?.DemoEnabled == true)
        {
            var simulation = await db.SandboxPaymentSimulations.AsNoTracking().SingleOrDefaultAsync(x => x.InvoiceId == settlement.InvoiceId && x.UserId == userId, ct);
            var display = SandboxPaymentDisplay.Balance(settlement.TotalAmount, settlement.ConfirmedPaidAmount, simulation?.VerifiedAmount ?? 0);
            return new(serviceRequestId, settlement.InvoiceId, settlement.InvoiceNumber, settlement.TotalAmount,
                display.Paid, display.Outstanding, display.Status,
                simulation?.VerifiedAmount > 0 ? "SANDBOX_VERIFIED" : "SANDBOX_PENDING", null, null, null,
                simulation?.BankName, simulation?.BankCode, simulation?.AccountLast4 is { } last4 ? "******" + last4 : null, simulation?.AccountName, null,
                TransferCode: simulation?.TransferCode,
                PaymentEnvironment: "SANDBOX", SandboxSimulationAvailable: sandbox.ProtectedApiToken is not null && sandbox.LastCheckSucceeded && sandbox.ProtectedWebhookKey is not null,
                SimulatedTransferAmount: simulation?.VerifiedAmount > 0 ? simulation.VerifiedAmount : null);
        }
        var payment = await LatestAsync(settlement.InvoiceId, userId, ct);
        var destination = await DestinationAsync(settlement.InvoiceId, ct);
        var automatic = await AutomaticAsync(destination, ct);
        var instruction = await db.PaymentInstructions.AsNoTracking().SingleOrDefaultAsync(x => x.InvoiceId == settlement.InvoiceId, ct);
        var qrUrl = settlement.OutstandingAmount > 0 && (automatic || payment?.Status != PaymentStatus.PENDING) && destination is not null
            ? $"/api/v1/resident/service-requests/{serviceRequestId}/payment/qr"
            : null;
        return new(serviceRequestId, settlement.InvoiceId, settlement.InvoiceNumber, settlement.TotalAmount,
            settlement.ConfirmedPaidAmount, settlement.OutstandingAmount, settlement.Status,
            settlement.OutstandingAmount == 0 ? "CONFIRMED" : payment?.Status == PaymentStatus.PENDING ? "PENDING" : settlement.ConfirmedPaidAmount > 0 ? "PARTIAL" : payment?.Status.ToString(),
            payment?.Id, payment?.ReferenceNumber, payment?.RejectionReason,
            destination?.BankName, destination?.BankCode,
            destination is null ? null : $"****{destination.AccountNumberLast4}", destination?.AccountName, qrUrl, automatic, instruction?.TransferCode);
    }

    public async Task<byte[]> QrAsync(Guid serviceRequestId, Guid userId, CancellationToken ct)
    {
        var settlement = await OwnedSettlementAsync(serviceRequestId, userId, ct);
        await EnsureLiveAsync(ct);
        if (settlement.OutstandingAmount <= 0)
            throw new ResidentPaymentFailure(409, "invoice_already_paid", "Hóa đơn đã được thanh toán.");
        var destination = await DestinationAsync(settlement.InvoiceId, ct)
            ?? throw new ResidentPaymentFailure(409, "payment_destination_unavailable", "Chưa có tài khoản nhận tiền đang hoạt động.");
        if (!await AutomaticAsync(destination, ct) &&
            await db.Payments.AnyAsync(x => x.InvoiceId == settlement.InvoiceId && x.Status == PaymentStatus.PENDING, ct))
            throw new ResidentPaymentFailure(409, "payment_pending", "Khoản chuyển đang chờ kế toán xác nhận.");
        var account = protector.Unprotect(destination.ProtectedAccountNumber);
        var instruction = await db.PaymentInstructions.AsNoTracking().SingleAsync(x => x.InvoiceId == settlement.InvoiceId, ct);
        var payload = VietQrPayload(destination.BankBin, account, settlement.OutstandingAmount, instruction.TransferCode);
        using var data = QRCodeGenerator.GenerateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(10);
    }

    private async Task<bool> AutomaticAsync(PaymentDestination? destination, CancellationToken ct)
    {
        if (options?.Value.Enabled != true || !SePayVerification.HasCredential(options.Value) || destination?.SePayAccountId is null) return false;
        if (oauth?.Value.Enabled != true) return true;
        return await db.SePayConnections.AsNoTracking().AnyAsync(x => !x.RequiresReconnect && x.ProtectedRefreshToken != null &&
            x.WebhookId != null && x.WebhookDestinationId == destination.Id, ct);
    }

    public async Task<ResidentServiceRequestPaymentView> ReportAsync(
        Guid serviceRequestId, Guid userId, ReportResidentServiceRequestTransferRequest request, CancellationToken ct)
    {
        await EnsureLiveAsync(ct);
        var reference = request.ReferenceNumber?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(reference) || reference.Length > 100)
            throw new ResidentPaymentFailure(400, "invalid_transfer_reference", "Vui lòng nhập mã giao dịch hợp lệ.");
        if (request.Note?.Trim().Length > 1000)
            throw new ResidentPaymentFailure(400, "transfer_note_too_long", "Ghi chú không được vượt quá 1000 ký tự.");
        var settlement = await OwnedSettlementAsync(serviceRequestId, userId, ct);
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        if (transaction is not null)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({settlement.InvoiceId.ToString()}, 0))", ct);
            settlement = await OwnedSettlementAsync(serviceRequestId, userId, ct);
        }
        if (settlement.OutstandingAmount <= 0)
            throw new ResidentPaymentFailure(409, "invoice_already_paid", "Hóa đơn đã được thanh toán.");
        if (await db.Payments.AnyAsync(x => x.InvoiceId == settlement.InvoiceId && x.Status == PaymentStatus.PENDING, ct))
            throw new ResidentPaymentFailure(409, "payment_pending", "Khoản chuyển đang chờ kế toán xác nhận.");

        var now = clock.GetUtcNow();
        var transferredAt = request.TransferredAt ?? now;
        if (transferredAt > now.AddMinutes(5))
            throw new ResidentPaymentFailure(400, "invalid_transfer_time", "Thời gian chuyển khoản không hợp lệ.");
        var number = $"SR-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        var payment = new Payment(number[..Math.Min(40, number.Length)], settlement.InvoiceId,
            settlement.OutstandingAmount, "BANK_TRANSFER", transferredAt, now, userId, reference, request.Note);
        db.Payments.Add(payment);
        db.PaymentStatusHistory.Add(new PaymentStatusHistory(payment.Id, PaymentStatus.PENDING, userId, now,
            reason: "Cư dân báo đã chuyển khoản"));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            throw new ResidentPaymentFailure(409, "transfer_reference_exists", "Mã giao dịch đã được sử dụng.");
        }
        if (transaction is not null) await transaction.CommitAsync(ct);
        return await GetAsync(serviceRequestId, userId, ct);
    }

    public async Task<ResidentServiceRequestPaymentView> PrepareSandboxAsync(Guid serviceRequestId, Guid userId, CancellationToken ct)
    {
        var settlement = await OwnedSettlementAsync(serviceRequestId, userId, ct);
        var version = await db.SePaySandboxConnections.AsNoTracking().Select(x => (int?)x.Version).SingleOrDefaultAsync(ct);
        if (sandboxSetup is null) throw new ResidentPaymentFailure(409, "sandbox_setup_required", "Cần cấu hình kết nối và khóa webhook Sandbox trước.");
        PropFlow.Modules.Payments.Infrastructure.PaymentSettings.SandboxReceivingAccount account;
        try { account = await sandboxSetup.ReceivingAccountAsync(ct); }
        catch (PaymentSettingsFailure e) { throw new ResidentPaymentFailure(e.Status, e.Code, e.Message); }
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(728405193)", ct);
        var sandbox = await db.SePaySandboxConnections.AsNoTracking().SingleOrDefaultAsync(ct);
        if (sandbox?.DemoEnabled != true || sandbox.Version != version || sandbox.ProtectedApiToken is null || !sandbox.LastCheckSucceeded || sandbox.ProtectedWebhookKey is null)
            throw new ResidentPaymentFailure(409, "sandbox_simulation_disabled", "Cấu hình Sandbox đã thay đổi hoặc chưa sẵn sàng. Vui lòng tải lại.");
        if (settlement.OutstandingAmount <= 0) throw new ResidentPaymentFailure(409, "invoice_already_paid", "Hóa đơn đã được thanh toán.");
        var existing = await db.SandboxPaymentSimulations.SingleOrDefaultAsync(x => x.InvoiceId == settlement.InvoiceId, ct);
        if (existing is null)
        {
            existing = new() { InvoiceId = settlement.InvoiceId, ServiceRequestId = serviceRequestId, UserId = userId, CreatedAt = clock.GetUtcNow() };
            db.SandboxPaymentSimulations.Add(existing);
        }
        if (existing.UserId != userId) throw new ResidentPaymentFailure(409, "sandbox_owner_changed", "Hóa đơn thử nghiệm đã được gắn với tài khoản khác.");
        if (existing.TransferCode is null)
        {
            existing.TransferCode = "PS" + Guid.NewGuid().ToString("N")[..22].ToUpperInvariant();
            existing.RequestedAmount = settlement.OutstandingAmount; existing.VerifiedAmount = 0;
            existing.ProtectedAccountNumber = protector.Protect(account.AccountNumber);
            existing.AccountLast4 = account.AccountNumber[^4..]; existing.AccountName = account.AccountName;
            existing.BankName = account.BankName; existing.BankCode = account.BankCode;
            existing.GatewayAliases = System.Text.Json.JsonSerializer.Serialize(new[] { account.BankCode, account.BankName, account.ShortName }.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray());
        }
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return await GetAsync(serviceRequestId, userId, ct);
    }

    private async Task EnsureLiveAsync(CancellationToken ct)
    {
        if (await db.SePaySandboxConnections.AsNoTracking().AnyAsync(x => x.DemoEnabled, ct))
            throw new ResidentPaymentFailure(409, "sandbox_real_transfer_disabled", "Đang bật mô phỏng Sandbox. Chức năng QR và báo chuyển khoản thật được tạm ẩn.");
    }

    private async Task<ServiceRequestSettlementSnapshot> OwnedSettlementAsync(Guid serviceRequestId, Guid userId, CancellationToken ct)
    {
        var settlement = await billing.FindForUserAsync(serviceRequestId, userId, ct)
            ?? throw new ResidentPaymentFailure(404, "service_request_invoice_not_found", "Không tìm thấy hóa đơn của yêu cầu này.");
        if (settlement.Status == "CANCELLED") throw new ResidentPaymentFailure(409, "invoice_cancelled", "Hóa đơn đã hủy, không thể thanh toán.");
        return settlement;
    }

    private Task<Payment?> LatestAsync(Guid invoiceId, Guid userId, CancellationToken ct) => db.Payments.AsNoTracking()
        .Where(x => x.InvoiceId == invoiceId && x.SubmittedBy == userId)
        .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);

    private async Task<PaymentDestination?> DestinationAsync(Guid invoiceId, CancellationToken ct)
    {
        var pinned = await (from instruction in db.PaymentInstructions.AsNoTracking()
                            join destination in db.PaymentDestinations.AsNoTracking()
                                on instruction.PaymentDestinationId equals destination.Id
                            where instruction.InvoiceId == invoiceId
                            select destination).SingleOrDefaultAsync(ct);
        if (pinned is not null) return pinned;
        var active = await db.PaymentDestinations.SingleOrDefaultAsync(x => x.IsActive, ct);
        if (active is null) return null;
        db.PaymentInstructions.Add(new PaymentInstruction(invoiceId, active.Id, clock.GetUtcNow()));
        try { await db.SaveChangesAsync(ct); return active; }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return await (from instruction in db.PaymentInstructions.AsNoTracking()
                          join destination in db.PaymentDestinations.AsNoTracking()
                              on instruction.PaymentDestinationId equals destination.Id
                          where instruction.InvoiceId == invoiceId
                          select destination).SingleOrDefaultAsync(ct);
        }
    }

    public static string VietQrPayload(string bankBin, string account, decimal? amount, string content)
    {
        static string Field(string id, string value) => $"{id}{Encoding.UTF8.GetByteCount(value):00}{value}";
        var beneficiary = Field("00", bankBin) + Field("01", account);
        var merchant = Field("00", "A000000727") + Field("01", beneficiary) + Field("02", "QRIBFTTA");
        var additional = Field("08", content.Length > 25 ? content[..25] : content);
        var raw = Field("00", "01") + Field("01", "12") + Field("38", merchant) + Field("53", "704") +
                  (amount.HasValue ? Field("54", amount.Value.ToString("0", CultureInfo.InvariantCulture)) : "") + Field("58", "VN") +
                  (string.IsNullOrEmpty(content) ? "" : Field("62", additional)) + "6304";
        ushort crc = 0xFFFF;
        foreach (var value in Encoding.ASCII.GetBytes(raw))
        {
            crc ^= (ushort)(value << 8);
            for (var bit = 0; bit < 8; bit++) crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
        }
        return raw + crc.ToString("X4", CultureInfo.InvariantCulture);
    }
}
