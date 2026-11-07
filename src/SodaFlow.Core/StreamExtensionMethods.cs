using System;
using System.Collections.Generic;

namespace SodaFlow;

internal static class StreamExtensionMethodsInternal
{
    internal static Stream<T> OrElseImpl<T, T2>(this IEnumerable<T2> s)
        where T2 : Stream<T> =>
        s.MergeImpl<T, T2>(static (left, _) => left);

    internal static Stream<T> MergeImpl<T, T2>(this IEnumerable<T2> s, Func<T, T, T> f)
        where T2 : Stream<T>
    {
        IReadOnlyList<Stream<T>> v = [.. s];

        return TransactionInternal.Apply(
            code: static (trans, _, state) => MergeMany(trans: trans, inputs: state.V, f: state.F),
            state: (V: v, F: f));
    }

    /// <summary>
    ///     Merges the inputs through one node. Each input keeps its value of the
    ///     transaction in a slot at its own index. An <see cref="OnceGate" /> queues
    ///     one action at the output node at the first arrival. The action folds the
    ///     slots from the first index and sends one time.
    /// </summary>
    /// <remarks>
    ///     Before, this was a balanced tree of pairwise merges, with a coalesce
    ///     node for each pair. An input then crossed about log2 n nodes, and one
    ///     input crossed a merge with no partner. Here an input crosses one node,
    ///     whatever n is. The fold goes from the first index. Thus, the value of
    ///     the stream earlier in the list goes to the left of <paramref name="f" />,
    ///     whatever the sequence the inputs arrived in. An input that fires more
    ///     than one time in a transaction folds into its own slot first, in the
    ///     sequence of its firings. A coalesce did the same. A transaction that
    ///     fails clears the slots, thus a value from it does not go to the next one.
    ///     An empty slot is the default of the struct. The value of an input goes
    ///     into <see cref="MaybeInternal{T}" />, thus a null value and an empty
    ///     slot stay apart.
    /// </remarks>
    private static Stream<T> MergeMany<T>(TransactionInternal trans, IReadOnlyList<Stream<T>> inputs, Func<T, T, T> f)
    {
        switch (inputs.Count)
        {
            case 0:
                return new Stream<T>();
            case 1:
                return inputs[0];
            case 2:
                return MergeTwo<T>.Create(trans: trans, first: inputs[0], second: inputs[1], f: f);
        }

        IKeepListenersAlive[] roots = new IKeepListenersAlive[inputs.Count];

        for (int i = 0; i < inputs.Count; i++)
        {
            roots[i] = inputs[i].KeepListenersAlive;
        }

        Stream<T> @out = new(KeepListenersAliveInternal.Combine(roots));
        MaybeInternal<T>[] slots = new MaybeInternal<T>[inputs.Count];

        OnceGate gate =
            new(
                node: @out.Node,
                action: Combine,
                discard: () => Array.Clear(array: slots, index: 0, length: slots.Length));

        Stream<T> result = @out;

        for (int i = 0; i < inputs.Count; i++)
        {
            result =
                result.UnsafeAttachListener(
                    inputs[i]
                        .Listen(
                            target: gate.Node,
                            trans: trans,
                            action: new MergeInput<T>(slots: slots, index: i, f: f, gate: gate),
                            suppressEarlierFirings: false));
        }

        return result;

        void Combine(TransactionInternal trans3)
        {
            MaybeInternal<T> folded = MaybeInternal<T>.None;

            // ReSharper disable once ForCanBeConvertedToForeach - An index, thus the slot can be cleared in place.
            for (int index = 0; index < slots.Length; index++)
            {
                if (!slots[index].TryGetValue(out T held))
                {
                    continue;
                }

                slots[index] = MaybeInternal<T>.None;

                folded =
                    folded.TryGetValue(out T left)
                        ? MaybeInternal.Some(f(arg1: left, arg2: held))
                        : MaybeInternal.Some(held);
            }

            if (folded.TryGetValue(out T value))
            {
                @out.Send(trans: trans3, a: value);
            }
        }
    }

    internal static Stream<T> FilterSomeImpl<T, TMaybe>(this Stream<TMaybe> s, Action<TMaybe, Action<T>> matchSome)
    {
        Stream<T> @out = new(s.KeepListenersAlive);

        return @out.UnsafeAttachListener(
            s.Listen(target: @out.Node, action: new FilterSomeAction<T, TMaybe>(@out: @out, matchSome: matchSome)));
    }

