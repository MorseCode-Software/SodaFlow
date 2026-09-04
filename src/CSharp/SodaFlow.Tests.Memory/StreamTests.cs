using System;
using System.Collections.Generic;
using JetBrains.dotMemoryUnit;
using NUnit.Framework;

namespace SodaFlow.Tests.Memory;

[TestFixture]
public class StreamTests
{
    [Test]
    [Ignore("Requires dotMemory.")]
    public void TestListenStrong()
    {
        int? listenerCount = null;
        int? listenerCount2 = null;
        int? listenerCount3 = null;
        int? listenerCount4 = null;
        int? beforeListenerCount = null;
        int? duringListenerCount = null;
        int? duringStreamCount = null;
        int? afterListenerCount = null;

        ((Action)(() =>
        {
            StreamSink<int> s = Stream.CreateSink<int>();

            dotMemory.Check(memory =>
                listenerCount = memory.GetObjects(static where => where.Interface.Is<IListener>()).ObjectsCount);

            dotMemory.Check(memory =>
                beforeListenerCount = memory.GetObjects(static where => where.Type.Is<Stream<string>>()).ObjectsCount);

            Stream<string> m = s.Map(static x => (x + 2).ToString());
            List<string> @out = [];

            ((Action)(() =>
            {
                // ReSharper disable once UnusedVariable
                IListener listener = m.ListenStrong(@out.Add);

                dotMemory.Check(memory =>
                    listenerCount2 = memory.GetObjects(static where => where.Interface.Is<IListener>()).ObjectsCount);

                dotMemory.Check(memory =>
                    duringListenerCount =
                        memory.GetObjects(static where => where.Type.Is<Stream<string>>()).ObjectsCount);
            }))();

            dotMemory.Check(memory =>
                listenerCount3 = memory.GetObjects(static where => where.Interface.Is<IListener>()).ObjectsCount);

            dotMemory.Check(memory =>
                duringStreamCount = memory.GetObjects(static where => where.Type.Is<Stream<string>>()).ObjectsCount);
        }))();

        dotMemory.Check(memory =>
            listenerCount4 = memory.GetObjects(static where => where.Interface.Is<IListener>()).ObjectsCount);

        dotMemory.Check(memory =>
            afterListenerCount = memory.GetObjects(static where => where.Type.Is<Stream<string>>()).ObjectsCount);

        Assert.IsNotNull(beforeListenerCount);
        Assert.IsNotNull(listenerCount);
        Assert.IsNotNull(listenerCount2);
        Assert.IsNotNull(duringListenerCount);
        Assert.IsNotNull(duringStreamCount);
        Assert.IsNotNull(afterListenerCount);

        Assert.AreEqual(expected: listenerCount, actual: listenerCount4, message: "BeforeL == AfterL");
        Assert.IsTrue(condition: listenerCount2 > listenerCount3, message: "DuringL > AfterL");
        Assert.IsTrue(condition: listenerCount2 > listenerCount, message: "DuringL > BeforeL");

        Assert.AreEqual(expected: beforeListenerCount, actual: afterListenerCount, message: "Before == After");
        Assert.AreEqual(expected: duringListenerCount, actual: duringStreamCount, message: "During == During2");
        Assert.IsTrue(condition: duringListenerCount > beforeListenerCount, message: "During > Before");
    }

