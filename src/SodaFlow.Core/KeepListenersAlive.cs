using System;
using System.Collections.Generic;

namespace SodaFlow;

/// <summary>
///     The set in which <c>ListenStrong</c> roots a listener.
/// </summary>
/// <remarks>
///     <para>
///         Each root stream, which is a sink, a never stream, or a loop, owns one set.
///         A stream with one input shares the set of that input. A stream with more
///         than one input gets a fan-out over the sets of all of them. Thus, every
///         root holds the listener, and the listener lives while any root lives.
///         Before, such a stream took the set of its first input only. A listener on
///         it stopped with no message when the application dropped that root and
///         kept the other.
///     </para>
///     <para>
///         A caller locks the set of its own stream before a call. A fan-out locks
///         each member before it calls into it. Thus, two fan-outs that share a root
///         cannot change the set of that root at the same time. The locks nest from
///         an output toward its inputs only. Outside a loop, the inputs of a stream
///         cannot include the stream, thus the locks cannot deadlock.
///     </para>
///     <para>
///         A switch over a loop can reach its own set again through the outer or
///         the inner. The thread that comes back gets a lock that it already has,
///         which a lock lets it do. The set of the switch then stops that call. Two
///         threads that start calls in the same loop at the same time can each wait
///         for a lock that the other has.
///     </para>
/// </remarks>
internal interface IKeepListenersAlive
{
    void KeepListenerAlive(IListener listener);
    void StopKeepingListenerAlive(IListener listener);
    void Use(IKeepListenersAlive childKeepListenersAlive);
    void StopUsing(IKeepListenersAlive childKeepListenersAlive);
}

internal static class KeepListenersAliveInternal
{
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
    ///     holds one flat list of roots. A call to the set visits each root one time,
    ///     whatever the number of operators between the stream and that root.
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

// The set of a root stream. The set counts each listener. A switch whose inner shares a root
// with its outer keeps a listener in the set of that root two times. A move to the next inner
// stops it one time. A set that the listener left at that stop lost the root that the outer
// still holds.
internal sealed class KeepListenersAliveImplementation : IKeepListenersAlive
{
    // ReSharper disable once CollectionNeverQueried.Local
    private List<IKeepListenersAlive>? childKeepListenersAliveList;

    // SodaFlow makes one of these for each root stream, and many streams get no
    // listener. Thus, the two collections wait until the code needs them.
    private Dictionary<IListener, int>? listeners;

    public void KeepListenerAlive(IListener listener)
    {
        this.listeners ??= [];
        this.listeners.TryGetValue(key: listener, value: out int count);
        this.listeners[listener] = count + 1;
    }

    public void StopKeepingListenerAlive(IListener listener)
    {
        if (this.listeners == null || !this.listeners.TryGetValue(key: listener, value: out int count))
        {
            return;
        }

        if (count == 1)
        {
            this.listeners.Remove(listener);
        }
        else
        {
            this.listeners[listener] = count - 1;
        }
    }

    public void Use(IKeepListenersAlive childKeepListenersAlive)
    {
        this.childKeepListenersAliveList ??= [];
        this.childKeepListenersAliveList.Add(childKeepListenersAlive);
    }

    // The list holds a child one time for each Use. Thus, a removal of one entry is the
    // reverse of one Use. A child that two members of a fan-out gave stays until both stop.
    public void StopUsing(IKeepListenersAlive childKeepListenersAlive) =>
        this.childKeepListenersAliveList?.Remove(childKeepListenersAlive);
}

// The set of a stream with more than one input: each call goes to the set of each input.
file sealed class FanOutKeepListenersAlive(in IKeepListenersAlive[] members) : IKeepListenersAlive
{
    internal IKeepListenersAlive[] Members { get; } = members;

    public void KeepListenerAlive(IListener listener)
    {
        foreach (IKeepListenersAlive member in this.Members)
        {
            lock (member)
            {
                member.KeepListenerAlive(listener);
            }
        }
    }

    public void StopKeepingListenerAlive(IListener listener)
    {
        foreach (IKeepListenersAlive member in this.Members)
        {
            lock (member)
            {
                member.StopKeepingListenerAlive(listener);
            }
        }
    }

    public void Use(IKeepListenersAlive childKeepListenersAlive)
    {
        foreach (IKeepListenersAlive member in this.Members)
        {
            lock (member)
            {
                member.Use(childKeepListenersAlive);
            }
        }
    }

    public void StopUsing(IKeepListenersAlive childKeepListenersAlive)
    {
        foreach (IKeepListenersAlive member in this.Members)
        {
            lock (member)
            {
                member.StopUsing(childKeepListenersAlive);
            }
        }
    }
}

// The set of a switch: the set of the outer cell, and the set of the inner stream or behavior
// that the switch selects now. Each switch moves the listeners and the children from the set of
// the previous inner to the set of the next. Thus, the root of the inner holds the listener
// while the switch selects that inner. When the outer is a constant, that is the only root that
// holds it. The switch keeps its own count of each listener. A switch of a switch keeps a
// listener here two times. The move must then stop it two times in the previous inner.
internal sealed class SwitchKeepListenersAlive(in IKeepListenersAlive outer) : IKeepListenersAlive
{
    private readonly IKeepListenersAlive outer = outer;
    private List<IKeepListenersAlive>? childKeepListenersAliveList;
    private IKeepListenersAlive? inner;
    private Dictionary<IListener, int>? listeners;

