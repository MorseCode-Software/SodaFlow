using System.Collections.Generic;

namespace SodaFlow;

/// <summary>
///     The set in which <c>ListenStrong</c> roots a listener, and in which a loop
///     keeps its own set alive.
/// </summary>
/// <remarks>
///     <para>
///         Each root stream, which is a sink, a never stream, or a loop, owns one set.
///         A stream with one input shares the set of that input. A stream with more
///         than one input gets a fan-out over the sets of all of them. A switch gets a
///         set over its outer and the inner that it selects now. A target that a set
///         keeps alive goes into each root set that the set reaches. Thus, the target
///         lives while any of those roots lives.
///     </para>
///     <para>
///         A walk with a visited set finds the roots that a set reaches. It goes
///         through the members of each fan-out. It also goes through the outer of each
///         switch and the inner that the switch selects now. Through a loop, a set can
///         reach itself. The visited set ends the walk there, and
///         each root gets the target one time for each call. A switch that selects a
///         new inner walks again for each target that passes it, and changes only the
///         roots that changed. Before, each switch counted the targets that passed it
///         and sent the counts on. A count did not record its path. Thus, a move that
///         came back through a loop took away a count that a different path gave. A
///         later stop then left the target in the roots for good.
///     </para>
///     <para>
///         All the sets share one lock. The walk reads the sets of many streams, and a
///         lock for each set let two threads in the same loop wait for each other. No
///         code runs under this lock that gets a different lock.
///     </para>
/// </remarks>
internal interface IKeepListenersAlive
{
    /// <summary>
    ///     Keeps <paramref name="target" /> alive in each root that this set reaches.
    ///     The roots change when a switch on the way moves.
    /// </summary>
    void KeepAlive(object target);

    /// <summary>
    ///     Reverses one call to <see cref="KeepAlive" /> with the same target.
    /// </summary>
    void StopKeepingAlive(object target);
}

internal static class KeepListenersAliveInternal
{
    /// <summary>
    ///     The lock of every keep-alive set.
    /// </summary>
    internal static readonly object Lock = new();

    /// <summary>
    ///     The set for a stream with two inputs.
    /// </summary>
    internal static IKeepListenersAlive Combine(IKeepListenersAlive first, IKeepListenersAlive second)
    {
        if (ReferenceEquals(objA: first, objB: second))
        {
            return first;
        }

        return first is FanOutKeepListenersAlive || second is FanOutKeepListenersAlive
            ? Flatten([first, second])
            : new FanOutKeepListenersAlive([first, second]);
    }

    /// <summary>
    ///     The set for a stream with more than two inputs.
    /// </summary>
    /// <remarks>
    ///     A fan-out among the inputs opens into its members. Thus, a lift of lifts
    ///     holds one flat list of sets.
    /// </remarks>
    internal static IKeepListenersAlive Combine(params IKeepListenersAlive[] inputs) =>
        inputs.Length == 2 ? Combine(first: inputs[0], second: inputs[1]) : Flatten(inputs);

    private static IKeepListenersAlive Flatten(IEnumerable<IKeepListenersAlive> inputs)
    {
        List<IKeepListenersAlive> members = [];

        foreach (IKeepListenersAlive input in inputs)
        {
            if (input is FanOutKeepListenersAlive fanOut)
            {
                // ReSharper disable once LoopCanBePartlyConvertedToQuery - A query allocates for each construction.
                foreach (IKeepListenersAlive member in fanOut.Members)
                {
                    if (!members.Contains(member))
                    {
                        members.Add(member);
                    }
                }
            }
            else if (!members.Contains(input))
            {
                members.Add(input);
            }
        }

        return members.Count == 1 ? members[0] : new FanOutKeepListenersAlive([.. members]);
    }
}

// The set of a root stream. It holds each target, and counts the calls that gave it. A target
// that a fan-out gives from two of its inputs, which share this root, stays until both stop.
internal sealed class KeepListenersAliveImplementation : IKeepListenersAlive
{
    // SodaFlow makes one of these for each root stream, and many streams get no target. Thus,
    // the dictionary waits until the code needs it.
    private Dictionary<object, int>? targets;

    public void KeepAlive(object target)
    {
        lock (KeepListenersAliveInternal.Lock)
        {
            this.Add(target);
        }
    }

    public void StopKeepingAlive(object target)
    {
        lock (KeepListenersAliveInternal.Lock)
        {
            this.Remove(target);
        }
    }

    // The caller holds the lock.
    internal void Add(object target)
    {
        this.targets ??= [];
        this.targets.TryGetValue(key: target, value: out int count);
        this.targets[target] = count + 1;
    }

    // The caller holds the lock.
    internal void Remove(object target)
    {
        if (this.targets == null || !this.targets.TryGetValue(key: target, value: out int count))
        {
            return;
        }

        if (count == 1)
        {
            this.targets.Remove(target);
        }
        else
        {
            this.targets[target] = count - 1;
        }
    }
}

