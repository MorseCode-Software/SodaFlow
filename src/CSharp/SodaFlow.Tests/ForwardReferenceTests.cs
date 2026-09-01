using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace SodaFlow.Tests
{
    [TestFixture]
    public class ForwardReferenceTests
    {
        private class Child
        {
            public Child(Cell<Node> parent) => this.Parent = parent;

            public Cell<Node> Parent { get; }
        }

        private class Node
        {
            public Node(Func<Cell<Node>, Child> makeChild) => this.Child = makeChild(Cell.Constant(this));

            private Node(Child child) => this.Child = child;

            public Child Child { get; }

            public static Node WithChildHolding(Cell<Node> reference) => new Node(new Child(reference));
        }

        [Test]
        public void TestWithoutCapturesResolvesTheReference()
        {
            Node node = ForwardReference<Node>.WithoutCaptures(reference => Node.WithChildHolding(reference.AsCell()));

            Assert.AreSame(expected: node, actual: node.Child.Parent.Sample());
        }

        [Test]
        public void TestWithoutCapturesReturnsWhatTheFunctionProduced()
        {
            object produced = null;

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
            Node node = ForwardReference<Node>.WithoutCaptures(reference => Node.WithChildHolding(reference.AsCell()));

            List<Node> @out = new List<Node>();

            using (node.Child.Parent.ListenStrong(@out.Add))
            {
            }

            CollectionAssert.AreEqual(expected: new[] { node }, actual: @out);
        }

        [Test]
        public void TestWithCapturesResolvesTheReferenceAndReturnsTheCaptures()
        {
            (Node node, StreamSink<int> sink) =
                ForwardReference<Node>.WithCaptures(reference =>
                    (Value: Node.WithChildHolding(reference.AsCell()), Captures: Stream.CreateSink<int>()));

            Assert.AreSame(expected: node, actual: node.Child.Parent.Sample());
            Assert.IsNotNull(sink);
        }

        [Test]
        public void TestWithCapturesRunsTheFunctionOnce()
        {
            int calls = 0;

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
                ForwardReference<Node>.WithCaptures(reference =>
                {
                    Child c = new Child(reference.AsCell());
                    return (Value: Node.WithChildHolding(reference.AsCell()), Captures: c);
                });

            Assert.AreSame(expected: node, actual: child.Parent.Sample());
            Assert.AreSame(expected: node, actual: node.Child.Parent.Sample());
        }

        [Test]
        public void TestWorksInsideAnExistingTransaction()
        {
            Node node =
                Transaction.Run(() =>
                    ForwardReference<Node>.WithoutCaptures(reference => Node.WithChildHolding(reference.AsCell())));

            Assert.AreSame(expected: node, actual: node.Child.Parent.Sample());
        }

        [Test]
        public void TestReferenceCannotBeReadDuringConstruction() =>
            // The reference is a promise about what the value will be, not the value, so asking
            // for it before the constructing function has returned has no answer.
            Assert.Throws<InvalidOperationException>(() =>
                ForwardReference<int>.WithoutCaptures(reference => reference.Sample()));
    }
}