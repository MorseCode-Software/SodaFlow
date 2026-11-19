using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

namespace SodaFlow.Analyzers.Timing;

/// <summary>
///     The options of a command, each given as a name and a value. A name can
///     occur more than one time.
/// </summary>
internal sealed class Arguments
{
    private readonly ImmutableDictionary<string, ImmutableArray<string>> values;

    private Arguments(ImmutableDictionary<string, ImmutableArray<string>> values) => this.values = values;

    /// <summary>Reads the options that follow the command.</summary>
    /// <param name="arguments">
    ///     The text of each option, such as "--runs" and then
    ///     "10".
    /// </param>
    /// <returns>The options.</returns>
    public static Arguments Parse(IReadOnlyList<string> arguments)
    {
        if (arguments.Count % 2 != 0)
        {
            throw new ArgumentException("Each option must have a name and a value.");
        }

        IEnumerable<(string Name, string Value)> pairs =
            Enumerable.Range(start: 0, count: arguments.Count / 2)
                .Select(index => (Name: arguments[index * 2], Value: arguments[index * 2 + 1]));

        return new Arguments(
            pairs
                .Select(static pair =>
                    pair.Name.StartsWith(value: "--", comparisonType: StringComparison.Ordinal)
                        ? pair
                        : throw new ArgumentException($"An option name must start with \"--\": {pair.Name}"))
                .GroupBy(keySelector: static pair => pair.Name[2..], comparer: StringComparer.Ordinal)
                .ToImmutableDictionary(
                    keySelector: static group => group.Key,
                    elementSelector: static group => group.Select(static pair => pair.Value).ToImmutableArray(),
                    keyComparer: StringComparer.Ordinal));
    }

    /// <summary>Gives each value of an option.</summary>
    /// <param name="name">The name of the option, with no "--".</param>
    /// <returns>The values, in the order of the command line. There can be none.</returns>
    public ImmutableArray<string> All(string name) =>
        this.values.TryGetValue(key: name, value: out ImmutableArray<string> found) ? found : [];

    /// <summary>Gives the one value of an option that must occur one time.</summary>
    /// <param name="name">The name of the option, with no "--".</param>
    /// <returns>The value.</returns>
    public string One(string name) =>
        this.All(name) switch
        {
            [var value] => value,
            [] => throw new ArgumentException($"The option --{name} is necessary."),
            _ => throw new ArgumentException($"The option --{name} can occur only one time.")
        };

    /// <summary>Gives the one value of an option as an integer.</summary>
    /// <param name="name">The name of the option, with no "--".</param>
    /// <returns>The number.</returns>
    public int Integer(string name) =>
        int.Parse(s: this.One(name), style: NumberStyles.None, provider: CultureInfo.InvariantCulture);

    /// <summary>Gives the one value of an option as a number.</summary>
    /// <param name="name">The name of the option, with no "--".</param>
    /// <returns>The number.</returns>
    public double Number(string name) =>
        double.Parse(s: this.One(name), style: NumberStyles.AllowDecimalPoint, provider: CultureInfo.InvariantCulture);
}
