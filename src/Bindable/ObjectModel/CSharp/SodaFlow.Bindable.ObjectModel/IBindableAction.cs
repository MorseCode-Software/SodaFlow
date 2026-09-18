using System.Windows.Input;
using JetBrains.Annotations;
using SodaFlow.Functional;

namespace SodaFlow.Bindable.ObjectModel;

/// <summary>
///     An <see cref="ICommand" /> that supplies its calls as a stream. A
///     <see cref="Cell{T}" /> of <see cref="bool" /> controls when the command is available.
/// </summary>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public interface IBindableAction : IBindableAction<Unit>
{
}
