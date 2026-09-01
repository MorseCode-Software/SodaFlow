using System;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace SodaFlow.Functional;

/// <summary>
///     The common non-generic view of every either, regardless of how many cases it has.
/// </summary>
/// <remarks>
///     Where the number of cases is known, <see cref="IEitherOfTwo" /> and its wider siblings
///     add the matching operations; where the types are known, prefer
///     <see cref="Either{T1,T2}" /> itself.
/// </remarks>
public interface IEither
{
    /// <summary>
    ///     Gets whichever value this holds, without regard to which case it is.
    /// </summary>
    /// <returns>The held value, boxed if it is a value type.</returns>
    /// <remarks>
    ///     The case is lost along with the type. Where either matters, use one of the match
    ///     operations instead.
    /// </remarks>
    [Pure]
    object? GetValueAsObject();
}

/// <summary>
///     A non-generic view of an either of two cases, for code which must handle one
///     without knowing what types the cases would be.
/// </summary>
/// <remarks>
///     Every member mirrors one on <see cref="Either{T1,T2}" /> and its wider siblings, with
///     the held value surfaced as <see cref="object" />. Prefer the generic type wherever the
///     types are known: this interface boxes, and loses the type of the value.
/// </remarks>
public interface IEitherOfTwo : IEither
{
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
    T Match<T>(
        [InstantHandle] Func<object?, T> onFirst,
        [InstantHandle] Func<object?, T> onSecond);

    /// <summary>
    ///     Runs one of the two actions depending on which case is held.
    /// </summary>
    /// <param name="onFirst">Run with the value when the first case is held.</param>
    /// <param name="onSecond">Run with the value when the second case is held.</param>
    void MatchVoid(
        [InstantHandle] Action<object?> onFirst,
        [InstantHandle] Action<object?> onSecond);

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
    Task<T> MatchAsync<T>(
        [InstantHandle] Func<object?, Task<T>> onFirst,
        [InstantHandle] Func<object?, Task<T>> onSecond);

    /// <summary>
    ///     Runs one of the two asynchronous actions depending on which case is held.
    /// </summary>
    /// <param name="onFirst">Run with the value when the first case is held.</param>
    /// <param name="onSecond">Run with the value when the second case is held.</param>
    /// <returns>A task which completes when the selected action has completed.</returns>
    Task MatchAsyncVoid(
        [InstantHandle] Func<object?, Task> onFirst,
        [InstantHandle] Func<object?, Task> onSecond);
}

/// <summary>
///     A non-generic view of an either of three cases, for code which must handle one
///     without knowing what types the cases would be.
/// </summary>
/// <remarks>
///     Every member mirrors one on <see cref="Either{T1,T2}" /> and its wider siblings, with
///     the held value surfaced as <see cref="object" />. Prefer the generic type wherever the
///     types are known: this interface boxes, and loses the type of the value.
/// </remarks>
public interface IEitherOfThree : IEither
{
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
    T Match<T>(
        [InstantHandle] Func<object?, T> onFirst,
        [InstantHandle] Func<object?, T> onSecond,
        [InstantHandle] Func<object?, T> onThird);

    /// <summary>
    ///     Runs one of the three actions depending on which case is held.
    /// </summary>
    /// <param name="onFirst">Run with the value when the first case is held.</param>
    /// <param name="onSecond">Run with the value when the second case is held.</param>
    /// <param name="onThird">Run with the value when the third case is held.</param>
    void MatchVoid(
        [InstantHandle] Action<object?> onFirst,
        [InstantHandle] Action<object?> onSecond,
        [InstantHandle] Action<object?> onThird);

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
    Task<T> MatchAsync<T>(
        [InstantHandle] Func<object?, Task<T>> onFirst,
        [InstantHandle] Func<object?, Task<T>> onSecond,
        [InstantHandle] Func<object?, Task<T>> onThird);

    /// <summary>
    ///     Runs one of the three asynchronous actions depending on which case is held.
    /// </summary>
    /// <param name="onFirst">Run with the value when the first case is held.</param>
    /// <param name="onSecond">Run with the value when the second case is held.</param>
    /// <param name="onThird">Run with the value when the third case is held.</param>
    /// <returns>A task which completes when the selected action has completed.</returns>
    Task MatchAsyncVoid(
        [InstantHandle] Func<object?, Task> onFirst,
        [InstantHandle] Func<object?, Task> onSecond,
        [InstantHandle] Func<object?, Task> onThird);
}

