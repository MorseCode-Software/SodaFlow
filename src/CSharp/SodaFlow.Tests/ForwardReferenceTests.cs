using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests;

public sealed class ForwardReferenceTests
{
    private sealed class Child(Cell<Node> parent)
    {
        public Cell<Node> Parent { get; } = parent;
    }

    private sealed class Node(Child child)
    {
        public Child Child { get; } = child;

        public static Node WithChildHolding(Cell<Node> reference) => new(new Child(reference));
    }

    [Test]
    public async Task TestWithoutCapturesResolvesTheReference()
    {
        Node node =
            ForwardReference<Node>.WithoutCaptures(static reference => Node.WithChildHolding(reference.AsCell()));

        await Assert.That(node.Child.Parent.Sample()).IsSameReferenceAs(node);
    }

    [Test]
    public async Task TestWithoutCapturesReturnsWhatTheFunctionProduced()
    {
        object? produced = null;

        object result =
            ForwardReference<object>.WithoutCaptures(_ =>
            {
                produced = new object();
                return produced;
            });

        await Assert.That(result).IsSameReferenceAs(produced);
    }

    [Test]
    public async Task TestWithoutCapturesRunsTheFunctionOnce()
    {
        int calls = 0;

        // ReSharper disable once ReturnValueOfPureMethodIsNotUsed - Testing for side effect only.
        ForwardReference<int>.WithoutCaptures(_ =>
        {
            calls++;
            return 1;
        });

        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task TestWithoutCapturesReferenceNeverChanges()
    {
        // The single-valued case of a cell loop: the reference resolves once and stays there.
        Node node =
            ForwardReference<Node>.WithoutCaptures(static reference => Node.WithChildHolding(reference.AsCell()));

        List<Node> @out = [];

        using (node.Child.Parent.ListenStrong(@out.Add))
        {
        }

        await Assert.That(@out).IsEquivalentTo([node], CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestWithCapturesResolvesTheReferenceAndReturnsTheCaptures()
    {
        (Node node, StreamSink<int> sink) =
            ForwardReference<Node>.WithCaptures(static reference =>
                (Value: Node.WithChildHolding(reference.AsCell()), Captures: Stream.CreateSink<int>()));

        await Assert.That(node.Child.Parent.Sample()).IsSameReferenceAs(node);
        await Assert.That(sink).IsNotNull();
    }

    [Test]
    public async Task TestWithCapturesRunsTheFunctionOnce()
    {
        int calls = 0;

        // ReSharper disable once ReturnValueOfPureMethodIsNotUsed - Testing for side effect only.
        ForwardReference<int>.WithCaptures(_ =>
        {
            calls++;
            return (Value: 1, Captures: 2);
        });

        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task TestTwoObjectsCanReferToEachOther()
    {
        // Neither exists when the other is constructed, which is the knot this unties.
        (Node node, Child child) =
            ForwardReference<Node>.WithCaptures(static reference =>
            {
                Child c = new(reference.AsCell());
                return (Value: Node.WithChildHolding(reference.AsCell()), Captures: c);
            });

        await Assert.That(child.Parent.Sample()).IsSameReferenceAs(node);
        await Assert.That(node.Child.Parent.Sample()).IsSameReferenceAs(node);
    }

    [Test]
    public async Task TestWorksInsideAnExistingTransaction()
    {
        Node node =
            Transaction.Run(static () =>
                ForwardReference<Node>.WithoutCaptures(static reference => Node.WithChildHolding(reference.AsCell())));

        await Assert.That(node.Child.Parent.Sample()).IsSameReferenceAs(node);
    }

    [Test]
    public async Task TestReferenceCannotBeReadDuringConstruction() =>
        // The reference is a promise about what the value will be, not the value, so asking
        // for it before the constructing function has returned has no answer.
        await Assert.That(static () =>
            // ReSharper disable once ReturnValueOfPureMethodIsNotUsed - Testing for side effect only.
            ForwardReference<int>.WithoutCaptures(static reference => reference.Sample())).ThrowsExactly<InvalidOperationException>();
}
