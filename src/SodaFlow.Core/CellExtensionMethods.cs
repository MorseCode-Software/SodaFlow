using System;
using System.Collections.Generic;

namespace SodaFlow;

internal static class CellExtensionMethodsInternal
{
    internal static Cell<TResult> LiftCellsImpl<T, T2, TResult>(
        this IEnumerable<T2> c,
        Func<IReadOnlyList<T>, TResult> f)
        where T2 : Cell<T> =>
        LiftCellsImpl(inputs: [.. c], f: f);

    internal static Cell<TResult> LiftCellsImpl<T, T2, TResult>(
        this IReadOnlyCollection<T2> c,
        Func<IReadOnlyList<T>, TResult> f)
        where T2 : Cell<T> =>
        LiftCellsImpl(inputs: [.. c], f: f);

    // The list lift of BehaviorExtensionMethodsInternal, held in a cell. The array is the lift's
    // own copy, as it is there.
    private static Cell<TResult> LiftCellsImpl<T, T2, TResult>(T2[] inputs, Func<IReadOnlyList<T>, TResult> f)
        where T2 : Cell<T> =>
        TransactionInternal.Apply(
            code: static (trans1, _, state) =>
            {
                (Stream<TResult> @out, LazySample<TResult> initialValue) =
                    BehaviorExtensionMethodsInternal.LiftBehaviors(
                        trans1: trans1,
                        inputs: state.Inputs,
                        f: state.F);

                return @out.HoldLazyCellInternal(trans: trans1, initialValue: initialValue);
            },
            state: (Inputs: inputs, F: f));
}
