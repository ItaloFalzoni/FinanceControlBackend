using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace FinanceControl.API.Application.Telemetry;

/// <summary>
/// Single entry point for the ledger's traces and metrics. Exporters are wired
/// in Program.cs (console when OTEL_CONSOLE_EXPORTER=true, OTLP-ready otherwise).
/// </summary>
public static class AccountTelemetry
{
    public const string ActivitySourceName = "FinanceControl.Account";
    public const string MeterName = "FinanceControl.Account";

    private static readonly ActivitySource Activities = new(ActivitySourceName);
    private static readonly Meter Meters = new(MeterName);
    private static readonly Counter<long> Deposits = Meters.CreateCounter<long>("deposit_total");
    private static readonly Counter<long> Withdrawals = Meters.CreateCounter<long>("withdraw_total");
    private static readonly Counter<long> BalanceReads = Meters.CreateCounter<long>("balance_read_total");
    private static readonly Counter<long> HistoryReads = Meters.CreateCounter<long>("history_read_total");

    public static Activity? StartOperation(string name, long amountCents)
    {
        var activity = Activities.StartActivity(name);
        activity?.SetTag("operation.amount", amountCents);
        return activity;
    }

    public static Activity? StartOperation(string name)
    {
        return Activities.StartActivity(name);
    }

    public static void CountDeposit() => Deposits.Add(1);

    public static void CountWithdrawal() => Withdrawals.Add(1);

    public static void CountBalanceRead() => BalanceReads.Add(1);

    public static void CountHistoryRead() => HistoryReads.Add(1);
}
