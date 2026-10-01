using System.Collections.Generic;

namespace SodaFlow.Samples.Bounce.ViewModels;

/// <summary>
///     Reads a full scene at one instant.
/// </summary>
public static class SceneExtensionMethods
{
    /// <summary>
    ///     The position of each ball, at the same moment.
    /// </summary>
    /// <param name="scene">The scene to read.</param>
    /// <remarks>
    ///     <para>
    ///         There is one transaction for the frame, and not one transaction for each ball. A
    ///         sample of a behavior opens a transaction when there is no transaction. Thus, four
    ///         reads give the four balls at four different instants. Such a frame can show one
    ///         ball after its bounce and a second ball before its bounce.
    ///     </para>
    ///     <para>
    ///         A new transaction also runs each timer that is due, thus the frame that draws a
    ///         bounce is the frame that finds it.
    ///     </para>
    /// </remarks>
    public static IReadOnlyList<(double X, double Y)> SamplePositions(this IScene scene) =>
        Transaction.Run(() =>
        {
            (double X, double Y)[] positions = new (double X, double Y)[scene.Balls.Count];

            for (int i = 0; i < positions.Length; i++)
            {
                positions[i] = scene.Balls[i].SampleAt();
            }

            return positions;
        });
}
