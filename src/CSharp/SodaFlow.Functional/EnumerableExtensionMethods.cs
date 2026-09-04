using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace SodaFlow.Functional;

/// <summary>
///     The operations on an ordinary sequence which answer with a <see cref="Maybe{T}" /> rather
///     than with a default value or an exception.
/// </summary>
/// <remarks>
///     Each of these mirrors a LINQ operator whose <c>OrDefault</c> form cannot say whether it
///     found anything: <c>FirstOrDefault</c> over a sequence of <see cref="int" /> returns zero
///     both for an empty sequence and for one whose first element is zero. Answering with a
///     <see cref="Maybe{T}" /> keeps the two apart.
///     A <see langword="null" /> sequence is treated as empty throughout, matching the behavior
///     of <see cref="MaybeExtensionMethods" />.
/// </remarks>
[PublicAPI]
public static class EnumerableExtensionMethods
{
    /// <summary>
    ///     Applies a function which may produce no value to every element of a sequence, and
    ///     returns the results which were produced.
    /// </summary>
    /// <typeparam name="T">The type of the values in the sequence.</typeparam>
    /// <typeparam name="TResult">The type of the values the function produces.</typeparam>
    /// <param name="source">The sequence to map. A <see langword="null" /> sequence is treated as empty.</param>
    /// <param name="selector">Applied to each element in turn.</param>
    /// <returns>
    ///     The results for which <paramref name="selector" /> produced a value, in order, with the
    ///     elements it produced nothing for left out.
    /// </returns>
    /// <remarks>
    ///     Filtering and mapping in one step, for the common case where deciding whether to keep
    ///     an element is the same work as producing the value to keep - parsing, looking up,
    ///     narrowing a type. Written with LINQ alone that means either doing the work twice or
    ///     mapping to a <see cref="Maybe{T}" /> and unwrapping afterward, which is what this
    ///     does for you.
    ///     Lazy: neither the source nor <paramref name="selector" /> is touched until the result
    ///     is enumerated.
    ///     Use <see cref="MaybeExtensionMethods.AllSomeOrNone{T,TResult}" /> where a single
    ///     element producing nothing should fail the whole thing instead.
    /// </remarks>
    [Pure]
    public static IEnumerable<TResult> Choose<T, TResult>(
        this IEnumerable<T>? source,
        Func<T, Maybe<TResult>> selector) =>
        (source ?? []).Select(selector).WhereSome();

    /// <summary>
    ///     Applies a function which may produce no value to every element of a sequence along with
    ///     its index, and returns the results which were produced.
    /// </summary>
    /// <typeparam name="T">The type of the values in the sequence.</typeparam>
    /// <typeparam name="TResult">The type of the values the function produces.</typeparam>
    /// <param name="source">The sequence to map. A <see langword="null" /> sequence is treated as empty.</param>
    /// <param name="selector">
    ///     Applied to each element in turn, along with the index of that element in the source.
    /// </param>
    /// <returns>
    ///     The results for which <paramref name="selector" /> produced a value, in order, with the
    ///     elements it produced nothing for left out.
    /// </returns>
    /// <remarks>
    ///     The index is the position in the source, so it still counts the elements which produced
    ///     nothing.
    /// </remarks>
    [Pure]
    public static IEnumerable<TResult> Choose<T, TResult>(
        this IEnumerable<T>? source,
        Func<T, int, Maybe<TResult>> selector) =>
        (source ?? []).Select(selector).WhereSome();

    /// <summary>
    ///     Returns the first element of a sequence, if it has one.
    /// </summary>
    /// <typeparam name="T">The type of the values in the sequence.</typeparam>
    /// <param name="source">The sequence to read. A <see langword="null" /> sequence is treated as empty.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the first element, and one containing no value if
    ///     the sequence is empty.
    /// </returns>
    /// <remarks>
    ///     Reads at most one element of the source.
    /// </remarks>
    [Pure]
    public static Maybe<T> FirstOrNone<T>([InstantHandle] this IEnumerable<T>? source)
    {
        if (source == null)
        {
            return Maybe.None;
        }

        using IEnumerator<T> enumerator = source.GetEnumerator();

        return enumerator.MoveNext() ? Maybe.Some(enumerator.Current) : Maybe.None;
    }

