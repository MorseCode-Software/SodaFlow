using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace SodaFlow.Functional;

/// <summary>
///     Constructors for <see cref="Maybe{T}" /> with no type argument in the code.
/// </summary>
[PublicAPI]
public static class Maybe
{
    /// <summary>
    ///     The absence of a value, convertible to a <see cref="Maybe{T}" /> of any type.
    /// </summary>
    /// <remarks>
    ///     Assigning or returning this changes it implicitly to the <see cref="Maybe{T}" /> the
    ///     context needs, thus a caller never writes the type argument. Where no conversion
    ///     is available - a ternary whose other branch is also untyped, for instance - use
    ///     <see cref="Maybe{T}.None" /> as an alternative.
    /// </remarks>
    public static readonly NoneType None;

    /// <summary>
    ///     Creates a <see cref="Maybe{T}" /> containing the given value.
    /// </summary>
    /// <typeparam name="T">The type of the value, inferred from <paramref name="value" />.</typeparam>
    /// <param name="value">The value to contain.</param>
    /// <returns>A <see cref="Maybe{T}" /> containing <paramref name="value" />.</returns>
    /// <remarks>
    ///     This holds a <see langword="null" /> value as it holds each other value. It gives a
    ///     <see cref="Maybe{T}" /> which has a value, and that value is <see langword="null" />.
    /// </remarks>
    [Pure]
    public static Maybe<T> Some<T>(T value) => Maybe<T>.Some(value);

    /// <summary>
    ///     Creates a <see cref="Maybe{T}" /> containing the given value if a condition holds,
    ///     and one containing nothing if it does not.
    /// </summary>
    /// <typeparam name="T">The type of the value, inferred from <paramref name="value" />.</typeparam>
    /// <param name="condition">True to hold the value.</param>
    /// <param name="value">The value to contain when <paramref name="condition" /> holds.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing <paramref name="value" /> if
    ///     <paramref name="condition" /> is <see langword="true" />, and one containing no value
    ///     otherwise.
    /// </returns>
    /// <remarks>
    ///     This is the translation of an <c>if</c> that gives a value in one branch and
    ///     has nothing to give in the other. This code evaluates <paramref name="value" /> in each condition,
    ///     because it is an argument. Where that is not necessary, use
    ///     <see cref="SomeIf{T}(bool,System.Func{T})" />.
    /// </remarks>
    [Pure]
    public static Maybe<T> SomeIf<T>(bool condition, T value) => condition ? Some(value) : None;

    /// <summary>
    ///     Creates a <see cref="Maybe{T}" /> containing the result of the given function if a
    ///     condition holds, and one containing nothing if it does not.
    /// </summary>
    /// <typeparam name="T">The type of the value, inferred from <paramref name="valueFactory" />.</typeparam>
    /// <param name="condition">True to make a value and hold it.</param>
    /// <param name="valueFactory">Run to give the value when <paramref name="condition" /> holds.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the result of <paramref name="valueFactory" /> if
    ///     <paramref name="condition" /> is <see langword="true" />, and one containing no value
    ///     otherwise.
    /// </returns>
    /// <remarks>
    ///     <paramref name="valueFactory" /> runs only when <paramref name="condition" /> holds. Thus,
    ///     use this version when the value has a high cost, or when the value is only correct in that
    ///     condition.
    /// </remarks>
    [Pure]
    public static Maybe<T> SomeIf<T>(
        bool condition,
        [InstantHandle] Func<T> valueFactory) =>
        condition ? Some(valueFactory()) : None;

    /// <summary>
    ///     Creates a <see cref="Maybe{T}" /> containing the given reference, unless it is
    ///     <see langword="null" />.
    /// </summary>
    /// <typeparam name="T">The type of the value, inferred from <paramref name="value" />.</typeparam>
    /// <param name="value">The reference to contain if it is not <see langword="null" />.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing <paramref name="value" /> if it is not
    ///     <see langword="null" />, and one containing no value otherwise.
    /// </returns>
    /// <remarks>
    ///     This is the bridge from the older convention, where a <see langword="null" /> reference
    ///     stands for the absence of a value. It is deliberately not what <see cref="Some{T}" /> does.
    ///     That one holds <see langword="null" /> as it holds each other value. Thus, a
    ///     <see cref="Maybe{T}" /> of a nullable type can tell no value at all from a value that is
    ///     <see langword="null" />.
    /// </remarks>
    [Pure]
    public static Maybe<T> SomeNotNull<T>(T? value)
        where T : class =>
        value == null ? None : Some(value);

