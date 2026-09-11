using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Collections;
using SodaFlow.Functional;
using AccountOrder = SodaFlow.Collections.KeyOrder<
    int,
    SodaFlow.Samples.Accounts.ViewModels.AccountIdentity,
    SodaFlow.Samples.Accounts.ViewModels.AccountState>;

namespace SodaFlow.Samples.Accounts.ViewModels;

/// <summary>A column of the list, which is a thing the list can be sorted by.</summary>
internal enum AccountColumn
{
    /// <summary>The account number, which is part of the identity.</summary>
    Number,

    /// <summary>Whose account it is, also part of the identity.</summary>
    Holder,

    /// <summary>The balance, which is the part that moves.</summary>
    Balance,
}

/// <summary>Which column the list is sorted by, and which way.</summary>
/// <remarks>
///     <para>
///         Two of the three sort on the identity half of an account, which no edit can touch, so
///         under either of those a deposit moves a balance and can never move a row. Sorting by
///         balance is the one that re-files, and switching between them with the deposit button is
///         how the difference is seen.
///     </para>
///     <para>
///         The order is derived from this rather than stored beside it, so there is one piece of
///         state on screen - a column and a direction - and the sort follows from it.
///     </para>
/// </remarks>
internal sealed class SortSelection
{
    internal SortSelection(AccountColumn column, bool descending)
    {
        this.Column = column;
        this.Descending = descending;
    }

    private AccountColumn Column { get; }

    private bool Descending { get; }

    /// <summary>This selection as an order the sort stage can hold.</summary>
    /// <remarks>
    ///     Three orders projecting sort values of three types - an <c>int</c>, a <c>string</c> and
    ///     a <c>long</c> - and all three are the same type here, which is what lets one cell hold
    ///     whichever is in force. The comparers are spelled out because the direction is decided at
    ///     run time rather than written into the call, and because holders want comparing the way
    ///     names are read rather than the way their code units happen to fall.
    /// </remarks>
    internal AccountOrder Order =>
        this.Column switch
        {
            AccountColumn.Number => AccountOrder.ByIdentity(
                static identity => identity.Number,
                Comparer<int>.Default,
                Comparer<int>.Default,
                this.Descending),
            AccountColumn.Holder => AccountOrder.ByIdentity(
                static identity => identity.Holder,
                StringComparer.CurrentCultureIgnoreCase,
                Comparer<int>.Default,
                this.Descending),
            _ => AccountOrder.By(
                static (_, state) => state.Balance,
                Comparer<long>.Default,
                Comparer<int>.Default,
                this.Descending),
        };

    /// <summary>What clicking a header does: the same column reverses, another one selects.</summary>
    /// <remarks>
    ///     A newly chosen column starts ascending, except the balance, which starts at the largest
    ///     because that is the way a list of balances is usually wanted.
    /// </remarks>
    internal SortSelection Clicked(AccountColumn column) =>
        column == this.Column
            ? new SortSelection(column, !this.Descending)
            : new SortSelection(column, column == AccountColumn.Balance);

    /// <summary>A header's caption, marked if it is the column in force.</summary>
    internal string Caption(AccountColumn column, string name) =>
        column == this.Column ? name + (this.Descending ? " \u25bc" : " \u25b2") : name;
}

