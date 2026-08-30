using System;
using System.Collections.Generic;
using System.Linq;

namespace SodaFlow.Functional
{
    /// <summary>
    ///     The operations on an ordinary sequence which answer with a <see cref="Maybe{T}" /> rather
    ///     than with a default value or an exception.
    /// </summary>
    /// <remarks>
    ///     Each of these mirrors a LINQ operator whose <c>OrDefault</c> form cannot say whether it
    ///     found anything: <c>FirstOrDefault</c> over a sequence of <see cref="int" /> returns zero
    ///     both for an empty sequence and for one whose first element is zero. Answering with a
    ///     <see cref="Maybe{T}" /> keeps the two apart.
    ///
    ///     A <see langword="null" /> sequence is treated as empty throughout, matching the behavior
    ///     of <see cref="MaybeExtensionMethods" />.
    /// </remarks>
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
        ///     mapping to a <see cref="Maybe{T}" /> and unwrapping afterwards, which is what this
        ///     does for you.
        ///
        ///     Lazy: neither the source nor <paramref name="selector" /> is touched until the result
        ///     is enumerated.
        ///
        ///     Use <see cref="MaybeExtensionMethods.AllSomeOrNone{T,TResult}" /> where a single
        ///     element producing nothing should fail the whole thing instead.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public static IEnumerable<TResult> Choose<T, TResult>(
            this IEnumerable<T> source,
            Func<T, Maybe<TResult>> selector) =>
            (source ?? new T[0]).Select(selector).WhereSome();

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
        [JetBrains.Annotations.Pure]
        public static IEnumerable<TResult> Choose<T, TResult>(
            this IEnumerable<T> source,
            Func<T, int, Maybe<TResult>> selector) =>
            (source ?? new T[0]).Select(selector).WhereSome();

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
        [JetBrains.Annotations.Pure]
        public static Maybe<T> FirstOrNone<T>([JetBrains.Annotations.InstantHandle] this IEnumerable<T> source)
        {
            if (source == null)
            {
                return Maybe.None;
            }

            using (IEnumerator<T> enumerator = source.GetEnumerator())
            {
                return enumerator.MoveNext() ? Maybe.Some(enumerator.Current) : Maybe.None;
            }
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
        [JetBrains.Annotations.Pure]
        public static Maybe<T> FirstOrNone<T>(
            [JetBrains.Annotations.InstantHandle] this IEnumerable<T> source,
            [JetBrains.Annotations.InstantHandle] Func<T, bool> predicate) =>
            (source ?? new T[0]).Where(predicate).FirstOrNone();

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
        [JetBrains.Annotations.Pure]
        public static Maybe<T> LastOrNone<T>([JetBrains.Annotations.InstantHandle] this IEnumerable<T> source)
        {
            if (source == null)
            {
                return Maybe.None;
            }

            IReadOnlyList<T> list = source as IReadOnlyList<T>;
            if (list != null)
            {
                return list.Count > 0 ? Maybe.Some(list[list.Count - 1]) : Maybe.None;
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
        [JetBrains.Annotations.Pure]
        public static Maybe<T> LastOrNone<T>(
            [JetBrains.Annotations.InstantHandle] this IEnumerable<T> source,
            [JetBrains.Annotations.InstantHandle] Func<T, bool> predicate) =>
            (source ?? new T[0]).Where(predicate).LastOrNone();

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
        ///
        ///     Reads at most two elements of the source.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public static Maybe<T> SingleOrNone<T>([JetBrains.Annotations.InstantHandle] this IEnumerable<T> source)
        {
            if (source == null)
            {
                return Maybe.None;
            }

            using (IEnumerator<T> enumerator = source.GetEnumerator())
            {
                if (!enumerator.MoveNext())
                {
                    return Maybe.None;
                }

                T result = enumerator.Current;

                if (enumerator.MoveNext())
                {
                    throw new InvalidOperationException("The sequence contains more than one element.");
                }

                return Maybe.Some(result);
            }
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
        [JetBrains.Annotations.Pure]
        public static Maybe<T> SingleOrNone<T>(
            [JetBrains.Annotations.InstantHandle] this IEnumerable<T> source,
            [JetBrains.Annotations.InstantHandle] Func<T, bool> predicate) =>
            (source ?? new T[0]).Where(predicate).SingleOrNone();

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
        ///
        ///     A sequence which can be indexed is indexed; anything else is enumerated up to
        ///     <paramref name="index" /> and no further.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public static Maybe<T> ElementAtOrNone<T>(
            [JetBrains.Annotations.InstantHandle] this IEnumerable<T> source,
            int index)
        {
            if (source == null || index < 0)
            {
                return Maybe.None;
            }

            IReadOnlyList<T> list = source as IReadOnlyList<T>;
            if (list != null)
            {
                return index < list.Count ? Maybe.Some(list[index]) : Maybe.None;
            }

            using (IEnumerator<T> enumerator = source.GetEnumerator())
            {
                for (int i = 0; i <= index; i++)
                {
                    if (!enumerator.MoveNext())
                    {
                        return Maybe.None;
                    }
                }

                return Maybe.Some(enumerator.Current);
            }
        }
    }
}
