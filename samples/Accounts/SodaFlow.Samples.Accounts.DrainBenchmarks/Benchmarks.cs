using System;
using System.Collections.Generic;
using System.Globalization;
using BenchmarkDotNet.Attributes;
using JetBrains.Annotations;
using SodaFlow.Samples.Accounts.ViewModels;

namespace SodaFlow.Samples.Accounts.DrainBenchmarks;

/// <summary>The view models to test, with the name that the benchmarks and the footprint mode
/// use.</summary>
internal static class ViewModels
{
    /// <summary>AccountsViewModel: drainable accounts are a filtered ReactiveCollection.</summary>
    public const string Original = "Original";

    /// <summary>AccountsViewModelOptimizedDrain: drainable accounts are an ImmutableHashSet folded over item changes.</summary>
    public const string OptimizedDrain = "OptimizedDrain";

    public static IAccountsViewModel Create(string name) =>
        name switch
        {
            Original => AccountsViewModel.Create(),
            OptimizedDrain => AccountsViewModelOptimizedDrain.Create(),
            _ => throw new ArgumentOutOfRangeException(
                paramName: nameof(name),
                actualValue: name,
                message: "Unknown view model.")
        };
}

/// <summary>
///     One click of the Pay button of a row, into the first row of the first page, before a drain
///     and after a drain.
/// </summary>
/// <remarks>
///     Each iteration pays into the same view model, thus the code makes the test that the clicks
///     had a result one time, in the global cleanup. The balance of the row must increase by one
///     deposit for each Pay. This test finds the Pays that stopped during a run and the Pays that
///     never occurred. The code gets the deposit from a Pay in the setup, before the measurement,
///     and does not copy it from the view models.
/// </remarks>
[MemoryDiagnoser]
[UsedImplicitly]
public class PayBenchmarks
{
    private static readonly CultureInfo UsDollars = CultureInfo.GetCultureInfo("en-US");

    // ReSharper disable NullableWarningSuppressionIsUsed - Set in Setup
    private IAccountRowViewModel row = null!;

    private IAccountsViewModel viewModel = null!;
    // ReSharper restore NullableWarningSuppressionIsUsed

    private decimal startingBalance;
    private decimal deposit;
    private long pays;

    [Params(ViewModels.Original, ViewModels.OptimizedDrain)]
    [UsedImplicitly]
    public string ViewModel { get; set; } = ViewModels.Original;

    [Params(false, true)]
    [UsedImplicitly]
    public bool AfterDrain { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        this.viewModel = ViewModels.Create(this.ViewModel);

        if (this.AfterDrain)
        {
            this.viewModel.DrainFrozenAccounts.Execute(null);

            if (this.viewModel.DrainFrozenAccounts.IsEnabledCell.Sample())
            {
                throw new InvalidOperationException("The drain did not empty the frozen accounts.");
            }
        }

        // This is the first row in the default order, which is arrival order. No column sorts
        // until a user clicks a header. That order does not read balances, thus a deposit into
        // this row does not move it. The row stays on the page and its command stays active for
        // the full run.
        this.row = this.viewModel.Rows.Cell.Sample()[0];

        if (!this.row.Deposit.IsEnabledCell.Sample())
        {
            throw new InvalidOperationException("The first row cannot be paid into.");
        }

        decimal beforeSetupPay = BalanceOf(this.row);

        this.row.Deposit.Execute(null);

        this.startingBalance = BalanceOf(this.row);
        this.deposit = this.startingBalance - beforeSetupPay;
        this.pays = 0;

        if (this.deposit <= 0)
        {
            throw new InvalidOperationException("A Pay in setup did not raise the balance.");
        }
    }

    [Benchmark]
    public void Pay()
    {
        this.row.Deposit.Execute(null);
        this.pays++;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        decimal expected = this.startingBalance + this.pays * this.deposit;
        decimal actual = BalanceOf(this.row);

        this.viewModel.Dispose();

        if (actual != expected)
        {
            throw new InvalidOperationException(
                string.Format(
                    provider: UsDollars,
                    format: "{0:N0} Pays of {1:C} should have left the balance at {2:C}, but it is {3:C}.",
                    this.pays,
                    this.deposit,
                    expected,
                    actual));
        }
    }

