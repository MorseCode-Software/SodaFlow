namespace SodaFlow.Analyzers;

/// <summary>What the analyzer knows about the code that a call runs.</summary>
internal enum CalleeKind
{
    /// <summary>
    ///     A method of the library. The operator table has its rule, and the
    ///     analyzer does not read its body.
    /// </summary>
    Library,

    /// <summary>
    ///     Code of this compilation, or a delegate whose function the evaluator
    ///     knows. The walk reads its body.
    /// </summary>
    Body,

    /// <summary>
    ///     Code with no body to read, in an assembly that does not reference the
    ///     library. It cannot sample a cell. It can keep a value, give it back, or
    ///     give it to a function of the caller.
    /// </summary>
    Mover,

    /// <summary>
    ///     Code that the analyzer cannot read and that can know the library, a
    ///     delegate of an unknown source, or a body that is too deep. The walk
    ///     reports a pending value that goes to it with SF1003.
    /// </summary>
    Unknown
}
