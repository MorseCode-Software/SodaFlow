using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SodaFlow.Samples.Search.ViewModels;

/// <summary>
///     This replaces the true target of a call: a web server, a database, or a process. It
///     sleeps, thus a user can see the asynchronous behavior. It also obeys its cancellation
///     token, thus a cancellation stops the work and does not only discard the result.
/// </summary>
internal static class Catalog
{
    private static readonly string[] Entries =
    [
        "Amsterdam",
        "Athens",
        "Auckland",
        "Bangkok",
        "Barcelona",
        "Berlin",
        "Bogota",
        "Boston",
        "Brisbane",
        "Brussels",
        "Budapest",
        "Buenos Aires",
        "Cairo",
        "Cape Town",
        "Chicago",
        "Copenhagen",
        "Dakar",
        "Delhi",
        "Dubai",
        "Dublin",
        "Edinburgh",
        "Hanoi",
        "Helsinki",
        "Istanbul",
        "Jakarta",
        "Johannesburg",
        "Kyoto",
        "Lagos",
        "Lima",
        "Lisbon",
        "London",
        "Los Angeles",
        "Madrid",
        "Manila",
        "Melbourne",
        "Mexico City",
        "Montreal",
        "Moscow",
        "Mumbai",
        "Nairobi",
        "New York",
        "Osaka",
        "Oslo",
        "Paris",
        "Prague",
        "Reykjavik",
        "Rio de Janeiro",
        "Rome",
        "San Francisco",
        "Santiago",
        "Sao Paulo",
        "Seoul",
        "Shanghai",
        "Singapore",
        "Stockholm",
        "Sydney",
        "Taipei",
        "Tokyo",
        "Toronto",
        "Vancouver",
        "Vienna",
        "Warsaw",
        "Wellington",
        "Zurich"
    ];

    /// <summary>Matches entries containing <paramref name="query" />, slowly.</summary>
    /// <exception cref="InvalidOperationException">
    ///     This occurs for the query "fail", thus the sample can show the error path.
    /// </exception>
    public static async Task<IReadOnlyList<string>> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        await Task.Delay(delay: TimeSpan.FromMilliseconds(600), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // This code trims one time. The predicate below runs against each entry in the catalog.
        // A call to Trim in the predicate did that work approximately sixty times for each search,
        // and Trim gives a new string at each call with a space to remove.
        string trimmed = query.Trim();

        if (string.Equals(a: trimmed, b: "fail", comparisonType: StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The catalog is unavailable. Try again shortly.");
        }

        return
        [
            .. Entries.Where(e => e.Contains(value: trimmed, comparisonType: StringComparison.OrdinalIgnoreCase))
        ];
    }
}
