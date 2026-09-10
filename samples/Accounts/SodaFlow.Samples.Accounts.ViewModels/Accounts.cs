using System.Collections.Generic;
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
public sealed class AccountIdentity : IIdentity<int>
{
    /// <param name="number">The account number, which is its key.</param>
    /// <param name="holder">Whose account it is.</param>
    public AccountIdentity(int number, string holder)
    {
        this.Number = number;
        this.Holder = holder;
    }

    /// <summary>The account number.</summary>
    public int Number { get; }

    /// <summary>Whose account it is.</summary>
    public string Holder { get; }

    /// <inheritdoc />
    public int Key => this.Number;
}

/// <summary>The half that moves: what the account currently holds.</summary>
public sealed class AccountState
{
    /// <param name="balance">Pence, so the sample never shows a rounding artifact.</param>
    /// <param name="isFrozen">Whether the account is frozen, which the view filters on.</param>
    public AccountState(long balance, bool isFrozen)
    {
        this.Balance = balance;
        this.IsFrozen = isFrozen;
    }

    /// <summary>The balance, in pence.</summary>
    public long Balance { get; }

    /// <summary>Whether the account is frozen.</summary>
    public bool IsFrozen { get; }

    /// <summary>The same account with a different balance.</summary>
    public AccountState WithBalance(long balance) => new(balance, this.IsFrozen);
}

/// <summary>The accounts this sample starts with.</summary>
/// <remarks>
///     Enough of them that the paging is doing something, and few enough that the numbers on screen
///     can be checked by hand.
/// </remarks>
internal static class AccountSeed
{
    private static readonly string[] Holders =
    {
        "Ackroyd", "Bhatt", "Calloway", "Dimitrova", "Eze", "Fairbairn", "Gruber", "Haddad",
        "Ivanov", "Jarrett", "Kowalski", "Lindqvist", "Moreau", "Nakamura", "Okonkwo", "Pereira",
        "Quill", "Rasmussen", "Sørensen", "Tanaka",
    };

    internal static IReadOnlyList<Item<AccountIdentity, AccountState>> Items
    {
        get
        {
            List<Item<AccountIdentity, AccountState>> items = new(Holders.Length);

            for (int index = 0; index < Holders.Length; index++)
            {
                items.Add(new Item<AccountIdentity, AccountState>(
                    new AccountIdentity(1000 + index, Holders[index]),

                    // Balances that are not in account-number order, so sorting by balance is
                    // visibly doing something. Every fourth account starts frozen.
                    new AccountState(((index * 37) % 20 + 1) * 125_00L, index % 4 == 3)));
            }

            return items;
        }
    }
}
