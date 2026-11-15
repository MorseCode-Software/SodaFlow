using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Analyzers.Tests;

/// <summary>
///     Checks the numbers from which the walk knows that a read of a loop left
///     no new state.
/// </summary>
internal sealed class ShapeNumberTests
{
    /// <summary>
    ///     A loop can give the state of one local to a second local. The sum for
    ///     the two locals after that must not be the sum from before. If it is,
    ///     the walk does not make a read that it must make.
    /// </summary>
    /// <remarks>
    ///     The number before this one was 31 times the location, with an exclusive
    ///     or of the state. The state of a pending cell has no bit from 19. After
    ///     the multiplication with 31, the locations here are the same in each bit
    ///     before that. With that number, each of the 2,016 pairs has one sum.
    /// </remarks>
    [Test]
    public async Task TwoLocationsThatChangeStatesDoNotHaveTheSumFromBefore()
    {
        const int pending = 472827;

        // 31 times this number is 1 in 32 bits.
        const uint inverseOf31 = 0xBDEF7BDF;

        int[] locations =
        [
            .. Enumerable.Range(start: 0, count: 64)
                .Select(static part => unchecked((int)(inverseOf31 * (uint)(7 + (part << 19)))))
        ];

        int same = 0;

        for (int first = 0; first < locations.Length; first++)
        {
            for (int second = first + 1; second < locations.Length; second++)
            {
                int before =
                    unchecked(ShapeNumber.Of(location: locations[first], state: pending)
                              + ShapeNumber.Of(location: locations[second], state: 0));

                int after =
                    unchecked(ShapeNumber.Of(location: locations[first], state: 0)
                              + ShapeNumber.Of(location: locations[second], state: pending));

                if (before == after)
                {
                    same++;
                }
            }
        }

        await Assert.That(same).IsEqualTo(0);
    }

    /// <summary>
    ///     The number of a name does not change from one run of the analyzer to
    ///     the next. The number here is from a second source, which is not the
    ///     code of the analyzer.
    /// </summary>
    [Test]
    public async Task ANameHasTheSameNumberInEachRun()
    {
        await Assert.That(ShapeNumber.Of("Share")).IsEqualTo(1334144472);
        await Assert.That(ShapeNumber.Of("share")).IsNotEqualTo(ShapeNumber.Of("Share"));
    }
}
