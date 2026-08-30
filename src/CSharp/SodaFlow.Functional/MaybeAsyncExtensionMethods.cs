using System;
using System.Threading.Tasks;

namespace SodaFlow.Functional
{
    /// <summary>
    ///     The operations which carry a <see cref="Maybe{T}" /> across an asynchronous step.
    /// </summary>
    /// <remarks>
    ///     <see cref="Maybe{T}" /> already speaks asynchronously on the consuming side, through
    ///     <see cref="Maybe{T}.MatchAsync{TResult}" /> and the helpers built on it. What is here is
    ///     the composing side: the same <c>Map</c>, <c>Bind</c>, <c>Where</c> and <c>ValueOr</c>
    ///     vocabulary, in the two shapes an asynchronous chain needs.
    ///
    ///     The <c>Async</c>-suffixed members take a function which returns a task, for the step
    ///     which does the awaiting. The members without the suffix take a
    ///     <see cref="Task{TResult}" /> of a <see cref="Maybe{T}" /> as their subject, so a chain
    ///     started asynchronously can be continued without awaiting in the middle of it and
    ///     parenthesizing what came before:
    ///
    ///     <code>
    ///     Maybe&lt;string&gt; name = await id.TryParseInt32()
    ///         .BindAsync(i =&gt; repository.FindAsync(i))
    ///         .Map(u =&gt; u.Name)
    ///         .Where(n =&gt; n.Length &gt; 0);
    ///     </code>
    ///
    ///     The function is run only when there is a value, in every case, so nothing is awaited on
    ///     the empty path - which is also why the empty path returns a cached completed task rather
    ///     than allocating one.
    ///
    ///     Every await here is configured not to capture the calling context. Nothing in this file
    ///     runs caller code on the continuation - the awaits exist only to rewrap a result - so
    ///     resuming on a captured context would cost a hop and gain nothing, and would deadlock a
    ///     caller which blocks on the returned task.
    /// </remarks>
    public static class MaybeAsyncExtensionMethods
    {
        /// <summary>
        ///     Transforms the contained value with an asynchronous function, if there is one.
        /// </summary>
        /// <typeparam name="T">The type of the value, when there is one.</typeparam>
        /// <typeparam name="TResult">The type of the value the function produces.</typeparam>
        /// <param name="a">The value to transform.</param>
        /// <param name="f">Run with the contained value when one is present.</param>
        /// <returns>
        ///     A task giving a <see cref="Maybe{T}" /> containing the result of <paramref name="f" />
        ///     if <paramref name="a" /> contained a value, and one containing no value otherwise.
        /// </returns>
        /// <remarks>
        ///     <paramref name="f" /> is not run when there is no value, so this is also how to avoid
        ///     starting work that has no input.
        /// </remarks>
        public static Task<Maybe<TResult>> MapAsync<T, TResult>(
            this Maybe<T> a,
            [JetBrains.Annotations.InstantHandle] Func<T, Task<TResult>> f) =>
            a.Match(v => WrapAsync(f(v)), NoneTask<TResult>);

        /// <summary>
        ///     Transforms the contained value into another <see cref="Maybe{T}" /> with an
        ///     asynchronous function, if there is one, flattening the result.
        /// </summary>
        /// <typeparam name="T">The type of the value, when there is one.</typeparam>
        /// <typeparam name="TResult">The type of the value the function produces.</typeparam>
        /// <param name="a">The value to transform.</param>
        /// <param name="f">Run with the contained value when one is present.</param>
        /// <returns>
        ///     The task returned by <paramref name="f" /> if <paramref name="a" /> contained a value,
        ///     and a completed task giving no value otherwise.
        /// </returns>
        /// <remarks>
        ///     The asynchronous <see cref="MaybeMonad.Bind{T,TResult}" />, for the lookup which is
        ///     itself asynchronous and may find nothing. When there is a value the task returned is
        ///     the one <paramref name="f" /> returned, not a wrapper around it, so a failure surfaces
        ///     as that task faulting.
        /// </remarks>
        public static Task<Maybe<TResult>> BindAsync<T, TResult>(
            this Maybe<T> a,
            [JetBrains.Annotations.InstantHandle] Func<T, Task<Maybe<TResult>>> f) =>
            a.Match(f, NoneTask<TResult>);

        /// <summary>
        ///     Keeps the contained value only if it satisfies an asynchronous predicate.
        /// </summary>
        /// <typeparam name="T">The type of the value, when there is one.</typeparam>
        /// <param name="a">The value to filter.</param>
        /// <param name="predicate">Run with the contained value when one is present.</param>
        /// <returns>
        ///     A task giving <paramref name="a" /> if it contained a value which satisfied
        ///     <paramref name="predicate" />, and a <see cref="Maybe{T}" /> containing no value
        ///     otherwise.
        /// </returns>
        public static Task<Maybe<T>> WhereAsync<T>(
            this Maybe<T> a,
            [JetBrains.Annotations.InstantHandle] Func<T, Task<bool>> predicate) =>
            a.Match(v => KeepIfAsync(v, predicate(v)), NoneTask<T>);

