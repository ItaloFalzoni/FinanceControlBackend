namespace FinanceControl.API.Domain.Exceptions;

public sealed class UnknownTransactionTypeException(int typeValue) : DomainException($"Unknown transaction type {typeValue}.")
{
    public int TypeValue { get; } = typeValue;
}
