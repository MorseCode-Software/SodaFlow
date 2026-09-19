using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Collections;
using SodaFlow.Functional;
using AccountOrder =
    SodaFlow.Collections.KeyOrder<
        int,
        SodaFlow.Samples.Accounts.ViewModels.AccountIdentity,
        SodaFlow.Samples.Accounts.ViewModels.AccountState>;

namespace SodaFlow.Samples.Accounts.ViewModels;

/// <summary>A column of the list. The list can sort on a column.</summary>
internal enum AccountColumn
{
    /// <summary>The account number. It is part of the identity.</summary>
    Number,

    /// <summary>The holder of the account. It is also part of the identity.</summary>
    Holder,

    /// <summary>The balance. It is the part that changes.</summary>
    Balance
}

/// <summary>The column that the list sorts on, and the direction.</summary>
/// <remarks>
///     <para>
///         Two of the three orders sort on the identity part of an account, and no edit can
///         change that part. Thus with those two orders a deposit changes a balance and cannot
///         move a row. The order on the balance moves a row. Use the deposit button and change
///         between the orders to see the difference.
///     </para>
///     <para>
///         This type gives the order, and no code keeps the order in a second field. Thus the
///         screen has one item of state, a column and a direction, and the sort comes from it.
///     </para>
/// </remarks>
/// <param name="Column">The column that the list sorts on.</param>
/// <param name="IsDescending">True when the list starts at the largest value.</param>
// ReSharper disable once InheritdocConsiderUsage
internal sealed record SortSelection(AccountColumn Column, bool IsDescending)
{
    /// <summary>The comparer for a holder, as one instance and not a new instance for each
    /// order.</summary>
    /// <remarks>
    ///     <see cref="StringComparer.CurrentCultureIgnoreCase" /> builds a new comparer at each
    ///     read. A sort stage identifies the order that it holds, and the same order in the
    ///     opposite direction, only when the new order comes from the same selector instance and
    ///     the same comparer instance. For the opposite direction it sorts again with the holders
    ///     that it read, and does not read each holder again. A read of the comparer in the call
    ///     makes the stage read and sort each holder again at each click of the same header. The
    ///     culture is the culture at the first use of this field.
    /// </remarks>
    private static readonly IComparer<string> HolderComparer = StringComparer.CurrentCultureIgnoreCase;

    /// <summary>This selection, as an order that the sort stage can hold.</summary>
    /// <remarks>
    ///     The three orders give sort values of three types: an <c>int</c>, a <c>string</c> and
    ///     a <c>long</c>. The three orders have the same type here, thus one cell can hold the
    ///     order that applies. This code names each comparer, because it selects the direction as
    ///     it operates and does not write the direction into the call. A holder also needs an
    ///     explicit comparer for each field, and not the default for the type of the column.
    /// </remarks>
    internal AccountOrder Order =>
        this.Column switch
        {
            AccountColumn.Number => AccountOrder.ByIdentity(
                selector: static identity => identity.Number,
                sortComparer: Comparer<int>.Default,
                keyComparer: Comparer<int>.Default,
                isDescending: this.IsDescending),
            AccountColumn.Holder => AccountOrder.ByIdentity(
                selector: static identity => identity.Holder,
                sortComparer: HolderComparer,
                keyComparer: Comparer<int>.Default,
                isDescending: this.IsDescending),
            _ => AccountOrder.By(
                selector: static (_, state) => state.Balance,
                sortComparer: Comparer<long>.Default,
                keyComparer: Comparer<int>.Default,
                isDescending: this.IsDescending)
        };
}