        /// <summary>
        ///     Transforms the value the task will give, if it gives one.
        /// </summary>
        /// <typeparam name="T">The type of the value, when there is one.</typeparam>
        /// <typeparam name="TResult">The type of the value the function produces.</typeparam>
        /// <param name="a">The task giving the value to transform.</param>
        /// <param name="f">Run with the contained value when one is present.</param>
        /// <returns>
        ///     A task giving a <see cref="Maybe{T}" /> containing the result of <paramref name="f" />
        ///     if the awaited value contained one, and one containing no value otherwise.
        /// </returns>
        public static async Task<Maybe<TResult>> Map<T, TResult>(
            this Task<Maybe<T>> a,
            Func<T, TResult> f) =>
            (await a.ConfigureAwait(false)).Map(f);

        /// <summary>
        ///     Transforms the value the task will give with an asynchronous function, if it gives one.
        /// </summary>
        /// <typeparam name="T">The type of the value, when there is one.</typeparam>
        /// <typeparam name="TResult">The type of the value the function produces.</typeparam>
        /// <param name="a">The task giving the value to transform.</param>
        /// <param name="f">Run with the contained value when one is present.</param>
        /// <returns>
        ///     A task giving a <see cref="Maybe{T}" /> containing the result of <paramref name="f" />
        ///     if the awaited value contained one, and one containing no value otherwise.
        /// </returns>
        public static async Task<Maybe<TResult>> MapAsync<T, TResult>(
            this Task<Maybe<T>> a,
            Func<T, Task<TResult>> f) =>
            await (await a.ConfigureAwait(false)).MapAsync(f).ConfigureAwait(false);

        /// <summary>
        ///     Transforms the value the task will give into another <see cref="Maybe{T}" />, if it
        ///     gives one, flattening the result.
        /// </summary>
        /// <typeparam name="T">The type of the value, when there is one.</typeparam>
        /// <typeparam name="TResult">The type of the value the function produces.</typeparam>
        /// <param name="a">The task giving the value to transform.</param>
        /// <param name="f">Run with the contained value when one is present.</param>
        /// <returns>
        ///     A task giving the result of <paramref name="f" /> if the awaited value contained a
        ///     value, and a <see cref="Maybe{T}" /> containing no value otherwise.
        /// </returns>
        public static async Task<Maybe<TResult>> Bind<T, TResult>(
            this Task<Maybe<T>> a,
            Func<T, Maybe<TResult>> f) =>
            (await a.ConfigureAwait(false)).Bind(f);

        /// <summary>
        ///     Transforms the value the task will give into another <see cref="Maybe{T}" /> with an
        ///     asynchronous function, if it gives one, flattening the result.
        /// </summary>
        /// <typeparam name="T">The type of the value, when there is one.</typeparam>
        /// <typeparam name="TResult">The type of the value the function produces.</typeparam>
        /// <param name="a">The task giving the value to transform.</param>
        /// <param name="f">Run with the contained value when one is present.</param>
        /// <returns>
        ///     A task giving the result of <paramref name="f" /> if the awaited value contained a
        ///     value, and a <see cref="Maybe{T}" /> containing no value otherwise.
        /// </returns>
        public static async Task<Maybe<TResult>> BindAsync<T, TResult>(
            this Task<Maybe<T>> a,
            Func<T, Task<Maybe<TResult>>> f) =>
            await (await a.ConfigureAwait(false)).BindAsync(f).ConfigureAwait(false);

        /// <summary>
        ///     Keeps the value the task will give only if it satisfies a predicate.
        /// </summary>
        /// <typeparam name="T">The type of the value, when there is one.</typeparam>
        /// <param name="a">The task giving the value to filter.</param>
        /// <param name="predicate">Run with the contained value when one is present.</param>
        /// <returns>
        ///     A task giving the awaited value if it contained one which satisfied
        ///     <paramref name="predicate" />, and a <see cref="Maybe{T}" /> containing no value
        ///     otherwise.
        /// </returns>
        public static async Task<Maybe<T>> Where<T>(this Task<Maybe<T>> a, Func<T, bool> predicate) =>
            (await a.ConfigureAwait(false)).Where(predicate);

        /// <summary>
        ///     Keeps the value the task will give only if it satisfies an asynchronous predicate.
        /// </summary>
        /// <typeparam name="T">The type of the value, when there is one.</typeparam>
        /// <param name="a">The task giving the value to filter.</param>
        /// <param name="predicate">Run with the contained value when one is present.</param>
        /// <returns>
        ///     A task giving the awaited value if it contained one which satisfied
        ///     <paramref name="predicate" />, and a <see cref="Maybe{T}" /> containing no value
        ///     otherwise.
        /// </returns>
        public static async Task<Maybe<T>> WhereAsync<T>(
            this Task<Maybe<T>> a,
            Func<T, Task<bool>> predicate) =>
            await (await a.ConfigureAwait(false)).WhereAsync(predicate).ConfigureAwait(false);

