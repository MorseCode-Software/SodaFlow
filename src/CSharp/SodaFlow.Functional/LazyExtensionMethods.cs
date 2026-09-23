using System;
using JetBrains.Annotations;

namespace SodaFlow.Functional;

/// <summary>
///     The operations available on a <see cref="Lazy{T}" />.
/// </summary>
/// <remarks>
///     Everything here is itself lazy: the result is a <see cref="Lazy{T}" /> which forces its
///     inputs only when a read forces the result.
/// </remarks>
[PublicAPI]
public static class LazyExtensionMethods
{
    /// <summary>
    ///     Maps the lazy input value with the given function. The Lazy from this call has the value of
    ///     that function on the value of the lazy input.
    /// </summary>
    /// <typeparam name="T">The type of the lazy input value.</typeparam>
    /// <typeparam name="TResult">The type of the lazy return value.</typeparam>
    /// <param name="a">The lazy input.</param>
    /// <param name="f">The function to transform the lazy input value.</param>
    /// <returns>
    ///     A lazy value which will give the value of the lazy input value <paramref name="a" /> transformed by the
    ///     function <paramref name="f" />.
    /// </returns>
    public static Lazy<TResult> Map<T, TResult>(this Lazy<T> a, Func<T, TResult> f) => new(() => f(a.Value));

    //      /**
    //     * Lift a binary function into lazy values. The Lazy from this call has the value of the
    //     * function on the values of the input Lazy values.
    //  */

    /// <summary>
    ///     Lifts a function into lazy input values. The Lazy from this call has the value of that
    ///     function on the values of the lazy inputs.
    /// </summary>
    /// <typeparam name="T1">The type of the first lazy input value.</typeparam>
    /// <typeparam name="T2">The type of the second lazy input value.</typeparam>
    /// <typeparam name="TResult">The type of the lazy return value.</typeparam>
    /// <param name="a">The first lazy input.</param>
    /// <param name="b">The second lazy input.</param>
    /// <param name="f">The function to transform the lazy input value.</param>
    /// <returns>
    ///     A lazy value which will give the value of the lazy input values <paramref name="a" /> and
    ///     <paramref name="b" /> transformed by the function <paramref name="f" />.
    /// </returns>
    public static Lazy<TResult> Lift<T1, T2, TResult>(this Lazy<T1> a, Lazy<T2> b, Func<T1, T2, TResult> f) =>
        new(() => f(arg1: a.Value, arg2: b.Value));

    /// <summary>
    ///     Lifts a function into lazy input values. The Lazy from this call has the value of that
    ///     function on the values of the lazy inputs.
    /// </summary>
    /// <typeparam name="T1">The type of the first lazy input value.</typeparam>
    /// <typeparam name="T2">The type of the second lazy input value.</typeparam>
    /// <typeparam name="T3">The type of the third lazy input value.</typeparam>
    /// <typeparam name="TResult">The type of the lazy return value.</typeparam>
    /// <param name="a">The first lazy input.</param>
    /// <param name="b">The second lazy input.</param>
    /// <param name="c">The third lazy input.</param>
    /// <param name="f">The function to transform the lazy input value.</param>
    /// <returns>
    ///     A lazy value which will give the value of the lazy input values <paramref name="a" />,
    ///     <paramref name="b" />, and <paramref name="c" /> transformed by the function <paramref name="f" />.
    /// </returns>
    public static Lazy<TResult> Lift<T1, T2, T3, TResult>(
        this Lazy<T1> a,
        Lazy<T2> b,
        Lazy<T3> c,
        Func<T1, T2, T3, TResult> f) =>
        new(() => f(arg1: a.Value, arg2: b.Value, arg3: c.Value));

    /// <summary>
    ///     Lifts a function into lazy input values. The Lazy from this call has the value of that
    ///     function on the values of the lazy inputs.
    /// </summary>
    /// <typeparam name="T1">The type of the first lazy input value.</typeparam>
    /// <typeparam name="T2">The type of the second lazy input value.</typeparam>
    /// <typeparam name="T3">The type of the third lazy input value.</typeparam>
    /// <typeparam name="T4">The type of the fourth lazy input value.</typeparam>
    /// <typeparam name="TResult">The type of the lazy return value.</typeparam>
    /// <param name="a">The first lazy input.</param>
    /// <param name="b">The second lazy input.</param>
    /// <param name="c">The third lazy input.</param>
    /// <param name="d">The fourth lazy input.</param>
    /// <param name="f">The function to transform the lazy input value.</param>
    /// <returns>
    ///     A lazy value which will give the value of the lazy input values <paramref name="a" />,
    ///     <paramref name="b" />, <paramref name="c" />, and <paramref name="d" /> transformed by the function
    ///     <paramref name="f" />.
    /// </returns>
    public static Lazy<TResult> Lift<T1, T2, T3, T4, TResult>(
        this Lazy<T1> a,
        Lazy<T2> b,
        Lazy<T3> c,
        Lazy<T4> d,
        Func<T1, T2, T3, T4, TResult> f) =>
        new(() => f(arg1: a.Value, arg2: b.Value, arg3: c.Value, arg4: d.Value));
}
