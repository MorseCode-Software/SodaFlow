using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SodaFlow.Functional
{
    /// <summary>
    ///     Constructors for the <see cref="Either{T1,T2}" /> family which do not require every
    ///     type argument to be written out.
    /// </summary>
    /// <remarks>
    ///     An either is named by all of its cases, so constructing one directly means repeating
    ///     types the surrounding code has already established. The constructors here instead
    ///     return a small value marked with the position it belongs in, which converts
    ///     implicitly into whichever either is being assigned or returned.
    /// </remarks>
    public static class Either
    {
        /// <summary>
        ///     Marks a value as belonging in the first position of an either.
        /// </summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The value.</param>
        /// <returns>
        ///     A marked value which converts implicitly into any either whose first type argument
        ///     is <typeparamref name="T" />.
        /// </returns>
        /// <remarks>
        ///     This is what lets an either be returned or assigned without naming all of its type
        ///     arguments: the marked value carries only the position and the value, and the
        ///     conversion at the assignment or return supplies the rest. Where no such conversion is
        ///     available, use the either's own <c>First</c> instead.
        /// </remarks>
        public static EitherFirst<T> First<T>(T value) => new EitherFirst<T>(value);
        /// <summary>
        ///     Marks a value as belonging in the second position of an either.
        /// </summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The value.</param>
        /// <returns>
        ///     A marked value which converts implicitly into any either whose second type argument
        ///     is <typeparamref name="T" />.
        /// </returns>
        /// <remarks>
        ///     This is what lets an either be returned or assigned without naming all of its type
        ///     arguments: the marked value carries only the position and the value, and the
        ///     conversion at the assignment or return supplies the rest. Where no such conversion is
        ///     available, use the either's own <c>Second</c> instead.
        /// </remarks>
        public static EitherSecond<T> Second<T>(T value) => new EitherSecond<T>(value);
        /// <summary>
        ///     Marks a value as belonging in the third position of an either.
        /// </summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The value.</param>
        /// <returns>
        ///     A marked value which converts implicitly into any either whose third type argument
        ///     is <typeparamref name="T" />.
        /// </returns>
        /// <remarks>
        ///     This is what lets an either be returned or assigned without naming all of its type
        ///     arguments: the marked value carries only the position and the value, and the
        ///     conversion at the assignment or return supplies the rest. Where no such conversion is
        ///     available, use the either's own <c>Third</c> instead.
        /// </remarks>
        public static EitherThird<T> Third<T>(T value) => new EitherThird<T>(value);
        /// <summary>
        ///     Marks a value as belonging in the fourth position of an either.
        /// </summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The value.</param>
        /// <returns>
        ///     A marked value which converts implicitly into any either whose fourth type argument
        ///     is <typeparamref name="T" />.
        /// </returns>
        /// <remarks>
        ///     This is what lets an either be returned or assigned without naming all of its type
        ///     arguments: the marked value carries only the position and the value, and the
        ///     conversion at the assignment or return supplies the rest. Where no such conversion is
        ///     available, use the either's own <c>Fourth</c> instead.
        /// </remarks>
        public static EitherFourth<T> Fourth<T>(T value) => new EitherFourth<T>(value);
        /// <summary>
        ///     Marks a value as belonging in the fifth position of an either.
        /// </summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The value.</param>
        /// <returns>
        ///     A marked value which converts implicitly into any either whose fifth type argument
        ///     is <typeparamref name="T" />.
        /// </returns>
        /// <remarks>
        ///     This is what lets an either be returned or assigned without naming all of its type
        ///     arguments: the marked value carries only the position and the value, and the
        ///     conversion at the assignment or return supplies the rest. Where no such conversion is
        ///     available, use the either's own <c>Fifth</c> instead.
        /// </remarks>
        public static EitherFifth<T> Fifth<T>(T value) => new EitherFifth<T>(value);
        /// <summary>
        ///     Marks a value as belonging in the sixth position of an either.
        /// </summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The value.</param>
        /// <returns>
        ///     A marked value which converts implicitly into any either whose sixth type argument
        ///     is <typeparamref name="T" />.
        /// </returns>
        /// <remarks>
        ///     This is what lets an either be returned or assigned without naming all of its type
        ///     arguments: the marked value carries only the position and the value, and the
        ///     conversion at the assignment or return supplies the rest. Where no such conversion is
        ///     available, use the either's own <c>Sixth</c> instead.
        /// </remarks>
        public static EitherSixth<T> Sixth<T>(T value) => new EitherSixth<T>(value);
        /// <summary>
        ///     Marks a value as belonging in the seventh position of an either.
        /// </summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The value.</param>
        /// <returns>
        ///     A marked value which converts implicitly into any either whose seventh type argument
        ///     is <typeparamref name="T" />.
        /// </returns>
        /// <remarks>
        ///     This is what lets an either be returned or assigned without naming all of its type
        ///     arguments: the marked value carries only the position and the value, and the
        ///     conversion at the assignment or return supplies the rest. Where no such conversion is
        ///     available, use the either's own <c>Seventh</c> instead.
        /// </remarks>
        public static EitherSeventh<T> Seventh<T>(T value) => new EitherSeventh<T>(value);
        /// <summary>
        ///     Marks a value as belonging in the eighth position of an either.
        /// </summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The value.</param>
        /// <returns>
        ///     A marked value which converts implicitly into any either whose eighth type argument
        ///     is <typeparamref name="T" />.
        /// </returns>
        /// <remarks>
        ///     This is what lets an either be returned or assigned without naming all of its type
        ///     arguments: the marked value carries only the position and the value, and the
        ///     conversion at the assignment or return supplies the rest. Where no such conversion is
        ///     available, use the either's own <c>Eighth</c> instead.
        /// </remarks>
        public static EitherEighth<T> Eighth<T>(T value) => new EitherEighth<T>(value);

        /// <summary>
        ///     Begins a call which collapses an either whose cases all derive from
        ///     <typeparamref name="T" /> down to a single <typeparamref name="T" />.
        /// </summary>
        /// <typeparam name="T">The common type the cases are to be viewed as.</typeparam>
        /// <returns>
        ///     A helper whose <c>From</c> overloads take the either and perform the collapse.
        /// </returns>
        /// <remarks>
        ///     Written in two calls - <c>Either.GetValueAs&lt;TCommon&gt;().From(e)</c> - because C#
        ///     cannot specify one type argument and infer the rest within a single call, and the
        ///     either's own type arguments are much better inferred than written out.
        /// </remarks>
        public static GetValueAsHelper<T> GetValueAs<T>() => GetValueAsHelper<T>.Instance;

        /// <summary>
        ///     A value marked as belonging in the first position of an either, as produced by
        ///     <see cref="Either.First{T}" />.
        /// </summary>
        /// <typeparam name="T">The type of the marked value.</typeparam>
        /// <remarks>
        ///     Has nothing usable of its own. It exists to be converted implicitly into an either
        ///     which has this type in its first position.
        /// </remarks>
        public sealed class EitherFirst<T>
        {
            internal EitherFirst(T value) => this.Value = value;

            internal T Value { get; }
        }

        /// <summary>
        ///     A value marked as belonging in the second position of an either, as produced by
        ///     <see cref="Either.Second{T}" />.
        /// </summary>
        /// <typeparam name="T">The type of the marked value.</typeparam>
        /// <remarks>
        ///     Has nothing usable of its own. It exists to be converted implicitly into an either
        ///     which has this type in its second position.
        /// </remarks>
        public sealed class EitherSecond<T>
        {
            internal EitherSecond(T value) => this.Value = value;

            internal T Value { get; }
        }

        /// <summary>
        ///     A value marked as belonging in the third position of an either, as produced by
        ///     <see cref="Either.Third{T}" />.
        /// </summary>
        /// <typeparam name="T">The type of the marked value.</typeparam>
        /// <remarks>
        ///     Has nothing usable of its own. It exists to be converted implicitly into an either
        ///     which has this type in its third position.
        /// </remarks>
        public sealed class EitherThird<T>
        {
            internal EitherThird(T value) => this.Value = value;

            internal T Value { get; }
        }

        /// <summary>
        ///     A value marked as belonging in the fourth position of an either, as produced by
        ///     <see cref="Either.Fourth{T}" />.
        /// </summary>
        /// <typeparam name="T">The type of the marked value.</typeparam>
        /// <remarks>
        ///     Has nothing usable of its own. It exists to be converted implicitly into an either
        ///     which has this type in its fourth position.
        /// </remarks>
        public sealed class EitherFourth<T>
        {
            internal EitherFourth(T value) => this.Value = value;

            internal T Value { get; }
        }

        /// <summary>
        ///     A value marked as belonging in the fifth position of an either, as produced by
        ///     <see cref="Either.Fifth{T}" />.
        /// </summary>
        /// <typeparam name="T">The type of the marked value.</typeparam>
        /// <remarks>
        ///     Has nothing usable of its own. It exists to be converted implicitly into an either
        ///     which has this type in its fifth position.
        /// </remarks>
        public sealed class EitherFifth<T>
        {
            internal EitherFifth(T value) => this.Value = value;

            internal T Value { get; }
        }

        /// <summary>
        ///     A value marked as belonging in the sixth position of an either, as produced by
        ///     <see cref="Either.Sixth{T}" />.
        /// </summary>
        /// <typeparam name="T">The type of the marked value.</typeparam>
        /// <remarks>
        ///     Has nothing usable of its own. It exists to be converted implicitly into an either
        ///     which has this type in its sixth position.
        /// </remarks>
        public sealed class EitherSixth<T>
        {
            internal EitherSixth(T value) => this.Value = value;

            internal T Value { get; }
        }

        /// <summary>
        ///     A value marked as belonging in the seventh position of an either, as produced by
        ///     <see cref="Either.Seventh{T}" />.
        /// </summary>
        /// <typeparam name="T">The type of the marked value.</typeparam>
        /// <remarks>
        ///     Has nothing usable of its own. It exists to be converted implicitly into an either
        ///     which has this type in its seventh position.
        /// </remarks>
        public sealed class EitherSeventh<T>
        {
            internal EitherSeventh(T value) => this.Value = value;

            internal T Value { get; }
        }

        /// <summary>
        ///     A value marked as belonging in the eighth position of an either, as produced by
        ///     <see cref="Either.Eighth{T}" />.
        /// </summary>
        /// <typeparam name="T">The type of the marked value.</typeparam>
        /// <remarks>
        ///     Has nothing usable of its own. It exists to be converted implicitly into an either
        ///     which has this type in its eighth position.
        /// </remarks>
        public sealed class EitherEighth<T>
        {
            internal EitherEighth(T value) => this.Value = value;

            internal T Value { get; }
        }

        /// <summary>
        ///     The second half of <see cref="Either.GetValueAs{T}" />, holding the overloads which
        ///     do the collapsing.
        /// </summary>
        /// <typeparam name="T">The common type every case of the either is to be viewed as.</typeparam>
        public class GetValueAsHelper<T>
        {
            internal static readonly GetValueAsHelper<T> Instance = new GetValueAsHelper<T>();

            private GetValueAsHelper()
            {
            }

            /// <summary>
            ///     Collapses an either into the single common type all of its cases derive from.
            /// </summary>
            /// <typeparam name="T1">The type of the first case, which must be a <typeparamref name="T" />.</typeparam>
            /// <typeparam name="T2">The type of the second case, which must be a <typeparamref name="T" />.</typeparam>
            /// <param name="a">The either to collapse.</param>
            /// <returns>Whichever value the either holds, typed as <typeparamref name="T" />.</returns>
            /// <remarks>
            ///     Nothing is converted; the constraints guarantee every case already is a
            ///     <typeparamref name="T" />.
            /// </remarks>
            public T From<T1, T2>(Either<T1, T2> a)
                where T1 : T
                where T2 : T =>
                a.Match<T>(v1 => v1, v2 => v2);

            /// <summary>
            ///     Collapses an either into the single common type all of its cases derive from.
            /// </summary>
            /// <typeparam name="T1">The type of the first case, which must be a <typeparamref name="T" />.</typeparam>
            /// <typeparam name="T2">The type of the second case, which must be a <typeparamref name="T" />.</typeparam>
            /// <typeparam name="T3">The type of the third case, which must be a <typeparamref name="T" />.</typeparam>
            /// <param name="a">The either to collapse.</param>
            /// <returns>Whichever value the either holds, typed as <typeparamref name="T" />.</returns>
            /// <remarks>
            ///     Nothing is converted; the constraints guarantee every case already is a
            ///     <typeparamref name="T" />.
            /// </remarks>
            public T From<T1, T2, T3>(Either<T1, T2, T3> a)
                where T1 : T
                where T2 : T
                where T3 : T =>
                a.Match<T>(v1 => v1, v2 => v2, v3 => v3);

            /// <summary>
            ///     Collapses an either into the single common type all of its cases derive from.
            /// </summary>
            /// <typeparam name="T1">The type of the first case, which must be a <typeparamref name="T" />.</typeparam>
            /// <typeparam name="T2">The type of the second case, which must be a <typeparamref name="T" />.</typeparam>
            /// <typeparam name="T3">The type of the third case, which must be a <typeparamref name="T" />.</typeparam>
            /// <typeparam name="T4">The type of the fourth case, which must be a <typeparamref name="T" />.</typeparam>
            /// <param name="a">The either to collapse.</param>
            /// <returns>Whichever value the either holds, typed as <typeparamref name="T" />.</returns>
            /// <remarks>
            ///     Nothing is converted; the constraints guarantee every case already is a
            ///     <typeparamref name="T" />.
            /// </remarks>
            public T From<T1, T2, T3, T4>(Either<T1, T2, T3, T4> a)
                where T1 : T
                where T2 : T
                where T3 : T
                where T4 : T =>
                a.Match<T>(v1 => v1, v2 => v2, v3 => v3, v4 => v4);

            /// <summary>
            ///     Collapses an either into the single common type all of its cases derive from.
            /// </summary>
            /// <typeparam name="T1">The type of the first case, which must be a <typeparamref name="T" />.</typeparam>
            /// <typeparam name="T2">The type of the second case, which must be a <typeparamref name="T" />.</typeparam>
            /// <typeparam name="T3">The type of the third case, which must be a <typeparamref name="T" />.</typeparam>
            /// <typeparam name="T4">The type of the fourth case, which must be a <typeparamref name="T" />.</typeparam>
            /// <typeparam name="T5">The type of the fifth case, which must be a <typeparamref name="T" />.</typeparam>
            /// <param name="a">The either to collapse.</param>
            /// <returns>Whichever value the either holds, typed as <typeparamref name="T" />.</returns>
            /// <remarks>
            ///     Nothing is converted; the constraints guarantee every case already is a
            ///     <typeparamref name="T" />.
            /// </remarks>
            public T From<T1, T2, T3, T4, T5>(Either<T1, T2, T3, T4, T5> a)
                where T1 : T
                where T2 : T
                where T3 : T
                where T4 : T
                where T5 : T =>
                a.Match<T>(v1 => v1, v2 => v2, v3 => v3, v4 => v4, v5 => v5);

            /// <summary>
            ///     Collapses an either into the single common type all of its cases derive from.
            /// </summary>
            /// <typeparam name="T1">The type of the first case, which must be a <typeparamref name="T" />.</typeparam>
            /// <typeparam name="T2">The type of the second case, which must be a <typeparamref name="T" />.</typeparam>
            /// <typeparam name="T3">The type of the third case, which must be a <typeparamref name="T" />.</typeparam>
            /// <typeparam name="T4">The type of the fourth case, which must be a <typeparamref name="T" />.</typeparam>
            /// <typeparam name="T5">The type of the fifth case, which must be a <typeparamref name="T" />.</typeparam>
            /// <typeparam name="T6">The type of the sixth case, which must be a <typeparamref name="T" />.</typeparam>
            /// <param name="a">The either to collapse.</param>
            /// <returns>Whichever value the either holds, typed as <typeparamref name="T" />.</returns>
            /// <remarks>
            ///     Nothing is converted; the constraints guarantee every case already is a
            ///     <typeparamref name="T" />.
            /// </remarks>
            public T From<T1, T2, T3, T4, T5, T6>(Either<T1, T2, T3, T4, T5, T6> a)
                where T1 : T
                where T2 : T
                where T3 : T
                where T4 : T
                where T5 : T
                where T6 : T =>
                a.Match<T>(v1 => v1, v2 => v2, v3 => v3, v4 => v4, v5 => v5, v6 => v6);

            /// <summary>
            ///     Collapses an either into the single common type all of its cases derive from.
            /// </summary>
            /// <typeparam name="T1">The type of the first case, which must be a <typeparamref name="T" />.</typeparam>
            /// <typeparam name="T2">The type of the second case, which must be a <typeparamref name="T" />.</typeparam>
            /// <typeparam name="T3">The type of the third case, which must be a <typeparamref name="T" />.</typeparam>
            /// <typeparam name="T4">The type of the fourth case, which must be a <typeparamref name="T" />.</typeparam>
            /// <typeparam name="T5">The type of the fifth case, which must be a <typeparamref name="T" />.</typeparam>
            /// <typeparam name="T6">The type of the sixth case, which must be a <typeparamref name="T" />.</typeparam>
            /// <typeparam name="T7">The type of the seventh case, which must be a <typeparamref name="T" />.</typeparam>
            /// <param name="a">The either to collapse.</param>
            /// <returns>Whichever value the either holds, typed as <typeparamref name="T" />.</returns>
            /// <remarks>
            ///     Nothing is converted; the constraints guarantee every case already is a
            ///     <typeparamref name="T" />.
            /// </remarks>
            public T From<T1, T2, T3, T4, T5, T6, T7>(Either<T1, T2, T3, T4, T5, T6, T7> a)
                where T1 : T
                where T2 : T
                where T3 : T
                where T4 : T
                where T5 : T
                where T6 : T
                where T7 : T =>
                a.Match<T>(v1 => v1, v2 => v2, v3 => v3, v4 => v4, v5 => v5, v6 => v6, v7 => v7);

            /// <summary>
            ///     Collapses an either into the single common type all of its cases derive from.
            /// </summary>
            /// <typeparam name="T1">The type of the first case, which must be a <typeparamref name="T" />.</typeparam>
            /// <typeparam name="T2">The type of the second case, which must be a <typeparamref name="T" />.</typeparam>
            /// <typeparam name="T3">The type of the third case, which must be a <typeparamref name="T" />.</typeparam>
            /// <typeparam name="T4">The type of the fourth case, which must be a <typeparamref name="T" />.</typeparam>
            /// <typeparam name="T5">The type of the fifth case, which must be a <typeparamref name="T" />.</typeparam>
            /// <typeparam name="T6">The type of the sixth case, which must be a <typeparamref name="T" />.</typeparam>
            /// <typeparam name="T7">The type of the seventh case, which must be a <typeparamref name="T" />.</typeparam>
            /// <typeparam name="T8">The type of the eighth case, which must be a <typeparamref name="T" />.</typeparam>
            /// <param name="a">The either to collapse.</param>
            /// <returns>Whichever value the either holds, typed as <typeparamref name="T" />.</returns>
            /// <remarks>
            ///     Nothing is converted; the constraints guarantee every case already is a
            ///     <typeparamref name="T" />.
            /// </remarks>
            public T From<T1, T2, T3, T4, T5, T6, T7, T8>(Either<T1, T2, T3, T4, T5, T6, T7, T8> a)
                where T1 : T
                where T2 : T
                where T3 : T
                where T4 : T
                where T5 : T
                where T6 : T
                where T7 : T
                where T8 : T =>
                a.Match<T>(v1 => v1, v2 => v2, v3 => v3, v4 => v4, v5 => v5, v6 => v6, v7 => v7, v8 => v8);
        }
    }

    /// <summary>
    ///     A value which is exactly one of two possibilities.
    /// </summary>
    /// <typeparam name="T1">The type of the first possibility.</typeparam>
    /// <typeparam name="T2">The type of the second possibility.</typeparam>
    /// <remarks>
    ///     A discriminated union: the value is one of the two cases, and which one is part
    ///     of the value rather than something the caller has to track alongside it.
    ///
    ///     There is no property that hands the value out unchecked. Reach it with
    ///     <c>Match</c>, or with one of the helpers built on it, so that every case has to be
    ///     answered for.
    ///
    ///     This is a struct, so <see langword="default" /> is a valid instance; it holds the
    ///     first case, with the default value of <typeparamref name="T1" />.
    /// </remarks>
    public struct Either<T1, T2> : IEitherOfTwo, IEquatable<Either<T1, T2>>
    {
        private readonly int valueType;
        private readonly T1? value1;
        private readonly T2? value2;

        private Either(int valueType, T1? value1, T2? value2)
        {
            this.valueType = valueType;
            this.value1 = value1;
            this.value2 = value2;
        }

        #region Type Constructors

        /// <summary>
        ///     Creates an either holding the given value as its first case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its first case.</returns>
        /// <remarks>
        ///     <see cref="Either.First{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2> First(T1 value) => new Either<T1, T2>(0, value, default(T2));
        /// <summary>
        ///     Creates an either holding the given value as its second case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its second case.</returns>
        /// <remarks>
        ///     <see cref="Either.Second{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2> Second(T2 value) => new Either<T1, T2>(1, default(T1), value);

        #endregion

        #region Base Functionality

        T IEitherOfTwo.Match<T>(Func<object?, T> onFirst, Func<object?, T> onSecond) =>
            this.Match(v => onFirst(v), v => onSecond(v));

        object IEither.GetValueAsObject() => Either.GetValueAs<object>().From(this);

        /// <summary>
        ///     Runs one of the two functions depending on which case is held, and returns its
        ///     result.
        /// </summary>
        /// <typeparam name="T">The type each of the functions returns.</typeparam>
        /// <param name="onFirst">Run with the value when the first case is held.</param>
        /// <param name="onSecond">Run with the value when the second case is held.</param>
        /// <returns>Whatever the function that was run returned.</returns>
        /// <remarks>
        ///     This is the only way the held value is reached, and every other member here is
        ///     expressed in terms of it. Exactly one of the functions is called, and it is called
        ///     before this method returns.
        /// </remarks>
        public T Match<T>(
            [JetBrains.Annotations.InstantHandle] Func<T1, T> onFirst,
            [JetBrains.Annotations.InstantHandle] Func<T2, T> onSecond) =>
            this.valueType == 0 ? onFirst(this.value1!) : onSecond(this.value2!);

        #endregion

        #region Helper Methods

        /// <summary>
        ///     Runs one of the two actions depending on which case is held.
        /// </summary>
        /// <param name="onFirst">Run with the value when the first case is held.</param>
        /// <param name="onSecond">Run with the value when the second case is held.</param>
        public void MatchVoid(Action<T1> onFirst, Action<T2> onSecond) =>
            // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
            this.Match(onFirst.ToFunc(), onSecond.ToFunc());

        /// <summary>
        ///     Runs one of the two asynchronous functions depending on which case is held, and
        ///     returns its result.
        /// </summary>
        /// <typeparam name="T">The type each of the functions produces.</typeparam>
        /// <param name="onFirst">Run with the value when the first case is held.</param>
        /// <param name="onSecond">Run with the value when the second case is held.</param>
        /// <returns>The task returned by whichever function was run.</returns>
        /// <remarks>
        ///     Only the selected function is invoked; the returned task is its task, not a wrapper,
        ///     so failures surface as that task faulting rather than as an exception from this call.
        /// </remarks>
        public Task<T> MatchAsync<T>(
            [JetBrains.Annotations.InstantHandle] Func<T1, Task<T>> onFirst,
            [JetBrains.Annotations.InstantHandle] Func<T2, Task<T>> onSecond) =>
            this.Match(onFirst, onSecond);

        /// <summary>
        ///     Runs one of the two asynchronous actions depending on which case is held.
        /// </summary>
        /// <param name="onFirst">Run with the value when the first case is held.</param>
        /// <param name="onSecond">Run with the value when the second case is held.</param>
        /// <returns>A task which completes when the selected action has completed.</returns>
        public Task MatchAsyncVoid(
            [JetBrains.Annotations.InstantHandle] Func<T1, Task> onFirst,
            [JetBrains.Annotations.InstantHandle] Func<T2, Task> onSecond) =>
            this.MatchAsync(onFirst.ToAsyncFunc(), onSecond.ToAsyncFunc());

        /// <summary>
        ///     Transforms the value if this holds the first case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the first case is transformed into.</typeparam>
        /// <param name="f">The function to transform the first case with.</param>
        /// <returns>
        ///     An either whose first case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the first case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the first case.
        /// </remarks>
        public Either<T, T2> MapFirst<T>([JetBrains.Annotations.InstantHandle] Func<T1, T> f) =>
            this.Match(v1 => Either<T, T2>.First(f(v1)), v2 => Either.Second(v2));

        /// <summary>
        ///     Transforms the value if this holds the second case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the second case is transformed into.</typeparam>
        /// <param name="f">The function to transform the second case with.</param>
        /// <returns>
        ///     An either whose second case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the second case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the second case.
        /// </remarks>
        public Either<T1, T> MapSecond<T>([JetBrains.Annotations.InstantHandle] Func<T2, T> f) =>
            this.Match(Either<T1, T>.First, v2 => Either.Second(f(v2)));

        /// <summary>
        ///     Exchanges the two cases, so that what was held first is held second and the other way
        ///     round.
        /// </summary>
        /// <returns>
        ///     An either of the two types in the opposite order, holding the same value in the other
        ///     position.
        /// </returns>
        /// <remarks>
        ///     For handing an either to something which names the same two types the other way round,
        ///     and for reaching the first case with an operation that only addresses the second - or
        ///     the reverse. Swapping twice gives back the original.
        ///
        ///     This exists only here, on the two-case either. With three or more cases there is no
        ///     single exchange to make: what a swap would mean is a choice among several reorderings,
        ///     and naming one of them <c>Swap</c> would make the others look unavailable rather than
        ///     unnamed.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public Either<T2, T1> Swap() =>
            this.Match(Either<T2, T1>.Second, Either<T2, T1>.First);

        /// <summary>
        ///     Gets the held value if this holds the first case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the first case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T1> TryGetFirst() =>
            this.Match(Maybe.Some, _ => Maybe.None);

        /// <summary>
        ///     Gets the held value if this holds the second case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the second case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T2> TryGetSecond() =>
            this.Match(_ => Maybe.None, Maybe.Some);

        /// <summary>
        ///     Returns whether this holds the first case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the first case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetFirst" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsFirst() =>
            this.Match(_ => true, _ => false);

        /// <summary>
        ///     Returns whether this holds the second case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the second case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetSecond" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsSecond() =>
            this.Match(_ => false, _ => true);

        void IEitherOfTwo.MatchVoid(Action<object?> onFirst, Action<object?> onSecond) =>
            // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
            this.Upcast<IEitherOfTwo>().Match(onFirst.ToFunc(), onSecond.ToFunc());

        Task<T> IEitherOfTwo.MatchAsync<T>(Func<object?, Task<T>> onFirst, Func<object?, Task<T>> onSecond) =>
            this.Upcast<IEitherOfTwo>().Match(onFirst, onSecond);

        Task IEitherOfTwo.MatchAsyncVoid(Func<object?, Task> onFirst, Func<object?, Task> onSecond) =>
            this.Upcast<IEitherOfTwo>().MatchAsync(onFirst.ToAsyncFunc(), onSecond.ToAsyncFunc());

        #endregion

        /// <summary>
        ///     Converts a value marked for the first position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.First{T}" />.</param>
        /// <returns>An either holding that value as its first case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.First{T}" /> usable in place of this type's own
        ///     <c>First</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default first value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2>(Either.EitherFirst<T1>? value) =>
            First(value == null ? default(T1)! : value.Value);

        /// <summary>
        ///     Converts a value marked for the second position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.Second{T}" />.</param>
        /// <returns>An either holding that value as its second case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.Second{T}" /> usable in place of this type's own
        ///     <c>Second</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default second value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2>(Either.EitherSecond<T2>? value) =>
            Second(value == null ? default(T2)! : value.Value);

        /// <summary>
        ///     Determines whether two instances hold the same case with equal values.
        /// </summary>
        /// <param name="x">The first instance.</param>
        /// <param name="y">The second instance.</param>
        /// <returns>
        ///     <see langword="true" /> if both hold the same case and the values they hold are
        ///     equal according to <see cref="EqualityComparer{T}.Default" />.
        /// </returns>
        public static bool operator ==(Either<T1, T2> x, Either<T1, T2> y) =>
            x.valueType == y.valueType
            && EqualityComparer<T1>.Default.Equals(x.value1!, y.value1!)
            && EqualityComparer<T2>.Default.Equals(x.value2!, y.value2!);

        /// <summary>
        ///     Determines whether two instances differ, by negating <see cref="op_Equality" />.
        /// </summary>
        /// <param name="x">The first instance.</param>
        /// <param name="y">The second instance.</param>
        /// <returns>
        ///     <see langword="true" /> if the two hold different cases, or hold the same case with
        ///     values which are not equal.
        /// </returns>
        public static bool operator !=(Either<T1, T2> x, Either<T1, T2> y) => !(x == y);
        /// <summary>
        ///     Determines whether the given object is an either of this same type which is equal to
        ///     this one.
        /// </summary>
        /// <param name="obj">The object to compare against.</param>
        /// <returns>
        ///     <see langword="true" /> if <paramref name="obj" /> is an either of the same type which
        ///     <see cref="op_Equality" /> considers equal to this one.
        /// </returns>
        /// <remarks>
        ///     An either is never equal to the bare value it holds, only to another either.
        /// </remarks>
        public override bool Equals(object? obj) => obj is Either<T1, T2> e && this == e;

        /// <summary>
        ///     Determines whether the given instance is equal to this one.
        /// </summary>
        /// <param name="other">The instance to compare against.</param>
        /// <returns>
        ///     <see langword="true" /> if <see cref="op_Equality" /> considers the two equal.
        /// </returns>
        /// <remarks>
        ///     The same comparison as <see cref="op_Equality" />, under the name
        ///     <see cref="EqualityComparer{T}.Default" /> looks for, so that comparing these in a
        ///     collection does not box both operands the way <see cref="Equals(object)" /> must.
        /// </remarks>
        public bool Equals(Either<T1, T2> other) => this == other;

        /// <summary>
        ///     Returns a hash code consistent with <see cref="op_Equality" />.
        /// </summary>
        /// <returns>A hash code for this instance.</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = this.valueType;
                hashCode = (hashCode * 397) ^ EqualityComparer<T1>.Default.GetHashCode(this.value1!);
                hashCode = (hashCode * 397) ^ EqualityComparer<T2>.Default.GetHashCode(this.value2!);
                return hashCode;
            }
        }

        /// <summary>
        ///     Returns a readable description of this instance, for diagnostics.
        /// </summary>
        /// <returns>The name of the case held, followed by the value it holds.</returns>
        public override string ToString() => this.Match(v1 => $"First: {v1}", v2 => $"Second: {v2}");
    }

    /// <summary>
    ///     A value which is exactly one of three possibilities.
    /// </summary>
    /// <typeparam name="T1">The type of the first possibility.</typeparam>
    /// <typeparam name="T2">The type of the second possibility.</typeparam>
    /// <typeparam name="T3">The type of the third possibility.</typeparam>
    /// <remarks>
    ///     A discriminated union: the value is one of the three cases, and which one is part
    ///     of the value rather than something the caller has to track alongside it.
    ///
    ///     There is no property that hands the value out unchecked. Reach it with
    ///     <c>Match</c>, or with one of the helpers built on it, so that every case has to be
    ///     answered for.
    ///
    ///     This is a struct, so <see langword="default" /> is a valid instance; it holds the
    ///     first case, with the default value of <typeparamref name="T1" />.
    /// </remarks>
    public struct Either<T1, T2, T3> : IEitherOfThree, IEquatable<Either<T1, T2, T3>>
    {
        private readonly int valueType;
        private readonly T1? value1;
        private readonly T2? value2;
        private readonly T3? value3;

        private Either(int valueType, T1? value1, T2? value2, T3? value3)
        {
            this.valueType = valueType;
            this.value1 = value1;
            this.value2 = value2;
            this.value3 = value3;
        }

        #region Type Constructors

        /// <summary>
        ///     Creates an either holding the given value as its first case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its first case.</returns>
        /// <remarks>
        ///     <see cref="Either.First{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3> First(T1 value) => new Either<T1, T2, T3>(0, value, default(T2), default(T3));
        /// <summary>
        ///     Creates an either holding the given value as its second case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its second case.</returns>
        /// <remarks>
        ///     <see cref="Either.Second{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3> Second(T2 value) => new Either<T1, T2, T3>(1, default(T1), value, default(T3));
        /// <summary>
        ///     Creates an either holding the given value as its third case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its third case.</returns>
        /// <remarks>
        ///     <see cref="Either.Third{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3> Third(T3 value) => new Either<T1, T2, T3>(2, default(T1), default(T2), value);

        #endregion

        #region Base Functionality

        T IEitherOfThree.Match<T>(Func<object?, T> onFirst, Func<object?, T> onSecond, Func<object?, T> onThird) =>
            this.Match(v => onFirst(v), v => onSecond(v), v => onThird(v));

        object IEither.GetValueAsObject() => Either.GetValueAs<object>().From(this);

        /// <summary>
        ///     Runs one of the three functions depending on which case is held, and returns its
        ///     result.
        /// </summary>
        /// <typeparam name="T">The type each of the functions returns.</typeparam>
        /// <param name="onFirst">Run with the value when the first case is held.</param>
        /// <param name="onSecond">Run with the value when the second case is held.</param>
        /// <param name="onThird">Run with the value when the third case is held.</param>
        /// <returns>Whatever the function that was run returned.</returns>
        /// <remarks>
        ///     This is the only way the held value is reached, and every other member here is
        ///     expressed in terms of it. Exactly one of the functions is called, and it is called
        ///     before this method returns.
        /// </remarks>
        public T Match<T>(
            [JetBrains.Annotations.InstantHandle] Func<T1, T> onFirst,
            [JetBrains.Annotations.InstantHandle] Func<T2, T> onSecond,
            [JetBrains.Annotations.InstantHandle] Func<T3, T> onThird) =>
            this.valueType == 0
                ? onFirst(this.value1!)
                : (this.valueType == 1 ? onSecond(this.value2!) : onThird(this.value3!));

        #endregion

        #region Helper Methods

        /// <summary>
        ///     Runs one of the three actions depending on which case is held.
        /// </summary>
        /// <param name="onFirst">Run with the value when the first case is held.</param>
        /// <param name="onSecond">Run with the value when the second case is held.</param>
        /// <param name="onThird">Run with the value when the third case is held.</param>
        public void MatchVoid(
            [JetBrains.Annotations.InstantHandle] Action<T1> onFirst,
            [JetBrains.Annotations.InstantHandle] Action<T2> onSecond,
            [JetBrains.Annotations.InstantHandle] Action<T3> onThird) =>
            // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
            this.Match(onFirst.ToFunc(), onSecond.ToFunc(), onThird.ToFunc());

        /// <summary>
        ///     Runs one of the three asynchronous functions depending on which case is held, and
        ///     returns its result.
        /// </summary>
        /// <typeparam name="T">The type each of the functions produces.</typeparam>
        /// <param name="onFirst">Run with the value when the first case is held.</param>
        /// <param name="onSecond">Run with the value when the second case is held.</param>
        /// <param name="onThird">Run with the value when the third case is held.</param>
        /// <returns>The task returned by whichever function was run.</returns>
        /// <remarks>
        ///     Only the selected function is invoked; the returned task is its task, not a wrapper,
        ///     so failures surface as that task faulting rather than as an exception from this call.
        /// </remarks>
        public Task<T> MatchAsync<T>(
            [JetBrains.Annotations.InstantHandle] Func<T1, Task<T>> onFirst,
            [JetBrains.Annotations.InstantHandle] Func<T2, Task<T>> onSecond,
            [JetBrains.Annotations.InstantHandle] Func<T3, Task<T>> onThird) =>
            this.Match(onFirst, onSecond, onThird);

        /// <summary>
        ///     Runs one of the three asynchronous actions depending on which case is held.
        /// </summary>
        /// <param name="onFirst">Run with the value when the first case is held.</param>
        /// <param name="onSecond">Run with the value when the second case is held.</param>
        /// <param name="onThird">Run with the value when the third case is held.</param>
        /// <returns>A task which completes when the selected action has completed.</returns>
        public Task MatchAsyncVoid(
            [JetBrains.Annotations.InstantHandle] Func<T1, Task> onFirst,
            [JetBrains.Annotations.InstantHandle] Func<T2, Task> onSecond,
            [JetBrains.Annotations.InstantHandle] Func<T3, Task> onThird) =>
            this.MatchAsync(onFirst.ToAsyncFunc(), onSecond.ToAsyncFunc(), onThird.ToAsyncFunc());

        /// <summary>
        ///     Transforms the value if this holds the first case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the first case is transformed into.</typeparam>
        /// <param name="f">The function to transform the first case with.</param>
        /// <returns>
        ///     An either whose first case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the first case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the first case.
        /// </remarks>
        public Either<T, T2, T3> MapFirst<T>([JetBrains.Annotations.InstantHandle] Func<T1, T> f) =>
            this.Match(v1 => Either<T, T2, T3>.First(f(v1)), v2 => Either.Second(v2), v3 => Either.Third(v3));

        /// <summary>
        ///     Transforms the value if this holds the second case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the second case is transformed into.</typeparam>
        /// <param name="f">The function to transform the second case with.</param>
        /// <returns>
        ///     An either whose second case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the second case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the second case.
        /// </remarks>
        public Either<T1, T, T3> MapSecond<T>([JetBrains.Annotations.InstantHandle] Func<T2, T> f) =>
            this.Match(Either<T1, T, T3>.First, v2 => Either.Second(f(v2)), v3 => Either.Third(v3));

        /// <summary>
        ///     Transforms the value if this holds the third case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the third case is transformed into.</typeparam>
        /// <param name="f">The function to transform the third case with.</param>
        /// <returns>
        ///     An either whose third case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the third case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the third case.
        /// </remarks>
        public Either<T1, T2, T> MapThird<T>([JetBrains.Annotations.InstantHandle] Func<T3, T> f) =>
            this.Match(Either<T1, T2, T>.First, v2 => Either.Second(v2), v3 => Either.Third(f(v3)));

        /// <summary>
        ///     Gets the held value if this holds the first case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the first case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T1> TryGetFirst() =>
            this.Match(Maybe.Some, _ => Maybe.None, _ => Maybe.None);

        /// <summary>
        ///     Gets the held value if this holds the second case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the second case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T2> TryGetSecond() =>
            this.Match(_ => Maybe.None, Maybe.Some, _ => Maybe.None);

        /// <summary>
        ///     Gets the held value if this holds the third case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the third case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T3> TryGetThird() =>
            this.Match(_ => Maybe.None, _ => Maybe.None, Maybe.Some);

        /// <summary>
        ///     Returns whether this holds the first case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the first case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetFirst" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsFirst() =>
            this.Match(_ => true, _ => false, _ => false);

        /// <summary>
        ///     Returns whether this holds the second case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the second case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetSecond" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsSecond() =>
            this.Match(_ => false, _ => true, _ => false);

        /// <summary>
        ///     Returns whether this holds the third case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the third case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetThird" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsThird() =>
            this.Match(_ => false, _ => false, _ => true);

        void IEitherOfThree.MatchVoid(Action<object?> onFirst, Action<object?> onSecond, Action<object?> onThird) =>
            // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
            this.Upcast<IEitherOfThree>().Match(onFirst.ToFunc(), onSecond.ToFunc(), onThird.ToFunc());

        Task<T> IEitherOfThree.MatchAsync<T>(
            Func<object?, Task<T>> onFirst,
            Func<object?, Task<T>> onSecond,
            Func<object?, Task<T>> onThird) =>
            this.Upcast<IEitherOfThree>().Match(onFirst, onSecond, onThird);

        Task IEitherOfThree.MatchAsyncVoid(
            Func<object?, Task> onFirst,
            Func<object?, Task> onSecond,
            Func<object?, Task> onThird) =>
            this.Upcast<IEitherOfThree>()
                .MatchAsync(onFirst.ToAsyncFunc(), onSecond.ToAsyncFunc(), onThird.ToAsyncFunc());

        #endregion

        /// <summary>
        ///     Converts a value marked for the first position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.First{T}" />.</param>
        /// <returns>An either holding that value as its first case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.First{T}" /> usable in place of this type's own
        ///     <c>First</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default first value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3>(Either.EitherFirst<T1>? value) =>
            First(value == null ? default(T1)! : value.Value);

        /// <summary>
        ///     Converts a value marked for the second position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.Second{T}" />.</param>
        /// <returns>An either holding that value as its second case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.Second{T}" /> usable in place of this type's own
        ///     <c>Second</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default second value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3>(Either.EitherSecond<T2>? value) =>
            Second(value == null ? default(T2)! : value.Value);

        /// <summary>
        ///     Converts a value marked for the third position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.Third{T}" />.</param>
        /// <returns>An either holding that value as its third case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.Third{T}" /> usable in place of this type's own
        ///     <c>Third</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default third value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3>(Either.EitherThird<T3>? value) =>
            Third(value == null ? default(T3)! : value.Value);

        /// <summary>
        ///     Determines whether two instances hold the same case with equal values.
        /// </summary>
        /// <param name="x">The first instance.</param>
        /// <param name="y">The second instance.</param>
        /// <returns>
        ///     <see langword="true" /> if both hold the same case and the values they hold are
        ///     equal according to <see cref="EqualityComparer{T}.Default" />.
        /// </returns>
        public static bool operator ==(Either<T1, T2, T3> x, Either<T1, T2, T3> y) =>
            x.valueType == y.valueType
            && EqualityComparer<T1>.Default.Equals(x.value1!, y.value1!)
            && EqualityComparer<T2>.Default.Equals(x.value2!, y.value2!)
            && EqualityComparer<T3>.Default.Equals(x.value3!, y.value3!);

        /// <summary>
        ///     Determines whether two instances differ, by negating <see cref="op_Equality" />.
        /// </summary>
        /// <param name="x">The first instance.</param>
        /// <param name="y">The second instance.</param>
        /// <returns>
        ///     <see langword="true" /> if the two hold different cases, or hold the same case with
        ///     values which are not equal.
        /// </returns>
        public static bool operator !=(Either<T1, T2, T3> x, Either<T1, T2, T3> y) => !(x == y);
        /// <summary>
        ///     Determines whether the given object is an either of this same type which is equal to
        ///     this one.
        /// </summary>
        /// <param name="obj">The object to compare against.</param>
        /// <returns>
        ///     <see langword="true" /> if <paramref name="obj" /> is an either of the same type which
        ///     <see cref="op_Equality" /> considers equal to this one.
        /// </returns>
        /// <remarks>
        ///     An either is never equal to the bare value it holds, only to another either.
        /// </remarks>
        public override bool Equals(object? obj) => obj is Either<T1, T2, T3> e && this == e;

        /// <summary>
        ///     Determines whether the given instance is equal to this one.
        /// </summary>
        /// <param name="other">The instance to compare against.</param>
        /// <returns>
        ///     <see langword="true" /> if <see cref="op_Equality" /> considers the two equal.
        /// </returns>
        /// <remarks>
        ///     The same comparison as <see cref="op_Equality" />, under the name
        ///     <see cref="EqualityComparer{T}.Default" /> looks for, so that comparing these in a
        ///     collection does not box both operands the way <see cref="Equals(object)" /> must.
        /// </remarks>
        public bool Equals(Either<T1, T2, T3> other) => this == other;

        /// <summary>
        ///     Returns a hash code consistent with <see cref="op_Equality" />.
        /// </summary>
        /// <returns>A hash code for this instance.</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = this.valueType;
                hashCode = (hashCode * 397) ^ EqualityComparer<T1>.Default.GetHashCode(this.value1!);
                hashCode = (hashCode * 397) ^ EqualityComparer<T2>.Default.GetHashCode(this.value2!);
                hashCode = (hashCode * 397) ^ EqualityComparer<T3>.Default.GetHashCode(this.value3!);
                return hashCode;
            }
        }

        /// <summary>
        ///     Returns a readable description of this instance, for diagnostics.
        /// </summary>
        /// <returns>The name of the case held, followed by the value it holds.</returns>
        public override string ToString() => this.Match(
            v1 => $"First: {v1}",
            v2 => $"Second: {v2}",
            v3 => $"Third: {v3}");
    }

    /// <summary>
    ///     A value which is exactly one of four possibilities.
    /// </summary>
    /// <typeparam name="T1">The type of the first possibility.</typeparam>
    /// <typeparam name="T2">The type of the second possibility.</typeparam>
    /// <typeparam name="T3">The type of the third possibility.</typeparam>
    /// <typeparam name="T4">The type of the fourth possibility.</typeparam>
    /// <remarks>
    ///     A discriminated union: the value is one of the four cases, and which one is part
    ///     of the value rather than something the caller has to track alongside it.
    ///
    ///     There is no property that hands the value out unchecked. Reach it with
    ///     <c>Match</c>, or with one of the helpers built on it, so that every case has to be
    ///     answered for.
    ///
    ///     This is a struct, so <see langword="default" /> is a valid instance; it holds the
    ///     first case, with the default value of <typeparamref name="T1" />.
    /// </remarks>
    public struct Either<T1, T2, T3, T4> : IEitherOfFour, IEquatable<Either<T1, T2, T3, T4>>
    {
        private readonly int valueType;
        private readonly T1? value1;
        private readonly T2? value2;
        private readonly T3? value3;
        private readonly T4? value4;

        private Either(int valueType, T1? value1, T2? value2, T3? value3, T4? value4)
        {
            this.valueType = valueType;
            this.value1 = value1;
            this.value2 = value2;
            this.value3 = value3;
            this.value4 = value4;
        }

        #region Type Constructors

        /// <summary>
        ///     Creates an either holding the given value as its first case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its first case.</returns>
        /// <remarks>
        ///     <see cref="Either.First{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3, T4> First(T1 value) =>
            new Either<T1, T2, T3, T4>(0, value, default(T2), default(T3), default(T4));

        /// <summary>
        ///     Creates an either holding the given value as its second case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its second case.</returns>
        /// <remarks>
        ///     <see cref="Either.Second{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3, T4> Second(T2 value) =>
            new Either<T1, T2, T3, T4>(1, default(T1), value, default(T3), default(T4));

        /// <summary>
        ///     Creates an either holding the given value as its third case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its third case.</returns>
        /// <remarks>
        ///     <see cref="Either.Third{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3, T4> Third(T3 value) =>
            new Either<T1, T2, T3, T4>(2, default(T1), default(T2), value, default(T4));

        /// <summary>
        ///     Creates an either holding the given value as its fourth case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its fourth case.</returns>
        /// <remarks>
        ///     <see cref="Either.Fourth{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3, T4> Fourth(T4 value) =>
            new Either<T1, T2, T3, T4>(3, default(T1), default(T2), default(T3), value);

        #endregion

        #region Base Functionality

        T IEitherOfFour.Match<T>(
            Func<object?, T> onFirst,
            Func<object?, T> onSecond,
            Func<object?, T> onThird,
            Func<object?, T> onFourth) =>
            this.Match(v => onFirst(v), v => onSecond(v), v => onThird(v), v => onFourth(v));

        object IEither.GetValueAsObject() => Either.GetValueAs<object>().From(this);

        /// <summary>
        ///     Runs one of the four functions depending on which case is held, and returns its
        ///     result.
        /// </summary>
        /// <typeparam name="T">The type each of the functions returns.</typeparam>
        /// <param name="onFirst">Run with the value when the first case is held.</param>
        /// <param name="onSecond">Run with the value when the second case is held.</param>
        /// <param name="onThird">Run with the value when the third case is held.</param>
        /// <param name="onFourth">Run with the value when the fourth case is held.</param>
        /// <returns>Whatever the function that was run returned.</returns>
        /// <remarks>
        ///     This is the only way the held value is reached, and every other member here is
        ///     expressed in terms of it. Exactly one of the functions is called, and it is called
        ///     before this method returns.
        /// </remarks>
        public T Match<T>(
            [JetBrains.Annotations.InstantHandle] Func<T1, T> onFirst,
            [JetBrains.Annotations.InstantHandle] Func<T2, T> onSecond,
            [JetBrains.Annotations.InstantHandle] Func<T3, T> onThird,
            [JetBrains.Annotations.InstantHandle] Func<T4, T> onFourth) =>
            this.valueType == 0
                ? onFirst(this.value1!)
                : (this.valueType == 1
                    ? onSecond(this.value2!)
                    : (this.valueType == 2 ? onThird(this.value3!) : onFourth(this.value4!)));

        #endregion

        #region Helper Methods

        /// <summary>
        ///     Runs one of the four actions depending on which case is held.
        /// </summary>
        /// <param name="onFirst">Run with the value when the first case is held.</param>
        /// <param name="onSecond">Run with the value when the second case is held.</param>
        /// <param name="onThird">Run with the value when the third case is held.</param>
        /// <param name="onFourth">Run with the value when the fourth case is held.</param>
        public void MatchVoid(
            [JetBrains.Annotations.InstantHandle] Action<T1> onFirst,
            [JetBrains.Annotations.InstantHandle] Action<T2> onSecond,
            [JetBrains.Annotations.InstantHandle] Action<T3> onThird,
            [JetBrains.Annotations.InstantHandle] Action<T4> onFourth) =>
            // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
            this.Match(onFirst.ToFunc(), onSecond.ToFunc(), onThird.ToFunc(), onFourth.ToFunc());

        /// <summary>
        ///     Runs one of the four asynchronous functions depending on which case is held, and
        ///     returns its result.
        /// </summary>
        /// <typeparam name="T">The type each of the functions produces.</typeparam>
        /// <param name="onFirst">Run with the value when the first case is held.</param>
        /// <param name="onSecond">Run with the value when the second case is held.</param>
        /// <param name="onThird">Run with the value when the third case is held.</param>
        /// <param name="onFourth">Run with the value when the fourth case is held.</param>
        /// <returns>The task returned by whichever function was run.</returns>
        /// <remarks>
        ///     Only the selected function is invoked; the returned task is its task, not a wrapper,
        ///     so failures surface as that task faulting rather than as an exception from this call.
        /// </remarks>
        public Task<T> MatchAsync<T>(
            [JetBrains.Annotations.InstantHandle] Func<T1, Task<T>> onFirst,
            [JetBrains.Annotations.InstantHandle] Func<T2, Task<T>> onSecond,
            [JetBrains.Annotations.InstantHandle] Func<T3, Task<T>> onThird,
            [JetBrains.Annotations.InstantHandle] Func<T4, Task<T>> onFourth) =>
            this.Match(onFirst, onSecond, onThird, onFourth);

        /// <summary>
        ///     Runs one of the four asynchronous actions depending on which case is held.
        /// </summary>
        /// <param name="onFirst">Run with the value when the first case is held.</param>
        /// <param name="onSecond">Run with the value when the second case is held.</param>
        /// <param name="onThird">Run with the value when the third case is held.</param>
        /// <param name="onFourth">Run with the value when the fourth case is held.</param>
        /// <returns>A task which completes when the selected action has completed.</returns>
        public Task MatchAsyncVoid(
            [JetBrains.Annotations.InstantHandle] Func<T1, Task> onFirst,
            [JetBrains.Annotations.InstantHandle] Func<T2, Task> onSecond,
            [JetBrains.Annotations.InstantHandle] Func<T3, Task> onThird,
            [JetBrains.Annotations.InstantHandle] Func<T4, Task> onFourth) =>
            this.MatchAsync(
                onFirst.ToAsyncFunc(),
                onSecond.ToAsyncFunc(),
                onThird.ToAsyncFunc(),
                onFourth.ToAsyncFunc());

        /// <summary>
        ///     Transforms the value if this holds the first case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the first case is transformed into.</typeparam>
        /// <param name="f">The function to transform the first case with.</param>
        /// <returns>
        ///     An either whose first case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the first case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the first case.
        /// </remarks>
        public Either<T, T2, T3, T4> MapFirst<T>([JetBrains.Annotations.InstantHandle] Func<T1, T> f) =>
            this.Match(
                v1 => Either<T, T2, T3, T4>.First(f(v1)),
                v2 => Either.Second(v2),
                v3 => Either.Third(v3),
                v4 => Either.Fourth(v4));

        /// <summary>
        ///     Transforms the value if this holds the second case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the second case is transformed into.</typeparam>
        /// <param name="f">The function to transform the second case with.</param>
        /// <returns>
        ///     An either whose second case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the second case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the second case.
        /// </remarks>
        public Either<T1, T, T3, T4> MapSecond<T>([JetBrains.Annotations.InstantHandle] Func<T2, T> f) =>
            this.Match(
                Either<T1, T, T3, T4>.First,
                v2 => Either.Second(f(v2)),
                v3 => Either.Third(v3),
                v4 => Either.Fourth(v4));

        /// <summary>
        ///     Transforms the value if this holds the third case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the third case is transformed into.</typeparam>
        /// <param name="f">The function to transform the third case with.</param>
        /// <returns>
        ///     An either whose third case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the third case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the third case.
        /// </remarks>
        public Either<T1, T2, T, T4> MapThird<T>([JetBrains.Annotations.InstantHandle] Func<T3, T> f) =>
            this.Match(
                Either<T1, T2, T, T4>.First,
                v2 => Either.Second(v2),
                v3 => Either.Third(f(v3)),
                v4 => Either.Fourth(v4));

        /// <summary>
        ///     Transforms the value if this holds the fourth case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the fourth case is transformed into.</typeparam>
        /// <param name="f">The function to transform the fourth case with.</param>
        /// <returns>
        ///     An either whose fourth case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the fourth case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the fourth case.
        /// </remarks>
        public Either<T1, T2, T3, T> MapFourth<T>([JetBrains.Annotations.InstantHandle] Func<T4, T> f) =>
            this.Match(
                Either<T1, T2, T3, T>.First,
                v2 => Either.Second(v2),
                v3 => Either.Third(v3),
                v4 => Either.Fourth(f(v4)));

        /// <summary>
        ///     Gets the held value if this holds the first case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the first case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T1> TryGetFirst() =>
            this.Match(Maybe.Some, _ => Maybe.None, _ => Maybe.None, _ => Maybe.None);

        /// <summary>
        ///     Gets the held value if this holds the second case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the second case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T2> TryGetSecond() =>
            this.Match(_ => Maybe.None, Maybe.Some, _ => Maybe.None, _ => Maybe.None);

        /// <summary>
        ///     Gets the held value if this holds the third case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the third case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T3> TryGetThird() =>
            this.Match(_ => Maybe.None, _ => Maybe.None, Maybe.Some, _ => Maybe.None);

        /// <summary>
        ///     Gets the held value if this holds the fourth case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the fourth case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T4> TryGetFourth() =>
            this.Match(_ => Maybe.None, _ => Maybe.None, _ => Maybe.None, Maybe.Some);

        /// <summary>
        ///     Returns whether this holds the first case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the first case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetFirst" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsFirst() =>
            this.Match(_ => true, _ => false, _ => false, _ => false);

        /// <summary>
        ///     Returns whether this holds the second case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the second case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetSecond" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsSecond() =>
            this.Match(_ => false, _ => true, _ => false, _ => false);

        /// <summary>
        ///     Returns whether this holds the third case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the third case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetThird" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsThird() =>
            this.Match(_ => false, _ => false, _ => true, _ => false);

        /// <summary>
        ///     Returns whether this holds the fourth case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the fourth case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetFourth" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsFourth() =>
            this.Match(_ => false, _ => false, _ => false, _ => true);

        void IEitherOfFour.MatchVoid(
            Action<object?> onFirst,
            Action<object?> onSecond,
            Action<object?> onThird,
            Action<object?> onFourth) =>
            // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
            this.Upcast<IEitherOfFour>()
                .Match(onFirst.ToFunc(), onSecond.ToFunc(), onThird.ToFunc(), onFourth.ToFunc());

        Task<T> IEitherOfFour.MatchAsync<T>(
            Func<object?, Task<T>> onFirst,
            Func<object?, Task<T>> onSecond,
            Func<object?, Task<T>> onThird,
            Func<object?, Task<T>> onFourth) =>
            this.Upcast<IEitherOfFour>().Match(onFirst, onSecond, onThird, onFourth);

        Task IEitherOfFour.MatchAsyncVoid(
            Func<object?, Task> onFirst,
            Func<object?, Task> onSecond,
            Func<object?, Task> onThird,
            Func<object?, Task> onFourth) =>
            this.Upcast<IEitherOfFour>()
                .MatchAsync(
                    onFirst.ToAsyncFunc(),
                    onSecond.ToAsyncFunc(),
                    onThird.ToAsyncFunc(),
                    onFourth.ToAsyncFunc());

        #endregion

        /// <summary>
        ///     Converts a value marked for the first position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.First{T}" />.</param>
        /// <returns>An either holding that value as its first case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.First{T}" /> usable in place of this type's own
        ///     <c>First</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default first value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3, T4>(Either.EitherFirst<T1>? value) =>
            First(value == null ? default(T1)! : value.Value);

        /// <summary>
        ///     Converts a value marked for the second position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.Second{T}" />.</param>
        /// <returns>An either holding that value as its second case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.Second{T}" /> usable in place of this type's own
        ///     <c>Second</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default second value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3, T4>(Either.EitherSecond<T2>? value) =>
            Second(value == null ? default(T2)! : value.Value);

        /// <summary>
        ///     Converts a value marked for the third position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.Third{T}" />.</param>
        /// <returns>An either holding that value as its third case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.Third{T}" /> usable in place of this type's own
        ///     <c>Third</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default third value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3, T4>(Either.EitherThird<T3>? value) =>
            Third(value == null ? default(T3)! : value.Value);

        /// <summary>
        ///     Converts a value marked for the fourth position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.Fourth{T}" />.</param>
        /// <returns>An either holding that value as its fourth case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.Fourth{T}" /> usable in place of this type's own
        ///     <c>Fourth</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default fourth value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3, T4>(Either.EitherFourth<T4>? value) =>
            Fourth(value == null ? default(T4)! : value.Value);

        /// <summary>
        ///     Determines whether two instances hold the same case with equal values.
        /// </summary>
        /// <param name="x">The first instance.</param>
        /// <param name="y">The second instance.</param>
        /// <returns>
        ///     <see langword="true" /> if both hold the same case and the values they hold are
        ///     equal according to <see cref="EqualityComparer{T}.Default" />.
        /// </returns>
        public static bool operator ==(Either<T1, T2, T3, T4> x, Either<T1, T2, T3, T4> y) =>
            x.valueType == y.valueType
            && EqualityComparer<T1>.Default.Equals(x.value1!, y.value1!)
            && EqualityComparer<T2>.Default.Equals(x.value2!, y.value2!)
            && EqualityComparer<T3>.Default.Equals(x.value3!, y.value3!)
            && EqualityComparer<T4>.Default.Equals(x.value4!, y.value4!);

        /// <summary>
        ///     Determines whether two instances differ, by negating <see cref="op_Equality" />.
        /// </summary>
        /// <param name="x">The first instance.</param>
        /// <param name="y">The second instance.</param>
        /// <returns>
        ///     <see langword="true" /> if the two hold different cases, or hold the same case with
        ///     values which are not equal.
        /// </returns>
        public static bool operator !=(Either<T1, T2, T3, T4> x, Either<T1, T2, T3, T4> y) => !(x == y);
        /// <summary>
        ///     Determines whether the given object is an either of this same type which is equal to
        ///     this one.
        /// </summary>
        /// <param name="obj">The object to compare against.</param>
        /// <returns>
        ///     <see langword="true" /> if <paramref name="obj" /> is an either of the same type which
        ///     <see cref="op_Equality" /> considers equal to this one.
        /// </returns>
        /// <remarks>
        ///     An either is never equal to the bare value it holds, only to another either.
        /// </remarks>
        public override bool Equals(object? obj) => obj is Either<T1, T2, T3, T4> e && this == e;

        /// <summary>
        ///     Determines whether the given instance is equal to this one.
        /// </summary>
        /// <param name="other">The instance to compare against.</param>
        /// <returns>
        ///     <see langword="true" /> if <see cref="op_Equality" /> considers the two equal.
        /// </returns>
        /// <remarks>
        ///     The same comparison as <see cref="op_Equality" />, under the name
        ///     <see cref="EqualityComparer{T}.Default" /> looks for, so that comparing these in a
        ///     collection does not box both operands the way <see cref="Equals(object)" /> must.
        /// </remarks>
        public bool Equals(Either<T1, T2, T3, T4> other) => this == other;

        /// <summary>
        ///     Returns a hash code consistent with <see cref="op_Equality" />.
        /// </summary>
        /// <returns>A hash code for this instance.</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = this.valueType;
                hashCode = (hashCode * 397) ^ EqualityComparer<T1>.Default.GetHashCode(this.value1!);
                hashCode = (hashCode * 397) ^ EqualityComparer<T2>.Default.GetHashCode(this.value2!);
                hashCode = (hashCode * 397) ^ EqualityComparer<T3>.Default.GetHashCode(this.value3!);
                hashCode = (hashCode * 397) ^ EqualityComparer<T4>.Default.GetHashCode(this.value4!);
                return hashCode;
            }
        }

        /// <summary>
        ///     Returns a readable description of this instance, for diagnostics.
        /// </summary>
        /// <returns>The name of the case held, followed by the value it holds.</returns>
        public override string ToString() => this.Match(
            v1 => $"First: {v1}",
            v2 => $"Second: {v2}",
            v3 => $"Third: {v3}",
            v4 => $"Fourth: {v4}");
    }

    /// <summary>
    ///     A value which is exactly one of five possibilities.
    /// </summary>
    /// <typeparam name="T1">The type of the first possibility.</typeparam>
    /// <typeparam name="T2">The type of the second possibility.</typeparam>
    /// <typeparam name="T3">The type of the third possibility.</typeparam>
    /// <typeparam name="T4">The type of the fourth possibility.</typeparam>
    /// <typeparam name="T5">The type of the fifth possibility.</typeparam>
    /// <remarks>
    ///     A discriminated union: the value is one of the five cases, and which one is part
    ///     of the value rather than something the caller has to track alongside it.
    ///
    ///     There is no property that hands the value out unchecked. Reach it with
    ///     <c>Match</c>, or with one of the helpers built on it, so that every case has to be
    ///     answered for.
    ///
    ///     This is a struct, so <see langword="default" /> is a valid instance; it holds the
    ///     first case, with the default value of <typeparamref name="T1" />.
    /// </remarks>
    public struct Either<T1, T2, T3, T4, T5> : IEitherOfFive, IEquatable<Either<T1, T2, T3, T4, T5>>
    {
        private readonly int valueType;
        private readonly T1? value1;
        private readonly T2? value2;
        private readonly T3? value3;
        private readonly T4? value4;
        private readonly T5? value5;

        private Either(int valueType, T1? value1, T2? value2, T3? value3, T4? value4, T5? value5)
        {
            this.valueType = valueType;
            this.value1 = value1;
            this.value2 = value2;
            this.value3 = value3;
            this.value4 = value4;
            this.value5 = value5;
        }

        #region Type Constructors

        /// <summary>
        ///     Creates an either holding the given value as its first case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its first case.</returns>
        /// <remarks>
        ///     <see cref="Either.First{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3, T4, T5> First(T1 value) => new Either<T1, T2, T3, T4, T5>(
            0,
            value,
            default(T2),
            default(T3),
            default(T4),
            default(T5));

        /// <summary>
        ///     Creates an either holding the given value as its second case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its second case.</returns>
        /// <remarks>
        ///     <see cref="Either.Second{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3, T4, T5> Second(T2 value) => new Either<T1, T2, T3, T4, T5>(
            1,
            default(T1),
            value,
            default(T3),
            default(T4),
            default(T5));

        /// <summary>
        ///     Creates an either holding the given value as its third case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its third case.</returns>
        /// <remarks>
        ///     <see cref="Either.Third{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3, T4, T5> Third(T3 value) => new Either<T1, T2, T3, T4, T5>(
            2,
            default(T1),
            default(T2),
            value,
            default(T4),
            default(T5));

        /// <summary>
        ///     Creates an either holding the given value as its fourth case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its fourth case.</returns>
        /// <remarks>
        ///     <see cref="Either.Fourth{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3, T4, T5> Fourth(T4 value) => new Either<T1, T2, T3, T4, T5>(
            3,
            default(T1),
            default(T2),
            default(T3),
            value,
            default(T5));

        /// <summary>
        ///     Creates an either holding the given value as its fifth case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its fifth case.</returns>
        /// <remarks>
        ///     <see cref="Either.Fifth{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3, T4, T5> Fifth(T5 value) => new Either<T1, T2, T3, T4, T5>(
            4,
            default(T1),
            default(T2),
            default(T3),
            default(T4),
            value);

        #endregion

        #region Base Functionality

        T IEitherOfFive.Match<T>(
            Func<object?, T> onFirst,
            Func<object?, T> onSecond,
            Func<object?, T> onThird,
            Func<object?, T> onFourth,
            Func<object?, T> onFifth) =>
            this.Match(v => onFirst(v), v => onSecond(v), v => onThird(v), v => onFourth(v), v => onFifth(v));

        object IEither.GetValueAsObject() => Either.GetValueAs<object>().From(this);

        /// <summary>
        ///     Runs one of the five functions depending on which case is held, and returns its
        ///     result.
        /// </summary>
        /// <typeparam name="T">The type each of the functions returns.</typeparam>
        /// <param name="onFirst">Run with the value when the first case is held.</param>
        /// <param name="onSecond">Run with the value when the second case is held.</param>
        /// <param name="onThird">Run with the value when the third case is held.</param>
        /// <param name="onFourth">Run with the value when the fourth case is held.</param>
        /// <param name="onFifth">Run with the value when the fifth case is held.</param>
        /// <returns>Whatever the function that was run returned.</returns>
        /// <remarks>
        ///     This is the only way the held value is reached, and every other member here is
        ///     expressed in terms of it. Exactly one of the functions is called, and it is called
        ///     before this method returns.
        /// </remarks>
        public T Match<T>(
            [JetBrains.Annotations.InstantHandle] Func<T1, T> onFirst,
            [JetBrains.Annotations.InstantHandle] Func<T2, T> onSecond,
            [JetBrains.Annotations.InstantHandle] Func<T3, T> onThird,
            [JetBrains.Annotations.InstantHandle] Func<T4, T> onFourth,
            [JetBrains.Annotations.InstantHandle] Func<T5, T> onFifth) =>
            this.valueType == 0
                ? onFirst(this.value1!)
                : (this.valueType == 1
                    ? onSecond(this.value2!)
                    : (this.valueType == 2
                        ? onThird(this.value3!)
                        : (this.valueType == 3 ? onFourth(this.value4!) : onFifth(this.value5!))));

        #endregion

        #region Helper Methods

        /// <summary>
        ///     Runs one of the five actions depending on which case is held.
        /// </summary>
        /// <param name="onFirst">Run with the value when the first case is held.</param>
        /// <param name="onSecond">Run with the value when the second case is held.</param>
        /// <param name="onThird">Run with the value when the third case is held.</param>
        /// <param name="onFourth">Run with the value when the fourth case is held.</param>
        /// <param name="onFifth">Run with the value when the fifth case is held.</param>
        public void MatchVoid(
            [JetBrains.Annotations.InstantHandle] Action<T1> onFirst,
            [JetBrains.Annotations.InstantHandle] Action<T2> onSecond,
            [JetBrains.Annotations.InstantHandle] Action<T3> onThird,
            [JetBrains.Annotations.InstantHandle] Action<T4> onFourth,
            [JetBrains.Annotations.InstantHandle] Action<T5> onFifth) =>
            // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
            this.Match(onFirst.ToFunc(), onSecond.ToFunc(), onThird.ToFunc(), onFourth.ToFunc(), onFifth.ToFunc());

        /// <summary>
        ///     Runs one of the five asynchronous functions depending on which case is held, and
        ///     returns its result.
        /// </summary>
        /// <typeparam name="T">The type each of the functions produces.</typeparam>
        /// <param name="onFirst">Run with the value when the first case is held.</param>
        /// <param name="onSecond">Run with the value when the second case is held.</param>
        /// <param name="onThird">Run with the value when the third case is held.</param>
        /// <param name="onFourth">Run with the value when the fourth case is held.</param>
        /// <param name="onFifth">Run with the value when the fifth case is held.</param>
        /// <returns>The task returned by whichever function was run.</returns>
        /// <remarks>
        ///     Only the selected function is invoked; the returned task is its task, not a wrapper,
        ///     so failures surface as that task faulting rather than as an exception from this call.
        /// </remarks>
        public Task<T> MatchAsync<T>(
            [JetBrains.Annotations.InstantHandle] Func<T1, Task<T>> onFirst,
            [JetBrains.Annotations.InstantHandle] Func<T2, Task<T>> onSecond,
            [JetBrains.Annotations.InstantHandle] Func<T3, Task<T>> onThird,
            [JetBrains.Annotations.InstantHandle] Func<T4, Task<T>> onFourth,
            [JetBrains.Annotations.InstantHandle] Func<T5, Task<T>> onFifth) =>
            this.Match(onFirst, onSecond, onThird, onFourth, onFifth);

        /// <summary>
        ///     Runs one of the five asynchronous actions depending on which case is held.
        /// </summary>
        /// <param name="onFirst">Run with the value when the first case is held.</param>
        /// <param name="onSecond">Run with the value when the second case is held.</param>
        /// <param name="onThird">Run with the value when the third case is held.</param>
        /// <param name="onFourth">Run with the value when the fourth case is held.</param>
        /// <param name="onFifth">Run with the value when the fifth case is held.</param>
        /// <returns>A task which completes when the selected action has completed.</returns>
        public Task MatchAsyncVoid(
            [JetBrains.Annotations.InstantHandle] Func<T1, Task> onFirst,
            [JetBrains.Annotations.InstantHandle] Func<T2, Task> onSecond,
            [JetBrains.Annotations.InstantHandle] Func<T3, Task> onThird,
            [JetBrains.Annotations.InstantHandle] Func<T4, Task> onFourth,
            [JetBrains.Annotations.InstantHandle] Func<T5, Task> onFifth) =>
            this.MatchAsync(
                onFirst.ToAsyncFunc(),
                onSecond.ToAsyncFunc(),
                onThird.ToAsyncFunc(),
                onFourth.ToAsyncFunc(),
                onFifth.ToAsyncFunc());

        /// <summary>
        ///     Transforms the value if this holds the first case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the first case is transformed into.</typeparam>
        /// <param name="f">The function to transform the first case with.</param>
        /// <returns>
        ///     An either whose first case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the first case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the first case.
        /// </remarks>
        public Either<T, T2, T3, T4, T5> MapFirst<T>([JetBrains.Annotations.InstantHandle] Func<T1, T> f) =>
            this.Match(
                v1 => Either<T, T2, T3, T4, T5>.First(f(v1)),
                v2 => Either.Second(v2),
                v3 => Either.Third(v3),
                v4 => Either.Fourth(v4),
                v5 => Either.Fifth(v5));

        /// <summary>
        ///     Transforms the value if this holds the second case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the second case is transformed into.</typeparam>
        /// <param name="f">The function to transform the second case with.</param>
        /// <returns>
        ///     An either whose second case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the second case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the second case.
        /// </remarks>
        public Either<T1, T, T3, T4, T5> MapSecond<T>([JetBrains.Annotations.InstantHandle] Func<T2, T> f) =>
            this.Match(
                Either<T1, T, T3, T4, T5>.First,
                v2 => Either.Second(f(v2)),
                v3 => Either.Third(v3),
                v4 => Either.Fourth(v4),
                v5 => Either.Fifth(v5));

        /// <summary>
        ///     Transforms the value if this holds the third case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the third case is transformed into.</typeparam>
        /// <param name="f">The function to transform the third case with.</param>
        /// <returns>
        ///     An either whose third case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the third case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the third case.
        /// </remarks>
        public Either<T1, T2, T, T4, T5> MapThird<T>([JetBrains.Annotations.InstantHandle] Func<T3, T> f) =>
            this.Match(
                Either<T1, T2, T, T4, T5>.First,
                v2 => Either.Second(v2),
                v3 => Either.Third(f(v3)),
                v4 => Either.Fourth(v4),
                v5 => Either.Fifth(v5));

        /// <summary>
        ///     Transforms the value if this holds the fourth case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the fourth case is transformed into.</typeparam>
        /// <param name="f">The function to transform the fourth case with.</param>
        /// <returns>
        ///     An either whose fourth case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the fourth case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the fourth case.
        /// </remarks>
        public Either<T1, T2, T3, T, T5> MapFourth<T>([JetBrains.Annotations.InstantHandle] Func<T4, T> f) =>
            this.Match(
                Either<T1, T2, T3, T, T5>.First,
                v2 => Either.Second(v2),
                v3 => Either.Third(v3),
                v4 => Either.Fourth(f(v4)),
                v5 => Either.Fifth(v5));

        /// <summary>
        ///     Transforms the value if this holds the fifth case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the fifth case is transformed into.</typeparam>
        /// <param name="f">The function to transform the fifth case with.</param>
        /// <returns>
        ///     An either whose fifth case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the fifth case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the fifth case.
        /// </remarks>
        public Either<T1, T2, T3, T4, T> MapFifth<T>([JetBrains.Annotations.InstantHandle] Func<T5, T> f) =>
            this.Match(
                Either<T1, T2, T3, T4, T>.First,
                v2 => Either.Second(v2),
                v3 => Either.Third(v3),
                v4 => Either.Fourth(v4),
                v5 => Either.Fifth(f(v5)));

        /// <summary>
        ///     Gets the held value if this holds the first case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the first case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T1> TryGetFirst() =>
            this.Match(Maybe.Some, _ => Maybe.None, _ => Maybe.None, _ => Maybe.None, _ => Maybe.None);

        /// <summary>
        ///     Gets the held value if this holds the second case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the second case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T2> TryGetSecond() =>
            this.Match(_ => Maybe.None, Maybe.Some, _ => Maybe.None, _ => Maybe.None, _ => Maybe.None);

        /// <summary>
        ///     Gets the held value if this holds the third case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the third case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T3> TryGetThird() =>
            this.Match(_ => Maybe.None, _ => Maybe.None, Maybe.Some, _ => Maybe.None, _ => Maybe.None);

        /// <summary>
        ///     Gets the held value if this holds the fourth case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the fourth case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T4> TryGetFourth() =>
            this.Match(_ => Maybe.None, _ => Maybe.None, _ => Maybe.None, Maybe.Some, _ => Maybe.None);

        /// <summary>
        ///     Gets the held value if this holds the fifth case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the fifth case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T5> TryGetFifth() =>
            this.Match(_ => Maybe.None, _ => Maybe.None, _ => Maybe.None, _ => Maybe.None, Maybe.Some);

        /// <summary>
        ///     Returns whether this holds the first case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the first case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetFirst" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsFirst() =>
            this.Match(_ => true, _ => false, _ => false, _ => false, _ => false);

        /// <summary>
        ///     Returns whether this holds the second case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the second case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetSecond" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsSecond() =>
            this.Match(_ => false, _ => true, _ => false, _ => false, _ => false);

        /// <summary>
        ///     Returns whether this holds the third case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the third case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetThird" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsThird() =>
            this.Match(_ => false, _ => false, _ => true, _ => false, _ => false);

        /// <summary>
        ///     Returns whether this holds the fourth case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the fourth case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetFourth" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsFourth() =>
            this.Match(_ => false, _ => false, _ => false, _ => true, _ => false);

        /// <summary>
        ///     Returns whether this holds the fifth case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the fifth case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetFifth" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsFifth() =>
            this.Match(_ => false, _ => false, _ => false, _ => false, _ => true);

        void IEitherOfFive.MatchVoid(
            Action<object?> onFirst,
            Action<object?> onSecond,
            Action<object?> onThird,
            Action<object?> onFourth,
            Action<object?> onFifth) =>
            // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
            this.Upcast<IEitherOfFive>()
                .Match(onFirst.ToFunc(), onSecond.ToFunc(), onThird.ToFunc(), onFourth.ToFunc(), onFifth.ToFunc());

        Task<T> IEitherOfFive.MatchAsync<T>(
            Func<object?, Task<T>> onFirst,
            Func<object?, Task<T>> onSecond,
            Func<object?, Task<T>> onThird,
            Func<object?, Task<T>> onFourth,
            Func<object?, Task<T>> onFifth) =>
            this.Upcast<IEitherOfFive>().Match(onFirst, onSecond, onThird, onFourth, onFifth);

        Task IEitherOfFive.MatchAsyncVoid(
            Func<object?, Task> onFirst,
            Func<object?, Task> onSecond,
            Func<object?, Task> onThird,
            Func<object?, Task> onFourth,
            Func<object?, Task> onFifth) =>
            this.Upcast<IEitherOfFive>()
                .MatchAsync(
                    onFirst.ToAsyncFunc(),
                    onSecond.ToAsyncFunc(),
                    onThird.ToAsyncFunc(),
                    onFourth.ToAsyncFunc(),
                    onFifth.ToAsyncFunc());

        #endregion

        /// <summary>
        ///     Converts a value marked for the first position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.First{T}" />.</param>
        /// <returns>An either holding that value as its first case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.First{T}" /> usable in place of this type's own
        ///     <c>First</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default first value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3, T4, T5>(Either.EitherFirst<T1>? value) =>
            First(value == null ? default(T1)! : value.Value);

        /// <summary>
        ///     Converts a value marked for the second position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.Second{T}" />.</param>
        /// <returns>An either holding that value as its second case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.Second{T}" /> usable in place of this type's own
        ///     <c>Second</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default second value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3, T4, T5>(Either.EitherSecond<T2>? value) =>
            Second(value == null ? default(T2)! : value.Value);

        /// <summary>
        ///     Converts a value marked for the third position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.Third{T}" />.</param>
        /// <returns>An either holding that value as its third case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.Third{T}" /> usable in place of this type's own
        ///     <c>Third</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default third value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3, T4, T5>(Either.EitherThird<T3>? value) =>
            Third(value == null ? default(T3)! : value.Value);

        /// <summary>
        ///     Converts a value marked for the fourth position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.Fourth{T}" />.</param>
        /// <returns>An either holding that value as its fourth case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.Fourth{T}" /> usable in place of this type's own
        ///     <c>Fourth</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default fourth value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3, T4, T5>(Either.EitherFourth<T4>? value) =>
            Fourth(value == null ? default(T4)! : value.Value);

        /// <summary>
        ///     Converts a value marked for the fifth position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.Fifth{T}" />.</param>
        /// <returns>An either holding that value as its fifth case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.Fifth{T}" /> usable in place of this type's own
        ///     <c>Fifth</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default fifth value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3, T4, T5>(Either.EitherFifth<T5>? value) =>
            Fifth(value == null ? default(T5)! : value.Value);

        /// <summary>
        ///     Determines whether two instances hold the same case with equal values.
        /// </summary>
        /// <param name="x">The first instance.</param>
        /// <param name="y">The second instance.</param>
        /// <returns>
        ///     <see langword="true" /> if both hold the same case and the values they hold are
        ///     equal according to <see cref="EqualityComparer{T}.Default" />.
        /// </returns>
        public static bool operator ==(Either<T1, T2, T3, T4, T5> x, Either<T1, T2, T3, T4, T5> y) =>
            x.valueType == y.valueType
            && EqualityComparer<T1>.Default.Equals(x.value1!, y.value1!)
            && EqualityComparer<T2>.Default.Equals(x.value2!, y.value2!)
            && EqualityComparer<T3>.Default.Equals(x.value3!, y.value3!)
            && EqualityComparer<T4>.Default.Equals(x.value4!, y.value4!)
            && EqualityComparer<T5>.Default.Equals(x.value5!, y.value5!);

        /// <summary>
        ///     Determines whether two instances differ, by negating <see cref="op_Equality" />.
        /// </summary>
        /// <param name="x">The first instance.</param>
        /// <param name="y">The second instance.</param>
        /// <returns>
        ///     <see langword="true" /> if the two hold different cases, or hold the same case with
        ///     values which are not equal.
        /// </returns>
        public static bool operator !=(Either<T1, T2, T3, T4, T5> x, Either<T1, T2, T3, T4, T5> y) => !(x == y);
        /// <summary>
        ///     Determines whether the given object is an either of this same type which is equal to
        ///     this one.
        /// </summary>
        /// <param name="obj">The object to compare against.</param>
        /// <returns>
        ///     <see langword="true" /> if <paramref name="obj" /> is an either of the same type which
        ///     <see cref="op_Equality" /> considers equal to this one.
        /// </returns>
        /// <remarks>
        ///     An either is never equal to the bare value it holds, only to another either.
        /// </remarks>
        public override bool Equals(object? obj) => obj is Either<T1, T2, T3, T4, T5> e && this == e;

        /// <summary>
        ///     Determines whether the given instance is equal to this one.
        /// </summary>
        /// <param name="other">The instance to compare against.</param>
        /// <returns>
        ///     <see langword="true" /> if <see cref="op_Equality" /> considers the two equal.
        /// </returns>
        /// <remarks>
        ///     The same comparison as <see cref="op_Equality" />, under the name
        ///     <see cref="EqualityComparer{T}.Default" /> looks for, so that comparing these in a
        ///     collection does not box both operands the way <see cref="Equals(object)" /> must.
        /// </remarks>
        public bool Equals(Either<T1, T2, T3, T4, T5> other) => this == other;

        /// <summary>
        ///     Returns a hash code consistent with <see cref="op_Equality" />.
        /// </summary>
        /// <returns>A hash code for this instance.</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = this.valueType;
                hashCode = (hashCode * 397) ^ EqualityComparer<T1>.Default.GetHashCode(this.value1!);
                hashCode = (hashCode * 397) ^ EqualityComparer<T2>.Default.GetHashCode(this.value2!);
                hashCode = (hashCode * 397) ^ EqualityComparer<T3>.Default.GetHashCode(this.value3!);
                hashCode = (hashCode * 397) ^ EqualityComparer<T4>.Default.GetHashCode(this.value4!);
                hashCode = (hashCode * 397) ^ EqualityComparer<T5>.Default.GetHashCode(this.value5!);
                return hashCode;
            }
        }

        /// <summary>
        ///     Returns a readable description of this instance, for diagnostics.
        /// </summary>
        /// <returns>The name of the case held, followed by the value it holds.</returns>
        public override string ToString() => this.Match(
            v1 => $"First: {v1}",
            v2 => $"Second: {v2}",
            v3 => $"Third: {v3}",
            v4 => $"Fourth: {v4}",
            v5 => $"Fifth: {v5}");
    }

    /// <summary>
    ///     A value which is exactly one of six possibilities.
    /// </summary>
    /// <typeparam name="T1">The type of the first possibility.</typeparam>
    /// <typeparam name="T2">The type of the second possibility.</typeparam>
    /// <typeparam name="T3">The type of the third possibility.</typeparam>
    /// <typeparam name="T4">The type of the fourth possibility.</typeparam>
    /// <typeparam name="T5">The type of the fifth possibility.</typeparam>
    /// <typeparam name="T6">The type of the sixth possibility.</typeparam>
    /// <remarks>
    ///     A discriminated union: the value is one of the six cases, and which one is part
    ///     of the value rather than something the caller has to track alongside it.
    ///
    ///     There is no property that hands the value out unchecked. Reach it with
    ///     <c>Match</c>, or with one of the helpers built on it, so that every case has to be
    ///     answered for.
    ///
    ///     This is a struct, so <see langword="default" /> is a valid instance; it holds the
    ///     first case, with the default value of <typeparamref name="T1" />.
    /// </remarks>
    public struct Either<T1, T2, T3, T4, T5, T6> : IEitherOfSix, IEquatable<Either<T1, T2, T3, T4, T5, T6>>
    {
        private readonly int valueType;
        private readonly T1? value1;
        private readonly T2? value2;
        private readonly T3? value3;
        private readonly T4? value4;
        private readonly T5? value5;
        private readonly T6? value6;

        internal Either(int valueType, T1? value1, T2? value2, T3? value3, T4? value4, T5? value5, T6? value6)
        {
            this.valueType = valueType;
            this.value1 = value1;
            this.value2 = value2;
            this.value3 = value3;
            this.value4 = value4;
            this.value5 = value5;
            this.value6 = value6;
        }

        #region Type Constructors

        /// <summary>
        ///     Creates an either holding the given value as its first case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its first case.</returns>
        /// <remarks>
        ///     <see cref="Either.First{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3, T4, T5, T6> First(T1 value) => new Either<T1, T2, T3, T4, T5, T6>(
            0,
            value,
            default(T2),
            default(T3),
            default(T4),
            default(T5),
            default(T6));

        /// <summary>
        ///     Creates an either holding the given value as its second case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its second case.</returns>
        /// <remarks>
        ///     <see cref="Either.Second{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3, T4, T5, T6> Second(T2 value) => new Either<T1, T2, T3, T4, T5, T6>(
            1,
            default(T1),
            value,
            default(T3),
            default(T4),
            default(T5),
            default(T6));

        /// <summary>
        ///     Creates an either holding the given value as its third case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its third case.</returns>
        /// <remarks>
        ///     <see cref="Either.Third{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3, T4, T5, T6> Third(T3 value) => new Either<T1, T2, T3, T4, T5, T6>(
            2,
            default(T1),
            default(T2),
            value,
            default(T4),
            default(T5),
            default(T6));

        /// <summary>
        ///     Creates an either holding the given value as its fourth case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its fourth case.</returns>
        /// <remarks>
        ///     <see cref="Either.Fourth{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3, T4, T5, T6> Fourth(T4 value) => new Either<T1, T2, T3, T4, T5, T6>(
            3,
            default(T1),
            default(T2),
            default(T3),
            value,
            default(T5),
            default(T6));

        /// <summary>
        ///     Creates an either holding the given value as its fifth case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its fifth case.</returns>
        /// <remarks>
        ///     <see cref="Either.Fifth{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3, T4, T5, T6> Fifth(T5 value) => new Either<T1, T2, T3, T4, T5, T6>(
            4,
            default(T1),
            default(T2),
            default(T3),
            default(T4),
            value,
            default(T6));

        /// <summary>
        ///     Creates an either holding the given value as its sixth case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its sixth case.</returns>
        /// <remarks>
        ///     <see cref="Either.Sixth{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3, T4, T5, T6> Sixth(T6 value) => new Either<T1, T2, T3, T4, T5, T6>(
            5,
            default(T1),
            default(T2),
            default(T3),
            default(T4),
            default(T5),
            value);

        #endregion

        #region Base Functionality

        T IEitherOfSix.Match<T>(
            Func<object?, T> onFirst,
            Func<object?, T> onSecond,
            Func<object?, T> onThird,
            Func<object?, T> onFourth,
            Func<object?, T> onFifth,
            Func<object?, T> onSixth) =>
            this.Match(
                v => onFirst(v),
                v => onSecond(v),
                v => onThird(v),
                v => onFourth(v),
                v => onFifth(v),
                v => onSixth(v));

        object IEither.GetValueAsObject() => Either.GetValueAs<object>().From(this);

        /// <summary>
        ///     Runs one of the six functions depending on which case is held, and returns its
        ///     result.
        /// </summary>
        /// <typeparam name="T">The type each of the functions returns.</typeparam>
        /// <param name="onFirst">Run with the value when the first case is held.</param>
        /// <param name="onSecond">Run with the value when the second case is held.</param>
        /// <param name="onThird">Run with the value when the third case is held.</param>
        /// <param name="onFourth">Run with the value when the fourth case is held.</param>
        /// <param name="onFifth">Run with the value when the fifth case is held.</param>
        /// <param name="onSixth">Run with the value when the sixth case is held.</param>
        /// <returns>Whatever the function that was run returned.</returns>
        /// <remarks>
        ///     This is the only way the held value is reached, and every other member here is
        ///     expressed in terms of it. Exactly one of the functions is called, and it is called
        ///     before this method returns.
        /// </remarks>
        public T Match<T>(
            [JetBrains.Annotations.InstantHandle] Func<T1, T> onFirst,
            [JetBrains.Annotations.InstantHandle] Func<T2, T> onSecond,
            [JetBrains.Annotations.InstantHandle] Func<T3, T> onThird,
            [JetBrains.Annotations.InstantHandle] Func<T4, T> onFourth,
            [JetBrains.Annotations.InstantHandle] Func<T5, T> onFifth,
            [JetBrains.Annotations.InstantHandle] Func<T6, T> onSixth) =>
            this.valueType == 0
                ? onFirst(this.value1!)
                : (this.valueType == 1
                    ? onSecond(this.value2!)
                    : (this.valueType == 2
                        ? onThird(this.value3!)
                        : (this.valueType == 3
                            ? onFourth(this.value4!)
                            : (this.valueType == 4 ? onFifth(this.value5!) : onSixth(this.value6!)))));

        #endregion

        #region Helper Methods

        /// <summary>
        ///     Runs one of the six actions depending on which case is held.
        /// </summary>
        /// <param name="onFirst">Run with the value when the first case is held.</param>
        /// <param name="onSecond">Run with the value when the second case is held.</param>
        /// <param name="onThird">Run with the value when the third case is held.</param>
        /// <param name="onFourth">Run with the value when the fourth case is held.</param>
        /// <param name="onFifth">Run with the value when the fifth case is held.</param>
        /// <param name="onSixth">Run with the value when the sixth case is held.</param>
        public void MatchVoid(
            [JetBrains.Annotations.InstantHandle] Action<T1> onFirst,
            [JetBrains.Annotations.InstantHandle] Action<T2> onSecond,
            [JetBrains.Annotations.InstantHandle] Action<T3> onThird,
            [JetBrains.Annotations.InstantHandle] Action<T4> onFourth,
            [JetBrains.Annotations.InstantHandle] Action<T5> onFifth,
            [JetBrains.Annotations.InstantHandle] Action<T6> onSixth) =>
            // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
            this.Match(
                onFirst.ToFunc(),
                onSecond.ToFunc(),
                onThird.ToFunc(),
                onFourth.ToFunc(),
                onFifth.ToFunc(),
                onSixth.ToFunc());

        /// <summary>
        ///     Runs one of the six asynchronous functions depending on which case is held, and
        ///     returns its result.
        /// </summary>
        /// <typeparam name="T">The type each of the functions produces.</typeparam>
        /// <param name="onFirst">Run with the value when the first case is held.</param>
        /// <param name="onSecond">Run with the value when the second case is held.</param>
        /// <param name="onThird">Run with the value when the third case is held.</param>
        /// <param name="onFourth">Run with the value when the fourth case is held.</param>
        /// <param name="onFifth">Run with the value when the fifth case is held.</param>
        /// <param name="onSixth">Run with the value when the sixth case is held.</param>
        /// <returns>The task returned by whichever function was run.</returns>
        /// <remarks>
        ///     Only the selected function is invoked; the returned task is its task, not a wrapper,
        ///     so failures surface as that task faulting rather than as an exception from this call.
        /// </remarks>
        public Task<T> MatchAsync<T>(
            [JetBrains.Annotations.InstantHandle] Func<T1, Task<T>> onFirst,
            [JetBrains.Annotations.InstantHandle] Func<T2, Task<T>> onSecond,
            [JetBrains.Annotations.InstantHandle] Func<T3, Task<T>> onThird,
            [JetBrains.Annotations.InstantHandle] Func<T4, Task<T>> onFourth,
            [JetBrains.Annotations.InstantHandle] Func<T5, Task<T>> onFifth,
            [JetBrains.Annotations.InstantHandle] Func<T6, Task<T>> onSixth) =>
            this.Match(onFirst, onSecond, onThird, onFourth, onFifth, onSixth);

        /// <summary>
        ///     Runs one of the six asynchronous actions depending on which case is held.
        /// </summary>
        /// <param name="onFirst">Run with the value when the first case is held.</param>
        /// <param name="onSecond">Run with the value when the second case is held.</param>
        /// <param name="onThird">Run with the value when the third case is held.</param>
        /// <param name="onFourth">Run with the value when the fourth case is held.</param>
        /// <param name="onFifth">Run with the value when the fifth case is held.</param>
        /// <param name="onSixth">Run with the value when the sixth case is held.</param>
        /// <returns>A task which completes when the selected action has completed.</returns>
        public Task MatchAsyncVoid(
            [JetBrains.Annotations.InstantHandle] Func<T1, Task> onFirst,
            [JetBrains.Annotations.InstantHandle] Func<T2, Task> onSecond,
            [JetBrains.Annotations.InstantHandle] Func<T3, Task> onThird,
            [JetBrains.Annotations.InstantHandle] Func<T4, Task> onFourth,
            [JetBrains.Annotations.InstantHandle] Func<T5, Task> onFifth,
            [JetBrains.Annotations.InstantHandle] Func<T6, Task> onSixth) =>
            this.MatchAsync(
                onFirst.ToAsyncFunc(),
                onSecond.ToAsyncFunc(),
                onThird.ToAsyncFunc(),
                onFourth.ToAsyncFunc(),
                onFifth.ToAsyncFunc(),
                onSixth.ToAsyncFunc());

        /// <summary>
        ///     Transforms the value if this holds the first case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the first case is transformed into.</typeparam>
        /// <param name="f">The function to transform the first case with.</param>
        /// <returns>
        ///     An either whose first case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the first case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the first case.
        /// </remarks>
        public Either<T, T2, T3, T4, T5, T6> MapFirst<T>([JetBrains.Annotations.InstantHandle] Func<T1, T> f) =>
            this.Match(
                v1 => Either<T, T2, T3, T4, T5, T6>.First(f(v1)),
                v2 => Either.Second(v2),
                v3 => Either.Third(v3),
                v4 => Either.Fourth(v4),
                v5 => Either.Fifth(v5),
                v6 => Either.Sixth(v6));

        /// <summary>
        ///     Transforms the value if this holds the second case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the second case is transformed into.</typeparam>
        /// <param name="f">The function to transform the second case with.</param>
        /// <returns>
        ///     An either whose second case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the second case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the second case.
        /// </remarks>
        public Either<T1, T, T3, T4, T5, T6> MapSecond<T>([JetBrains.Annotations.InstantHandle] Func<T2, T> f) =>
            this.Match(
                Either<T1, T, T3, T4, T5, T6>.First,
                v2 => Either.Second(f(v2)),
                v3 => Either.Third(v3),
                v4 => Either.Fourth(v4),
                v5 => Either.Fifth(v5),
                v6 => Either.Sixth(v6));

        /// <summary>
        ///     Transforms the value if this holds the third case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the third case is transformed into.</typeparam>
        /// <param name="f">The function to transform the third case with.</param>
        /// <returns>
        ///     An either whose third case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the third case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the third case.
        /// </remarks>
        public Either<T1, T2, T, T4, T5, T6> MapThird<T>([JetBrains.Annotations.InstantHandle] Func<T3, T> f) =>
            this.Match(
                Either<T1, T2, T, T4, T5, T6>.First,
                v2 => Either.Second(v2),
                v3 => Either.Third(f(v3)),
                v4 => Either.Fourth(v4),
                v5 => Either.Fifth(v5),
                v6 => Either.Sixth(v6));

        /// <summary>
        ///     Transforms the value if this holds the fourth case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the fourth case is transformed into.</typeparam>
        /// <param name="f">The function to transform the fourth case with.</param>
        /// <returns>
        ///     An either whose fourth case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the fourth case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the fourth case.
        /// </remarks>
        public Either<T1, T2, T3, T, T5, T6> MapFourth<T>([JetBrains.Annotations.InstantHandle] Func<T4, T> f) =>
            this.Match(
                Either<T1, T2, T3, T, T5, T6>.First,
                v2 => Either.Second(v2),
                v3 => Either.Third(v3),
                v4 => Either.Fourth(f(v4)),
                v5 => Either.Fifth(v5),
                v6 => Either.Sixth(v6));

        /// <summary>
        ///     Transforms the value if this holds the fifth case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the fifth case is transformed into.</typeparam>
        /// <param name="f">The function to transform the fifth case with.</param>
        /// <returns>
        ///     An either whose fifth case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the fifth case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the fifth case.
        /// </remarks>
        public Either<T1, T2, T3, T4, T, T6> MapFifth<T>([JetBrains.Annotations.InstantHandle] Func<T5, T> f) =>
            this.Match(
                Either<T1, T2, T3, T4, T, T6>.First,
                v2 => Either.Second(v2),
                v3 => Either.Third(v3),
                v4 => Either.Fourth(v4),
                v5 => Either.Fifth(f(v5)),
                v6 => Either.Sixth(v6));

        /// <summary>
        ///     Transforms the value if this holds the sixth case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the sixth case is transformed into.</typeparam>
        /// <param name="f">The function to transform the sixth case with.</param>
        /// <returns>
        ///     An either whose sixth case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the sixth case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the sixth case.
        /// </remarks>
        public Either<T1, T2, T3, T4, T5, T> MapSixth<T>([JetBrains.Annotations.InstantHandle] Func<T6, T> f) =>
            this.Match(
                Either<T1, T2, T3, T4, T5, T>.First,
                v2 => Either.Second(v2),
                v3 => Either.Third(v3),
                v4 => Either.Fourth(v4),
                v5 => Either.Fifth(v5),
                v6 => Either.Sixth(f(v6)));

        /// <summary>
        ///     Gets the held value if this holds the first case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the first case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T1> TryGetFirst() =>
            this.Match(Maybe.Some, _ => Maybe.None, _ => Maybe.None, _ => Maybe.None, _ => Maybe.None, _ => Maybe.None);

        /// <summary>
        ///     Gets the held value if this holds the second case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the second case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T2> TryGetSecond() =>
            this.Match(_ => Maybe.None, Maybe.Some, _ => Maybe.None, _ => Maybe.None, _ => Maybe.None, _ => Maybe.None);

        /// <summary>
        ///     Gets the held value if this holds the third case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the third case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T3> TryGetThird() =>
            this.Match(_ => Maybe.None, _ => Maybe.None, Maybe.Some, _ => Maybe.None, _ => Maybe.None, _ => Maybe.None);

        /// <summary>
        ///     Gets the held value if this holds the fourth case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the fourth case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T4> TryGetFourth() =>
            this.Match(_ => Maybe.None, _ => Maybe.None, _ => Maybe.None, Maybe.Some, _ => Maybe.None, _ => Maybe.None);

        /// <summary>
        ///     Gets the held value if this holds the fifth case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the fifth case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T5> TryGetFifth() =>
            this.Match(_ => Maybe.None, _ => Maybe.None, _ => Maybe.None, _ => Maybe.None, Maybe.Some, _ => Maybe.None);

        /// <summary>
        ///     Gets the held value if this holds the sixth case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the sixth case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T6> TryGetSixth() =>
            this.Match(_ => Maybe.None, _ => Maybe.None, _ => Maybe.None, _ => Maybe.None, _ => Maybe.None, Maybe.Some);

        /// <summary>
        ///     Returns whether this holds the first case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the first case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetFirst" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsFirst() =>
            this.Match(_ => true, _ => false, _ => false, _ => false, _ => false, _ => false);

        /// <summary>
        ///     Returns whether this holds the second case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the second case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetSecond" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsSecond() =>
            this.Match(_ => false, _ => true, _ => false, _ => false, _ => false, _ => false);

        /// <summary>
        ///     Returns whether this holds the third case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the third case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetThird" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsThird() =>
            this.Match(_ => false, _ => false, _ => true, _ => false, _ => false, _ => false);

        /// <summary>
        ///     Returns whether this holds the fourth case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the fourth case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetFourth" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsFourth() =>
            this.Match(_ => false, _ => false, _ => false, _ => true, _ => false, _ => false);

        /// <summary>
        ///     Returns whether this holds the fifth case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the fifth case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetFifth" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsFifth() =>
            this.Match(_ => false, _ => false, _ => false, _ => false, _ => true, _ => false);

        /// <summary>
        ///     Returns whether this holds the sixth case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the sixth case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetSixth" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsSixth() =>
            this.Match(_ => false, _ => false, _ => false, _ => false, _ => false, _ => true);

        void IEitherOfSix.MatchVoid(
            Action<object?> onFirst,
            Action<object?> onSecond,
            Action<object?> onThird,
            Action<object?> onFourth,
            Action<object?> onFifth,
            Action<object?> onSixth) =>
            // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
            this.Upcast<IEitherOfSix>()
                .Match(
                    onFirst.ToFunc(),
                    onSecond.ToFunc(),
                    onThird.ToFunc(),
                    onFourth.ToFunc(),
                    onFifth.ToFunc(),
                    onSixth.ToFunc());

        Task<T> IEitherOfSix.MatchAsync<T>(
            Func<object?, Task<T>> onFirst,
            Func<object?, Task<T>> onSecond,
            Func<object?, Task<T>> onThird,
            Func<object?, Task<T>> onFourth,
            Func<object?, Task<T>> onFifth,
            Func<object?, Task<T>> onSixth) =>
            this.Upcast<IEitherOfSix>().Match(onFirst, onSecond, onThird, onFourth, onFifth, onSixth);

        Task IEitherOfSix.MatchAsyncVoid(
            Func<object?, Task> onFirst,
            Func<object?, Task> onSecond,
            Func<object?, Task> onThird,
            Func<object?, Task> onFourth,
            Func<object?, Task> onFifth,
            Func<object?, Task> onSixth) =>
            this.Upcast<IEitherOfSix>()
                .MatchAsync(
                    onFirst.ToAsyncFunc(),
                    onSecond.ToAsyncFunc(),
                    onThird.ToAsyncFunc(),
                    onFourth.ToAsyncFunc(),
                    onFifth.ToAsyncFunc(),
                    onSixth.ToAsyncFunc());

        #endregion

        /// <summary>
        ///     Converts a value marked for the first position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.First{T}" />.</param>
        /// <returns>An either holding that value as its first case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.First{T}" /> usable in place of this type's own
        ///     <c>First</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default first value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3, T4, T5, T6>(Either.EitherFirst<T1>? value) =>
            First(value == null ? default(T1)! : value.Value);

        /// <summary>
        ///     Converts a value marked for the second position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.Second{T}" />.</param>
        /// <returns>An either holding that value as its second case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.Second{T}" /> usable in place of this type's own
        ///     <c>Second</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default second value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3, T4, T5, T6>(Either.EitherSecond<T2>? value) =>
            Second(value == null ? default(T2)! : value.Value);

        /// <summary>
        ///     Converts a value marked for the third position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.Third{T}" />.</param>
        /// <returns>An either holding that value as its third case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.Third{T}" /> usable in place of this type's own
        ///     <c>Third</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default third value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3, T4, T5, T6>(Either.EitherThird<T3>? value) =>
            Third(value == null ? default(T3)! : value.Value);

        /// <summary>
        ///     Converts a value marked for the fourth position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.Fourth{T}" />.</param>
        /// <returns>An either holding that value as its fourth case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.Fourth{T}" /> usable in place of this type's own
        ///     <c>Fourth</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default fourth value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3, T4, T5, T6>(Either.EitherFourth<T4>? value) =>
            Fourth(value == null ? default(T4)! : value.Value);

        /// <summary>
        ///     Converts a value marked for the fifth position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.Fifth{T}" />.</param>
        /// <returns>An either holding that value as its fifth case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.Fifth{T}" /> usable in place of this type's own
        ///     <c>Fifth</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default fifth value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3, T4, T5, T6>(Either.EitherFifth<T5>? value) =>
            Fifth(value == null ? default(T5)! : value.Value);

        /// <summary>
        ///     Converts a value marked for the sixth position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.Sixth{T}" />.</param>
        /// <returns>An either holding that value as its sixth case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.Sixth{T}" /> usable in place of this type's own
        ///     <c>Sixth</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default sixth value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3, T4, T5, T6>(Either.EitherSixth<T6>? value) =>
            Sixth(value == null ? default(T6)! : value.Value);

        /// <summary>
        ///     Determines whether two instances hold the same case with equal values.
        /// </summary>
        /// <param name="x">The first instance.</param>
        /// <param name="y">The second instance.</param>
        /// <returns>
        ///     <see langword="true" /> if both hold the same case and the values they hold are
        ///     equal according to <see cref="EqualityComparer{T}.Default" />.
        /// </returns>
        public static bool operator ==(Either<T1, T2, T3, T4, T5, T6> x, Either<T1, T2, T3, T4, T5, T6> y) =>
            x.valueType == y.valueType
            && EqualityComparer<T1>.Default.Equals(x.value1!, y.value1!)
            && EqualityComparer<T2>.Default.Equals(x.value2!, y.value2!)
            && EqualityComparer<T3>.Default.Equals(x.value3!, y.value3!)
            && EqualityComparer<T4>.Default.Equals(x.value4!, y.value4!)
            && EqualityComparer<T5>.Default.Equals(x.value5!, y.value5!)
            && EqualityComparer<T6>.Default.Equals(x.value6!, y.value6!);

        /// <summary>
        ///     Determines whether two instances differ, by negating <see cref="op_Equality" />.
        /// </summary>
        /// <param name="x">The first instance.</param>
        /// <param name="y">The second instance.</param>
        /// <returns>
        ///     <see langword="true" /> if the two hold different cases, or hold the same case with
        ///     values which are not equal.
        /// </returns>
        public static bool operator !=(Either<T1, T2, T3, T4, T5, T6> x, Either<T1, T2, T3, T4, T5, T6> y) => !(x == y);
        /// <summary>
        ///     Determines whether the given object is an either of this same type which is equal to
        ///     this one.
        /// </summary>
        /// <param name="obj">The object to compare against.</param>
        /// <returns>
        ///     <see langword="true" /> if <paramref name="obj" /> is an either of the same type which
        ///     <see cref="op_Equality" /> considers equal to this one.
        /// </returns>
        /// <remarks>
        ///     An either is never equal to the bare value it holds, only to another either.
        /// </remarks>
        public override bool Equals(object? obj) => obj is Either<T1, T2, T3, T4, T5, T6> e && this == e;

        /// <summary>
        ///     Determines whether the given instance is equal to this one.
        /// </summary>
        /// <param name="other">The instance to compare against.</param>
        /// <returns>
        ///     <see langword="true" /> if <see cref="op_Equality" /> considers the two equal.
        /// </returns>
        /// <remarks>
        ///     The same comparison as <see cref="op_Equality" />, under the name
        ///     <see cref="EqualityComparer{T}.Default" /> looks for, so that comparing these in a
        ///     collection does not box both operands the way <see cref="Equals(object)" /> must.
        /// </remarks>
        public bool Equals(Either<T1, T2, T3, T4, T5, T6> other) => this == other;

        /// <summary>
        ///     Returns a hash code consistent with <see cref="op_Equality" />.
        /// </summary>
        /// <returns>A hash code for this instance.</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = this.valueType;
                hashCode = (hashCode * 397) ^ EqualityComparer<T1>.Default.GetHashCode(this.value1!);
                hashCode = (hashCode * 397) ^ EqualityComparer<T2>.Default.GetHashCode(this.value2!);
                hashCode = (hashCode * 397) ^ EqualityComparer<T3>.Default.GetHashCode(this.value3!);
                hashCode = (hashCode * 397) ^ EqualityComparer<T4>.Default.GetHashCode(this.value4!);
                hashCode = (hashCode * 397) ^ EqualityComparer<T5>.Default.GetHashCode(this.value5!);
                hashCode = (hashCode * 397) ^ EqualityComparer<T6>.Default.GetHashCode(this.value6!);
                return hashCode;
            }
        }

        /// <summary>
        ///     Returns a readable description of this instance, for diagnostics.
        /// </summary>
        /// <returns>The name of the case held, followed by the value it holds.</returns>
        public override string ToString() => this.Match(
            v1 => $"First: {v1}",
            v2 => $"Second: {v2}",
            v3 => $"Third: {v3}",
            v4 => $"Fourth: {v4}",
            v5 => $"Fifth: {v5}",
            v6 => $"Sixth: {v6}");
    }

    /// <summary>
    ///     A value which is exactly one of seven possibilities.
    /// </summary>
    /// <typeparam name="T1">The type of the first possibility.</typeparam>
    /// <typeparam name="T2">The type of the second possibility.</typeparam>
    /// <typeparam name="T3">The type of the third possibility.</typeparam>
    /// <typeparam name="T4">The type of the fourth possibility.</typeparam>
    /// <typeparam name="T5">The type of the fifth possibility.</typeparam>
    /// <typeparam name="T6">The type of the sixth possibility.</typeparam>
    /// <typeparam name="T7">The type of the seventh possibility.</typeparam>
    /// <remarks>
    ///     A discriminated union: the value is one of the seven cases, and which one is part
    ///     of the value rather than something the caller has to track alongside it.
    ///
    ///     There is no property that hands the value out unchecked. Reach it with
    ///     <c>Match</c>, or with one of the helpers built on it, so that every case has to be
    ///     answered for.
    ///
    ///     This is a struct, so <see langword="default" /> is a valid instance; it holds the
    ///     first case, with the default value of <typeparamref name="T1" />.
    /// </remarks>
    public struct Either<T1, T2, T3, T4, T5, T6, T7> : IEitherOfSeven, IEquatable<Either<T1, T2, T3, T4, T5, T6, T7>>
    {
        private readonly int valueType;
        private readonly T1? value1;
        private readonly T2? value2;
        private readonly T3? value3;
        private readonly T4? value4;
        private readonly T5? value5;
        private readonly T6? value6;
        private readonly T7? value7;

        internal Either(int valueType, T1? value1, T2? value2, T3? value3, T4? value4, T5? value5, T6? value6, T7? value7)
        {
            this.valueType = valueType;
            this.value1 = value1;
            this.value2 = value2;
            this.value3 = value3;
            this.value4 = value4;
            this.value5 = value5;
            this.value6 = value6;
            this.value7 = value7;
        }

        #region Type Constructors

        /// <summary>
        ///     Creates an either holding the given value as its first case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its first case.</returns>
        /// <remarks>
        ///     <see cref="Either.First{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3, T4, T5, T6, T7> First(T1 value) => new Either<T1, T2, T3, T4, T5, T6, T7>(
            0,
            value,
            default(T2),
            default(T3),
            default(T4),
            default(T5),
            default(T6),
            default(T7));

        /// <summary>
        ///     Creates an either holding the given value as its second case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its second case.</returns>
        /// <remarks>
        ///     <see cref="Either.Second{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3, T4, T5, T6, T7> Second(T2 value) => new Either<T1, T2, T3, T4, T5, T6, T7>(
            1,
            default(T1),
            value,
            default(T3),
            default(T4),
            default(T5),
            default(T6),
            default(T7));

        /// <summary>
        ///     Creates an either holding the given value as its third case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its third case.</returns>
        /// <remarks>
        ///     <see cref="Either.Third{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3, T4, T5, T6, T7> Third(T3 value) => new Either<T1, T2, T3, T4, T5, T6, T7>(
            2,
            default(T1),
            default(T2),
            value,
            default(T4),
            default(T5),
            default(T6),
            default(T7));

        /// <summary>
        ///     Creates an either holding the given value as its fourth case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its fourth case.</returns>
        /// <remarks>
        ///     <see cref="Either.Fourth{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3, T4, T5, T6, T7> Fourth(T4 value) => new Either<T1, T2, T3, T4, T5, T6, T7>(
            3,
            default(T1),
            default(T2),
            default(T3),
            value,
            default(T5),
            default(T6),
            default(T7));

        /// <summary>
        ///     Creates an either holding the given value as its fifth case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its fifth case.</returns>
        /// <remarks>
        ///     <see cref="Either.Fifth{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3, T4, T5, T6, T7> Fifth(T5 value) => new Either<T1, T2, T3, T4, T5, T6, T7>(
            4,
            default(T1),
            default(T2),
            default(T3),
            default(T4),
            value,
            default(T6),
            default(T7));

        /// <summary>
        ///     Creates an either holding the given value as its sixth case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its sixth case.</returns>
        /// <remarks>
        ///     <see cref="Either.Sixth{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3, T4, T5, T6, T7> Sixth(T6 value) => new Either<T1, T2, T3, T4, T5, T6, T7>(
            5,
            default(T1),
            default(T2),
            default(T3),
            default(T4),
            default(T5),
            value,
            default(T7));

        /// <summary>
        ///     Creates an either holding the given value as its seventh case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its seventh case.</returns>
        /// <remarks>
        ///     <see cref="Either.Seventh{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3, T4, T5, T6, T7> Seventh(T7 value) => new Either<T1, T2, T3, T4, T5, T6, T7>(
            6,
            default(T1),
            default(T2),
            default(T3),
            default(T4),
            default(T5),
            default(T6),
            value);

        #endregion

        #region Base Functionality

        T IEitherOfSeven.Match<T>(
            Func<object?, T> onFirst,
            Func<object?, T> onSecond,
            Func<object?, T> onThird,
            Func<object?, T> onFourth,
            Func<object?, T> onFifth,
            Func<object?, T> onSixth,
            Func<object?, T> onSeventh) =>
            this.Match(
                v => onFirst(v),
                v => onSecond(v),
                v => onThird(v),
                v => onFourth(v),
                v => onFifth(v),
                v => onSixth(v),
                v => onSeventh(v));

        object IEither.GetValueAsObject() => Either.GetValueAs<object>().From(this);

        /// <summary>
        ///     Runs one of the seven functions depending on which case is held, and returns its
        ///     result.
        /// </summary>
        /// <typeparam name="T">The type each of the functions returns.</typeparam>
        /// <param name="onFirst">Run with the value when the first case is held.</param>
        /// <param name="onSecond">Run with the value when the second case is held.</param>
        /// <param name="onThird">Run with the value when the third case is held.</param>
        /// <param name="onFourth">Run with the value when the fourth case is held.</param>
        /// <param name="onFifth">Run with the value when the fifth case is held.</param>
        /// <param name="onSixth">Run with the value when the sixth case is held.</param>
        /// <param name="onSeventh">Run with the value when the seventh case is held.</param>
        /// <returns>Whatever the function that was run returned.</returns>
        /// <remarks>
        ///     This is the only way the held value is reached, and every other member here is
        ///     expressed in terms of it. Exactly one of the functions is called, and it is called
        ///     before this method returns.
        /// </remarks>
        public T Match<T>(
            [JetBrains.Annotations.InstantHandle] Func<T1, T> onFirst,
            [JetBrains.Annotations.InstantHandle] Func<T2, T> onSecond,
            [JetBrains.Annotations.InstantHandle] Func<T3, T> onThird,
            [JetBrains.Annotations.InstantHandle] Func<T4, T> onFourth,
            [JetBrains.Annotations.InstantHandle] Func<T5, T> onFifth,
            [JetBrains.Annotations.InstantHandle] Func<T6, T> onSixth,
            [JetBrains.Annotations.InstantHandle] Func<T7, T> onSeventh) =>
            this.valueType == 0
                ? onFirst(this.value1!)
                : (this.valueType == 1
                    ? onSecond(this.value2!)
                    : (this.valueType == 2
                        ? onThird(this.value3!)
                        : (this.valueType == 3
                            ? onFourth(this.value4!)
                            : (this.valueType == 4
                                ? onFifth(this.value5!)
                                : (this.valueType == 5 ? onSixth(this.value6!) : onSeventh(this.value7!))))));

        #endregion

        #region Helper Methods

        /// <summary>
        ///     Runs one of the seven actions depending on which case is held.
        /// </summary>
        /// <param name="onFirst">Run with the value when the first case is held.</param>
        /// <param name="onSecond">Run with the value when the second case is held.</param>
        /// <param name="onThird">Run with the value when the third case is held.</param>
        /// <param name="onFourth">Run with the value when the fourth case is held.</param>
        /// <param name="onFifth">Run with the value when the fifth case is held.</param>
        /// <param name="onSixth">Run with the value when the sixth case is held.</param>
        /// <param name="onSeventh">Run with the value when the seventh case is held.</param>
        public void MatchVoid(
            [JetBrains.Annotations.InstantHandle] Action<T1> onFirst,
            [JetBrains.Annotations.InstantHandle] Action<T2> onSecond,
            [JetBrains.Annotations.InstantHandle] Action<T3> onThird,
            [JetBrains.Annotations.InstantHandle] Action<T4> onFourth,
            [JetBrains.Annotations.InstantHandle] Action<T5> onFifth,
            [JetBrains.Annotations.InstantHandle] Action<T6> onSixth,
            [JetBrains.Annotations.InstantHandle] Action<T7> onSeventh) =>
            // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
            this.Match(
                onFirst.ToFunc(),
                onSecond.ToFunc(),
                onThird.ToFunc(),
                onFourth.ToFunc(),
                onFifth.ToFunc(),
                onSixth.ToFunc(),
                onSeventh.ToFunc());

        /// <summary>
        ///     Runs one of the seven asynchronous functions depending on which case is held, and
        ///     returns its result.
        /// </summary>
        /// <typeparam name="T">The type each of the functions produces.</typeparam>
        /// <param name="onFirst">Run with the value when the first case is held.</param>
        /// <param name="onSecond">Run with the value when the second case is held.</param>
        /// <param name="onThird">Run with the value when the third case is held.</param>
        /// <param name="onFourth">Run with the value when the fourth case is held.</param>
        /// <param name="onFifth">Run with the value when the fifth case is held.</param>
        /// <param name="onSixth">Run with the value when the sixth case is held.</param>
        /// <param name="onSeventh">Run with the value when the seventh case is held.</param>
        /// <returns>The task returned by whichever function was run.</returns>
        /// <remarks>
        ///     Only the selected function is invoked; the returned task is its task, not a wrapper,
        ///     so failures surface as that task faulting rather than as an exception from this call.
        /// </remarks>
        public Task<T> MatchAsync<T>(
            [JetBrains.Annotations.InstantHandle] Func<T1, Task<T>> onFirst,
            [JetBrains.Annotations.InstantHandle] Func<T2, Task<T>> onSecond,
            [JetBrains.Annotations.InstantHandle] Func<T3, Task<T>> onThird,
            [JetBrains.Annotations.InstantHandle] Func<T4, Task<T>> onFourth,
            [JetBrains.Annotations.InstantHandle] Func<T5, Task<T>> onFifth,
            [JetBrains.Annotations.InstantHandle] Func<T6, Task<T>> onSixth,
            [JetBrains.Annotations.InstantHandle] Func<T7, Task<T>> onSeventh) =>
            this.Match(onFirst, onSecond, onThird, onFourth, onFifth, onSixth, onSeventh);

        /// <summary>
        ///     Runs one of the seven asynchronous actions depending on which case is held.
        /// </summary>
        /// <param name="onFirst">Run with the value when the first case is held.</param>
        /// <param name="onSecond">Run with the value when the second case is held.</param>
        /// <param name="onThird">Run with the value when the third case is held.</param>
        /// <param name="onFourth">Run with the value when the fourth case is held.</param>
        /// <param name="onFifth">Run with the value when the fifth case is held.</param>
        /// <param name="onSixth">Run with the value when the sixth case is held.</param>
        /// <param name="onSeventh">Run with the value when the seventh case is held.</param>
        /// <returns>A task which completes when the selected action has completed.</returns>
        public Task MatchAsyncVoid(
            [JetBrains.Annotations.InstantHandle] Func<T1, Task> onFirst,
            [JetBrains.Annotations.InstantHandle] Func<T2, Task> onSecond,
            [JetBrains.Annotations.InstantHandle] Func<T3, Task> onThird,
            [JetBrains.Annotations.InstantHandle] Func<T4, Task> onFourth,
            [JetBrains.Annotations.InstantHandle] Func<T5, Task> onFifth,
            [JetBrains.Annotations.InstantHandle] Func<T6, Task> onSixth,
            [JetBrains.Annotations.InstantHandle] Func<T7, Task> onSeventh) =>
            this.MatchAsync(
                onFirst.ToAsyncFunc(),
                onSecond.ToAsyncFunc(),
                onThird.ToAsyncFunc(),
                onFourth.ToAsyncFunc(),
                onFifth.ToAsyncFunc(),
                onSixth.ToAsyncFunc(),
                onSeventh.ToAsyncFunc());

        /// <summary>
        ///     Transforms the value if this holds the first case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the first case is transformed into.</typeparam>
        /// <param name="f">The function to transform the first case with.</param>
        /// <returns>
        ///     An either whose first case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the first case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the first case.
        /// </remarks>
        public Either<T, T2, T3, T4, T5, T6, T7> MapFirst<T>([JetBrains.Annotations.InstantHandle] Func<T1, T> f) =>
            this.Match(
                v1 => Either<T, T2, T3, T4, T5, T6, T7>.First(f(v1)),
                v2 => Either.Second(v2),
                v3 => Either.Third(v3),
                v4 => Either.Fourth(v4),
                v5 => Either.Fifth(v5),
                v6 => Either.Sixth(v6),
                v7 => Either.Seventh(v7));

        /// <summary>
        ///     Transforms the value if this holds the second case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the second case is transformed into.</typeparam>
        /// <param name="f">The function to transform the second case with.</param>
        /// <returns>
        ///     An either whose second case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the second case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the second case.
        /// </remarks>
        public Either<T1, T, T3, T4, T5, T6, T7> MapSecond<T>([JetBrains.Annotations.InstantHandle] Func<T2, T> f) =>
            this.Match(
                Either<T1, T, T3, T4, T5, T6, T7>.First,
                v2 => Either.Second(f(v2)),
                v3 => Either.Third(v3),
                v4 => Either.Fourth(v4),
                v5 => Either.Fifth(v5),
                v6 => Either.Sixth(v6),
                v7 => Either.Seventh(v7));

        /// <summary>
        ///     Transforms the value if this holds the third case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the third case is transformed into.</typeparam>
        /// <param name="f">The function to transform the third case with.</param>
        /// <returns>
        ///     An either whose third case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the third case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the third case.
        /// </remarks>
        public Either<T1, T2, T, T4, T5, T6, T7> MapThird<T>([JetBrains.Annotations.InstantHandle] Func<T3, T> f) =>
            this.Match(
                Either<T1, T2, T, T4, T5, T6, T7>.First,
                v2 => Either.Second(v2),
                v3 => Either.Third(f(v3)),
                v4 => Either.Fourth(v4),
                v5 => Either.Fifth(v5),
                v6 => Either.Sixth(v6),
                v7 => Either.Seventh(v7));

        /// <summary>
        ///     Transforms the value if this holds the fourth case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the fourth case is transformed into.</typeparam>
        /// <param name="f">The function to transform the fourth case with.</param>
        /// <returns>
        ///     An either whose fourth case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the fourth case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the fourth case.
        /// </remarks>
        public Either<T1, T2, T3, T, T5, T6, T7> MapFourth<T>([JetBrains.Annotations.InstantHandle] Func<T4, T> f) =>
            this.Match(
                Either<T1, T2, T3, T, T5, T6, T7>.First,
                v2 => Either.Second(v2),
                v3 => Either.Third(v3),
                v4 => Either.Fourth(f(v4)),
                v5 => Either.Fifth(v5),
                v6 => Either.Sixth(v6),
                v7 => Either.Seventh(v7));

        /// <summary>
        ///     Transforms the value if this holds the fifth case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the fifth case is transformed into.</typeparam>
        /// <param name="f">The function to transform the fifth case with.</param>
        /// <returns>
        ///     An either whose fifth case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the fifth case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the fifth case.
        /// </remarks>
        public Either<T1, T2, T3, T4, T, T6, T7> MapFifth<T>([JetBrains.Annotations.InstantHandle] Func<T5, T> f) =>
            this.Match(
                Either<T1, T2, T3, T4, T, T6, T7>.First,
                v2 => Either.Second(v2),
                v3 => Either.Third(v3),
                v4 => Either.Fourth(v4),
                v5 => Either.Fifth(f(v5)),
                v6 => Either.Sixth(v6),
                v7 => Either.Seventh(v7));

        /// <summary>
        ///     Transforms the value if this holds the sixth case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the sixth case is transformed into.</typeparam>
        /// <param name="f">The function to transform the sixth case with.</param>
        /// <returns>
        ///     An either whose sixth case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the sixth case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the sixth case.
        /// </remarks>
        public Either<T1, T2, T3, T4, T5, T, T7> MapSixth<T>([JetBrains.Annotations.InstantHandle] Func<T6, T> f) =>
            this.Match(
                Either<T1, T2, T3, T4, T5, T, T7>.First,
                v2 => Either.Second(v2),
                v3 => Either.Third(v3),
                v4 => Either.Fourth(v4),
                v5 => Either.Fifth(v5),
                v6 => Either.Sixth(f(v6)),
                v7 => Either.Seventh(v7));

        /// <summary>
        ///     Transforms the value if this holds the seventh case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the seventh case is transformed into.</typeparam>
        /// <param name="f">The function to transform the seventh case with.</param>
        /// <returns>
        ///     An either whose seventh case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the seventh case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the seventh case.
        /// </remarks>
        public Either<T1, T2, T3, T4, T5, T6, T> MapSeventh<T>([JetBrains.Annotations.InstantHandle] Func<T7, T> f) =>
            this.Match(
                Either<T1, T2, T3, T4, T5, T6, T>.First,
                v2 => Either.Second(v2),
                v3 => Either.Third(v3),
                v4 => Either.Fourth(v4),
                v5 => Either.Fifth(v5),
                v6 => Either.Sixth(v6),
                v7 => Either.Seventh(f(v7)));

        /// <summary>
        ///     Gets the held value if this holds the first case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the first case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T1> TryGetFirst() =>
            this.Match(
                Maybe.Some,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None);

        /// <summary>
        ///     Gets the held value if this holds the second case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the second case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T2> TryGetSecond() =>
            this.Match(
                _ => Maybe.None,
                Maybe.Some,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None);

        /// <summary>
        ///     Gets the held value if this holds the third case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the third case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T3> TryGetThird() =>
            this.Match(
                _ => Maybe.None,
                _ => Maybe.None,
                Maybe.Some,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None);

        /// <summary>
        ///     Gets the held value if this holds the fourth case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the fourth case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T4> TryGetFourth() =>
            this.Match(
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                Maybe.Some,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None);

        /// <summary>
        ///     Gets the held value if this holds the fifth case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the fifth case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T5> TryGetFifth() =>
            this.Match(
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                Maybe.Some,
                _ => Maybe.None,
                _ => Maybe.None);

        /// <summary>
        ///     Gets the held value if this holds the sixth case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the sixth case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T6> TryGetSixth() =>
            this.Match(
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                Maybe.Some,
                _ => Maybe.None);

        /// <summary>
        ///     Gets the held value if this holds the seventh case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the seventh case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T7> TryGetSeventh() =>
            this.Match(
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                Maybe.Some);

        /// <summary>
        ///     Returns whether this holds the first case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the first case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetFirst" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsFirst() =>
            this.Match(_ => true, _ => false, _ => false, _ => false, _ => false, _ => false, _ => false);

        /// <summary>
        ///     Returns whether this holds the second case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the second case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetSecond" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsSecond() =>
            this.Match(_ => false, _ => true, _ => false, _ => false, _ => false, _ => false, _ => false);

        /// <summary>
        ///     Returns whether this holds the third case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the third case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetThird" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsThird() =>
            this.Match(_ => false, _ => false, _ => true, _ => false, _ => false, _ => false, _ => false);

        /// <summary>
        ///     Returns whether this holds the fourth case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the fourth case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetFourth" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsFourth() =>
            this.Match(_ => false, _ => false, _ => false, _ => true, _ => false, _ => false, _ => false);

        /// <summary>
        ///     Returns whether this holds the fifth case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the fifth case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetFifth" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsFifth() =>
            this.Match(_ => false, _ => false, _ => false, _ => false, _ => true, _ => false, _ => false);

        /// <summary>
        ///     Returns whether this holds the sixth case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the sixth case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetSixth" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsSixth() =>
            this.Match(_ => false, _ => false, _ => false, _ => false, _ => false, _ => true, _ => false);

        /// <summary>
        ///     Returns whether this holds the seventh case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the seventh case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetSeventh" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsSeventh() =>
            this.Match(_ => false, _ => false, _ => false, _ => false, _ => false, _ => false, _ => true);

        void IEitherOfSeven.MatchVoid(
            Action<object?> onFirst,
            Action<object?> onSecond,
            Action<object?> onThird,
            Action<object?> onFourth,
            Action<object?> onFifth,
            Action<object?> onSixth,
            Action<object?> onSeventh) =>
            // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
            this.Upcast<IEitherOfSeven>()
                .Match(
                    onFirst.ToFunc(),
                    onSecond.ToFunc(),
                    onThird.ToFunc(),
                    onFourth.ToFunc(),
                    onFifth.ToFunc(),
                    onSixth.ToFunc(),
                    onSeventh.ToFunc());

        Task<T> IEitherOfSeven.MatchAsync<T>(
            Func<object?, Task<T>> onFirst,
            Func<object?, Task<T>> onSecond,
            Func<object?, Task<T>> onThird,
            Func<object?, Task<T>> onFourth,
            Func<object?, Task<T>> onFifth,
            Func<object?, Task<T>> onSixth,
            Func<object?, Task<T>> onSeventh) =>
            this.Upcast<IEitherOfSeven>().Match(onFirst, onSecond, onThird, onFourth, onFifth, onSixth, onSeventh);

        Task IEitherOfSeven.MatchAsyncVoid(
            Func<object?, Task> onFirst,
            Func<object?, Task> onSecond,
            Func<object?, Task> onThird,
            Func<object?, Task> onFourth,
            Func<object?, Task> onFifth,
            Func<object?, Task> onSixth,
            Func<object?, Task> onSeventh) =>
            this.Upcast<IEitherOfSeven>()
                .MatchAsync(
                    onFirst.ToAsyncFunc(),
                    onSecond.ToAsyncFunc(),
                    onThird.ToAsyncFunc(),
                    onFourth.ToAsyncFunc(),
                    onFifth.ToAsyncFunc(),
                    onSixth.ToAsyncFunc(),
                    onSeventh.ToAsyncFunc());

        #endregion

        /// <summary>
        ///     Converts a value marked for the first position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.First{T}" />.</param>
        /// <returns>An either holding that value as its first case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.First{T}" /> usable in place of this type's own
        ///     <c>First</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default first value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3, T4, T5, T6, T7>(Either.EitherFirst<T1>? value) =>
            First(value == null ? default(T1)! : value.Value);

        /// <summary>
        ///     Converts a value marked for the second position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.Second{T}" />.</param>
        /// <returns>An either holding that value as its second case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.Second{T}" /> usable in place of this type's own
        ///     <c>Second</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default second value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3, T4, T5, T6, T7>(Either.EitherSecond<T2>? value) =>
            Second(value == null ? default(T2)! : value.Value);

        /// <summary>
        ///     Converts a value marked for the third position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.Third{T}" />.</param>
        /// <returns>An either holding that value as its third case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.Third{T}" /> usable in place of this type's own
        ///     <c>Third</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default third value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3, T4, T5, T6, T7>(Either.EitherThird<T3>? value) =>
            Third(value == null ? default(T3)! : value.Value);

        /// <summary>
        ///     Converts a value marked for the fourth position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.Fourth{T}" />.</param>
        /// <returns>An either holding that value as its fourth case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.Fourth{T}" /> usable in place of this type's own
        ///     <c>Fourth</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default fourth value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3, T4, T5, T6, T7>(Either.EitherFourth<T4>? value) =>
            Fourth(value == null ? default(T4)! : value.Value);

        /// <summary>
        ///     Converts a value marked for the fifth position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.Fifth{T}" />.</param>
        /// <returns>An either holding that value as its fifth case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.Fifth{T}" /> usable in place of this type's own
        ///     <c>Fifth</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default fifth value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3, T4, T5, T6, T7>(Either.EitherFifth<T5>? value) =>
            Fifth(value == null ? default(T5)! : value.Value);

        /// <summary>
        ///     Converts a value marked for the sixth position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.Sixth{T}" />.</param>
        /// <returns>An either holding that value as its sixth case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.Sixth{T}" /> usable in place of this type's own
        ///     <c>Sixth</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default sixth value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3, T4, T5, T6, T7>(Either.EitherSixth<T6>? value) =>
            Sixth(value == null ? default(T6)! : value.Value);

        /// <summary>
        ///     Converts a value marked for the seventh position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.Seventh{T}" />.</param>
        /// <returns>An either holding that value as its seventh case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.Seventh{T}" /> usable in place of this type's own
        ///     <c>Seventh</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default seventh value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3, T4, T5, T6, T7>(Either.EitherSeventh<T7>? value) =>
            Seventh(value == null ? default(T7)! : value.Value);

        /// <summary>
        ///     Determines whether two instances hold the same case with equal values.
        /// </summary>
        /// <param name="x">The first instance.</param>
        /// <param name="y">The second instance.</param>
        /// <returns>
        ///     <see langword="true" /> if both hold the same case and the values they hold are
        ///     equal according to <see cref="EqualityComparer{T}.Default" />.
        /// </returns>
        public static bool operator ==(Either<T1, T2, T3, T4, T5, T6, T7> x, Either<T1, T2, T3, T4, T5, T6, T7> y) =>
            x.valueType == y.valueType
            && EqualityComparer<T1>.Default.Equals(x.value1!, y.value1!)
            && EqualityComparer<T2>.Default.Equals(x.value2!, y.value2!)
            && EqualityComparer<T3>.Default.Equals(x.value3!, y.value3!)
            && EqualityComparer<T4>.Default.Equals(x.value4!, y.value4!)
            && EqualityComparer<T5>.Default.Equals(x.value5!, y.value5!)
            && EqualityComparer<T6>.Default.Equals(x.value6!, y.value6!)
            && EqualityComparer<T7>.Default.Equals(x.value7!, y.value7!);

        /// <summary>
        ///     Determines whether two instances differ, by negating <see cref="op_Equality" />.
        /// </summary>
        /// <param name="x">The first instance.</param>
        /// <param name="y">The second instance.</param>
        /// <returns>
        ///     <see langword="true" /> if the two hold different cases, or hold the same case with
        ///     values which are not equal.
        /// </returns>
        public static bool operator !=(Either<T1, T2, T3, T4, T5, T6, T7> x, Either<T1, T2, T3, T4, T5, T6, T7> y) =>
            !(x == y);

        /// <summary>
        ///     Determines whether the given object is an either of this same type which is equal to
        ///     this one.
        /// </summary>
        /// <param name="obj">The object to compare against.</param>
        /// <returns>
        ///     <see langword="true" /> if <paramref name="obj" /> is an either of the same type which
        ///     <see cref="op_Equality" /> considers equal to this one.
        /// </returns>
        /// <remarks>
        ///     An either is never equal to the bare value it holds, only to another either.
        /// </remarks>
        public override bool Equals(object? obj) => obj is Either<T1, T2, T3, T4, T5, T6, T7> e && this == e;

        /// <summary>
        ///     Determines whether the given instance is equal to this one.
        /// </summary>
        /// <param name="other">The instance to compare against.</param>
        /// <returns>
        ///     <see langword="true" /> if <see cref="op_Equality" /> considers the two equal.
        /// </returns>
        /// <remarks>
        ///     The same comparison as <see cref="op_Equality" />, under the name
        ///     <see cref="EqualityComparer{T}.Default" /> looks for, so that comparing these in a
        ///     collection does not box both operands the way <see cref="Equals(object)" /> must.
        /// </remarks>
        public bool Equals(Either<T1, T2, T3, T4, T5, T6, T7> other) => this == other;

        /// <summary>
        ///     Returns a hash code consistent with <see cref="op_Equality" />.
        /// </summary>
        /// <returns>A hash code for this instance.</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = this.valueType;
                hashCode = (hashCode * 397) ^ EqualityComparer<T1>.Default.GetHashCode(this.value1!);
                hashCode = (hashCode * 397) ^ EqualityComparer<T2>.Default.GetHashCode(this.value2!);
                hashCode = (hashCode * 397) ^ EqualityComparer<T3>.Default.GetHashCode(this.value3!);
                hashCode = (hashCode * 397) ^ EqualityComparer<T4>.Default.GetHashCode(this.value4!);
                hashCode = (hashCode * 397) ^ EqualityComparer<T5>.Default.GetHashCode(this.value5!);
                hashCode = (hashCode * 397) ^ EqualityComparer<T6>.Default.GetHashCode(this.value6!);
                hashCode = (hashCode * 397) ^ EqualityComparer<T7>.Default.GetHashCode(this.value7!);
                return hashCode;
            }
        }

        /// <summary>
        ///     Returns a readable description of this instance, for diagnostics.
        /// </summary>
        /// <returns>The name of the case held, followed by the value it holds.</returns>
        public override string ToString() => this.Match(
            v1 => $"First: {v1}",
            v2 => $"Second: {v2}",
            v3 => $"Third: {v3}",
            v4 => $"Fourth: {v4}",
            v5 => $"Fifth: {v5}",
            v6 => $"Sixth: {v6}",
            v7 => $"Seventh: {v7}");
    }

    /// <summary>
    ///     A value which is exactly one of eight possibilities.
    /// </summary>
    /// <typeparam name="T1">The type of the first possibility.</typeparam>
    /// <typeparam name="T2">The type of the second possibility.</typeparam>
    /// <typeparam name="T3">The type of the third possibility.</typeparam>
    /// <typeparam name="T4">The type of the fourth possibility.</typeparam>
    /// <typeparam name="T5">The type of the fifth possibility.</typeparam>
    /// <typeparam name="T6">The type of the sixth possibility.</typeparam>
    /// <typeparam name="T7">The type of the seventh possibility.</typeparam>
    /// <typeparam name="T8">The type of the eighth possibility.</typeparam>
    /// <remarks>
    ///     A discriminated union: the value is one of the eight cases, and which one is part
    ///     of the value rather than something the caller has to track alongside it.
    ///
    ///     There is no property that hands the value out unchecked. Reach it with
    ///     <c>Match</c>, or with one of the helpers built on it, so that every case has to be
    ///     answered for.
    ///
    ///     This is a struct, so <see langword="default" /> is a valid instance; it holds the
    ///     first case, with the default value of <typeparamref name="T1" />.
    /// </remarks>
    public struct Either<T1, T2, T3, T4, T5, T6, T7, T8> : IEitherOfEight, IEquatable<Either<T1, T2, T3, T4, T5, T6, T7, T8>>
    {
        private readonly int valueType;
        private readonly T1? value1;
        private readonly T2? value2;
        private readonly T3? value3;
        private readonly T4? value4;
        private readonly T5? value5;
        private readonly T6? value6;
        private readonly T7? value7;
        private readonly T8? value8;

        internal Either(
            int valueType,
            T1? value1,
            T2? value2,
            T3? value3,
            T4? value4,
            T5? value5,
            T6? value6,
            T7? value7,
            T8? value8)
        {
            this.valueType = valueType;
            this.value1 = value1;
            this.value2 = value2;
            this.value3 = value3;
            this.value4 = value4;
            this.value5 = value5;
            this.value6 = value6;
            this.value7 = value7;
            this.value8 = value8;
        }

        #region Type Constructors

        /// <summary>
        ///     Creates an either holding the given value as its first case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its first case.</returns>
        /// <remarks>
        ///     <see cref="Either.First{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3, T4, T5, T6, T7, T8> First(T1 value) =>
            new Either<T1, T2, T3, T4, T5, T6, T7, T8>(
                0,
                value,
                default(T2),
                default(T3),
                default(T4),
                default(T5),
                default(T6),
                default(T7),
                default(T8));

        /// <summary>
        ///     Creates an either holding the given value as its second case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its second case.</returns>
        /// <remarks>
        ///     <see cref="Either.Second{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3, T4, T5, T6, T7, T8> Second(T2 value) =>
            new Either<T1, T2, T3, T4, T5, T6, T7, T8>(
                1,
                default(T1),
                value,
                default(T3),
                default(T4),
                default(T5),
                default(T6),
                default(T7),
                default(T8));

        /// <summary>
        ///     Creates an either holding the given value as its third case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its third case.</returns>
        /// <remarks>
        ///     <see cref="Either.Third{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3, T4, T5, T6, T7, T8> Third(T3 value) =>
            new Either<T1, T2, T3, T4, T5, T6, T7, T8>(
                2,
                default(T1),
                default(T2),
                value,
                default(T4),
                default(T5),
                default(T6),
                default(T7),
                default(T8));

        /// <summary>
        ///     Creates an either holding the given value as its fourth case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its fourth case.</returns>
        /// <remarks>
        ///     <see cref="Either.Fourth{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3, T4, T5, T6, T7, T8> Fourth(T4 value) =>
            new Either<T1, T2, T3, T4, T5, T6, T7, T8>(
                3,
                default(T1),
                default(T2),
                default(T3),
                value,
                default(T5),
                default(T6),
                default(T7),
                default(T8));

        /// <summary>
        ///     Creates an either holding the given value as its fifth case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its fifth case.</returns>
        /// <remarks>
        ///     <see cref="Either.Fifth{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3, T4, T5, T6, T7, T8> Fifth(T5 value) =>
            new Either<T1, T2, T3, T4, T5, T6, T7, T8>(
                4,
                default(T1),
                default(T2),
                default(T3),
                default(T4),
                value,
                default(T6),
                default(T7),
                default(T8));

        /// <summary>
        ///     Creates an either holding the given value as its sixth case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its sixth case.</returns>
        /// <remarks>
        ///     <see cref="Either.Sixth{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3, T4, T5, T6, T7, T8> Sixth(T6 value) =>
            new Either<T1, T2, T3, T4, T5, T6, T7, T8>(
                5,
                default(T1),
                default(T2),
                default(T3),
                default(T4),
                default(T5),
                value,
                default(T7),
                default(T8));

        /// <summary>
        ///     Creates an either holding the given value as its seventh case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its seventh case.</returns>
        /// <remarks>
        ///     <see cref="Either.Seventh{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3, T4, T5, T6, T7, T8> Seventh(T7 value) =>
            new Either<T1, T2, T3, T4, T5, T6, T7, T8>(
                6,
                default(T1),
                default(T2),
                default(T3),
                default(T4),
                default(T5),
                default(T6),
                value,
                default(T8));

        /// <summary>
        ///     Creates an either holding the given value as its eighth case.
        /// </summary>
        /// <param name="value">The value to hold.</param>
        /// <returns>An either holding <paramref name="value" /> as its eighth case.</returns>
        /// <remarks>
        ///     <see cref="Either.Eighth{T}" /> is usually more convenient, since it leaves the
        ///     remaining type arguments to be supplied by the context.
        /// </remarks>
        public static Either<T1, T2, T3, T4, T5, T6, T7, T8> Eighth(T8 value) =>
            new Either<T1, T2, T3, T4, T5, T6, T7, T8>(
                7,
                default(T1),
                default(T2),
                default(T3),
                default(T4),
                default(T5),
                default(T6),
                default(T7),
                value);

        #endregion

        #region Base Functionality

        T IEitherOfEight.Match<T>(
            Func<object?, T> onFirst,
            Func<object?, T> onSecond,
            Func<object?, T> onThird,
            Func<object?, T> onFourth,
            Func<object?, T> onFifth,
            Func<object?, T> onSixth,
            Func<object?, T> onSeventh,
            Func<object?, T> onEighth) =>
            this.Match(
                v => onFirst(v),
                v => onSecond(v),
                v => onThird(v),
                v => onFourth(v),
                v => onFifth(v),
                v => onSixth(v),
                v => onSeventh(v),
                v => onEighth(v));

        object IEither.GetValueAsObject() => Either.GetValueAs<object>().From(this);

        /// <summary>
        ///     Runs one of the eight functions depending on which case is held, and returns its
        ///     result.
        /// </summary>
        /// <typeparam name="T">The type each of the functions returns.</typeparam>
        /// <param name="onFirst">Run with the value when the first case is held.</param>
        /// <param name="onSecond">Run with the value when the second case is held.</param>
        /// <param name="onThird">Run with the value when the third case is held.</param>
        /// <param name="onFourth">Run with the value when the fourth case is held.</param>
        /// <param name="onFifth">Run with the value when the fifth case is held.</param>
        /// <param name="onSixth">Run with the value when the sixth case is held.</param>
        /// <param name="onSeventh">Run with the value when the seventh case is held.</param>
        /// <param name="onEighth">Run with the value when the eighth case is held.</param>
        /// <returns>Whatever the function that was run returned.</returns>
        /// <remarks>
        ///     This is the only way the held value is reached, and every other member here is
        ///     expressed in terms of it. Exactly one of the functions is called, and it is called
        ///     before this method returns.
        /// </remarks>
        public T Match<T>(
            [JetBrains.Annotations.InstantHandle] Func<T1, T> onFirst,
            [JetBrains.Annotations.InstantHandle] Func<T2, T> onSecond,
            [JetBrains.Annotations.InstantHandle] Func<T3, T> onThird,
            [JetBrains.Annotations.InstantHandle] Func<T4, T> onFourth,
            [JetBrains.Annotations.InstantHandle] Func<T5, T> onFifth,
            [JetBrains.Annotations.InstantHandle] Func<T6, T> onSixth,
            [JetBrains.Annotations.InstantHandle] Func<T7, T> onSeventh,
            [JetBrains.Annotations.InstantHandle] Func<T8, T> onEighth) =>
            this.valueType == 0
                ? onFirst(this.value1!)
                : (this.valueType == 1
                    ? onSecond(this.value2!)
                    : (this.valueType == 2
                        ? onThird(this.value3!)
                        : (this.valueType == 3
                            ? onFourth(this.value4!)
                            : (this.valueType == 4
                                ? onFifth(this.value5!)
                                : (this.valueType == 5
                                    ? onSixth(this.value6!)
                                    : (this.valueType == 6 ? onSeventh(this.value7!) : onEighth(this.value8!)))))));

        #endregion

        #region Helper Methods

        /// <summary>
        ///     Runs one of the eight actions depending on which case is held.
        /// </summary>
        /// <param name="onFirst">Run with the value when the first case is held.</param>
        /// <param name="onSecond">Run with the value when the second case is held.</param>
        /// <param name="onThird">Run with the value when the third case is held.</param>
        /// <param name="onFourth">Run with the value when the fourth case is held.</param>
        /// <param name="onFifth">Run with the value when the fifth case is held.</param>
        /// <param name="onSixth">Run with the value when the sixth case is held.</param>
        /// <param name="onSeventh">Run with the value when the seventh case is held.</param>
        /// <param name="onEighth">Run with the value when the eighth case is held.</param>
        public void MatchVoid(
            [JetBrains.Annotations.InstantHandle] Action<T1> onFirst,
            [JetBrains.Annotations.InstantHandle] Action<T2> onSecond,
            [JetBrains.Annotations.InstantHandle] Action<T3> onThird,
            [JetBrains.Annotations.InstantHandle] Action<T4> onFourth,
            [JetBrains.Annotations.InstantHandle] Action<T5> onFifth,
            [JetBrains.Annotations.InstantHandle] Action<T6> onSixth,
            [JetBrains.Annotations.InstantHandle] Action<T7> onSeventh,
            [JetBrains.Annotations.InstantHandle] Action<T8> onEighth) =>
            // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
            this.Match(
                onFirst.ToFunc(),
                onSecond.ToFunc(),
                onThird.ToFunc(),
                onFourth.ToFunc(),
                onFifth.ToFunc(),
                onSixth.ToFunc(),
                onSeventh.ToFunc(),
                onEighth.ToFunc());

        /// <summary>
        ///     Runs one of the eight asynchronous functions depending on which case is held, and
        ///     returns its result.
        /// </summary>
        /// <typeparam name="T">The type each of the functions produces.</typeparam>
        /// <param name="onFirst">Run with the value when the first case is held.</param>
        /// <param name="onSecond">Run with the value when the second case is held.</param>
        /// <param name="onThird">Run with the value when the third case is held.</param>
        /// <param name="onFourth">Run with the value when the fourth case is held.</param>
        /// <param name="onFifth">Run with the value when the fifth case is held.</param>
        /// <param name="onSixth">Run with the value when the sixth case is held.</param>
        /// <param name="onSeventh">Run with the value when the seventh case is held.</param>
        /// <param name="onEighth">Run with the value when the eighth case is held.</param>
        /// <returns>The task returned by whichever function was run.</returns>
        /// <remarks>
        ///     Only the selected function is invoked; the returned task is its task, not a wrapper,
        ///     so failures surface as that task faulting rather than as an exception from this call.
        /// </remarks>
        public Task<T> MatchAsync<T>(
            [JetBrains.Annotations.InstantHandle] Func<T1, Task<T>> onFirst,
            [JetBrains.Annotations.InstantHandle] Func<T2, Task<T>> onSecond,
            [JetBrains.Annotations.InstantHandle] Func<T3, Task<T>> onThird,
            [JetBrains.Annotations.InstantHandle] Func<T4, Task<T>> onFourth,
            [JetBrains.Annotations.InstantHandle] Func<T5, Task<T>> onFifth,
            [JetBrains.Annotations.InstantHandle] Func<T6, Task<T>> onSixth,
            [JetBrains.Annotations.InstantHandle] Func<T7, Task<T>> onSeventh,
            [JetBrains.Annotations.InstantHandle] Func<T8, Task<T>> onEighth) =>
            this.Match(onFirst, onSecond, onThird, onFourth, onFifth, onSixth, onSeventh, onEighth);

        /// <summary>
        ///     Runs one of the eight asynchronous actions depending on which case is held.
        /// </summary>
        /// <param name="onFirst">Run with the value when the first case is held.</param>
        /// <param name="onSecond">Run with the value when the second case is held.</param>
        /// <param name="onThird">Run with the value when the third case is held.</param>
        /// <param name="onFourth">Run with the value when the fourth case is held.</param>
        /// <param name="onFifth">Run with the value when the fifth case is held.</param>
        /// <param name="onSixth">Run with the value when the sixth case is held.</param>
        /// <param name="onSeventh">Run with the value when the seventh case is held.</param>
        /// <param name="onEighth">Run with the value when the eighth case is held.</param>
        /// <returns>A task which completes when the selected action has completed.</returns>
        public Task MatchAsyncVoid(
            [JetBrains.Annotations.InstantHandle] Func<T1, Task> onFirst,
            [JetBrains.Annotations.InstantHandle] Func<T2, Task> onSecond,
            [JetBrains.Annotations.InstantHandle] Func<T3, Task> onThird,
            [JetBrains.Annotations.InstantHandle] Func<T4, Task> onFourth,
            [JetBrains.Annotations.InstantHandle] Func<T5, Task> onFifth,
            [JetBrains.Annotations.InstantHandle] Func<T6, Task> onSixth,
            [JetBrains.Annotations.InstantHandle] Func<T7, Task> onSeventh,
            [JetBrains.Annotations.InstantHandle] Func<T8, Task> onEighth) =>
            this.MatchAsync(
                onFirst.ToAsyncFunc(),
                onSecond.ToAsyncFunc(),
                onThird.ToAsyncFunc(),
                onFourth.ToAsyncFunc(),
                onFifth.ToAsyncFunc(),
                onSixth.ToAsyncFunc(),
                onSeventh.ToAsyncFunc(),
                onEighth.ToAsyncFunc());

        /// <summary>
        ///     Transforms the value if this holds the first case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the first case is transformed into.</typeparam>
        /// <param name="f">The function to transform the first case with.</param>
        /// <returns>
        ///     An either whose first case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the first case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the first case.
        /// </remarks>
        public Either<T, T2, T3, T4, T5, T6, T7, T8> MapFirst<T>([JetBrains.Annotations.InstantHandle] Func<T1, T> f) =>
            this.Match(
                v1 => Either<T, T2, T3, T4, T5, T6, T7, T8>.First(f(v1)),
                v2 => Either.Second(v2),
                v3 => Either.Third(v3),
                v4 => Either.Fourth(v4),
                v5 => Either.Fifth(v5),
                v6 => Either.Sixth(v6),
                v7 => Either.Seventh(v7),
                v8 => Either.Eighth(v8));

        /// <summary>
        ///     Transforms the value if this holds the second case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the second case is transformed into.</typeparam>
        /// <param name="f">The function to transform the second case with.</param>
        /// <returns>
        ///     An either whose second case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the second case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the second case.
        /// </remarks>
        public Either<T1, T, T3, T4, T5, T6, T7, T8> MapSecond<T>([JetBrains.Annotations.InstantHandle] Func<T2, T> f) =>
            this.Match(
                Either<T1, T, T3, T4, T5, T6, T7, T8>.First,
                v2 => Either.Second(f(v2)),
                v3 => Either.Third(v3),
                v4 => Either.Fourth(v4),
                v5 => Either.Fifth(v5),
                v6 => Either.Sixth(v6),
                v7 => Either.Seventh(v7),
                v8 => Either.Eighth(v8));

        /// <summary>
        ///     Transforms the value if this holds the third case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the third case is transformed into.</typeparam>
        /// <param name="f">The function to transform the third case with.</param>
        /// <returns>
        ///     An either whose third case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the third case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the third case.
        /// </remarks>
        public Either<T1, T2, T, T4, T5, T6, T7, T8> MapThird<T>([JetBrains.Annotations.InstantHandle] Func<T3, T> f) =>
            this.Match(
                Either<T1, T2, T, T4, T5, T6, T7, T8>.First,
                v2 => Either.Second(v2),
                v3 => Either.Third(f(v3)),
                v4 => Either.Fourth(v4),
                v5 => Either.Fifth(v5),
                v6 => Either.Sixth(v6),
                v7 => Either.Seventh(v7),
                v8 => Either.Eighth(v8));

        /// <summary>
        ///     Transforms the value if this holds the fourth case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the fourth case is transformed into.</typeparam>
        /// <param name="f">The function to transform the fourth case with.</param>
        /// <returns>
        ///     An either whose fourth case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the fourth case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the fourth case.
        /// </remarks>
        public Either<T1, T2, T3, T, T5, T6, T7, T8> MapFourth<T>([JetBrains.Annotations.InstantHandle] Func<T4, T> f) =>
            this.Match(
                Either<T1, T2, T3, T, T5, T6, T7, T8>.First,
                v2 => Either.Second(v2),
                v3 => Either.Third(v3),
                v4 => Either.Fourth(f(v4)),
                v5 => Either.Fifth(v5),
                v6 => Either.Sixth(v6),
                v7 => Either.Seventh(v7),
                v8 => Either.Eighth(v8));

        /// <summary>
        ///     Transforms the value if this holds the fifth case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the fifth case is transformed into.</typeparam>
        /// <param name="f">The function to transform the fifth case with.</param>
        /// <returns>
        ///     An either whose fifth case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the fifth case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the fifth case.
        /// </remarks>
        public Either<T1, T2, T3, T4, T, T6, T7, T8> MapFifth<T>([JetBrains.Annotations.InstantHandle] Func<T5, T> f) =>
            this.Match(
                Either<T1, T2, T3, T4, T, T6, T7, T8>.First,
                v2 => Either.Second(v2),
                v3 => Either.Third(v3),
                v4 => Either.Fourth(v4),
                v5 => Either.Fifth(f(v5)),
                v6 => Either.Sixth(v6),
                v7 => Either.Seventh(v7),
                v8 => Either.Eighth(v8));

        /// <summary>
        ///     Transforms the value if this holds the sixth case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the sixth case is transformed into.</typeparam>
        /// <param name="f">The function to transform the sixth case with.</param>
        /// <returns>
        ///     An either whose sixth case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the sixth case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the sixth case.
        /// </remarks>
        public Either<T1, T2, T3, T4, T5, T, T7, T8> MapSixth<T>([JetBrains.Annotations.InstantHandle] Func<T6, T> f) =>
            this.Match(
                Either<T1, T2, T3, T4, T5, T, T7, T8>.First,
                v2 => Either.Second(v2),
                v3 => Either.Third(v3),
                v4 => Either.Fourth(v4),
                v5 => Either.Fifth(v5),
                v6 => Either.Sixth(f(v6)),
                v7 => Either.Seventh(v7),
                v8 => Either.Eighth(v8));

        /// <summary>
        ///     Transforms the value if this holds the seventh case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the seventh case is transformed into.</typeparam>
        /// <param name="f">The function to transform the seventh case with.</param>
        /// <returns>
        ///     An either whose seventh case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the seventh case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the seventh case.
        /// </remarks>
        public Either<T1, T2, T3, T4, T5, T6, T, T8> MapSeventh<T>([JetBrains.Annotations.InstantHandle] Func<T7, T> f) =>
            this.Match(
                Either<T1, T2, T3, T4, T5, T6, T, T8>.First,
                v2 => Either.Second(v2),
                v3 => Either.Third(v3),
                v4 => Either.Fourth(v4),
                v5 => Either.Fifth(v5),
                v6 => Either.Sixth(v6),
                v7 => Either.Seventh(f(v7)),
                v8 => Either.Eighth(v8));

        /// <summary>
        ///     Transforms the value if this holds the eighth case, and passes every other case
        ///     through unchanged.
        /// </summary>
        /// <typeparam name="T">The type the eighth case is transformed into.</typeparam>
        /// <param name="f">The function to transform the eighth case with.</param>
        /// <returns>
        ///     An either whose eighth case has type <typeparamref name="T" />, holding the result of
        ///     <paramref name="f" /> if this held the eighth case, and otherwise the same case and
        ///     value as this.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is called only when this holds the eighth case.
        /// </remarks>
        public Either<T1, T2, T3, T4, T5, T6, T7, T> MapEighth<T>([JetBrains.Annotations.InstantHandle] Func<T8, T> f) =>
            this.Match(
                Either<T1, T2, T3, T4, T5, T6, T7, T>.First,
                v2 => Either.Second(v2),
                v3 => Either.Third(v3),
                v4 => Either.Fourth(v4),
                v5 => Either.Fifth(v5),
                v6 => Either.Sixth(v6),
                v7 => Either.Seventh(v7),
                v8 => Either.Eighth(f(v8)));

        /// <summary>
        ///     Gets the held value if this holds the first case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the first case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T1> TryGetFirst() =>
            this.Match(
                Maybe.Some,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None);

        /// <summary>
        ///     Gets the held value if this holds the second case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the second case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T2> TryGetSecond() =>
            this.Match(
                _ => Maybe.None,
                Maybe.Some,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None);

        /// <summary>
        ///     Gets the held value if this holds the third case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the third case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T3> TryGetThird() =>
            this.Match(
                _ => Maybe.None,
                _ => Maybe.None,
                Maybe.Some,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None);

        /// <summary>
        ///     Gets the held value if this holds the fourth case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the fourth case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T4> TryGetFourth() =>
            this.Match(
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                Maybe.Some,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None);

        /// <summary>
        ///     Gets the held value if this holds the fifth case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the fifth case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T5> TryGetFifth() =>
            this.Match(
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                Maybe.Some,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None);

        /// <summary>
        ///     Gets the held value if this holds the sixth case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the sixth case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T6> TryGetSixth() =>
            this.Match(
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                Maybe.Some,
                _ => Maybe.None,
                _ => Maybe.None);

        /// <summary>
        ///     Gets the held value if this holds the seventh case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the seventh case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T7> TryGetSeventh() =>
            this.Match(
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                Maybe.Some,
                _ => Maybe.None);

        /// <summary>
        ///     Gets the held value if this holds the eighth case.
        /// </summary>
        /// <returns>
        ///     A <see cref="Maybe{T}" /> containing the value if this holds the eighth case, and
        ///     containing nothing otherwise.
        /// </returns>
        [JetBrains.Annotations.Pure]
        public Maybe<T8> TryGetEighth() =>
            this.Match(
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                _ => Maybe.None,
                Maybe.Some);

        /// <summary>
        ///     Returns whether this holds the first case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the first case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetFirst" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsFirst() =>
            this.Match(_ => true, _ => false, _ => false, _ => false, _ => false, _ => false, _ => false, _ => false);

        /// <summary>
        ///     Returns whether this holds the second case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the second case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetSecond" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsSecond() =>
            this.Match(_ => false, _ => true, _ => false, _ => false, _ => false, _ => false, _ => false, _ => false);

        /// <summary>
        ///     Returns whether this holds the third case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the third case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetThird" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsThird() =>
            this.Match(_ => false, _ => false, _ => true, _ => false, _ => false, _ => false, _ => false, _ => false);

        /// <summary>
        ///     Returns whether this holds the fourth case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the fourth case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetFourth" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsFourth() =>
            this.Match(_ => false, _ => false, _ => false, _ => true, _ => false, _ => false, _ => false, _ => false);

        /// <summary>
        ///     Returns whether this holds the fifth case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the fifth case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetFifth" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsFifth() =>
            this.Match(_ => false, _ => false, _ => false, _ => false, _ => true, _ => false, _ => false, _ => false);

        /// <summary>
        ///     Returns whether this holds the sixth case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the sixth case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetSixth" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsSixth() =>
            this.Match(_ => false, _ => false, _ => false, _ => false, _ => false, _ => true, _ => false, _ => false);

        /// <summary>
        ///     Returns whether this holds the seventh case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the seventh case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetSeventh" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsSeventh() =>
            this.Match(_ => false, _ => false, _ => false, _ => false, _ => false, _ => false, _ => true, _ => false);

        /// <summary>
        ///     Returns whether this holds the eighth case.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> if the eighth case is held, and <see langword="false" />
        ///     otherwise.
        /// </returns>
        /// <remarks>
        ///     To get at the value as well, use <see cref="TryGetEighth" />, or <c>Match</c> to handle
        ///     every case.
        /// </remarks>
        [JetBrains.Annotations.Pure]
        public bool IsEighth() =>
            this.Match(_ => false, _ => false, _ => false, _ => false, _ => false, _ => false, _ => false, _ => true);

        void IEitherOfEight.MatchVoid(
            Action<object?> onFirst,
            Action<object?> onSecond,
            Action<object?> onThird,
            Action<object?> onFourth,
            Action<object?> onFifth,
            Action<object?> onSixth,
            Action<object?> onSeventh,
            Action<object?> onEighth) =>
            // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
            this.Upcast<IEitherOfEight>()
                .Match(
                    onFirst.ToFunc(),
                    onSecond.ToFunc(),
                    onThird.ToFunc(),
                    onFourth.ToFunc(),
                    onFifth.ToFunc(),
                    onSixth.ToFunc(),
                    onSeventh.ToFunc(),
                    onEighth.ToFunc());

        Task<T> IEitherOfEight.MatchAsync<T>(
            Func<object?, Task<T>> onFirst,
            Func<object?, Task<T>> onSecond,
            Func<object?, Task<T>> onThird,
            Func<object?, Task<T>> onFourth,
            Func<object?, Task<T>> onFifth,
            Func<object?, Task<T>> onSixth,
            Func<object?, Task<T>> onSeventh,
            Func<object?, Task<T>> onEighth) =>
            this.Upcast<IEitherOfEight>()
                .Match(onFirst, onSecond, onThird, onFourth, onFifth, onSixth, onSeventh, onEighth);

        Task IEitherOfEight.MatchAsyncVoid(
            Func<object?, Task> onFirst,
            Func<object?, Task> onSecond,
            Func<object?, Task> onThird,
            Func<object?, Task> onFourth,
            Func<object?, Task> onFifth,
            Func<object?, Task> onSixth,
            Func<object?, Task> onSeventh,
            Func<object?, Task> onEighth) =>
            this.Upcast<IEitherOfEight>()
                .MatchAsync(
                    onFirst.ToAsyncFunc(),
                    onSecond.ToAsyncFunc(),
                    onThird.ToAsyncFunc(),
                    onFourth.ToAsyncFunc(),
                    onFifth.ToAsyncFunc(),
                    onSixth.ToAsyncFunc(),
                    onSeventh.ToAsyncFunc(),
                    onEighth.ToAsyncFunc());

        #endregion

        /// <summary>
        ///     Converts a value marked for the first position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.First{T}" />.</param>
        /// <returns>An either holding that value as its first case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.First{T}" /> usable in place of this type's own
        ///     <c>First</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default first value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3, T4, T5, T6, T7, T8>(Either.EitherFirst<T1>? value) =>
            First(value == null ? default(T1)! : value.Value);

        /// <summary>
        ///     Converts a value marked for the second position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.Second{T}" />.</param>
        /// <returns>An either holding that value as its second case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.Second{T}" /> usable in place of this type's own
        ///     <c>Second</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default second value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3, T4, T5, T6, T7, T8>(Either.EitherSecond<T2>? value) =>
            Second(value == null ? default(T2)! : value.Value);

        /// <summary>
        ///     Converts a value marked for the third position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.Third{T}" />.</param>
        /// <returns>An either holding that value as its third case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.Third{T}" /> usable in place of this type's own
        ///     <c>Third</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default third value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3, T4, T5, T6, T7, T8>(Either.EitherThird<T3>? value) =>
            Third(value == null ? default(T3)! : value.Value);

        /// <summary>
        ///     Converts a value marked for the fourth position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.Fourth{T}" />.</param>
        /// <returns>An either holding that value as its fourth case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.Fourth{T}" /> usable in place of this type's own
        ///     <c>Fourth</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default fourth value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3, T4, T5, T6, T7, T8>(Either.EitherFourth<T4>? value) =>
            Fourth(value == null ? default(T4)! : value.Value);

        /// <summary>
        ///     Converts a value marked for the fifth position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.Fifth{T}" />.</param>
        /// <returns>An either holding that value as its fifth case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.Fifth{T}" /> usable in place of this type's own
        ///     <c>Fifth</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default fifth value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3, T4, T5, T6, T7, T8>(Either.EitherFifth<T5>? value) =>
            Fifth(value == null ? default(T5)! : value.Value);

        /// <summary>
        ///     Converts a value marked for the sixth position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.Sixth{T}" />.</param>
        /// <returns>An either holding that value as its sixth case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.Sixth{T}" /> usable in place of this type's own
        ///     <c>Sixth</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default sixth value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3, T4, T5, T6, T7, T8>(Either.EitherSixth<T6>? value) =>
            Sixth(value == null ? default(T6)! : value.Value);

        /// <summary>
        ///     Converts a value marked for the seventh position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.Seventh{T}" />.</param>
        /// <returns>An either holding that value as its seventh case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.Seventh{T}" /> usable in place of this type's own
        ///     <c>Seventh</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default seventh value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3, T4, T5, T6, T7, T8>(Either.EitherSeventh<T7>? value) =>
            Seventh(value == null ? default(T7)! : value.Value);

        /// <summary>
        ///     Converts a value marked for the eighth position into an either of this type.
        /// </summary>
        /// <param name="value">The marked value, as produced by <see cref="Either.Eighth{T}" />.</param>
        /// <returns>An either holding that value as its eighth case.</returns>
        /// <remarks>
        ///     This is what makes <see cref="Either.Eighth{T}" /> usable in place of this type's own
        ///     <c>Eighth</c>. A <see langword="null" /> marked value converts to an either holding a
        ///     default eighth value rather than throwing.
        /// </remarks>
        public static implicit operator Either<T1, T2, T3, T4, T5, T6, T7, T8>(Either.EitherEighth<T8>? value) =>
            Eighth(value == null ? default(T8)! : value.Value);

        /// <summary>
        ///     Determines whether two instances hold the same case with equal values.
        /// </summary>
        /// <param name="x">The first instance.</param>
        /// <param name="y">The second instance.</param>
        /// <returns>
        ///     <see langword="true" /> if both hold the same case and the values they hold are
        ///     equal according to <see cref="EqualityComparer{T}.Default" />.
        /// </returns>
        public static bool operator ==(
            Either<T1, T2, T3, T4, T5, T6, T7, T8> x,
            Either<T1, T2, T3, T4, T5, T6, T7, T8> y) =>
            x.valueType == y.valueType
            && EqualityComparer<T1>.Default.Equals(x.value1!, y.value1!)
            && EqualityComparer<T2>.Default.Equals(x.value2!, y.value2!)
            && EqualityComparer<T3>.Default.Equals(x.value3!, y.value3!)
            && EqualityComparer<T4>.Default.Equals(x.value4!, y.value4!)
            && EqualityComparer<T5>.Default.Equals(x.value5!, y.value5!)
            && EqualityComparer<T6>.Default.Equals(x.value6!, y.value6!)
            && EqualityComparer<T7>.Default.Equals(x.value7!, y.value7!)
            && EqualityComparer<T8>.Default.Equals(x.value8!, y.value8!);

        /// <summary>
        ///     Determines whether two instances differ, by negating <see cref="op_Equality" />.
        /// </summary>
        /// <param name="x">The first instance.</param>
        /// <param name="y">The second instance.</param>
        /// <returns>
        ///     <see langword="true" /> if the two hold different cases, or hold the same case with
        ///     values which are not equal.
        /// </returns>
        public static bool operator !=(
            Either<T1, T2, T3, T4, T5, T6, T7, T8> x,
            Either<T1, T2, T3, T4, T5, T6, T7, T8> y) => !(x == y);

        /// <summary>
        ///     Determines whether the given object is an either of this same type which is equal to
        ///     this one.
        /// </summary>
        /// <param name="obj">The object to compare against.</param>
        /// <returns>
        ///     <see langword="true" /> if <paramref name="obj" /> is an either of the same type which
        ///     <see cref="op_Equality" /> considers equal to this one.
        /// </returns>
        /// <remarks>
        ///     An either is never equal to the bare value it holds, only to another either.
        /// </remarks>
        public override bool Equals(object? obj) => obj is Either<T1, T2, T3, T4, T5, T6, T7, T8> e && this == e;

        /// <summary>
        ///     Determines whether the given instance is equal to this one.
        /// </summary>
        /// <param name="other">The instance to compare against.</param>
        /// <returns>
        ///     <see langword="true" /> if <see cref="op_Equality" /> considers the two equal.
        /// </returns>
        /// <remarks>
        ///     The same comparison as <see cref="op_Equality" />, under the name
        ///     <see cref="EqualityComparer{T}.Default" /> looks for, so that comparing these in a
        ///     collection does not box both operands the way <see cref="Equals(object)" /> must.
        /// </remarks>
        public bool Equals(Either<T1, T2, T3, T4, T5, T6, T7, T8> other) => this == other;

        /// <summary>
        ///     Returns a hash code consistent with <see cref="op_Equality" />.
        /// </summary>
        /// <returns>A hash code for this instance.</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = this.valueType;
                hashCode = (hashCode * 397) ^ EqualityComparer<T1>.Default.GetHashCode(this.value1!);
                hashCode = (hashCode * 397) ^ EqualityComparer<T2>.Default.GetHashCode(this.value2!);
                hashCode = (hashCode * 397) ^ EqualityComparer<T3>.Default.GetHashCode(this.value3!);
                hashCode = (hashCode * 397) ^ EqualityComparer<T4>.Default.GetHashCode(this.value4!);
                hashCode = (hashCode * 397) ^ EqualityComparer<T5>.Default.GetHashCode(this.value5!);
                hashCode = (hashCode * 397) ^ EqualityComparer<T6>.Default.GetHashCode(this.value6!);
                hashCode = (hashCode * 397) ^ EqualityComparer<T7>.Default.GetHashCode(this.value7!);
                hashCode = (hashCode * 397) ^ EqualityComparer<T8>.Default.GetHashCode(this.value8!);
                return hashCode;
            }
        }

        /// <summary>
        ///     Returns a readable description of this instance, for diagnostics.
        /// </summary>
        /// <returns>The name of the case held, followed by the value it holds.</returns>
        public override string ToString() => this.Match(
            v1 => $"First: {v1}",
            v2 => $"Second: {v2}",
            v3 => $"Third: {v3}",
            v4 => $"Fourth: {v4}",
            v5 => $"Fifth: {v5}",
            v6 => $"Sixth: {v6}",
            v7 => $"Seventh: {v7}",
            v8 => $"Eighth: {v8}");
    }
}
