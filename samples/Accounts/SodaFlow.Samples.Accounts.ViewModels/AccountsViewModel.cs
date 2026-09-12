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
/// <param name="Column">The column the list is sorted by.</param>
/// <param name="Descending">Whether it runs from the largest down.</param>
// ReSharper disable once InheritdocConsiderUsage
internal sealed record SortSelection(AccountColumn Column, bool Descending)
{
    /// <summary>This selection as an order the sort stage can hold.</summary>
    /// <remarks>
    ///     Three orders projecting sort values of three types - an <c>int</c>, a <c>string</c> and
    ///     a <c>long</c> - and all three are the same type here, which is what lets one cell hold
    ///     whichever is in force. The comparers are spelled out because the direction is decided at
    ///     run time rather than written into the call, and because holders want comparison to be
    ///     explicit for each field rather than the default for the column type.
    /// </remarks>
    internal AccountOrder Order =>
        this.Column switch
        {
            AccountColumn.Number => AccountOrder.ByIdentity(
                selector: static identity => identity.Number,
                sortComparer: Comparer<int>.Default,
                keyComparer: Comparer<int>.Default,
                descending: this.Descending),
            AccountColumn.Holder => AccountOrder.ByIdentity(
                selector: static identity => identity.Holder,
                sortComparer: StringComparer.CurrentCultureIgnoreCase,
                keyComparer: Comparer<int>.Default,
                descending: this.Descending),
            _ => AccountOrder.By(
                selector: static (_, state) => state.Balance,
                sortComparer: Comparer<long>.Default,
                keyComparer: Comparer<int>.Default,
                descending: this.Descending),
        };

    /// <summary>What clicking a header does: the same column reverses, another one selects.</summary>
    /// <remarks>
    ///     A newly chosen column starts ascending, except the balance, which starts at the largest
    ///     because that is the way a list of balances is usually wanted.
    /// </remarks>
    internal SortSelection Clicked(AccountColumn column) =>
        column == this.Column
            ? this with { Descending = !this.Descending }
            : new SortSelection(Column: column, Descending: column == AccountColumn.Balance);

    /// <summary>A header's caption, marked if it is the column in force.</summary>
    internal string Caption(AccountColumn column, string name) =>
        Caption(sortSelection: Maybe.Some(this), column: column, name: name);

    /// <summary>A header's caption, marked if it is the column in force.</summary>
    internal static string Caption(Maybe<SortSelection> sortSelection, AccountColumn column, string name) =>
        name + sortSelection.Match(
            onSome: sortSelection =>
                column == sortSelection.Column ? sortSelection.Descending ? " \u25bc" : " \u25b2" : string.Empty,
            onNone: static () => string.Empty);
}

