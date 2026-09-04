using System;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace SodaFlow.Functional;

/// <summary>
///     Extension methods for basic type conversion helpers.
/// </summary>
/// <remarks>
///     C# splits what is one idea in a functional language into two: <see cref="System.Action" />
///     and <see cref="System.Func{TResult}" />. Anything written to take a function therefore
///     cannot be handed an action. These conversions close that gap, so a single implementation
///     taking a function can serve both - which is how, for instance,
///     <c>Maybe&lt;T&gt;.MatchVoid</c> is expressed in terms of <c>Maybe&lt;T&gt;.Match</c>.
/// </remarks>
[PublicAPI]
public static class UnitExtensionMethods
{
    /// <summary>
    ///     Discards a value, yielding <see cref="Unit" /> in its place.
    /// </summary>
    /// <typeparam name="T">The type of the value being discarded.</typeparam>
    /// <param name="o">The value to discard.</param>
    /// <returns><see cref="Unit.Value" />.</returns>
    /// <remarks>
    ///     For deliberately throwing away a result, where saying so is clearer than letting the
    ///     expression stand on its own.
    /// </remarks>
    public static Unit Ignore<T>(this T o) => Unit.Value;

    /// <summary>
    ///     Returns the value as the given type, letting the target type be named where inference
    ///     would otherwise pick the value's own.
    /// </summary>
    /// <typeparam name="T">The type to view the value as.</typeparam>
    /// <param name="o">The value.</param>
    /// <returns>The same value, typed as <typeparamref name="T" />.</returns>
    /// <remarks>
    ///     Nothing is converted; this only changes the static type. Used here to reach an explicit
    ///     interface implementation - <c>this.Upcast&lt;IMaybe&gt;()</c> - without a cast
    ///     expression and the parentheses that come with it.
    /// </remarks>
    public static T Upcast<T>(this T o) => o;

    /// <summary>
    ///     Converts an action into a function returning <see cref="Unit" />, so that it can be
    ///     used where a function returning a value is required.
    /// </summary>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> and returns
    ///     <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred: the returned function invokes <paramref name="action" /> each time
    ///     it is itself called.
    /// </remarks>
    public static Func<Unit> ToFunc(this Action action) =>
        () =>
        {
            action();
            return Unit.Value;
        };

    /// <summary>
    ///     Converts an action into a function returning <see cref="Unit" />, so that it can be
    ///     used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T">The type of the argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its argument and returns
    ///     <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred: the returned function invokes <paramref name="action" /> each time
    ///     it is itself called.
    /// </remarks>
    public static Func<T, Unit> ToFunc<T>(this Action<T> action) =>
        v =>
        {
            action(v);
            return Unit.Value;
        };

    /// <summary>
    ///     Converts an action into a function returning <see cref="Unit" />, so that it can be
    ///     used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its arguments and returns
    ///     <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred: the returned function invokes <paramref name="action" /> each time
    ///     it is itself called.
    /// </remarks>
    public static Func<T1, T2, Unit> ToFunc<T1, T2>(this Action<T1, T2> action) =>
        (v1, v2) =>
        {
            action(arg1: v1, arg2: v2);
            return Unit.Value;
        };

    /// <summary>
    ///     Converts an action into a function returning <see cref="Unit" />, so that it can be
    ///     used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <typeparam name="T3">The type of the third argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its arguments and returns
    ///     <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred: the returned function invokes <paramref name="action" /> each time
    ///     it is itself called.
    /// </remarks>
    public static Func<T1, T2, T3, Unit> ToFunc<T1, T2, T3>(this Action<T1, T2, T3> action) =>
        (v1, v2, v3) =>
        {
            action(arg1: v1, arg2: v2, arg3: v3);
            return Unit.Value;
        };

    /// <summary>
    ///     Converts an action into a function returning <see cref="Unit" />, so that it can be
    ///     used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <typeparam name="T3">The type of the third argument.</typeparam>
    /// <typeparam name="T4">The type of the fourth argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its arguments and returns
    ///     <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred: the returned function invokes <paramref name="action" /> each time
    ///     it is itself called.
    /// </remarks>
    public static Func<T1, T2, T3, T4, Unit> ToFunc<T1, T2, T3, T4>(this Action<T1, T2, T3, T4> action) =>
        (v1, v2, v3, v4) =>
        {
            action(arg1: v1, arg2: v2, arg3: v3, arg4: v4);
            return Unit.Value;
        };

    /// <summary>
    ///     Converts an action into a function returning <see cref="Unit" />, so that it can be
    ///     used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <typeparam name="T3">The type of the third argument.</typeparam>
    /// <typeparam name="T4">The type of the fourth argument.</typeparam>
    /// <typeparam name="T5">The type of the fifth argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its arguments and returns
    ///     <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred: the returned function invokes <paramref name="action" /> each time
    ///     it is itself called.
    /// </remarks>
    public static Func<T1, T2, T3, T4, T5, Unit> ToFunc<T1, T2, T3, T4, T5>(this Action<T1, T2, T3, T4, T5> action) =>
        (v1, v2, v3, v4, v5) =>
        {
            action(arg1: v1, arg2: v2, arg3: v3, arg4: v4, arg5: v5);
            return Unit.Value;
        };

