using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace SodaFlow.Tests;

[TestFixture]
public class ForwardReferenceTests
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
    public void TestWithoutCapturesResolvesTheReference()
    {
        Node node =
            ForwardReference<Node>.WithoutCaptures(static reference => Node.WithChildHolding(reference.AsCell()));

        Assert.AreSame(expected: node, actual: node.Child.Parent.Sample());
    }

    [Test]
    public void TestWithoutCapturesReturnsWhatTheFunctionProduced()
    {
        object? produced = null;

        object result =
            ForwardReference<object>.WithoutCaptures(_ =>
            {
                produced = new object();
                return produced;
            });

        Assert.AreSame(expected: produced, actual: result);
    }

    [Test]
    public void TestWithoutCapturesRunsTheFunctionOnce()
    {
        int calls = 0;

        // ReSharper disable once ReturnValueOfPureMethodIsNotUsed - Testing for side effect only.
        ForwardReference<int>.WithoutCaptures(_ =>
        {
            calls++;
            return 1;
        });

        Assert.AreEqual(expected: 1, actual: calls);
    }

    [Test]
    public void TestWithoutCapturesReferenceNeverChanges()
    {
        // The single-valued case of a cell loop: the reference resolves once and stays there.
        Node node =
            ForwardReference<Node>.WithoutCaptures(static reference => Node.WithChildHolding(reference.AsCell()));

        List<Node> @out = [];

        using (node.Child.Parent.ListenStrong(@out.Add))
        {
        }

        CollectionAssert.AreEqual(expected: new[] { node }, actual: @out);
    }

    [Test]
    public void TestWithCapturesResolvesTheReferenceAndReturnsTheCaptures()
    {
        (Node node, StreamSink<int> sink) =
            ForwardReference<Node>.WithCaptures(static reference =>
                (Value: Node.WithChildHolding(reference.AsCell()), Captures: Stream.CreateSink<int>()));

        Assert.AreSame(expected: node, actual: node.Child.Parent.Sample());
        Assert.IsNotNull(sink);
    }

    [Test]
    public void TestWithCapturesRunsTheFunctionOnce()
    {
        int calls = 0;

        // ReSharper disable once ReturnValueOfPureMethodIsNotUsed - Testing for side effect only.
        ForwardReference<int>.WithCaptures(_ =>
        {
            calls++;
            return (Value: 1, Captures: 2);
        });

        Assert.AreEqual(expected: 1, actual: calls);
    }

    [Test]
    public void TestTwoObjectsCanReferToEachOther()
    {
        // Neither exists when the other is constructed, which is the knot this unties.
        (Node node, Child child) =
            ForwardReference<Node>.WithCaptures(static reference =>
            {
                Child c = new(reference.AsCell());
                return (Value: Node.WithChildHolding(reference.AsCell()), Captures: c);
            });

        Assert.AreSame(expected: node, actual: child.Parent.Sample());
        Assert.AreSame(expected: node, actual: node.Child.Parent.Sample());
    }

    [Test]
    public void TestWorksInsideAnExistingTransaction()
    {
        Node node =
            Transaction.Run(static () =>
                ForwardReference<Node>.WithoutCaptures(static reference => Node.WithChildHolding(reference.AsCell())));

        Assert.AreSame(expected: node, actual: node.Child.Parent.Sample());
    }

    [Test]
    public void TestReferenceCannotBeReadDuringConstruction() =>
        // The reference is a promise about what the value will be, not the value, so asking
        // for it before the constructing function has returned has no answer.
        Assert.Throws<InvalidOperationException>(static () =>
            // ReSharper disable once ReturnValueOfPureMethodIsNotUsed - Testing for side effect only.
            ForwardReference<int>.WithoutCaptures(static reference => reference.Sample()));
}
