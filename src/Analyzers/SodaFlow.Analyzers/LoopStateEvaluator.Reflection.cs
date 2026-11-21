using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator that follows the method or the constructor that a call of reflection
// runs, when the call names one of them with literals.
internal sealed partial class LoopStateEvaluator
{
    // The bits of BindingFlags that find a public instance constructor.
    private const int PublicInstance = 4 | 16;

    /// <summary>
    ///     Walks the method or the constructor that a call of reflection runs,
    ///     as <see cref="ReflectedCall" /> found it.
    /// </summary>
    private Followed? FollowReflection(
        SyntaxNode invocation,
        (IMethodSymbol Callee, ExpressionSyntax? Receiver, ImmutableArray<ExpressionSyntax> Arguments) reflected)
    {
        (IMethodSymbol callee, ExpressionSyntax? receiver, ImmutableArray<ExpressionSyntax> arguments) = reflected;

        IReadOnlyList<Binding> bindings =
        [
            .. arguments.Select((argument, position) =>
                new Binding(parameter: callee.Parameters[position], argument: argument, state: this.Eval(argument)))
        ];

        if (callee.MethodKind == MethodKind.Constructor)
        {
            return this.FollowConstructor(call: invocation, constructor: callee, arguments: default, bound: bindings);
        }

        if (BodyOf(callee) is not { } body)
        {
            return null;
        }

        (Dictionary<ISymbol, LoopState> members, LoopState? receiverState) =
            this.MembersFor(isStatic: callee.IsStatic, owner: receiver);

        return this.Enter(
            call: invocation,
            name: callee.Name,
            body: body,
            bindings: bindings,
            definer: null,
            members: members,
            receiver: receiverState,
            target: null,
            returnsAFunction: callee.ReturnType.TypeKind == TypeKind.Delegate,
            returnsAnObject: this.DoesNotSayItsClass(callee.ReturnType),
            owner: receiverState == null ? null : receiver);
    }

    /// <summary>
    ///     The method or the constructor that a call of reflection runs, the
    ///     object for a method, and the argument for each parameter. Null for a
    ///     different call.
    /// </summary>
    /// <remarks>
    ///     The calls are <c>typeof(T).GetMethod("M").Invoke(o, [a])</c> and
    ///     <c>Activator.CreateInstance(typeof(T), a)</c>, with the arguments in
    ///     an array that the call writes. Reflection finds the member at run
    ///     time. Thus, the walk follows only a member that the call can find and
    ///     run with no exception: the one public method of that name, or the one
    ///     public constructor with that count of parameters.
    /// </remarks>
    private (IMethodSymbol Callee, ExpressionSyntax? Receiver, ImmutableArray<ExpressionSyntax> Arguments)?
        ReflectedCall(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;

        switch (method.Name)
        {
            case "Invoke"
                when invocation.Expression is MemberAccessExpressionSyntax access
                     && WithoutAssertion(access.Expression) is InvocationExpressionSyntax
                     {
                         Expression: MemberAccessExpressionSyntax
                         {
                             Name.Identifier.ValueText: "GetMethod",
                             Expression: TypeOfExpressionSyntax typeOf
                         },
                         ArgumentList.Arguments: { Count: 1 } names
                     }
                     && this.ReflectedType(typeOf) is { } type
                     && this.region.ModelFor(names[0]).GetConstantValue(names[0].Expression)
                         is { HasValue: true, Value: string name }
                     && OnlyPublicMethod(type: type, name: name) is { } callee
                     && ArgumentFor(method: method, arguments: arguments, parameter: "parameters") is { } given
                     && ElementsWritten(given) is { } elements
                     && Fits(callee: callee, arguments: elements):
            {
                ExpressionSyntax? receiver =
                    callee.IsStatic ? null : ArgumentFor(method: method, arguments: arguments, parameter: "obj");

                // Invoke throws for an instance method with no object.
                return callee.IsStatic || receiver is not (null or LiteralExpressionSyntax)
                    ? (callee, receiver, elements)
                    : null;
            }
            // CreateInstance<T>() runs the constructor of T with no parameters, as new T() does.
            case "CreateInstance"
                when (method.IsGenericMethod
                         ? method.TypeArguments.FirstOrDefault()
                         : ArgumentFor(method: method, arguments: arguments, parameter: "type")
                             is TypeOfExpressionSyntax typeOf
                             ? this.ReflectedType(typeOf)
                             : null)
                     is INamedTypeSymbol { IsAbstract: false } type
                     && this.FindsPublicConstructors(method: method, arguments: arguments)
                     && this.ArgumentsOfAConstructor(method: method, arguments: arguments) is { } elements:
            {
                IMethodSymbol[] fits =
                [
                    .. type.InstanceConstructors.Where(constructor =>
                        constructor.DeclaredAccessibility == Accessibility.Public
                        && constructor.Parameters.Length == elements.Length)
                ];

                return fits.Length == 1 && Fits(callee: fits[0], arguments: elements)
                    ? (fits[0], null, elements)
                    : null;
            }
            default:
                return null;
        }
    }

