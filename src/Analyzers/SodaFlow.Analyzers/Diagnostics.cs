using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

/// <summary>
///     The diagnostics that <see cref="LoopReadAnalyzer" /> and
///     <see cref="ListenerAnalyzer" /> report.
/// </summary>
internal static class Diagnostics
{
    private const string Category = "SodaFlow.Loops";

    private const string ListenersCategory = "SodaFlow.Listeners";

    public static readonly DiagnosticDescriptor ReadBeforeClose =
        new(
            id: "SF1001",
            title: "A looped value is read before its loop closes",
            messageFormat:
            "'{0}' reads the current value of looped value '{1}' before the loop closes, which throws at run time. "
            + "Use SampleLazy, or move the read into a callback.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor LinkedClose =
        new(
            id: "SF1002",
            title: "A loop closes with a value that is linked to its own placeholder",
            messageFormat:
            "The definition of loop '{0}' is linked to '{0}' with no sampled hold and no Operational.Defer between "
            + "them. That is a dependency cycle. It throws DependencyCycleException at the close, or later when a "
            + "switch selects the loop.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor Escape =
        new(
            id: "SF1003",
            title: "A looped value leaves the library before its loop closes",
            messageFormat:
            "Looped value '{0}' is passed to '{1}', which the analyzer cannot see into. Write AsCell() or "
            + "AsBehavior() at the argument to say that the callee does not read it before the loop closes.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MemberReadInCallee =
        new(
            id: "SF1005",
            title: "A member built from a looped value is read before its loop closes",
            messageFormat:
            "'{0}' reads '{1}', which was built from looped value '{2}', before the loop closes. The read throws at run time.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PendingClose =
        new(
            id: "SF1006",
            title: "A loop closes with a value whose initial value reads its own placeholder",
            messageFormat:
            "The initial value of the definition of loop '{0}' reads '{0}', so the loop reads itself at the close. "
            + "The close throws at run time.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ReadInCallee =
        new(
            id: "SF1004",
            title: "A callee reads a looped value before its loop closes",
            messageFormat:
            "'{0}' samples its parameter '{1}'. Looped value '{2}' is passed to it before the loop closes.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DroppedListener =
        new(
            id: "SF2001",
            title: "Nothing holds the listener that Listen or ListenOnce returns",
            messageFormat:
            "Nothing holds the listener that '{0}' returns, thus a garbage collection can stop it with no message. "
            + "Keep it in a field, or in a local that later code reads.",
            category: ListenersCategory,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);
}
