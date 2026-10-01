// ReSharper disable CheckNamespace - The compiler finds each type here by its full name, thus the namespace must be the namespace of the type in the runtime.
// ReSharper disable InheritdocConsiderUsage - Attribute documents nothing that these members do.

#if !NETCOREAPP3_0_OR_GREATER && !NETSTANDARD2_1_OR_GREATER
using JetBrains.Annotations;

namespace System.Diagnostics.CodeAnalysis;

/// <summary>
///     Gives that the parameter is not null when a method returns
///     <see cref="ReturnValue" />, at each type of the parameter that permits null.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
internal sealed class NotNullWhenAttribute : Attribute
{
    /// <summary>Initializes the attribute with the specified return value condition.</summary>
    /// <param name="returnValue">
    ///     The condition on the return value. When the method returns this value, the parameter
    ///     is not null.
    /// </param>
    public NotNullWhenAttribute(bool returnValue) => this.ReturnValue = returnValue;

    /// <summary>Gets the return value condition.</summary>
    [UsedImplicitly]
    public bool ReturnValue { get; }
}
#endif
