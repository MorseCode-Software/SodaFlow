using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests;

/// <summary>
///     CleanupNow runs the action at the call, one time. The collected path is in
///     SodaFlow.Tests.Memory, which can run the sweep.
/// </summary>
public sealed class CleanupTests
{
    [Test]
    public async Task CleanupNowRunsTheActionNow()
    {
        int runs = 0;
        Cleanup cleanup = new(() => runs++);

        cleanup.CleanupNow();

        await Assert.That(runs).IsEqualTo(1);
    }

    [Test]
    public async Task ASecondCleanupNowDoesNothing()
    {
        int runs = 0;
        Cleanup cleanup = new(() => runs++);

        cleanup.CleanupNow();
        cleanup.CleanupNow();

        await Assert.That(runs).IsEqualTo(1);
    }
}
