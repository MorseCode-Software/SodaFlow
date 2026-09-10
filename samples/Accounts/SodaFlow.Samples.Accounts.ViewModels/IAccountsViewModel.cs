using System;
using System.Collections.Generic;
using SodaFlow.Bindable.ObjectModel;

namespace SodaFlow.Samples.Accounts.ViewModels;

/// <summary>One row of the list, which is one account seen through the view showing it.</summary>
/// <remarks>
///     <para>
///         Each of these holds its own cells, and each of those follows one account. A deposit into
///         one account moves that row and nothing else - not the list, not the other nineteen rows,
///         and not this row's holder, which cannot change while the account exists.
///     </para>
///     <para>
///         That is the whole reason the collection exists. Binding a list of rows to one cell
///         holding the whole list would rebuild every row on every edit, which is what the search
///         sample does and what is right for a search sample: its results genuinely are one answer
///         that changes as a whole.
///     </para>
/// </remarks>
public interface IAccountRowViewModel
{
    /// <summary>The account number, which never changes while the account exists.</summary>
    IOneWayBindableValue<string> Number { get; }

    /// <summary>Whose account it is - also fixed for the life of the account.</summary>
    IOneWayBindableValue<string> Holder { get; }

    /// <summary>The balance, which is the part that moves.</summary>
    IOneWayBindableValue<string> Balance { get; }
}

/// <summary>What the views bind to.</summary>
/// <remarks>
///     <para>
///         The shape of a list-backed screen: a page of rows, a total over everything rather than
///         over the page, and buttons that edit the collection the rows are drawn from.
///     </para>
///     <para>
///         <see cref="IDisposable" /> is on the contract for the reason it is on the counter
///         sample's: whoever built one has to release it, and that is as true through this
///         interface as through the class.
///     </para>
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
public interface IAccountsViewModel : IDisposable
{
    /// <summary>
    ///     The rows on the current page, which is a filtered, sorted, windowed view of the accounts.
    /// </summary>
    /// <remarks>
    ///     This moves when the page's membership or order moves, and not when an account on it is
    ///     merely edited - that reaches the row rather than the list.
    /// </remarks>
    IOneWayBindableValue<IReadOnlyList<IAccountRowViewModel>> Rows { get; }

    /// <summary>The total balance across every account, frozen ones included.</summary>
    /// <remarks>
    ///     Over the whole collection rather than the page, and folded from what changed rather than
    ///     recomputed - see the implementation, where that is the interesting line.
    /// </remarks>
    IOneWayBindableValue<string> Total { get; }

    /// <summary>Which page is showing, and how many there are.</summary>
    IOneWayBindableValue<string> Page { get; }

    /// <summary>Whether the list is showing frozen accounts as well as active ones.</summary>
    IOneWayBindableValue<string> FilterDescription { get; }

    /// <summary>Moves the window on, and is disabled on the last page.</summary>
    IBindableAction NextPage { get; }

    /// <summary>Moves it back, and is disabled on the first.</summary>
    IBindableAction PreviousPage { get; }

    /// <summary>Pays a hundred pounds into the account at the top of the current page.</summary>
    /// <remarks>
    ///     Deliberately edits one account rather than many: the point on screen is that one row
    ///     changes and the rest of the page sits still, even though the sort could have moved it.
    /// </remarks>
    IBindableAction DepositIntoTopOfPage { get; }

    /// <summary>Shows or hides frozen accounts, which is a criteria change and rebuilds the view.</summary>
    IBindableAction ToggleFrozen { get; }
}
