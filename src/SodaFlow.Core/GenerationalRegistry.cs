using System;
using System.Collections.Generic;
using System.Linq;
using IEntry = SodaFlow.StreamListenerManager.IEntry;

namespace SodaFlow;

/// <summary>
///     The entries of <see cref="StreamListenerManager" />, in one list for each
///     generation of the garbage collector.
/// </summary>
/// <remarks>
///     <para>
///         A stream makes its node in its constructor, and the two survive the same
///         collections. Thus, while the stream is alive, the generation of the node is
///         the generation of the stream. A stream in generation N dies only in a
///         collection of generation N or higher. Thus, a sweep after a collection of
///         generation N examines the lists for generations 0 to N and no other.
///     </para>
///     <para>
///         A new entry goes in the list for generation 0. The sweep moves each live
///         entry to the list for the generation of its node. An entry that registered
///         after the collection stays where it is. Thus, a young collection costs a
///         sweep of the young entries only, and the sweep examines a large live graph
///         after a full collection only.
///     </para>
///     <para>
///         This class is not thread-safe. <see cref="StreamListenerManager" /> holds
///         its lock around each call.
///     </para>
/// </remarks>
internal sealed class GenerationalRegistry(in Func<object, int> getGeneration)
{
    // GC.MaxGeneration is a call into the runtime, and the sweep reads it for each entry.
    // The manager reads this field too, so the call to the runtime occurs one time.
    internal static readonly int MaxGeneration = GC.MaxGeneration;

    private readonly Func<object, int> getGeneration = getGeneration;
    private readonly List<IEntry>[] generations = CreateGenerations();

    internal int Count => this.generations.Sum(static list => list.Count);

    internal void Add(IEntry entry) => this.generations[0].Add(entry);

    /// <summary>
    ///     Removes the entries of dead streams from the lists for generations 0 to
    ///     <paramref name="collectedGeneration" /> and returns them. Moves each live
    ///     entry to the list for the generation of its node.
    /// </summary>
    internal List<IEntry>? Sweep(int collectedGeneration)
    {
        List<IEntry>? dead = null;

        // The sweep goes from the highest generation down. An entry that moves goes to a higher
        // generation, which this sweep examined before. Thus, the sweep examines each entry one
        // time.
        for (int generation = Math.Min(val1: collectedGeneration, val2: MaxGeneration); generation >= 0; generation--)
        {
            List<IEntry> list = this.generations[generation];

            // This moves backwards and puts the last entry into each empty position. Each entry
            // that it moves comes from a position that it read first. Thus, it reads each entry
            // one time.
            for (int i = list.Count - 1; i >= 0; i--)
            {
                IEntry entry = list[i];
                int target;

                if (!entry.IsStreamAlive)
                {
                    target = -1;
                }
                else if (generation == MaxGeneration)
                {
                    // No list is above this one. Thus, the sweep does not read the generation of
                    // the node, which is the cost of a full sweep over a large live graph.
                    target = generation;
                }
                else
                {
                    target = Math.Min(val1: this.getGeneration(arg: entry), val2: MaxGeneration);
                }

                if (target == generation)
                {
                    continue;
                }

                int last = list.Count - 1;

                if (i != last)
                {
                    list[i] = list[last];
                }

                list.RemoveAt(last);

                if (target < 0)
                {
                    dead ??= [];
                    dead.Add(entry);
                }
                else
                {
                    this.generations[target].Add(entry);
                }
            }

            // A List keeps its backing array after a removal. Release that array after a large
            // number of entries becomes small again.
            if (list.Capacity > 100 && list.Count < list.Capacity / 2)
            {
                list.TrimExcess();
            }
        }

        return dead;
    }

    private static List<IEntry>[] CreateGenerations()
    {
        List<IEntry>[] lists = new List<IEntry>[MaxGeneration + 1];

        for (int i = 0; i < lists.Length; i++)
        {
            lists[i] = [];
        }

        return lists;
    }
}