    /// <summary>
    ///     Converts an action into a function returning <see cref="Unit" />, so that it can be
    ///     used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <typeparam name="T3">The type of the third argument.</typeparam>
    /// <typeparam name="T4">The type of the fourth argument.</typeparam>
    /// <typeparam name="T5">The type of the fifth argument.</typeparam>
    /// <typeparam name="T6">The type of the sixth argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its arguments and returns
    ///     <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred: the returned function invokes <paramref name="action" /> each time
    ///     it is itself called.
    /// </remarks>
    public static Func<T1, T2, T3, T4, T5, T6, Unit> ToFunc<T1, T2, T3, T4, T5, T6>(
        this Action<T1, T2, T3, T4, T5, T6> action) =>
        (v1, v2, v3, v4, v5, v6) =>
        {
            action(arg1: v1, arg2: v2, arg3: v3, arg4: v4, arg5: v5, arg6: v6);
            return Unit.Value;
        };

    /// <summary>
    ///     Converts an action into a function returning <see cref="Unit" />, so that it can be
    ///     used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <typeparam name="T3">The type of the third argument.</typeparam>
    /// <typeparam name="T4">The type of the fourth argument.</typeparam>
    /// <typeparam name="T5">The type of the fifth argument.</typeparam>
    /// <typeparam name="T6">The type of the sixth argument.</typeparam>
    /// <typeparam name="T7">The type of the seventh argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its arguments and returns
    ///     <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred: the returned function invokes <paramref name="action" /> each time
    ///     it is itself called.
    /// </remarks>
    public static Func<T1, T2, T3, T4, T5, T6, T7, Unit> ToFunc<T1, T2, T3, T4, T5, T6, T7>(
        this Action<T1, T2, T3, T4, T5, T6, T7> action) =>
        (v1, v2, v3, v4, v5, v6, v7) =>
        {
            action(arg1: v1, arg2: v2, arg3: v3, arg4: v4, arg5: v5, arg6: v6, arg7: v7);
            return Unit.Value;
        };

    /// <summary>
    ///     Converts an action into a function returning <see cref="Unit" />, so that it can be
    ///     used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <typeparam name="T3">The type of the third argument.</typeparam>
    /// <typeparam name="T4">The type of the fourth argument.</typeparam>
    /// <typeparam name="T5">The type of the fifth argument.</typeparam>
    /// <typeparam name="T6">The type of the sixth argument.</typeparam>
    /// <typeparam name="T7">The type of the seventh argument.</typeparam>
    /// <typeparam name="T8">The type of the eighth argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its arguments and returns
    ///     <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred: the returned function invokes <paramref name="action" /> each time
    ///     it is itself called.
    /// </remarks>
    public static Func<T1, T2, T3, T4, T5, T6, T7, T8, Unit> ToFunc<T1, T2, T3, T4, T5, T6, T7, T8>(
        this Action<T1, T2, T3, T4, T5, T6, T7, T8> action) =>
        (v1, v2, v3, v4, v5, v6, v7, v8) =>
        {
            action(arg1: v1, arg2: v2, arg3: v3, arg4: v4, arg5: v5, arg6: v6, arg7: v7, arg8: v8);
            return Unit.Value;
        };

    /// <summary>
    ///     Converts an action into a function returning <see cref="Unit" />, so that it can be
    ///     used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <typeparam name="T3">The type of the third argument.</typeparam>
    /// <typeparam name="T4">The type of the fourth argument.</typeparam>
    /// <typeparam name="T5">The type of the fifth argument.</typeparam>
    /// <typeparam name="T6">The type of the sixth argument.</typeparam>
    /// <typeparam name="T7">The type of the seventh argument.</typeparam>
    /// <typeparam name="T8">The type of the eighth argument.</typeparam>
    /// <typeparam name="T9">The type of the ninth argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its arguments and returns
    ///     <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred: the returned function invokes <paramref name="action" /> each time
    ///     it is itself called.
    /// </remarks>
    public static Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, Unit> ToFunc<T1, T2, T3, T4, T5, T6, T7, T8, T9>(
        this Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> action) =>
        (v1, v2, v3, v4, v5, v6, v7, v8, v9) =>
        {
            action(arg1: v1, arg2: v2, arg3: v3, arg4: v4, arg5: v5, arg6: v6, arg7: v7, arg8: v8, arg9: v9);
            return Unit.Value;
        };

    /// <summary>
    ///     Converts an action into a function returning <see cref="Unit" />, so that it can be
    ///     used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <typeparam name="T3">The type of the third argument.</typeparam>
    /// <typeparam name="T4">The type of the fourth argument.</typeparam>
    /// <typeparam name="T5">The type of the fifth argument.</typeparam>
    /// <typeparam name="T6">The type of the sixth argument.</typeparam>
    /// <typeparam name="T7">The type of the seventh argument.</typeparam>
    /// <typeparam name="T8">The type of the eighth argument.</typeparam>
    /// <typeparam name="T9">The type of the ninth argument.</typeparam>
    /// <typeparam name="T10">The type of the tenth argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its arguments and returns
    ///     <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred: the returned function invokes <paramref name="action" /> each time
    ///     it is itself called.
    /// </remarks>
    public static Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, Unit> ToFunc<T1, T2, T3, T4, T5, T6, T7, T8, T9,
        T10>(this Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> action) =>
        (v1, v2, v3, v4, v5, v6, v7, v8, v9, v10) =>
        {
            action(
                arg1: v1,
                arg2: v2,
                arg3: v3,
                arg4: v4,
                arg5: v5,
                arg6: v6,
                arg7: v7,
                arg8: v8,
                arg9: v9,
                arg10: v10);

            return Unit.Value;
        };