    /// <summary>
    ///     Creates a <see cref="Maybe{T}" /> containing the value of the given
    ///     <see cref="System.Nullable{T}" />, if it has one.
    /// </summary>
    /// <typeparam name="T">The underlying type of <paramref name="value" />.</typeparam>
    /// <param name="value">The nullable value to change.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the value of <paramref name="value" /> if it has
    ///     one, and one containing no value otherwise.
    /// </returns>
    [Pure]
    public static Maybe<T> SomeNotNull<T>(T? value)
        where T : struct =>
        value.HasValue ? Some(value.Value) : None;

    /// <summary>
    ///     Runs a method of the <c>bool Try...(out TResult)</c> shape and returns what it produced.
    /// </summary>
    /// <typeparam name="TResult">The type of the value the method produces.</typeparam>
    /// <param name="tryGet">The method to run.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the output of <paramref name="tryGet" /> if it
    ///     returned <see langword="true" />, and one containing no value otherwise.
    /// </returns>
    /// <remarks>
    ///     The wrappers this library ships - on <see cref="StringExtensionMethods" /> and
    ///     <see cref="ReadOnlyDictionaryExtensionMethods" /> - cover the usual conditions. This is for
    ///     the ones it does not know about: your own, or a different library.
    /// </remarks>
    [Pure]
    public static Maybe<TResult> FromTryGet<TResult>([InstantHandle] TryGet<TResult> tryGet) =>
        tryGet(out TResult result) ? Some(result) : None;

    /// <summary>
    ///     Runs a method of the <c>bool Try...(T, out TResult)</c> shape against the given input and
    ///     returns what it produced.
    /// </summary>
    /// <typeparam name="T">The type of the input.</typeparam>
    /// <typeparam name="TResult">The type of the value the method produces.</typeparam>
    /// <param name="value">The input to give to <paramref name="tryGet" />.</param>
    /// <param name="tryGet">The method to run.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the output of <paramref name="tryGet" /> if it
    ///     returned <see langword="true" />, and one containing no value otherwise.
    /// </returns>
    /// <remarks>
    ///     A caller must write the two type arguments. A method group carries no type of its own for
    ///     type inference to work from:
    ///     <c>Maybe.FromTryGet&lt;string, int&gt;(s, int.TryParse)</c>.
    /// </remarks>
    [Pure]
    public static Maybe<TResult> FromTryGet<T, TResult>(
        T value,
        [InstantHandle] TryGet<T, TResult> tryGet) =>
        tryGet(value: value, result: out TResult result) ? Some(result) : None;

    /// <summary>
    ///     Runs a method of the <c>bool Try...(T1, T2, out TResult)</c> shape against the given
    ///     inputs and returns what it produced.
    /// </summary>
    /// <typeparam name="T1">The type of the first input.</typeparam>
    /// <typeparam name="T2">The type of the second input.</typeparam>
    /// <typeparam name="TResult">The type of the value the method produces.</typeparam>
    /// <param name="value1">The first input to give to <paramref name="tryGet" />.</param>
    /// <param name="value2">The second input to give to <paramref name="tryGet" />.</param>
    /// <param name="tryGet">The method to run.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the output of <paramref name="tryGet" /> if it
    ///     returned <see langword="true" />, and one containing no value otherwise.
    /// </returns>
    [Pure]
    public static Maybe<TResult> FromTryGet<T1, T2, TResult>(
        T1 value1,
        T2 value2,
        [InstantHandle] TryGet<T1, T2, TResult> tryGet) =>
        tryGet(value1: value1, value2: value2, result: out TResult result) ? Some(result) : None;

