using System.Collections.Immutable;
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
/// <param name="readBack">
///     For a read, the members of the object of a forward reference that the
///     value reads back. See <see cref="LoopState.ReadBack" />.
/// </param>
/// <param name="undecided">
///     True for a read in a run of the timers or a Dispose in a callee. The
///     run is in the branch of a condition whose value the walk does not
///     know. See <see cref="Undecided" />.
/// </param>
internal sealed class Finding(
    in LoopFrame home,
    in ISymbol? origin,
    in bool escape,
    in Diagnostic diagnostic,
    in string subject,
    in ImmutableArray<ISymbol> readBack = default,
    in bool undecided = false)
{
    public LoopFrame Home { get; } = home;

    public ISymbol? Origin { get; } = origin;

    public bool Escape { get; } = escape;

    public Diagnostic Diagnostic { get; } = diagnostic;

    public string Subject { get; } = subject;

    public ImmutableArray<ISymbol> ReadBack { get; } = readBack.IsDefault ? ImmutableArray<ISymbol>.Empty : readBack;

    /// <summary>
    ///     True for a read of a run that maybe does not occur. The region
    ///     reports it as an info, and only where no warning has its location.
    ///     The walk gives such a read to no decision about a state.
    /// </summary>
    public bool Undecided { get; } = undecided;

    /// <summary>True for a read that the walk is sure of, and not a value that goes to unseen code.</summary>
    public bool IsASureRead => !this.Escape && !this.Undecided;

    /// <summary>The same <see cref="Finding" />, for a read of a run that maybe does not occur.</summary>
    public Finding AsUndecided() =>
        new(
            home: this.Home,
            origin: this.Origin,
            escape: this.Escape,
            diagnostic: this.Diagnostic,
            subject: this.Subject,
            readBack: this.ReadBack,
            undecided: true);
}
