using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace SodaFlow.Functional;

/// <summary>
///     The operations available on a <see cref="Maybe{T}" /> and on sequences of them.
/// </summary>
[PublicAPI]
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
    ///     The two conditions with no value give the same result: an outer with no value and
    ///     an outer containing an inner with no value the two give no value.
    /// </remarks>
    [Pure]
    public static Maybe<T> Flatten<T>(this Maybe<Maybe<T>> a) => a.Bind(static v => v);

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
    [Pure]
    public static IEnumerable<T> WhereSome<T>(this IEnumerable<Maybe<T>>? o) =>
        (o ?? [])
        .Select(static m =>
            m.Match(
                onSome: static v => new ValueAndHasValue<T>(value: v, hasValue: true),
                onNone: static () => new ValueAndHasValue<T>(value: default, hasValue: false)))
        .Where(static p => p.HasValue)
        // ReSharper disable once NullableWarningSuppressionIsUsed - If HasValue is true, then Value will have a
        // non-null value.
        .Select(static p => p.Value!);

    /// <summary>
    ///     Turns a sequence of values that can be missing into a sequence that can be missing,
    ///     which has a value only when each entry was.
    /// </summary>
    /// <typeparam name="T">The type of the values in the sequence.</typeparam>
    /// <param name="o">The sequence to collect. A <see langword="null" /> sequence is treated as empty.</param>
    /// <returns>
    ///     All the values if each entry had one, and no value if any entry did not. An empty
    ///     sequence gives an empty sequence rather than no value.
    /// </returns>
    /// <remarks>
    ///     Unlike <see cref="WhereSome{T}" />, this enumerates the source immediately, since
    ///     no code can know if the result has a value without a read to the end of it.
    /// </remarks>
    [Pure]
    public static Maybe<IEnumerable<T>> AllSomeOrNone<T>(this IEnumerable<Maybe<T>>? o)
    {
        ValueAndHasValue<T>[] rr =
        [
            .. (o ?? [])
            .Select(static m =>
                m.Match(
                    onSome: static v => new ValueAndHasValue<T>(value: v, hasValue: true),
                    onNone: static () => new ValueAndHasValue<T>(value: default, hasValue: false)))
        ];

        // ReSharper disable once NullableWarningSuppressionIsUsed - If HasValue is true, then Value will have a
        // non-null value.
        return rr.Any(static r => !r.HasValue) ? Maybe.None : Maybe.Some(rr.Select(static r => r.Value!));
    }

    /// <summary>
    ///     Applies a function which can give no value to each element of a sequence, and
    ///     collects the results only if all of them were produced.
    /// </summary>
    /// <typeparam name="T">The type of the values in the sequence.</typeparam>
    /// <typeparam name="TResult">The type of the values the function produces.</typeparam>
    /// <param name="o">The sequence to map. A <see langword="null" /> sequence is treated as empty.</param>
    /// <param name="f">Applied to each element in turn.</param>
    /// <returns>
    ///     All the results if <paramref name="f" /> produced a value for each element, and no
    ///     value if it did not give one for any. An empty sequence gives an empty sequence
    ///     rather than no value.
    /// </returns>
    /// <remarks>
    ///     This is the all-or-nothing counterpart to
    ///     <see cref="EnumerableExtensionMethods.Choose{T,TResult}(IEnumerable{T},Func{T,Maybe{TResult}})" />,
    ///     which keeps whatever it can get and discards the others. Use this one where a single
    ///     element failing means the full result is meaningless - parsing a file of numbers,
    ///     say, rather than picking the numbers out of a file of mixed lines.
    ///     Like the other overload this enumerates the source immediately. <paramref name="f" />
    ///     is applied to each element after one has given no value, thus it must not be dependent
    ///     on a stop before the end.
    /// </remarks>
    [Pure]
    public static Maybe<IEnumerable<TResult>> AllSomeOrNone<T, TResult>(
        this IEnumerable<T>? o,
        [InstantHandle] Func<T, Maybe<TResult>> f) =>
        (o ?? []).Select(f).AllSomeOrNone();

    /// <summary>
    ///     Views a <see cref="Maybe{T}" /> as a sequence of one element, or of no elements.
    /// </summary>
    /// <typeparam name="T">The type of the value, when there is one.</typeparam>
    /// <param name="a">The value to view as a sequence.</param>
    /// <returns>
    ///     A sequence that holds only the contained value if there is one, and an empty sequence
    ///     otherwise.
    /// </returns>
    /// <remarks>
    ///     This is what lets a <see cref="Maybe{T}" /> be fed to anything which takes a sequence -
    ///     and <c>SelectMany</c>, where it removes entries with no value in the same
    ///     step that produces them.
    /// </remarks>
    [Pure]
    public static IEnumerable<T> ToEnumerable<T>(this Maybe<T> a) =>
        a.Match(onSome: static v => [v], onNone: static () => Array.Empty<T>());

    /// <summary>
    ///     Changes a <see cref="Maybe{T}" /> of a value type into a
    ///     <see cref="System.Nullable{T}" />.
    /// </summary>
    /// <typeparam name="T">The type of the value, when there is one.</typeparam>
    /// <param name="a">The value to change.</param>
    /// <returns>
    ///     A <see cref="System.Nullable{T}" /> holding the contained value if there is one, and
    ///     <see langword="null" /> otherwise.
    /// </returns>
    /// <remarks>
    ///     For handing a value to an API which speaks in nullables.
    ///     <see cref="Maybe.SomeNotNull{T}(System.Nullable{T})" /> changes
    ///     back.
    /// </remarks>
    [Pure]
    public static T? ToNullable<T>(this Maybe<T> a)
        where T : struct =>
        a.Match(onSome: static v => (T?)v, onNone: static () => null);

    /// <summary>
    ///     Changes a reference which can be <see langword="null" /> into a <see cref="Maybe{T}" />.
    /// </summary>
    /// <typeparam name="T">The type of the reference.</typeparam>
    /// <param name="value">The reference to change.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing <paramref name="value" /> if it is not
    ///     <see langword="null" />, and one containing no value otherwise.
    /// </returns>
    /// <remarks>
    ///     The same thing as <see cref="Maybe.SomeNotNull{T}(T)" />, in the position which reads
    ///     better at the end of a chain.
    /// </remarks>
    [Pure]
    public static Maybe<T> ToMaybe<T>(this T? value)
        where T : class =>
        Maybe.SomeNotNull(value);

    /// <summary>
    ///     Changes a <see cref="System.Nullable{T}" /> into a <see cref="Maybe{T}" />.
    /// </summary>
    /// <typeparam name="T">The underlying type of <paramref name="value" />.</typeparam>
    /// <param name="value">The nullable value to change.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the value of <paramref name="value" /> if it has
    ///     one, and one containing no value otherwise.
    /// </returns>
    /// <remarks>
    ///     The same thing as <see cref="Maybe.SomeNotNull{T}(System.Nullable{T})" />, in the position which reads
    ///     better at the end of a chain. <see cref="ToNullable{T}" /> changes back.
    /// </remarks>
    [Pure]
    public static Maybe<T> ToMaybe<T>(this T? value)
        where T : struct =>
        Maybe.SomeNotNull(value);

    /// <summary>
    ///     Returns the contained value if there is one, and the given value otherwise.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="a">The value to read.</param>
    /// <param name="defaultValue">Returned when <paramref name="a" /> contains no value.</param>
    /// <returns>
    ///     The contained value, or <paramref name="defaultValue" /> if there is none.
    /// </returns>
    /// <remarks>
    ///     <paramref name="defaultValue" /> is evaluated in each condition, because it is an argument. Where
    ///     that is not wanted, use <see cref="ValueOr{T}(Maybe{T},Func{T})" />.
    /// </remarks>
    [Pure]
    public static T ValueOr<T>(this Maybe<T> a, T defaultValue) =>
        a.Match(onSome: static v => v, onNone: () => defaultValue);

    /// <summary>
    ///     Returns the contained value if there is one, and the result of the given function
    ///     otherwise.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="a">The value to read.</param>
    /// <param name="defaultValueFactory">Run to give a value when <paramref name="a" /> contains none.</param>
    /// <returns>
    ///     The contained value, or the result of <paramref name="defaultValueFactory" /> if there
    ///     is none.
    /// </returns>
    /// <remarks>
    ///     <paramref name="defaultValueFactory" /> is run only when there is no contained value.
    /// </remarks>
    [Pure]
    public static T ValueOr<T>(
        this Maybe<T> a,
        [InstantHandle] Func<T> defaultValueFactory) =>
        a.Match(onSome: static v => v, onNone: defaultValueFactory);

    /// <summary>
    ///     Returns the contained value if there is one, and the default for its type otherwise.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="a">The value to read.</param>
    /// <returns>
    ///     The contained value, or <see langword="default" /> if there is none.
    /// </returns>
    /// <remarks>
    ///     This is the one helper here which cannot tell you which case you got: for a type whose
    ///     default is itself a legitimate value - zero, <see langword="false" />,
    ///     <see langword="null" /> - the answer is ambiguous. Use it only where that
    ///     genuinely has no effect.
    /// </remarks>
    [Pure]
    public static T? ValueOrDefault<T>(this Maybe<T> a) =>
        a.Match<T?>(onSome: static v => v, onNone: static () => default);

    /// <summary>
    ///     Returns the contained value if there is one, and throws the exception produced by the
    ///     given function otherwise.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="a">The value to read.</param>
    /// <param name="onNone">Run to give the exception to throw when there is no value.</param>
    /// <returns>The contained value.</returns>
    /// <exception cref="Exception">
    ///     Whatever <paramref name="onNone" /> produced, when there is no contained value.
    /// </exception>
    /// <remarks>
    ///     The deliberate exit, for the boundary where the absence of a value really is a
    ///     failure - a necessary configuration value, for example. It makes the caller answer for
    ///     the empty condition, by making them say what the failure is.
    /// </remarks>
    public static T ValueOrThrow<T>(
        this Maybe<T> a,
        [InstantHandle] Func<Exception> onNone) =>
        a.Match<T>(onSome: static v => v, onNone: () => throw onNone());

    /// <summary>
    ///     Returns this value if it has one, and the given value otherwise.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="a">The value to prefer.</param>
    /// <param name="b">Returned when <paramref name="a" /> contains no value.</param>
    /// <returns>
    ///     <paramref name="a" /> if it contains a value, and <paramref name="b" /> otherwise.
    /// </returns>
    /// <remarks>
    ///     Chained, this is a list of fallbacks: the first source which has an answer wins, and
    ///     the result has no value only if none of them did.
    /// </remarks>
    [Pure]
    public static Maybe<T> OrElse<T>(this Maybe<T> a, Maybe<T> b) => a.Match(onSome: _ => a, onNone: () => b);

    /// <summary>
    ///     Returns this value if it has one, and the result of the given function otherwise.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="a">The value to prefer.</param>
    /// <param name="b">Run to give a fallback when <paramref name="a" /> contains no value.</param>
    /// <returns>
    ///     <paramref name="a" /> if it contains a value, and the result of <paramref name="b" />
    ///     otherwise.
    /// </returns>
    /// <remarks>
    ///     <paramref name="b" /> is run only when <paramref name="a" /> has no value, so this is
    ///     the version to use when a read of the fallback costs something - a second lookup, a
    ///     second parse.
    /// </remarks>
    [Pure]
    public static Maybe<T> OrElse<T>(
        this Maybe<T> a,
        [InstantHandle] Func<Maybe<T>> b) =>
        a.Match(onSome: _ => a, onNone: b);

    /// <summary>
    ///     Lift a binary function into values that can be missing, so the result has a value only when
    ///     the two inputs were.
    /// </summary>
    /// <typeparam name="T1">The type of the first value.</typeparam>
    /// <typeparam name="T2">The type of the second value.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="a">The first value.</param>
    /// <param name="b">The second value.</param>
    /// <param name="f">Applied to the two contained values when the two have values.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the result of <paramref name="f" /> if the two
    ///     <paramref name="a" /> and <paramref name="b" /> contain values, and one containing no
    ///     value otherwise.
    /// </returns>
    /// <remarks>
    ///     <paramref name="f" /> is run only when each input has a value, which is what makes this
    ///     the procedure to put some parsed or looked-up values together, with no <c>Match</c> for each input.
    /// </remarks>
    [Pure]
    public static Maybe<TResult> Lift<T1, T2, TResult>(
        this Maybe<T1> a,
        Maybe<T2> b,
        [InstantHandle] Func<T1, T2, TResult> f) =>
        a.Bind(v1 => b.Map(v2 => f(arg1: v1, arg2: v2)));

    /// <summary>
    ///     Lift a ternary function into values that can be missing, so the result has a value only when
    ///     all three inputs were.
    /// </summary>
    /// <typeparam name="T1">The type of the first value.</typeparam>
    /// <typeparam name="T2">The type of the second value.</typeparam>
    /// <typeparam name="T3">The type of the third value.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="a">The first value.</param>
    /// <param name="b">The second value.</param>
    /// <param name="c">The third value.</param>
    /// <param name="f">Applied to the three contained values when each one has a value.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the result of <paramref name="f" /> if
    ///     <paramref name="a" />, <paramref name="b" /> and <paramref name="c" /> all contain
    ///     values, and one containing no value otherwise.
    /// </returns>
    [Pure]
    public static Maybe<TResult> Lift<T1, T2, T3, TResult>(
        this Maybe<T1> a,
        Maybe<T2> b,
        Maybe<T3> c,
        [InstantHandle] Func<T1, T2, T3, TResult> f) =>
        a.Bind(v1 => b.Bind(v2 => c.Map(v3 => f(arg1: v1, arg2: v2, arg3: v3))));

    /// <summary>
    ///     Lift a quaternary function into values that can be missing, so the result has a value only when
    ///     all four inputs were.
    /// </summary>
    /// <typeparam name="T1">The type of the first value.</typeparam>
    /// <typeparam name="T2">The type of the second value.</typeparam>
    /// <typeparam name="T3">The type of the third value.</typeparam>
    /// <typeparam name="T4">The type of the fourth value.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="a">The first value.</param>
    /// <param name="b">The second value.</param>
    /// <param name="c">The third value.</param>
    /// <param name="d">The fourth value.</param>
    /// <param name="f">Applied to the four contained values when each one has a value.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the result of <paramref name="f" /> if
    ///     <paramref name="a" />, <paramref name="b" />, <paramref name="c" /> and
    ///     <paramref name="d" /> all contain values, and one containing no value otherwise.
    /// </returns>
    [Pure]
    public static Maybe<TResult> Lift<T1, T2, T3, T4, TResult>(
        this Maybe<T1> a,
        Maybe<T2> b,
        Maybe<T3> c,
        Maybe<T4> d,
        [InstantHandle] Func<T1, T2, T3, T4, TResult> f) =>
        a.Bind(v1 => b.Bind(v2 => c.Bind(v3 => d.Map(v4 => f(arg1: v1, arg2: v2, arg3: v3, arg4: v4)))));

    private struct ValueAndHasValue<T>(T? value, bool hasValue)
    {
        public T? Value { get; } = value;
        public bool HasValue { get; } = hasValue;
    }
}
