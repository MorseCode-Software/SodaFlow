using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

/// <summary>
///     The options of one call of a serializer that change which members it
///     writes. The walk reads the options of <c>System.Text.Json</c> from an
///     object creation with an initializer at the call. Options from elsewhere
///     get the careful value of each: the serializer writes fields and
///     read-only members, and has no converter that the walk knows.
/// </summary>
/// <remarks>
///     <c>DefaultIgnoreCondition</c> changes no getter that the serializer
///     calls. Each condition that it permits reads the value before it decides.
/// </remarks>
/// <param name="writesFields">True when the serializer writes the public fields.</param>
/// <param name="skipsReadOnlyProperties">True when it skips a property with no public setter.</param>
/// <param name="skipsReadOnlyFields">True when it skips a readonly field.</param>
/// <param name="converters">The converters of the options, each as the code makes it.</param>
internal sealed class SerializerOptions(
    in bool writesFields,
    in bool skipsReadOnlyProperties,
    in bool skipsReadOnlyFields,
    in ImmutableArray<ExpressionSyntax> converters)
{
    public bool WritesFields { get; } = writesFields;

    public bool SkipsReadOnlyProperties { get; } = skipsReadOnlyProperties;

    public bool SkipsReadOnlyFields { get; } = skipsReadOnlyFields;

    public ImmutableArray<ExpressionSyntax> Converters { get; } = converters;

    /// <summary>
    ///     The options of one call. Json.NET writes the public fields, and
    ///     <c>XmlSerializer</c> writes those that are not readonly. For
    ///     <c>System.Text.Json</c>, the walk reads the options of the argument for
    ///     a parameter of type <c>JsonSerializerOptions</c>. An argument of
    ///     metadata from a context gives options from elsewhere.
    /// </summary>
    /// <param name="region">The region, for the type of an argument.</param>
    /// <param name="method">The method that the code calls.</param>
    /// <param name="arguments">The arguments of the call.</param>
    public static SerializerOptions Of(
        LoopRegion region,
        IMethodSymbol method,
        SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        if (!SodaFlowSymbols.WritesTheDeclaredType(method))
        {
            return new SerializerOptions(
                writesFields: true,
                skipsReadOnlyProperties: false,
                skipsReadOnlyFields: method.ContainingType?.Name == "XmlSerializer",
                converters: ImmutableArray<ExpressionSyntax>.Empty);
        }

        return arguments.Aggregate(
            seed: Defaults,
            func: (result, argument) =>
                LoopStateEvaluator.ParameterFor(
                        method: method,
                        argument: argument,
                        position: arguments.IndexOf(argument))
                    ?.Type.Name switch
                    {
                        "JsonSerializerOptions" => Read(region: region, options: argument.Expression),
                        "JsonTypeInfo" or "JsonSerializerContext" => Elsewhere,
                        _ => result
                    });
    }

    // The options of System.Text.Json with no change.
    private static SerializerOptions Defaults { get; } =
        new(
            writesFields: false,
            skipsReadOnlyProperties: false,
            skipsReadOnlyFields: false,
            converters: ImmutableArray<ExpressionSyntax>.Empty);

    // The careful value of each option, for options that the walk cannot read.
    private static SerializerOptions Elsewhere { get; } =
        new(
            writesFields: true,
            skipsReadOnlyProperties: false,
            skipsReadOnlyFields: false,
            converters: ImmutableArray<ExpressionSyntax>.Empty);

    // A creation with no argument, or with only JsonSerializerDefaults, starts from the defaults.
    // Each other value is options from elsewhere, as a copy of other options is.
    private static SerializerOptions Read(LoopRegion region, ExpressionSyntax options)
    {
        if (LoopStateEvaluator.Unwrapped(options) is not BaseObjectCreationExpressionSyntax made
            || made.ArgumentList?.Arguments.Any(argument =>
                region.TypeOf(argument.Expression)?.Name != "JsonSerializerDefaults") == true)
        {
            return Elsewhere;
        }

        bool writesFields = false;
        bool skipsReadOnlyProperties = false;
        bool skipsReadOnlyFields = false;
        ImmutableArray<ExpressionSyntax> converters = ImmutableArray<ExpressionSyntax>.Empty;

        foreach (AssignmentExpressionSyntax set in made.Initializer?.Expressions.OfType<AssignmentExpressionSyntax>()
                                                    ?? [])
        {
            // A value that is not a literal can be true or false. The careful value writes more.
            switch (set.Left)
            {
                case IdentifierNameSyntax { Identifier.ValueText: "IncludeFields" }:
                    writesFields = !set.Right.IsKind(SyntaxKind.FalseLiteralExpression);

                    break;
                case IdentifierNameSyntax { Identifier.ValueText: "IgnoreReadOnlyProperties" }:
                    skipsReadOnlyProperties = set.Right.IsKind(SyntaxKind.TrueLiteralExpression);

                    break;
                case IdentifierNameSyntax { Identifier.ValueText: "IgnoreReadOnlyFields" }:
                    skipsReadOnlyFields = set.Right.IsKind(SyntaxKind.TrueLiteralExpression);

                    break;
                case IdentifierNameSyntax { Identifier.ValueText: "Converters" }
                    when set.Right is InitializerExpressionSyntax added:
                    converters = added.Expressions.ToImmutableArray();

                    break;
            }
        }

        return new SerializerOptions(
            writesFields: writesFields,
            skipsReadOnlyProperties: skipsReadOnlyProperties,
            skipsReadOnlyFields: skipsReadOnlyFields,
            converters: converters);
    }
}
