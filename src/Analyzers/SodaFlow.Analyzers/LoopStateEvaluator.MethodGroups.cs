using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a method group that names a method of the object of this code.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     True when a method group names a method of the object that this code
    ///     is in. The group has no owner, or its owner is this or base.
    /// </summary>
    /// <remarks>
    ///     A callee can run the delegate in the call. A map or a
    ///     <c>Lazy</c> can keep the delegate and run it after a different
    ///     method changes a member that the method reads. Thus, the walk of the
    ///     method gets the members of this, as a call of the method on this
    ///     does.
    /// </remarks>
    /// <param name="expression">The method group.</param>
    /// <param name="group">The method that the group names.</param>
    private static bool NamesAMethodOfThis(ExpressionSyntax expression, IMethodSymbol group) =>
        !(group.ReducedFrom ?? group).IsStatic
        && Unwrapped(expression)
            is SimpleNameSyntax
            or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax or BaseExpressionSyntax };

    // The object of a method group with an owner that is not this, with this code, which names the
    // group. It is the expression whose state the group keeps as its receiver.
    private (LoopStateEvaluator Namer, ExpressionSyntax Written)? OwnerOfAGroup(ExpressionSyntax expression) =>
        expression is MemberAccessExpressionSyntax { Expression: not ThisExpressionSyntax and var owned }
            ? (this, owned)
            : null;

    /// <summary>
    ///     The members of the object of a method of a different object, at a
    ///     call of the method.
    /// </summary>
    /// <remarks>
    ///     The group keeps the state of its object at the name. The code can
    ///     change a member of that object before the call. A lambda that calls
    ///     the method reads the name again at the call. Thus, an object of the
    ///     store gets the members that the store has at the call.
    /// </remarks>
    /// <param name="function">The function.</param>
    private static Construction? MembersOfTheReceiver(FunctionValue function) =>
        function is { Receiver: { } named, Owner: ({ } namer, { } written) }
            ? namer.Refreshed(expression: written, state: named).Members
            : function.Receiver?.Members;

    /// <summary>
    ///     The function of each method that a method group can run, as a lambda
    ///     that calls the method runs it. None for a group with no method to read.
    /// </summary>
    /// <remarks>
    ///     A virtual or an interface method runs the method of the class of its
    ///     object, as a call does. A group of an extension method keeps its
    ///     receiver for the first parameter.
    /// </remarks>
    private LoopState FunctionsOfAGroup(ExpressionSyntax expression, IMethodSymbol group)
    {
        ExpressionSyntax? owned = expression is MemberAccessExpressionSyntax access ? access.Expression : null;
        ExpressionSyntax? extended =
            group.ReducedFrom != null || this.ExtensionParameterOf(group.OriginalDefinition) != null ? owned : null;

        LoopState? receiver =
            owned != null && (extended != null || owned is not ThisExpressionSyntax) ? this.Eval(owned) : null;

        return this.MethodsOfAGroup(owner: owned, group: group)
            .Select(static method =>
                method.DeclaringSyntaxReferences
                    .Select(static reference => reference.GetSyntax())
                    .FirstOrDefault(static syntax => syntax is MethodDeclarationSyntax or LocalFunctionStatementSyntax))
            .OfType<SyntaxNode>()
            .Aggregate(
                seed: LoopState.None,
                func: (all, declaration) =>
                    all.Join(
                        LoopState.OfFunction(
                            new FunctionValue(
                                declaration: declaration,
                                definer: declaration is LocalFunctionStatementSyntax ? this : null,
                                receiver: receiver,
                                ofThis: NamesAMethodOfThis(expression: expression, group: group) ? this : null,
                                owner: this.OwnerOfAGroup(expression),
                                extended: extended))));
    }

    // The methods that a group can run. Only a virtual or an abstract method can have one more. The
    // walk takes the method of each class that the object can have, as DefinitionsOf does for a
    // call. An object that the walk did not see made keeps the method of the group, and so does
    // base, which has no members.
    private IEnumerable<IMethodSymbol> MethodsOfAGroup(ExpressionSyntax? owner, IMethodSymbol group)
    {
        IMethodSymbol definition = (group.ReducedFrom ?? group).OriginalDefinition;

        if (group is not { IsStatic: false, ReducedFrom: null }
            || !(definition.IsVirtual || definition.IsAbstract || definition.IsOverride))
        {
            return [definition];
        }

        Construction? members =
            owner is null or ThisExpressionSyntax
                ? this.builtType != null ? Construction.Of(this.builtType) : this.thisObject
                : this.Self(owner).Members;

        IMethodSymbol[] actual =
        [
            .. members?.Resolve(definition).OfType<IMethodSymbol>().Select(static found => found.OriginalDefinition)
            ?? []
        ];

        return actual.Length == 0 ? [definition] : actual;
    }

    /// <summary>
    ///     A method group of a read of the library, such as <c>x.Sample</c>.
    ///     Until the delegate runs, it is pending, as a lazy sample of x is. Its
    ///     function reads x when something runs the delegate.
    /// </summary>
    /// <remarks>
    ///     A static group with no receiver, such as
    ///     <c>CellExtensionMethods.Sample</c>, reads its first argument.
    /// </remarks>
    private LoopState EvalAReadGroup(MemberAccessExpressionSyntax read, IMethodSymbol group)
    {
        LoopState owner = this.Eval(read.Expression);

        return owner.WithLinked(false)
            .Join(
                LoopState.OfFunction(
                    new FunctionValue(
                        declaration: read,
                        definer: this,
                        receiver: group.ReducedFrom != null || !group.IsStatic ? owner : null,
                        ofThis: null,
                        owner: null)));
    }

    // The run of the function of a read group. The receiver is a value of the code of the group,
    // and an argument is a value of this code. A cell whose functions run at its first read runs
    // them now.
    private Followed FollowAReadGroup(MemberAccessExpressionSyntax read, FunctionValue function, LoopState argument)
    {
        (LoopStateEvaluator reader, LoopState state) =
            function is { Receiver: { } receiver, Definer: { } definer } ? (definer, receiver) : (this, argument);

        if (!state.Computed.IsEmpty)
        {
            state = reader.AtTheFirstRead(read: read, state: state, marks: null);
        }

        if (!state.Pending)
        {
            return Followed.Nothing;
        }

        string name = read.Name.Identifier.ValueText;

        return new Followed(
            state: LoopState.None,
            findings: ImmutableArray.Create(
                new Finding(
                    home: reader.Frame,
                    origin: state.Origin,
                    escape: false,
                    diagnostic: Diagnostic.Create(
                        descriptor: Diagnostics.ReadBeforeClose,
                        location: read.GetLocation(),
                        name,
                        this.region.PlaceholderName),
                    subject: name,
                    readBack: state.ReadBack)),
            outs: Followed.Nothing.Outs,
            sets: Followed.Nothing.Sets,
            through: Followed.Nothing.Through,
            alwaysGives: Followed.Nothing.AlwaysGives);
    }

    // A method group of an extension method gives its receiver to the first parameter, as a call
    // of the method does. The arguments of a call of the delegate go to the parameters after it. A
    // member of an extension block gives the receiver to the parameter of the block.
    private IReadOnlyList<Binding> WithTheReceiverOfAGroup(
        FunctionValue function,
        IMethodSymbol method,
        Func<ImmutableArray<IParameterSymbol>, IReadOnlyList<Binding>> bind)
    {
        if (function is not { Extended: { } extended, Receiver: { } receiver })
        {
            return bind(method.Parameters);
        }

        IParameterSymbol? block = this.ExtensionParameterOf(method);

        if (block == null && method.Parameters.IsEmpty)
        {
            return bind(method.Parameters);
        }

        return
        [
            new Binding(parameter: block ?? method.Parameters[0], argument: extended, state: receiver),
            .. bind(block != null ? method.Parameters : method.Parameters.RemoveAt(0))
        ];
    }
}
