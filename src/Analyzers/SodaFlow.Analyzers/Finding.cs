using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

/// <summary>
///     What the walk found in the code of a frame. It is a read of a pending
///     value, or a pending value that goes to code the analyzer cannot read.
/// </summary>
/// <param name="home">The frame that the origin is a name of.</param>
/// <param name="origin">
///     The parameter or the member through which the value came into that
///     frame. The call that made the frame reports the finding at the argument
///     for that parameter. Null when the value has no such name.
/// </param>
/// <param name="escape">True for a value that goes to unseen code, false for a read.</param>
/// <param name="diagnostic">
///     What to report when the frame is the block itself, or when the finding
///     is in a lambda or a local function of the block.
/// </param>
/// <param name="subject">For a value that goes to unseen code, the name of that code.</param>
internal sealed class Finding(
    in LoopFrame home,
    in ISymbol? origin,
    in bool escape,
    in Diagnostic diagnostic,
    in string subject)
{
    public LoopFrame Home { get; } = home;

    public ISymbol? Origin { get; } = origin;

    public bool Escape { get; } = escape;

    public Diagnostic Diagnostic { get; } = diagnostic;

    public string Subject { get; } = subject;
}