    /// <summary>
    ///     Runs a method of the <c>bool Try...(T1, T2, T3, out TResult)</c> shape against the given
    ///     inputs and returns what it produced.
    /// </summary>
    /// <typeparam name="T1">The type of the first input.</typeparam>
    /// <typeparam name="T2">The type of the second input.</typeparam>
    /// <typeparam name="T3">The type of the third input.</typeparam>
    /// <typeparam name="TResult">The type of the value the method produces.</typeparam>
    /// <param name="value1">The first input to give to <paramref name="tryGet" />.</param>
    /// <param name="value2">The second input to give to <paramref name="tryGet" />.</param>
    /// <param name="value3">The third input to give to <paramref name="tryGet" />.</param>
    /// <param name="tryGet">The method to run.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the output of <paramref name="tryGet" /> if it
    ///     returned <see langword="true" />, and one containing no value otherwise.
    /// </returns>
    [Pure]
    public static Maybe<TResult> FromTryGet<T1, T2, T3, TResult>(
        T1 value1,
        T2 value2,
        T3 value3,
        [InstantHandle] TryGet<T1, T2, T3, TResult> tryGet) =>
        tryGet(value1: value1, value2: value2, value3: value3, result: out TResult result) ? Some(result) : None;

    /// <summary>
    ///     The type of <see cref="Maybe.None" />: a value carrying no information, whose only
    ///     purpose is to change to a <see cref="Maybe{T}" /> containing nothing.
    /// </summary>
    public struct NoneType;
}

