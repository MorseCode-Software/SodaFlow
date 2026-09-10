using System.Collections.Generic;

namespace SodaFlow.Collections.Tests;

/// <summary>The immutable portion of a test item. The key is its <see cref="Number" />.</summary>
internal sealed record ItemId(int Number, string Code);

/// <summary>The mutable portion of a test item.</summary>
internal sealed record ItemState(string Name, int Score);

/// <summary>
///     The same identity, carrying its own key, for the <c>Create</c> overloads that take no
///     selector.
/// </summary>
// ReSharper disable once InheritdocConsiderUsage
internal sealed record SelfKeyedItemId(int Number) : IIdentity<int>
{
    public int Key => this.Number;
}

internal static class TestUtil
{
    internal static int KeyOf(ItemId identity) => identity.Number;

    internal static Entry<ItemId, ItemState> Item(int number, string name, int score) =>
        new(new ItemId(number, $"C{number}"), new ItemState(name, score));

    internal static CollectionEdit<int, ItemId, ItemState> Add(params Entry<ItemId, ItemState>[] entries) =>
        CollectionEdit<int, ItemId, ItemState>.Add(entries);

    internal static CollectionEdit<int, ItemId, ItemState> Remove(params int[] keys) =>
        CollectionEdit<int, ItemId, ItemState>.Remove(keys);

    internal static CollectionEdit<int, ItemId, ItemState> Score(int key, int score) =>
        CollectionEdit<int, ItemId, ItemState>.Update(key, state => state with { Score = score });

    internal static Entry<SelfKeyedItemId, ItemState> SelfKeyedItem(int number, string name, int score) =>
        new(new SelfKeyedItemId(number), new ItemState(name, score));

    internal static List<int> Keys(IEnumerable<int> keys) => [.. keys];
}