    [Test]
    [Ignore("Requires dotMemory.")]
    public void TestUnlisten()
    {
        int? listenerCount = null;
        int? listenerCount2 = null;
        int? listenerCount3 = null;
        int? listenerCount4 = null;
        int? beforeListenerCount = null;
        int? duringListenerCount = null;
        int? duringStreamCount = null;
        int? afterListenerCount = null;

        ((Action)(() =>
        {
            StreamSink<int> s = Stream.CreateSink<int>();

            dotMemory.Check(memory =>
                listenerCount = memory.GetObjects(static where => where.Interface.Is<IListener>()).ObjectsCount);

            dotMemory.Check(memory =>
                beforeListenerCount = memory.GetObjects(static where => where.Type.Is<Stream<string>>()).ObjectsCount);

            Stream<string> m = s.Map(static x => (x + 2).ToString());
            List<string> @out = [];

            ((Action)(() =>
            {
                IListener listener = m.ListenStrong(@out.Add);

                listener.Unlisten();

                dotMemory.Check(memory =>
                    listenerCount2 = memory.GetObjects(static where => where.Interface.Is<IListener>()).ObjectsCount);

                dotMemory.Check(memory =>
                    duringListenerCount =
                        memory.GetObjects(static where => where.Type.Is<Stream<string>>()).ObjectsCount);
            }))();

            dotMemory.Check(memory =>
                listenerCount3 = memory.GetObjects(static where => where.Interface.Is<IListener>()).ObjectsCount);

            dotMemory.Check(memory =>
                duringStreamCount = memory.GetObjects(static where => where.Type.Is<Stream<string>>()).ObjectsCount);
        }))();

        dotMemory.Check(memory =>
            listenerCount4 = memory.GetObjects(static where => where.Interface.Is<IListener>()).ObjectsCount);

        dotMemory.Check(memory =>
            afterListenerCount = memory.GetObjects(static where => where.Type.Is<Stream<string>>()).ObjectsCount);

        Assert.IsNotNull(beforeListenerCount);
        Assert.IsNotNull(listenerCount);
        Assert.IsNotNull(listenerCount2);
        Assert.IsNotNull(duringListenerCount);
        Assert.IsNotNull(duringStreamCount);
        Assert.IsNotNull(afterListenerCount);

        Assert.AreEqual(expected: listenerCount, actual: listenerCount4, message: "BeforeL == After2L");
        Assert.IsTrue(condition: listenerCount2 > listenerCount3, message: "DuringL > AfterL");
        Assert.IsTrue(condition: listenerCount2 > listenerCount, message: "DuringL > BeforeL");

        Assert.AreEqual(expected: beforeListenerCount, actual: afterListenerCount, message: "Before == After");
        Assert.AreEqual(expected: duringListenerCount, actual: duringStreamCount, message: "During == During2");
        Assert.IsTrue(condition: duringListenerCount > beforeListenerCount, message: "During > Before");
    }

    [Test]
    [Ignore("Requires dotMemory.")]
    public void TestStreamGarbageCollection()
    {
        int? beforeListenerCount = null;
        int? duringListenerCount = null;
        int? duringListenerCount2 = null;
        int? afterListenerCount = null;

        ((Action)(() =>
        {
            // ReSharper disable once NotAccessedVariable
            IListener? listener = null;

            StreamSink<int> s = Stream.CreateSink<int>();

            dotMemory.Check(memory =>
                beforeListenerCount = memory.GetObjects(static where => where.Type.Is<Stream<string>>()).ObjectsCount);

            ((Action)(() =>
            {
                Stream<string> m = s.Map(static x => (x + 2).ToString());
                List<string> @out = [];

                listener = m.ListenStrong(@out.Add);

                dotMemory.Check(memory =>
                    duringListenerCount =
                        memory.GetObjects(static where => where.Type.Is<Stream<string>>()).ObjectsCount);
            }))();

            dotMemory.Check(memory =>
                duringListenerCount2 = memory.GetObjects(static where => where.Type.Is<Stream<string>>()).ObjectsCount);
        }))();

        dotMemory.Check(memory =>
            afterListenerCount = memory.GetObjects(static where => where.Type.Is<Stream<string>>()).ObjectsCount);

        Assert.IsNotNull(beforeListenerCount);
        Assert.IsNotNull(duringListenerCount);
        Assert.IsNotNull(duringListenerCount2);
        Assert.IsNotNull(afterListenerCount);

        Assert.AreEqual(expected: beforeListenerCount, actual: afterListenerCount, message: "Before == After");
        Assert.AreEqual(expected: duringListenerCount, actual: duringListenerCount2, message: "During == During2");
        Assert.IsTrue(condition: duringListenerCount > beforeListenerCount, message: "During > Before");
    }

