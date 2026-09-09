using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Collections.Tests;

/// <summary>
///     Scaffolding, to be deleted along with this file once the reactive collections come over and
///     bring real tests with them.
/// </summary>
/// <remarks>
///     It exists rather than the project simply starting out empty because Microsoft.Testing.Platform
///     treats a run that discovered no tests as a failure, and build.cake runs one solution-wide
///     <c>dotnet test</c> — so an empty test project would take the whole build down with it.
///     What it asserts is the only thing there is to assert yet: that this project's reference chain
///     resolves and a graph can be built and torn down through it.
/// </remarks>
public sealed class ScaffoldTests
{
    [Test]
    public async Task ReferenceChainResolves()
    {
        StreamSink<int> source = Stream.CreateSink<int>();
        List<int> received = [];
        IListener l = source.ListenStrong(received.Add);

        source.Send(1);
        l.Unlisten();
        source.Send(2);

        await Assert.That(received).IsEquivalentTo([1]);
    }
}
