using System.Collections.Generic;

namespace SodaFlow.Samples.Bounce.ViewModels;

/// <summary>
///     Reading a whole scene at one instant.
/// </summary>
public static class SceneExtensionMethods
{
    extension(IScene scene)
    {
        /// <summary>
        ///     Where every ball is, all as of the same moment.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         One transaction for the frame rather than one per ball. Sampling a behavior opens a
        ///         transaction if it is not already inside one, so reading four balls separately would
        ///         read them at four slightly different instants - which is not wrong so much as
        ///         needlessly untrue, and would show as a frame in which one ball had bounced and
        ///         another had not quite.
        ///     </para>
        ///     <para>
        ///         Opening a transaction also runs any timer that has come due, so the frame that draws
        ///         a bounce is the frame that discovers it.
        ///     </para>
        ///     <para>
        ///         A method rather than a property, though it reads like one and an extension block
        ///         would now allow it. Sampling is a deliberate act everywhere else in this sample -
        ///         <see cref="Ball.SampleAt" />, <c>Cell.Sample</c> - and a property would quietly
        ///         suggest that reading it twice costs nothing and answers the same.
        ///     </para>
        /// </remarks>
        public IReadOnlyList<(double X, double Y)> SamplePositions() =>
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
}
