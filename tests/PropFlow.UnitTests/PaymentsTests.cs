using System.Reflection;
using PropFlow.Modules.Payments.Domain.Payments;

namespace PropFlow.UnitTests;

public class PaymentsTests
{
    private readonly DateTimeOffset _now = new(2026, 9, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Payment_Constructor_CreatesPendingPaymentAndNormalizesReferences()
    {
        var payment = NewPayment(amount: 1500000m, referenceNumber: " txn-001 ");

        Assert.NotEqual(Guid.Empty, payment.Id);
        Assert.Equal("PAY-001", payment.PaymentNumber);
        Assert.Equal("BANK_TRANSFER", payment.PaymentMethodCode);
        Assert.Equal("TXN-001", payment.ReferenceNumber);
        Assert.Equal(PaymentStatus.PENDING, payment.Status);
        Assert.Equal(1500000m, payment.Amount);
        Assert.Equal(_now, payment.CreatedAt);
        Assert.Null(payment.ConfirmedAt);
        Assert.Null(payment.RejectedAt);
    }

    [Fact]
    public void Payment_Constructor_ValidatesRequiredValuesAndPositiveAmount()
    {
        Assert.Throws<ArgumentException>(() => new Payment("", Guid.NewGuid(), 1m, "BANK", _now, _now));
        Assert.Throws<ArgumentException>(() => new Payment("PAY-1", Guid.Empty, 1m, "BANK", _now, _now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Payment("PAY-1", Guid.NewGuid(), 0m, "BANK", _now, _now));
        Assert.Throws<ArgumentException>(() => new Payment("PAY-1", Guid.NewGuid(), 1m, "", _now, _now));
        Assert.Throws<ArgumentException>(() => new Payment("PAY-1", Guid.NewGuid(), 1m, "BANK", _now, _now, Guid.Empty));
    }

    [Fact]
    public void Payment_Confirm_CompletesPendingPaymentWhenWithinRemainingBalance()
    {
        var payment = NewPayment(amount: 1000000m);
        var accountant = Guid.NewGuid();
        var confirmedAt = _now.AddMinutes(10);

        payment.Confirm(accountant, remainingPayableAmount: 1000000m, confirmedAt);

        Assert.Equal(PaymentStatus.CONFIRMED, payment.Status);
        Assert.Equal(accountant, payment.ConfirmedBy);
        Assert.Equal(confirmedAt, payment.ConfirmedAt);
        Assert.Equal(confirmedAt, payment.UpdatedAt);
        Assert.Null(payment.RejectedBy);
        Assert.Null(payment.RejectionReason);
    }

    [Fact]
    public void Payment_Confirm_RejectsOverpaymentAndInvalidActor()
    {
        var payment = NewPayment(amount: 1000000m);

        Assert.Throws<ArgumentException>(() => payment.Confirm(Guid.Empty, 1000000m, _now));
        Assert.Throws<ArgumentOutOfRangeException>(() => payment.Confirm(Guid.NewGuid(), -1m, _now));
        Assert.Throws<InvalidOperationException>(() => payment.Confirm(Guid.NewGuid(), 999999m, _now));
    }

    [Fact]
    public void Payment_Reject_CompletesPendingPaymentWithReason()
    {
        var payment = NewPayment();
        var accountant = Guid.NewGuid();
        var rejectedAt = _now.AddMinutes(5);

        payment.Reject(accountant, "Invalid transaction reference", rejectedAt);

        Assert.Equal(PaymentStatus.REJECTED, payment.Status);
        Assert.Equal(accountant, payment.RejectedBy);
        Assert.Equal(rejectedAt, payment.RejectedAt);
        Assert.Equal("Invalid transaction reference", payment.RejectionReason);
        Assert.Null(payment.ConfirmedBy);
        Assert.Null(payment.ConfirmedAt);
    }

    [Fact]
    public void Payment_Reject_ValidatesActorAndReason()
    {
        var payment = NewPayment();

        Assert.Throws<ArgumentException>(() => payment.Reject(Guid.Empty, "Invalid", _now));
        Assert.Throws<ArgumentException>(() => payment.Reject(Guid.NewGuid(), "", _now));
    }

    [Fact]
    public void Payment_TerminalStates_CannotBeChangedAgain()
    {
        var confirmed = NewPayment();
        confirmed.Confirm(Guid.NewGuid(), confirmed.Amount, _now.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => confirmed.Confirm(Guid.NewGuid(), confirmed.Amount, _now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => confirmed.Reject(Guid.NewGuid(), "Invalid", _now.AddMinutes(2)));

        var rejected = NewPayment();
        rejected.Reject(Guid.NewGuid(), "Invalid", _now.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => rejected.Confirm(Guid.NewGuid(), rejected.Amount, _now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => rejected.Reject(Guid.NewGuid(), "Again", _now.AddMinutes(2)));
    }

    [Fact]
    public void PaymentStatusHistory_IsAppendOnlySnapshot()
    {
        var history = new PaymentStatusHistory(
            Guid.NewGuid(),
            PaymentStatus.CONFIRMED,
            Guid.NewGuid(),
            _now,
            PaymentStatus.PENDING,
            "Confirmed");

        Assert.NotEqual(Guid.Empty, history.Id);
        Assert.Equal(PaymentStatus.PENDING, history.FromStatus);
        Assert.Equal(PaymentStatus.CONFIRMED, history.ToStatus);

        var publicMethods = typeof(PaymentStatusHistory)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .ToList();
        Assert.Empty(publicMethods);
    }

    private Payment NewPayment(decimal amount = 1500000m, string? referenceNumber = null)
    {
        return new Payment(
            " pay-001 ",
            Guid.NewGuid(),
            amount,
            " bank_transfer ",
            _now,
            _now,
            submittedBy: Guid.NewGuid(),
            referenceNumber: referenceNumber,
            note: "Resident payment");
    }
}