    /// <summary>
    ///     Converts an action into a function returning <see cref="Unit" />, so that it can be
    ///     used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <typeparam name="T3">The type of the third argument.</typeparam>
    /// <typeparam name="T4">The type of the fourth argument.</typeparam>
    /// <typeparam name="T5">The type of the fifth argument.</typeparam>
    /// <typeparam name="T6">The type of the sixth argument.</typeparam>
    /// <typeparam name="T7">The type of the seventh argument.</typeparam>
    /// <typeparam name="T8">The type of the eighth argument.</typeparam>
    /// <typeparam name="T9">The type of the ninth argument.</typeparam>
    /// <typeparam name="T10">The type of the tenth argument.</typeparam>
    /// <typeparam name="T11">The type of the eleventh argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its arguments and returns
    ///     <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred: the returned function invokes <paramref name="action" /> each time
    ///     it is itself called.
    /// </remarks>
    public static Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, Unit> ToFunc<T1, T2, T3, T4, T5, T6, T7, T8,
        T9, T10, T11>(this Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> action) =>
        (v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11) =>
        {
            action(
                arg1: v1,
                arg2: v2,
                arg3: v3,
                arg4: v4,
                arg5: v5,
                arg6: v6,
                arg7: v7,
                arg8: v8,
                arg9: v9,
                arg10: v10,
                arg11: v11);

            return Unit.Value;
        };

    /// <summary>
    ///     Converts an action into a function returning <see cref="Unit" />, so that it can be
    ///     used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <typeparam name="T3">The type of the third argument.</typeparam>
    /// <typeparam name="T4">The type of the fourth argument.</typeparam>
    /// <typeparam name="T5">The type of the fifth argument.</typeparam>
    /// <typeparam name="T6">The type of the sixth argument.</typeparam>
    /// <typeparam name="T7">The type of the seventh argument.</typeparam>
    /// <typeparam name="T8">The type of the eighth argument.</typeparam>
    /// <typeparam name="T9">The type of the ninth argument.</typeparam>
    /// <typeparam name="T10">The type of the tenth argument.</typeparam>
    /// <typeparam name="T11">The type of the eleventh argument.</typeparam>
    /// <typeparam name="T12">The type of the twelfth argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its arguments and returns
    ///     <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred: the returned function invokes <paramref name="action" /> each time
    ///     it is itself called.
    /// </remarks>
    public static Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, Unit> ToFunc<T1, T2, T3, T4, T5, T6, T7,
        T8, T9, T10, T11, T12>(this Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> action) =>
        (v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11, v12) =>
        {
            action(
                arg1: v1,
                arg2: v2,
                arg3: v3,
                arg4: v4,
                arg5: v5,
                arg6: v6,
                arg7: v7,
                arg8: v8,
                arg9: v9,
                arg10: v10,
                arg11: v11,
                arg12: v12);

            return Unit.Value;
        };

    /// <summary>
    ///     Converts an action into a function returning <see cref="Unit" />, so that it can be
    ///     used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <typeparam name="T3">The type of the third argument.</typeparam>
    /// <typeparam name="T4">The type of the fourth argument.</typeparam>
    /// <typeparam name="T5">The type of the fifth argument.</typeparam>
    /// <typeparam name="T6">The type of the sixth argument.</typeparam>
    /// <typeparam name="T7">The type of the seventh argument.</typeparam>
    /// <typeparam name="T8">The type of the eighth argument.</typeparam>
    /// <typeparam name="T9">The type of the ninth argument.</typeparam>
    /// <typeparam name="T10">The type of the tenth argument.</typeparam>
    /// <typeparam name="T11">The type of the eleventh argument.</typeparam>
    /// <typeparam name="T12">The type of the twelfth argument.</typeparam>
    /// <typeparam name="T13">The type of the thirteenth argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its arguments and returns
    ///     <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred: the returned function invokes <paramref name="action" /> each time
    ///     it is itself called.
    /// </remarks>
    public static Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, Unit> ToFunc<T1, T2, T3, T4, T5, T6,
        T7, T8, T9, T10, T11, T12, T13>(this Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> action) =>
        (v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11, v12, v13) =>
        {
            action(
                arg1: v1,
                arg2: v2,
                arg3: v3,
                arg4: v4,
                arg5: v5,
                arg6: v6,
                arg7: v7,
                arg8: v8,
                arg9: v9,
                arg10: v10,
                arg11: v11,
                arg12: v12,
                arg13: v13);

            return Unit.Value;
        };