/// <summary>
///     A value that is there, or that is not there.
/// </summary>
/// <typeparam name="T">The type of the value, when there is one.</typeparam>
/// <remarks>
///     Unlike <see cref="System.Nullable{T}" /> this works for reference types and for value
///     types. Unlike a <see langword="null" /> reference, the type itself shows that there can be
///     no value.
///     There is no property that hands the value out unchecked. Read the value with
///     <see cref="Match{TResult}" />, or with one of the helpers on it, thus the caller must
///     answer for the condition with no value.
///     This is a struct, so <see langword="default" /> is a correct instance and holds nothing:
///     the same as <see cref="None" />.
/// </remarks>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public struct Maybe<T> : IMaybe, IEquatable<Maybe<T>>, IComparable<Maybe<T>>
{
    private readonly bool hasValue;
    private readonly T? value;

    private Maybe(T value)
    {
        this.hasValue = true;
        this.value = value;
    }

    #region Type Constructors

    /// <summary>
    ///     Creates a <see cref="Maybe{T}" /> containing the given value.
    /// </summary>
    /// <param name="value">The value to contain.</param>
    /// <returns>A <see cref="Maybe{T}" /> containing <paramref name="value" />.</returns>
    /// <remarks>
    ///     <see cref="Maybe.Some{T}" /> is usually more convenient, since it infers the type
    ///     argument from the value.
    /// </remarks>
    [Pure]
    public static Maybe<T> Some(T value) => new(value);

    /// <summary>
    ///     A <see cref="Maybe{T}" /> containing no value.
    /// </summary>
    /// <remarks>
    ///     <see cref="Maybe.None" /> is usually more convenient, since it changes to whichever
    ///     <see cref="Maybe{T}" /> the context calls for. Use this where no such conversion is
    ///     available.
    /// </remarks>
    public static readonly Maybe<T> None;

    #endregion

    #region Base Functionality

    T1 IMaybe.Match<T1>(Func<object?, T1> onSome, Func<T1> onNone) =>
        this.Match(onSome: v => onSome(v), onNone: onNone);

    /// <summary>
    ///     Runs one function when there is a value, and a different one when there is none.
    /// </summary>
    /// <typeparam name="TResult">The type each of the two functions returns.</typeparam>
    /// <param name="onSome">Run with the contained value when there is one.</param>
    /// <param name="onNone">Run when there is no value.</param>
    /// <returns>Whatever the function that was run returned.</returns>
    /// <remarks>
    ///     This is the only member that reads the contained value, and it expresses each other member
    ///     here. This calls one function of the two, before this method
    ///     returns.
    /// </remarks>
    public TResult Match<TResult>(Func<T, TResult> onSome, Func<TResult> onNone) =>
        // ReSharper disable once NullableWarningSuppressionIsUsed - If hasValue is true, then value will have a
        // non-null value.
        this.hasValue ? onSome(this.value!) : onNone();

    #endregion

    #region Helper Methods

    // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
    /// <summary>
    ///     Runs one action when there is a value, and a different action when there is none.
    /// </summary>
    /// <param name="onSome">Run with the contained value when there is one.</param>
    /// <param name="onNone">Run when there is no value.</param>
    public void MatchVoid(
        [InstantHandle] Action<T> onSome,
        [InstantHandle] Action onNone) =>
        this.Match(onSome: onSome.ToFunc(), onNone: onNone.ToFunc());

    // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
    /// <summary>
    ///     Runs an action with the contained value when there is one, and otherwise does nothing.
    /// </summary>
    /// <param name="onSome">Run with the contained value when there is one.</param>
    public void MatchSome([InstantHandle] Action<T> onSome) =>
        this.MatchVoid(
            onSome: onSome,
            onNone: static () =>
            {
            });

    // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
    /// <summary>
    ///     Runs an action when there is no value, and otherwise does nothing.
    /// </summary>
    /// <param name="onNone">Run when there is no value.</param>
    public void MatchNone([InstantHandle] Action onNone) =>
        this.MatchVoid(
            onSome: static _ =>
            {
            },
            onNone: onNone);

    /// <summary>
    ///     Runs one asynchronous function when there is a value, and a different one when there is
    ///     none, and gives the result.
    /// </summary>
    /// <typeparam name="TResult">The type each of the two functions produces.</typeparam>
    /// <param name="onSome">Run with the contained value when there is one.</param>
    /// <param name="onNone">Run when there is no value.</param>
    /// <returns>The task returned by whichever function was run.</returns>
    /// <remarks>
    ///     Only the selected function runs. The task from this call is its task, and not a wrapper.
    ///     Thus, a failure shows as a fault on that task, and not as an exception from this call.
    /// </remarks>
    public Task<TResult> MatchAsync<TResult>(
        [InstantHandle] Func<T, Task<TResult>> onSome,
        [InstantHandle] Func<Task<TResult>> onNone) =>
        this.Match(onSome: onSome, onNone: onNone);

    /// <summary>
    ///     Runs one asynchronous action when there is a value, and a different one when there is none.
    /// </summary>
    /// <param name="onSome">Run with the contained value when there is one.</param>
    /// <param name="onNone">Run when there is no value.</param>
    /// <returns>A task that completes at the end of the selected action.</returns>
    public Task MatchAsyncVoid(
        [InstantHandle] Func<T, Task> onSome,
        [InstantHandle] Func<Task> onNone) =>
        this.MatchAsync(onSome: onSome.ToAsyncFunc(), onNone: onNone.ToAsyncFunc());

    /// <summary>
    ///     Runs an asynchronous action with the contained value when there is one, and otherwise
    ///     does nothing.
    /// </summary>
    /// <param name="onSome">Run with the contained value when there is one.</param>
    /// <returns>
    ///     A task that completes at the end of the action, or a completed task if no
    ///     value is there.
    /// </returns>
    public Task MatchSomeAsync([InstantHandle] Func<T, Task> onSome) =>
        this.MatchAsyncVoid(onSome: onSome, onNone: static () => Task.FromResult(false));

    /// <summary>
    ///     Runs an asynchronous action when there is no value, and otherwise does nothing.
    /// </summary>
    /// <param name="onNone">Run when there is no value.</param>
    /// <returns>
    ///     A task that completes at the end of the action, or a completed task if a
    ///     value is there.
    /// </returns>
    public Task MatchNoneAsync([InstantHandle] Func<Task> onNone) =>
        this.MatchAsyncVoid(onSome: static _ => Task.FromResult(false), onNone: onNone);

    /// <summary>
    ///     Map the <see cref="Maybe{T}" /> value using a mapping function if a value exists, or propogate the
    ///     None value if it does not.
    /// </summary>
    /// <param name="f">The function to transform this <see cref="Maybe{T}" />.</param>
    /// <typeparam name="TResult">The type of the maybe result value.</typeparam>
    /// <returns>
    ///     The <see cref="Maybe{TResult}" /> which results from transforming this <see cref="Maybe{T}" /> using
    ///     <paramref name="f" />.
    /// </returns>
    public Maybe<TResult> Map<TResult>([InstantHandle] Func<T, TResult> f) => this.Bind(v => Maybe.Some(f(v)));

    /// <summary>
    ///     Gives true when there is a value.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> if this contains a value, and <see langword="false" /> otherwise.
    /// </returns>
    /// <remarks>
    ///     There is no matching property to read the value with, deliberately. This is for the cases
    ///     where only the existence of a value matters. Where the value is necessary, use
    ///     <see cref="Match{TResult}" />, thus the code answers the two cases.
    /// </remarks>
    [Pure]
    public bool HasValue() => this.Match(onSome: static _ => true, onNone: static () => false);

    // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
    void IMaybe.MatchVoid(Action<object?> onSome, Action onNone) =>
        this.Upcast<IMaybe>().Match(onSome: onSome.ToFunc(), onNone: onNone.ToFunc());

    // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
    void IMaybe.MatchSome(Action<object?> onSome) =>
        this.Upcast<IMaybe>()
            .MatchVoid(
                onSome: onSome,
                onNone: static () =>
                {
                });

    // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
    void IMaybe.MatchNone(Action onNone) =>
        this.Upcast<IMaybe>()
            .MatchVoid(
                onSome: static _ =>
                {
                },
                onNone: onNone);

    Task<TResult> IMaybe.MatchAsync<TResult>(Func<object?, Task<TResult>> onSome, Func<Task<TResult>> onNone) =>
        this.Upcast<IMaybe>().Match(onSome: onSome, onNone: onNone);

    Task IMaybe.MatchAsyncVoid(Func<object?, Task> onSome, Func<Task> onNone) =>
        this.Upcast<IMaybe>().MatchAsync(onSome: onSome.ToAsyncFunc(), onNone: onNone.ToAsyncFunc());

    Task IMaybe.MatchSomeAsync(Func<object?, Task> onSome) =>
        this.Upcast<IMaybe>().MatchAsyncVoid(onSome: onSome, onNone: static () => Task.FromResult(false));

    Task IMaybe.MatchNoneAsync(Func<Task> onNone) =>
        this.Upcast<IMaybe>().MatchAsyncVoid(onSome: static _ => Task.FromResult(false), onNone: onNone);

    #endregion

    /// <summary>
    ///     Changes the untyped <see cref="Maybe.None" /> into a <see cref="Maybe{T}" /> of this
    ///     type containing no value.
    /// </summary>
    /// <param name="_">The untyped absence of a value. It carries no information.</param>
    /// <returns>A <see cref="Maybe{T}" /> containing no value.</returns>
    /// <remarks>
    ///     This is what lets code give <see cref="Maybe.None" /> as a result or a value wherever a
    ///     <see cref="Maybe{T}" />, with no name for the type argument.
    /// </remarks>
    public static implicit operator Maybe<T>(Maybe.NoneType _) => None;

    /// <summary>
    ///     Gives true when two instances contain equal values, or the two contain none.
    /// </summary>
    /// <param name="x">The first instance.</param>
    /// <param name="y">The second instance.</param>
    /// <returns>
    ///     <see langword="true" /> if the two contain no value, or the two contain values which
    ///     <see cref="EqualityComparer{T}.Default" /> considers equal.
    /// </returns>
    public static bool operator ==(Maybe<T> x, Maybe<T> y) =>
        // ReSharper disable NullableWarningSuppressionIsUsed - .NET 4.7.2 and .NET Standard 2.0 did not define these
        // parameters as being nullable as they should have.
        x.hasValue == y.hasValue && EqualityComparer<T>.Default.Equals(x: x.value!, y: y.value!);
    // ReSharper restore NullableWarningSuppressionIsUsed

    /// <summary>
    ///     Gives true when two instances are different. This negates <see cref="op_Equality" />.
    /// </summary>
    /// <param name="x">The first instance.</param>
    /// <param name="y">The second instance.</param>
    /// <returns>
    ///     <see langword="true" /> if one contains a value and the other does not, or if the two
    ///     contain values which are not equal.
    /// </returns>
    public static bool operator !=(Maybe<T> x, Maybe<T> y) => !(x == y);

    /// <summary>
    ///     Gives true when the given object is a <see cref="Maybe{T}" /> of this type equal to
    ///     this one.
    /// </summary>
    /// <param name="obj">The object to compare against.</param>
    /// <returns>
    ///     <see langword="true" /> if <paramref name="obj" /> is a <see cref="Maybe{T}" /> of the
    ///     same type which <see cref="op_Equality" /> considers equal to this one.
    /// </returns>
    /// <remarks>
    ///     A <see cref="Maybe{T}" /> is never equal to the bare value it contains, only to a second
    ///     <see cref="Maybe{T}" />.
    /// </remarks>
    public override bool Equals(object? obj) => obj is Maybe<T> m && this == m;

    /// <summary>
    ///     Gives true when the given instance is equal to this one.
    /// </summary>
    /// <param name="other">The instance to compare against.</param>
    /// <returns>
    ///     <see langword="true" /> if <see cref="op_Equality" /> considers the two equal.
    /// </returns>
    /// <remarks>
    ///     The same compare as <see cref="op_Equality" />, with the name
    ///     <see cref="EqualityComparer{T}.Default" /> looks for. Without it, this struct has no
    ///     <see cref="IEquatable{T}" /> and sends each compare through
    ///     <see cref="Equals(object)" />, which boxes the two operands. That occurs on each
    ///     <see cref="System.Linq.Enumerable.Distinct{TSource}(IEnumerable{TSource})" />,
    ///     <see cref="System.Linq.Enumerable.Contains{TSource}(IEnumerable{TSource},TSource)" />,
    ///     <see cref="List{T}.IndexOf(T)" />, and dictionary lookup. A struct is how this type avoids
    ///     an allocation, and that made it allocate in the collection-heavy code that notices.
    /// </remarks>
    // ReSharper disable once InheritdocConsiderUsage
    public bool Equals(Maybe<T> other) => this == other;

    /// <summary>
    ///     Puts this instance in order against a second one, and a missing value comes first.
    /// </summary>
    /// <param name="other">The instance to compare against.</param>
    /// <returns>
    ///     A negative number when this sorts before <paramref name="other" />, and a positive number
    ///     when this sorts after it. Zero when no one of the two sorts before the other.
    /// </returns>
    /// <remarks>
    ///     No value sorts before each value. That is how <see cref="Comparer{T}.Default" /> puts a
    ///     <see cref="System.Nullable{T}" /> in order, thus it is the answer with the fewest
    ///     surprises. This puts two instances that each have a value in order by
    ///     <see cref="Comparer{T}.Default" /> for <typeparamref name="T" />, which throws when
    ///     <typeparamref name="T" /> has no ordering. That is the same failure, at the same point, as
    ///     an order of bare <typeparamref name="T" /> values gives.
    ///     This reads <see cref="Comparer{T}.Default" /> where
    ///     <see cref="op_Equality" /> reads <see cref="EqualityComparer{T}.Default" />. Take a type
    ///     whose ordering and equality disagree, and <see cref="string" /> is one: the culture gives
    ///     its order, and an ordinal compare gives its equality. Thus, a zero result here does not have
    ///     to mean that <see cref="op_Equality" /> gives <see langword="true" />. This inherits that
    ///     from those two comparers, and an order of bare <typeparamref name="T" /> values does the
    ///     same.
    ///     There are deliberately no <c>&lt;</c> and <c>&gt;</c> operators to go with this.
    ///     <see cref="System.Nullable{T}" /> has them, and they are a trap. They answer
    ///     <see langword="false" /> in the two directions when one side is missing, thus
    ///     <c>!(a &lt; b)</c> stops to mean <c>a &gt;= b</c>. A sort is what needs an ordering, and a
    ///     sort uses this method.
    /// </remarks>
    // ReSharper disable once InheritdocConsiderUsage
    public int CompareTo(Maybe<T> other) =>
        this.Match(
            onSome: v =>
                other.Match(
                    onSome: otherValue => Comparer<T>.Default.Compare(x: v, y: otherValue),
                    onNone: static () => 1),
            onNone: () => other.Match(onSome: static _ => -1, onNone: static () => 0));

    /// <summary>
    ///     Gives a hash code that agrees with <see cref="op_Equality" />.
    /// </summary>
    /// <returns>A hash code for this instance.</returns>
    public override int GetHashCode()
    {
        unchecked
        {
            int hashCode = this.hasValue.GetHashCode();

            if (this.value is not null)
            {
                hashCode = (hashCode * 397) ^ EqualityComparer<T>.Default.GetHashCode(this.value);
            }

            return hashCode;
        }
    }

    /// <summary>
    ///     Returns a readable description of this instance, for diagnostics.
    /// </summary>
    /// <returns>
    ///     <c>{Some: value}</c> when there is a value, and <c>{None}</c> when one is not.
    /// </returns>
    public override string ToString() =>
        this.Match(onSome: static v => $"{{Some: {v}}}", onNone: static () => "{None}");
}
