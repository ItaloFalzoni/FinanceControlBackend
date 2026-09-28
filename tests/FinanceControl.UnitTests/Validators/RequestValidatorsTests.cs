using FinanceControl.API.Application;
using FinanceControl.API.Application.Validators;

namespace FinanceControl.UnitTests.Validators;

public sealed class RequestValidatorsTests
{
    [Fact]
    public void ValidateDeposit_Valid_ReturnsNoErrors()
    {
        var errors = RequestValidators.Validate(new DepositRequest(10000, "Sales"));

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateWithdraw_Valid_ReturnsNoErrors()
    {
        var errors = RequestValidators.Validate(new WithdrawRequest(5000, "Supplier"));

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ValidateDeposit_InvalidAmount_ReturnsAmountError(long amount)
    {
        var errors = RequestValidators.Validate(new DepositRequest(amount, "Sales"));

        Assert.Contains(errors, e => e.Contains("greater than zero"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ValidateWithdraw_InvalidAmount_ReturnsAmountError(long amount)
    {
        var errors = RequestValidators.Validate(new WithdrawRequest(amount, "Supplier"));

        Assert.Contains(errors, e => e.Contains("greater than zero"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateDeposit_MissingDescription_ReturnsDescriptionError(string? description)
    {
        var errors = RequestValidators.Validate(new DepositRequest(1000, description!));

        Assert.Contains(errors, e => e.Contains("Description is required"));
    }

    [Fact]
    public void ValidateDeposit_LongDescription_ReturnsLengthError()
    {
        var errors = RequestValidators.Validate(new DepositRequest(1000, new string('x', 501)));

        Assert.Contains(errors, e => e.Contains("must not exceed 500"));
    }

    [Fact]
    public void ValidateDeposit_MultipleViolations_ReturnsAllErrors()
    {
        var errors = RequestValidators.Validate(new DepositRequest(0, ""));

        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void ValidateDeposit_NullRequest_ReturnsBodyRequired()
    {
        var errors = RequestValidators.Validate((DepositRequest?)null);

        Assert.Contains(errors, e => e.Contains("Request body is required"));
    }

    [Fact]
    public void ValidateWithdraw_NullRequest_ReturnsBodyRequired()
    {
        var errors = RequestValidators.Validate((WithdrawRequest?)null);

        Assert.Contains(errors, e => e.Contains("Request body is required"));
    }
}