    /// <summary>
    ///     Converts an action into a function returning <see cref="Unit" />, so that it can be
    ///     used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <typeparam name="T3">The type of the third argument.</typeparam>
    /// <typeparam name="T4">The type of the fourth argument.</typeparam>
    /// <typeparam name="T5">The type of the fifth argument.</typeparam>
    /// <typeparam name="T6">The type of the sixth argument.</typeparam>
    /// <typeparam name="T7">The type of the seventh argument.</typeparam>
    /// <typeparam name="T8">The type of the eighth argument.</typeparam>
    /// <typeparam name="T9">The type of the ninth argument.</typeparam>
    /// <typeparam name="T10">The type of the tenth argument.</typeparam>
    /// <typeparam name="T11">The type of the eleventh argument.</typeparam>
    /// <typeparam name="T12">The type of the twelfth argument.</typeparam>
    /// <typeparam name="T13">The type of the thirteenth argument.</typeparam>
    /// <typeparam name="T14">The type of the fourteenth argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its arguments and returns
    ///     <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred: the returned function invokes <paramref name="action" /> each time
    ///     it is itself called.
    /// </remarks>
    public static Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, Unit> ToFunc<T1, T2, T3, T4, T5,
        T6, T7, T8, T9, T10, T11, T12, T13, T14>(
        this Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> action) =>
        (v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11, v12, v13, v14) =>
        {
            action(
                arg1: v1,
                arg2: v2,
                arg3: v3,
                arg4: v4,
                arg5: v5,
                arg6: v6,
                arg7: v7,
                arg8: v8,
                arg9: v9,
                arg10: v10,
                arg11: v11,
                arg12: v12,
                arg13: v13,
                arg14: v14);

            return Unit.Value;
        };

    /// <summary>
    ///     Converts an action into a function returning <see cref="Unit" />, so that it can be
    ///     used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <typeparam name="T3">The type of the third argument.</typeparam>
    /// <typeparam name="T4">The type of the fourth argument.</typeparam>
    /// <typeparam name="T5">The type of the fifth argument.</typeparam>
    /// <typeparam name="T6">The type of the sixth argument.</typeparam>
    /// <typeparam name="T7">The type of the seventh argument.</typeparam>
    /// <typeparam name="T8">The type of the eighth argument.</typeparam>
    /// <typeparam name="T9">The type of the ninth argument.</typeparam>
    /// <typeparam name="T10">The type of the tenth argument.</typeparam>
    /// <typeparam name="T11">The type of the eleventh argument.</typeparam>
    /// <typeparam name="T12">The type of the twelfth argument.</typeparam>
    /// <typeparam name="T13">The type of the thirteenth argument.</typeparam>
    /// <typeparam name="T14">The type of the fourteenth argument.</typeparam>
    /// <typeparam name="T15">The type of the fifteenth argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its arguments and returns
    ///     <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred: the returned function invokes <paramref name="action" /> each time
    ///     it is itself called.
    /// </remarks>
    public static Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, Unit> ToFunc<T1, T2, T3,
        T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(
        this Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15> action) =>
        (v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11, v12, v13, v14, v15) =>
        {
            action(
                arg1: v1,
                arg2: v2,
                arg3: v3,
                arg4: v4,
                arg5: v5,
                arg6: v6,
                arg7: v7,
                arg8: v8,
                arg9: v9,
                arg10: v10,
                arg11: v11,
                arg12: v12,
                arg13: v13,
                arg14: v14,
                arg15: v15);

            return Unit.Value;
        };

    /// <summary>
    ///     Converts an action into a function returning <see cref="Unit" />, so that it can be
    ///     used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <typeparam name="T3">The type of the third argument.</typeparam>
    /// <typeparam name="T4">The type of the fourth argument.</typeparam>
    /// <typeparam name="T5">The type of the fifth argument.</typeparam>
    /// <typeparam name="T6">The type of the sixth argument.</typeparam>
    /// <typeparam name="T7">The type of the seventh argument.</typeparam>
    /// <typeparam name="T8">The type of the eighth argument.</typeparam>
    /// <typeparam name="T9">The type of the ninth argument.</typeparam>
    /// <typeparam name="T10">The type of the tenth argument.</typeparam>
    /// <typeparam name="T11">The type of the eleventh argument.</typeparam>
    /// <typeparam name="T12">The type of the twelfth argument.</typeparam>
    /// <typeparam name="T13">The type of the thirteenth argument.</typeparam>
    /// <typeparam name="T14">The type of the fourteenth argument.</typeparam>
    /// <typeparam name="T15">The type of the fifteenth argument.</typeparam>
    /// <typeparam name="T16">The type of the sixteenth argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its arguments and returns
    ///     <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred: the returned function invokes <paramref name="action" /> each time
    ///     it is itself called.
    /// </remarks>
    public static Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, Unit> ToFunc<T1, T2,
        T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>(
        this Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16> action) =>
        (v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11, v12, v13, v14, v15, v16) =>
        {
            action(
                arg1: v1,
                arg2: v2,
                arg3: v3,
                arg4: v4,
                arg5: v5,
                arg6: v6,
                arg7: v7,
                arg8: v8,
                arg9: v9,
                arg10: v10,
                arg11: v11,
                arg12: v12,
                arg13: v13,
                arg14: v14,
                arg15: v15,
                arg16: v16);

            return Unit.Value;
        };

