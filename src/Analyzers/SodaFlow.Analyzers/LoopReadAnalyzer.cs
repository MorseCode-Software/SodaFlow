using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

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
                ?.Expression is not { } written)
        {
            return;
        }

        if (BlockOf(written: written, model: model) is not (var placeholder, var body))
        {
            // A block with no body to read gets no check. The report says so, as for a callee that the
            // walk cannot see. A function with no parameter cannot read the placeholder. A block of the
            // library can give the function of its caller to its own block. That is not such a block,
            // because the walk checks the function at the call of the caller.
            if (FunctionIn(written: written, model: model) is not AnonymousFunctionExpressionSyntax
                && !SymbolEqualityComparer.Default.Equals(x: method.ContainingAssembly, y: model.Compilation.Assembly))
            {
                ITypeSymbol? looped =
                    (model.GetTypeInfo(written).ConvertedType as INamedTypeSymbol)?.DelegateInvokeMethod?.Parameters
                    .FirstOrDefault()
                    ?.Type;

                report(
                    Diagnostic.Create(
                        descriptor: Diagnostics.Escape,
                        location: written.GetLocation(),
                        (looped ?? method.ContainingType).ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        written.ToString()));
            }

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
    ///     body to read. Parentheses, a cast, and a creation of a delegate do not
    ///     hide the function: see <see cref="FunctionIn" />.
    /// </summary>
    private static (ISymbol Placeholder, SyntaxNode Body)? BlockOf(ExpressionSyntax written, SemanticModel model)
    {
        written = FunctionIn(written: written, model: model);

        if (written is IdentifierNameSyntax
            && model.GetSymbolInfo(written).Symbol is ILocalSymbol { Type.TypeKind: TypeKind.Delegate } delegateLocal)
        {
            if (FunctionAt(local: delegateLocal, use: written, model: model) is not { } assigned)
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

        // An object can override a virtual method, and the walk does not know the class of the object.
        // A name through base, or on a value of a sealed type, gives the method that it binds to. No
        // class can override a sealed override.
        if (named is { IsVirtual: true } or { IsOverride: true, IsSealed: false }
            && written is not MemberAccessExpressionSyntax { Expression: BaseExpressionSyntax }
            && (written is MemberAccessExpressionSyntax access
                    ? model.GetTypeInfo(access.Expression).Type
                    : named.ContainingType)
                is not { IsSealed: true })
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
    ///     The function that an argument gives to a block. Parentheses, a cast,
    ///     and a creation of a delegate from one function each give that
    ///     function.
    /// </summary>
    private static ExpressionSyntax FunctionIn(ExpressionSyntax written, SemanticModel model)
    {
        ExpressionSyntax function = LoopStateEvaluator.Unwrapped(written);

        while (function is ObjectCreationExpressionSyntax { ArgumentList.Arguments: { Count: 1 } only } creation
               && model.GetTypeInfo(creation).Type is { TypeKind: TypeKind.Delegate })
        {
            function = LoopStateEvaluator.Unwrapped(only[0].Expression);
        }

        return function;
    }

    /// <summary>
    ///     The lambda, the anonymous method, or the name of a method that a local
    ///     has at a use. It is the value of the declaration, or of the last
    ///     assignment before the statement of the use. Null when the code can
    ///     give the local a different value there, by a different assignment or
    ///     through a reference.
    /// </summary>
    /// <remarks>
    ///     A value that the walk knows comes from an assignment that is a
    ///     statement of the block of the declaration. A label can run such a
    ///     statement again after the use, and a function around the use can run
    ///     after a later statement. A write after the statement of the use cannot
    ///     change the local before the use. An assignment statement that holds
    ///     the use outside a function cannot give a lambda. Thus, it gives no
    ///     function, as each other value with no lambda does.
    /// </remarks>
    private static ExpressionSyntax? FunctionAt(ISymbol local, SyntaxNode use, SemanticModel model)
    {
        if (local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                is not VariableDeclaratorSyntax { Initializer.Value: var value } declarator
            || declarator.Ancestors().FirstOrDefault(static node => node is BlockSyntax or SwitchSectionSyntax)
                is not { } scope)
        {
            return null;
        }

        // Each section of a switch can use a local that one section declares.
        (SyntaxList<StatementSyntax> statements, SyntaxNode uses) =
            scope is SwitchSectionSyntax { Parent: { } whole } section
                ? (section.Statements, whole)
                : (((BlockSyntax)scope).Statements, scope);

        SyntaxNode[] around = [.. use.Ancestors().TakeWhile(node => node != scope)];

        // The statement of the block that holds the use, when the walk knows the order of the code.
        TextSpan? at =
            around.OfType<StatementSyntax>().LastOrDefault() is { } statement
            && !around.Any(static node => node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
            && !statements.Any(static each => each is LabeledStatementSyntax)
                ? statement.Span
                : null;

        foreach (IdentifierNameSyntax name in uses.DescendantNodes()
                     .OfType<IdentifierNameSyntax>()
                     .Where(name => name.Identifier.ValueText == local.Name))
        {
            bool writes =
                name.Parent switch
                {
                    AssignmentExpressionSyntax assignment => assignment.Left == name,
                    ArgumentSyntax argument => !argument.RefKindKeyword.IsKind(SyntaxKind.None)
                                               || IsDeconstructedInto(argument),
                    RefExpressionSyntax => true,
                    _ => false
                };

            if (!writes
                || name.SpanStart >= at?.End
                || !SymbolEqualityComparer.Default.Equals(x: model.GetSymbolInfo(name).Symbol, y: local))
            {
                continue;
            }

            if (at == null
                || ValueGiven(name) is not
                {
                    Assignment.Parent: ExpressionStatementSyntax { Parent: var block }, Value: var given
                }
                || block != scope)
            {
                return null;
            }

            // The names come in the order of the code, thus the last one gives the value.
            value = given;
        }

        // A name of a method or of a local function gives that function. BlockOf reads it.
        return FunctionIn(written: value, model: model)
            is (AnonymousFunctionExpressionSyntax or SimpleNameSyntax or MemberAccessExpressionSyntax)
            and var function
                ? function
                : null;
    }

    // True for an element of a tuple on the left of a deconstruction, also of a tuple in a tuple. A
    // foreach can deconstruct only into new locals.
    private static bool IsDeconstructedInto(SyntaxNode argument)
    {
        for (SyntaxNode? element = argument; element is ArgumentSyntax { Parent: TupleExpressionSyntax tuple };)
        {
            if (tuple.Parent is AssignmentExpressionSyntax { Left: var left } && left == tuple)
            {
                return true;
            }

            element = tuple.Parent;
        }

        return false;
    }

    // The value that a simple assignment gives a name. A deconstruction of a tuple literal into a
    // tuple of names gives each name the element at the same position.
    private static (AssignmentExpressionSyntax Assignment, ExpressionSyntax Value)? ValueGiven(SyntaxNode name) =>
        name.Parent switch
        {
            AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression } assigned =>
                (assigned, assigned.Right),
            ArgumentSyntax
                {
                    Parent: TupleExpressionSyntax
                    {
                        Parent: AssignmentExpressionSyntax
                        {
                            RawKind: (int)SyntaxKind.SimpleAssignmentExpression,
                            Right: TupleExpressionSyntax right
                        } assigned
                    } left
                } element
                when assigned.Left == left && right.Arguments.Count == left.Arguments.Count =>
                (assigned, right.Arguments[left.Arguments.IndexOf(element)].Expression),
            _ => null
        };

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
