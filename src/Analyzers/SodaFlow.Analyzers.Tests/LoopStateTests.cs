using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Analyzers.Tests;

/// <summary>Checks the state that the analyzer keeps for a value.</summary>
internal sealed class LoopStateTests
{
    /// <summary>
    ///     The join of two states has each object of the two states one time. The
    ///     objects of the first state come first, in their order, and then each
    ///     object that only the second state has.
    /// </summary>
    /// <remarks>
    ///     The join has two paths, for a short array of objects and for a long
    ///     one. The second case here is long, and it uses the path with a set.
    /// </remarks>
    /// <param name="inEach">The number of objects of each state.</param>
    /// <param name="inTheTwo">The number of objects that the two states have together.</param>
    [Test]
    [Arguments(4, 2)]
    [Arguments(18, 10)]
    public async Task TheJoinOfTwoStatesHasEachObjectOneTime(int inEach, int inTheTwo)
    {
        MadeObject[] all =
        [
            .. Enumerable.Range(start: 0, count: 2 * inEach - inTheTwo)
                .Select(static _ =>
                    new MadeObject(
                        type: null,
                        exact: false,
                        frame: null,
                        turn: null,
                        isOfTurnsBefore: false,
                        shapeOf: null,
                        shapePart: 0))
        ];

        LoopState first = LoopState.None.WithObjects([.. all.Take(inEach)]);
        LoopState second = LoopState.None.WithObjects([.. all.Skip(inEach - inTheTwo)]);

        await Assert.That(first.Join(second).Objects.SequenceEqual(all)).IsTrue();
    }
}