    /// <summary>
    ///     Converts an asynchronous action into a function producing <see cref="Unit" />, so that
    ///     it can be used where a function returning a value is required.
    /// </summary>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> and, once it has completed,
    ///     produces <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred beyond what the action itself defers: the returned function invokes
    ///     <paramref name="action" /> each time it is called, and its task completes when that
    ///     action's does.
    /// </remarks>
    public static Func<Task<Unit>> ToAsyncFunc(this Func<Task> action) =>
        async () =>
        {
            await action();
            return Unit.Value;
        };

    /// <summary>
    ///     Converts an asynchronous action into a function producing <see cref="Unit" />, so that
    ///     it can be used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T">The type of the argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its argument and, once it has completed,
    ///     produces <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred beyond what the action itself defers: the returned function invokes
    ///     <paramref name="action" /> each time it is called, and its task completes when that
    ///     action's does.
    /// </remarks>
    public static Func<T, Task<Unit>> ToAsyncFunc<T>(this Func<T, Task> action) =>
        async v =>
        {
            await action(v);
            return Unit.Value;
        };

    /// <summary>
    ///     Converts an asynchronous action into a function producing <see cref="Unit" />, so that
    ///     it can be used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its arguments and, once it has completed,
    ///     produces <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred beyond what the action itself defers: the returned function invokes
    ///     <paramref name="action" /> each time it is called, and its task completes when that
    ///     action's does.
    /// </remarks>
    public static Func<T1, T2, Task<Unit>> ToAsyncFunc<T1, T2>(this Func<T1, T2, Task> action) =>
        async (v1, v2) =>
        {
            await action(arg1: v1, arg2: v2);
            return Unit.Value;
        };

    /// <summary>
    ///     Converts an asynchronous action into a function producing <see cref="Unit" />, so that
    ///     it can be used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <typeparam name="T3">The type of the third argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its arguments and, once it has completed,
    ///     produces <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred beyond what the action itself defers: the returned function invokes
    ///     <paramref name="action" /> each time it is called, and its task completes when that
    ///     action's does.
    /// </remarks>
    public static Func<T1, T2, T3, Task<Unit>> ToAsyncFunc<T1, T2, T3>(this Func<T1, T2, T3, Task> action) =>
        async (v1, v2, v3) =>
        {
            await action(arg1: v1, arg2: v2, arg3: v3);
            return Unit.Value;
        };

    /// <summary>
    ///     Converts an asynchronous action into a function producing <see cref="Unit" />, so that
    ///     it can be used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <typeparam name="T3">The type of the third argument.</typeparam>
    /// <typeparam name="T4">The type of the fourth argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its arguments and, once it has completed,
    ///     produces <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred beyond what the action itself defers: the returned function invokes
    ///     <paramref name="action" /> each time it is called, and its task completes when that
    ///     action's does.
    /// </remarks>
    public static Func<T1, T2, T3, T4, Task<Unit>> ToAsyncFunc<T1, T2, T3, T4>(
        this Func<T1, T2, T3, T4, Task> action) =>
        async (v1, v2, v3, v4) =>
        {
            await action(arg1: v1, arg2: v2, arg3: v3, arg4: v4);
            return Unit.Value;
        };

    /// <summary>
    ///     Converts an asynchronous action into a function producing <see cref="Unit" />, so that
    ///     it can be used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <typeparam name="T3">The type of the third argument.</typeparam>
    /// <typeparam name="T4">The type of the fourth argument.</typeparam>
    /// <typeparam name="T5">The type of the fifth argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its arguments and, once it has completed,
    ///     produces <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred beyond what the action itself defers: the returned function invokes
    ///     <paramref name="action" /> each time it is called, and its task completes when that
    ///     action's does.
    /// </remarks>
    public static Func<T1, T2, T3, T4, T5, Task<Unit>> ToAsyncFunc<T1, T2, T3, T4, T5>(
        this Func<T1, T2, T3, T4, T5, Task> action) =>
        async (v1, v2, v3, v4, v5) =>
        {
            await action(arg1: v1, arg2: v2, arg3: v3, arg4: v4, arg5: v5);
            return Unit.Value;
        };

    /// <summary>
    ///     Converts an asynchronous action into a function producing <see cref="Unit" />, so that
    ///     it can be used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <typeparam name="T3">The type of the third argument.</typeparam>
    /// <typeparam name="T4">The type of the fourth argument.</typeparam>
    /// <typeparam name="T5">The type of the fifth argument.</typeparam>
    /// <typeparam name="T6">The type of the sixth argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its arguments and, once it has completed,
    ///     produces <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred beyond what the action itself defers: the returned function invokes
    ///     <paramref name="action" /> each time it is called, and its task completes when that
    ///     action's does.
    /// </remarks>
    public static Func<T1, T2, T3, T4, T5, T6, Task<Unit>> ToAsyncFunc<T1, T2, T3, T4, T5, T6>(
        this Func<T1, T2, T3, T4, T5, T6, Task> action) =>
        async (v1, v2, v3, v4, v5, v6) =>
        {
            await action(arg1: v1, arg2: v2, arg3: v3, arg4: v4, arg5: v5, arg6: v6);
            return Unit.Value;
        };

