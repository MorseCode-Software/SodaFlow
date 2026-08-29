using System;
using System.Threading.Tasks;

namespace SodaFlow.Functional
{
    /// <summary>
    ///     A non-generic view of a <see cref="Maybe{T}" />, for code which must handle a value that may
    ///     or may not be present without knowing what type it would be.
    /// </summary>
    /// <remarks>
    ///     Every member mirrors one on <see cref="Maybe{T}" />, with the contained value surfaced as
    ///     <see cref="object" />. Prefer <see cref="Maybe{T}" /> itself wherever the type is known: this
    ///     interface boxes, and loses the type of the value.
    /// </remarks>
    public interface IMaybe
    {
        /// <summary>
        ///     Runs one of two functions depending on whether a value is present, and returns its result.
        /// </summary>
        /// <typeparam name="T">The type each of the two functions returns.</typeparam>
        /// <param name="onSome">Run with the contained value when one is present.</param>
        /// <param name="onNone">Run when no value is present.</param>
        /// <returns>Whatever the function that was run returned.</returns>
        /// <remarks>
        ///     Exactly one of the two functions is called, and it is called before this method returns.
        ///     This is the primitive the rest of the interface is expressed in terms of.
        /// </remarks>
        T Match<T>(
            [JetBrains.Annotations.InstantHandle] Func<object, T> onSome,
            [JetBrains.Annotations.InstantHandle] Func<T> onNone);
        
        /// <summary>
        ///     Runs one of two actions depending on whether a value is present.
        /// </summary>
        /// <param name="onSome">Run with the contained value when one is present.</param>
        /// <param name="onNone">Run when no value is present.</param>
        void MatchVoid(
            [JetBrains.Annotations.InstantHandle] Action<object> onSome,
            [JetBrains.Annotations.InstantHandle] Action onNone);
        
        /// <summary>
        ///     Runs an action with the contained value if one is present, and otherwise does nothing.
        /// </summary>
        /// <param name="onSome">Run with the contained value when one is present.</param>
        void MatchSome([JetBrains.Annotations.InstantHandle] Action<object> onSome);

        /// <summary>
        ///     Runs an action if no value is present, and otherwise does nothing.
        /// </summary>
        /// <param name="onNone">Run when no value is present.</param>
        void MatchNone([JetBrains.Annotations.InstantHandle] Action onNone);
        
        /// <summary>
        ///     Runs one of two asynchronous functions depending on whether a value is present, and
        ///     returns its result.
        /// </summary>
        /// <typeparam name="T">The type each of the two functions produces.</typeparam>
        /// <param name="onSome">Run with the contained value when one is present.</param>
        /// <param name="onNone">Run when no value is present.</param>
        /// <returns>The task returned by whichever function was run.</returns>
        /// <remarks>
        ///     Only the selected function is invoked; the returned task is its task, not a wrapper, so
        ///     failures surface as that task faulting rather than as an exception from this call.
        /// </remarks>
        Task<T> MatchAsync<T>(
            [JetBrains.Annotations.InstantHandle] Func<object, Task<T>> onSome,
            [JetBrains.Annotations.InstantHandle] Func<Task<T>> onNone);
        
        /// <summary>
        ///     Runs one of two asynchronous actions depending on whether a value is present.
        /// </summary>
        /// <param name="onSome">Run with the contained value when one is present.</param>
        /// <param name="onNone">Run when no value is present.</param>
        /// <returns>A task which completes when the selected action has completed.</returns>
        Task MatchAsyncVoid(
            [JetBrains.Annotations.InstantHandle] Func<object, Task> onSome,
            [JetBrains.Annotations.InstantHandle] Func<Task> onNone);
        
        /// <summary>
        ///     Runs an asynchronous action with the contained value if one is present, and otherwise
        ///     does nothing.
        /// </summary>
        /// <param name="onSome">Run with the contained value when one is present.</param>
        /// <returns>
        ///     A task which completes when the action has completed, or an already completed task if no
        ///     value is present.
        /// </returns>
        Task MatchSomeAsync([JetBrains.Annotations.InstantHandle] Func<object, Task> onSome);

        /// <summary>
        ///     Runs an asynchronous action if no value is present, and otherwise does nothing.
        /// </summary>
        /// <param name="onNone">Run when no value is present.</param>
        /// <returns>
        ///     A task which completes when the action has completed, or an already completed task if a
        ///     value is present.
        /// </returns>
        Task MatchNoneAsync([JetBrains.Annotations.InstantHandle] Func<Task> onNone);
    }
}