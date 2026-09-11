using System.Collections.Generic;
using System.Linq;
using SodaFlow.Collections;

namespace SodaFlow.Samples.Accounts.ViewModels;

/// <summary>The half of an account that never changes: what it is.</summary>
/// <remarks>
///     The key is derived from this alone, which is what lets the collection treat a state edit as
///     something that cannot move an item into or out of a view keyed on identity.
///     <para />
///     A class rather than a record because this sample targets netstandard2.0 at C# 10, where a
///     positional record needs an <c>IsExternalInit</c> shim - noise about the target framework
///     rather than about SodaFlow.
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class AccountIdentity : IIdentity<int>
{
    /// <param name="number">The account number, which is its key.</param>
    /// <param name="holder">Whose account it is.</param>
    internal AccountIdentity(int number, string holder)
    {
        this.Number = number;
        this.Holder = holder;
    }

    /// <summary>The account number.</summary>
    internal int Number { get; }

    /// <summary>Whose account it is.</summary>
    internal string Holder { get; }

    /// <inheritdoc />
    public int Key => this.Number;
}

/// <summary>The half that moves: what the account currently holds.</summary>
internal sealed class AccountState
{
    /// <param name="balance">Pence, so the sample never shows a rounding artifact.</param>
    /// <param name="isFrozen">Whether the account is frozen, which the view filters on.</param>
    internal AccountState(long balance, bool isFrozen)
    {
        this.Balance = balance;
        this.IsFrozen = isFrozen;
    }

    /// <summary>The balance, in pence.</summary>
    internal long Balance { get; }

    /// <summary>Whether the account is frozen.</summary>
    internal bool IsFrozen { get; }

    /// <summary>The same account with a different balance.</summary>
    internal AccountState WithBalance(long balance) => new(balance, this.IsFrozen);
}

/// <summary>The accounts this sample starts with.</summary>
/// <remarks>
///     <para>
///         A hundred thousand of them behind a page of six, which is the shape the collection is
///         built for: almost all of the items are not being looked at, and an edit should cost what
///         the rows on screen cost rather than what the collection holds.
///     </para>
///     <para>
///         Generated rather than written out, and deterministically, so every run shows the same
///         accounts in the same places and a number seen on screen can be found again.
///     </para>
/// </remarks>
internal static class AccountSeed
{
    /// <summary>How many accounts there are.</summary>
    private const int Count = 100_000;

    /// <summary>The first account number, so that every number on screen is six digits wide.</summary>
    private const int FirstNumber = 100_000;

    /// <summary>The largest opening balance, in pence: fifty thousand pounds.</summary>
    private const uint MaximumBalance = 50_000_00;

    private static readonly string[] Surnames =
    {
        "Ackroyd", "Bhatt", "Calloway", "Dimitrova", "Eze", "Fairbairn", "Gruber", "Haddad",
        "Ivanov", "Jarrett", "Kowalski", "Lindqvist", "Moreau", "Nakamura", "Okonkwo", "Pereira",
        "Quill", "Rasmussen", "Sørensen", "Tanaka", "Urquhart", "Varga", "Whitlock", "Xu",
        "Yilmaz", "Zielinski", "Abernathy", "Brennan", "Castellano", "Delacroix", "Eriksson",
        "Fonseca", "Galloway", "Hartmann", "Ishikawa", "Jovanovic", "Kaur", "Lachance", "Mbeki",
        "Novak", "Oyelaran", "Petrakis", "Quintero", "Rahman", "Szabo", "Thorne", "Ueda", "Vasquez",
    };

    private static readonly string[] GivenNames =
    {
        "Ada", "Bruno", "Chidi", "Dagny", "Elif", "Farid", "Greta", "Hiro", "Imani", "Jonas",
        "Kalani", "Leila", "Mateo", "Nadia", "Omar", "Priya", "Rafael", "Saoirse", "Tomasz",
        "Uma", "Viktor", "Wren", "Yusuf", "Zara",
    };

    /// <summary>The accounts, built once and shared, since nothing can change an item.</summary>
    internal static IReadOnlyList<Item<AccountIdentity, AccountState>> Items { get; } =
        Enumerable.Range(0, Count).Select(Create).ToList();

    private static Item<AccountIdentity, AccountState> Create(int index)
    {
        // Two hashes, so the holder and the balance are drawn independently of each other and of
        // the account number, and sorting by any one column visibly re-files the list.
        uint forState = Scramble((uint)index);
        uint forHolder = Scramble(forState);

        // Every surname with every given name, rather than a handful of pairs on repeat.
        string holder = Surnames[forHolder % Surnames.Length] + ", " +
                        GivenNames[(forHolder / Surnames.Length) % GivenNames.Length];

        // Balances scattered to the penny, so ties are rare. The top two bits both clear is one
        // account in four, which is how many start frozen.
        return new Item<AccountIdentity, AccountState>(
            new AccountIdentity(FirstNumber + index, holder),
            new AccountState((forState % MaximumBalance) + 1, (forState >> 30) == 0));
    }

    /// <summary>A cheap, fixed hash, so the seed is the same on every run and every runtime.</summary>
    /// <remarks>
    ///     Not <see cref="System.Random" />: a seeded one is stable in practice, but that is a
    ///     compatibility promise about a legacy algorithm rather than something this should lean on.
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
