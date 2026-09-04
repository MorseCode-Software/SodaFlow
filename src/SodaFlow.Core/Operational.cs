using System.Collections.Generic;

namespace SodaFlow;

internal static class OperationalInternal
{
    internal static Stream<T> UpdatesImpl<T>(Behavior<T> b) =>
        TransactionInternal.Apply((trans, _) => b.Updates().Coalesce(trans1: trans, f: static (_, right) => right));

    internal static Stream<T> ValueImpl<T>(Behavior<T> b) => TransactionInternal.Apply((trans, _) => b.Value(trans));

    internal static Stream<T> DeferImpl<T>(Stream<T> s) => SplitImpl<T, T[]>(s.MapImpl(static a => new[] { a }));

    internal static Stream<T> SplitImpl<T, TCollection>(Stream<TCollection> s)
        where TCollection : IEnumerable<T>
    {
        Stream<T> @out = new(s.KeepListenersAlive);

        IListener l1 =
            s.Listen(
                target: new Node<T>(),
                action: (trans, aa) =>
                {
                    int childIx = 0;

                    foreach (T a in aa)
                    {
                        trans.Split(index: childIx, action: trans1 => @out.Send(trans: trans1, a: a));
                        childIx++;
                    }
                });

        return @out.UnsafeAttachListener(l1);
    }
}