    // This does not go through FilterSomeImpl. That method knows no optional type, thus it matches
    // through a delegate that gets a closure for each firing. MaybeInternal is this assembly's own
    // type, and TryGetValue reads it with no closure.
    internal static Stream<T> FilterSomeInternal<T>(this Stream<MaybeInternal<T>> s)
    {
        Stream<T> @out = new(s.KeepListenersAlive);

        return @out.UnsafeAttachListener(s.Listen(target: @out.Node, action: new FilterSomeInternalAction<T>(@out)));
    }

    /// <summary>
    ///     Maps and filters in one node. This is <c>s.MapImpl(f).FilterSomeImpl(matchSome)</c>
    ///     with one stream, one node, and one listener, and not two of each.
    /// </summary>
    /// <remarks>
    ///     A language surface gives its optional type through <paramref name="matchSome" />, as
    ///     for <see cref="FilterSomeImpl{T,TMaybe}" />.
    /// </remarks>
    internal static Stream<TResult> ChooseImpl<T, TResult, TMaybe>(
        this Stream<T> s,
        Func<T, TMaybe> f,
        Action<TMaybe, Action<TResult>> matchSome)
    {
        Stream<TResult> @out = new(s.KeepListenersAlive);

        return @out.UnsafeAttachListener(
            s.Listen(
                target: @out.Node,
                action: new ChooseAction<T, TResult, TMaybe>(@out: @out, f: f, matchSome: matchSome)));
    }

    /// <summary>
    ///     The same as <see cref="ChooseImpl{T,TResult,TMaybe}" />, for a function that gives a
    ///     <see cref="MaybeInternal{T}" />. It allocates nothing for a firing that gives no value.
    /// </summary>
    /// <remarks>
    ///     The per-key cells of a collection are a map and then a filter. The map gives a value
    ///     for one key and no value for each other key. One node for the two makes each such cell
    ///     allocate approximately one kilobyte less, which is one quarter of its cost.
    /// </remarks>
    internal static Stream<TResult> ChooseInternal<T, TResult>(this Stream<T> s, Func<T, MaybeInternal<TResult>> f)
    {
        Stream<TResult> @out = new(s.KeepListenersAlive);

        return @out.UnsafeAttachListener(
            s.Listen(target: @out.Node, action: new ChooseInternalAction<T, TResult>(@out: @out, f: f)));
    }

    // The actions of the links above. Each holds what its lambda captured, and is the listener
    // that the output attaches. See StreamAction.
    private sealed class MergeInput<T>(
        in MaybeInternal<T>[] slots,
        in int index,
        in Func<T, T, T> f,
        in OnceGate gate)
        : StreamAction<T>
    {
        private readonly Func<T, T, T> f = f;
        private readonly OnceGate gate = gate;
        private readonly int index = index;
        private readonly MaybeInternal<T>[] slots = slots;

        public override void Fire(TransactionInternal trans, T value)
        {
            this.slots[this.index] =
                this.slots[this.index].TryGetValue(out T earlier)
                    ? MaybeInternal.Some(this.f(arg1: earlier, arg2: value))
                    : MaybeInternal.Some(value);

            this.gate.Arrived(trans);
        }
    }

    private sealed class FilterSomeAction<T, TMaybe>(in Stream<T> @out, in Action<TMaybe, Action<T>> matchSome)
        : StreamAction<TMaybe>
    {
        private readonly Action<TMaybe, Action<T>> matchSome = matchSome;
        private readonly Stream<T> @out = @out;

        public override void Fire(TransactionInternal trans, TMaybe value) =>
            this.matchSome(arg1: value, arg2: v => this.@out.Send(trans: trans, a: v));
    }

    private sealed class FilterSomeInternalAction<T>(in Stream<T> @out) : StreamAction<MaybeInternal<T>>
    {
        private readonly Stream<T> @out = @out;

        public override void Fire(TransactionInternal trans, MaybeInternal<T> value)
        {
            if (value.TryGetValue(out T v))
            {
                this.@out.Send(trans: trans, a: v);
            }
        }
    }

    private sealed class ChooseAction<T, TResult, TMaybe>(
        in Stream<TResult> @out,
        in Func<T, TMaybe> f,
        in Action<TMaybe, Action<TResult>> matchSome)
        : StreamAction<T>
    {
        private readonly Func<T, TMaybe> f = f;
        private readonly Action<TMaybe, Action<TResult>> matchSome = matchSome;
        private readonly Stream<TResult> @out = @out;

        public override void Fire(TransactionInternal trans, T value) =>
            this.matchSome(arg1: this.f(value), arg2: v => this.@out.Send(trans: trans, a: v));
    }

