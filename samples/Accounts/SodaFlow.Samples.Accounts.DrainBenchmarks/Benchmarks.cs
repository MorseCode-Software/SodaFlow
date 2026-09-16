using BenchmarkDotNet.Attributes;
using SodaFlow.Samples.Accounts.ViewModels;

namespace SodaFlow.Samples.Accounts.DrainBenchmarks;

/// <summary>The view models under test, by the name the benchmarks and the footprint mode use.</summary>
public static class ViewModels
{
    /// <summary>b1398a3's AccountsViewModel: drainable accounts are a filtered ReactiveCollection.</summary>
    public const string Original = "Original";

    /// <summary>b1398a3's AccountsViewModelOptimizedDrain: an ImmutableHashSet folded over item changes.</summary>
    public const string OptimizedDrain = "OptimizedDrain";

    /// <summary>Review variant: the same hash set, updated in one pass with a builder only when needed.</summary>
    public const string TunedSet = "TunedSet";

    /// <summary>Review variant: a count folded over item changes, and the keys found at drain time.</summary>
    public const string CountAndScan = "CountAndScan";

    public static IAccountsViewModel Create(string name) =>
        name switch
        {
            Original => AccountsViewModel.Create(),
            OptimizedDrain => AccountsViewModelOptimizedDrain.Create(),
            TunedSet => AccountsViewModelTunedSet.Create(),
            CountAndScan => AccountsViewModelCountAndScan.Create(),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown view model."),
        };
}

/// <summary>
///     One click of a row's Pay button, into the first row of the first page, before any drain and
///     after one.
/// </summary>
[MemoryDiagnoser]
public class PayBenchmarks
{
    private IAccountRowViewModel row = null!;
    private IAccountsViewModel viewModel = null!;

    [Params(ViewModels.Original, ViewModels.OptimizedDrain, ViewModels.TunedSet, ViewModels.CountAndScan)]
    public string ViewModel { get; set; } = ViewModels.Original;

    [Params(false, true)]
    public bool AfterDrain { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        this.viewModel = ViewModels.Create(this.ViewModel);

        if (this.AfterDrain)
        {
            this.viewModel.DrainFrozenAccounts.Execute(null);

            if (this.viewModel.DrainFrozenAccounts.CanExecute(null))
            {
                throw new InvalidOperationException("The drain did not empty the frozen accounts.");
            }
        }

        // The first row in the default order, which is arrival order - no column is sorted until a
        // header is clicked. That order does not read balances, so paying into this row never
        // moves it: it stays on the page and its command stays live for the whole run.
        this.row = this.viewModel.Rows.Value[0];

        if (!this.row.Deposit.CanExecute(null))
        {
            throw new InvalidOperationException("The first row cannot be paid into.");
        }
    }

    [Benchmark]
    public void Pay() => this.row.Deposit.Execute(null);

    [GlobalCleanup]
    public void Cleanup() => this.viewModel.Dispose();
}

/// <summary>One click of Drain frozen accounts, on a view model nobody has drained yet.</summary>
/// <remarks>
///     A drain empties every frozen account, so it cannot be repeated on the same view model: each
///     iteration builds a fresh one first, outside the measurement, and checks afterwards that the
///     drain really happened.
/// </remarks>
[MemoryDiagnoser]
[InvocationCount(1)]
[WarmupCount(3)]
[IterationCount(20)]
public class DrainBenchmarks
{
    private IAccountsViewModel? viewModel;

    [Params(ViewModels.Original, ViewModels.OptimizedDrain, ViewModels.TunedSet, ViewModels.CountAndScan)]
    public string ViewModel { get; set; } = ViewModels.Original;

    [IterationSetup]
    public void Setup() => this.viewModel = ViewModels.Create(this.ViewModel);

    [Benchmark]
    public void Drain() => this.viewModel!.DrainFrozenAccounts.Execute(null);

    [IterationCleanup]
    public void Cleanup()
    {
        if (this.viewModel!.DrainFrozenAccounts.CanExecute(null))
        {
            throw new InvalidOperationException("The drain did not empty the frozen accounts.");
        }

        this.viewModel.Dispose();
        this.viewModel = null;
    }
}

