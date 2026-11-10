using System;
using System.Collections.Generic;
using System.Linq;
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

    // The lambdas that the walk read where they are, and that their callee runs one time.
    private readonly HashSet<AnonymousFunctionExpressionSyntax> once = [];
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

    public override void VisitForEachStatement(ForEachStatementSyntax node)
    {
        this.Visit(node.Expression);

        // The collection has the state of all its elements, thus each element has that state.
        if (this.region.ModelFor(node).GetDeclaredSymbol(node) is { } element)
        {
            this.evaluator.Declare(local: element, state: this.evaluator.Eval(node.Expression));
        }

        this.Visit(node.Statement);
    }

    public override void VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
    {
        this.Visit(node.Expression);
        this.AssignState(target: node.Variable, state: this.evaluator.Eval(node.Expression), replaces: false);
        this.Visit(node.Statement);
    }

    public override void VisitIsPatternExpression(IsPatternExpressionSyntax node)
    {
        this.DeclarePattern(pattern: node.Pattern, state: this.evaluator.Eval(node.Expression));
        base.VisitIsPatternExpression(node);
    }

    public override void VisitSwitchStatement(SwitchStatementSyntax node)
    {
        LoopState state = this.evaluator.Eval(node.Expression);

        foreach (CasePatternSwitchLabelSyntax label in node.Sections
                     .SelectMany(static section => section.Labels)
                     .OfType<CasePatternSwitchLabelSyntax>())
        {
            this.DeclarePattern(pattern: label.Pattern, state: state);
        }

        base.VisitSwitchStatement(node);
    }

    public override void VisitSwitchExpression(SwitchExpressionSyntax node)
    {
        LoopState state = this.evaluator.Eval(node.GoverningExpression);

        foreach (SwitchExpressionArmSyntax arm in node.Arms)
        {
            this.DeclarePattern(pattern: arm.Pattern, state: state);
        }

        base.VisitSwitchExpression(node);
    }

    /// <summary>
    ///     Gives each variable of a pattern the state of the value that the
    ///     pattern is for. A variable for a member of an object gets the state of
    ///     that member, when the walk knows the members.
    /// </summary>
    private void DeclarePattern(PatternSyntax pattern, LoopState state)
    {
        if (state.IsNone)
        {
            return;
        }

        switch (pattern)
        {
            case DeclarationPatternSyntax declaration:
                this.DeclareDesignation(designation: declaration.Designation, state: state);
                break;
            case VarPatternSyntax variable:
                this.DeclareDesignation(designation: variable.Designation, state: state);
                break;
            case RecursivePatternSyntax recursive:
            {
                this.DeclareDesignation(designation: recursive.Designation, state: state);
                foreach (SubpatternSyntax part in recursive.PropertyPatternClause?.Subpatterns ?? default)
                {
                    ExpressionSyntax? name = part.NameColon?.Name ?? part.ExpressionColon?.Expression;

                    this.DeclarePattern(
                        pattern: part.Pattern,
                        state: state.Members != null && name != null && this.region.SymbolOf(name) is { } member
                            ? state.Members.StateOf(state.Members.Resolve(member)).FromIfUnknown(state.Origin)
                            : state);
                }

                foreach (SubpatternSyntax part in recursive.PositionalPatternClause?.Subpatterns ?? default)
                {
                    this.DeclarePattern(pattern: part.Pattern, state: state);
                }

                break;
            }
            case BinaryPatternSyntax binary:
                this.DeclarePattern(pattern: binary.Left, state: state);
                this.DeclarePattern(pattern: binary.Right, state: state);
                break;
            case UnaryPatternSyntax unary:
                this.DeclarePattern(pattern: unary.Pattern, state: state);
                break;
            case ParenthesizedPatternSyntax parenthesized:
                this.DeclarePattern(pattern: parenthesized.Pattern, state: state);
                break;
            case ListPatternSyntax list:
                this.DeclareDesignation(designation: list.Designation, state: state);

                foreach (PatternSyntax element in list.Patterns)
                {
                    this.DeclarePattern(pattern: element, state: state);
                }

                break;
            case SlicePatternSyntax { Pattern: { } rest }:
                this.DeclarePattern(pattern: rest, state: state);
                break;
        }
    }

    private void DeclareDesignation(VariableDesignationSyntax? designation, LoopState state)
    {
        if (designation == null)
        {
            return;
        }

        SemanticModel model = this.region.ModelFor(designation);

        foreach (SingleVariableDesignationSyntax single in designation.DescendantNodesAndSelf()
                     .OfType<SingleVariableDesignationSyntax>())
        {
            if (model.GetDeclaredSymbol(single) is { } declared)
            {
                this.evaluator.Declare(local: declared, state: state);
            }
        }
    }

    public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        if (!node.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            base.VisitAssignmentExpression(node);

            // A delegate that gets one more function, as in handlers += () => ..., can be each of
            // its functions.
            if (node.IsKind(SyntaxKind.AddAssignmentExpression)
                && this.evaluator.Eval(node.Right) is { Functions.IsEmpty: false } added)
            {
                this.AssignState(target: node.Left, state: added, replaces: false);
            }

            return;
        }

        // The assignment writes its target, and does not read it. Only the owner of the target
        // is read.
        switch (node.Left)
        {
            case IdentifierNameSyntax or TupleExpressionSyntax or DeclarationExpressionSyntax:
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
        this.Assign(target: node.Left, value: node.Right, replaces: this.AlwaysRuns(node));
    }

    /// <summary>
    ///     True for an assignment that is a statement at the first level of its
    ///     function, with no branch and no loop around it. Each call of the function
    ///     runs it, thus the state that it gives replaces the state before it.
    /// </summary>
    /// <remarks>
    ///     A lambda that the walk reads where it is has a callee that runs it. A
    ///     transaction and a loop block run it one time, and the walk then looks at
    ///     what is around the call. A query runs it one time for each element, which
    ///     can be no time, thus an assignment in it does not always run.
    /// </remarks>
    private bool AlwaysRuns(AssignmentExpressionSyntax assignment)
    {
        if (assignment.Parent is not ExpressionStatementSyntax statement)
        {
            return false;
        }

        foreach (SyntaxNode ancestor in statement.Ancestors())
        {
            switch (ancestor)
            {
                case AnonymousFunctionExpressionSyntax lambda when this.entered.Contains(lambda):
                    if (!this.once.Contains(lambda))
                    {
                        return false;
                    }

                    continue;
                case AnonymousFunctionExpressionSyntax
                    or LocalFunctionStatementSyntax
                    or MemberDeclarationSyntax
                    or AccessorDeclarationSyntax:
                    return true;
                // The call that runs the lambda, and the statement that the call is in.
                case ArgumentSyntax
                    or ArgumentListSyntax
                    or InvocationExpressionSyntax
                    or ExpressionStatementSyntax
                    or EqualsValueClauseSyntax
                    or VariableDeclaratorSyntax
                    or VariableDeclarationSyntax
                    or LocalDeclarationStatementSyntax
                    or ReturnStatementSyntax:
                    continue;
                case BlockSyntax
                    or UsingStatementSyntax
                    or LockStatementSyntax
                    or CheckedStatementSyntax
                    or UnsafeStatementSyntax
                    or FixedStatementSyntax:
                    continue;
                default:
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Gives a target the state of a value. A deconstruction of a tuple
    ///     expression gives each part of the tuple to its own target.
    /// </summary>
    private void Assign(ExpressionSyntax target, ExpressionSyntax value, bool replaces)
    {
        if (target is TupleExpressionSyntax targets
            && value is TupleExpressionSyntax values
            && targets.Arguments.Count == values.Arguments.Count)
        {
            for (int i = 0; i < targets.Arguments.Count; i++)
            {
                this.Assign(
                    target: targets.Arguments[i].Expression,
                    value: values.Arguments[i].Expression,
                    replaces: replaces);
            }

            return;
        }

        LoopState whole = this.evaluator.Eval(value);

        // A deconstruction of a value that is not a tuple expression. Each target gets the state
        // of its element, when the evaluator knows the elements. Otherwise, each gets the state of
        // the value.
        IReadOnlyList<SyntaxNode>? parts =
            target switch
            {
                TupleExpressionSyntax tuple => [.. tuple.Arguments.Select(static argument => argument.Expression)],
                DeclarationExpressionSyntax { Designation: ParenthesizedVariableDesignationSyntax several } =>
                    [.. several.Variables],
                _ => null
            };

        if (parts == null
            || whole.Members == null
            || this.region.TypeOf(value) is not INamedTypeSymbol { IsTupleType: true } type
            || type.TupleElements.Length != parts.Count)
        {
            this.AssignState(target: target, state: whole, replaces: replaces);

            return;
        }

        for (int i = 0; i < parts.Count; i++)
        {
            LoopState element =
                whole.Members.Has(type.TupleElements[i])
                    ? whole.Members.StateOf(type.TupleElements[i]).FromIfUnknown(whole.Origin)
                    : whole;

            if (parts[i] is ExpressionSyntax expression)
            {
                this.AssignState(target: expression, state: element, replaces: replaces);
            }
            else
            {
                this.DeclareDesignation(designation: parts[i] as VariableDesignationSyntax, state: element);
            }
        }
    }

    /// <param name="target">The expression that gets the value.</param>
    /// <param name="state">The state of the value.</param>
    /// <param name="replaces">
    ///     True when the assignment always runs. Then, a local has the new state
    ///     only. Otherwise, it has the new state and the state before it together.
    /// </param>
    private void AssignState(ExpressionSyntax target, LoopState state, bool replaces)
    {
        switch (target)
        {
            case DeclarationExpressionSyntax declaration:
                this.DeclareDesignation(designation: declaration.Designation, state: state);

                return;
            // A value that is not a tuple expression: each part gets the state of all of it.
            case TupleExpressionSyntax parts:
                foreach (ArgumentSyntax part in parts.Arguments)
                {
                    this.AssignState(target: part.Expression, state: state, replaces: replaces);
                }

                return;
            // A value put into a collection is in the collection.
            case ElementAccessExpressionSyntax element:
                this.evaluator.Absorb(target: element.Expression, state: state);

                return;
        }

        switch (this.region.SymbolOf(target))
        {
            case { } local and (ILocalSymbol or IParameterSymbol) when target is IdentifierNameSyntax:
                if (replaces)
                {
                    this.evaluator.Assign(local: local, state: state);
                }
                else
                {
                    this.evaluator.Declare(local: local, state: state);
                }

                break;
            // A member of the object that the code is in: a constructor that sets its members, or
            // a method that does.
            case { IsStatic: false } own and (IFieldSymbol or IPropertySymbol)
                when target is IdentifierNameSyntax
                    or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax }:
                this.evaluator.SetMember(member: own, state: state);
                break;
            case { } member and (IFieldSymbol or IPropertySymbol) when !this.region.Symbols.IsSodaFlow(member):
                this.evaluator.SetMemberOf(
                    owner: (target as MemberAccessExpressionSyntax)?.Expression,
                    member: member,
                    state: state);
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
        if (this.region.SymbolOf(node) is not IMethodSymbol method)
        {
            base.VisitInvocationExpression(node);

            return;
        }

        this.Visit(node.Expression);

        SodaFlowSymbols symbols = this.region.Symbols;
        SeparatedSyntaxList<ArgumentSyntax> arguments = node.ArgumentList.Arguments;
        ExpressionSyntax? receiver = LoopStateEvaluator.ReceiverOf(symbols: symbols, invocation: node, method: method);
        CalleeKind kind = this.evaluator.Classify(call: node, method: method);

        // An operator that keeps its function has nothing for the evaluator to follow.
        Followed? followed =
            kind == CalleeKind.Library
            && !OperatorTable.FunctionsInTheCall.Contains(LoopStateEvaluator.OperatorName(method))
                ? null
                : this.evaluator.Follow(node);

        // A lambda written at the call is code that the walk reads here when the callee runs it
        // in the call.
        bool Runs(ArgumentSyntax argument, int position) =>
            this.evaluator.RunsFunction(
                kind: kind,
                method: method,
                parameter: LoopStateEvaluator.ParameterFor(method: method, argument: argument, position: position));

        switch (kind)
        {
            // An operator keeps its function for a firing after the close, but for the few that
            // run it in the block.
            case CalleeKind.Library:
                this.VisitArguments(
                    arguments: arguments,
                    runs: Runs,
                    given: LoopState.None,
                    oneTime: OperatorTable.RunsFunctionOneTime(method));

                break;
            // The evaluator follows the body, and a lambda is a value that the body gets.
            case CalleeKind.Body:
                this.VisitArguments(arguments: arguments, runs: Runs, given: LoopState.None, oneTime: false);
                this.AssignOuts(arguments: arguments, method: method, followed: followed, moved: LoopState.None);

                break;
            // Code that cannot know the library runs a lambda in the call, with the values that
            // the call gave it. It can put an argument into its receiver, as Add does to a list.
            case CalleeKind.Mover:
            {
                LoopState moved = this.evaluator.Moved(invocation: node, method: method, withReceiver: true);

                this.VisitArguments(arguments: arguments, runs: Runs, given: moved, oneTime: false);

                if (receiver != null && method.ReducedFrom == null)
                {
                    this.evaluator.Absorb(
                        target: receiver,
                        state: this.evaluator.Moved(invocation: node, method: method, withReceiver: false));
                }

                this.AssignOuts(arguments: arguments, method: method, followed: null, moved: moved);

                break;
            }
            // The analyzer cannot see a pending value that goes to this callee.
            default:
            {
                this.VisitArguments(arguments: arguments, runs: Runs, given: LoopState.None, oneTime: false);

                IMethodSymbol called = method.ReducedFrom ?? method;

                if (method.ReducedFrom != null && receiver != null)
                {
                    this.CheckEscape(callee: called, argument: receiver);
                }

                this.CheckEscapes(callee: called, arguments: arguments);

                break;
            }
        }

        this.Report(followed);

        // CanExecute is an ICommand member, which a bindable action implements. Thus, the read
        // members count on a library receiver too, and not only on a library method.
        if (receiver != null
            && SodaFlowSymbols.ReadMembers.Contains(LoopStateEvaluator.OperatorName(method))
            && (kind == CalleeKind.Library || symbols.IsCarrierType(this.region.TypeOf(receiver))))
        {
            this.CheckRead(node: node, owner: receiver, name: method.Name);
        }
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
        if (this.region.SymbolOf(node) is not IMethodSymbol constructor)
        {
            this.Visit(arguments);
            this.Visit(initializer);

            return;
        }

        CalleeKind kind = this.evaluator.Classify(call: node, method: constructor);

        // A constructor gets a lambda as a value. The body says what runs it, when the evaluator
        // follows the body. A constructor of code that the analyzer cannot read keeps it for a
        // time after the call.
        if (arguments != null)
        {
            this.VisitArguments(
                arguments: arguments.Arguments,
                runs: static (_, _) => false,
                given: LoopState.None,
                oneTime: false);
        }

        this.Visit(initializer);
        this.Report(this.evaluator.Follow(node));

        if (kind == CalleeKind.Unknown && arguments != null)
        {
            this.CheckEscapes(callee: constructor, arguments: arguments.Arguments);
        }
    }

    /// <summary>
    ///     The arguments of a call. An argument that is a lambda is code that the
    ///     walk reads here only when the callee runs it in the call.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="runs">
    ///     True when the callee runs, in the call, the function that it gets for
    ///     the argument at the position.
    /// </param>
    /// <param name="given">
    ///     The state of the values that the callee can give to a lambda, which
    ///     each parameter of the lambda then has.
    /// </param>
    /// <param name="oneTime">True when the callee runs the function one time, and not zero or many.</param>
    private void VisitArguments(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        Func<ArgumentSyntax, int, bool> runs,
        LoopState given,
        bool oneTime)
    {
        for (int i = 0; i < arguments.Count; i++)
        {
            ArgumentSyntax argument = arguments[i];

            if (argument.Expression is not AnonymousFunctionExpressionSyntax lambda)
            {
                this.Visit(argument);

                continue;
            }

            if (!runs(arg1: argument, arg2: i) || !this.entered.Add(lambda))
            {
                continue;
            }

            if (oneTime)
            {
                this.once.Add(lambda);
            }

            if (!given.IsNone)
            {
                SemanticModel model = this.region.ModelFor(lambda);

                foreach (ParameterSyntax parameter in LoopStateEvaluator.ParametersOf(lambda))
                {
                    if (model.GetDeclaredSymbol(parameter) is { } declared)
                    {
                        this.evaluator.Declare(local: declared, state: given);
                    }
                }
            }

            // The lambda is part of this code. The walk reads it where it is, with the same
            // evaluator.
            this.Visit(lambda.Body);
        }
    }

    /// <summary>
    ///     Gives the target of each <c>out</c> or <c>ref</c> argument the state
    ///     that the callee gives to that parameter.
    /// </summary>
    private void AssignOuts(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IMethodSymbol method,
        Followed? followed,
        LoopState moved)
    {
        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;
        int offset = method.ReducedFrom != null ? 1 : 0;

        for (int i = 0; i < arguments.Count; i++)
        {
            if (!arguments[i].RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword)
                && !arguments[i].RefOrOutKeyword.IsKind(SyntaxKind.RefKeyword))
            {
                continue;
            }

            if (SodaFlowSymbols.ParameterOf(callee: definition, argument: arguments[i], position: i + offset)
                is not { } parameter)
            {
                continue;
            }

            LoopState state =
                followed == null
                    ? moved
                    : followed.Outs.TryGetValue(key: parameter, value: out LoopState? assigned)
                        ? assigned
                        : LoopState.None;

            this.AssignState(target: arguments[i].Expression, state: state, replaces: false);
        }
    }

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
            && this.region.SymbolOf(node) is IPropertySymbol { IsStatic: false, DeclaringSyntaxReferences.Length: > 0 })
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
        if (this.region.SymbolOf(node) is not IPropertySymbol property)
        {
            return;
        }

        if (!this.region.Symbols.IsSodaFlow(property))
        {
            // Only a property of this compilation has a getter that the evaluator can read.
            if (property.DeclaringSyntaxReferences.Length > 0)
            {
                this.Report(this.evaluator.Follow(node));
            }
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
            || !this.region.Symbols.IsCarrierType(this.region.TypeOf(argument))
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