    /// <summary>
    ///     Returns the first element of a sequence which satisfies a predicate, if there is one.
    /// </summary>
    /// <typeparam name="T">The type of the values in the sequence.</typeparam>
    /// <param name="source">The sequence to search. A <see langword="null" /> sequence is treated as empty.</param>
    /// <param name="predicate">The condition the element must satisfy.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the first matching element, and one containing no
    ///     value if there is none.
    /// </returns>
    /// <remarks>
    ///     Stops at the first match.
    /// </remarks>
    [Pure]
    public static Maybe<T> FirstOrNone<T>(
        [InstantHandle] this IEnumerable<T>? source,
        [InstantHandle] Func<T, bool> predicate) =>
        (source ?? []).Where(predicate).FirstOrNone();

    /// <summary>
    ///     Returns the last element of a sequence, if it has one.
    /// </summary>
    /// <typeparam name="T">The type of the values in the sequence.</typeparam>
    /// <param name="source">The sequence to read. A <see langword="null" /> sequence is treated as empty.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the last element, and one containing no value if
    ///     the sequence is empty.
    /// </returns>
    /// <remarks>
    ///     A sequence which can be indexed is indexed; anything else has to be enumerated to the
    ///     end to find out what the last element was.
    /// </remarks>
    [Pure]
    public static Maybe<T> LastOrNone<T>([InstantHandle] this IEnumerable<T>? source)
    {
        switch (source)
        {
            case null:
                return Maybe.None;
            case IReadOnlyList<T> list:
                return list.Count > 0
                    ? Maybe.Some(
                        list[
#if NET
                            ^1
#else
                            list.Count - 1
#endif
                        ])
                    : Maybe.None;
        }

        Maybe<T> result = Maybe.None;

        foreach (T item in source)
        {
            result = Maybe.Some(item);
        }

        return result;
    }

    /// <summary>
    ///     Returns the last element of a sequence which satisfies a predicate, if there is one.
    /// </summary>
    /// <typeparam name="T">The type of the values in the sequence.</typeparam>
    /// <param name="source">The sequence to search. A <see langword="null" /> sequence is treated as empty.</param>
    /// <param name="predicate">The condition the element must satisfy.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the last matching element, and one containing no
    ///     value if there is none.
    /// </returns>
    [Pure]
    public static Maybe<T> LastOrNone<T>(
        [InstantHandle] this IEnumerable<T>? source,
        [InstantHandle] Func<T, bool> predicate) =>
        (source ?? []).Where(predicate).LastOrNone();

    /// <summary>
    ///     Returns the only element of a sequence, if it has one.
    /// </summary>
    /// <typeparam name="T">The type of the values in the sequence.</typeparam>
    /// <param name="source">The sequence to read. A <see langword="null" /> sequence is treated as empty.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the single element, and one containing no value if
    ///     the sequence is empty.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     The sequence contains more than one element.
    /// </exception>
    /// <remarks>
    ///     This throws where <see cref="Enumerable.SingleOrDefault{T}(IEnumerable{T})" /> throws,
    ///     and for the same reason: a sequence with two elements has not failed to produce an
    ///     answer, it has contradicted the assumption that there was only ever one to produce.
    ///     Returning no value would hide that. Where more than one is expected and simply not
    ///     wanted, filter first, or use <see cref="FirstOrNone{T}(IEnumerable{T})" />.
    ///     Reads at most two elements of the source.
    /// </remarks>
    [Pure]
    public static Maybe<T> SingleOrNone<T>([InstantHandle] this IEnumerable<T>? source)
    {
        if (source == null)
        {
            return Maybe.None;
        }

        using IEnumerator<T> enumerator = source.GetEnumerator();

        if (!enumerator.MoveNext())
        {
            return Maybe.None;
        }

        T result = enumerator.Current;

        return enumerator.MoveNext()
            ? throw new InvalidOperationException("The sequence contains more than one element.")
            : Maybe.Some(result);
    }

