using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SodaFlow.Samples.Search
{
    /// <summary>
    ///     Stands in for whatever a real application would call: a web service, a database, a
    ///     process. It sleeps so the asynchronous behavior is visible, and it honors its
    ///     cancellation token, which is what makes cancellation actually stop work rather than
    ///     merely discard its result.
    /// </summary>
    public static class Catalog
    {
        private static readonly string[] Entries =
        {
            "Amsterdam", "Athens", "Auckland", "Bangkok", "Barcelona", "Berlin", "Bogota",
            "Boston", "Brisbane", "Brussels", "Budapest", "Buenos Aires", "Cairo", "Cape Town",
            "Chicago", "Copenhagen", "Dakar", "Delhi", "Dubai", "Dublin", "Edinburgh", "Hanoi",
            "Helsinki", "Istanbul", "Jakarta", "Johannesburg", "Kyoto", "Lagos", "Lima", "Lisbon",
            "London", "Los Angeles", "Madrid", "Manila", "Melbourne", "Mexico City", "Montreal",
            "Moscow", "Mumbai", "Nairobi", "New York", "Osaka", "Oslo", "Paris", "Prague",
            "Reykjavik", "Rio de Janeiro", "Rome", "San Francisco", "Santiago", "Sao Paulo",
            "Seoul", "Shanghai", "Singapore", "Stockholm", "Sydney", "Taipei", "Tokyo", "Toronto",
            "Vancouver", "Vienna", "Warsaw", "Wellington", "Zurich"
        };

        /// <summary>Matches entries containing <paramref name="query" />, slowly.</summary>
        /// <exception cref="InvalidOperationException">
        ///     Thrown for the query "fail", so the sample has a way to show the error path.
        /// </exception>
        public static async Task<IReadOnlyList<string>> SearchAsync(
            string query,
            CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(600), cancellationToken).ConfigureAwait(false);

            if (string.Equals(query.Trim(), "fail", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The catalog is unavailable. Try again shortly.");
            }

            return Entries
                .Where(e => e.IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
        }
    }
}
