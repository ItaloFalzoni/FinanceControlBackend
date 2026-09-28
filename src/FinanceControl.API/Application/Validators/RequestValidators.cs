namespace FinanceControl.API.Application.Validators;

/// <summary>
/// Hand-rolled input validation (no external library).
/// Every violation is collected so the endpoint can return them all in a single 400,
/// preserving the response contract: ErrorResponse("Validation failed", "msg1; msg2").
/// </summary>
public static class RequestValidators
{
    private const int DescriptionMaxLength = 500;

    public static IReadOnlyList<string> Validate(DepositRequest request)
    {
        var errors = new List<string>();

        AppendAmountError(request.Amount, "Deposit amount must be greater than zero.", errors);
        AppendDescriptionErrors(request.Description, errors);

        return errors;
    }

    public static IReadOnlyList<string> Validate(WithdrawRequest request)
    {
        var errors = new List<string>();

        AppendAmountError(request.Amount, "Withdrawal amount must be greater than zero.", errors);
        AppendDescriptionErrors(request.Description, errors);

        return errors;
    }

    private static void AppendAmountError(long amount, string message, List<string> errors)
    {
        if (amount <= 0)
            errors.Add(message);
    }

    private static void AppendDescriptionErrors(string? description, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(description))
            errors.Add("Description is required.");
        if (description?.Length > DescriptionMaxLength)
            errors.Add($"Description must not exceed {DescriptionMaxLength} characters.");
    }
}
