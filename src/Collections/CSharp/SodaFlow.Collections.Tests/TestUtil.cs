using System.Collections.Generic;

namespace SodaFlow.Collections.Tests;

/// <summary>The immutable portion of a test item. The key is its <see cref="Number" />.</summary>
internal sealed record ItemIdentity(int Number, string Code);

/// <summary>The mutable portion of a test item.</summary>
internal sealed record ItemState(string Name, int Score);

internal static class TestUtil
{
    internal static int KeyOf(ItemIdentity identity) => identity.Number;

    internal static Item<ItemIdentity, ItemState> Item(int number, string name, int score) =>
        new(new ItemIdentity(number, $"C{number}"), new ItemState(name, score));

    internal static CollectionEdit<int, ItemIdentity, ItemState> Add(params Item<ItemIdentity, ItemState>[] items) =>
        CollectionEdit<int, ItemIdentity, ItemState>.Add(items);

    internal static CollectionEdit<int, ItemIdentity, ItemState> Remove(params int[] keys) =>
        CollectionEdit<int, ItemIdentity, ItemState>.Remove(keys);

    internal static CollectionEdit<int, ItemIdentity, ItemState> Score(int key, int score) =>
        CollectionEdit<int, ItemIdentity, ItemState>.Update(key, state => state with { Score = score });

    internal static List<int> Keys(IEnumerable<int> keys) => [.. keys];
}
