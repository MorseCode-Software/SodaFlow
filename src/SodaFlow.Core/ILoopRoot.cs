namespace SodaFlow;

/// <summary>
///     A loop placeholder, as the values that depend on it see it. A value keeps the
///     placeholders that it depends on, thus a conversion to a plain type can check
///     that each loop is closed.
/// </summary>
internal interface ILoopRoot
{
    /// <summary>
    ///     Tells you if the loop is closed, and thus if the value of the placeholder
    ///     exists.
    /// </summary>
    bool IsClosed { get; }
}