    /// <summary>
    ///     The balance of the row. This code reads the cell and not the bindable, and converts
    ///     the value from the fixed US-dollar format of each balance on the screen.
    /// </summary>
    private static decimal BalanceOf(IAccountRowViewModel row) =>
        decimal.Parse(s: row.Balance.Cell.Sample(), style: NumberStyles.Currency, provider: UsDollars);
}

/// <summary>One click of Drain frozen accounts, on a view model that no code drained.</summary>
/// <remarks>
///     A drain empties each frozen account, thus a second drain on the same view model is not
///     possible. Each iteration builds a new view model first, before the measurement, and then
///     tests that the drain occurred.
/// </remarks>
[MemoryDiagnoser]
[InvocationCount(1)]
[WarmupCount(3)]
[IterationCount(20)]
[UsedImplicitly]
public class DrainBenchmarks
{
    // ReSharper disable once NullableWarningSuppressionIsUsed - Set in Setup
    private IAccountsViewModel viewModel = null!;

    [Params(ViewModels.Original, ViewModels.OptimizedDrain)]
    [UsedImplicitly]
    public string ViewModel { get; set; } = ViewModels.Original;

    [IterationSetup]
    public void Setup() => this.viewModel = ViewModels.Create(this.ViewModel);

    [Benchmark]
    public void Drain() => this.viewModel.DrainFrozenAccounts.Execute(null);

    [IterationCleanup]
    public void Cleanup()
    {
        if (this.viewModel.DrainFrozenAccounts.IsEnabledCell.Sample())
        {
            throw new InvalidOperationException("The drain did not empty the frozen accounts.");
        }

        this.viewModel.Dispose();
        // ReSharper disable once NullableWarningSuppressionIsUsed
        this.viewModel = null!;
    }
}

/// <summary>
///     One click of Show frozen accounts. It changes the predicate of the filter and does not
///     change an account.
/// </summary>
/// <remarks>
///     <para>
///         The filter tests each account again. It lists the accounts that entered or left only
///         while their count is sufficiently small. Show frozen accounts adds approximately 25,000
///         accounts to a filter with approximately 75,000 accounts. That count is above the limit
///         of the filter, which is one tenth of its content. Thus the filter builds again and
///         reports a reset, and the sort, the page, and the rows below it also build again. This
///         is the cost of that operation for the full chain to the rows on the screen.
///     </para>
///     <para>
///         This code waits for nothing, because nothing more occurs after the click returns. The
///         row list is a cell, thus it changes in the transaction that the click opens. This
///         process has no binding scheduler and no synchronization context, thus the bindables use
///         BindingScheduler.Immediate. That scheduler sends their values and their change
///         notifications as the transaction closes, which is in the click. A dispatcher in a true
///         UI does its work after that, and a benchmark here cannot see it.
///     </para>
///     <para>
///         Each iteration builds a new view model, before the measurement, thus the toggle
///         always moves in the same direction. The cleanup tests that the toggle changed and that
///         the row list changed. Thus a click with no result causes a failure of the run, and does
///         not show a fast time. The tests read the cells and not the values of the bindables. A
///         scheduler that uses a dispatcher sends those values only after the click, thus these
///         tests are correct with each scheduler.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[InvocationCount(1)]
[WarmupCount(3)]
[IterationCount(20)]
[UsedImplicitly]
public class ToggleFrozenBenchmarks
{
    // ReSharper disable NullableWarningSuppressionIsUsed - Set in Setup
    private IAccountsViewModel viewModel = null!;
    private IReadOnlyList<IAccountRowViewModel> rowsBefore = null!;

    // ReSharper restore NullableWarningSuppressionIsUsed

    private bool initialShowFrozen;

    [Params(ViewModels.Original, ViewModels.OptimizedDrain)]
    [UsedImplicitly]
    public string ViewModel { get; set; } = ViewModels.Original;