    // The outer or the inner can reach this set again through a loop. For example, a view model
    // can switch onto a stream that comes from its own output. A call that comes back here while
    // this set forwards stops, because this set already does its part of that call. Without this
    // stop, the call goes around the loop until the stack overflows.
    private bool forwarding;

    public void KeepListenerAlive(IListener listener)
    {
        if (this.forwarding)
        {
            return;
        }

        this.listeners ??= [];
        this.listeners.TryGetValue(key: listener, value: out int count);
        this.listeners[listener] = count + 1;
        this.Forward(call: static (set, value) => set.KeepListenerAlive(value), argument: listener);
    }

    public void StopKeepingListenerAlive(IListener listener)
    {
        if (this.forwarding
            || this.listeners == null
            || !this.listeners.TryGetValue(key: listener, value: out int count))
        {
            return;
        }

        if (count == 1)
        {
            this.listeners.Remove(listener);
        }
        else
        {
            this.listeners[listener] = count - 1;
        }

        this.Forward(call: static (set, value) => set.StopKeepingListenerAlive(value), argument: listener);
    }

    public void Use(IKeepListenersAlive childKeepListenersAlive)
    {
        if (this.forwarding)
        {
            return;
        }

        this.childKeepListenersAliveList ??= [];
        this.childKeepListenersAliveList.Add(childKeepListenersAlive);
        this.Forward(call: static (set, value) => set.Use(value), argument: childKeepListenersAlive);
    }

    public void StopUsing(IKeepListenersAlive childKeepListenersAlive)
    {
        if (this.forwarding
            || this.childKeepListenersAliveList == null
            || !this.childKeepListenersAliveList.Remove(childKeepListenersAlive))
        {
            return;
        }

        this.Forward(call: static (set, value) => set.StopUsing(value), argument: childKeepListenersAlive);
    }

    /// <summary>
    ///     Moves the listeners and the children of this set from the set of the
    ///     previous inner to the set of the next.
    /// </summary>
    /// <remarks>
    ///     The switch calls this when a transaction commits the inner that it selects.
    ///     A transaction that fails keeps the previous inner, thus it does not call
    ///     this.
    /// </remarks>
    internal void SetInner(IKeepListenersAlive next)
    {
        lock (this)
        {
            if (ReferenceEquals(objA: this.inner, objB: next))
            {
                return;
            }

            // The next inner can reach this set through a loop, as the previous inner can. The
            // calls that come back here while the move runs stop, as they do in Forward.
            this.forwarding = true;

            try
            {
                if (this.inner != null)
                {
                    this.Transfer(set: this.inner, keep: false);
                }

                this.inner = next;
                this.Transfer(set: next, keep: true);
            }
            finally
            {
                this.forwarding = false;
            }
        }
    }

    // Sends a call to the set of the outer and to the set of the current inner.
    private void Forward<T>(Action<IKeepListenersAlive, T> call, T argument)
    {
        this.forwarding = true;

        try
        {
            lock (this.outer)
            {
                call(arg1: this.outer, arg2: argument);
            }

            if (this.inner != null)
            {
                lock (this.inner)
                {
                    call(arg1: this.inner, arg2: argument);
                }
            }
        }
        finally
        {
            this.forwarding = false;
        }
    }

    private void Transfer(IKeepListenersAlive set, bool keep)
    {
        lock (set)
        {
            if (this.listeners != null)
            {
                foreach (KeyValuePair<IListener, int> entry in this.listeners)
                {
                    for (int i = 0; i < entry.Value; i++)
                    {
                        if (keep)
                        {
                            set.KeepListenerAlive(entry.Key);
                        }
                        else
                        {
                            set.StopKeepingListenerAlive(entry.Key);
                        }
                    }
                }
            }

            if (this.childKeepListenersAliveList != null)
            {
                foreach (IKeepListenersAlive child in this.childKeepListenersAliveList)
                {
                    if (keep)
                    {
                        set.Use(child);
                    }
                    else
                    {
                        set.StopUsing(child);
                    }
                }
            }
        }
    }
}