/// <summary>One row, holding cells that follow one account through the view showing it.</summary>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class AccountRowViewModel : IAccountRowViewModel
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

    /// <inheritdoc />
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

    /// <summary>What the deposit button pays in, in cents.</summary>
    private const long DepositAmount = 100_00L;

    /// <summary>How every amount on screen is written.</summary>
    private static readonly NumberFormatInfo UsDollars = CultureInfo.GetCultureInfo("en-US").NumberFormat;

    private readonly IReadOnlyList<IDisposable> disposables;

    private AccountsViewModel(
        IOneWayBindableValue<IReadOnlyList<IAccountRowViewModel>> rows,
        IOneWayBindableValue<string> total,
        IOneWayBindableValue<string> page,
        IOneWayBindableValue<string> filterDescription,
        IOneWayBindableValue<string> numberHeader,
        IOneWayBindableValue<string> holderHeader,
        IOneWayBindableValue<string> balanceHeader,
        IBindableAction nextPage,
        IBindableAction previousPage,
        IBindableAction depositIntoTopOfPage,
        IBindableAction toggleFrozen,
        IBindableAction sortByNumber,
        IBindableAction sortByHolder,
        IBindableAction sortByBalance,
        MappedItems<IAccountRowViewModel> projectedRows)
    {
        this.Rows = rows;
        this.Total = total;
        this.Page = page;
        this.FilterDescription = filterDescription;
        this.NumberHeader = numberHeader;
        this.HolderHeader = holderHeader;
        this.BalanceHeader = balanceHeader;
        this.NextPage = nextPage;
        this.PreviousPage = previousPage;
        this.DepositIntoTopOfPage = depositIntoTopOfPage;
        this.ToggleFrozen = toggleFrozen;
        this.SortByNumber = sortByNumber;
        this.SortByHolder = sortByHolder;
        this.SortByBalance = sortByBalance;

        // The projection is in here too. Disposing it releases every row it still holds, which is
        // the ones that never left the view and so never triggered the eviction callback.
        this.disposables = new IDisposable[]
        {
            rows, total, page, filterDescription, numberHeader, holderHeader, balanceHeader,
            nextPage, previousPage, depositIntoTopOfPage, toggleFrozen, sortByNumber, sortByHolder,
            sortByBalance, projectedRows,
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
    public IOneWayBindableValue<string> NumberHeader { get; }

    /// <inheritdoc />
    public IOneWayBindableValue<string> HolderHeader { get; }

    /// <inheritdoc />
    public IOneWayBindableValue<string> BalanceHeader { get; }

    /// <inheritdoc />
    public IBindableAction NextPage { get; }

    /// <inheritdoc />
    public IBindableAction PreviousPage { get; }

    /// <inheritdoc />
    public IBindableAction DepositIntoTopOfPage { get; }

    /// <inheritdoc />
    public IBindableAction ToggleFrozen { get; }

    /// <inheritdoc />
    public IBindableAction SortByNumber { get; }

    /// <inheritdoc />
    public IBindableAction SortByHolder { get; }

    /// <inheritdoc />
    public IBindableAction SortByBalance { get; }

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
            StreamSink<Unit> sortByNumber = Stream.CreateSink<Unit>();
            StreamSink<Unit> sortByHolder = Stream.CreateSink<Unit>();
            StreamSink<Unit> sortByBalance = Stream.CreateSink<Unit>();

            Cell<bool> showFrozen =
                toggleFrozen.Accum(initialState: false, f: static (_, showing) => !showing);

            // One piece of state for the whole header row: which column, and which way. Three
            // buttons become one stream of columns, and the selection folds over it.
            Cell<SortSelection> sort = new[]
                {
                    sortByNumber.MapTo(AccountColumn.Number),
                    sortByHolder.MapTo(AccountColumn.Holder),
                    sortByBalance.MapTo(AccountColumn.Balance),
                }
                .OrElse()
                .Accum(
                    initialState: new SortSelection(AccountColumn.Balance, descending: true),
                    f: static (column, current) => current.Clicked(column));

            // The deposit pays into whichever account is at the top of the page, so the edit
            // depends on the view, and the view depends on the edits. That is a real cycle and the
            // loop is how it is closed - declared here, tied off once the view exists.
            StreamLoop<CollectionEdit<int, AccountIdentity, AccountState>> deposits =
                Stream.CreateLoop<CollectionEdit<int, AccountIdentity, AccountState>>();

            // No key selector: AccountIdentity implements IIdentity<int>.
            ReactiveCollection<int, AccountIdentity, AccountState> accounts =
                ReactiveCollection.Create(AccountSeed.Items, deposits);

            // The sort takes its order from a cell, so clicking a header re-files this stage
            // rather than building a second chain and choosing between the two. The three orders
            // sort by an int, a string and a long, and one cell holds all of them: an order keeps
            // its sort value's type to itself.
            ReactiveCollection<int, AccountIdentity, AccountState> filtered = accounts
                .Filter(showFrozen, static (showing, _, state) => showing || !state.IsFrozen)
                .SortBy(sort.Map(static selection => selection.Order));

            // Paging moves an offset. Toggling the filter sends it back to the first page, because
            // an offset that outlived the rows it pointed at would show an empty list. Sorting
            // deliberately does not: it reorders the same members rather than choosing different
            // ones, so every offset that was valid before it still is.
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
                onEvicted: static row => row.Dispose());

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
                total: total.Map(static cents => "Total across all accounts: " + Money(cents))
                    .ToOneWay(),
                page: offset.Lift(
                        pageCount,
                        static (at, count) => string.Format(
                            CultureInfo.CurrentCulture,
                            "Page {0:N0} of {1:N0}",
                            (at / PageSize) + 1,
                            count))
                    .ToOneWay(),
                filterDescription: showFrozen.Map(static showing =>
                        showing ? "Showing all accounts" : "Showing active accounts only")
                    .ToOneWay(),
                numberHeader: sort
                    .Map(static selection => selection.Caption(AccountColumn.Number, "Number"))
                    .ToOneWay(),
                holderHeader: sort
                    .Map(static selection => selection.Caption(AccountColumn.Holder, "Holder"))
                    .ToOneWay(),
                balanceHeader: sort
                    .Map(static selection => selection.Caption(AccountColumn.Balance, "Balance"))
                    .ToOneWay(),
                nextPage: nextPage.ToBindableAction(
                    offset.Lift(filtered.KeysCell, static (at, keys) => at + PageSize < keys.Count)),
                previousPage: previousPage.ToBindableAction(offset.Map(static at => at > 0)),
                depositIntoTopOfPage: deposit.ToBindableAction(
                    page.KeysCell.Map(static keys => keys.Count > 0)),
                toggleFrozen: toggleFrozen.ToBindableAction(),
                sortByNumber: sortByNumber.ToBindableAction(),
                sortByHolder: sortByHolder.ToBindableAction(),
                sortByBalance: sortByBalance.ToBindableAction(),
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

    /// <summary>Cents as a dollar amount.</summary>
    private static string Money(AccountState state) => Money(state.Balance);

    /// <remarks>
    ///     Formatted as US dollars whatever the machine's culture, because the amounts are dollars:
    ///     the current culture's currency format would put its own symbol on them.
    /// </remarks>
    private static string Money(long cents) =>
        (cents / 100m).ToString("C", UsDollars);
}
