using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace SodaFlow.Analyzers;

/// <summary>
///     Finds each call to <c>Listen</c> or <c>ListenOnce</c> of the library
///     whose listener nothing keeps.
/// </summary>
/// <remarks>
///     <para>
///         The listener of such a call holds the handler, and the stream holds the
///         listener only through a weak reference. Thus, when nothing holds the
///         listener, a garbage collection can stop it, and nothing says so.
///     </para>
///     <para>
///         The rule reports a call whose result the code drops: a statement of the
///         call alone, or a discard. It also reports a call whose result goes into a
///         local that no later code reads. The flow analysis of the compiler says
///         which code can read the value. A local that a lambda or a local function
///         captures counts as read, because the function can read it at a later
///         time. The rule does not report a field, a return, an argument, or an
///         element. It does not see an owner that nothing holds, such as an object
///         whose field has the listener.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
// ReSharper disable once InheritdocConsiderUsage - This class has its own summary, which says more than the base class does.
public sealed class ListenerAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> ListenNames = ImmutableHashSet.Create("Listen", "ListenOnce");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.DroppedListener);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static start =>
        {
            SodaFlowSymbols symbols = new(start.Compilation);

            // Code that does not reference the library has no listener.
            if (!symbols.HasLibrary)
            {
                return;
            }

            start.RegisterOperationAction(
                action: operation => AnalyzeCall(context: operation, symbols: symbols),
                OperationKind.Invocation);
        });
    }

    private static void AnalyzeCall(OperationAnalysisContext context, SodaFlowSymbols symbols)
    {
        IInvocationOperation call = (IInvocationOperation)context.Operation;
        IMethodSymbol method = call.TargetMethod;

        if (!ListenNames.Contains(method.Name) || !IsPublicListen(method: method, symbols: symbols) || !IsDropped(call))
        {
            return;
        }

        context.ReportDiagnostic(
            Diagnostic.Create(
                descriptor: Diagnostics.DroppedListener,
                location: NameOf(call.Syntax),
                messageArgs: method.Name));
    }

    // A public method of the library. Core has internal methods with the same names, which link
    // the operators of a graph, and whose results the library keeps in its own way.
    private static bool IsPublicListen(ISymbol method, SodaFlowSymbols symbols) =>
        symbols.IsSodaFlow(method)
        && method.DeclaredAccessibility == Accessibility.Public
        && method.ContainingType.DeclaredAccessibility == Accessibility.Public;

    // True when nothing keeps the result of the call. The use of the result is the operation above
    // the call, past an implicit conversion and a conditional access.
    private static bool IsDropped(IOperation call)
    {
        IOperation result = call;
        IOperation? use = call.Parent;

        while (use is IConversionOperation { IsImplicit: true }
               || (use is IConditionalAccessOperation access && ReferenceEquals(objA: access.WhenNotNull, objB: result)))
        {
            result = use;
            use = use.Parent;
        }

        return use switch
        {
            IExpressionStatementOperation => true,
            ISimpleAssignmentOperation { Target: IDiscardOperation } => true,
            ISimpleAssignmentOperation { Target: ILocalReferenceOperation target, Parent: IExpressionStatementOperation statement } =>
                IsNotReadLater(local: target.Local, statement: statement.Syntax, call: call),
            IVariableInitializerOperation { Parent: IVariableDeclaratorOperation declarator } =>
                declarator.Parent?.Parent is IVariableDeclarationGroupOperation { Syntax: LocalDeclarationStatementSyntax declaration }
                && IsNotReadLater(local: declarator.Symbol, statement: declaration, call: call),
            _ => false
        };
    }

    // True when no code after the statement can read the value that the statement puts in the
    // local, and no function captures the local. The statement is the region of the flow analysis.
    // An assignment in the body of an expression lambda is not a statement, thus it gives false.
    // Its local is a capture of that lambda.
    private static bool IsNotReadLater(ISymbol local, SyntaxNode statement, IOperation call)
    {
        if (statement is not StatementSyntax region || call.SemanticModel?.AnalyzeDataFlow(region) is not { Succeeded: true } flow)
        {
            return false;
        }

        return !flow.DataFlowsOut.Contains(local) && !flow.ReadInside.Contains(local) && !IsCaptured(local: local, call: call);
    }

    // A function captures a local when it reads or writes the local, and the declaration of the
    // local is outside it. The function can run after the statement, thus each capture counts as a
    // read.
    private static bool IsCaptured(ISymbol local, IOperation call)
    {
        IOperation root = call;

        while (root.Parent != null)
        {
            root = root.Parent;
        }

        SyntaxReference? declared = local.DeclaringSyntaxReferences.FirstOrDefault();

        return root.Descendants()
            .Where(static operation => operation is IAnonymousFunctionOperation or ILocalFunctionOperation)
            .Where(function => declared == null || !function.Syntax.Span.Contains(declared.Span))
            .Any(function =>
                function.Descendants()
                    .OfType<ILocalReferenceOperation>()
                    .Any(reference => SymbolEqualityComparer.Default.Equals(x: reference.Local, y: local)));
    }

    // The name of the method in the call, so that the report marks only the name. Code of a consumer
    // calls an extension method through a member access or a conditional access. The call itself is
    // the place for each other form.
    private static Location NameOf(SyntaxNode call) =>
        (call is InvocationExpressionSyntax { Expression: var callee }
            ? (callee as MemberAccessExpressionSyntax)?.Name ?? (callee as MemberBindingExpressionSyntax)?.Name ?? call
            : call)
        .GetLocation();
}
