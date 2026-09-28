namespace FinanceControl.API.Domain.Exceptions;

public sealed class InsufficientFundsException(long currentBalanceCents, long requestedAmountCents) : DomainException($"Insufficient funds. Current balance: {currentBalanceCents} cents, requested amount: {requestedAmountCents} cents.")
{

    /// <summary>Balance in cents at the time of the failure.</summary>
    public long CurrentBalance { get; } = currentBalanceCents;
    /// <summary>Requested debit in cents.</summary>
    public long RequestedAmount { get; } = requestedAmountCents;
}