// The set of a stream that is not a root: a fan-out or a switch. A target whose roots can change,
// because a switch is on the way, gets a registration that remembers its roots. A target whose walk
// finds no switch reaches the same roots each time, thus it needs no registration.
internal abstract class CompositeKeepListenersAlive : IKeepListenersAlive
{
    // Most sets keep one target or none. Thus, the first registration has a field, and the
    // dictionary waits for a second. The lock guards both.
    private Registration? first;
    private Dictionary<object, Registration>? more;

    public void KeepAlive(object target)
    {
        lock (KeepListenersAliveInternal.Lock)
        {
            Registration? found = this.Find(target);

            if (found != null)
            {
                found.Count++;
                return;
            }

            Walk walk = Walk.From(this);

            try
            {
                if (walk.Switches.Count == 0)
                {
                    foreach (KeepListenersAliveImplementation root in walk.Roots)
                    {
                        root.Add(target);
                    }

                    return;
                }

                Registration registration = new(origin: this, target: target);
                registration.Start(walk);

                if (this.first == null)
                {
                    this.first = registration;
                }
                else
                {
                    this.more ??= [];
                    this.more[target] = registration;
                }
            }
            finally
            {
                walk.Clear();
            }
        }
    }

    public void StopKeepingAlive(object target)
    {
        lock (KeepListenersAliveInternal.Lock)
        {
            Registration? found = this.Find(target);

            if (found != null)
            {
                if (--found.Count == 0)
                {
                    if (ReferenceEquals(objA: found, objB: this.first))
                    {
                        this.first = null;
                    }
                    else
                    {
                        this.more?.Remove(target);
                    }

                    found.Release();
                }

                return;
            }

            // A target with no registration found no switch. The members of a fan-out do not change,
            // thus the same walk finds the same roots.
            Walk walk = Walk.From(this);

            try
            {
                foreach (KeepListenersAliveImplementation root in walk.Roots)
                {
                    root.Remove(target);
                }
            }
            finally
            {
                walk.Clear();
            }
        }
    }

    // The caller holds the lock.
    private Registration? Find(object target)
    {
        if (this.first != null && Equals(objA: this.first.Target, objB: target))
        {
            return this.first;
        }

        return this.more != null && this.more.TryGetValue(key: target, value: out Registration? found) ? found : null;
    }
}

// The set of a stream with more than one input: its roots are the roots of each input.
file sealed class FanOutKeepListenersAlive(in IKeepListenersAlive[] members) : CompositeKeepListenersAlive
{
    internal IKeepListenersAlive[] Members { get; } = members;
}

// The set of a switch: the set of the outer, and the set of the inner that the switch selects now.
// When the outer is a constant, the inner is the only way to a root.
internal sealed class SwitchKeepListenersAlive(in IKeepListenersAlive outer) : CompositeKeepListenersAlive
{
    // The registrations whose walk passes this switch. Most switches have one or none. Thus, the
    // first has a field, and the set waits for a second. The lock guards both.
    private Registration? firstPassing;
    private HashSet<Registration>? morePassing;

    internal IKeepListenersAlive Outer { get; } = outer;

    // The lock guards this.
    internal IKeepListenersAlive? Inner { get; private set; }

    /// <summary>
    ///     Selects the next inner, and moves each registration that passes this switch
    ///     to the roots that it reaches through the next inner.
    /// </summary>
    /// <remarks>
    ///     The switch calls this when a transaction commits the inner that it selects.
    ///     A transaction that fails keeps the previous inner, thus it does not call
    ///     this.
    /// </remarks>
    internal void SetInner(IKeepListenersAlive next)
    {
        lock (KeepListenersAliveInternal.Lock)
        {
            IKeepListenersAlive? previous = this.Inner;

            if (ReferenceEquals(objA: previous, objB: next))
            {
                return;
            }

            if (this.morePassing == null || this.morePassing.Count == 0)
            {
                if (this.firstPassing != null)
                {
                    this.Move(registration: this.firstPassing, previous: previous, next: next);
                }

                this.Inner = next;
                return;
            }

            // A move changes the registrations of the switches on the way. Thus, the loop reads a
            // copy.
            Registration? firstCopy = this.firstPassing;
            Registration[] copy = new Registration[this.morePassing.Count];
            this.morePassing.CopyTo(copy);

            if (firstCopy != null)
            {
                this.Move(registration: firstCopy, previous: previous, next: next);
            }

            foreach (Registration registration in copy)
            {
                this.Move(registration: registration, previous: previous, next: next);
            }

            this.Inner = next;
        }
    }

    // The caller holds the lock.
    internal void Pass(Registration registration)
    {
        if (this.firstPassing == null)
        {
            this.firstPassing = registration;
            return;
        }

        this.morePassing ??= [];
        this.morePassing.Add(registration);
    }

    // The caller holds the lock.
    internal void Leave(Registration registration)
    {
        if (ReferenceEquals(objA: this.firstPassing, objB: registration))
        {
            this.firstPassing = null;
        }
        else
        {
            this.morePassing?.Remove(registration);
        }
    }

