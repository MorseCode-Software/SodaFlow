using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using JetBrains.Annotations;

namespace SodaFlow.Functional;

/// <summary>
///     The operations available on a <see cref="Lazy{T}" />.
/// </summary>
/// <remarks>
///     <para>
///         Everything here is itself lazy: the result is a <see cref="Lazy{T}" />
///         which forces its inputs only when a read forces the result.
///     </para>
///     <para>
///         Each operation has two forms, as the constructors of
///         <see cref="Lazy{T}" /> have. The form with no mode makes its result with
///         <see cref="LazyThreadSafetyMode.ExecutionAndPublication" />, as
///         <c>new Lazy&lt;T&gt;(valueFactory)</c> does. That result keeps an
///         exception from the function, and each read after it throws that
///         exception again. The form with a <see cref="LazyThreadSafetyMode" />
///         gives that mode to the result.
///         <see cref="LazyThreadSafetyMode.PublicationOnly" /> keeps no exception,
///         and a read after a throw runs the function again.
///     </para>
/// </remarks>
// The type parameter of Lazy asks the trimmer to keep a parameterless constructor. The constructors of Lazy that
// do not get a value factory use it. The suppression is on the class and not on each method. The trimmer also
// reports the fields of the closures that the compiler makes, and a suppression on a method does not stop those
// reports.
[PublicAPI]
[UnconditionalSuppressMessage(
    category: "Trimming",
    checkId: "IL2091",
    Justification = "Each Lazy here gets a value factory, thus it does not use a parameterless constructor.")]
public static class LazyExtensionMethods
{
    /// <summary>
    ///     Maps the lazy input value with the given function. The Lazy from this call
    ///     has the value of that function on the value of the lazy input.
    /// </summary>
    /// <typeparam name="T">The type of the lazy input value.</typeparam>
    /// <typeparam name="TResult">The type of the lazy return value.</typeparam>
    /// <param name="a">The lazy input.</param>
    /// <param name="f">The function to transform the lazy input value.</param>
    /// <returns>
    ///     A lazy value which will give the value of the lazy input value
    ///     <paramref name="a" /> transformed by the function <paramref name="f" />.
    /// </returns>
    public static Lazy<TResult> Map<T, TResult>(this Lazy<T> a, Func<T, TResult> f) => new(() => f(a.Value));

    /// <summary>
    ///     Maps the lazy input value with the given function, and gives the result
    ///     the given thread safety mode.
    /// </summary>
    /// <typeparam name="T">The type of the lazy input value.</typeparam>
    /// <typeparam name="TResult">The type of the lazy return value.</typeparam>
    /// <param name="a">The lazy input.</param>
    /// <param name="f">The function to transform the lazy input value.</param>
    /// <param name="mode">
    ///     The thread safety mode of the result, as for
    ///     <see cref="Lazy{T}(Func{T},LazyThreadSafetyMode)" />.
    /// </param>
    /// <returns>
    ///     A lazy value which will give the value of the lazy input value
    ///     <paramref name="a" /> transformed by the function <paramref name="f" />.
    /// </returns>
    public static Lazy<TResult> Map<T, TResult>(this Lazy<T> a, Func<T, TResult> f, LazyThreadSafetyMode mode) =>
        new(valueFactory: () => f(a.Value), mode: mode);

    /// <summary>
    ///     Lifts a function into lazy input values. The Lazy from this call has the
    ///     value of that function on the values of the lazy inputs.
    /// </summary>
    /// <typeparam name="T1">The type of the first lazy input value.</typeparam>
    /// <typeparam name="T2">The type of the second lazy input value.</typeparam>
    /// <typeparam name="TResult">The type of the lazy return value.</typeparam>
    /// <param name="a">The first lazy input.</param>
    /// <param name="b">The second lazy input.</param>
    /// <param name="f">The function to transform the lazy input value.</param>
    /// <returns>
    ///     A lazy value which will give the value of the lazy input values
    ///     <paramref name="a" /> and <paramref name="b" /> transformed by the function
    ///     <paramref name="f" />.
    /// </returns>
    public static Lazy<TResult> Lift<T1, T2, TResult>(this Lazy<T1> a, Lazy<T2> b, Func<T1, T2, TResult> f) =>
        new(() => f(arg1: a.Value, arg2: b.Value));

