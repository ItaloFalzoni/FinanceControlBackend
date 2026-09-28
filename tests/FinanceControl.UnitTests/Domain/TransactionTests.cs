using FinanceControl.API.Domain.Entities;
using FinanceControl.API.Domain.Enums;
using FinanceControl.API.Domain.Exceptions;

namespace FinanceControl.UnitTests.Domain;

public sealed class TransactionTests
{
    [Fact]
    public void CreateCredit_Valid_SetsProperties()
    {
        var before = DateTimeOffset.UtcNow;

        var transaction = Transaction.CreateCredit(10000, "  Sales  ");

        Assert.Equal(10000L, transaction.Amount);
        Assert.Equal(TransactionType.Credit, transaction.Type);
        Assert.Equal(10000L, transaction.SignedAmount);
        Assert.Equal("Sales", transaction.Description);
        Assert.NotEqual(Guid.Empty, transaction.Id);
        Assert.InRange(transaction.CreatedAt, before, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void CreateDebit_Valid_SignedAmountIsNegative()
    {
        var transaction = Transaction.CreateDebit(4000, "Supplier");

        Assert.Equal(4000L, transaction.Amount);
        Assert.Equal(TransactionType.Debit, transaction.Type);
        Assert.Equal(-4000L, transaction.SignedAmount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CreateCredit_InvalidAmount_ThrowsInvalidAmountException(long amount)
    {
        Assert.Throws<InvalidAmountException>(() => Transaction.CreateCredit(amount, "Sales"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CreateDebit_InvalidAmount_ThrowsInvalidAmountException(long amount)
    {
        Assert.Throws<InvalidAmountException>(() => Transaction.CreateDebit(amount, "Supplier"));
    }

    [Fact]
    public void CreateCredit_NullDescription_DefaultsToEmpty()
    {
        var transaction = Transaction.CreateCredit(1000, null!);

        Assert.Equal(string.Empty, transaction.Description);
    }

    [Fact]
    public void Reconstitute_UnknownType_ThrowsDomainException()
    {
        Assert.Throws<UnknownTransactionTypeException>(() => Transaction.Reconstitute(
            Guid.NewGuid(), 1000, TransactionType.Unknown, "Sales", DateTimeOffset.UtcNow));
    }
}
