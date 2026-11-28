using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SodaFlow.Samples.Search.ViewModels;

/// <summary>Gives the entries that contain the text of a query.</summary>
/// <param name="query">The text that the user typed.</param>
/// <param name="cancellationToken">
///     Becomes canceled when a new search replaces this search, or when the
///     user cancels.
/// </param>
/// <remarks>
///     This is a delegate type and not a <c>Func</c>, thus each call names its
///     arguments, as the other code in this sample does.
/// </remarks>
public delegate Task<IReadOnlyList<string>> CatalogSearch(string query, CancellationToken cancellationToken);
