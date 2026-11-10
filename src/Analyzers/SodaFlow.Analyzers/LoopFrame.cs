namespace SodaFlow.Analyzers;

/// <summary>
///     The code of one call that the walk reads. It is the block, or the body
///     of a method, a constructor, or a getter that the block calls. A local
///     function and a lambda are part of the frame whose code contains them.
/// </summary>
/// <remarks>
///     A frame has no data. It is an identity: a finding keeps the frame that
///     its origin is a name of.
/// </remarks>
internal sealed class LoopFrame;
