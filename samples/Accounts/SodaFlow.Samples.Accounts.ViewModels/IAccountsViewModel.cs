using System;
using System.Collections.Generic;
using SodaFlow.Bindable.ObjectModel;

namespace SodaFlow.Samples.Accounts.ViewModels;

/// <summary>One row of the list. It is one account in the view that shows it.</summary>
/// <remarks>
///     <para>
///         Each row holds its own cells, and each cell follows one account. A deposit into one
///         account changes that row and nothing else. It does not change the list, the other rows
///         on the page, the one hundred thousand accounts that are not on the page, or the holder
///         of this row. The holder cannot change while the account is in the collection.
///     </para>
///     <para>
///         That is the full cause for the collection. A bind of a list of rows to one cell with
///         the full list builds each row again at each edit. The search sample does that, and it is
///         correct for a search sample, because its results are one answer that changes
///         together.
///     </para>
///     <para>
///         <see cref="IDisposable" /> is on the contract because the row must release those cells
///         and the deposit command. This applies to a row through this interface and to a row
///         through its class.
///     </para>
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
public interface IAccountRowViewModel : IDisposable
{
    /// <summary>The account number. It does not change while the account is in the collection.</summary>
    IOneWayBindableValue<string> Number { get; }

    /// <summary>The holder of the account. It is also constant for the life of the account.</summary>
    IOneWayBindableValue<string> Holder { get; }

    /// <summary>The balance. It is the part that changes.</summary>
    IOneWayBindableValue<string> Balance { get; }

    /// <summary>True when the account is frozen. The views show this with a gray row.</summary>
    IOneWayBindableValue<bool> IsFrozen { get; }

    /// <summary>Pays one hundred dollars into the account of this row. It is disabled when the
    /// account is frozen.</summary>
    /// <remarks>
    ///     A view can hide this for a frozen account, but that is presentation and not the rule.
    ///     The view model gates the deposit, thus no path to the command can pay into a frozen
    ///     account.
    /// </remarks>
    IBindableAction Deposit { get; }
}

/// <summary>The contract that the views bind to.</summary>
/// <remarks>
///     <para>
///         This is the shape of a screen with a list: a page of rows, a total across all accounts
///         and not across the page, and buttons that edit the collection of the rows.
///     </para>
///     <para>
///         <see cref="IDisposable" /> is on the contract for the cause that applies to the
///         counter sample. The code that built one must release it, through this interface and
///         through the class.
///     </para>
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
public interface IAccountsViewModel : IDisposable
{
    /// <summary>
    ///     The rows on the current page, which is a filtered, sorted, and windowed view of the
    ///     accounts.
    /// </summary>
    /// <remarks>
    ///     This changes when the members or the sequence of the page change. An edit to an
    ///     account on the page does not change this, because the edit goes to the row and not to
    ///     the list.
    /// </remarks>
    IOneWayBindableValue<IReadOnlyList<IAccountRowViewModel>> Rows { get; }

    /// <summary>The total balance across all accounts, with the frozen accounts.</summary>
    /// <remarks>
    ///     This is across the full collection and not across the page. A fold of the change gives
    ///     the total, and no code calculates the total again. See the implementation for that
    ///     line.
    /// </remarks>
    IOneWayBindableValue<string> Total { get; }

    /// <summary>The current page, and the number of pages.</summary>
    IOneWayBindableValue<string> Page { get; }

    /// <summary>True when the list shows the frozen accounts and the active accounts.</summary>
    IOneWayBindableValue<string> FilterDescription { get; }

    /// <summary>The account number header, with a mark when the list sorts on it.</summary>
    IOneWayBindableValue<string> NumberHeader { get; }

    /// <summary>The holder header, with a mark when the list sorts on it.</summary>
    IOneWayBindableValue<string> HolderHeader { get; }

    /// <summary>The balance header, with a mark when the list sorts on it.</summary>
    IOneWayBindableValue<string> BalanceHeader { get; }

    /// <summary>Moves the window forward. It is disabled on the last page.</summary>
    IBindableAction NextPage { get; }

    /// <summary>Moves the window back. It is disabled on the first page.</summary>
    IBindableAction PreviousPage { get; }

    /// <summary>
    ///     True when the list shows the frozen accounts. This is a change of criteria and builds
    ///     the view again. It is two-way, for a toggle switch.
    /// </summary>
    ITwoWayBindableValue<bool> ShowFrozen { get; }

    /// <summary>
    ///     Sets the balance of each frozen account to zero, at each state of the frozen account
    ///     filter. It is disabled when no account has a balance to drain.
    /// </summary>
    /// <remarks>
    ///     This is the opposite of a deposit. It is one click, but it edits one quarter of the
    ///     collection in one transaction and not one account. A row on the page moves only when the
    ///     drain contains its account.
    /// </remarks>
    IBindableAction DrainFrozenAccounts { get; }

    /// <summary>Sorts on the account number, or reverses the direction when the list sorts on
    /// it.</summary>
    /// <remarks>
    ///     A sort on the account number uses the identity part of an account, and no edit can
    ///     change that part. Thus, with this order a deposit changes a balance and cannot move a
    ///     row. The collection finds that itself, and no code tells it. The selector receives the
    ///     identity and never the state, thus the sort does not see an edit to a state.
    /// </remarks>
    IBindableAction SortByNumber { get; }

    /// <summary>Sorts on the holder, or reverses the direction. This order also uses only the
    /// identity.</summary>
    IBindableAction SortByHolder { get; }

    /// <summary>Sorts on the balance, or reverses the direction.</summary>
    /// <remarks>
    ///     This order sorts on the part that changes, thus a deposit can move a row along the
    ///     list. Use the deposit button with this order and with the other two orders.
    /// </remarks>
    IBindableAction SortByBalance { get; }
}