    // Walks from the origin of the registration with the previous inner and with the next one, and
    // applies the difference. The caller holds the lock, and sets the next inner after the moves.
    private void Move(Registration registration, IKeepListenersAlive? previous, IKeepListenersAlive next)
    {
        this.Inner = previous;
        Walk before = Walk.Before(registration.Origin);
        this.Inner = next;
        Walk after = Walk.From(registration.Origin);

        try
        {
            registration.Apply(before: before, after: after);
        }
        finally
        {
            before.Clear();
            after.Clear();
        }
    }
}

// One target that a composite set keeps alive through a switch. It stores no roots. The walk from
// its origin finds the roots that it reaches now. Only the move of a switch on the way can change
// what that walk finds, and each move applies the change. Thus, a walk at the stop finds the same
// roots as the walks before it gave the target to.
internal sealed class Registration(CompositeKeepListenersAlive origin, object target)
{
    internal CompositeKeepListenersAlive Origin { get; } = origin;

    internal object Target { get; } = target;

    // The number of calls with this target to the origin. The lock guards it.
    internal int Count { get; set; } = 1;

    // The caller holds the lock.
    internal void Start(Walk walk)
    {
        foreach (KeepListenersAliveImplementation root in walk.Roots)
        {
            root.Add(this.Target);
        }

        foreach (SwitchKeepListenersAlive @switch in walk.Switches)
        {
            @switch.Pass(this);
        }
    }

    // The caller holds the lock.
    internal void Release()
    {
        Walk walk = Walk.From(this.Origin);

        try
        {
            foreach (KeepListenersAliveImplementation root in walk.Roots)
            {
                root.Remove(this.Target);
            }

            foreach (SwitchKeepListenersAlive @switch in walk.Switches)
            {
                @switch.Leave(this);
            }
        }
        finally
        {
            walk.Clear();
        }
    }

    // The target goes into each root that the walk after reaches and the walk before did not. It
    // leaves each root that the walk before reached and the walk after does not. The same for the
    // switches. A root that both reach does not change. The caller holds the lock.
    internal void Apply(Walk before, Walk after)
    {
        // ReSharper disable LoopCanBePartlyConvertedToQuery, ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator - A query allocates for each move of a switch.
        foreach (KeepListenersAliveImplementation root in after.Roots)
        {
            if (!before.Reached(root))
            {
                root.Add(this.Target);
            }
        }

        foreach (KeepListenersAliveImplementation root in before.Roots)
        {
            if (!after.Reached(root))
            {
                root.Remove(this.Target);
            }
        }

        foreach (SwitchKeepListenersAlive @switch in after.Switches)
        {
            if (!before.Reached(@switch))
            {
                @switch.Pass(this);
            }
        }

        foreach (SwitchKeepListenersAlive @switch in before.Switches)
        {
            if (!after.Reached(@switch))
            {
                @switch.Leave(this);
            }
        }
        // ReSharper restore LoopCanBePartlyConvertedToQuery, ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
    }
}

// The sets that one set reaches: the roots, the switches, and every set on the way. A move needs
// two walks at the same time, thus there are two of these. Each walk uses the same collections,
// which the lock guards, and allocates nothing after the first. The caller clears the walk when it
// is done, because the collections otherwise keep the sets of the last walk in memory.
internal sealed class Walk
{
    private static readonly Walk Primary = new();
    private static readonly Walk Secondary = new();

    private readonly Stack<IKeepListenersAlive> pending = new();
    private readonly HashSet<IKeepListenersAlive> visited = [];

    private Walk()
    {
    }

    internal List<KeepListenersAliveImplementation> Roots { get; } = [];

    internal List<SwitchKeepListenersAlive> Switches { get; } = [];

    // The caller holds the lock.
    internal static Walk From(IKeepListenersAlive start) => Primary.Run(start);

    // The walk before a move, which the caller keeps while it makes the walk after. The caller holds
    // the lock.
    internal static Walk Before(IKeepListenersAlive start) => Secondary.Run(start);

    internal bool Reached(IKeepListenersAlive set) => this.visited.Contains(set);

    // The caller holds the lock.
    internal void Clear()
    {
        this.visited.Clear();
        this.Roots.Clear();
        this.Switches.Clear();
    }

    private Walk Run(IKeepListenersAlive start)
    {
        this.pending.Push(start);

        while (this.pending.Count > 0)
        {
            IKeepListenersAlive set = this.pending.Pop();

            if (!this.visited.Add(set))
            {
                continue;
            }

            switch (set)
            {
                case KeepListenersAliveImplementation root:
                    this.Roots.Add(root);
                    break;
                case SwitchKeepListenersAlive @switch:
                    this.Switches.Add(@switch);
                    this.pending.Push(@switch.Outer);

                    if (@switch.Inner != null)
                    {
                        this.pending.Push(@switch.Inner);
                    }

                    break;
                case FanOutKeepListenersAlive fanOut:
                    foreach (IKeepListenersAlive member in fanOut.Members)
                    {
                        this.pending.Push(member);
                    }

                    break;
            }
        }

        return this;
    }
}
