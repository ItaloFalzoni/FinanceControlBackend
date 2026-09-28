namespace FinanceControl.API.Domain.Exceptions;

public sealed class InvalidAmountException : DomainException
{
    public InvalidAmountException(long amountCents)
        : base($"The amount '{amountCents}' (cents) is invalid. It must be greater than zero.") { }
}