    /// <summary>
    ///     Converts an asynchronous action into a function producing <see cref="Unit" />, so that
    ///     it can be used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <typeparam name="T3">The type of the third argument.</typeparam>
    /// <typeparam name="T4">The type of the fourth argument.</typeparam>
    /// <typeparam name="T5">The type of the fifth argument.</typeparam>
    /// <typeparam name="T6">The type of the sixth argument.</typeparam>
    /// <typeparam name="T7">The type of the seventh argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its arguments and, once it has completed,
    ///     produces <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred beyond what the action itself defers: the returned function invokes
    ///     <paramref name="action" /> each time it is called, and its task completes when that
    ///     action's does.
    /// </remarks>
    public static Func<T1, T2, T3, T4, T5, T6, T7, Task<Unit>> ToAsyncFunc<T1, T2, T3, T4, T5, T6, T7>(
        this Func<T1, T2, T3, T4, T5, T6, T7, Task> action) =>
        async (v1, v2, v3, v4, v5, v6, v7) =>
        {
            await action(arg1: v1, arg2: v2, arg3: v3, arg4: v4, arg5: v5, arg6: v6, arg7: v7);
            return Unit.Value;
        };

    /// <summary>
    ///     Converts an asynchronous action into a function producing <see cref="Unit" />, so that
    ///     it can be used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <typeparam name="T3">The type of the third argument.</typeparam>
    /// <typeparam name="T4">The type of the fourth argument.</typeparam>
    /// <typeparam name="T5">The type of the fifth argument.</typeparam>
    /// <typeparam name="T6">The type of the sixth argument.</typeparam>
    /// <typeparam name="T7">The type of the seventh argument.</typeparam>
    /// <typeparam name="T8">The type of the eighth argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its arguments and, once it has completed,
    ///     produces <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred beyond what the action itself defers: the returned function invokes
    ///     <paramref name="action" /> each time it is called, and its task completes when that
    ///     action's does.
    /// </remarks>
    public static Func<T1, T2, T3, T4, T5, T6, T7, T8, Task<Unit>> ToAsyncFunc<T1, T2, T3, T4, T5, T6, T7, T8>(
        this Func<T1, T2, T3, T4, T5, T6, T7, T8, Task> action) =>
        async (v1, v2, v3, v4, v5, v6, v7, v8) =>
        {
            await action(arg1: v1, arg2: v2, arg3: v3, arg4: v4, arg5: v5, arg6: v6, arg7: v7, arg8: v8);
            return Unit.Value;
        };

    /// <summary>
    ///     Converts an asynchronous action into a function producing <see cref="Unit" />, so that
    ///     it can be used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <typeparam name="T3">The type of the third argument.</typeparam>
    /// <typeparam name="T4">The type of the fourth argument.</typeparam>
    /// <typeparam name="T5">The type of the fifth argument.</typeparam>
    /// <typeparam name="T6">The type of the sixth argument.</typeparam>
    /// <typeparam name="T7">The type of the seventh argument.</typeparam>
    /// <typeparam name="T8">The type of the eighth argument.</typeparam>
    /// <typeparam name="T9">The type of the ninth argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its arguments and, once it has completed,
    ///     produces <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred beyond what the action itself defers: the returned function invokes
    ///     <paramref name="action" /> each time it is called, and its task completes when that
    ///     action's does.
    /// </remarks>
    public static Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, Task<Unit>> ToAsyncFunc<T1, T2, T3, T4, T5, T6, T7, T8,
        T9>(this Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, Task> action) =>
        async (v1, v2, v3, v4, v5, v6, v7, v8, v9) =>
        {
            await action(arg1: v1, arg2: v2, arg3: v3, arg4: v4, arg5: v5, arg6: v6, arg7: v7, arg8: v8, arg9: v9);
            return Unit.Value;
        };

    /// <summary>
    ///     Converts an asynchronous action into a function producing <see cref="Unit" />, so that
    ///     it can be used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <typeparam name="T3">The type of the third argument.</typeparam>
    /// <typeparam name="T4">The type of the fourth argument.</typeparam>
    /// <typeparam name="T5">The type of the fifth argument.</typeparam>
    /// <typeparam name="T6">The type of the sixth argument.</typeparam>
    /// <typeparam name="T7">The type of the seventh argument.</typeparam>
    /// <typeparam name="T8">The type of the eighth argument.</typeparam>
    /// <typeparam name="T9">The type of the ninth argument.</typeparam>
    /// <typeparam name="T10">The type of the tenth argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its arguments and, once it has completed,
    ///     produces <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred beyond what the action itself defers: the returned function invokes
    ///     <paramref name="action" /> each time it is called, and its task completes when that
    ///     action's does.
    /// </remarks>
    public static Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, Task<Unit>> ToAsyncFunc<T1, T2, T3, T4, T5, T6, T7,
        T8, T9, T10>(this Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, Task> action) =>
        async (v1, v2, v3, v4, v5, v6, v7, v8, v9, v10) =>
        {
            await action(
                arg1: v1,
                arg2: v2,
                arg3: v3,
                arg4: v4,
                arg5: v5,
                arg6: v6,
                arg7: v7,
                arg8: v8,
                arg9: v9,
                arg10: v10);

            return Unit.Value;
        };

