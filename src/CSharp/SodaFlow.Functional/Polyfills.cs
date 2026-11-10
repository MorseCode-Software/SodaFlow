// ReSharper disable CheckNamespace - The compiler finds each type here by its full name, thus the namespace must be the namespace of the type in the runtime.
// ReSharper disable InheritdocConsiderUsage - Attribute documents nothing that these members do.

#if !NET5_0_OR_GREATER
using JetBrains.Annotations;

namespace System.Diagnostics.CodeAnalysis;

#if !NETCOREAPP3_0_OR_GREATER && !NETSTANDARD2_1_OR_GREATER
/// <summary>
///     Specifies that the parameter is not null when a method returns
///     <see cref="ReturnValue" />, and that this holds when the type of the
///     parameter permits null.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
internal sealed class NotNullWhenAttribute : Attribute
{
    /// <summary>Initializes the attribute with the specified return value condition.</summary>
    /// <param name="returnValue">
    ///     The return value condition. If the method returns this value, the parameter
    ///     is not null.
    /// </param>
    public NotNullWhenAttribute(bool returnValue) => this.ReturnValue = returnValue;

    /// <summary>Gets the return value condition.</summary>
    [UsedImplicitly]
    public bool ReturnValue { get; }
}
#endif

/// <summary>
///     Stops a report of the trim analysis for the code that it is on. The compiler
///     keeps it in the assembly, thus the trimmer also reads it later.
/// </summary>
/// <remarks>
///     The trimmer finds the attribute by its full name. The net6.0 and net8.0
///     builds use the type of the runtime. This copy lets the net472 and
///     netstandard2.0 builds compile the same source.
/// </remarks>
[AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = true)]
internal sealed class UnconditionalSuppressMessageAttribute : Attribute
{
    /// <summary>Initializes the attribute with the category and the identifier of the report.</summary>
    /// <param name="category">The category of the report.</param>
    /// <param name="checkId">The identifier of the report.</param>
    public UnconditionalSuppressMessageAttribute(string category, string checkId)
    {
        this.Category = category;
        this.CheckId = checkId;
    }

    /// <summary>Gets the category of the report.</summary>
    [UsedImplicitly]
    public string Category { get; }

    /// <summary>Gets the identifier of the report.</summary>
    [UsedImplicitly]
    public string CheckId { get; }

    /// <summary>Gets or sets the text that tells why the report does not apply.</summary>
    [UsedImplicitly]
    public string? Justification { get; set; }
}
#endif