/// <summary>
///     One click of Show frozen accounts, which changes the filter's predicate rather than any
///     account - measured from the click until the row list has changed.
/// </summary>
/// <remarks>
///     <para>
///         A predicate change is applied incrementally: the filter re-tests every account and
///         reports the ones that entered or left, rather than rebuilding and resetting the stages
///         below it. This is the cost of that for the whole chain down to the rows on screen.
///     </para>
///     <para>
///         Each iteration builds a fresh view model, outside the measurement, so the toggle always
///         goes the same way, and checks afterwards that it really switched. The row list reaches
///         its listener through the binding scheduler rather than inside the click, so the
///         measurement waits for it; the listener is attached in setup so that subscribing is not
///         part of what is measured.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[InvocationCount(1)]
[WarmupCount(3)]
[IterationCount(20)]
public class ToggleFrozenBenchmarks
{
    private IAccountsViewModel? viewModel;
    private RowsChanged? rowsChanged;
    private bool initialShowFrozen;

    [Params(ViewModels.OptimizedDrain, ViewModels.TunedSet)]
    public string ViewModel { get; set; } = ViewModels.TunedSet;

    [IterationSetup]
    public void Setup()
    {
        this.viewModel = ViewModels.Create(this.ViewModel);
        this.initialShowFrozen = this.viewModel.ShowFrozen.Value;
        this.rowsChanged = new RowsChanged(this.viewModel);
    }

    [Benchmark]
    public void ToggleFrozen()
    {
        this.viewModel!.ShowFrozen.Value = !this.initialShowFrozen;
        this.rowsChanged!.Wait();
    }

    [IterationCleanup]
    public void Cleanup()
    {
        this.rowsChanged!.Dispose();
        this.rowsChanged = null;

        if (this.viewModel!.ShowFrozen.Value == this.initialShowFrozen)
        {
            throw new InvalidOperationException("The frozen toggle was not switched.");
        }

        this.viewModel.Dispose();
        this.viewModel = null;
    }
}

/// <summary>
///     One click of the balance header on a list already sorted by balance, which reverses the
///     sort - measured from the click until the row list has changed.
/// </summary>
/// <remarks>
///     <para>
///         A sort stage handed its own order run the other way turns the list it holds around rather
///         than filing every account again, so this measures that path. Setup sorts by balance first,
///         outside the measurement, so that the click measured is the reversal and not the first
///         sort.
///     </para>
///     <para>
///         Each iteration builds a fresh view model, so the reversal always goes the same way. The
///         listener is attached in setup, for the reason given on
///         <see cref="ToggleFrozenBenchmarks" />.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[InvocationCount(1)]
[WarmupCount(3)]
[IterationCount(20)]
public class ToggleBalanceSortBenchmarks
{
    private IAccountsViewModel? viewModel;
    private RowsChanged? rowsChanged;

    [Params(ViewModels.OptimizedDrain, ViewModels.TunedSet)]
    public string ViewModel { get; set; } = ViewModels.TunedSet;

    [IterationSetup]
    public void Setup()
    {
        this.viewModel = ViewModels.Create(this.ViewModel);
        this.rowsChanged = new RowsChanged(this.viewModel);

        this.viewModel.SortByBalance.Execute(null);
        this.rowsChanged.Wait();

        // Anything the first sort signalled after the wait belongs to it, not to the reversal.
        this.rowsChanged.Clear();
    }

    [Benchmark]
    public void ToggleSort()
    {
        this.viewModel!.SortByBalance.Execute(null);
        this.rowsChanged!.Wait();
    }

    [IterationCleanup]
    public void Cleanup()
    {
        this.rowsChanged!.Dispose();
        this.rowsChanged = null;

        this.viewModel!.Dispose();
        this.viewModel = null;
    }
}

/// <summary>
///     Signals each time a view model's row list changes, for a benchmark that has to wait for a
///     click to reach the screen.
/// </summary>
/// <remarks>
///     One listener for the life of an iteration, so that neither subscribing nor unsubscribing is
///     measured and nothing is left listening once the iteration ends. A wait gives up after a
///     generous timeout rather than hanging the run when a click changes nothing.
/// </remarks>
internal sealed class RowsChanged : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly AutoResetEvent changed = new(initialState: false);
    private readonly IListener listener;

    internal RowsChanged(IAccountsViewModel viewModel) =>
        this.listener = viewModel.Rows.Cell.Updates().Listen(_ => this.changed.Set());

    /// <summary>Waits for the next change, then clears the signal for the one after.</summary>
    internal void Wait()
    {
        if (!this.changed.WaitOne(Timeout))
        {
            throw new InvalidOperationException("The row list did not change.");
        }
    }

    /// <summary>Forgets a change already signalled, so the next wait is for one still to come.</summary>
    internal void Clear() => this.changed.Reset();

    public void Dispose()
    {
        this.listener.Unlisten();
        this.changed.Dispose();
    }
}
