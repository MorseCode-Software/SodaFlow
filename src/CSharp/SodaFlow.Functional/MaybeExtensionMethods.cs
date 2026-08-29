using System.Collections.Generic;
using System.Linq;

namespace SodaFlow.Functional
{
    /// <summary>
    ///     The operations available on a <see cref="Maybe{T}" /> and on sequences of them.
    /// </summary>
    public static class MaybeExtensionMethods
    {
        /// <summary>
        ///     Collapses a nested <see cref="Maybe{T}" /> into a single one.
        /// </summary>
        /// <typeparam name="T">The type of the innermost value.</typeparam>
        /// <param name="a">The nested value.</param>
        /// <returns>
        ///     The inner <see cref="Maybe{T}" /> if the outer one has a value, and a
        ///     <see cref="Maybe{T}" /> containing no value otherwise.
        /// </returns>
        /// <remarks>
        ///     The two ways of having no value collapse to the same thing: an outer with no value and
        ///     an outer containing an inner with no value both give no value.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public static Maybe<T> Flatten<T>(this Maybe<Maybe<T>> a) => a.Bind(v => v);

        /// <summary>
        ///     Returns the values from a sequence which have one, discarding the entries which do not.
        /// </summary>
        /// <typeparam name="T">The type of the values in the sequence.</typeparam>
        /// <param name="o">The sequence to filter. A <see langword="null" /> sequence is treated as empty.</param>
        /// <returns>The contained values, in order, with the empty entries left out.</returns>
        /// <remarks>
        ///     Lazy, like the LINQ operators it is built from: the source is not enumerated until the
        ///     result is.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public static IEnumerable<T> WhereMaybe<T>(this IEnumerable<Maybe<T>> o) =>
            (o ?? new Maybe<T>[0])
            .Select(m => m.Match(v => new ValueAndHasValue<T>(v, true), () => new ValueAndHasValue<T>(default(T), false)))
            .Where(p => p.HasValue)
            .Select(p => p.Value);

        /// <summary>
        ///     Turns a sequence of possibly-absent values into a possibly-absent sequence of values,
        ///     which is present only if every entry was.
        /// </summary>
        /// <typeparam name="T">The type of the values in the sequence.</typeparam>
        /// <param name="o">The sequence to collect. A <see langword="null" /> sequence is treated as empty.</param>
        /// <returns>
        ///     All of the values if every entry had one, and no value if any entry did not. An empty
        ///     sequence gives an empty sequence rather than no value.
        /// </returns>
        /// <remarks>
        ///     Unlike <see cref="WhereMaybe{T}" />, this enumerates the source immediately, since
        ///     whether the result has a value at all cannot be known without reaching the end of it.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public static Maybe<IEnumerable<T>> AllMaybeOrNone<T>(this IEnumerable<Maybe<T>> o)
        {
            ValueAndHasValue<T>[] rr = (o ?? new Maybe<T>[0])
                .Select(m => m.Match(v => new ValueAndHasValue<T>(v, true), () => new ValueAndHasValue<T>(default(T), false)))
                .ToArray();

            return rr.Any(r => !r.HasValue) ? Maybe.None : Maybe.Some(rr.Select(r => r.Value));
        }

        private struct ValueAndHasValue<T>
        {
            public ValueAndHasValue(T value, bool hasValue)
            {
                this.Value = value;
                this.HasValue = hasValue;
            }

            public T Value { get; }
            public bool HasValue { get; }
        }
    }
}