/// <summary>
///     A non-generic view of an either of four cases, for code which must handle one
///     without knowing what types the cases would be.
/// </summary>
/// <remarks>
///     Every member mirrors one on <see cref="Either{T1,T2}" /> and its wider siblings, with
///     the held value surfaced as <see cref="object" />. Prefer the generic type wherever the
///     types are known: this interface boxes, and loses the type of the value.
/// </remarks>
public interface IEitherOfFour : IEither
{
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
    T Match<T>(
        [InstantHandle] Func<object?, T> onFirst,
        [InstantHandle] Func<object?, T> onSecond,
        [InstantHandle] Func<object?, T> onThird,
        [InstantHandle] Func<object?, T> onFourth);

    /// <summary>
    ///     Runs one of the four actions depending on which case is held.
    /// </summary>
    /// <param name="onFirst">Run with the value when the first case is held.</param>
    /// <param name="onSecond">Run with the value when the second case is held.</param>
    /// <param name="onThird">Run with the value when the third case is held.</param>
    /// <param name="onFourth">Run with the value when the fourth case is held.</param>
    void MatchVoid(
        [InstantHandle] Action<object?> onFirst,
        [InstantHandle] Action<object?> onSecond,
        [InstantHandle] Action<object?> onThird,
        [InstantHandle] Action<object?> onFourth);

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
    Task<T> MatchAsync<T>(
        [InstantHandle] Func<object?, Task<T>> onFirst,
        [InstantHandle] Func<object?, Task<T>> onSecond,
        [InstantHandle] Func<object?, Task<T>> onThird,
        [InstantHandle] Func<object?, Task<T>> onFourth);

    /// <summary>
    ///     Runs one of the four asynchronous actions depending on which case is held.
    /// </summary>
    /// <param name="onFirst">Run with the value when the first case is held.</param>
    /// <param name="onSecond">Run with the value when the second case is held.</param>
    /// <param name="onThird">Run with the value when the third case is held.</param>
    /// <param name="onFourth">Run with the value when the fourth case is held.</param>
    /// <returns>A task which completes when the selected action has completed.</returns>
    Task MatchAsyncVoid(
        [InstantHandle] Func<object?, Task> onFirst,
        [InstantHandle] Func<object?, Task> onSecond,
        [InstantHandle] Func<object?, Task> onThird,
        [InstantHandle] Func<object?, Task> onFourth);
}

/// <summary>
///     A non-generic view of an either of five cases, for code which must handle one
///     without knowing what types the cases would be.
/// </summary>
/// <remarks>
///     Every member mirrors one on <see cref="Either{T1,T2}" /> and its wider siblings, with
///     the held value surfaced as <see cref="object" />. Prefer the generic type wherever the
///     types are known: this interface boxes, and loses the type of the value.
/// </remarks>
public interface IEitherOfFive : IEither
{
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
    T Match<T>(
        [InstantHandle] Func<object?, T> onFirst,
        [InstantHandle] Func<object?, T> onSecond,
        [InstantHandle] Func<object?, T> onThird,
        [InstantHandle] Func<object?, T> onFourth,
        [InstantHandle] Func<object?, T> onFifth);

    /// <summary>
    ///     Runs one of the five actions depending on which case is held.
    /// </summary>
    /// <param name="onFirst">Run with the value when the first case is held.</param>
    /// <param name="onSecond">Run with the value when the second case is held.</param>
    /// <param name="onThird">Run with the value when the third case is held.</param>
    /// <param name="onFourth">Run with the value when the fourth case is held.</param>
    /// <param name="onFifth">Run with the value when the fifth case is held.</param>
    void MatchVoid(
        [InstantHandle] Action<object?> onFirst,
        [InstantHandle] Action<object?> onSecond,
        [InstantHandle] Action<object?> onThird,
        [InstantHandle] Action<object?> onFourth,
        [InstantHandle] Action<object?> onFifth);

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
    Task<T> MatchAsync<T>(
        [InstantHandle] Func<object?, Task<T>> onFirst,
        [InstantHandle] Func<object?, Task<T>> onSecond,
        [InstantHandle] Func<object?, Task<T>> onThird,
        [InstantHandle] Func<object?, Task<T>> onFourth,
        [InstantHandle] Func<object?, Task<T>> onFifth);

