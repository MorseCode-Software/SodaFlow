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
}
