using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

/// <summary>
///     Walks the code that runs while a loop is open and reports each read of a
///     pending value. A lambda given to a library operator runs after the close, and
///     the walk does not enter it. The walk enters all other lambdas.
/// </summary>
internal sealed class SinkWalker(in LoopRegion region, in LoopStateEvaluator evaluator) : CSharpSyntaxWalker
{
    private readonly LoopStateEvaluator evaluator = evaluator;
    private readonly LoopRegion region = region;

    private readonly Dictionary<ISymbol, AnonymousFunctionExpressionSyntax> storedLambdas =
        new(SymbolEqualityComparer.Default);

    private readonly HashSet<AnonymousFunctionExpressionSyntax> walkedLambdas = [];

    public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
    {
        SemanticModel model = this.region.ModelFor(node);

        foreach (VariableDeclaratorSyntax declarator in node.Declaration.Variables)
        {
            if (declarator.Initializer == null)
            {
                continue;
            }

            // A lambda kept in a local runs when the local is invoked, and the walk enters it then.
            if (declarator.Initializer.Value is AnonymousFunctionExpressionSyntax stored
                && model.GetDeclaredSymbol(declarator) is { } holder)
            {
                this.storedLambdas[holder] = stored;

                continue;
            }

            this.Visit(declarator.Initializer.Value);

            if (model.GetDeclaredSymbol(declarator) is { } local)
            {
                this.evaluator.Declare(local, this.evaluator.Eval(declarator.Initializer.Value));
            }
        }
    }

    public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        // An event subscription runs its lambda at an event, after the close.
        if (node.RawKind == (int)SyntaxKind.AddAssignmentExpression && node.Right is AnonymousFunctionExpressionSyntax)
        {
            this.Visit(node.Left);

            return;
        }

        base.VisitAssignmentExpression(node);

        if (node.Left is IdentifierNameSyntax
            && this.region.ModelFor(node).GetSymbolInfo(node.Left).Symbol is ILocalSymbol local)
        {
            this.evaluator.Declare(local, this.evaluator.Eval(node.Right));
        }
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        SemanticModel model = this.region.ModelFor(node);

        // A stored lambda runs here, through the delegate's Invoke.
        if (node.Expression is IdentifierNameSyntax
            && model.GetSymbolInfo(node.Expression).Symbol is { } holder
            && this.storedLambdas.TryGetValue(holder, out AnonymousFunctionExpressionSyntax? stored)
            && this.walkedLambdas.Add(stored))
        {
            this.Visit(stored.Body);
        }

        if (model.GetSymbolInfo(node).Symbol is not IMethodSymbol method)
        {
            base.VisitInvocationExpression(node);

            return;
        }

        this.Visit(node.Expression);

        bool library = SodaFlowSymbols.IsSodaFlow(method);
        bool runsNow = LoopStateEvaluator.RunsNow(method);
        bool defers = (library && !runsNow) || RunsOnAnotherThread(method.ContainingType);

        foreach (ArgumentSyntax argument in node.ArgumentList.Arguments)
        {
            if (argument.Expression is AnonymousFunctionExpressionSyntax && defers)
            {
                continue;
            }

            this.Visit(argument);
        }

        ExpressionSyntax? receiver = LoopStateEvaluator.ReceiverOf(invocation: node, method: method);

        // CanExecute is an ICommand member, which a bindable action implements. Thus, the read
        // members count on a library receiver too, and not only on a library method.
        bool libraryReceiver = receiver != null && SodaFlowSymbols.IsCarrierType(model.GetTypeInfo(receiver).Type);

        if (library || libraryReceiver)
        {
            if (SodaFlowSymbols.ReadMembers.Contains(LoopStateEvaluator.OperatorName(method))
                && receiver != null
                && this.evaluator.Eval(receiver).Pending)
            {
                this.region.Report(
                    Diagnostic.Create(
                        descriptor: Diagnostics.ReadBeforeClose,
                        location: node.GetLocation(),
                        method.Name,
                        this.region.PlaceholderName));
            }

            if (library)
            {
                return;
            }
        }

        IMethodSymbol definition = method.ReducedFrom ?? method;

        if (method.ReducedFrom != null && receiver != null && definition.Parameters.Length > 0)
        {
            this.CheckEscape(
                callee: definition,
                parameter: definition.Parameters[0],
                argument: receiver,
                followed: this.evaluator.FollowedParametersOf(node));
        }

