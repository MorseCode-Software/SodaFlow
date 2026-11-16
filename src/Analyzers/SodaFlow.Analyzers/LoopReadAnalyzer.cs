using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SodaFlow.Analyzers;

/// <summary>
///     Finds each read of a looped cell or behavior before its loop closes,
///     and each loop whose definition is linked to its own placeholder.
/// </summary>
/// <remarks>
///     A loop block hands its function a placeholder, and the function is the
///     code that runs while the loop is open. This analyzer checks that code.
///     The function can be a lambda, an anonymous method, or a method of this
///     compilation that the block names. The analyzer does not check an explicit
///     loop. Only the block methods of the SodaFlow assemblies make one, and
///     each passes the placeholder directly to the block of the caller. See
///     <see cref="LoopStateEvaluator" /> for the model of the operators.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
// ReSharper disable once InheritdocConsiderUsage - This class has its own summary, which says more than the base class does.
public sealed class LoopReadAnalyzer : DiagnosticAnalyzer
{
    // The most reads of one block. A read can give a sink content that a send of a sink gets in
    // the next read. Each read after the first one is for such a chain of sinks.
    private const int MaxReads = 3;

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            Diagnostics.ReadBeforeClose,
            Diagnostics.LinkedClose,
            Diagnostics.Escape,
            Diagnostics.ReadInCallee,
            Diagnostics.MemberReadInCallee,
            Diagnostics.PendingClose);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static start =>
        {
            SodaFlowSymbols symbols = new(start.Compilation);

            // Code that does not reference the library has no loop block.
            if (!symbols.HasLibrary)
            {
                return;
            }

            start.RegisterSyntaxNodeAction(
                action: node => AnalyzeLoopBlock(
                    invocation: (InvocationExpressionSyntax)node.Node,
                    model: node.SemanticModel,
                    symbols: symbols,
                    report: node.ReportDiagnostic,
                    cancellation: node.CancellationToken),
                SyntaxKind.InvocationExpression);
        });
    }

    /// <summary>
    ///     Reads one invocation, and reports what the walk finds when it is a loop
    ///     block.
    /// </summary>
    /// <param name="invocation">The invocation.</param>
    /// <param name="model">The semantic model of the file of the invocation.</param>
    /// <param name="symbols">The symbols of the library in the compilation.</param>
    /// <param name="report">The action that gets each diagnostic, at the time that the walk finds it.</param>
    /// <param name="cancellation">The token of the compiler. The walk throws when the token is canceled.</param>
    internal static void AnalyzeLoopBlock(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        SodaFlowSymbols symbols,
        Action<Diagnostic> report,
        CancellationToken cancellation)
    {
        // The name of the method is in the syntax. This action runs for each invocation in the
        // compilation, thus it asks for the symbol only where the name is the name of a block.
        SimpleNameSyntax? name =
            invocation.Expression switch
            {
                MemberAccessExpressionSyntax access => access.Name,
                MemberBindingExpressionSyntax binding => binding.Name,
                SimpleNameSyntax simple => simple,
                _ => null
            };

        if (name == null
            || !SodaFlowSymbols.IsLoopBlockName(name.Identifier.ValueText)
            || model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !symbols.IsLoopBlock(method))
        {
            return;
        }

        // The block is the argument of the one delegate parameter. A public block takes only that
        // argument. A Core block also takes the transaction and the state of the block.
        IMethodSymbol definition = method.OriginalDefinition;

        if (invocation.ArgumentList.Arguments
                .Where((argument, position) =>
                    SodaFlowSymbols.ParameterOf(callee: definition, argument: argument, position: position)?.Type
                        .TypeKind
                    == TypeKind.Delegate)
                .FirstOrDefault()
                ?.Expression is not { } written
            || BlockOf(written: written, model: model) is not (var placeholder, var body))
        {
            return;
        }

        string looper = method.ContainingType.OriginalDefinition.Name;

        LoopState placeholderState =
            looper switch
            {
                "StreamLooper" => LoopState.StreamPlaceholder,
                "ForwardReference" => ForwardReferenceState(
                    value: method.ContainingType.TypeArguments[0],
                    symbols: symbols,
                    compilation: model.Compilation.Assembly),
                _ => LoopState.PendingPlaceholder
            };

        bool withCaptures = method.Name is "WithCaptures" or "WithCapturesImpl";

        ImmutableArray<ExpressionSyntax> closings =
            (body is ExpressionSyntax expression ? [expression] : LoopStateEvaluator.ReturnExpressions(body))
            .Select(e => withCaptures ? LoopedElement(e) : e)
            .ToImmutableArray();

        // A send can give a sink content after the code made a value from the sink. The next read
        // gives each sink that content from the start.
        LoopRegion? before = null;

        for (int read = 1;; read++)
        {
            LoopRegion region =
                new(
                    compilation: model.Compilation,
                    symbols: symbols,
                    placeholder: placeholder,
                    placeholderState: placeholderState,
                    root: body,
                    report: report,
                    cancellation: cancellation,
                    readBefore: before);

            if (region.Analyze(closings: closings, withCaptures: withCaptures, canReadAgain: read < MaxReads))
            {
                return;
            }

            before = region;
        }
    }

    /// <summary>
    ///     The state of the placeholder of a forward reference. A constant cell
    ///     closes the loop, thus the placeholder itself has no links after the
    ///     close. The value that it holds is the object that the block returns.
    ///     A member of that object has the value that the block gives it.
    ///     Thus, a read back of the value or of a member has that value as the
    ///     origin of its links.
    /// </summary>
    private static LoopState ForwardReferenceState(
        ITypeSymbol value,
        SodaFlowSymbols symbols,
        IAssemblySymbol compilation)
    {
        ImmutableDictionary<ISymbol, LoopState>.Builder members =
            ImmutableDictionary.CreateBuilder<ISymbol, LoopState>(SymbolEqualityComparer.Default);

        IEnumerable<ITypeSymbol> types =
            value.TypeKind == TypeKind.Interface ? value.AllInterfaces.Prepend(value) : BaseTypes(value);

        foreach (ISymbol member in types.SelectMany(static type => type.GetMembers()))
        {
            ITypeSymbol? type =
                member switch
                {
                    IFieldSymbol { IsStatic: false, IsImplicitlyDeclared: false } field => field.Type,
                    IPropertySymbol { IsStatic: false, IsIndexer: false } property => property.Type,
                    _ => null
                };

            if (type != null && symbols.CanHoldState(type: type, compilation: compilation))
            {
                members[member.OriginalDefinition] = LoopState.ReadBackOf(member.OriginalDefinition);
            }
        }

        // A value of the library is a node of the graph, which can have links. A different value has
        // only its members.
        LoopState itself =
            value is INamedTypeSymbol library && symbols.IsSodaFlow(library)
                ? LoopState.ReadBackOf(value)
                : LoopState.None.ReadingBack(ImmutableArray.Create<ISymbol>(value));

        return LoopState.PendingPlaceholder.WithContent(
            members.Count > 0 && value is INamedTypeSymbol named
                ? itself.WithMembers(Construction.Of(members: members.ToImmutable(), type: named.OriginalDefinition))
                : itself);

        static IEnumerable<ITypeSymbol> BaseTypes(ITypeSymbol type)
        {
            for (ITypeSymbol? each = type; each != null; each = each.BaseType)
            {
                yield return each;
            }
        }
    }

    /// <summary>
    ///     The placeholder and the body of a block. The block can be a lambda, an
    ///     anonymous method, or the name of a method or a local function of this
    ///     compilation. It can also be a local of a delegate type that only its
    ///     declaration gives a value. A delegate from a different source has no
    ///     body to read.
    /// </summary>
    private static (ISymbol Placeholder, SyntaxNode Body)? BlockOf(ExpressionSyntax written, SemanticModel model)
    {
        if (written is IdentifierNameSyntax
            && model.GetSymbolInfo(written).Symbol is ILocalSymbol { Type.TypeKind: TypeKind.Delegate } delegateLocal)
        {
            if (OnlyFunctionOf(local: delegateLocal, model: model) is not { } assigned)
            {
                return null;
            }

            written = assigned;
        }

        if (written is AnonymousFunctionExpressionSyntax function)
        {
            ParameterSyntax? parameter =
                function switch
                {
                    SimpleLambdaExpressionSyntax simple => simple.Parameter,
                    ParenthesizedLambdaExpressionSyntax lambda => lambda.ParameterList.Parameters.FirstOrDefault(),
                    AnonymousMethodExpressionSyntax anonymous => anonymous.ParameterList?.Parameters.FirstOrDefault(),
                    _ => null
                };

            return parameter != null && model.GetDeclaredSymbol(parameter) is { } declared
                ? (declared, function.Body)
                : null;
        }

        if (model.GetSymbolInfo(written).Symbol is not IMethodSymbol named)
        {
            return null;
        }

        // The definition, because its body binds to its own parameters. For an extension method on
        // a value, that value is the first parameter and the placeholder is the second.
        IMethodSymbol method = (named.ReducedFrom ?? named).OriginalDefinition;
        int position = named.ReducedFrom != null ? 1 : 0;

        SyntaxNode? body =
            method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() switch
            {
                MethodDeclarationSyntax declaration =>
                    declaration.Body ?? (SyntaxNode?)declaration.ExpressionBody?.Expression,
                LocalFunctionStatementSyntax local => local.Body ?? (SyntaxNode?)local.ExpressionBody?.Expression,
                _ => null
            };

        return body != null && position < method.Parameters.Length ? (method.Parameters[position], body) : null;
    }

    /// <summary>
    ///     The lambda or the anonymous method that the declaration of a local
    ///     gives it. Null when the code can give the local a different value, by
    ///     an assignment or through a reference.
    /// </summary>
    private static AnonymousFunctionExpressionSyntax? OnlyFunctionOf(ISymbol local, SemanticModel model)
    {
        if (local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                is not VariableDeclaratorSyntax
                {
                    Initializer.Value: AnonymousFunctionExpressionSyntax function
                } declarator
            || declarator.FirstAncestorOrSelf<BlockSyntax>() is not { } scope)
        {
            return null;
        }

        bool changes =
            scope.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Where(name => name.Identifier.ValueText == local.Name)
                .Any(name =>
                    name.Parent switch
                    {
                        AssignmentExpressionSyntax assignment => assignment.Left == name,
                        ArgumentSyntax argument => !argument.RefKindKeyword.IsKind(SyntaxKind.None),
                        RefExpressionSyntax => true,
                        _ => false
                    }
                    && SymbolEqualityComparer.Default.Equals(x: model.GetSymbolInfo(name).Symbol, y: local));

        return changes ? null : function;
    }

    /// <summary>
    ///     The element of a returned tuple literal that closes the loop. C# converts
    ///     a tuple to the result type of the block by position and not by name,
    ///     thus it is the first element. A different value stays whole, and the
    ///     region takes its first part.
    /// </summary>
    private static ExpressionSyntax LoopedElement(ExpressionSyntax returned) =>
        returned is TupleExpressionSyntax { Arguments: { Count: > 0 } elements }
            ? elements[0].Expression
            : returned;
}
