using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

/// <summary>
///     Questions about symbols that the analyzer asks of the SodaFlow
///     assemblies.
/// </summary>
internal static class SodaFlowSymbols
{
    private static readonly ImmutableHashSet<string> PendingPlaceholderTypes =
        ImmutableHashSet.Create("LoopedCell", "LoopedBehavior");

    private static readonly ImmutableHashSet<string> StreamPlaceholderTypes =
        ImmutableHashSet.Create("LoopedStream");

    private static readonly ImmutableHashSet<string> LooperTypes =
        ImmutableHashSet.Create("CellLooper", "BehaviorLooper", "StreamLooper", "ForwardReference");

    /// <summary>
    ///     The members that read the current value of a cell, a behavior, or a
    ///     bindable built from one.
    /// </summary>
    public static readonly ImmutableHashSet<string> ReadMembers =
        ImmutableHashSet.Create("Sample", "Value", "CanExecute");

    /// <summary>
    ///     A member of one of the library's own assemblies, and not of a test, a
    ///     benchmark, or a sample, which share the prefix.
    /// </summary>
    /// <remarks>
    ///     A prefix is a prototype's rule. A shipped analyzer marks the library
    ///     assemblies with an attribute and reads that.
    /// </remarks>
    public static bool IsSodaFlow(ISymbol? symbol) =>
        symbol?.ContainingAssembly?.Name switch
        {
            null => false,
            "SodaFlow" => true,
            { } name => name.StartsWith(value: "SodaFlow.", comparisonType: StringComparison.Ordinal)
                        && !name.Contains(".Tests")
                        && !name.Contains(".Benchmarks")
                        && !name.Contains(".Samples.")
        };

    /// <summary>
    ///     A method that runs a loop block. The public blocks are
    ///     <c>WithCaptures</c> and <c>WithoutCaptures</c> on a looper type. The
    ///     assemblies that build on Core use <c>WithCapturesImpl</c> and
    ///     <c>WithoutCapturesImpl</c> on <c>LoopedCell</c>, which are internal.
    /// </summary>
    public static bool IsLoopBlock(IMethodSymbol method) =>
        IsLoopBlockName(method.Name)
        && IsSodaFlow(method.ContainingType)
        && method.Name switch
        {
            "WithCaptures" or "WithoutCaptures" =>
                LooperTypes.Contains(method.ContainingType.OriginalDefinition.Name),
            "WithCapturesImpl" or "WithoutCapturesImpl" =>
                method.ContainingType.OriginalDefinition.Name == "LoopedCell",
            _ => false
        };

    /// <summary>
    ///     True for each name that <see cref="IsLoopBlock" /> accepts. The name of
    ///     an invocation is in its syntax, thus this check needs no symbol.
    /// </summary>
    public static bool IsLoopBlockName(string name) =>
        name is "WithCaptures" or "WithoutCaptures" or "WithCapturesImpl" or "WithoutCapturesImpl";

    /// <summary>
    ///     The state that an expression has from its type alone. Only a
    ///     placeholder has a placeholder type, so the type says that the value is
    ///     pending.
    /// </summary>
    public static LoopState StateOfType(ITypeSymbol? type) => StateOfType(type: type, depth: 0);

    private static LoopState StateOfType(ITypeSymbol? type, int depth)
    {
        // An array of placeholders is pending as one value, as a list of them is.
        while (type is IArrayTypeSymbol array)
        {
            type = array.ElementType;
            depth++;
        }

        if (type is not INamedTypeSymbol named)
        {
            return LoopState.None;
        }

        if (IsSodaFlow(named))
        {
            string name = named.OriginalDefinition.Name;

            if (PendingPlaceholderTypes.Contains(name))
            {
                return LoopState.PendingPlaceholder;
            }

            return StreamPlaceholderTypes.Contains(name) ? LoopState.StreamPlaceholder : LoopState.None;
        }

        // One level into a collection type: a list of placeholders is pending as one value, which
        // is what a list Lift over it reads.
        return depth == 0
            ? named.TypeArguments.Aggregate(
                seed: LoopState.None,
                func: static (state, argument) => state.Join(StateOfType(type: argument, depth: 1)))
            : LoopState.None;
    }

    /// <summary>The parameter that an argument binds to, by name or by position.</summary>
    public static IParameterSymbol? ParameterOf(IMethodSymbol callee, ArgumentSyntax argument, int position)
    {
        if (argument.NameColon != null)
        {
            string name = argument.NameColon.Name.Identifier.ValueText;

            return callee.Parameters.FirstOrDefault(parameter => parameter.Name == name);
        }

        if (position < callee.Parameters.Length)
        {
            return callee.Parameters[position];
        }

        return callee.Parameters.Length > 0 && callee.Parameters[callee.Parameters.Length - 1].IsParams
            ? callee.Parameters[callee.Parameters.Length - 1]
            : null;
    }

    public static bool IsStreamType(ITypeSymbol? type) =>
        type is INamedTypeSymbol named && IsSodaFlow(named) && IsStreamName(named.OriginalDefinition.Name);

    public static bool IsCellOrBehaviorType(ITypeSymbol? type) =>
        type is INamedTypeSymbol named && IsSodaFlow(named) && IsCellOrBehaviorName(named.OriginalDefinition.Name);

    public static bool IsLazySampleType(ITypeSymbol? type) =>
        type is INamedTypeSymbol { OriginalDefinition.Name: "LazySample" } named && IsSodaFlow(named);

    /// <summary>
    ///     A type whose values can hold a pending read: a cell, a behavior, a
    ///     lazy sample, or a bindable.
    /// </summary>
    public static bool IsCarrierType(ITypeSymbol? type) =>
        type is INamedTypeSymbol named
        && IsSodaFlow(named)
        && !IsStreamName(named.OriginalDefinition.Name)
        && named.SpecialType == SpecialType.None
        && !IsListenerName(named.OriginalDefinition.Name);

    private static bool IsStreamName(string name) =>
        name is "Stream" or "StreamSink" or "LoopedStream" or "CellStreamSink";

    private static bool IsCellOrBehaviorName(string name) =>
        name is "Cell"
            or "CellSink"
            or "LoopedCell"
            or "Behavior"
            or "BehaviorSink"
            or "LoopedBehavior";

    private static bool IsListenerName(string name) =>
        name is "IListener" or "IStrongListener" or "IWeakListener" or "Unit" or "Maybe" or "Either";
}
