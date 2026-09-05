using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using SodaFlow.Functional;

namespace SodaFlow.Tests;

public class CommonTests
{
    [Test]
    public async Task TestBaseSend1()
    {
        StreamSink<string> s = Stream.CreateSink<string>();
        List<string> @out = [];
        IListener l = s.ListenStrong(@out.Add);
        s.Send("a");
        s.Send("b");
        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(new[] { "a", "b" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestOperationalSplit()
    {
        StreamSink<List<string>> a = Stream.CreateSink<List<string>>();
        Stream<string> b = Operational.Split<string, List<string>>(a);
        List<string> @out = [];
        IListener l = b.ListenStrong(@out.Add);
        a.Send(["a", "b"]);
        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(new[] { "a", "b" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestOperationalDefer1()
    {
        StreamSink<string> a = Stream.CreateSink<string>();
        Stream<string> b = Operational.Defer(a);
        List<string> @out = [];
        IListener l = b.ListenStrong(@out.Add);
        a.Send("a");
        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(new[] { "a" }, CollectionOrdering.Matching);
        List<string> out2 = [];
        IListener l2 = b.ListenStrong(out2.Add);
        a.Send("b");
        l2.Unlisten();
        await Assert.That(out2).IsEquivalentTo(new[] { "b" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestOperationalDefer2()
    {
        StreamSink<string> a = Stream.CreateSink<string>();
        StreamSink<string> b = Stream.CreateSink<string>();
        Stream<string> c = Operational.Defer(a).OrElse(b);
        List<string> @out = [];
        IListener l = c.ListenStrong(@out.Add);
        a.Send("a");
        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(new[] { "a" }, CollectionOrdering.Matching);
        List<string> out2 = [];
        IListener l2 = c.ListenStrong(out2.Add);

        Transaction.RunVoid(() =>
        {
            a.Send("b");
            b.Send("B");
        });

        l2.Unlisten();
        await Assert.That(out2).IsEquivalentTo(new[] { "B", "b" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestStreamOrElse1()
    {
        StreamSink<int> a = Stream.CreateSink<int>();
        StreamSink<int> b = Stream.CreateSink<int>();
        Stream<int> c = a.OrElse(b);
        List<int> @out = [];
        IListener l = c.ListenStrong(@out.Add);
        a.Send(0);
        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(new[] { 0 }, CollectionOrdering.Matching);
        List<int> out2 = [];
        IListener l2 = c.ListenStrong(out2.Add);
        b.Send(10);
        l2.Unlisten();
        await Assert.That(out2).IsEquivalentTo(new[] { 10 }, CollectionOrdering.Matching);
        List<int> out3 = [];
        IListener l3 = c.ListenStrong(out3.Add);

        Transaction.RunVoid(() =>
        {
            a.Send(2);
            b.Send(20);
        });

        l3.Unlisten();
        await Assert.That(out3).IsEquivalentTo(new[] { 2 }, CollectionOrdering.Matching);
        List<int> out4 = [];
        IListener l4 = c.ListenStrong(out4.Add);
        b.Send(30);
        l4.Unlisten();
        await Assert.That(out4).IsEquivalentTo(new[] { 30 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestOperationalDeferSimultaneous()
    {
        StreamSink<string> a = Stream.CreateSink<string>();
        StreamSink<string> b = Stream.CreateSink<string>();
        Stream<string> c = Operational.Defer(a).OrElse(Operational.Defer(b));
        List<string> @out = [];
        IListener l = c.ListenStrong(@out.Add);
        b.Send("A");
        l.Unlisten();
        await Assert.That(@out).IsEquivalentTo(new[] { "A" }, CollectionOrdering.Matching);
        List<string> out2 = [];
        IListener l2 = c.ListenStrong(out2.Add);

        Transaction.RunVoid(() =>
        {
            a.Send("b");
            b.Send("B");
        });

        l2.Unlisten();
        await Assert.That(out2).IsEquivalentTo(new[] { "b" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestUnitEqualsOperator()
    {
        Unit u1 = Unit.Value;
        Unit u2 = Unit.Value;

        await Assert.That(u1 == u2).IsTrue();
        await Assert.That(u2 == u1).IsTrue();
    }

    [Test]
    public async Task TestUnitNotEqualsOperator()
    {
        Unit u1 = Unit.Value;
        Unit u2 = Unit.Value;

        await Assert.That(u1 != u2).IsFalse();
        await Assert.That(u2 != u1).IsFalse();
    }
}