/// <summary>The operations that a view model needs on a sort selection that can be
/// missing.</summary>
/// <remarks>
///     <para>
///         This is an extension block and not a set of static methods on
///         <see cref="SortSelection" />, because the subject of each operation is the
///         <see cref="Maybe{T}" /> and not the selection in it. Before, they were static methods
///         whose first parameter was that subject. C# had no better shape. Now a type from a
///         different assembly can get members of its own.
///     </para>
///     <para>
///         <c>Order</c> gives the largest benefit. Each caller needs the order that applies, and
///         each caller wrote the two parts of that: a map into the selection, and then a default
///         of arrival order for a list that no user sorted.
///     </para>
/// </remarks>
internal static class SortSelectionExtensions
{
    extension(Maybe<SortSelection> sortSelection)
    {
        /// <summary>The order that applies. It is arrival order until a user clicks a
        /// header.</summary>
        internal AccountOrder Order =>
            sortSelection.Map(static selection => selection.Order).ValueOr(AccountOrder.ByArrival);

        /// <summary>The result of a click on a header. The same column reverses the direction,
        /// and a different column becomes the sort column.</summary>
        /// <remarks>
        ///     A new column starts at the smallest value. The balance starts at the largest
        ///     value, because a user usually wants a list of balances in that direction.
        /// </remarks>
        internal SortSelection UpdateSort(AccountColumn column)
        {
            return sortSelection.Match(
                onSome: selection =>
                    column == selection.Column
                        ? selection with { IsDescending = !selection.IsDescending }
                        : CreateNewSortSelection(column),
                onNone: () => CreateNewSortSelection(column));

            static SortSelection CreateNewSortSelection(AccountColumn column) =>
                new(Column: column, IsDescending: column == AccountColumn.Balance);
        }

        /// <summary>The text of a header, with a mark when it is the sort column.</summary>
        internal string Caption(AccountColumn column, string name) =>
            name
            + sortSelection.Match(
                onSome: selection =>
                    column == selection.Column ? selection.IsDescending ? " \u25bc" : " \u25b2" : string.Empty,
                onNone: static () => string.Empty);
    }
}

