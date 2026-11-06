using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests.Internal;

public sealed class GenerationalRegistryTests
{
    [Test]
    public async Task TheSweepAfterAYoungCollectionReturnsADeadEntryInGenerationZero()
    {
        GenerationalRegistry registry = CreateRegistry();
        FakeEntry entry = new();
        registry.Add(entry);
        entry.IsStreamAlive = false;

        List<StreamListenerManager.IEntry> dead = registry.Sweep(collectedGeneration: 0) ?? [];

        await Assert.That(dead).IsEquivalentTo(Entries(entry));
        await Assert.That(registry.Count).IsEqualTo(0);
        await Assert.That(entry.Released).IsFalse().Because("the caller releases, outside the registry lock");
    }

    [Test]
    public async Task TheSweepMovesALiveEntryToTheGenerationOfItsNode()
    {
        GenerationalRegistry registry = CreateRegistry();
        FakeEntry entry = new();
        registry.Add(entry);
        entry.Generation = 2;

        await Assert.That(registry.Sweep(collectedGeneration: 0)).IsNull();
        await Assert.That(registry.Count).IsEqualTo(1);

        entry.IsStreamAlive = false;

        await Assert.That(registry.Sweep(collectedGeneration: 0))
            .IsNull()
            .Because("an entry in generation 2 cannot die in a collection of generation 0");

        await Assert.That(registry.Sweep(collectedGeneration: 1))
            .IsNull()
            .Because("an entry in generation 2 cannot die in a collection of generation 1");

        await Assert.That(registry.Count).IsEqualTo(1);

        List<StreamListenerManager.IEntry> dead = registry.Sweep(collectedGeneration: 2) ?? [];

        await Assert.That(dead).IsEquivalentTo(Entries(entry));
        await Assert.That(registry.Count).IsEqualTo(0);
    }

    [Test]
    public async Task AnEntryRegisteredAfterTheCollectionStaysInGenerationZero()
    {
        GenerationalRegistry registry = CreateRegistry();
        FakeEntry entry = new();
        registry.Add(entry);

        await Assert.That(registry.Sweep(collectedGeneration: 2)).IsNull();

        entry.IsStreamAlive = false;

        List<StreamListenerManager.IEntry> dead = registry.Sweep(collectedGeneration: 0) ?? [];

        await Assert.That(dead).IsEquivalentTo(Entries(entry));
    }

    [Test]
    public async Task TheSweepAfterAFullCollectionExaminesEveryGeneration()
    {
        GenerationalRegistry registry = CreateRegistry();
        FakeEntry old = new();
        FakeEntry middle = new();
        FakeEntry young = new();
        registry.Add(old);
        registry.Add(middle);
        old.Generation = 2;
        middle.Generation = 1;

        await Assert.That(registry.Sweep(collectedGeneration: 2)).IsNull();

        registry.Add(young);

        await Assert.That(registry.Count).IsEqualTo(3);

        old.IsStreamAlive = false;
        middle.IsStreamAlive = false;
        young.IsStreamAlive = false;

        List<StreamListenerManager.IEntry> dead = registry.Sweep(collectedGeneration: 2) ?? [];

        await Assert.That(dead).IsEquivalentTo(Entries(old, middle, young));
        await Assert.That(registry.Count).IsEqualTo(0);
    }

    [Test]
    public async Task TheSweepAfterACollectionOfGenerationOneLeavesGenerationTwo()
    {
        GenerationalRegistry registry = CreateRegistry();
        FakeEntry old = new();
        FakeEntry middle = new();
        registry.Add(old);
        registry.Add(middle);
        old.Generation = 2;
        middle.Generation = 1;

        await Assert.That(registry.Sweep(collectedGeneration: 2)).IsNull();

        old.IsStreamAlive = false;
        middle.IsStreamAlive = false;

        List<StreamListenerManager.IEntry> dead = registry.Sweep(collectedGeneration: 1) ?? [];

        await Assert.That(dead).IsEquivalentTo(Entries(middle));
        await Assert.That(registry.Count).IsEqualTo(1);
    }

    [Test]
    public async Task TheCollectedGenerationIsTheHighestWhoseCountChanged()
    {
        int[] lastCounts = new int[3];
        int[] counts = [0, 5, 2];
        Func<int, int> read = g => counts[g];

        await Assert.That(StreamListenerManager.CollectedGeneration(lastCounts: lastCounts, collectionCount: read))
            .IsEqualTo(2);

        await Assert.That(lastCounts).IsEquivalentTo([0, 5, 2]);

        await Assert.That(StreamListenerManager.CollectedGeneration(lastCounts: lastCounts, collectionCount: read))
            .IsEqualTo(0)
            .Because("no collection since the last call");

        counts[1] = 6;

        await Assert.That(StreamListenerManager.CollectedGeneration(lastCounts: lastCounts, collectionCount: read))
            .IsEqualTo(1);

        counts[1] = 7;
        counts[2] = 3;

        await Assert.That(StreamListenerManager.CollectedGeneration(lastCounts: lastCounts, collectionCount: read))
            .IsEqualTo(2);

        await Assert.That(lastCounts).IsEquivalentTo([0, 7, 3]);
    }

    private static StreamListenerManager.IEntry[] Entries(params StreamListenerManager.IEntry[] entries) => entries;

    private static GenerationalRegistry CreateRegistry() =>
        new(getGeneration: static entry => ((FakeEntry)entry).Generation);

    private sealed class FakeEntry : StreamListenerManager.IEntry
    {
        public bool IsStreamAlive { get; set; } = true;

        public int Generation { get; set; }

        public bool Released { get; private set; }

        public void Release() => this.Released = true;
    }
}
