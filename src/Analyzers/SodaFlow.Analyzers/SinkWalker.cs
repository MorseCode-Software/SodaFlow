using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

/// <summary>
///     Walks code that runs while a loop is open, and finds each read of a
///     pending value. The code is the block, or the body of a callee that the
///     block calls.
/// </summary>
/// <remarks>
///     A lambda and a local function are values. The walk does not read one
///     at its declaration. It reads one where the code calls it, or gives it to
///     a callee that runs it in the call. At a call to code of this compilation,
///     the evaluator walks the body of the callee with a second walker. This
///     walker reports what that walk found.
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage - This class has its own summary, which says more than the base class does.
internal sealed class SinkWalker(in LoopRegion region, in LoopStateEvaluator evaluator, in Action<Finding> sink)
    : CSharpSyntaxWalker
{
    private readonly HashSet<AnonymousFunctionExpressionSyntax> entered = [];
    private readonly LoopStateEvaluator evaluator = evaluator;
    private readonly LoopRegion region = region;
    private readonly Action<Finding> sink = sink;

    public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
    {
        SemanticModel model = this.region.ModelFor(node);

        foreach (VariableDeclaratorSyntax declarator in node.Declaration.Variables)
        {
            if (declarator.Initializer == null)
            {
                continue;
            }

            this.Visit(declarator.Initializer.Value);

            if (model.GetDeclaredSymbol(declarator) is { } local)
            {
                this.evaluator.Declare(local: local, state: this.evaluator.Eval(declarator.Initializer.Value));
            }
        }
    }

    // A local function runs when the code calls it, and the evaluator follows it then.
    public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
    {
    }

    // A lambda runs when something calls it. A callee that runs it in the call reads it there.
    public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
    {
    }

    public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
    {
    }

    public override void VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
    {
    }

    public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        if (!node.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            base.VisitAssignmentExpression(node);

            return;
        }

        // The assignment writes its target, and does not read it. Only the owner of the target
        // is read.
        switch (node.Left)
        {
            case IdentifierNameSyntax:
                break;
            case MemberAccessExpressionSyntax access:
                this.Visit(access.Expression);
                break;
            case ElementAccessExpressionSyntax element:
                this.Visit(element.Expression);
                this.Visit(element.ArgumentList);
                break;
            default:
                this.Visit(node.Left);
                break;
        }

        this.Visit(node.Right);

        switch (this.region.ModelFor(node).GetSymbolInfo(node.Left).Symbol)
        {
            case ILocalSymbol or IParameterSymbol when node.Left is IdentifierNameSyntax:
                this.evaluator.Declare(
                    local: this.region.ModelFor(node).GetSymbolInfo(node.Left).Symbol!,
                    state: this.evaluator.Eval(node.Right));
                break;
            // A member of the object that the code is in: a constructor that sets its members, or
            // a method that does.
            case IFieldSymbol { IsStatic: false } or IPropertySymbol { IsStatic: false }
                when node.Left is IdentifierNameSyntax
                    or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax }:
                this.evaluator.SetMember(
                    member: this.region.ModelFor(node).GetSymbolInfo(node.Left).Symbol!,
                    state: this.evaluator.Eval(node.Right));
                break;
        }
    }

    public override void VisitInitializerExpression(InitializerExpressionSyntax node)
    {
        // An object initializer names a member and gives it a value. Only the value is code that
        // runs.
        bool setsMembers =
            node.IsKind(SyntaxKind.ObjectInitializerExpression) || node.IsKind(SyntaxKind.WithInitializerExpression);

        foreach (ExpressionSyntax expression in node.Expressions)
        {
            this.Visit(
                setsMembers && expression is AssignmentExpressionSyntax { Left: IdentifierNameSyntax } assignment
                    ? assignment.Right
                    : expression);
        }
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        SemanticModel model = this.region.ModelFor(node);

        if (model.GetSymbolInfo(node).Symbol is not IMethodSymbol method)
        {
            base.VisitInvocationExpression(node);

            return;
        }

        this.Visit(node.Expression);

        SodaFlowSymbols symbols = this.region.Symbols;
        bool library = symbols.IsSodaFlow(method);
        Followed? followed = this.evaluator.Follow(node);

        // A lambda written at the call runs in the call for two kinds of callee. One is a library
        // operator that runs its function in the block. The other is code that the analyzer
        // cannot read. A callee whose body the evaluator follows gets the lambda as a value.
        bool runsLambdas =
            followed == null
            && (library
                ? LoopStateEvaluator.RunsNow(symbols: symbols, method: method)
                : !RunsOnAnotherThread(method.ContainingType));

        this.VisitArguments(arguments: node.ArgumentList.Arguments, runsLambdas: runsLambdas);
        this.Report(followed);

        ExpressionSyntax? receiver = LoopStateEvaluator.ReceiverOf(symbols: symbols, invocation: node, method: method);

        // CanExecute is an ICommand member, which a bindable action implements. Thus, the read
        // members count on a library receiver too, and not only on a library method.
        bool libraryReceiver = receiver != null && symbols.IsCarrierType(model.GetTypeInfo(receiver).Type);

        if (library || libraryReceiver)
        {
            if (SodaFlowSymbols.ReadMembers.Contains(LoopStateEvaluator.OperatorName(method)) && receiver != null)
            {
                this.CheckRead(node: node, owner: receiver, name: method.Name);
            }

            if (library)
            {
                return;
            }
        }

        if (followed != null)
        {
            return;
        }

        // The callee has no body in this compilation, or is too deep. The analyzer cannot see a
        // pending value that goes to it.
        IMethodSymbol called = method.ReducedFrom ?? method;

        if (method.ReducedFrom != null && receiver != null)
        {
            this.CheckEscape(callee: called, argument: receiver);
        }

        this.CheckEscapes(callee: called, arguments: node.ArgumentList.Arguments);
    }

    public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node) =>
        this.VisitCreation(node: node, arguments: node.ArgumentList, initializer: node.Initializer);

    public override void VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node) =>
        this.VisitCreation(node: node, arguments: node.ArgumentList, initializer: node.Initializer);

    public override void VisitConstructorInitializer(ConstructorInitializerSyntax node) =>
        this.VisitCreation(node: node, arguments: node.ArgumentList, initializer: null);

    public override void VisitPrimaryConstructorBaseType(PrimaryConstructorBaseTypeSyntax node) =>
        this.VisitCreation(node: node, arguments: node.ArgumentList, initializer: null);

    /// <summary>
    ///     A call of a constructor: the creation of an object, or the constructor
    ///     that a constructor calls first.
    /// </summary>
    private void VisitCreation(
        SyntaxNode node,
        BaseArgumentListSyntax? arguments,
        InitializerExpressionSyntax? initializer)
    {
        IMethodSymbol? constructor = this.region.ModelFor(node).GetSymbolInfo(node).Symbol as IMethodSymbol;
        Followed? followed = this.evaluator.Follow(node);

        if (arguments != null)
        {
            this.VisitArguments(
                arguments: arguments.Arguments,
                runsLambdas: followed == null
                             && constructor != null
                             && !RunsOnAnotherThread(constructor.ContainingType));
        }

        this.Visit(initializer);
        this.Report(followed);

        if (followed == null
            && arguments != null
            && constructor != null
            && !this.region.Symbols.IsSodaFlow(constructor))
        {
            this.CheckEscapes(callee: constructor, arguments: arguments.Arguments);
        }
    }

    private void VisitArguments(SeparatedSyntaxList<ArgumentSyntax> arguments, bool runsLambdas)
    {
        foreach (ArgumentSyntax argument in arguments)
        {
            if (argument.Expression is not AnonymousFunctionExpressionSyntax lambda)
            {
                this.Visit(argument);
            }
            else if (runsLambdas && this.entered.Add(lambda))
            {
                // The lambda is part of this code. The walk reads it where it is, with the same
                // evaluator.
                this.Visit(lambda.Body);
            }
        }
    }

    /// <summary>
    ///     A type whose lambdas run on a different thread or after the block, so
    ///     not in the block.
    /// </summary>
    private static bool RunsOnAnotherThread(ISymbol type) =>
        type.ContainingNamespace?.ToDisplayString() is "System.Threading" or "System.Threading.Tasks"
        && type.Name is "Thread" or "Task" or "TaskFactory" or "ThreadPool" or "Timer" or "SynchronizationContext";

    public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        // Only the owner is code. The name is the member, which the check below reads.
        this.Visit(node.Expression);
        this.CheckPropertyRead(node: node, owner: node.Expression);
    }

    public override void VisitMemberBindingExpression(MemberBindingExpressionSyntax node)
    {
        if (LoopStateEvaluator.ConditionalReceiver(node) is { } owner)
        {
            this.CheckPropertyRead(node: node, owner: owner);
        }
    }

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        // A name with no owner can be a property of the object that the code is in. Its getter
        // can read a member of that object.
        if (this.evaluator.HasThisMembers
            && this.region.ModelFor(node).GetSymbolInfo(node).Symbol is IPropertySymbol { IsStatic: false })
        {
            this.Report(this.evaluator.Follow(node));
        }
    }

    /// <summary>
    ///     A property of the library that reads the current value, or a property
    ///     with a getter of this compilation, on the given owner.
    /// </summary>
    private void CheckPropertyRead(ExpressionSyntax node, ExpressionSyntax owner)
    {
        if (this.region.ModelFor(node).GetSymbolInfo(node).Symbol is not IPropertySymbol property)
        {
            return;
        }

        if (!this.region.Symbols.IsSodaFlow(property))
        {
            this.Report(this.evaluator.Follow(node));
        }
        else if (SodaFlowSymbols.ReadMembers.Contains(property.Name))
        {
            this.CheckRead(node: node, owner: owner, name: property.Name);
        }
    }

    /// <summary>A member that reads the current value of its owner.</summary>
    private void CheckRead(SyntaxNode node, ExpressionSyntax owner, string name)
    {
        LoopState state = this.evaluator.Eval(owner);

        if (!state.Pending)
        {
            return;
        }

        this.sink(
            new Finding(
                home: this.evaluator.Frame,
                origin: state.Origin,
                escape: false,
                diagnostic: Diagnostic.Create(
                    descriptor: Diagnostics.ReadBeforeClose,
                    location: node.GetLocation(),
                    name,
                    this.region.PlaceholderName),
                subject: name));
    }

    private void Report(Followed? followed)
    {
        if (followed == null)
        {
            return;
        }

        foreach (Finding finding in followed.Findings)
        {
            this.sink(finding);
        }
    }

    private void CheckEscapes(IMethodSymbol callee, SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        foreach (ArgumentSyntax argument in arguments)
        {
            this.CheckEscape(callee: callee, argument: argument.Expression);
        }
    }

    /// <summary>
    ///     A pending value given to code that the analyzer cannot read. An
    ///     <c>AsCell()</c> at the argument is the assertion of the caller and reports
    ///     nothing.
    /// </summary>
    private void CheckEscape(IMethodSymbol callee, ExpressionSyntax argument)
    {
        LoopState state = this.evaluator.Eval(argument);

        // Only a library value escapes. An object of the caller that holds a pending member goes
        // where the caller sends it, as it does with the looped types. The walk checks its reads
        // through its members.
        if (!state.Pending
            || !this.region.Symbols.IsCarrierType(this.region.ModelFor(argument).GetTypeInfo(argument).Type)
            || LoopStateEvaluator.IsAsserted(argument))
        {
            return;
        }

        string subject = callee.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        this.sink(
            new Finding(
                home: this.evaluator.Frame,
                origin: state.Origin,
                escape: true,
                diagnostic: Diagnostic.Create(
                    descriptor: Diagnostics.Escape,
                    location: argument.GetLocation(),
                    this.region.PlaceholderName,
                    subject),
                subject: subject));
    }
}