    /// <summary>
    ///     Returns the only element of a sequence which satisfies a predicate, if there is one.
    /// </summary>
    /// <typeparam name="T">The type of the values in the sequence.</typeparam>
    /// <param name="source">The sequence to search. A <see langword="null" /> sequence is treated as empty.</param>
    /// <param name="predicate">The condition the element must satisfy.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the single matching element, and one containing no
    ///     value if there is none.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     More than one element satisfies <paramref name="predicate" />.
    /// </exception>
    [Pure]
    public static Maybe<T> SingleOrNone<T>(
        [InstantHandle] this IEnumerable<T>? source,
        [InstantHandle] Func<T, bool> predicate) =>
        (source ?? []).Where(predicate).SingleOrNone();

    /// <summary>
    ///     Combines the elements of a sequence with a function, if it has any.
    /// </summary>
    /// <typeparam name="T">The type of the values in the sequence.</typeparam>
    /// <param name="source">The sequence to fold. A <see langword="null" /> sequence is treated as empty.</param>
    /// <param name="f">
    ///     Applied to the result so far and the next element, starting with the first two elements.
    /// </param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the combined value, the single element if there
    ///     was only one, and no value if the sequence was empty.
    /// </returns>
    /// <remarks>
    ///     The seedless <see cref="Enumerable.Aggregate{TSource}(IEnumerable{TSource},Func{TSource,TSource,TSource})" />,
    ///     which throws on an empty sequence because it has nothing to return. There is no such
    ///     problem when the answer can say there was nothing to combine.
    ///     Where a seed exists,
    ///     <see
    ///         cref="Enumerable.Aggregate{TSource,TAccumulate}(IEnumerable{TSource},TAccumulate,Func{TAccumulate,TSource,TAccumulate})" />
    ///     is already total and is the one to use.
    /// </remarks>
    [Pure]
    public static Maybe<T> AggregateOrNone<T>(
        [InstantHandle] this IEnumerable<T>? source,
        [InstantHandle] Func<T, T, T> f)
    {
        if (source == null)
        {
            return Maybe.None;
        }

        bool any = false;
        T? accumulated = default;

        foreach (T item in source)
        {
            // ReSharper disable once NullableWarningSuppressionIsUsed - accumulated will be non-null when any is true.
            accumulated = any ? f(arg1: accumulated!, arg2: item) : item;
            any = true;
        }

        // ReSharper disable once NullableWarningSuppressionIsUsed - accumulated will be non-null when any is true.
        return Maybe.SomeIf(condition: any, value: accumulated!);
    }

    /// <summary>
    ///     Returns the smallest element of a sequence, if it has any.
    /// </summary>
    /// <typeparam name="T">The type of the values in the sequence.</typeparam>
    /// <param name="source">The sequence to search. A <see langword="null" /> sequence is treated as empty.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the smallest element by
    ///     <see cref="Comparer{T}.Default" />, and no value if there is nothing to compare.
    /// </returns>
    /// <remarks>
    ///     <see cref="Enumerable.Min{TSource}(IEnumerable{TSource})" /> throws on an empty sequence
    ///     of a non-nullable type, and returns <see langword="null" /> on one of a nullable type -
    ///     which is indistinguishable from a sequence whose smallest element is
    ///     <see langword="null" />. Neither is a good answer, and neither is needed once the
    ///     result can say there was nothing to compare.
    /// </remarks>
    [Pure]
    public static Maybe<T> MinOrNone<T>([InstantHandle] this IEnumerable<T>? source) => source.MinOrNone(null);

