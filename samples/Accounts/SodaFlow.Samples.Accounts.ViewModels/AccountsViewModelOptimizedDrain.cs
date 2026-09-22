using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Collections;
using SodaFlow.Functional;

namespace SodaFlow.Samples.Accounts.ViewModels;

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
///     <para>
///         The graph is the graph of <see cref="AccountsViewModel" /> with one difference. A fold
///         across the item changes of the collection keeps the accounts to empty as a set of keys,
///         and there is no second filtered view. The set holds only keys and has no order.
///         Measurements show that it costs less to hold and less to drain.
///     </para>
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
public sealed class AccountsViewModelOptimizedDrain : IAccountsViewModel
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

    private AccountsViewModelOptimizedDrain(
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
            ForwardReference<AccountsViewModelOptimizedDrain>.WithoutCaptures(static viewModelLoop =>
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
                Cell<ImmutableHashSet<int>> drainableAccountKeys =
                    accounts.ItemChangesStream.AccumLazy(
                        initialState: accounts.SnapshotCell.SampleLazy()
                            .Map(static snapshot =>
                                snapshot.States.Pairs.Where(static pair => pair.Value.IsDrainable)
                                    .Select(static pair => pair.Key)
                                    .ToImmutableHashSet()),
                        f: static (changes, drainableAccountKeys) =>
                        {
                            // This calls Contains on the set until the membership of a key
                            // changes. Thus, an edit that changes no membership, such as a Pay,
                            // which is almost each edit, allocates nothing and gives the same set.
                            // From the first key that changes, this code uses the builder directly.
                            // Add and Remove on the builder do nothing for a key in the correct
                            // condition, thus a Drain examines each key one time.
                            ImmutableHashSet<int>.Builder? builder = null;

                            foreach (KeyValuePair<int, AccountState> pair in changes.NewStates)
                            {
                                bool drainable = pair.Value.IsDrainable;

                                if (builder is null)
                                {
                                    if (drainable == drainableAccountKeys.Contains(pair.Key))
                                    {
                                        continue;
                                    }

                                    builder = drainableAccountKeys.ToBuilder();
                                }

                                if (drainable)
                                {
                                    builder.Add(pair.Key);
                                }
                                else
                                {
                                    builder.Remove(pair.Key);
                                }
                            }

                            foreach (int key in changes.Removed)
                            {
                                if (builder is null)
                                {
                                    if (!drainableAccountKeys.Contains(key))
                                    {
                                        continue;
                                    }

                                    builder = drainableAccountKeys.ToBuilder();
                                }

                                builder.Remove(key);
                            }

                            return builder?.ToImmutable() ?? drainableAccountKeys;
                        });

                // This stage is calm, because the set sends a value at each edit that it
                // receives. A Pay into an active account gives the same set. Without Calm, each
                // edit calculates the same answer again and changes the enabled state of the
                // command for no result.
                Cell<bool> canDrain =
                    drainableAccountKeys.Map(static drainableAccountKeys => drainableAccountKeys.Count > 0).Calm();

                // The graph gates this, and the command is also disabled, for the cause that
                // applies to the deposit of a row. This code reads the keys in the transaction
                // that receives the edit, thus the drain empties only the accounts that were
                // frozen and not empty at the time of the click.
                Stream<CollectionEdit<int, AccountIdentity, AccountState>> drains =
                    drainFrozenAccounts
                        .Gate(canDrain)
                        .Snapshot(
                            c: drainableAccountKeys,
                            f: static (_, drainableAccountKeys) =>
                                Drain(drainableAccountKeys));

                // The sort reads its order from a cell, thus a click on a header sorts this stage
                // again and does not build a second chain to select between two. The three orders
                // sort on an int, a string, and a long, and one cell holds all three, because an
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

                return new AccountsViewModelOptimizedDrain(
                    // A list of rows is a list of the interface of those rows, but a cell is a
                    // class and cannot be covariant. Thus, this code gives the conversion as the
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
    ///     This is one edit, and not one edit for each account. Thus, the collection changes one
    ///     time for each drain, at all counts of accounts. Each view sorts one time, and the total
    ///     folds one delta.
    /// </remarks>
    private static CollectionEdit<int, AccountIdentity, AccountState> Drain(
        // This is the type of the set, and not an interface to the set. A foreach through the
        // interface boxes the struct enumerator of the set and makes each step an interface call.
        // A foreach through the set does not box the enumerator and does not make an interface
        // call, and a drain moves through each key.
        // ReSharper disable once SuggestBaseTypeForParameter
        ImmutableHashSet<int> keys)
    {
        Dictionary<int, Func<AccountState, AccountState>> updates = new(keys.Count);

        foreach (int key in keys)
        {
            updates.Add(key: key, value: Emptied);
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
