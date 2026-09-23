using System;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace SodaFlow.Functional;

/// <summary>
///     A non-generic view of a <see cref="Maybe{T}" />, for code which must handle a value that can
///     or that has none, with no knowledge of its type.
/// </summary>
/// <remarks>
///     Each member mirrors one on <see cref="Maybe{T}" />, with the contained value surfaced as
///     <see cref="object" />. Prefer <see cref="Maybe{T}" /> itself wherever the type is known: this
///     interface boxes, and loses the type of the value.
/// </remarks>
[PublicAPI]
public interface IMaybe
{
    /// <summary>
    ///     Runs one function when there is a value, and a different one when there is none.
    /// </summary>
    /// <typeparam name="T">The type each of the two functions returns.</typeparam>
    /// <param name="onSome">Run with the contained value when there is one.</param>
    /// <param name="onNone">Run when there is no value.</param>
    /// <returns>Whatever the function that was run returned.</returns>
    /// <remarks>
    ///     This calls one function of the two, and it calls that function before this method returns.
    ///     This is the primitive that expresses all the other members of the interface.
    /// </remarks>
    T Match<T>(
        [InstantHandle] Func<object?, T> onSome,
        [InstantHandle] Func<T> onNone);

    /// <summary>
    ///     Runs one action when there is a value, and a different action when there is none.
    /// </summary>
    /// <param name="onSome">Run with the contained value when there is one.</param>
    /// <param name="onNone">Run when there is no value.</param>
    void MatchVoid(
        [InstantHandle] Action<object?> onSome,
        [InstantHandle] Action onNone);

    /// <summary>
    ///     Runs an action with the contained value when there is one, and otherwise does nothing.
    /// </summary>
    /// <param name="onSome">Run with the contained value when there is one.</param>
    void MatchSome([InstantHandle] Action<object?> onSome);

    /// <summary>
    ///     Runs an action when there is no value, and otherwise does nothing.
    /// </summary>
    /// <param name="onNone">Run when there is no value.</param>
    void MatchNone([InstantHandle] Action onNone);

    /// <summary>
    ///     Runs one asynchronous function when there is a value, and a different one when there is none.
    ///     returns its result.
    /// </summary>
    /// <typeparam name="T">The type each of the two functions produces.</typeparam>
    /// <param name="onSome">Run with the contained value when there is one.</param>
    /// <param name="onNone">Run when there is no value.</param>
    /// <returns>The task returned by whichever function was run.</returns>
    /// <remarks>
    ///     Only the selected function runs. The task from this call is its task, and not a wrapper, so
    ///     failures surface as that task faulting rather than as an exception from this call.
    /// </remarks>
    Task<T> MatchAsync<T>(
        [InstantHandle] Func<object?, Task<T>> onSome,
        [InstantHandle] Func<Task<T>> onNone);

    /// <summary>
    ///     Runs one asynchronous action when there is a value, and a different one when there is none.
    /// </summary>
    /// <param name="onSome">Run with the contained value when there is one.</param>
    /// <param name="onNone">Run when there is no value.</param>
    /// <returns>A task which completes when the selected action has completed.</returns>
    Task MatchAsyncVoid(
        [InstantHandle] Func<object?, Task> onSome,
        [InstantHandle] Func<Task> onNone);

    /// <summary>
    ///     Runs an asynchronous action with the contained value when there is one, and otherwise
    ///     does nothing.
    /// </summary>
    /// <param name="onSome">Run with the contained value when there is one.</param>
    /// <returns>
    ///     A task which completes when the action has completed, or a completed task if no
    ///     value is there.
    /// </returns>
    Task MatchSomeAsync([InstantHandle] Func<object?, Task> onSome);

    /// <summary>
    ///     Runs an asynchronous action when there is no value, and otherwise does nothing.
    /// </summary>
    /// <param name="onNone">Run when there is no value.</param>
    /// <returns>
    ///     A task which completes when the action has completed, or a completed task if a
    ///     value is there.
    /// </returns>
    Task MatchNoneAsync([InstantHandle] Func<Task> onNone);
}
