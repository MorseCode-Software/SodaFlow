using System.ComponentModel;
using System.Diagnostics;
using SodaFlow.Samples.Accounts.ViewModels;

namespace SodaFlow.Samples.Accounts.DrainBenchmarks;

/// <summary>
///     Whether a Pay costs more the more Pays came before it: allocation on this thread, time and
///     retained memory per block of Pays, and how often the Rows list reported a change.
/// </summary>
/// <remarks>
///     Rows should not change at all here - paying into the first row in arrival order moves no
///     row - so any count other than zero means the row list is re-projected by a Pay, which would
///     also rebuild the merge of the rows' deposit streams each time.
/// </remarks>
public static class Growth
{
    private const int Blocks = 10;
    private const int PaysPerBlock = 5_000;

    public static void Run(string name)
    {
        IAccountsViewModel viewModel = ViewModels.Create(name);
        int rowsChanged = 0;
        ((INotifyPropertyChanged)viewModel.Rows).PropertyChanged += (_, _) => rowsChanged++;

        IAccountRowViewModel row = viewModel.Rows.Value[0];
        long baseline = Settled();

        Console.WriteLine($"{name}");

        for (int block = 1; block <= Blocks; block++)
        {
            int changesBefore = rowsChanged;
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            Stopwatch stopwatch = Stopwatch.StartNew();

            for (int i = 0; i < PaysPerBlock; i++)
            {
                row.Deposit.Execute(null);
            }

            stopwatch.Stop();
            double bytesPerPay = (GC.GetAllocatedBytesForCurrentThread() - allocatedBefore) / (double)PaysPerBlock;
            double microsecondsPerPay = stopwatch.Elapsed.TotalMilliseconds * 1000.0 / PaysPerBlock;
            double retained = (Settled() - baseline) / 1024.0 / 1024.0;

            Console.WriteLine(
                $"  after {block * PaysPerBlock,6:N0} pays: {bytesPerPay,8:F0} B/pay  {microsecondsPerPay,7:F1} us/pay  "
                + $"retained +{retained,6:F2} MB  Rows changed {rowsChanged - changesBefore}");
        }

        GC.KeepAlive(viewModel);
        viewModel.Dispose();
    }

    private static long Settled()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        return GC.GetTotalMemory(forceFullCollection: true);
    }
}
