using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JetBrains.dotMemoryUnit;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests.Memory;

public class StreamTests
{
    [Test]
    [Skip("Requires dotMemory.")]
    public async Task TestListenStrong()
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

        await Assert.That(beforeListenerCount).IsNotNull();
        await Assert.That(listenerCount).IsNotNull();
        await Assert.That(listenerCount2).IsNotNull();
        await Assert.That(duringListenerCount).IsNotNull();
        await Assert.That(duringStreamCount).IsNotNull();
        await Assert.That(afterListenerCount).IsNotNull();

        await Assert.That(listenerCount4).IsEqualTo(listenerCount).Because("BeforeL == AfterL");
        await Assert.That(listenerCount2 > listenerCount3).IsTrue().Because("DuringL > AfterL");
        await Assert.That(listenerCount2 > listenerCount).IsTrue().Because("DuringL > BeforeL");

        await Assert.That(afterListenerCount).IsEqualTo(beforeListenerCount).Because("Before == After");
        await Assert.That(duringStreamCount).IsEqualTo(duringListenerCount).Because("During == During2");
        await Assert.That(duringListenerCount > beforeListenerCount).IsTrue().Because("During > Before");
    }

    [Test]
    [Skip("Requires dotMemory.")]
    public async Task TestUnlisten()
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

        await Assert.That(beforeListenerCount).IsNotNull();
        await Assert.That(listenerCount).IsNotNull();
        await Assert.That(listenerCount2).IsNotNull();
        await Assert.That(duringListenerCount).IsNotNull();
        await Assert.That(duringStreamCount).IsNotNull();
        await Assert.That(afterListenerCount).IsNotNull();

        await Assert.That(listenerCount4).IsEqualTo(listenerCount).Because("BeforeL == After2L");
        await Assert.That(listenerCount2 > listenerCount3).IsTrue().Because("DuringL > AfterL");
        await Assert.That(listenerCount2 > listenerCount).IsTrue().Because("DuringL > BeforeL");

        await Assert.That(afterListenerCount).IsEqualTo(beforeListenerCount).Because("Before == After");
        await Assert.That(duringStreamCount).IsEqualTo(duringListenerCount).Because("During == During2");
        await Assert.That(duringListenerCount > beforeListenerCount).IsTrue().Because("During > Before");
    }

    [Test]
    [Skip("Requires dotMemory.")]
    public async Task TestStreamGarbageCollection()
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

        await Assert.That(beforeListenerCount).IsNotNull();
        await Assert.That(duringListenerCount).IsNotNull();
        await Assert.That(duringListenerCount2).IsNotNull();
        await Assert.That(afterListenerCount).IsNotNull();

        await Assert.That(afterListenerCount).IsEqualTo(beforeListenerCount).Because("Before == After");
        await Assert.That(duringListenerCount2).IsEqualTo(duringListenerCount).Because("During == During2");
        await Assert.That(duringListenerCount > beforeListenerCount).IsTrue().Because("During > Before");
    }

    [Test]
    [Skip("Requires dotMemory.")]
    public async Task TestMapMemory()
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
        }))();

        // The lambda above stays synchronous on purpose: it exists so that its locals are out of
        // scope by the time the snapshot below is taken. What the listener collected is checked
        // here instead, where @out still holds it and nothing has appended to it since.
        await Assert.That(@out).IsEquivalentTo(new[] { "7", "5" }, CollectionOrdering.Matching);

        dotMemory.Check(memory =>
            afterListenerCount = memory.GetObjects(static where => where.Interface.Is<IListener>()).ObjectsCount);

        await Assert.That(beforeListenerCount).IsNotNull();
        await Assert.That(duringListenerCount).IsNotNull();
        await Assert.That(afterListenerCount).IsNotNull();

        await Assert.That(afterListenerCount).IsEqualTo(beforeListenerCount).Because("Before == After");
        await Assert.That(duringListenerCount > beforeListenerCount).IsTrue().Because("During > Before");
    }

    [Test]
    [Skip("Requires dotMemory.")]
    public async Task TestNestedMapGarbageCollection()
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
        }))();

        // The lambda above stays synchronous on purpose: it exists so that its locals are out of
        // scope by the time the snapshot below is taken. What the listener collected is checked
        // here instead, where @out still holds it and nothing has appended to it since.
        await Assert.That(@out).IsEquivalentTo(new[] { "15", "11" }, CollectionOrdering.Matching);

        dotMemory.Check(memory =>
            afterStreamCount =
                memory.GetObjects(static where => where.Type.Is<Stream<int>>()).ObjectsCount +
                memory.GetObjects(static where => where.Type.Is<Stream<string>>()).ObjectsCount);

        dotMemory.Check(memory =>
            afterListenerCount = memory.GetObjects(static where => where.Interface.Is<IListener>()).ObjectsCount);

        // although all listeners and streams have been cleaned up, the nodes will not be disconnected until the stream fires next
        await Assert.That(s.Node.GetListenersCopy().Count).IsEqualTo(1);
        s.Send(1);
        await Assert.That(s.Node.GetListenersCopy().Count).IsEqualTo(0);

        await Assert.That(beforeStreamCount).IsNotNull();
        await Assert.That(beforeListenerCount).IsNotNull();
        await Assert.That(duringStreamCount).IsNotNull();
        await Assert.That(duringListenerCount).IsNotNull();
        await Assert.That(afterStreamCount).IsNotNull();
        await Assert.That(afterListenerCount).IsNotNull();

        await Assert.That(afterStreamCount).IsEqualTo(beforeStreamCount).Because("Before Streams == After Streams");

        await Assert.That(afterListenerCount).IsEqualTo(beforeListenerCount).Because("Before Listeners == After Listeners");

        await Assert.That(duringStreamCount > beforeStreamCount).IsTrue().Because("During Streams > Before Streams");

        await Assert.That(duringListenerCount > beforeListenerCount).IsTrue().Because("During Listeners > Before Listeners");
    }
}
