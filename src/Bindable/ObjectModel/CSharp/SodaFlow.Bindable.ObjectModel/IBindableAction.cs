using System.Windows.Input;
using JetBrains.Annotations;
using SodaFlow.Functional;

namespace SodaFlow.Bindable.ObjectModel;

/// <summary>
///     An <see cref="ICommand" /> whose invocations are exposed as a stream and whose enablement is
///     driven by a <see cref="Cell{T}" /> of <see cref="bool" />.
/// </summary>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public interface IBindableAction : IBindableAction<Unit>
{
}