        /// <summary>
        ///     Falls back to another <see cref="Maybe{T}" /> if the task gives no value.
        /// </summary>
        /// <typeparam name="T">The type of the value, when there is one.</typeparam>
        /// <param name="a">The task giving the value to prefer.</param>
        /// <param name="b">Used when the awaited value contains none.</param>
        /// <returns>
        ///     A task giving the awaited value if it contains one, and <paramref name="b" />
        ///     otherwise.
        /// </returns>
        public static async Task<Maybe<T>> OrElse<T>(this Task<Maybe<T>> a, Maybe<T> b) =>
            (await a.ConfigureAwait(false)).OrElse(b);

        /// <summary>
        ///     Runs one of two functions on the value the task gives, depending on whether there is
        ///     one, and returns its result.
        /// </summary>
        /// <typeparam name="T">The type of the value, when there is one.</typeparam>
        /// <typeparam name="TResult">The type each of the two functions returns.</typeparam>
        /// <param name="a">The task giving the value to match on.</param>
        /// <param name="onSome">Run with the contained value when one is present.</param>
        /// <param name="onNone">Run when no value is present.</param>
        /// <returns>A task giving whatever the function that was run returned.</returns>
        public static async Task<TResult> Match<T, TResult>(
            this Task<Maybe<T>> a,
            Func<T, TResult> onSome,
            Func<TResult> onNone) =>
            (await a.ConfigureAwait(false)).Match(onSome, onNone);

        /// <summary>
        ///     Returns the value the task gives if there is one, and the given value otherwise.
        /// </summary>
        /// <typeparam name="T">The type of the value, when there is one.</typeparam>
        /// <param name="a">The task giving the value to read.</param>
        /// <param name="defaultValue">Returned when the awaited value contains none.</param>
        /// <returns>A task giving the contained value, or <paramref name="defaultValue" />.</returns>
        public static async Task<T> ValueOr<T>(this Task<Maybe<T>> a, T defaultValue) =>
            (await a.ConfigureAwait(false)).ValueOr(defaultValue);

        /// <summary>
        ///     Returns the value the task gives if there is one, and the default for its type
        ///     otherwise.
        /// </summary>
        /// <typeparam name="T">The type of the value, when there is one.</typeparam>
        /// <param name="a">The task giving the value to read.</param>
        /// <returns>
        ///     A task giving the contained value, or <see langword="default" /> if there is none.
        /// </returns>
        /// <remarks>
        ///     Carries the caveat on <see cref="MaybeExtensionMethods.ValueOrDefault{T}" />: for a
        ///     type whose default is itself a legitimate value, the answer cannot say which case it
        ///     got.
        /// </remarks>
        public static async Task<T?> ValueOrDefault<T>(this Task<Maybe<T>> a) =>
            (await a.ConfigureAwait(false)).ValueOrDefault();

        /// <summary>
        ///     Returns the value the task gives if there is one, and throws the exception produced by
        ///     the given function otherwise.
        /// </summary>
        /// <typeparam name="T">The type of the value, when there is one.</typeparam>
        /// <param name="a">The task giving the value to read.</param>
        /// <param name="onNone">Run to produce the exception to throw when there is no value.</param>
        /// <returns>A task giving the contained value.</returns>
        /// <remarks>
        ///     The exception faults the returned task rather than being thrown from this call, since
        ///     whether there is a value is not known until the task completes.
        /// </remarks>
        public static async Task<T> ValueOrThrow<T>(this Task<Maybe<T>> a, Func<Exception> onNone) =>
            (await a.ConfigureAwait(false)).ValueOrThrow(onNone);

        private static async Task<Maybe<TResult>> WrapAsync<TResult>(Task<TResult> task) =>
            Maybe.Some(await task.ConfigureAwait(false));

        private static async Task<Maybe<T>> KeepIfAsync<T>(T value, Task<bool> keep) =>
            Maybe.SomeIf(await keep.ConfigureAwait(false), value);

        private static Task<Maybe<T>> NoneTask<T>() => CompletedNone<T>.Task;

        /// <summary>
        ///     Holds the one completed task giving no value, per type.
        /// </summary>
        /// <typeparam name="T">The type the absent value would have had.</typeparam>
        /// <remarks>
        ///     The empty path is the common one for a lookup that misses, and it always produces the
        ///     same answer, so there is no reason to allocate a fresh task for it every time.
        /// </remarks>
        private static class CompletedNone<T>
        {
            internal static readonly Task<Maybe<T>> Task = System.Threading.Tasks.Task.FromResult(Maybe<T>.None);
        }
    }
}
