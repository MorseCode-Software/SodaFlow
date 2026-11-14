using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using SodaFlow.Functional;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Analyzers.Tests;

/// <summary>
///     Holds <see cref="SodaFlowSymbols.MakesQuery" /> to the platform and to
///     <c>SodaFlow.Functional</c>. The analyzer reads a query where something
///     enumerates it. Thus, the rule is correct only if each method that it
///     calls a query enumerates nothing in the call. Each test calls the
///     methods of one owner with a source that records an enumeration.
/// </summary>
internal sealed class EnumerableQueriesTests
{
    // The methods that do not make a query and do not enumerate their source: the one that
    // asks for a count without an enumeration.
    private static readonly HashSet<string> EnumerateNothing = ["TryGetNonEnumeratedCount"];

    [Test]
    public async Task EachMethodOfEnumerableEnumeratesInTheCallOnlyWhenTheRuleSaysSo()
    {
        (List<string> wrong, int withSource) = Check(types: [typeof(Enumerable)], libraries: []);

        await Assert.That(wrong).IsEmpty();

        // A test that calls nothing shows nothing.
        await Assert.That(withSource).IsGreaterThanOrEqualTo(200).Because("the test called the methods");
    }

    [Test]
    public async Task EachMethodOfFunctionalEnumeratesInTheCallOnlyWhenTheRuleSaysSo()
    {
        Assembly functional = typeof(Maybe<>).Assembly;

        (List<string> wrong, int withSource) =
            Check(
                types:
                [
                    .. functional.GetExportedTypes()
                        .Where(static type => type is { IsAbstract: true, IsSealed: true })
                ],
                libraries: [functional]);

        await Assert.That(wrong).IsEmpty();
        await Assert.That(withSource).IsGreaterThanOrEqualTo(15).Because("the test called the methods");
    }

    /// <summary>
    ///     Calls each public static method of the types, and compares what the
    ///     method did with what the rule says for it.
    /// </summary>
    /// <returns>
    ///     The methods for which the rule is not correct, and the number of
    ///     methods that the test called with a source.
    /// </returns>
    private static (List<string> Wrong, int WithSource) Check(
        IReadOnlyList<Type> types,
        IEnumerable<Assembly> libraries)
    {
        Compilation compilation =
            AnalyzerHarness.Compile(source: string.Empty, libraries: libraries, assemblyName: "Queries");

        Dictionary<string, bool> makesQuery = [];
        List<string> wrong = [];

        foreach (IGrouping<string, IMethodSymbol> overloads in types
                     .Select(type =>
                         compilation.GetTypeByMetadataName(type.FullName ?? type.Name)
                         ?? throw new InvalidOperationException($"The compilation has no {type}."))
                     .SelectMany(static type => type.GetMembers())
                     .OfType<IMethodSymbol>()
                     .Where(static method => method.DeclaredAccessibility == Accessibility.Public)
                     .GroupBy(keySelector: static method => method.Name, comparer: StringComparer.Ordinal))
        {
            bool[] answers = [.. overloads.Select(SodaFlowSymbols.MakesQuery).Distinct()];

            // The test reads the rule by the name of the method. That is correct only while
            // each overload of a name has the same answer.
            if (answers.Length != 1)
            {
                wrong.Add($"{overloads.Key}: the rule has two answers for the overloads of this name");

                continue;
            }

            makesQuery[overloads.Key] = answers[0];
        }

        int withSource = 0;

        foreach (MethodInfo method in types.SelectMany(static type =>
                     type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)))
        {
            if (!makesQuery.TryGetValue(key: method.Name, value: out bool waits)
                || Call(method) is not (var sources, var enumerated)
                || sources == 0)
            {
                continue;
            }

            withSource++;

            // The rule is not correct for a query method that enumerates, or for a different method that
            // does not.
            if (waits == enumerated && (waits || !EnumerateNothing.Contains(method.Name)))
            {
                wrong.Add(
                    waits
                        ? $"{method}: the rule says that it makes a query, but it enumerates in the call"
                        : $"{method}: the rule says that it enumerates in the call, but it does not");
            }
        }

        return (wrong, withSource);
    }

    /// <summary>
    ///     Calls the method with a source that records an enumeration for each
    ///     sequence parameter, and does not enumerate the result. Null for a method
    ///     that the test cannot call.
    /// </summary>
    private static (int Sources, bool Enumerated)? Call(MethodInfo method)
    {
        try
        {
            if (method.IsGenericMethodDefinition)
            {
                method =
                    method.MakeGenericMethod(
                        [.. method.GetGenericArguments().Select(static _ => typeof(object))]);
            }
        }
        catch (ArgumentException)
        {
            // A type parameter of the method cannot be object.
            return null;
        }

        List<Trip> sources = [];

        object?[] arguments =
        [
            .. method.GetParameters().Select(parameter => Argument(type: parameter.ParameterType, sources: sources))
        ];

        try
        {
            method.Invoke(obj: null, parameters: arguments);
        }
        catch (TargetInvocationException)
        {
            // A method such as First throws for a source with no element, after it enumerates.
        }

        return (sources.Count, sources.Any(static source => source.Enumerated));
    }

    private static object? Argument(Type type, ICollection<Trip> sources)
    {
        if (type.IsByRef)
        {
            return null;
        }

        if (type == typeof(IEnumerable))
        {
            return Made(element: typeof(object), sources: sources);
        }

        if (type.IsGenericType
            && type.GetGenericTypeDefinition() is { } definition
            && (definition == typeof(IEnumerable<>) || definition == typeof(IOrderedEnumerable<>)))
        {
            return Made(element: type.GetGenericArguments()[0], sources: sources);
        }

        if (typeof(Delegate).IsAssignableFrom(type))
        {
            MethodInfo invoke =
                type.GetMethod("Invoke") ?? throw new InvalidOperationException($"{type} has no Invoke method.");

            return Expression.Lambda(
                    delegateType: type,
                    body: Expression.Default(invoke.ReturnType),
                    parameters: invoke.GetParameters().Select(static p => Expression.Parameter(p.ParameterType)))
                .Compile();
        }

        if (type == typeof(int))
        {
            return 1;
        }

        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    private static Trip Made(Type element, ICollection<Trip> sources)
    {
        Trip source =
            (Trip?)Activator.CreateInstance(typeof(Trip<>).MakeGenericType(element))
            ?? throw new InvalidOperationException($"The test cannot make a source of {element}.");

        sources.Add(source);

        return source;
    }

    private abstract class Trip
    {
        public bool Enumerated { get; protected set; }
    }

    // A sequence with no element that records a call of GetEnumerator. It is its own ordered
    // sequence, for a method such as ThenBy.
    private sealed class Trip<T> : Trip, IOrderedEnumerable<T>
    {
        public IEnumerator<T> GetEnumerator()
        {
            this.Enumerated = true;

            return Enumerable.Empty<T>().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

        public IOrderedEnumerable<T> CreateOrderedEnumerable<TKey>(
            Func<T, TKey> keySelector,
            IComparer<TKey>? comparer,
            bool descending) =>
            this;
    }
}