    /// <summary>
    ///     Runs one of the five asynchronous actions depending on which case is held.
    /// </summary>
    /// <param name="onFirst">Run with the value when the first case is held.</param>
    /// <param name="onSecond">Run with the value when the second case is held.</param>
    /// <param name="onThird">Run with the value when the third case is held.</param>
    /// <param name="onFourth">Run with the value when the fourth case is held.</param>
    /// <param name="onFifth">Run with the value when the fifth case is held.</param>
    /// <returns>A task which completes when the selected action has completed.</returns>
    Task MatchAsyncVoid(
        [InstantHandle] Func<object?, Task> onFirst,
        [InstantHandle] Func<object?, Task> onSecond,
        [InstantHandle] Func<object?, Task> onThird,
        [InstantHandle] Func<object?, Task> onFourth,
        [InstantHandle] Func<object?, Task> onFifth);
}

/// <summary>
///     A non-generic view of an either of six cases, for code which must handle one
///     without knowing what types the cases would be.
/// </summary>
/// <remarks>
///     Every member mirrors one on <see cref="Either{T1,T2}" /> and its wider siblings, with
///     the held value surfaced as <see cref="object" />. Prefer the generic type wherever the
///     types are known: this interface boxes, and loses the type of the value.
/// </remarks>
public interface IEitherOfSix : IEither
{
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
    T Match<T>(
        [InstantHandle] Func<object?, T> onFirst,
        [InstantHandle] Func<object?, T> onSecond,
        [InstantHandle] Func<object?, T> onThird,
        [InstantHandle] Func<object?, T> onFourth,
        [InstantHandle] Func<object?, T> onFifth,
        [InstantHandle] Func<object?, T> onSixth);

    /// <summary>
    ///     Runs one of the six actions depending on which case is held.
    /// </summary>
    /// <param name="onFirst">Run with the value when the first case is held.</param>
    /// <param name="onSecond">Run with the value when the second case is held.</param>
    /// <param name="onThird">Run with the value when the third case is held.</param>
    /// <param name="onFourth">Run with the value when the fourth case is held.</param>
    /// <param name="onFifth">Run with the value when the fifth case is held.</param>
    /// <param name="onSixth">Run with the value when the sixth case is held.</param>
    void MatchVoid(
        [InstantHandle] Action<object?> onFirst,
        [InstantHandle] Action<object?> onSecond,
        [InstantHandle] Action<object?> onThird,
        [InstantHandle] Action<object?> onFourth,
        [InstantHandle] Action<object?> onFifth,
        [InstantHandle] Action<object?> onSixth);

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
    Task<T> MatchAsync<T>(
        [InstantHandle] Func<object?, Task<T>> onFirst,
        [InstantHandle] Func<object?, Task<T>> onSecond,
        [InstantHandle] Func<object?, Task<T>> onThird,
        [InstantHandle] Func<object?, Task<T>> onFourth,
        [InstantHandle] Func<object?, Task<T>> onFifth,
        [InstantHandle] Func<object?, Task<T>> onSixth);

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
    Task MatchAsyncVoid(
        [InstantHandle] Func<object?, Task> onFirst,
        [InstantHandle] Func<object?, Task> onSecond,
        [InstantHandle] Func<object?, Task> onThird,
        [InstantHandle] Func<object?, Task> onFourth,
        [InstantHandle] Func<object?, Task> onFifth,
        [InstantHandle] Func<object?, Task> onSixth);
}

