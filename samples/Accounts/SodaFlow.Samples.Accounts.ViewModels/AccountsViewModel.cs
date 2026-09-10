using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Collections;
using SodaFlow.Functional;

namespace SodaFlow.Samples.Accounts.ViewModels;

/// <summary>One row, holding cells that follow one account through the view showing it.</summary>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class AccountRowViewModel : IAccountRowViewModel, IDisposable
{
    private readonly IReadOnlyList<IDisposable> disposables;

    internal AccountRowViewModel(
        IOneWayBindableValue<string> number,
        IOneWayBindableValue<string> holder,
        IOneWayBindableValue<string> balance)
    {
        this.Number = number;
        this.Holder = holder;
        this.Balance = balance;
        this.disposables = new IDisposable[] { number, holder, balance };
    }

    /// <inheritdoc />
    public IOneWayBindableValue<string> Number { get; }

    /// <inheritdoc />
    public IOneWayBindableValue<string> Holder { get; }

    /// <inheritdoc />
    public IOneWayBindableValue<string> Balance { get; }

    public void Dispose()
    {
        foreach (IDisposable disposable in this.disposables)
        {
            disposable.Dispose();
        }
    }
}

/// <summary>
///     A paged, filtered, sorted list over a collection of accounts, with a total over all of them.
/// </summary>
/// <remarks>
///     <para>
///         What to watch on screen. Depositing into the top account moves that row's balance and
///         nothing else - the other rows do not flicker, and the list itself does not rebuild, even
///         though the sort could have moved the account. That is the collection doing its job: an
///         edit reaches the rows bound to it, not the rows next to them.
///     </para>
///     <para>
///         Turning the page is a criteria change, which for a slice costs nothing much - it moves a
///         window over an ordering that did not move. Toggling frozen accounts is also a criteria
///         change, and that one does rebuild the filter, which is the expensive kind. Both are one
///         line here and the difference between them is invisible in the code, which is why the
///         reference page spells the costs out.
///     </para>
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
public sealed class AccountsViewModel : IAccountsViewModel
{
    /// <summary>How many rows a page shows.</summary>
    private const int PageSize = 6;

    /// <summary>What the deposit button pays in, in pence.</summary>
    private const long DepositAmount = 100_00L;

    private readonly IReadOnlyList<IDisposable> disposables;

    private AccountsViewModel(
        IOneWayBindableValue<IReadOnlyList<IAccountRowViewModel>> rows,
        IOneWayBindableValue<string> total,
        IOneWayBindableValue<string> page,
        IOneWayBindableValue<string> filterDescription,
        IBindableAction nextPage,
        IBindableAction previousPage,
        IBindableAction depositIntoTopOfPage,
        IBindableAction toggleFrozen,
        MappedItems<IAccountRowViewModel> projectedRows)
    {
        this.Rows = rows;
        this.Total = total;
        this.Page = page;
        this.FilterDescription = filterDescription;
        this.NextPage = nextPage;
        this.PreviousPage = previousPage;
        this.DepositIntoTopOfPage = depositIntoTopOfPage;
        this.ToggleFrozen = toggleFrozen;

        // The projection is in here too. Disposing it releases every row it still holds, which is
        // the ones that never left the view and so never triggered the eviction callback.
        this.disposables = new IDisposable[]
        {
            rows, total, page, filterDescription, nextPage, previousPage, depositIntoTopOfPage,
            toggleFrozen, projectedRows,
        };
    }

    /// <inheritdoc />
    public IOneWayBindableValue<IReadOnlyList<IAccountRowViewModel>> Rows { get; }

    /// <inheritdoc />
    public IOneWayBindableValue<string> Total { get; }

    /// <inheritdoc />
    public IOneWayBindableValue<string> Page { get; }

    /// <inheritdoc />
    public IOneWayBindableValue<string> FilterDescription { get; }

    /// <inheritdoc />
    public IBindableAction NextPage { get; }

    /// <inheritdoc />
    public IBindableAction PreviousPage { get; }

    /// <inheritdoc />
    public IBindableAction DepositIntoTopOfPage { get; }

    /// <inheritdoc />
    public IBindableAction ToggleFrozen { get; }

    /// <inheritdoc />
    /// <remarks>
    ///     The rows are not listed here one by one. They belong to the projection, which is in the
    ///     list and releases all of them when it goes - whether a row left the view early and was
    ///     evicted then, or lasted until now.
    /// </remarks>
    public void Dispose()
    {
        foreach (IDisposable disposable in this.disposables)
        {
            disposable.Dispose();
        }
    }