    [Test]
    [Ignore("Requires dotMemory.")]
    public void TestMapMemory()
    {
        int? beforeListenerCount = null;
        int? duringListenerCount = null;
        int? afterListenerCount = null;

        StreamSink<int> s = Stream.CreateSink<int>();
        Stream<string> m = s.Map(static x => (x + 2).ToString());
        List<string> @out = [];

        dotMemory.Check(memory =>
            beforeListenerCount = memory.GetObjects(static where => where.Interface.Is<IListener>()).ObjectsCount);

        ((Action)(() =>
        {
            IListener l = m.ListenStrong(@out.Add);

            dotMemory.Check(memory =>
                duringListenerCount = memory.GetObjects(static where => where.Interface.Is<IListener>()).ObjectsCount);

            s.Send(5);
            s.Send(3);
            l.Unlisten();
            CollectionAssert.AreEqual(expected: new[] { "7", "5" }, actual: @out);
        }))();

        dotMemory.Check(memory =>
            afterListenerCount = memory.GetObjects(static where => where.Interface.Is<IListener>()).ObjectsCount);

        Assert.IsNotNull(beforeListenerCount);
        Assert.IsNotNull(duringListenerCount);
        Assert.IsNotNull(afterListenerCount);

        Assert.AreEqual(expected: beforeListenerCount, actual: afterListenerCount, message: "Before == After");
        Assert.IsTrue(condition: duringListenerCount > beforeListenerCount, message: "During > Before");
    }

    [Test]
    [Ignore("Requires dotMemory.")]
    public void TestNestedMapGarbageCollection()
    {
        int? beforeStreamCount = null;
        int? beforeListenerCount = null;
        int? duringStreamCount = null;
        int? duringListenerCount = null;
        int? afterStreamCount = null;
        int? afterListenerCount = null;

        StreamSink<int> s = Stream.CreateSink<int>();
        List<string> @out = [];

        dotMemory.Check(memory =>
            beforeStreamCount =
                memory.GetObjects(static where => where.Type.Is<Stream<int>>()).ObjectsCount +
                memory.GetObjects(static where => where.Type.Is<Stream<string>>()).ObjectsCount);

        dotMemory.Check(memory =>
            beforeListenerCount = memory.GetObjects(static where => where.Interface.Is<IListener>()).ObjectsCount);

        ((Action)(() =>
        {
            Stream<string> m =
                s.Map(static x => x + 2).Map(static x => 2 * x).Map(static x => x + 1).Map(static x => x.ToString());

            IListener l = m.ListenStrong(@out.Add);

            dotMemory.Check(memory =>
                duringStreamCount =
                    memory.GetObjects(static where => where.Type.Is<Stream<int>>()).ObjectsCount +
                    memory.GetObjects(static where => where.Type.Is<Stream<string>>()).ObjectsCount);

            dotMemory.Check(memory =>
                duringListenerCount = memory.GetObjects(static where => where.Interface.Is<IListener>()).ObjectsCount);

            s.Send(5);
            s.Send(3);
            l.Unlisten();
            CollectionAssert.AreEqual(expected: new[] { "15", "11" }, actual: @out);
        }))();

        dotMemory.Check(memory =>
            afterStreamCount =
                memory.GetObjects(static where => where.Type.Is<Stream<int>>()).ObjectsCount +
                memory.GetObjects(static where => where.Type.Is<Stream<string>>()).ObjectsCount);

        dotMemory.Check(memory =>
            afterListenerCount = memory.GetObjects(static where => where.Interface.Is<IListener>()).ObjectsCount);

        // although all listeners and streams have been cleaned up, the nodes will not be disconnected until the stream fires next
        Assert.AreEqual(expected: 1, actual: s.Node.GetListenersCopy().Count);
        s.Send(1);
        Assert.AreEqual(expected: 0, actual: s.Node.GetListenersCopy().Count);

        Assert.IsNotNull(beforeStreamCount);
        Assert.IsNotNull(beforeListenerCount);
        Assert.IsNotNull(duringStreamCount);
        Assert.IsNotNull(duringListenerCount);
        Assert.IsNotNull(afterStreamCount);
        Assert.IsNotNull(afterListenerCount);

        Assert.AreEqual(
            expected: beforeStreamCount,
            actual: afterStreamCount,
            message: "Before Streams == After Streams");

        Assert.AreEqual(
            expected: beforeListenerCount,
            actual: afterListenerCount,
            message: "Before Listeners == After Listeners");

        Assert.IsTrue(condition: duringStreamCount > beforeStreamCount, message: "During Streams > Before Streams");

        Assert.IsTrue(
            condition: duringListenerCount > beforeListenerCount,
            message: "During Listeners > Before Listeners");
    }
}
