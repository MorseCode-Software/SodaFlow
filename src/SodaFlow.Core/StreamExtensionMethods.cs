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
        return TransactionInternal.Apply((trans, _) => Merge(trans: trans, e: v, start: 0, end: v.Count, f: f));
    }

    private static Stream<T> Merge<T>(
        TransactionInternal trans,
        IReadOnlyList<Stream<T>> e,
        int start,
        int end,
        Func<T, T, T> f)
    {
        int n = end - start;

        switch (n)
        {
            case 0:
                return new Stream<T>();
            case 1:
                return e[start];
            case 2:
                return e[start].Merge(trans: trans, s: e[start + 1], f: f);
            default:
            {
                int mid = (start + end) / 2;

                return Merge(trans: trans, e: e, start: start, end: mid, f: f)
                    .Merge(trans: trans, s: Merge(trans: trans, e: e, start: mid, end: end, f: f), f: f);
            }
        }
    }

    internal static Stream<T> FilterSomeImpl<T, TMaybe>(this Stream<TMaybe> s, Action<TMaybe, Action<T>> matchSome)
    {
        Stream<T> @out = new(s.KeepListenersAlive);

        IListener l =
            s.Listen(
                target: @out.Node,
                action: (trans2, a) => matchSome(arg1: a, arg2: v => @out.Send(trans: trans2, a: v)));

        return @out.UnsafeAttachListener(l);
    }

    internal static Stream<T> FilterSomeInternal<T>(this Stream<MaybeInternal<T>> s) =>
        s.FilterSomeImpl<T, MaybeInternal<T>>(static (m, a) => m.MatchSome(a));

    /// <summary>
    ///     Fires the last value of a stream after a time with no other value. Each firing moves the
    ///     time of the alarm, thus a sequence of firings with no space between them gives one value.
    /// </summary>
    /// <remarks>
    ///     The deadline is a loop: the alarm reads the cell, and the cell reads the alarm. A firing
    ///     in the transaction of the alarm arms the deadline again, because <c>arm</c> is the left
    ///     side of the OrElse. With <c>cleared</c> on the left, that value never fires.
    ///     <para>
    ///         The alarm also clears the deadline, and that is a guard and not a necessity. <c>at</c>
    ///         sets a timer at each change of the cell, thus a deadline that stays fires one time
    ///         only. The guard is for an <c>arm</c> that gives the same time two times: the cell then
    ///         changes at each firing, and the alarm comes. No test covers it, because the contract
    ///         of <c>arm</c> is a time after the time that it reads.
    ///     </para>
    ///     <para>
    ///         This method holds the time type and the deadline type as parameters, and knows no
    ///         optional type. Thus, each language surface gives its own: <c>arm</c> makes the
    ///         deadline that the timer system reads, and <c>disarmed</c> is the value for no alarm.
    ///         See <c>FilterSomeImpl</c> above, which takes its optional type in the same manner.
    ///     </para>
    ///     <para>
    ///         The cell of the last value is behind the stream by one transaction. That costs
    ///         nothing here, because the alarm comes in a transaction after the firing that set
    ///         it.
    ///     </para>
    /// </remarks>
    /// <typeparam name="T">The type of the stream.</typeparam>
    /// <typeparam name="TTime">The type of a point in time.</typeparam>
    /// <typeparam name="TDeadline">The type that the timer system reads, which can hold no time.</typeparam>
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
        TransactionInternal.Apply((trans, _) =>
        {
            LoopedCell<TDeadline> deadline = new();
            Stream<TTime> alarm = at(deadline);

            // The value of each firing, for the alarm to read. This holds MaybeInternal and not
            // the optional type of a caller. The alarm comes after a firing, thus the first value of
            // this cell never reaches the result.
            Cell<MaybeInternal<T>> latest = s.MapImpl(MaybeInternal.Some).HoldImpl(MaybeInternal<T>.None);

            Stream<TDeadline> armed = s.SnapshotImpl(b: time, f: (_, now) => arm(now));
            Stream<TDeadline> cleared = alarm.MapToImpl(disarmed);

            deadline.Loop(trans: trans, c: armed.OrElseImpl(cleared).HoldImpl(disarmed));

            return alarm.SnapshotImpl(latest).FilterSomeInternal();
        });
}