    public static IAccountsViewModel Create() =>
        Transaction.Run(static () =>
        {
            StreamSink<Unit> nextPage = Stream.CreateSink<Unit>();
            StreamSink<Unit> previousPage = Stream.CreateSink<Unit>();
            StreamSink<Unit> deposit = Stream.CreateSink<Unit>();
            StreamSink<Unit> toggleFrozen = Stream.CreateSink<Unit>();

            Cell<bool> showFrozen =
                toggleFrozen.Accum(initialState: false, f: static (_, showing) => !showing);

            // The deposit pays into whichever account is at the top of the page, so the edit
            // depends on the view, and the view depends on the edits. That is a real cycle and the
            // loop is how it is closed - declared here, tied off once the view exists.
            StreamLoop<CollectionEdit<int, AccountIdentity, AccountState>> deposits =
                Stream.CreateLoop<CollectionEdit<int, AccountIdentity, AccountState>>();

            // No key selector: AccountIdentity implements IIdentity<int>.
            ReactiveCollection<int, AccountIdentity, AccountState> accounts =
                ReactiveCollection.Create(AccountSeed.Items, deposits);

            ReactiveCollection<int, AccountIdentity, AccountState> filtered = accounts
                .Filter(showFrozen, static (showing, _, state) => showing || !state.IsFrozen)
                .SortByDescending(static (_, state) => state.Balance);

            // Paging moves an offset. Toggling the filter sends it back to the first page, because
            // an offset that outlived the rows it pointed at would show an empty list.
            Cell<int> offset = new[]
                {
                    nextPage.MapTo(static (int at) => at + PageSize),
                    previousPage.MapTo(static (int at) => at - PageSize),
                    toggleFrozen.MapTo(static (int _) => 0),
                }
                .OrElse()
                .Accum(initialState: 0, f: static (move, at) => Math.Max(0, move(at)));

            ReactiveCollection<int, AccountIdentity, AccountState> page =
                filtered.Slice(offset, Cell.Constant(PageSize));

            deposits.Loop(
                deposit
                    .Snapshot(page.KeysCell, static (_, keys) => keys)
                    .Filter(static keys => keys.Count > 0)
                    .Map(static keys => CollectionEdit<int, AccountIdentity, AccountState>.Update(
                        keys[0],
                        static state => state.WithBalance(state.Balance + DepositAmount))));

            // One row object per account, in the page's order. Map keeps them, so a deposit that
            // moves one balance leaves this list alone: the row follows its own account and the
            // list only moves when the page's membership or order does.
            //
            // The rows own bindables, so eviction disposes them. Nothing here has to know when
            // that happens - which is the point of the callback being where the projection is.
            MappedItems<IAccountRowViewModel> rows = page.Map<int, AccountIdentity, AccountState, IAccountRowViewModel>(
                key => new AccountRowViewModel(
                    // Asked of the page rather than of the collection, so a row answers for the
                    // view it belongs to: an account the filter excludes has no holder here.
                    page.IdentityCell(key)
                        .Map(static identity =>
                            identity.Match(
                                static value => value.Number.ToString(CultureInfo.CurrentCulture),
                                static () => string.Empty))
                        .ToOneWay(),
                    page.IdentityCell(key)
                        .Map(static identity =>
                            identity.Match(static value => value.Holder, static () => string.Empty))
                        .ToOneWay(),
                    page.StateCell(key)
                        .Map(static state => state.Match(Money, static () => string.Empty))
                        .ToOneWay()),
                onEvicted: static row => ((AccountRowViewModel)row).Dispose());

            // The total is folded from what changed rather than recomputed from the store. The
            // change carries both sides of it, so a delta needs nothing kept alongside.
            long initialTotal = AccountSeed.Items.Sum(static item => item.State.Balance);

            Cell<long> total = accounts.ItemChangesStream
                .Map(static change => DeltaOf(change))
                .Accum(initialState: initialTotal, f: static (delta, running) => running + delta);

            Cell<int> pageCount = filtered.KeysCell.Map(static keys =>
                Math.Max(1, (keys.Count + PageSize - 1) / PageSize));

            return new AccountsViewModel(
                rows: rows.Items.ToOneWay(),
                total: total.Map(static pence => "Total across all accounts: " + Money(pence))
                    .ToOneWay(),
                page: offset.Lift(
                        pageCount,
                        static (at, count) => string.Format(
                            CultureInfo.CurrentCulture,
                            "Page {0} of {1}",
                            (at / PageSize) + 1,
                            count))
                    .ToOneWay(),
                filterDescription: showFrozen.Map(static showing =>
                        showing ? "Showing all accounts" : "Showing active accounts only")
                    .ToOneWay(),
                nextPage: nextPage.ToBindableAction(
                    offset.Lift(filtered.KeysCell, static (at, keys) => at + PageSize < keys.Count)),
                previousPage: previousPage.ToBindableAction(offset.Map(static at => at > 0)),
                depositIntoTopOfPage: deposit.ToBindableAction(
                    page.KeysCell.Map(static keys => keys.Count > 0)),
                toggleFrozen: toggleFrozen.ToBindableAction(),
                projectedRows: rows);
        });

    /// <summary>How much the total moved, from the keys this change touched.</summary>
    /// <remarks>
    ///     An added key has no state before, so it contributes only its new balance; a removed one
    ///     is absent from the new states, so it contributes only the negation of its old. Neither
    ///     needs a special case beyond looking.
    /// </remarks>
    private static long DeltaOf(ItemChange<int, AccountIdentity, AccountState> change)
    {
        long delta = 0;

        foreach (KeyValuePair<int, AccountState> pair in change.NewStates)
        {
            if (change.Before.States.TryGetState(pair.Key, out AccountState was))
            {
                delta -= was.Balance;
            }

            delta += pair.Value.Balance;
        }

        foreach (int key in change.Removed)
        {
            if (change.Before.States.TryGetState(key, out AccountState was))
            {
                delta -= was.Balance;
            }
        }

        return delta;
    }

    /// <summary>Pence as a currency string.</summary>
    private static string Money(AccountState state) => Money(state.Balance);

    private static string Money(long pence) =>
        (pence / 100m).ToString("C", CultureInfo.CurrentCulture);
}