/// <summary>One row. It holds the cells that follow one account through the view that shows
/// it.</summary>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class AccountRowViewModel : IAccountRowViewModel
{
    private readonly IReadOnlyList<IDisposable> disposables;

    internal AccountRowViewModel(
        IOneWayBindableValue<string> number,
        IOneWayBindableValue<string> holder,
        IOneWayBindableValue<string> balance,
        IOneWayBindableValue<bool> isFrozen,
        IBindableAction deposit,
        Stream<CollectionEdit<int, AccountIdentity, AccountState>> depositsStream)
    {
        this.Number = number;
        this.Holder = holder;
        this.Balance = balance;
        this.IsFrozen = isFrozen;
        this.Deposit = deposit;
        this.DepositsStream = depositsStream;
        this.disposables = [number, holder, balance, isFrozen, deposit];
    }

    /// <inheritdoc />
    public IOneWayBindableValue<string> Number { get; }

    /// <inheritdoc />
    public IOneWayBindableValue<string> Holder { get; }

    /// <inheritdoc />
    public IOneWayBindableValue<string> Balance { get; }

    /// <inheritdoc />
    public IOneWayBindableValue<bool> IsFrozen { get; }

    /// <inheritdoc />
    public IBindableAction Deposit { get; }

    /// <summary>The edits that the deposits of this row make. The graph gates them, and the list
    /// sends them back in.</summary>
    internal Stream<CollectionEdit<int, AccountIdentity, AccountState>> DepositsStream { get; }

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
///         Look at the screen. A deposit into an account changes the balance of that row and
///         nothing else. The other rows do not change, and the list does not build again, but the
///         sort can move the account. That is the correct operation of the collection: an edit
///         goes to the rows that show it, and not to the rows near them.
///     </para>
///     <para>
///         A move to a different page is a change of criteria, and for a slice that change has a
///         low cost. It moves a window across an order that did not change. A change to the frozen
///         accounts is also a change of criteria, but that change builds the filter again, which
///         has a high cost. Each change is one line here, and the code does not show the
///         difference between them. For that cause the reference page gives the costs.
///     </para>
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
public sealed class AccountsViewModel : IAccountsViewModel
{
    /// <summary>The number of rows on a page.</summary>
    private const int PageSize = 6;

    /// <summary>The value that the deposit button adds, in cents.</summary>
    private const long DepositAmount = 100_00L;

    /// <summary>The format for each value on the screen.</summary>
    private static readonly NumberFormatInfo UsDollars = CultureInfo.GetCultureInfo("en-US").NumberFormat;

    private readonly IReadOnlyList<IDisposable> disposables;

    private readonly Stream<CollectionEdit<int, AccountIdentity, AccountState>> deposits;
    private readonly Stream<CollectionEdit<int, AccountIdentity, AccountState>> drains;

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
        ITwoWayBindableValue<bool> showFrozen,
        IBindableAction drainFrozenAccounts,
        IBindableAction sortByNumber,
        IBindableAction sortByHolder,
        IBindableAction sortByBalance,
        Stream<CollectionEdit<int, AccountIdentity, AccountState>> deposits,
        Stream<CollectionEdit<int, AccountIdentity, AccountState>> drains,
        MappedItems<AccountRowViewModel> projectedRows)
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
        this.ShowFrozen = showFrozen;
        this.DrainFrozenAccounts = drainFrozenAccounts;
        this.SortByNumber = sortByNumber;
        this.SortByHolder = sortByHolder;
        this.SortByBalance = sortByBalance;
        this.deposits = deposits;
        this.drains = drains;

        // The projection is also in here. Disposal of the projection releases each row that it
        // holds. Those are the rows that stayed in the view and thus did not cause the eviction
        // callback.
        this.disposables =
        [
            rows,
            total,
            page,
            filterDescription,
            numberHeader,
            holderHeader,
            balanceHeader,
            nextPage,
            previousPage,
            showFrozen,
            drainFrozenAccounts,
            sortByNumber,
            sortByHolder,
            sortByBalance,
            projectedRows
        ];
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
    public ITwoWayBindableValue<bool> ShowFrozen { get; }

    /// <inheritdoc />
    public IBindableAction DrainFrozenAccounts { get; }

    /// <inheritdoc />
    public IBindableAction SortByNumber { get; }

    /// <inheritdoc />
    public IBindableAction SortByHolder { get; }

    /// <inheritdoc />
    public IBindableAction SortByBalance { get; }

    /// <inheritdoc />
    /// <remarks>
    ///     This code does not name each row. The rows belong to the projection, which is in the
    ///     list and releases all of them at its disposal. This applies to a row that left the view
    ///     before now and to a row that stayed until now.
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
            ForwardReference<AccountsViewModel>.WithoutCaptures(static viewModelLoop =>
            {
                StreamSink<Unit> nextPage = Stream.CreateSink<Unit>();
                StreamSink<Unit> previousPage = Stream.CreateSink<Unit>();
                StreamSink<Unit> drainFrozenAccounts = Stream.CreateSink<Unit>();
                StreamSink<Unit> sortByNumber = Stream.CreateSink<Unit>();
                StreamSink<Unit> sortByHolder = Stream.CreateSink<Unit>();
                StreamSink<Unit> sortByBalance = Stream.CreateSink<Unit>();

                // A switch keeps its own position, thus this is a value that the view writes,
                // and not a command that counts clicks.
                CellSink<bool> showFrozen = Cell.CreateSink(false);

                // One item of state for the full header row: the column and the direction. Three
                // buttons become one stream of columns, and the selection folds across that
                // stream.
                Cell<Maybe<SortSelection>> sort =
                    new[]
                        {
                            sortByNumber.MapTo(AccountColumn.Number),
                            sortByHolder.MapTo(AccountColumn.Holder),
                            sortByBalance.MapTo(AccountColumn.Balance)
                        }
                        .OrElse()
                        .Accum(
                            initialState:
                            Maybe<SortSelection>.None,
                            f: static (column, current) => Maybe.Some(current.UpdateSort(column)));

                // Each row adds to its own account, thus the rows send the edits, and the
                // collection that receives the edits makes the rows. That is a true cycle, and the
                // loop closes it. This code declares the loop here and completes it after the rows
                // are available.
                Stream<CollectionEdit<int, AccountIdentity, AccountState>> depositsLoop =
                    viewModelLoop.Map(static viewModel => viewModel.deposits).SwitchS();

                // The drain is a cycle of the same shape. It reads the accounts to empty from
                // the collection that contains them.
                Stream<CollectionEdit<int, AccountIdentity, AccountState>> drainsLoop =
                    viewModelLoop.Map(static viewModel => viewModel.drains).SwitchS();

                // No key selector is necessary, because AccountIdentity is an IIdentity<int>.
                ReactiveCollection<int, AccountIdentity, AccountState> accounts =
                    ReactiveCollection.Create(initialEntries: AccountSeed.Items, depositsLoop, drainsLoop);

                // This is each frozen account with a balance, across the full collection and not
                // across the page or the filter, because a drain empties accounts that no user
                // sees. This is a second view of the same accounts, and the graph keeps it current
                // with the first view. The predicate reads only the state that it receives, thus
                // an edit to one account costs this view one test of that account.
                ReactiveCollection<int, AccountIdentity, AccountState> drainable =
                    accounts.Filter(static (_, state) => state.IsDrainable);

                Cell<bool> canDrain = drainable.KeysCell.Map(static keys => keys.Count > 0);

                // The graph gates this, and the command is also disabled, for the cause that
                // applies to the deposit of a row. This code reads the keys in the transaction
                // that receives the edit, thus the drain empties only the accounts that were
                // frozen and not empty at the time of the click.
                Stream<CollectionEdit<int, AccountIdentity, AccountState>> drains =
                    drainFrozenAccounts
                        .Gate(canDrain)
                        .Snapshot(c: drainable.KeysCell, f: static (_, keys) => Drain(keys));

                // The sort reads its order from a cell, thus a click on a header sorts this stage
                // again and does not build a second chain to select between two. The three orders
                // sort on an int, a string and a long, and one cell holds all three, because an
                // order keeps the type of its sort value private.
                ReactiveCollection<int, AccountIdentity, AccountState> filtered =
                    accounts
                        .Filter(
                            criteriaCell: showFrozen,
                            predicate: static (showing, _, state) => showing || !state.IsFrozen)
                        .SortBy(sort.Map(static selection => selection.Order));

                // A change of page moves an offset. A change of the filter sends the offset back
                // to the first page, because an offset that stays after the removal of its rows
                // shows an empty list. A change of the sort does not do this. The sort puts the
                // same members in a different sequence and does not select different members, thus
                // each offset that was correct before the sort is correct after it.
                Cell<int> offset =
                    new[]
                        {
                            nextPage.MapTo(static (int at) => at + PageSize),
                            previousPage.MapTo(static (int at) => at - PageSize),
                            showFrozen.Updates().MapTo(static (int _) => 0)
                        }
                        .OrElse()
                        .Accum(initialState: 0, f: static (move, at) => Math.Max(val1: 0, val2: move(at)));

                ReactiveCollection<int, AccountIdentity, AccountState> page =
                    filtered.Slice(offsetCell: offset, limitCell: Cell.Constant(PageSize));

                // There is one row object for each account, in the sequence of the page. Map
                // keeps the rows, thus a deposit that changes one balance does not change this
                // list. The row follows its own account, and the list changes only when the members
                // or the sequence of the page change.
                //
                // The rows hold bindables, thus the eviction disposes them. No code here needs to
                // know the time of the eviction, and that is the purpose of the callback at the
                // projection.
                MappedItems<AccountRowViewModel> rows =
                    page.Map(
                        project: key => Row(page: page, key: key),
                        onEvicted: static row => row.Dispose());

                // The deposits are the edits that the rows on the page send. The set of those
                // rows changes with the page, thus this code builds the merge again from each
                // version of the list and switches to it. A row that left the page sends nothing
                // more, before or after its eviction. A page holds six rows, thus each build is
                // six streams.
                Stream<CollectionEdit<int, AccountIdentity, AccountState>> deposits =
                    rows.Items
                        .Map(static items => items.Select(static row => row.DepositsStream).OrElse())
                        .SwitchS();

                // The total folds the change and does not calculate the total again from the
                // store. The change contains the two sides of it, thus a delta needs no other
                // data.
                long initialTotal = AccountSeed.Items.Sum(static item => item.State.Balance);

                Cell<long> total =
                    accounts.ItemChangesStream
                        .Map(static change => DeltaOf(change))
                        .Accum(initialState: initialTotal, f: static (delta, running) => running + delta);

                Cell<int> pageCount =
                    filtered.KeysCell.Map(static keys =>
                        Math.Max(val1: 1, val2: (keys.Count + PageSize - 1) / PageSize));

                return new AccountsViewModel(
                    // A list of rows is a list of the interface of those rows, but a cell is a
                    // class and cannot be covariant. Thus this code gives the conversion as the
                    // return type of the lambda.
                    rows: rows.Items
                        .Map(static IReadOnlyList<IAccountRowViewModel> (items) => items)
                        .ToOneWay(),
                    total: total.Map(static cents => "Total across all accounts: " + Money(cents))
                        .ToOneWay(),
                    page: offset.Lift(
                            c2: pageCount,
                            f: static (at, count) =>
                                string.Format(
                                    provider: CultureInfo.CurrentCulture,
                                    format: "Page {0:N0} of {1:N0}",
                                    arg0: at / PageSize + 1,
                                    arg1: count))
                        .ToOneWay(),
                    filterDescription: showFrozen.Map(static showing =>
                            showing ? "Showing all accounts" : "Showing active accounts only")
                        .ToOneWay(),
                    numberHeader: sort
                        .Map(static selection => selection.Caption(column: AccountColumn.Number, name: "Number"))
                        .ToOneWay(),
                    holderHeader: sort
                        .Map(static selection => selection.Caption(column: AccountColumn.Holder, name: "Holder"))
                        .ToOneWay(),
                    balanceHeader: sort
                        .Map(static selection => selection.Caption(column: AccountColumn.Balance, name: "Balance"))
                        .ToOneWay(),
                    nextPage: nextPage.ToBindableAction(
                        offset.Lift(c2: filtered.KeysCell, f: static (at, keys) => at + PageSize < keys.Count)),
                    previousPage: previousPage.ToBindableAction(offset.Map(static at => at > 0)),
                    showFrozen: showFrozen.ToTwoWay(),
                    drainFrozenAccounts: drainFrozenAccounts.ToBindableAction(canDrain),
                    sortByNumber: sortByNumber.ToBindableAction(),
                    sortByHolder: sortByHolder.ToBindableAction(),
                    sortByBalance: sortByBalance.ToBindableAction(),
                    projectedRows: rows,
                    deposits: deposits,
                    drains: drains);
            }));

    /// <summary>One edit that empties all of these accounts.</summary>
    /// <remarks>
    ///     This is one edit, and not one edit for each account. Thus the collection changes one
    ///     time for each drain, at all counts of accounts. Each view sorts one time, and the total
    ///     folds one delta.
    /// </remarks>
    private static CollectionEdit<int, AccountIdentity, AccountState> Drain(
        // This is the type of the keys, and not the list interface of that type, because a drain
        // reads all of the keys and a call through the class is faster than a call through the
        // interface.
        // ReSharper disable once SuggestBaseTypeForParameter
        OrderedKeys<int, AccountIdentity, AccountState> keys)
    {
        Dictionary<int, Func<AccountState, AccountState>> updates = new(keys.Count);

        // This code uses an index and not an enumeration, because the keys give their enumerator
        // as an interface and a foreach allocates one.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (int index = 0; index < keys.Count; index++)
        {
            updates.Add(key: keys[index], value: Emptied);
        }

        return new CollectionEdit<int, AccountIdentity, AccountState>(
            updates: updates,
            adds: [],
            removes: []);
    }

    /// <summary>The operation of a drain on one account.</summary>
    /// <remarks>
    ///     This is a static method, thus each update in a drain uses one delegate from the cache.
    ///     The drain does not allocate a delegate for each of the thousands of accounts.
    /// </remarks>
    private static AccountState Emptied(AccountState state) => state with { Balance = 0 };

    /// <summary>The row for one account, built from the page that shows it.</summary>
    /// <remarks>
    ///     Each cell here comes from the page and not from the collection, thus a row gives the
    ///     data of its own view. An account that the page does not hold has no holder, no balance,
    ///     and no target for a deposit.
    /// </remarks>
    private static AccountRowViewModel Row(ReactiveCollection<int, AccountIdentity, AccountState> page, int key)
    {
        Cell<Maybe<AccountIdentity>> identity = page.IdentityCell(key);
        Cell<Maybe<AccountState>> state = page.StateCell(key);

        Cell<bool> isFrozen =
            state.Map(static current =>
                current.Match(onSome: static value => value.IsFrozen, onNone: static () => false));

        Cell<bool> canDeposit =
            state.Map(static current =>
                current.Match(onSome: static value => !value.IsFrozen, onNone: static () => false));

        StreamSink<Unit> deposit = Stream.CreateSink<Unit>();

        return new AccountRowViewModel(
            number: identity
                .Map(static current =>
                    current.Match(
                        onSome: static value => value.Number.ToString(CultureInfo.CurrentCulture),
                        onNone: static () => string.Empty))
                .ToOneWay(),
            holder: identity
                .Map(static current =>
                    current.Match(onSome: static value => value.Holder, onNone: static () => string.Empty))
                .ToOneWay(),
            balance: state
                .Map(static current => current.Match(onSome: Money, onNone: static () => string.Empty))
                .ToOneWay(),
            isFrozen: isFrozen.ToOneWay(),

            // A frozen account disables this, and the view shows that...
            deposit: deposit.ToBindableAction(canDeposit),

            // ...and the graph gates it, which is the rule. The enabled state of a command is a
            // copy that the graph sends to the binding thread, and the graph can change before the
            // view receives that copy. Also, each holder of the command can call Execute. The graph
            // reads the gate in the transaction that receives the deposit, thus no path to this
            // stream adds to a frozen account.
            depositsStream: CollectionEdit<int, AccountIdentity, AccountState>.FromUpdates(
                key: key,
                transformsStream: deposit
                    .Gate(canDeposit)
                    .MapTo(static (AccountState current) =>
                        current with { Balance = current.Balance + DepositAmount })));
    }

    /// <summary>The change of the total, from the keys in this change.</summary>
    /// <remarks>
    ///     A new key has no state before the change, thus it adds only its new balance. A key
    ///     that the change removes is not in the new states, thus it adds only the negative of
    ///     its previous balance. This test is sufficient for the two conditions.
    /// </remarks>
    private static long DeltaOf(ItemChange<int, AccountIdentity, AccountState> change)
    {
        long delta = 0;

        foreach (KeyValuePair<int, AccountState> pair in change.NewStates)
        {
            if (change.Before.States.TryGetState(key: pair.Key, state: out AccountState? was))
            {
                delta -= was.Balance;
            }

            delta += pair.Value.Balance;
        }

        foreach (int key in change.Removed)
        {
            if (change.Before.States.TryGetState(key: key, state: out AccountState? was))
            {
                delta -= was.Balance;
            }
        }

        return delta;
    }

    /// <summary>Cents as a value in dollars.</summary>
    private static string Money(AccountState state) => Money(state.Balance);

    /// <remarks>
    ///     The format is US dollars at each culture of the machine, because the values are
    ///     dollars. The currency format of the current culture puts a different symbol on them.
    /// </remarks>
    private static string Money(long cents) => (cents / 100m).ToString(format: "C", provider: UsDollars);
}