/// <summary>One row, holding cells that follow one account through the view showing it.</summary>
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

    /// <summary>The edits this row's deposits make, already gated, for the list to feed back in.</summary>
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
///         What to watch on screen. Depositing into an account moves that row's balance and nothing
///         else - the other rows do not flicker, and the list itself does not rebuild, even though
///         the sort could have moved the account. That is the collection doing its job: an
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
        ITwoWayBindableValue<bool> showFrozen,
        IBindableAction drainFrozenAccounts,
        IBindableAction sortByNumber,
        IBindableAction sortByHolder,
        IBindableAction sortByBalance,
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

        // The projection is in here too. Disposing it releases every row it still holds, which is
        // the ones that never left the view and so never triggered the eviction callback.
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
            StreamSink<Unit> drainFrozenAccounts = Stream.CreateSink<Unit>();
            StreamSink<Unit> sortByNumber = Stream.CreateSink<Unit>();
            StreamSink<Unit> sortByHolder = Stream.CreateSink<Unit>();
            StreamSink<Unit> sortByBalance = Stream.CreateSink<Unit>();

            // A switch holds its own position, so this is a value the view writes rather than a
            // command whose presses are counted.
            CellSink<bool> showFrozen = Cell.CreateSink(false);

            // One piece of state for the whole header row: which column, and which way. Three
            // buttons become one stream of columns, and the selection folds over it.
            Cell<Maybe<SortSelection>> sort =
                new[]
                    {
                        sortByNumber.MapTo(AccountColumn.Number),
                        sortByHolder.MapTo(AccountColumn.Holder),
                        sortByBalance.MapTo(AccountColumn.Balance),
                    }
                    .OrElse()
                    .Accum(
                        initialState:
                        Maybe<SortSelection>.None,
                        f: static (column, current) =>
                            Maybe.Some(
                                current.Match(
                                    onSome: current => current.Clicked(column),
                                    onNone: () => new SortSelection(Column: column, Descending: false))));

            // Each row pays into its own account, so the edits come from the rows, and the rows
            // come from the collection the edits are for. That is a real cycle and the loop is how
            // it is closed - declared here, tied off once the rows exist.
            StreamLoop<CollectionEdit<int, AccountIdentity, AccountState>> deposits =
                Stream.CreateLoop<CollectionEdit<int, AccountIdentity, AccountState>>();

            // The drain is the same shape of cycle: which accounts it empties is read from the
            // collection it empties them in.
            StreamLoop<CollectionEdit<int, AccountIdentity, AccountState>> drains =
                Stream.CreateLoop<CollectionEdit<int, AccountIdentity, AccountState>>();

            // No key selector: AccountIdentity implements IIdentity<int>.
            ReactiveCollection<int, AccountIdentity, AccountState> accounts =
                ReactiveCollection.Create(initialEntries: AccountSeed.Items, deposits, drains);

            // Every frozen account with something left in it, across the whole collection rather
            // than the page or the filter, because a drain empties accounts nobody is looking at.
            // A second view over the same accounts, kept current alongside the first: the
            // predicate reads only the state it is handed, so an edit to one account costs this
            // view one test of that account.
            ReactiveCollection<int, AccountIdentity, AccountState> drainable =
                accounts.Filter(static (_, state) => state.IsFrozen && state.Balance != 0);

            Cell<bool> canDrain = drainable.KeysCell.Map(static keys => keys.Count > 0);

            // Gated in the graph as well as disabled on the command, for the reason a row's
            // deposit is. The keys are read in the same transaction the edit lands in, so what is
            // emptied is exactly what was frozen and non-empty at the moment of the click.
            drains.Loop(
                drainFrozenAccounts
                    .Gate(canDrain)
                    .Snapshot(c: drainable.KeysCell, f: static (_, keys) => Drain(keys)));

            // The sort takes its order from a cell, so clicking a header re-files this stage
            // rather than building a second chain and choosing between the two. The three orders
            // sort by an int, a string and a long, and one cell holds all of them: an order keeps
            // its sort value's type to itself.
            ReactiveCollection<int, AccountIdentity, AccountState> filtered =
                accounts
                    .Filter(
                        criteriaCell: showFrozen,
                        predicate: static (showing, _, state) => showing || !state.IsFrozen)
                    .SortBy(
                        sort.Map(static selection =>
                            selection.Map(static selection => selection.Order).ValueOr(AccountOrder.ByArrival)));

            // Paging moves an offset. Toggling the filter sends it back to the first page, because
            // an offset that outlived the rows it pointed at would show an empty list. Sorting
            // deliberately does not: it reorders the same members rather than choosing different
            // ones, so every offset that was valid before it still is.
            Cell<int> offset =
                new[]
                    {
                        nextPage.MapTo(static (int at) => at + PageSize),
                        previousPage.MapTo(static (int at) => at - PageSize),
                        showFrozen.Updates().MapTo(static (int _) => 0),
                    }
                    .OrElse()
                    .Accum(initialState: 0, f: static (move, at) => Math.Max(val1: 0, val2: move(at)));

            ReactiveCollection<int, AccountIdentity, AccountState> page =
                filtered.Slice(offsetCell: offset, limitCell: Cell.Constant(PageSize));

            // One row object per account, in the page's order. Map keeps them, so a deposit that
            // moves one balance leaves this list alone: the row follows its own account and the
            // list only moves when the page's membership or order does.
            //
            // The rows own bindables, so eviction disposes them. Nothing here has to know when
            // that happens - which is the point of the callback being where the projection is.
            MappedItems<AccountRowViewModel> rows =
                page.Map(
                    project: key => Row(page: page, key: key),
                    onEvicted: static row => row.Dispose());

            // The deposits are whatever the rows on the page are sending. Which rows those are
            // moves with the page, so the merge is rebuilt from each version of the list and
            // switched to: a row that has left the page is no longer listened to, whether or not
            // it has been evicted yet. A page holds six rows, so the rebuild is six streams.
            deposits.Loop(
                rows.Items
                    .Map(static items => items.Select(static row => row.DepositsStream).OrElse())
                    .SwitchS());

            // The total is folded from what changed rather than recomputed from the store. The
            // change carries both sides of it, so a delta needs nothing kept alongside.
            long initialTotal = AccountSeed.Items.Sum(static item => item.State.Balance);

            Cell<long> total =
                accounts.ItemChangesStream
                    .Map(static change => DeltaOf(change))
                    .Accum(initialState: initialTotal, f: static (delta, running) => running + delta);

            Cell<int> pageCount =
                filtered.KeysCell.Map(static keys =>
                    Math.Max(val1: 1, val2: (keys.Count + PageSize - 1) / PageSize));

            return new AccountsViewModel(
                // A list of rows is a list of the interface they implement, but a cell is a class
                // and cannot be covariant, so the conversion is spelled out as the lambda's return
                // type.
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
                    .Map(static selection =>
                        SortSelection.Caption(sortSelection: selection, column: AccountColumn.Number, name: "Number"))
                    .ToOneWay(),
                holderHeader: sort
                    .Map(static selection =>
                        SortSelection.Caption(sortSelection: selection, column: AccountColumn.Holder, name: "Holder"))
                    .ToOneWay(),
                balanceHeader: sort
                    .Map(static selection =>
                        SortSelection.Caption(sortSelection: selection, column: AccountColumn.Balance, name: "Balance"))
                    .ToOneWay(),
                nextPage: nextPage.ToBindableAction(
                    offset.Lift(c2: filtered.KeysCell, f: static (at, keys) => at + PageSize < keys.Count)),
                previousPage: previousPage.ToBindableAction(offset.Map(static at => at > 0)),
                showFrozen: showFrozen.ToTwoWay(),
                drainFrozenAccounts: drainFrozenAccounts.ToBindableAction(canDrain),
                sortByNumber: sortByNumber.ToBindableAction(),
                sortByHolder: sortByHolder.ToBindableAction(),
                sortByBalance: sortByBalance.ToBindableAction(),
                projectedRows: rows);
        });

    /// <summary>One edit emptying every one of these accounts.</summary>
    /// <remarks>
    ///     One edit rather than one per account, so however many accounts are drained the
    ///     collection moves once: every view re-files once, and the total folds one delta.
    /// </remarks>
    private static CollectionEdit<int, AccountIdentity, AccountState> Drain(
        // The keys' own type rather than the list interface it implements, because a drain reads
        // every one of them and a call through the class is cheaper than one through the interface.
        // ReSharper disable once SuggestBaseTypeForParameter
        OrderedKeys<int, AccountIdentity, AccountState> keys)
    {
        Dictionary<int, Func<AccountState, AccountState>> updates = new(keys.Count);

        // Indexed rather than enumerated, because the keys hand back their enumerator as an
        // interface, so a foreach would allocate one.
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

    /// <summary>What a drain does to one account.</summary>
    /// <remarks>
    ///     A static method, so every update in a drain shares one cached delegate rather than
    ///     allocating one for each of the thousands of accounts.
    /// </remarks>
    private static AccountState Emptied(AccountState state) => state with { Balance = 0 };

    /// <summary>The row for one account, built from the page it is showing on.</summary>
    /// <remarks>
    ///     Every cell here is asked of the page rather than of the collection, so a row answers for
    ///     the view it belongs to: an account the page does not hold has no holder, no balance, and
    ///     nothing that can be paid into.
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

            // Disabled for a frozen account, which is what the view shows...
            deposit: deposit.ToBindableAction(canDeposit),

            // ...and gated in the graph, which is the rule. A command's enablement is a copy
            // posted to the binding thread and can trail the graph, and anything holding the
            // command can call Execute; the gate is sampled in the transaction the deposit lands
            // in, so no path to this stream pays into a frozen account.
            depositsStream: CollectionEdit<int, AccountIdentity, AccountState>.FromUpdates(
                key: key,
                transformsStream: deposit
                    .Gate(canDeposit)
                    .MapTo(static (AccountState current) =>
                        current with { Balance = current.Balance + DepositAmount })));
    }

    /// <summary>How much the total moved, from the keys this change touched.</summary>
    /// <remarks>
    ///     An added key has no state before, so it contributes only its new balance; a removed one
    ///     is absent from the new states, so it contributes only the negation of its old balance.
    ///     Neither needs a special case beyond looking.
    /// </remarks>
    private static long DeltaOf(ItemChange<int, AccountIdentity, AccountState> change)
    {
        long delta = 0;

        foreach (KeyValuePair<int, AccountState> pair in change.NewStates)
        {
            if (change.Before.States.TryGetState(key: pair.Key, state: out AccountState was))
            {
                delta -= was.Balance;
            }

            delta += pair.Value.Balance;
        }

        foreach (int key in change.Removed)
        {
            if (change.Before.States.TryGetState(key: key, state: out AccountState was))
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
    private static string Money(long cents) => (cents / 100m).ToString(format: "C", provider: UsDollars);
}