    private INamedTypeSymbol? ReflectedType(TypeOfExpressionSyntax typeOf) =>
        this.region.SymbolOf(typeOf.Type) as INamedTypeSymbol;

    // The method that GetMethod finds with the default binding flags, which are public and include
    // the methods of the base types. Two methods of that name make GetMethod throw. Invoke throws
    // for a generic method, which has open type parameters. A virtual method runs the override of
    // the object, which the walk does not know.
    private static IMethodSymbol? OnlyPublicMethod(INamedTypeSymbol type, string name)
    {
        List<IMethodSymbol> found = [];

        for (INamedTypeSymbol? one = type; one != null; one = one.BaseType)
        {
            found.AddRange(
                one.GetMembers(name)
                    .OfType<IMethodSymbol>()
                    .Where(static candidate =>
                        candidate is { MethodKind: MethodKind.Ordinary, DeclaredAccessibility: Accessibility.Public }));
        }

        IMethodSymbol? only = found.Count == 1 ? found[0] : null;

        return only is { IsGenericMethod: false, IsVirtual: false, IsAbstract: false, IsOverride: false } ? only : null;
    }

    // True when the binding flags of a call of Activator find a public instance constructor. A call
    // with no flags uses those.
    private bool FindsPublicConstructors(IMethodSymbol method, SeparatedSyntaxList<ArgumentSyntax> arguments) =>
        method.Parameters.All(static parameter => parameter.Name != "bindingAttr")
        || (ArgumentFor(method: method, arguments: arguments, parameter: "bindingAttr") is { } flags
            && this.region.ModelFor(flags).GetConstantValue(flags) is { HasValue: true, Value: int value }
            && (value & PublicInstance) == PublicInstance);

    // The arguments that a call of Activator gives the constructor. A call can give them one by one
    // to the params parameter, or in an array that it writes. A method with no such parameter gives
    // no arguments.
    private ImmutableArray<ExpressionSyntax>? ArgumentsOfAConstructor(
        IMethodSymbol method,
        SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        if (method.Parameters.FirstOrDefault(static parameter => parameter.Name == "args") is not { } args)
        {
            return ImmutableArray<ExpressionSyntax>.Empty;
        }

        ExpressionSyntax[] given =
        [
            .. arguments
                .Where((argument, position) =>
                    SymbolEqualityComparer.Default.Equals(
                        x: SodaFlowSymbols.ParameterOf(callee: method, argument: argument, position: position),
                        y: args))
                .Select(static argument => argument.Expression)
        ];

        if (given.Length != 1)
        {
            return args.IsParams ? given.ToImmutableArray() : null;
        }

        // One argument is the array itself when it is an array.
        return ElementsWritten(given[0])
               ?? (args.IsParams && this.region.TypeOf(given[0]) is not IArrayTypeSymbol
                   ? ImmutableArray.Create(given[0])
                   : null);
    }

    // The argument for the parameter of the given name. Null when the call does not give one.
    private static ExpressionSyntax? ArgumentFor(
        IMethodSymbol method,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        string parameter)
    {
        for (int position = 0; position < arguments.Count; position++)
        {
            if (SodaFlowSymbols.ParameterOf(callee: method, argument: arguments[position], position: position)
                is { } found
                && found.Name == parameter)
            {
                return WithoutAssertion(arguments[position].Expression);
            }
        }

        return null;
    }

    // The elements of an array that the code writes at the call, each by itself. Null for a
    // different expression, and for an array with a spread element.
    public static ImmutableArray<ExpressionSyntax>? ElementsWritten(ExpressionSyntax array) =>
        WithoutAssertion(array) switch
        {
            CollectionExpressionSyntax collection
                when collection.Elements.All(static element => element is ExpressionElementSyntax) =>
                collection.Elements
                    .OfType<ExpressionElementSyntax>()
                    .Select(static element => element.Expression)
                    .ToImmutableArray(),
            ArrayCreationExpressionSyntax { Initializer: { } initializer } =>
                initializer.Expressions.ToImmutableArray(),
            ImplicitArrayCreationExpressionSyntax creation => creation.Initializer.Expressions.ToImmutableArray(),
            _ => null
        };

    // True when reflection can give each argument to the parameter at its position. It gives an
    // array element to a params parameter as one value, and the walk does not follow a ref
    // parameter through the array.
    private static bool Fits(IMethodSymbol callee, ImmutableArray<ExpressionSyntax> arguments) =>
        callee.Parameters.Length == arguments.Length
        && callee.Parameters.All(static parameter => parameter is { RefKind: RefKind.None, IsParams: false });

    private static ExpressionSyntax WithoutAssertion(ExpressionSyntax expression) =>
        expression switch
        {
            PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } sure =>
                WithoutAssertion(sure.Operand),
            ParenthesizedExpressionSyntax parenthesized => WithoutAssertion(parenthesized.Expression),
            _ => expression
        };
}