    [IterationSetup]
    public void Setup()
    {
        this.viewModel = ViewModels.Create(this.ViewModel);
        this.initialShowFrozen = this.viewModel.ShowFrozen.Cell.Sample();
        this.rowsBefore = this.viewModel.Rows.Cell.Sample();
    }

    [Benchmark]
    public void ToggleFrozen() => this.viewModel.ShowFrozen.Value = !this.initialShowFrozen;

    [IterationCleanup]
    public void Cleanup()
    {
        if (this.viewModel.ShowFrozen.Cell.Sample() == this.initialShowFrozen)
        {
            throw new InvalidOperationException("The frozen toggle was not switched.");
        }

        RowList.CheckChanged(
            before: this.rowsBefore,
            after: this.viewModel.Rows.Cell.Sample(),
            action: "The frozen toggle");

        this.viewModel.Dispose();
        // ReSharper disable NullableWarningSuppressionIsUsed
        this.viewModel = null!;
        this.rowsBefore = null!;
        // ReSharper restore NullableWarningSuppressionIsUsed
    }
}

/// <summary>
///     One click of the balance header on a list that is in balance order. It reverses the
///     sort.
/// </summary>
/// <remarks>
///     <para>
///         A sort stage that receives its own order in the opposite direction sorts again with
///         the balances that it holds, and does not read each account again. This benchmark
///         measures that path. The setup sorts on the balance first, before the measurement, thus
///         the click in the measurement is the reversal and not the first sort.
///     </para>
///     <para>
///         Each iteration builds a new view model, thus the reversal always moves in the same
///         direction. As in <see cref="ToggleFrozenBenchmarks" />, this code waits for nothing, and
///         the cleanup reads the cell of the row list to test that it changed.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[InvocationCount(1)]
[WarmupCount(3)]
[IterationCount(20)]
[UsedImplicitly]
public class ToggleBalanceSortBenchmarks
{
    // ReSharper disable NullableWarningSuppressionIsUsed
    private IAccountsViewModel viewModel = null!;
    private IReadOnlyList<IAccountRowViewModel> rowsBefore = null!;

    // ReSharper restore NullableWarningSuppressionIsUsed

    [Params(ViewModels.Original, ViewModels.OptimizedDrain)]
    [UsedImplicitly]
    public string ViewModel { get; set; } = ViewModels.Original;

    [IterationSetup]
    public void Setup()
    {
        this.viewModel = ViewModels.Create(this.ViewModel);

        IReadOnlyList<IAccountRowViewModel> unsorted = this.viewModel.Rows.Cell.Sample();

        this.viewModel.SortByBalance.Execute(null);

        RowList.CheckChanged(before: unsorted, after: this.viewModel.Rows.Cell.Sample(), action: "Sorting by balance");

        this.rowsBefore = this.viewModel.Rows.Cell.Sample();
    }

    [Benchmark]
    public void ToggleSort() => this.viewModel.SortByBalance.Execute(null);

    [IterationCleanup]
    public void Cleanup()
    {
        RowList.CheckChanged(
            before: this.rowsBefore,
            after: this.viewModel.Rows.Cell.Sample(),
            action: "Reversing the balance sort");

        this.viewModel.Dispose();
        // ReSharper disable NullableWarningSuppressionIsUsed
        this.viewModel = null!;
        this.rowsBefore = null!;
        // ReSharper restore NullableWarningSuppressionIsUsed
    }
}

/// <summary>The test that the toggle benchmarks do, after the measurement, that a click had a
/// result.</summary>
file static class RowList
{
    /// <summary>Throws an exception when the row list is the same list as before the click.</summary>
    /// <remarks>
    ///     This compares references. The projection makes a new list when the keys on the page
    ///     change, and keeps the same list instance when they do not change.
    /// </remarks>
    internal static void CheckChanged(
        IReadOnlyList<IAccountRowViewModel> before,
        IReadOnlyList<IAccountRowViewModel> after,
        string action)
    {
        if (ReferenceEquals(objA: before, objB: after))
        {
            throw new InvalidOperationException($"{action} did not change the row list.");
        }
    }
}