/// <summary>
///     A non-generic view of an either of seven cases, for code which must handle one
///     without knowing what types the cases would be.
/// </summary>
/// <remarks>
///     Every member mirrors one on <see cref="Either{T1,T2}" /> and its wider siblings, with
///     the held value surfaced as <see cref="object" />. Prefer the generic type wherever the
///     types are known: this interface boxes, and loses the type of the value.
/// </remarks>
public interface IEitherOfSeven : IEither
{
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
    T Match<T>(
        [InstantHandle] Func<object?, T> onFirst,
        [InstantHandle] Func<object?, T> onSecond,
        [InstantHandle] Func<object?, T> onThird,
        [InstantHandle] Func<object?, T> onFourth,
        [InstantHandle] Func<object?, T> onFifth,
        [InstantHandle] Func<object?, T> onSixth,
        [InstantHandle] Func<object?, T> onSeventh);

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
    void MatchVoid(
        [InstantHandle] Action<object?> onFirst,
        [InstantHandle] Action<object?> onSecond,
        [InstantHandle] Action<object?> onThird,
        [InstantHandle] Action<object?> onFourth,
        [InstantHandle] Action<object?> onFifth,
        [InstantHandle] Action<object?> onSixth,
        [InstantHandle] Action<object?> onSeventh);

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
    Task<T> MatchAsync<T>(
        [InstantHandle] Func<object?, Task<T>> onFirst,
        [InstantHandle] Func<object?, Task<T>> onSecond,
        [InstantHandle] Func<object?, Task<T>> onThird,
        [InstantHandle] Func<object?, Task<T>> onFourth,
        [InstantHandle] Func<object?, Task<T>> onFifth,
        [InstantHandle] Func<object?, Task<T>> onSixth,
        [InstantHandle] Func<object?, Task<T>> onSeventh);

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
    Task MatchAsyncVoid(
        [InstantHandle] Func<object?, Task> onFirst,
        [InstantHandle] Func<object?, Task> onSecond,
        [InstantHandle] Func<object?, Task> onThird,
        [InstantHandle] Func<object?, Task> onFourth,
        [InstantHandle] Func<object?, Task> onFifth,
        [InstantHandle] Func<object?, Task> onSixth,
        [InstantHandle] Func<object?, Task> onSeventh);
}

/// <summary>
///     A non-generic view of an either of eight cases, for code which must handle one
///     without knowing what types the cases would be.
/// </summary>
/// <remarks>
///     Every member mirrors one on <see cref="Either{T1,T2}" /> and its wider siblings, with
///     the held value surfaced as <see cref="object" />. Prefer the generic type wherever the
///     types are known: this interface boxes, and loses the type of the value.
/// </remarks>
public interface IEitherOfEight : IEither
{
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
    T Match<T>(
        [InstantHandle] Func<object?, T> onFirst,
        [InstantHandle] Func<object?, T> onSecond,
        [InstantHandle] Func<object?, T> onThird,
        [InstantHandle] Func<object?, T> onFourth,
        [InstantHandle] Func<object?, T> onFifth,
        [InstantHandle] Func<object?, T> onSixth,
        [InstantHandle] Func<object?, T> onSeventh,
        [InstantHandle] Func<object?, T> onEighth);

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
    void MatchVoid(
        [InstantHandle] Action<object?> onFirst,
        [InstantHandle] Action<object?> onSecond,
        [InstantHandle] Action<object?> onThird,
        [InstantHandle] Action<object?> onFourth,
        [InstantHandle] Action<object?> onFifth,
        [InstantHandle] Action<object?> onSixth,
        [InstantHandle] Action<object?> onSeventh,
        [InstantHandle] Action<object?> onEighth);

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
    Task<T> MatchAsync<T>(
        [InstantHandle] Func<object?, Task<T>> onFirst,
        [InstantHandle] Func<object?, Task<T>> onSecond,
        [InstantHandle] Func<object?, Task<T>> onThird,
        [InstantHandle] Func<object?, Task<T>> onFourth,
        [InstantHandle] Func<object?, Task<T>> onFifth,
        [InstantHandle] Func<object?, Task<T>> onSixth,
        [InstantHandle] Func<object?, Task<T>> onSeventh,
        [InstantHandle] Func<object?, Task<T>> onEighth);

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
    Task MatchAsyncVoid(
        [InstantHandle] Func<object?, Task> onFirst,
        [InstantHandle] Func<object?, Task> onSecond,
        [InstantHandle] Func<object?, Task> onThird,
        [InstantHandle] Func<object?, Task> onFourth,
        [InstantHandle] Func<object?, Task> onFifth,
        [InstantHandle] Func<object?, Task> onSixth,
        [InstantHandle] Func<object?, Task> onSeventh,
        [InstantHandle] Func<object?, Task> onEighth);
}