    private sealed class ChooseInternalAction<T, TResult>(in Stream<TResult> @out, in Func<T, MaybeInternal<TResult>> f)
        : StreamAction<T>
    {
        private readonly Func<T, MaybeInternal<TResult>> f = f;
        private readonly Stream<TResult> @out = @out;

        public override void Fire(TransactionInternal trans, T value)
        {
            if (this.f(value).TryGetValue(out TResult v))
            {
                this.@out.Send(trans: trans, a: v);
            }
        }
    }

    /// <summary>
    ///     Fires the last value of a stream after a time with no other value. Each
    ///     firing moves the time of the alarm, thus a sequence of firings with no
    ///     space between them gives one value.
    /// </summary>
    /// <remarks>
    ///     The deadline is a loop: the alarm reads the cell, and the cell reads the
    ///     alarm. A firing in the transaction of the alarm arms the deadline again,
    ///     because <c>arm</c> is the left side of the OrElse. With <c>cleared</c> on
    ///     the left, that value never fires.
    ///     <para>
    ///         The alarm also clears the deadline, and that is a guard and not a
    ///         necessity. <c>at</c> sets a timer at each change of the cell, thus a
    ///         deadline that stays fires one time only. The guard is for an <c>arm</c>
    ///         that gives the same time two times: the cell then changes at each
    ///         firing, and the alarm comes. No test covers it, because the contract of
    ///         <c>arm</c> is a time after the time that it reads.
    ///     </para>
    ///     <para>
    ///         This method holds the time type and the deadline type as parameters,
    ///         and knows no optional type. Thus, each language surface gives its own:
    ///         <c>arm</c> makes the deadline that the timer system reads, and
    ///         <c>disarmed</c> is the value for no alarm.
    ///         See <c>FilterSomeImpl</c> above, which takes its optional type in the
    ///         same manner.
    ///     </para>
    ///     <para>
    ///         The cell of the last value is behind the stream by one transaction.
    ///         That costs nothing here, because the alarm comes in a transaction after
    ///         the firing that set it.
    ///     </para>
    /// </remarks>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <typeparam name="TTime">The type of a point in time.</typeparam>
    /// <typeparam name="TDeadline">
    ///     The type that the timer system reads, which can
    ///     hold no time.
    /// </typeparam>
    /// <param name="s">The stream to debounce.</param>
    /// <param name="time">The clock.</param>
    /// <param name="at">Makes a stream that fires at the time in a cell.</param>
    /// <param name="arm">Makes the deadline for a firing at a time.</param>
    /// <param name="disarmed">The deadline with no time in it.</param>
    /// <returns>A stream that fires the last value after a time with no other value.</returns>
    internal static Stream<T> DebounceImpl<T, TTime, TDeadline>(
        this Stream<T> s,
        Behavior<TTime> time,
        Func<Cell<TDeadline>, Stream<TTime>> at,
        Func<TTime, TDeadline> arm,
        TDeadline disarmed) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                Debounce(
                    trans: trans,
                    s: state.S,
                    time: state.Time,
                    at: state.At,
                    arm: state.Arm,
                    disarmed: state.Disarmed),
            state: (S: s, Time: time, At: at, Arm: arm, Disarmed: disarmed));

    private static Stream<T> Debounce<T, TTime, TDeadline>(
        TransactionInternal trans,
        Stream<T> s,
        Behavior<TTime> time,
        Func<Cell<TDeadline>, Stream<TTime>> at,
        Func<TTime, TDeadline> arm,
        TDeadline disarmed)
    {
        LoopedCell<TDeadline> deadline = new();
#pragma warning disable SF1003 // The function is the timer's At, which fires in a new transaction.
        Stream<TTime> alarm = at(deadline);
#pragma warning restore SF1003 // The function is the timer's At, which fires in a new transaction.

        // The value of each firing, for the alarm to read. This holds MaybeInternal and not
        // the optional type of a caller. The alarm comes after a firing, thus the first value of
        // this cell never reaches the result.
        Cell<MaybeInternal<T>> latest = s.MapImpl(MaybeInternal.Some).HoldImpl(MaybeInternal<T>.None);

        Stream<TDeadline> armed = s.SnapshotImpl(b: time, f: (_, now) => arm(now));
        Stream<TDeadline> cleared = alarm.MapToImpl(disarmed);

        deadline.Loop(trans: trans, c: armed.OrElseImpl(cleared).HoldImpl(disarmed));

        return alarm.SnapshotImpl(latest).FilterSomeInternal();
    }
}
