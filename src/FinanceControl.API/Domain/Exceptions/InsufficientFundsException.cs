namespace FinanceControl.API.Domain.Exceptions;

public sealed class InsufficientFundsException : DomainException
{
    public InsufficientFundsException(long currentBalanceCents, long requestedAmountCents)
        : base($"Insufficient funds. Current balance: {currentBalanceCents} cents, requested amount: {requestedAmountCents} cents.")
    {
        CurrentBalance = currentBalanceCents;
        RequestedAmount = requestedAmountCents;
    }

    /// <summary>Balance in cents at the time of the failure.</summary>
    public long CurrentBalance { get; }
    /// <summary>Requested debit in cents.</summary>
    public long RequestedAmount { get; }
}
