namespace SodaFlow.Analyzers;

/// <summary>
///     The code of one call that the walk reads. It is the block, or the body
///     of a method, a constructor, or a getter that the block calls. A local
///     function and a lambda are part of the frame whose code contains them.
/// </summary>
/// <remarks>
///     A frame is an identity: a finding keeps the frame that its origin is a
///     name of.
/// </remarks>
internal sealed class LoopFrame
{
    /// <summary>
    ///     True after the walk of the code of this frame. A function that the
    ///     code made can run after that, when the caller kept it. Then, no walk
    ///     of this frame gets what the function finds.
    /// </summary>
    public bool HasReturned { get; private set; }

    public void Return() => this.HasReturned = true;
}