    /// <summary>
    ///     Returns the smallest element of a sequence by the given comparer, if it has any.
    /// </summary>
    /// <typeparam name="T">The type of the values in the sequence.</typeparam>
    /// <param name="source">The sequence to search. A <see langword="null" /> sequence is treated as empty.</param>
    /// <param name="comparer">
    ///     The ordering to use. <see langword="null" /> means <see cref="Comparer{T}.Default" />.
    /// </param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the smallest element, and no value if there is
    ///     nothing to compare.
    /// </returns>
    [Pure]
    public static Maybe<T> MinOrNone<T>(
        [InstantHandle] this IEnumerable<T>? source,
        IComparer<T>? comparer) =>
        source.ExtremeOrNone(comparer: comparer, wantLarger: false);

    /// <summary>
    ///     Returns the smallest value produced from a sequence, if it has any elements.
    /// </summary>
    /// <typeparam name="T">The type of the values in the sequence.</typeparam>
    /// <typeparam name="TResult">The type of the values to compare.</typeparam>
    /// <param name="source">The sequence to search. A <see langword="null" /> sequence is treated as empty.</param>
    /// <param name="selector">Applied to each element to produce the value to compare.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the smallest produced value by
    ///     <see cref="Comparer{T}.Default" />, and no value if there is nothing to compare.
    /// </returns>
    /// <remarks>
    ///     This returns the smallest value <paramref name="selector" /> produced, not the element
    ///     which produced it. For the element, order by the key and take
    ///     <see cref="FirstOrNone{T}(IEnumerable{T})" />.
    /// </remarks>
    [Pure]
    public static Maybe<TResult> MinOrNone<T, TResult>(
        [InstantHandle] this IEnumerable<T>? source,
        [InstantHandle] Func<T, TResult> selector) =>
        (source ?? []).Select(selector).MinOrNone();

    /// <summary>
    ///     Returns the largest element of a sequence, if it has any.
    /// </summary>
    /// <typeparam name="T">The type of the values in the sequence.</typeparam>
    /// <param name="source">The sequence to search. A <see langword="null" /> sequence is treated as empty.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the largest element by
    ///     <see cref="Comparer{T}.Default" />, and no value if there is nothing to compare.
    /// </returns>
    /// <remarks>
    ///     See <see cref="MinOrNone{T}(IEnumerable{T})" /> for why this exists.
    /// </remarks>
    [Pure]
    public static Maybe<T> MaxOrNone<T>([InstantHandle] this IEnumerable<T>? source) => source.MaxOrNone(null);

    /// <summary>
    ///     Returns the largest element of a sequence by the given comparer, if it has any.
    /// </summary>
    /// <typeparam name="T">The type of the values in the sequence.</typeparam>
    /// <param name="source">The sequence to search. A <see langword="null" /> sequence is treated as empty.</param>
    /// <param name="comparer">
    ///     The ordering to use. <see langword="null" /> means <see cref="Comparer{T}.Default" />.
    /// </param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the largest element, and no value if there is
    ///     nothing to compare.
    /// </returns>
    [Pure]
    public static Maybe<T> MaxOrNone<T>(
        [InstantHandle] this IEnumerable<T>? source,
        IComparer<T>? comparer) =>
        source.ExtremeOrNone(comparer: comparer, wantLarger: true);

    /// <summary>
    ///     Returns the largest value produced from a sequence, if it has any elements.
    /// </summary>
    /// <typeparam name="T">The type of the values in the sequence.</typeparam>
    /// <typeparam name="TResult">The type of the values to compare.</typeparam>
    /// <param name="source">The sequence to search. A <see langword="null" /> sequence is treated as empty.</param>
    /// <param name="selector">Applied to each element to produce the value to compare.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the largest produced value by
    ///     <see cref="Comparer{T}.Default" />, and no value if there is nothing to compare.
    /// </returns>
    /// <remarks>
    ///     This returns the largest value <paramref name="selector" /> produced, not the element
    ///     which produced it.
    /// </remarks>
    [Pure]
    public static Maybe<TResult> MaxOrNone<T, TResult>(
        [InstantHandle] this IEnumerable<T>? source,
        [InstantHandle] Func<T, TResult> selector) =>
        (source ?? []).Select(selector).MaxOrNone();

