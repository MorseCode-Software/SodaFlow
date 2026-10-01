using System;
using System.Threading.Tasks;
using JetBrains.Annotations;
using SodaFlow.Functional;

namespace SodaFlow.Async;

/// <summary>
///     The Execute overloads of <see cref="AsyncMapStatus{TInput,TResult}" /> that use
///     <see cref="Maybe{T}" />. SodaFlow.Core.Async cannot refer to Maybe, thus these overloads
///     are here and not on the type.
/// </summary>
[PublicAPI]
public static class AsyncMapStatusExtensions
{
    /// <param name="status">The pipeline that gets the value.</param>
    extension<TInput, TResult>(AsyncMapStatus<TInput, TResult> status)
    {
        /// <summary>
        ///     Puts the value of a cell into this pipeline only where the cell holds Some, and
        ///     answers with the Task of that value alone. This method reads the cell in the
        ///     transaction that puts the value in, as
        ///     <see cref="AsyncMapStatus{TInput,TResult}.Execute(Cell{TInput})" /> does.
        ///     <para>
        ///         Where the cell holds Some at that instant, the pipeline admits its value. The
        ///         Task then gives Some with the result, and it obeys the rules of
        ///         <see cref="AsyncMapStatus{TInput,TResult}.Execute(TInput)" /> for each other
        ///         condition. Read the remarks of that method.
        ///     </para>
        ///     <para>
        ///         Where the cell holds None at that instant, the pipeline admits nothing and the
        ///         operation does not run. The Task gives None. This method does not wait for the
        ///         cell to hold Some. A cancellation never gives None, thus None always tells that
        ///         the cell held None.
        ///     </para>
        /// </summary>
        /// <param name="value">The cell to read.</param>
        /// <returns>
        ///     The Task of the value that the cell gives, or a Task that gives None where the cell
        ///     holds None.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="value" /> is null.</exception>
        public Task<Maybe<TResult>> Execute(Cell<Maybe<TInput>> value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            ExecuteCompletion<TResult, Maybe<TResult>> completion = new(Maybe<TResult>.Some);

            status.ExecuteIfSome(
                read: () => value.SampleImpl().Match(
                    onSome: MaybeInternal.Some,
                    onNone: static () => MaybeInternal<TInput>.None),
                completion: completion,
                onNone: () => completion.TrySetOutput(Maybe<TResult>.None));

            return completion.Task;
        }
    }
}
