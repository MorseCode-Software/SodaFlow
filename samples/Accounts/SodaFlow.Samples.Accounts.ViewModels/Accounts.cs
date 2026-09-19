using System.Collections.Generic;
using System.Linq;
using SodaFlow.Collections;

namespace SodaFlow.Samples.Accounts.ViewModels;

/// <summary>The part of an account that does not change: its identity.</summary>
/// <remarks>
///     The key comes from this part only. Thus the collection knows that a state edit cannot move
///     an item into a view with a key on the identity, and cannot move an item out of such a
///     view.
/// </remarks>
/// <param name="Number">The account number, which is the key.</param>
/// <param name="Holder">The holder of the account.</param>
// ReSharper disable once InheritdocConsiderUsage
internal sealed record AccountIdentity(int Number, string Holder) : IIdentity<int>
{
    /// <inheritdoc />
    public int Key => this.Number;
}

/// <summary>The part that changes: the current content of the account.</summary>
/// <remarks>
///     This is a record, thus an edit is a <c>with</c> expression that names the one field to
///     change and keeps the other fields.
/// </remarks>
/// <param name="Balance">The balance, in cents, thus the sample does not show a rounding error.</param>
/// <param name="IsFrozen">True when the account is frozen. The view filters on this.</param>
// ReSharper disable once InheritdocConsiderUsage
internal sealed record AccountState(long Balance, bool IsFrozen)
{
    /// <summary>True when a drain empties this account. The account is frozen and has a
    /// balance.</summary>
    /// <remarks>
    ///     This is a name here, and no code writes it at each use. The two view models are
    ///     different in the method that finds the drainable accounts, and not in the set of those
    ///     accounts. One predicate in one position keeps that as the only difference.
    /// </remarks>
    internal bool IsDrainable => this.IsFrozen && this.Balance != 0;
}

/// <summary>The accounts at the start of this sample.</summary>
/// <remarks>
///     <para>
///         There are one hundred thousand accounts behind a page of six accounts. The collection
///         has that shape: almost no user sees the items, and an edit must cost the quantity of
///         the rows on the screen and not the quantity of the collection.
///     </para>
///     <para>
///         This code makes the accounts and does not write them, and it makes the same accounts
///         at each run. Thus each run shows the same accounts in the same positions, and a user
///         can find a number from the screen again.
///     </para>
/// </remarks>
internal static class AccountSeed
{
    /// <summary>The number of accounts.</summary>
    private const int Count = 100_000;

    /// <summary>The first account number, thus each number on the screen has six digits.</summary>
    private const int FirstNumber = 100_000;

    /// <summary>The maximum initial balance, in cents: fifty thousand dollars.</summary>
    private const uint MaximumBalance = 50_000_00;

    private static readonly string[] Surnames =
    [
        // ReSharper disable StringLiteralTypo
        "Ackroyd", "Bhatt", "Calloway", "Dimitrova", "Eze", "Fairbairn", "Gruber", "Haddad",
        "Ivanov", "Jarrett", "Kowalski", "Lindqvist", "Moreau", "Nakamura", "Okonkwo", "Pereira",
        "Quill", "Rasmussen", "Sørensen", "Tanaka", "Urquhart", "Varga", "Whitlock", "Xu",
        "Yilmaz", "Zielinski", "Abernathy", "Brennan", "Castellano", "Delacroix", "Eriksson",
        "Fonseca", "Galloway", "Hartmann", "Ishikawa", "Jovanovic", "Kaur", "Lachance", "Mbeki",
        "Novak", "Oyelaran", "Petrakis", "Quintero", "Rahman", "Szabo", "Thorne", "Ueda", "Vasquez"
        // ReSharper restore StringLiteralTypo
    ];

    private static readonly string[] GivenNames =
    [
        // ReSharper disable StringLiteralTypo
        "Ada", "Bruno", "Chidi", "Dagny", "Elif", "Farid", "Greta", "Hiro", "Imani", "Jonas",
        "Kalani", "Leila", "Mateo", "Nadia", "Omar", "Priya", "Rafael", "Saoirse", "Tomasz",
        "Uma", "Viktor", "Wren", "Yusuf", "Zara"
        // ReSharper restore StringLiteralTypo
    ];

    /// <summary>The accounts, built one time and shared, because no code can change an
    /// item.</summary>
    internal static IReadOnlyList<Item<AccountIdentity, AccountState>> Items { get; } =
        [.. Enumerable.Range(start: 0, count: Count).Select(Create)];

    private static Item<AccountIdentity, AccountState> Create(int index)
    {
        // There are two hashes, thus the code makes the holder and the balance independently of
        // each other and of the account number. Thus a sort on one column moves the rows of the
        // list.
        uint forState = Scramble((uint)index + 1);
        uint forHolder = Scramble(forState);

        // Each surname with each given name, and not a small set of pairs many times.
        string holder = Surnames[forHolder % Surnames.Length] + ", " +
                        GivenNames[forHolder / Surnames.Length % GivenNames.Length];

        // The balances are different to the cent, thus two equal balances are rare. The two
        // highest bits are zero for one account in four, and that is the quantity of frozen
        // accounts at the start.
        return new Item<AccountIdentity, AccountState>(
            identity: new AccountIdentity(Number: FirstNumber + index, Holder: holder),
            state: new AccountState(Balance: forState % MaximumBalance + 1, IsFrozen: forState >> 30 == 0));
    }

    /// <summary>A hash with a low cost and a constant result, thus the seed is the same at each
    /// run and on each runtime.</summary>
    /// <remarks>
    ///     This code does not use <see cref="System.Random" />. A seeded instance gives the same
    ///     values, but that is a compatibility statement about a previous algorithm, and this
    ///     code must not use it.
    /// </remarks>
    private static uint Scramble(uint value)
    {
        unchecked
        {
            value ^= value >> 16;
            value *= 0x7feb352d;
            value ^= value >> 15;
            value *= 0x846ca68b;
            value ^= value >> 16;
            return value;
        }
    }
}