    /// <summary>
    ///     Returns the element at a given position in a sequence, if there is one there.
    /// </summary>
    /// <typeparam name="T">The type of the values in the sequence.</typeparam>
    /// <param name="source">The sequence to read. A <see langword="null" /> sequence is treated as empty.</param>
    /// <param name="index">The zero-based position of the element to return.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the element at <paramref name="index" />, and one
    ///     containing no value if the sequence is shorter than that or
    ///     <paramref name="index" /> is negative.
    /// </returns>
    /// <remarks>
    ///     A negative index gives no value rather than throwing, which is where this differs from
    ///     <see cref="Enumerable.ElementAt{T}(IEnumerable{T},int)" /> and agrees with
    ///     <see cref="Enumerable.ElementAtOrDefault{T}(IEnumerable{T},int)" />.
    ///     A sequence which can be indexed is indexed; anything else is enumerated up to
    ///     <paramref name="index" /> and no further.
    /// </remarks>
    [Pure]
    public static Maybe<T> ElementAtOrNone<T>(
        [InstantHandle] this IEnumerable<T>? source,
        int index)
    {
        if (source == null || index < 0)
        {
            return Maybe.None;
        }

        if (source is IReadOnlyList<T> list)
        {
            return index < list.Count ? Maybe.Some(list[index]) : Maybe.None;
        }

        using IEnumerator<T> enumerator = source.GetEnumerator();

        for (int i = 0; i <= index; i++)
        {
            if (!enumerator.MoveNext())
            {
                return Maybe.None;
            }
        }

        return Maybe.Some(enumerator.Current);
    }

    /// <summary>
    ///     The shared implementation of <see cref="MinOrNone{T}(IEnumerable{T},IComparer{T})" />
    ///     and <see cref="MaxOrNone{T}(IEnumerable{T},IComparer{T})" />.
    /// </summary>
    /// <remarks>
    ///     Elements which are <see langword="null" /> are skipped, which is what
    ///     <see cref="Enumerable.Min{TSource}(IEnumerable{TSource})" /> does and is almost never
    ///     what <see cref="Comparer{T}.Default" /> would do - it sorts <see langword="null" />
    ///     before everything, so a single null element would otherwise be the answer for every
    ///     sequence of a reference type. A sequence of nothing but nulls therefore has nothing to
    ///     compare and gives no value, where LINQ would give <see langword="null" />.
    ///     The test is only made for types which can actually hold one; for a non-nullable value
    ///     type the branch is never taken and nothing is boxed.
    /// </remarks>
    private static Maybe<T> ExtremeOrNone<T>(this IEnumerable<T>? source, IComparer<T>? comparer, bool wantLarger)
    {
        if (source == null)
        {
            return Maybe.None;
        }

        IComparer<T> c = comparer ?? Comparer<T>.Default;
        bool canBeNull = !typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) != null;

        bool any = false;
        T? best = default;

        // ReSharper disable once LoopCanBePartlyConvertedToQuery - Done for performance reasons.
        foreach (T item in source)
        {
            if (canBeNull && item is null)
            {
                continue;
            }

            if (!any)
            {
                best = item;
                any = true;
                continue;
            }

            // ReSharper disable once NullableWarningSuppressionIsUsed - If we have gotten to this point, any has been
            // set to true and best has a non-null value.
            int comparison = c.Compare(x: item, y: best!);

            if (wantLarger ? comparison > 0 : comparison < 0)
            {
                best = item;
            }
        }

        // ReSharper disable once NullableWarningSuppressionIsUsed - If any is true, then best has a non-null value.
        return Maybe.SomeIf(condition: any, value: best!);
    }
}
