using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

/// <summary>Questions about symbols that the analyzer asks of the SodaFlow assemblies.</summary>
internal static class SodaFlowSymbols
{
    private static readonly ImmutableHashSet<string> PendingPlaceholderTypes =
        ImmutableHashSet.Create("LoopedCell", "CellLoop", "LoopedBehavior", "BehaviorLoop");

    private static readonly ImmutableHashSet<string> StreamPlaceholderTypes =
        ImmutableHashSet.Create("LoopedStream", "StreamLoop");

    private static readonly ImmutableHashSet<string> LooperTypes =
        ImmutableHashSet.Create("CellLooper", "BehaviorLooper", "StreamLooper", "ForwardReference");

    /// <summary>
    ///     The members that read the current value of a cell, a behavior, or a
    ///     bindable built from one.
    /// </summary>
    public static readonly ImmutableHashSet<string> ReadMembers = ImmutableHashSet.Create("Sample", "Value", "CanExecute");

    /// <summary>
    ///     A member of one of the library's own assemblies, and not of a test, a
    ///     benchmark, or a sample, which share the prefix.
    /// </summary>
    /// <remarks>
    ///     A prefix is a prototype's rule. A shipped analyzer marks the library
    ///     assemblies with an attribute and reads that.
    /// </remarks>
    public static bool IsSodaFlow(ISymbol? symbol)
    {
        string? name = symbol?.ContainingAssembly?.Name;

        if (name == null)
        {
            return false;
        }

        if (name == "SodaFlow")
        {
            return true;
        }

        return name.StartsWith("SodaFlow.", StringComparison.Ordinal)
            && !name.Contains(".Tests")
            && !name.Contains(".Benchmarks")
            && !name.Contains(".Samples.");
    }

    public static bool IsLooper(INamedTypeSymbol type) =>
        IsSodaFlow(type) && LooperTypes.Contains(type.OriginalDefinition.Name);

    /// <summary>
    ///     The state that an expression has from its type alone. Only a placeholder
    ///     has a placeholder type, so the type says that the value is pending.
    /// </summary>
    public static LoopState StateOfType(ITypeSymbol? type) => StateOfType(type: type, depth: 0);

    private static LoopState StateOfType(ITypeSymbol? type, int depth)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return StateOfType(type: array.ElementType, depth: depth + 1);
            case INamedTypeSymbol named:
            {
                if (IsSodaFlow(named))
                {
                    string name = named.OriginalDefinition.Name;

                    if (PendingPlaceholderTypes.Contains(name))
                    {
                        return LoopState.PendingPlaceholder;
                    }

                    if (StreamPlaceholderTypes.Contains(name))
                    {
                        return LoopState.StreamPlaceholder;
                    }
                }

                // One level into a collection type: a list of placeholders is pending as one value,
                // which is what a list Lift over it reads.
                if (depth == 0 && !IsSodaFlow(named))
                {
                    LoopState state = LoopState.None;

                    foreach (ITypeSymbol argument in named.TypeArguments)
                    {
                        state = state.Join(StateOfType(type: argument, depth: depth + 1));
                    }

                    return state;
                }

                return LoopState.None;
            }
            default:
                return LoopState.None;
        }
    }

    /// <summary>The parameter that an argument binds to, by name or by position.</summary>
    public static IParameterSymbol? ParameterOf(IMethodSymbol callee, ArgumentSyntax argument, int position)
    {
        if (argument.NameColon != null)
        {
            string name = argument.NameColon.Name.Identifier.ValueText;

            foreach (IParameterSymbol parameter in callee.Parameters)
            {
                if (parameter.Name == name)
                {
                    return parameter;
                }
            }

            return null;
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
    ///     A type whose values can hold a pending read: a cell, a behavior, a lazy
    ///     sample, or a bindable.
    /// </summary>
    public static bool IsCarrierType(ITypeSymbol? type) =>
        type is INamedTypeSymbol named
        && IsSodaFlow(named)
        && !IsStreamName(named.OriginalDefinition.Name)
        && named.SpecialType == SpecialType.None
        && !IsListenerName(named.OriginalDefinition.Name);

    private static bool IsStreamName(string name) =>
        name is "Stream" or "StreamSink" or "LoopedStream" or "StreamLoop" or "CellStreamSink";

    private static bool IsCellOrBehaviorName(string name) =>
        name is "Cell" or "CellSink" or "LoopedCell" or "CellLoop" or "Behavior" or "BehaviorSink"
            or "LoopedBehavior" or "BehaviorLoop";

    private static bool IsListenerName(string name) =>
        name is "IListener" or "IStrongListener" or "IWeakListener" or "Unit" or "Maybe" or "Either";
}