    /// <summary>
    ///     Converts an asynchronous action into a function producing <see cref="Unit" />, so that
    ///     it can be used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <typeparam name="T3">The type of the third argument.</typeparam>
    /// <typeparam name="T4">The type of the fourth argument.</typeparam>
    /// <typeparam name="T5">The type of the fifth argument.</typeparam>
    /// <typeparam name="T6">The type of the sixth argument.</typeparam>
    /// <typeparam name="T7">The type of the seventh argument.</typeparam>
    /// <typeparam name="T8">The type of the eighth argument.</typeparam>
    /// <typeparam name="T9">The type of the ninth argument.</typeparam>
    /// <typeparam name="T10">The type of the tenth argument.</typeparam>
    /// <typeparam name="T11">The type of the eleventh argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its arguments and, once it has completed,
    ///     produces <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred beyond what the action itself defers: the returned function invokes
    ///     <paramref name="action" /> each time it is called, and its task completes when that
    ///     action's does.
    /// </remarks>
    public static Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, Task<Unit>> ToAsyncFunc<T1, T2, T3, T4, T5, T6,
        T7, T8, T9, T10, T11>(this Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, Task> action) =>
        async (v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11) =>
        {
            await action(
                arg1: v1,
                arg2: v2,
                arg3: v3,
                arg4: v4,
                arg5: v5,
                arg6: v6,
                arg7: v7,
                arg8: v8,
                arg9: v9,
                arg10: v10,
                arg11: v11);

            return Unit.Value;
        };

    /// <summary>
    ///     Converts an asynchronous action into a function producing <see cref="Unit" />, so that
    ///     it can be used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <typeparam name="T3">The type of the third argument.</typeparam>
    /// <typeparam name="T4">The type of the fourth argument.</typeparam>
    /// <typeparam name="T5">The type of the fifth argument.</typeparam>
    /// <typeparam name="T6">The type of the sixth argument.</typeparam>
    /// <typeparam name="T7">The type of the seventh argument.</typeparam>
    /// <typeparam name="T8">The type of the eighth argument.</typeparam>
    /// <typeparam name="T9">The type of the ninth argument.</typeparam>
    /// <typeparam name="T10">The type of the tenth argument.</typeparam>
    /// <typeparam name="T11">The type of the eleventh argument.</typeparam>
    /// <typeparam name="T12">The type of the twelfth argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its arguments and, once it has completed,
    ///     produces <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred beyond what the action itself defers: the returned function invokes
    ///     <paramref name="action" /> each time it is called, and its task completes when that
    ///     action's does.
    /// </remarks>
    public static Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, Task<Unit>> ToAsyncFunc<T1, T2, T3, T4,
        T5, T6, T7, T8, T9, T10, T11, T12>(this Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, Task> action) =>
        async (v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11, v12) =>
        {
            await action(
                arg1: v1,
                arg2: v2,
                arg3: v3,
                arg4: v4,
                arg5: v5,
                arg6: v6,
                arg7: v7,
                arg8: v8,
                arg9: v9,
                arg10: v10,
                arg11: v11,
                arg12: v12);

            return Unit.Value;
        };

