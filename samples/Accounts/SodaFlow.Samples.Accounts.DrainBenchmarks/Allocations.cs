using SodaFlow.Samples.Accounts.ViewModels;

namespace SodaFlow.Samples.Accounts.DrainBenchmarks;

/// <summary>
///     Allocations counted on the calling thread only, which is where a Pay and a Drain run.
/// </summary>
/// <remarks>
///     BenchmarkDotNet's memory diagnoser counts every thread, so tiered JIT compilation on its
///     background thread lands in the numbers and makes differences of a few kilobytes unreadable.
///     This counts only what the operation itself allocates, after enough warm-up Pays for the
///     code to have tiered up.
/// </remarks>
public static class Allocations
{
    private const int Warmup = 5_000;
    private const int Measured = 20_000;

    public static void Run(string name)
    {
        IAccountsViewModel viewModel = ViewModels.Create(name);
        double beforeDrain = PerPay(viewModel.Rows.Value[0]);

        viewModel.DrainFrozenAccounts.Execute(null);
        double afterDrain = PerPay(viewModel.Rows.Value[0]);

        IAccountsViewModel fresh = ViewModels.Create(name);
        long start = GC.GetAllocatedBytesForCurrentThread();
        fresh.DrainFrozenAccounts.Execute(null);
        long drain = GC.GetAllocatedBytesForCurrentThread() - start;

        Console.WriteLine(
            $"{name,-15} pay before drain {beforeDrain,8:F0} B   pay after drain {afterDrain,8:F0} B   "
            + $"drain {drain / 1024.0 / 1024.0,6:F2} MB");

        viewModel.Dispose();
        fresh.Dispose();
    }

    private static double PerPay(IAccountRowViewModel row)
    {
        for (int i = 0; i < Warmup; i++)
        {
            row.Deposit.Execute(null);
        }

        long start = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < Measured; i++)
        {
            row.Deposit.Execute(null);
        }

        return (GC.GetAllocatedBytesForCurrentThread() - start) / (double)Measured;
    }
}
