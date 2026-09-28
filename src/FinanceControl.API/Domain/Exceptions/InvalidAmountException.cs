namespace FinanceControl.API.Domain.Exceptions;

public sealed class InvalidAmountException(long amountCents) : DomainException($"The amount '{amountCents}' (cents) is invalid. It must be greater than zero.")
{
}