    /// <summary>
    ///     Lifts a function into lazy input values, and gives the result the given
    ///     thread safety mode.
    /// </summary>
    /// <typeparam name="T1">The type of the first lazy input value.</typeparam>
    /// <typeparam name="T2">The type of the second lazy input value.</typeparam>
    /// <typeparam name="TResult">The type of the lazy return value.</typeparam>
    /// <param name="a">The first lazy input.</param>
    /// <param name="b">The second lazy input.</param>
    /// <param name="f">The function to transform the lazy input value.</param>
    /// <param name="mode">
    ///     The thread safety mode of the result, as for
    ///     <see cref="Lazy{T}(Func{T},LazyThreadSafetyMode)" />.
    /// </param>
    /// <returns>
    ///     A lazy value which will give the value of the lazy input values
    ///     <paramref name="a" /> and <paramref name="b" /> transformed by the function
    ///     <paramref name="f" />.
    /// </returns>
    public static Lazy<TResult> Lift<T1, T2, TResult>(
        this Lazy<T1> a,
        Lazy<T2> b,
        Func<T1, T2, TResult> f,
        LazyThreadSafetyMode mode) =>
        new(valueFactory: () => f(arg1: a.Value, arg2: b.Value), mode: mode);

    /// <summary>
    ///     Lifts a function into lazy input values. The Lazy from this call has the
    ///     value of that function on the values of the lazy inputs.
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
    ///     A lazy value which will give the value of the lazy input values
    ///     <paramref name="a" />, <paramref name="b" />, and <paramref name="c" />
    ///     transformed by the function <paramref name="f" />.
    /// </returns>
    public static Lazy<TResult> Lift<T1, T2, T3, TResult>(
        this Lazy<T1> a,
        Lazy<T2> b,
        Lazy<T3> c,
        Func<T1, T2, T3, TResult> f) =>
        new(() => f(arg1: a.Value, arg2: b.Value, arg3: c.Value));

    /// <summary>
    ///     Lifts a function into lazy input values, and gives the result the given
    ///     thread safety mode.
    /// </summary>
    /// <typeparam name="T1">The type of the first lazy input value.</typeparam>
    /// <typeparam name="T2">The type of the second lazy input value.</typeparam>
    /// <typeparam name="T3">The type of the third lazy input value.</typeparam>
    /// <typeparam name="TResult">The type of the lazy return value.</typeparam>
    /// <param name="a">The first lazy input.</param>
    /// <param name="b">The second lazy input.</param>
    /// <param name="c">The third lazy input.</param>
    /// <param name="f">The function to transform the lazy input value.</param>
    /// <param name="mode">
    ///     The thread safety mode of the result, as for
    ///     <see cref="Lazy{T}(Func{T},LazyThreadSafetyMode)" />.
    /// </param>
    /// <returns>
    ///     A lazy value which will give the value of the lazy input values
    ///     <paramref name="a" />, <paramref name="b" />, and <paramref name="c" />
    ///     transformed by the function <paramref name="f" />.
    /// </returns>
    public static Lazy<TResult> Lift<T1, T2, T3, TResult>(
        this Lazy<T1> a,
        Lazy<T2> b,
        Lazy<T3> c,
        Func<T1, T2, T3, TResult> f,
        LazyThreadSafetyMode mode) =>
        new(valueFactory: () => f(arg1: a.Value, arg2: b.Value, arg3: c.Value), mode: mode);

    /// <summary>
    ///     Lifts a function into lazy input values. The Lazy from this call has the
    ///     value of that function on the values of the lazy inputs.
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
    ///     A lazy value which will give the value of the lazy input values
    ///     <paramref name="a" />, <paramref name="b" />, <paramref name="c" />, and
    ///     <paramref name="d" /> transformed by the function <paramref name="f" />.
    /// </returns>
    public static Lazy<TResult> Lift<T1, T2, T3, T4, TResult>(
        this Lazy<T1> a,
        Lazy<T2> b,
        Lazy<T3> c,
        Lazy<T4> d,
        Func<T1, T2, T3, T4, TResult> f) =>
        new(() => f(arg1: a.Value, arg2: b.Value, arg3: c.Value, arg4: d.Value));

    /// <summary>
    ///     Lifts a function into lazy input values, and gives the result the given
    ///     thread safety mode.
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
    /// <param name="mode">
    ///     The thread safety mode of the result, as for
    ///     <see cref="Lazy{T}(Func{T},LazyThreadSafetyMode)" />.
    /// </param>
    /// <returns>
    ///     A lazy value which will give the value of the lazy input values
    ///     <paramref name="a" />, <paramref name="b" />, <paramref name="c" />, and
    ///     <paramref name="d" /> transformed by the function <paramref name="f" />.
    /// </returns>
    public static Lazy<TResult> Lift<T1, T2, T3, T4, TResult>(
        this Lazy<T1> a,
        Lazy<T2> b,
        Lazy<T3> c,
        Lazy<T4> d,
        Func<T1, T2, T3, T4, TResult> f,
        LazyThreadSafetyMode mode) =>
        new(valueFactory: () => f(arg1: a.Value, arg2: b.Value, arg3: c.Value, arg4: d.Value), mode: mode);
}
