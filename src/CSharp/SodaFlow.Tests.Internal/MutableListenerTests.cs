using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests.Internal;

/// <summary>
///     A mutable listener owns the listener it points at. Thus, the listener it
///     replaces stops.
/// </summary>
public sealed class MutableListenerTests
{
    [Test]
    public async Task SetListenerStopsTheListenerItReplaces()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        List<string> seen = [];
        MutableListener m = new();

        m.SetListener(s.ListenImpl(v => seen.Add("first:" + v)));
        m.SetListener(s.ListenImpl(v => seen.Add("second:" + v)));
        s.Send(1);
        m.Unlisten();

        await Assert.That(seen).IsEquivalentTo(expected: ["second:1"], ordering: CollectionOrdering.Matching);
    }

    // A set of the listener that is already the target must not stop it. Otherwise, a caller that
    // sets the same listener again by mistake loses it with no message.
    [Test]
    public async Task SetListenerWithTheSameListenerKeepsIt()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        List<int> seen = [];
        MutableListener m = new();
        IListenerInternal l = s.ListenImpl(seen.Add);

        m.SetListener(l);
        m.SetListener(l);
        s.Send(1);
        m.Unlisten();

        await Assert.That(seen).IsEquivalentTo(expected: [1], ordering: CollectionOrdering.Matching);
    }
}