        this.CheckEscapes(
            callee: definition,
            arguments: node.ArgumentList.Arguments,
            offset: method.ReducedFrom != null ? 1 : 0,
            followed: this.evaluator.FollowedParametersOf(node));

        // A method of an object whose construction was followed: its body can read a member that
        // the construction made pending.
        if (receiver != null && method.ReducedFrom == null && this.evaluator.ConstructionOf(receiver) is { } construction)
        {
            this.CheckMemberRead(member: method, construction: construction, location: node);
        }
    }

    public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        this.VisitCreationArguments(node, node.ArgumentList);
        this.CheckCreation(node, node.ArgumentList);
    }

    /// <summary>
    ///     The arguments of a construction, without a lambda that a thread or a timer
    ///     runs after the close.
    /// </summary>
    private void VisitCreationArguments(ExpressionSyntax node, ArgumentListSyntax? arguments)
    {
        if (arguments == null)
        {
            return;
        }

        bool defers =
            this.region.ModelFor(node).GetSymbolInfo(node).Symbol is IMethodSymbol constructor
            && RunsOnAnotherThread(constructor.ContainingType);

        foreach (ArgumentSyntax argument in arguments.Arguments)
        {
            if (argument.Expression is AnonymousFunctionExpressionSyntax && defers)
            {
                continue;
            }

            this.Visit(argument);
        }
    }

    /// <summary>
    ///     A type whose lambdas run on a different thread or after the block, so not
    ///     in the block.
    /// </summary>
    private static bool RunsOnAnotherThread(INamedTypeSymbol type) =>
        type.ContainingNamespace?.ToDisplayString() is "System.Threading" or "System.Threading.Tasks"
        && type.Name is "Thread" or "Task" or "TaskFactory" or "ThreadPool" or "Timer" or "SynchronizationContext";

    public override void VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
    {
        this.VisitCreationArguments(node, node.ArgumentList);
        this.CheckCreation(node, node.ArgumentList);
    }

    public override void VisitReturnStatement(ReturnStatementSyntax node)
    {
        // A lambda in a returned capture runs after the close, when the caller invokes it.
        if (node.Expression != null)
        {
            this.VisitWithoutLambdas(node.Expression);
        }
    }

    private void VisitWithoutLambdas(SyntaxNode node)
    {
        if (node is AnonymousFunctionExpressionSyntax)
        {
            return;
        }

        if (node is TupleExpressionSyntax or ArgumentSyntax or ParenthesizedExpressionSyntax)
        {
            foreach (SyntaxNode child in node.ChildNodes())
            {
                this.VisitWithoutLambdas(child);
            }

            return;
        }

        this.Visit(node);
    }

    public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        base.VisitMemberAccessExpression(node);

        if (this.region.ModelFor(node).GetSymbolInfo(node).Symbol is not IPropertySymbol property)
        {
            return;
        }

        if (SodaFlowSymbols.IsSodaFlow(property)
            && SodaFlowSymbols.ReadMembers.Contains(property.Name)
            && this.evaluator.Eval(node.Expression).Pending)
        {
            this.region.Report(
                Diagnostic.Create(
                    descriptor: Diagnostics.ReadBeforeClose,
                    location: node.GetLocation(),
                    property.Name,
                    this.region.PlaceholderName));
        }

        if (!SodaFlowSymbols.IsSodaFlow(property) && this.evaluator.ConstructionOf(node.Expression) is { } construction)
        {
            this.CheckMemberRead(member: property, construction: construction, location: node);
        }
    }

    /// <summary>
    ///     A method or a computed property of a followed object, whose body samples a
    ///     member that the construction made pending.
    /// </summary>
    private void CheckMemberRead(ISymbol member, Construction construction, SyntaxNode location)
    {
        ISymbol? read =
            this.PendingMemberReadBy(
                member: member,
                construction: construction,
                visited: new HashSet<ISymbol>(SymbolEqualityComparer.Default),
                depth: 0);

        if (read == null)
        {
            return;
        }

        this.region.Report(
            Diagnostic.Create(
                descriptor: Diagnostics.MemberReadInCallee,
                location: location.GetLocation(),
                member.Name,
                read.Name,
                this.region.PlaceholderName));
    }

    /// <summary>
    ///     The pending member that a method or property of a followed object reads.
    ///     The read can be in its own body, or in a member of the same object that it
    ///     calls, up to <see cref="LoopStateEvaluator.MaxInlineDepth" /> calls deep.
    /// </summary>
    private ISymbol? PendingMemberReadBy(ISymbol member, Construction construction, HashSet<ISymbol> visited, int depth)
    {
        if (depth >= LoopStateEvaluator.MaxInlineDepth || !visited.Add(member.OriginalDefinition))
        {
            return null;
        }

        SyntaxNode? body = BodyOf(member);

        if (body == null)
        {
            return null;
        }

        SemanticModel model = this.region.ModelFor(body);

        foreach (SyntaxNode node in body.DescendantNodesAndSelf(
                     descendIntoChildren: static n => n is not AnonymousFunctionExpressionSyntax))
        {
            switch (node)
            {
                case MemberAccessExpressionSyntax access
                    when SodaFlowSymbols.ReadMembers.Contains(access.Name.Identifier.ValueText):
                {
                    ExpressionSyntax target =
                        access.Expression is MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } own
                            ? own
                            : access.Expression;

                    if (model.GetSymbolInfo(target).Symbol is IPropertySymbol or IFieldSymbol
                        && construction.StateOf(model.GetSymbolInfo(target).Symbol!).Pending)
                    {
                        return model.GetSymbolInfo(target).Symbol;
                    }

                    break;
                }
                case InvocationExpressionSyntax { Expression: IdentifierNameSyntax or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } } call
                    when model.GetSymbolInfo(call).Symbol is IMethodSymbol next && IsMemberOfSameType(next, member):
                {
                    ISymbol? found = this.PendingMemberReadBy(next, construction, visited, depth + 1);

                    if (found != null)
                    {
                        return found;
                    }

                    break;
                }
                case IdentifierNameSyntax or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax }
                    when node.Parent is not InvocationExpressionSyntax
                    && model.GetSymbolInfo(node).Symbol is IPropertySymbol next
                    && IsMemberOfSameType(next, member):
                {
                    ISymbol? found = this.PendingMemberReadBy(next, construction, visited, depth + 1);

                    if (found != null)
                    {
                        return found;
                    }

                    break;
                }
            }
        }

        return null;
    }

    private static bool IsMemberOfSameType(ISymbol candidate, ISymbol member) =>
        SymbolEqualityComparer.Default.Equals(
            candidate.ContainingType?.OriginalDefinition,
            member.ContainingType?.OriginalDefinition);

    /// <summary>The body of a method, or of the getter of a property.</summary>
    private static SyntaxNode? BodyOf(ISymbol member)
    {
        SyntaxNode? declaration = member.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();

        return declaration switch
        {
            MethodDeclarationSyntax m => m.Body ?? (SyntaxNode?)m.ExpressionBody,
            PropertyDeclarationSyntax { ExpressionBody: { } e } => e,
            PropertyDeclarationSyntax { AccessorList: { } accessors } =>
                accessors.Accessors
                    .Where(static a => a.Keyword.ValueText == "get")
                    .Select(static a => a.Body ?? (SyntaxNode?)a.ExpressionBody)
                    .FirstOrDefault(static b => b != null),
            _ => null
        };
    }

    private void CheckCreation(ExpressionSyntax node, ArgumentListSyntax? arguments)
    {
        if (arguments == null
            || this.region.ModelFor(node).GetSymbolInfo(node).Symbol is not IMethodSymbol constructor
            || SodaFlowSymbols.IsSodaFlow(constructor))
        {
            return;
        }

        this.CheckEscapes(
            callee: constructor,
            arguments: arguments.Arguments,
            offset: 0,
            followed: this.evaluator.ConstructionOf(node)?.FollowedParameters);
    }

    private void CheckEscapes(
        IMethodSymbol callee,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        int offset,
        ImmutableHashSet<ISymbol>? followed)
    {
        for (int i = 0; i < arguments.Count; i++)
        {
            ArgumentSyntax argument = arguments[i];
            IParameterSymbol? parameter = SodaFlowSymbols.ParameterOf(callee: callee, argument: argument, position: i + offset);

            if (parameter != null)
            {
                this.CheckEscape(callee: callee, parameter: parameter, argument: argument.Expression, followed: followed);
            }
        }
    }

    /// <summary>
    ///     A pending value given to code that is not in the library. The body of the
    ///     callee answers the question when it is in this compilation. An
    ///     <c>AsCell()</c> at the argument is the assertion of the caller and reports
    ///     nothing.
    /// </summary>
    private void CheckEscape(
        IMethodSymbol callee,
        IParameterSymbol parameter,
        ExpressionSyntax argument,
        ImmutableHashSet<ISymbol>? followed)
    {
        // Only a library value escapes. An object of the caller that holds a pending member goes
        // where the caller sends it, as it does with the looped types. The walk checks its reads
        // through its construction.
        if (!this.evaluator.Eval(argument).Pending
            || !SodaFlowSymbols.IsCarrierType(this.region.ModelFor(argument).GetTypeInfo(argument).Type))
        {
            return;
        }

        if (IsAsserted(argument))
        {
            return;
        }

        if (this.SamplesParameter(callee: callee, parameter: parameter))
        {
            this.region.Report(
                Diagnostic.Create(
                    descriptor: Diagnostics.ReadInCallee,
                    location: argument.GetLocation(),
                    callee.MethodKind == MethodKind.Constructor ? callee.ContainingType.Name : callee.Name,
                    parameter.Name,
                    this.region.PlaceholderName));

            return;
        }

        // The construction was followed and the value went only into members, whose reads are
        // checked. Nothing is unseen.
        if (followed != null && followed.Contains(parameter))
        {
            return;
        }

        this.region.Report(
            Diagnostic.Create(
                descriptor: Diagnostics.Escape,
                location: argument.GetLocation(),
                this.region.PlaceholderName,
                callee.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
    }

    /// <summary>
    ///     An <c>AsCell()</c> or <c>AsBehavior()</c> on the argument, or on a value it
    ///     is derived from, is the assertion that nothing reads it before the close.
    /// </summary>
    private static bool IsAsserted(ExpressionSyntax argument)
    {
        ExpressionSyntax? current = argument;

        while (current is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access })
        {
            if (access.Name.Identifier.ValueText is "AsCell" or "AsBehavior")
            {
                return true;
            }

            current = access.Expression;
        }

        return false;
    }

    private bool SamplesParameter(IMethodSymbol callee, IParameterSymbol parameter) =>
        this.SamplesParameter(callee: callee, parameter: parameter, depth: 0);

    /// <summary>
    ///     True when the callee samples the parameter. The read can be in its own body,
    ///     or in a method in this compilation that gets the parameter from it. The scan
    ///     goes <see cref="LoopStateEvaluator.MaxInlineDepth" /> calls deep.
    /// </summary>
    private bool SamplesParameter(IMethodSymbol callee, IParameterSymbol parameter, int depth)
    {
        if (depth >= LoopStateEvaluator.MaxInlineDepth)
        {
            return false;
        }

        SyntaxNode? declaration = callee.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();

        if (declaration == null)
        {
            return false;
        }

        // A primary constructor declares on its type. Then, the scan covers the type: the
        // initializers of its members and the arguments to its base.
        SemanticModel model = this.region.ModelFor(declaration);

        foreach (InvocationExpressionSyntax call in declaration.DescendantNodes(
                     descendIntoChildren: static n => n is not AnonymousFunctionExpressionSyntax)
                 .OfType<InvocationExpressionSyntax>())
        {
            if (model.GetSymbolInfo(call).Symbol is not IMethodSymbol next)
            {
                continue;
            }

            if (call.Expression is MemberAccessExpressionSyntax access
                && SodaFlowSymbols.ReadMembers.Contains(access.Name.Identifier.ValueText)
                && SodaFlowSymbols.IsSodaFlow(next)
                && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(access.Expression).Symbol, parameter))
            {
                return true;
            }

            if (SodaFlowSymbols.IsSodaFlow(next))
            {
                continue;
            }

            // The parameter passed on to a method of the caller's own.
            IMethodSymbol definition = (next.ReducedFrom ?? next).OriginalDefinition;
            int offset = 0;

            if (next.ReducedFrom != null
                && call.Expression is MemberAccessExpressionSyntax receiverAccess
                && definition.Parameters.Length > 0)
            {
                offset = 1;

                if (SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(receiverAccess.Expression).Symbol, parameter)
                    && this.SamplesParameter(callee: definition, parameter: definition.Parameters[0], depth: depth + 1))
                {
                    return true;
                }
            }

            for (int i = 0; i < call.ArgumentList.Arguments.Count; i++)
            {
                ArgumentSyntax argument = call.ArgumentList.Arguments[i];

                if (!SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(argument.Expression).Symbol, parameter))
                {
                    continue;
                }

                IParameterSymbol? nextParameter =
                    SodaFlowSymbols.ParameterOf(callee: definition, argument: argument, position: i + offset);

                if (nextParameter != null
                    && this.SamplesParameter(callee: definition, parameter: nextParameter, depth: depth + 1))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
