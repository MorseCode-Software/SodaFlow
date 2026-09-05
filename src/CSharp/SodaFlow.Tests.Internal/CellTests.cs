using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SodaFlow.Functional;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests.Internal;

public sealed class CellTests
{
    [Test]
    public async Task TestTransaction()
    {
        bool calledBack = false;

        TransactionInternal.Apply((trans, _) =>
        {
            trans.Prioritized(node: Node<Unit>.Null, action: _ => calledBack = true);
            return UnitInternal.Value;
        });

        await Assert.That(calledBack).IsTrue();
    }

    [Test]
    public async Task TestRegen()
    {
        List<int> @out = [];

        TransactionInternal.Apply((trans, _) =>
        {
            SetNeedsRegeneratingAndPrioritized(() => @out.Add(1));
            SetNeedsRegeneratingAndPrioritized(() => SetNeedsRegeneratingAndPrioritized(() => @out.Add(4)));
            SetNeedsRegeneratingAndPrioritized(() => @out.Add(2));

            SetNeedsRegeneratingAndPrioritized(() =>
                SetNeedsRegeneratingAndPrioritized(() => SetNeedsRegeneratingAndPrioritized(() => @out.Add(6))));

            SetNeedsRegeneratingAndPrioritized(() => SetNeedsRegeneratingAndPrioritized(() => @out.Add(5)));
            trans.Prioritized(node: new Node<Unit>(), action: _ => @out.Add(3));

            return UnitInternal.Value;

            void SetNeedsRegeneratingAndPrioritized(Action action) =>
                trans.Prioritized(node: new Node<Unit>(), action: _ => action());
        });

        await Assert.That(@out).IsEquivalentTo([1, 2, 3, 4, 5, 6], CollectionOrdering.Matching);
    }
}
