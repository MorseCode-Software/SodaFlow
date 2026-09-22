using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using SodaFlow.Functional;

namespace SodaFlow.Async;

/// <summary>
///     A short shape for a strategy that reads no input, because the input type is
///     <see cref="Unit" />.
/// </summary>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public abstract class AsyncConcurrencyStrategy<TState>
    : AsyncConcurrencyStrategy<Unit, TState>
{
}

/// <summary>
///     The entry point, which is not generic, for the strategies in the library: Parallel, Queue,
///     QueuePerGroup, and SwitchLatest. Each one uses only the schedule, and not the
///     <c>TInput</c> or the <c>TResult</c> of the call, thus the two types are
///     <see cref="Unit" />. The schedule of each one is in one shared position, the internal
///     <c>AsyncConcurrencyStrategyFactory</c> in SodaFlow.Core.Async. That factory is generic over
///     the type of a value that a strategy does not use, because Core has no dependency on
///     SodaFlow.Functional and thus no type of its own for it. The static methods below give
///     <see cref="Unit" /> as that type argument and return the result. Thus, a consumer of this C#
///     wrapper gets a version with the <see cref="Unit" /> type, which this wrapper also uses for
///     <c>cancelAll</c> and for other values. This class, and its short base classes below, which
///     are <see cref="AsyncConcurrencyStrategy{TState}" /> and
///     <see cref="AsyncConcurrencyStrategy{TInput,TState}" />, have no relation to that shared
///     factory. They are here only to let a consumer with a custom strategy on
///     <see cref="Unit" /> subclass
///     <see cref="AsyncConcurrencyStrategy{TInput,TState}" /> and write
///     <see cref="Unit" /> one time.
/// </summary>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public abstract class AsyncConcurrencyStrategy
    : AsyncConcurrencyStrategy<Unit, Unit>
{
    private static readonly AsyncConcurrencyStrategyBase<Unit> ParallelInstance =
        AsyncConcurrencyStrategyFactory.Parallel(Unit.Value);

    private static readonly AsyncConcurrencyStrategyBase<Unit> QueueInstance =
        AsyncConcurrencyStrategyFactory.Queue<Unit>();

    private static readonly AsyncConcurrencyStrategyBase<Unit> SwitchLatestInstance =
        AsyncConcurrencyStrategyFactory.SwitchLatest<Unit>();

    /// <summary>Each send starts its own operation immediately. The results come in the sequence
    /// of their ends.</summary>
    public static AsyncConcurrencyStrategyBase<Unit> Parallel() => ParallelInstance;

    /// <summary>One operation or no operation runs at a time. A subsequent send goes to the
    /// queue, and the queue runs in sequence.</summary>
    public static AsyncConcurrencyStrategyBase<Unit> Queue() => QueueInstance;

    /// <summary>
    ///     The entry point for a queue for each group. In one group, one operation or no
    ///     operation runs at a time, and two different groups run at the same time. Call
    ///     <see cref="QueuePerGroupHelper{TInput}.Create{TGroup}" /> on the result to give the
    ///     group function. <typeparamref name="TInput" /> here only lets the compiler infer the
    ///     input type of that call, and
    ///     <see cref="QueuePerGroupHelper{TInput}.Create{TGroup}" /> makes the strategy.
    /// </summary>
    public static QueuePerGroupHelper<TInput> QueuePerGroup<TInput>() => QueuePerGroupHelper<TInput>.Instance;

    /// <summary>A new send cancels the operation that runs and replaces it.</summary>
    public static AsyncConcurrencyStrategyBase<Unit> SwitchLatest() => SwitchLatestInstance;

    /// <summary>
    ///     This type is here only to let <see cref="QueuePerGroup{TInput}" /> infer
    ///     <typeparamref name="TInput" />, and to let <see cref="Create{TGroup}" /> infer
    ///     <c>TGroup</c>. Without it, C# cannot infer two type parameters from two different
    ///     calls.
    /// </summary>
    [PublicAPI]
    public class QueuePerGroupHelper<TInput>
    {
        internal static readonly QueuePerGroupHelper<TInput> Instance = new();

        private QueuePerGroupHelper()
        {
        }

        /// <summary>
        ///     Builds a strategy with one queue for each group. <paramref name="getGroup" /> puts
        ///     each input in a group. In one group, a subsequent send goes behind the sends before
        ///     it, as <see cref="Queue" /> does. Two different groups do not wait for each other.
        /// </summary>
        /// <param name="getGroup">Calculates the group key of an input value.</param>
        /// <param name="groupComparer">
        ///     An optional equality comparer for the group keys. The default is
        ///     <see cref="EqualityComparer{TGroup}.Default" />.
        /// </param>
        public AsyncConcurrencyStrategyBase<TInput> Create<TGroup>(
            Func<TInput, TGroup> getGroup,
            IEqualityComparer<TGroup>? groupComparer = null)
            where TGroup : notnull =>
            AsyncConcurrencyStrategyFactory.QueuePerGroup<Unit, TInput, TGroup>(
                getGroup: getGroup,
                groupComparer: groupComparer);
    }
}