    /// <summary>
    ///     Converts an asynchronous action into a function producing <see cref="Unit" />, so that
    ///     it can be used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <typeparam name="T3">The type of the third argument.</typeparam>
    /// <typeparam name="T4">The type of the fourth argument.</typeparam>
    /// <typeparam name="T5">The type of the fifth argument.</typeparam>
    /// <typeparam name="T6">The type of the sixth argument.</typeparam>
    /// <typeparam name="T7">The type of the seventh argument.</typeparam>
    /// <typeparam name="T8">The type of the eighth argument.</typeparam>
    /// <typeparam name="T9">The type of the ninth argument.</typeparam>
    /// <typeparam name="T10">The type of the tenth argument.</typeparam>
    /// <typeparam name="T11">The type of the eleventh argument.</typeparam>
    /// <typeparam name="T12">The type of the twelfth argument.</typeparam>
    /// <typeparam name="T13">The type of the thirteenth argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its arguments and, once it has completed,
    ///     produces <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred beyond what the action itself defers: the returned function invokes
    ///     <paramref name="action" /> each time it is called, and its task completes when that
    ///     action's does.
    /// </remarks>
    public static Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, Task<Unit>> ToAsyncFunc<T1, T2, T3,
        T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
        this Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, Task> action) =>
        async (v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11, v12, v13) =>
        {
            await action(
                arg1: v1,
                arg2: v2,
                arg3: v3,
                arg4: v4,
                arg5: v5,
                arg6: v6,
                arg7: v7,
                arg8: v8,
                arg9: v9,
                arg10: v10,
                arg11: v11,
                arg12: v12,
                arg13: v13);

            return Unit.Value;
        };

    /// <summary>
    ///     Converts an asynchronous action into a function producing <see cref="Unit" />, so that
    ///     it can be used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <typeparam name="T3">The type of the third argument.</typeparam>
    /// <typeparam name="T4">The type of the fourth argument.</typeparam>
    /// <typeparam name="T5">The type of the fifth argument.</typeparam>
    /// <typeparam name="T6">The type of the sixth argument.</typeparam>
    /// <typeparam name="T7">The type of the seventh argument.</typeparam>
    /// <typeparam name="T8">The type of the eighth argument.</typeparam>
    /// <typeparam name="T9">The type of the ninth argument.</typeparam>
    /// <typeparam name="T10">The type of the tenth argument.</typeparam>
    /// <typeparam name="T11">The type of the eleventh argument.</typeparam>
    /// <typeparam name="T12">The type of the twelfth argument.</typeparam>
    /// <typeparam name="T13">The type of the thirteenth argument.</typeparam>
    /// <typeparam name="T14">The type of the fourteenth argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its arguments and, once it has completed,
    ///     produces <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred beyond what the action itself defers: the returned function invokes
    ///     <paramref name="action" /> each time it is called, and its task completes when that
    ///     action's does.
    /// </remarks>
    public static Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, Task<Unit>> ToAsyncFunc<T1, T2,
        T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
        this Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, Task> action) =>
        async (v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11, v12, v13, v14) =>
        {
            await action(
                arg1: v1,
                arg2: v2,
                arg3: v3,
                arg4: v4,
                arg5: v5,
                arg6: v6,
                arg7: v7,
                arg8: v8,
                arg9: v9,
                arg10: v10,
                arg11: v11,
                arg12: v12,
                arg13: v13,
                arg14: v14);

            return Unit.Value;
        };

    /// <summary>
    ///     Converts an asynchronous action into a function producing <see cref="Unit" />, so that
    ///     it can be used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <typeparam name="T3">The type of the third argument.</typeparam>
    /// <typeparam name="T4">The type of the fourth argument.</typeparam>
    /// <typeparam name="T5">The type of the fifth argument.</typeparam>
    /// <typeparam name="T6">The type of the sixth argument.</typeparam>
    /// <typeparam name="T7">The type of the seventh argument.</typeparam>
    /// <typeparam name="T8">The type of the eighth argument.</typeparam>
    /// <typeparam name="T9">The type of the ninth argument.</typeparam>
    /// <typeparam name="T10">The type of the tenth argument.</typeparam>
    /// <typeparam name="T11">The type of the eleventh argument.</typeparam>
    /// <typeparam name="T12">The type of the twelfth argument.</typeparam>
    /// <typeparam name="T13">The type of the thirteenth argument.</typeparam>
    /// <typeparam name="T14">The type of the fourteenth argument.</typeparam>
    /// <typeparam name="T15">The type of the fifteenth argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its arguments and, once it has completed,
    ///     produces <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred beyond what the action itself defers: the returned function invokes
    ///     <paramref name="action" /> each time it is called, and its task completes when that
    ///     action's does.
    /// </remarks>
    public static Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, Task<Unit>> ToAsyncFunc<T1,
        T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(
        this Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, Task> action) =>
        async (v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11, v12, v13, v14, v15) =>
        {
            await action(
                arg1: v1,
                arg2: v2,
                arg3: v3,
                arg4: v4,
                arg5: v5,
                arg6: v6,
                arg7: v7,
                arg8: v8,
                arg9: v9,
                arg10: v10,
                arg11: v11,
                arg12: v12,
                arg13: v13,
                arg14: v14,
                arg15: v15);

            return Unit.Value;
        };

    /// <summary>
    ///     Converts an asynchronous action into a function producing <see cref="Unit" />, so that
    ///     it can be used where a function returning a value is required.
    /// </summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <typeparam name="T3">The type of the third argument.</typeparam>
    /// <typeparam name="T4">The type of the fourth argument.</typeparam>
    /// <typeparam name="T5">The type of the fifth argument.</typeparam>
    /// <typeparam name="T6">The type of the sixth argument.</typeparam>
    /// <typeparam name="T7">The type of the seventh argument.</typeparam>
    /// <typeparam name="T8">The type of the eighth argument.</typeparam>
    /// <typeparam name="T9">The type of the ninth argument.</typeparam>
    /// <typeparam name="T10">The type of the tenth argument.</typeparam>
    /// <typeparam name="T11">The type of the eleventh argument.</typeparam>
    /// <typeparam name="T12">The type of the twelfth argument.</typeparam>
    /// <typeparam name="T13">The type of the thirteenth argument.</typeparam>
    /// <typeparam name="T14">The type of the fourteenth argument.</typeparam>
    /// <typeparam name="T15">The type of the fifteenth argument.</typeparam>
    /// <typeparam name="T16">The type of the sixteenth argument.</typeparam>
    /// <param name="action">The action to convert.</param>
    /// <returns>
    ///     A function which calls <paramref name="action" /> with its arguments and, once it has completed,
    ///     produces <see cref="Unit.Value" />.
    /// </returns>
    /// <remarks>
    ///     Nothing is deferred beyond what the action itself defers: the returned function invokes
    ///     <paramref name="action" /> each time it is called, and its task completes when that
    ///     action's does.
    /// </remarks>
    public static Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, Task<Unit>>
        ToAsyncFunc<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>(
            this Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, Task> action) =>
        async (v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11, v12, v13, v14, v15, v16) =>
        {
            await action(
                arg1: v1,
                arg2: v2,
                arg3: v3,
                arg4: v4,
                arg5: v5,
                arg6: v6,
                arg7: v7,
                arg8: v8,
                arg9: v9,
                arg10: v10,
                arg11: v11,
                arg12: v12,
                arg13: v13,
                arg14: v14,
                arg15: v15,
                arg16: v16);

            return Unit.Value;
        };
}
