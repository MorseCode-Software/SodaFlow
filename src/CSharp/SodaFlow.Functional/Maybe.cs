using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SodaFlow.Functional
{
    /// <summary>
    ///     Constructors for <see cref="Maybe{T}" /> which do not require the type argument to be
    ///     written out.
    /// </summary>
    public static class Maybe
    {
        /// <summary>
        ///     Creates a <see cref="Maybe{T}" /> containing the given value.
        /// </summary>
        /// <typeparam name="T">The type of the value, inferred from <paramref name="value" />.</typeparam>
        /// <param name="value">The value to contain.</param>
        /// <returns>A <see cref="Maybe{T}" /> containing <paramref name="value" />.</returns>
        /// <remarks>
        ///     A <see langword="null" /> value is contained like any other: this produces a
        ///     <see cref="Maybe{T}" /> which has a value, and that value is <see langword="null" />.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public static Maybe<T> Some<T>(T value) => Maybe<T>.Some(value);

        /// <summary>
        ///     The absence of a value, convertible to a <see cref="Maybe{T}" /> of any type.
        /// </summary>
        /// <remarks>
        ///     Assigning or returning this converts it implicitly to the <see cref="Maybe{T}" /> the
        ///     context calls for, so the type argument never has to be written out. Where no conversion
        ///     is available - a ternary whose other branch is also untyped, for instance - use
        ///     <see cref="Maybe{T}.None" /> instead.
        /// </remarks>
        public static readonly NoneType None = new NoneType();

        /// <summary>
        ///     The type of <see cref="Maybe.None" />: a value carrying no information, whose only
        ///     purpose is to convert to a <see cref="Maybe{T}" /> containing nothing.
        /// </summary>
        public struct NoneType
        {
        }
    }

    /// <summary>
    ///     A value which may or may not be present.
    /// </summary>
    /// <typeparam name="T">The type of the value, when there is one.</typeparam>
    /// <remarks>
    ///     Unlike <see cref="System.Nullable{T}" /> this works for reference types as well as value
    ///     types, and unlike a <see langword="null" /> reference it says in the type whether the
    ///     absence of a value is expected.
    ///
    ///     There is no property that hands the value out unchecked. Reach the value with
    ///     <see cref="Match{TResult}" /> or one of the helpers built on it, so that the case where
    ///     there is none has to be answered for.
    ///
    ///     This is a struct, so <see langword="default" /> is a valid instance and contains nothing -
    ///     the same as <see cref="None" />.
    /// </remarks>
    public struct Maybe<T> : IMaybe
    {
        private readonly bool hasValue;
        private readonly T value;

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
        [JetBrains.Annotations.Pure]
        public static Maybe<T> Some(T value) => new Maybe<T>(value);

        /// <summary>
        ///     A <see cref="Maybe{T}" /> containing no value.
        /// </summary>
        /// <remarks>
        ///     <see cref="Maybe.None" /> is usually more convenient, since it converts to whichever
        ///     <see cref="Maybe{T}" /> the context calls for. Use this where no such conversion is
        ///     available.
        /// </remarks>
        public static readonly Maybe<T> None = new Maybe<T>();

        #endregion

        #region Base Functionality

        T1 IMaybe.Match<T1>(Func<object, T1> onSome, Func<T1> onNone) => this.Match(v => onSome(v), onNone);

        /// <summary>
        ///     Runs one of two functions depending on whether a value is present, and returns its result.
        /// </summary>
        /// <typeparam name="TResult">The type each of the two functions returns.</typeparam>
        /// <param name="onSome">Run with the contained value when one is present.</param>
        /// <param name="onNone">Run when no value is present.</param>
        /// <returns>Whatever the function that was run returned.</returns>
        /// <remarks>
        ///     This is the only way the contained value is reached, and every other member here is
        ///     expressed in terms of it. Exactly one of the two functions is called, before this method
        ///     returns.
        /// </remarks>
        public TResult Match<TResult>(Func<T, TResult> onSome, Func<TResult> onNone) =>
            this.hasValue ? onSome(this.value) : onNone();

        #endregion

        #region Helper Methods

        // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
        /// <summary>
        ///     Runs one of two actions depending on whether a value is present.
        /// </summary>
        /// <param name="onSome">Run with the contained value when one is present.</param>
        /// <param name="onNone">Run when no value is present.</param>
        public void MatchVoid(
            [JetBrains.Annotations.InstantHandle] Action<T> onSome,
            [JetBrains.Annotations.InstantHandle] Action onNone) =>
            this.Match(onSome.ToFunc(), onNone.ToFunc());

        // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
        /// <summary>
        ///     Runs an action with the contained value if one is present, and otherwise does nothing.
        /// </summary>
        /// <param name="onSome">Run with the contained value when one is present.</param>
        public void MatchSome([JetBrains.Annotations.InstantHandle] Action<T> onSome) =>
            this.MatchVoid(onSome, () => { });

        // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
        /// <summary>
        ///     Runs an action if no value is present, and otherwise does nothing.
        /// </summary>
        /// <param name="onNone">Run when no value is present.</param>
        public void MatchNone([JetBrains.Annotations.InstantHandle] Action onNone) =>
            this.MatchVoid(_ => { }, onNone);

        /// <summary>
        ///     Runs one of two asynchronous functions depending on whether a value is present, and
        ///     returns its result.
        /// </summary>
        /// <typeparam name="TResult">The type each of the two functions produces.</typeparam>
        /// <param name="onSome">Run with the contained value when one is present.</param>
        /// <param name="onNone">Run when no value is present.</param>
        /// <returns>The task returned by whichever function was run.</returns>
        /// <remarks>
        ///     Only the selected function is invoked; the returned task is its task, not a wrapper, so
        ///     failures surface as that task faulting rather than as an exception from this call.
        /// </remarks>
        public Task<TResult> MatchAsync<TResult>(
            [JetBrains.Annotations.InstantHandle] Func<T, Task<TResult>> onSome,
            [JetBrains.Annotations.InstantHandle] Func<Task<TResult>> onNone) =>
            this.Match(onSome, onNone);

        /// <summary>
        ///     Runs one of two asynchronous actions depending on whether a value is present.
        /// </summary>
        /// <param name="onSome">Run with the contained value when one is present.</param>
        /// <param name="onNone">Run when no value is present.</param>
        /// <returns>A task which completes when the selected action has completed.</returns>
        public Task MatchAsyncVoid(
            [JetBrains.Annotations.InstantHandle] Func<T, Task> onSome,
            [JetBrains.Annotations.InstantHandle] Func<Task> onNone) =>
            this.MatchAsync(onSome.ToAsyncFunc(), onNone.ToAsyncFunc());

        /// <summary>
        ///     Runs an asynchronous action with the contained value if one is present, and otherwise
        ///     does nothing.
        /// </summary>
        /// <param name="onSome">Run with the contained value when one is present.</param>
        /// <returns>
        ///     A task which completes when the action has completed, or an already completed task if no
        ///     value is present.
        /// </returns>
        public Task MatchSomeAsync([JetBrains.Annotations.InstantHandle] Func<T, Task> onSome) =>
            this.MatchAsyncVoid(onSome, () => Task.FromResult(false));
        
        /// <summary>
        ///     Runs an asynchronous action if no value is present, and otherwise does nothing.
        /// </summary>
        /// <param name="onNone">Run when no value is present.</param>
        /// <returns>
        ///     A task which completes when the action has completed, or an already completed task if a
        ///     value is present.
        /// </returns>
        public Task MatchNoneAsync([JetBrains.Annotations.InstantHandle] Func<Task> onNone) =>
            this.MatchAsyncVoid(_ => Task.FromResult(false), onNone);

        /// <summary>
        ///     Map the <see cref="Maybe{T}" /> value using a mapping function if a value exists, or propogate the None value if
        ///     it does not.
        /// </summary>
        /// <param name="f">The function to transform this <see cref="Maybe{T}" />.</param>
        /// <typeparam name="TResult">The type of the maybe result value.</typeparam>
        /// <returns>
        ///     The <see cref="Maybe{TResult}" /> which results from transforming this <see cref="Maybe{T}" /> using
        ///     <paramref name="f" />.
        /// </returns>
        public Maybe<TResult> Map<TResult>([JetBrains.Annotations.InstantHandle] Func<T, TResult> f) =>
            this.Bind(v => Maybe.Some(f(v)));

        /// <summary>
        ///     Returns whether a value is present.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if this contains a value, and <see langword="false" /> otherwise.
        /// </returns>
        /// <remarks>
        ///     There is no matching property to read the value with, deliberately. This is for the cases
        ///     where only the presence matters; where the value is wanted, use
        ///     <see cref="Match{TResult}" /> so that both cases are handled.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool HasValue() => this.Match(v => true, () => false);

        // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
        void IMaybe.MatchVoid(Action<object> onSome, Action onNone) =>
            this.Upcast<IMaybe>().Match(onSome.ToFunc(), onNone.ToFunc());

        // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
        void IMaybe.MatchSome(Action<object> onSome) => this.Upcast<IMaybe>().MatchVoid(onSome, () => { });

        // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
        void IMaybe.MatchNone(Action onNone) => this.Upcast<IMaybe>().MatchVoid(_ => { }, onNone);

        Task<TResult> IMaybe.MatchAsync<TResult>(Func<object, Task<TResult>> onSome, Func<Task<TResult>> onNone) =>
            this.Upcast<IMaybe>().Match(onSome, onNone);

        Task IMaybe.MatchAsyncVoid(Func<object, Task> onSome, Func<Task> onNone) =>
            this.Upcast<IMaybe>().MatchAsync(onSome.ToAsyncFunc(), onNone.ToAsyncFunc());

        Task IMaybe.MatchSomeAsync(Func<object, Task> onSome) =>
            this.Upcast<IMaybe>().MatchAsyncVoid(onSome, () => Task.FromResult(false));

        Task IMaybe.MatchNoneAsync(Func<Task> onNone) =>
            this.Upcast<IMaybe>().MatchAsyncVoid(_ => Task.FromResult(false), onNone);

        #endregion

        /// <summary>
        ///     Converts the untyped <see cref="Maybe.None" /> into a <see cref="Maybe{T}" /> of this
        ///     type containing no value.
        /// </summary>
        /// <param name="_">The untyped absence of a value; it carries no information.</param>
        /// <returns>A <see cref="Maybe{T}" /> containing no value.</returns>
        /// <remarks>
        ///     This is what lets <see cref="Maybe.None" /> be returned or assigned wherever a
        ///     <see cref="Maybe{T}" /> is expected, without naming the type argument.
        /// </remarks>
        public static implicit operator Maybe<T>(Maybe.NoneType _) => None;

        /// <summary>
        ///     Determines whether two instances contain equal values, or both contain none.
        /// </summary>
        /// <param name="x">The first instance.</param>
        /// <param name="y">The second instance.</param>
        /// <returns>
        ///     <see langword="true" /> if both contain no value, or both contain values which
        ///     <see cref="EqualityComparer{T}.Default" /> considers equal.
        /// </returns>
        public static bool operator ==(Maybe<T> x, Maybe<T> y) =>
            x.hasValue == y.hasValue && EqualityComparer<T>.Default.Equals(x.value, y.value);

        /// <summary>
        ///     Determines whether two instances differ, by negating <see cref="op_Equality" />.
        /// </summary>
        /// <param name="x">The first instance.</param>
        /// <param name="y">The second instance.</param>
        /// <returns>
        ///     <see langword="true" /> if one contains a value and the other does not, or if both
        ///     contain values which are not equal.
        /// </returns>
        public static bool operator !=(Maybe<T> x, Maybe<T> y) => !(x == y);

        /// <summary>
        ///     Determines whether the given object is a <see cref="Maybe{T}" /> of this type equal to
        ///     this one.
        /// </summary>
        /// <param name="obj">The object to compare against.</param>
        /// <returns>
        ///     <see langword="true" /> if <paramref name="obj" /> is a <see cref="Maybe{T}" /> of the
        ///     same type which <see cref="op_Equality" /> considers equal to this one.
        /// </returns>
        /// <remarks>
        ///     A <see cref="Maybe{T}" /> is never equal to the bare value it contains, only to another
        ///     <see cref="Maybe{T}" />.
        /// </remarks>
        public override bool Equals(object obj) => obj is Maybe<T> m && this == m;

        /// <summary>
        ///     Returns a hash code consistent with <see cref="op_Equality" />.
        /// </summary>
        /// <returns>A hash code for this instance.</returns>
        public override int GetHashCode() =>
            (this.hasValue.GetHashCode() * 397) ^ EqualityComparer<T>.Default.GetHashCode(this.value);

        /// <summary>
        ///     Returns a readable description of this instance, for diagnostics.
        /// </summary>
        /// <returns>
        ///     <c>{Some: value}</c> when a value is present, and <c>{None}</c> when one is not.
        /// </returns>
        public override string ToString() => this.Match(v => $"{{Some: {v}}}", () => "{None}");
    }
}