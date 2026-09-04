using System;
using System.Globalization;
using JetBrains.Annotations;

namespace SodaFlow.Functional;

/// <summary>
///     The parsing operations on a string which answer with a <see cref="Maybe{T}" />.
/// </summary>
/// <remarks>
///     Each of these wraps the framework's own <c>TryParse</c> for the type it names, so what
///     counts as parseable is exactly what that method accepts. What changes is the shape of the
///     answer: a <see cref="Maybe{T}" /> which can be mapped, filtered and combined, in place of
///     a <see cref="bool" /> and an output parameter which cannot be used until the
///     <see cref="bool" /> has been checked.
///     The overloads which take no <see cref="IFormatProvider" /> use the current culture, again
///     matching the method being wrapped. Pass <see cref="CultureInfo.InvariantCulture" />
///     explicitly for text which is not meant to follow the user's culture - a configuration
///     file, a wire format, a machine-written log.
///     A <see langword="null" /> string parses as no value throughout, since that is what every
///     framework <c>TryParse</c> does with one.
/// </remarks>
[PublicAPI]
public static class StringExtensionMethods
{
    /// <summary>
    ///     Parses a <see cref="byte" /> from this string, if it holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="byte.TryParse(string,out byte)" />, so this reads
    ///     <c>NumberStyles.Integer</c> in the current culture. Use
    ///     <see cref="TryParseByte(string,NumberStyles,IFormatProvider)" /> to say otherwise.
    /// </remarks>
    [Pure]
    public static Maybe<byte> TryParseByte(this string? value) =>
        byte.TryParse(s: value, result: out byte result) ? Maybe.Some(result) : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="byte" /> from this string in the given style and culture, if it
    ///     holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <param name="styles">The number styles permitted in <paramref name="value" />.</param>
    /// <param name="provider">The culture-specific formatting information to read with.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="byte.TryParse(string,NumberStyles,IFormatProvider,out byte)" />.
    /// </remarks>
    [Pure]
    public static Maybe<byte> TryParseByte(this string? value, NumberStyles styles, IFormatProvider provider) =>
        byte.TryParse(s: value, style: styles, provider: provider, result: out byte result)
            ? Maybe.Some(result)
            : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="sbyte" /> from this string, if it holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="sbyte.TryParse(string,out sbyte)" />, so this reads
    ///     <c>NumberStyles.Integer</c> in the current culture. Use
    ///     <see cref="TryParseSByte(string,NumberStyles,IFormatProvider)" /> to say otherwise.
    /// </remarks>
    [Pure]
    public static Maybe<sbyte> TryParseSByte(this string? value) =>
        sbyte.TryParse(s: value, result: out sbyte result) ? Maybe.Some(result) : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="sbyte" /> from this string in the given style and culture, if it
    ///     holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <param name="styles">The number styles permitted in <paramref name="value" />.</param>
    /// <param name="provider">The culture-specific formatting information to read with.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="sbyte.TryParse(string,NumberStyles,IFormatProvider,out sbyte)" />.
    /// </remarks>
    [Pure]
    public static Maybe<sbyte> TryParseSByte(this string? value, NumberStyles styles, IFormatProvider provider) =>
        sbyte.TryParse(s: value, style: styles, provider: provider, result: out sbyte result)
            ? Maybe.Some(result)
            : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="short" /> from this string, if it holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="short.TryParse(string,out short)" />, so this reads
    ///     <c>NumberStyles.Integer</c> in the current culture. Use
    ///     <see cref="TryParseInt16(string,NumberStyles,IFormatProvider)" /> to say otherwise.
    /// </remarks>
    [Pure]
    public static Maybe<short> TryParseInt16(this string? value) =>
        short.TryParse(s: value, result: out short result) ? Maybe.Some(result) : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="short" /> from this string in the given style and culture, if it
    ///     holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <param name="styles">The number styles permitted in <paramref name="value" />.</param>
    /// <param name="provider">The culture-specific formatting information to read with.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="short.TryParse(string,NumberStyles,IFormatProvider,out short)" />.
    /// </remarks>
    [Pure]
    public static Maybe<short> TryParseInt16(this string? value, NumberStyles styles, IFormatProvider provider) =>
        short.TryParse(s: value, style: styles, provider: provider, result: out short result)
            ? Maybe.Some(result)
            : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="ushort" /> from this string, if it holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="ushort.TryParse(string,out ushort)" />, so this reads
    ///     <c>NumberStyles.Integer</c> in the current culture. Use
    ///     <see cref="TryParseUInt16(string,NumberStyles,IFormatProvider)" /> to say otherwise.
    /// </remarks>
    [Pure]
    public static Maybe<ushort> TryParseUInt16(this string? value) =>
        ushort.TryParse(s: value, result: out ushort result) ? Maybe.Some(result) : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="ushort" /> from this string in the given style and culture, if it
    ///     holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <param name="styles">The number styles permitted in <paramref name="value" />.</param>
    /// <param name="provider">The culture-specific formatting information to read with.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="ushort.TryParse(string,NumberStyles,IFormatProvider,out ushort)" />.
    /// </remarks>
    [Pure]
    public static Maybe<ushort> TryParseUInt16(this string? value, NumberStyles styles, IFormatProvider provider) =>
        ushort.TryParse(s: value, style: styles, provider: provider, result: out ushort result)
            ? Maybe.Some(result)
            : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="int" /> from this string, if it holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="int.TryParse(string,out int)" />, so this reads
    ///     <c>NumberStyles.Integer</c> in the current culture. Use
    ///     <see cref="TryParseInt32(string,NumberStyles,IFormatProvider)" /> to say otherwise.
    /// </remarks>
    [Pure]
    public static Maybe<int> TryParseInt32(this string? value) =>
        int.TryParse(s: value, result: out int result) ? Maybe.Some(result) : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="int" /> from this string in the given style and culture, if it
    ///     holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <param name="styles">The number styles permitted in <paramref name="value" />.</param>
    /// <param name="provider">The culture-specific formatting information to read with.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="int.TryParse(string,NumberStyles,IFormatProvider,out int)" />.
    /// </remarks>
    [Pure]
    public static Maybe<int> TryParseInt32(this string? value, NumberStyles styles, IFormatProvider provider) =>
        int.TryParse(s: value, style: styles, provider: provider, result: out int result)
            ? Maybe.Some(result)
            : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="uint" /> from this string, if it holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="uint.TryParse(string,out uint)" />, so this reads
    ///     <c>NumberStyles.Integer</c> in the current culture. Use
    ///     <see cref="TryParseUInt32(string,NumberStyles,IFormatProvider)" /> to say otherwise.
    /// </remarks>
    [Pure]
    public static Maybe<uint> TryParseUInt32(this string? value) =>
        uint.TryParse(s: value, result: out uint result) ? Maybe.Some(result) : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="uint" /> from this string in the given style and culture, if it
    ///     holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <param name="styles">The number styles permitted in <paramref name="value" />.</param>
    /// <param name="provider">The culture-specific formatting information to read with.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="uint.TryParse(string,NumberStyles,IFormatProvider,out uint)" />.
    /// </remarks>
    [Pure]
    public static Maybe<uint> TryParseUInt32(this string? value, NumberStyles styles, IFormatProvider provider) =>
        uint.TryParse(s: value, style: styles, provider: provider, result: out uint result)
            ? Maybe.Some(result)
            : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="long" /> from this string, if it holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="long.TryParse(string,out long)" />, so this reads
    ///     <c>NumberStyles.Integer</c> in the current culture. Use
    ///     <see cref="TryParseInt64(string,NumberStyles,IFormatProvider)" /> to say otherwise.
    /// </remarks>
    [Pure]
    public static Maybe<long> TryParseInt64(this string? value) =>
        long.TryParse(s: value, result: out long result) ? Maybe.Some(result) : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="long" /> from this string in the given style and culture, if it
    ///     holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <param name="styles">The number styles permitted in <paramref name="value" />.</param>
    /// <param name="provider">The culture-specific formatting information to read with.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="long.TryParse(string,NumberStyles,IFormatProvider,out long)" />.
    /// </remarks>
    [Pure]
    public static Maybe<long> TryParseInt64(this string? value, NumberStyles styles, IFormatProvider provider) =>
        long.TryParse(s: value, style: styles, provider: provider, result: out long result)
            ? Maybe.Some(result)
            : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="ulong" /> from this string, if it holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="ulong.TryParse(string,out ulong)" />, so this reads
    ///     <c>NumberStyles.Integer</c> in the current culture. Use
    ///     <see cref="TryParseUInt64(string,NumberStyles,IFormatProvider)" /> to say otherwise.
    /// </remarks>
    [Pure]
    public static Maybe<ulong> TryParseUInt64(this string? value) =>
        ulong.TryParse(s: value, result: out ulong result) ? Maybe.Some(result) : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="ulong" /> from this string in the given style and culture, if it
    ///     holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <param name="styles">The number styles permitted in <paramref name="value" />.</param>
    /// <param name="provider">The culture-specific formatting information to read with.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="ulong.TryParse(string,NumberStyles,IFormatProvider,out ulong)" />.
    /// </remarks>
    [Pure]
    public static Maybe<ulong> TryParseUInt64(this string? value, NumberStyles styles, IFormatProvider provider) =>
        ulong.TryParse(s: value, style: styles, provider: provider, result: out ulong result)
            ? Maybe.Some(result)
            : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="float" /> from this string, if it holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="float.TryParse(string,out float)" />, so this reads
    ///     <c>NumberStyles.Float | NumberStyles.AllowThousands</c> in the current culture. Use
    ///     <see cref="TryParseSingle(string,NumberStyles,IFormatProvider)" /> to say otherwise.
    /// </remarks>
    [Pure]
    public static Maybe<float> TryParseSingle(this string? value) =>
        float.TryParse(s: value, result: out float result) ? Maybe.Some(result) : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="float" /> from this string in the given style and culture, if it
    ///     holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <param name="styles">The number styles permitted in <paramref name="value" />.</param>
    /// <param name="provider">The culture-specific formatting information to read with.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="float.TryParse(string,NumberStyles,IFormatProvider,out float)" />.
    /// </remarks>
    [Pure]
    public static Maybe<float> TryParseSingle(this string? value, NumberStyles styles, IFormatProvider provider) =>
        float.TryParse(s: value, style: styles, provider: provider, result: out float result)
            ? Maybe.Some(result)
            : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="double" /> from this string, if it holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="double.TryParse(string,out double)" />, so this reads
    ///     <c>NumberStyles.Float | NumberStyles.AllowThousands</c> in the current culture. Use
    ///     <see cref="TryParseDouble(string,NumberStyles,IFormatProvider)" /> to say otherwise.
    /// </remarks>
    [Pure]
    public static Maybe<double> TryParseDouble(this string? value) =>
        double.TryParse(s: value, result: out double result) ? Maybe.Some(result) : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="double" /> from this string in the given style and culture, if it
    ///     holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <param name="styles">The number styles permitted in <paramref name="value" />.</param>
    /// <param name="provider">The culture-specific formatting information to read with.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="double.TryParse(string,NumberStyles,IFormatProvider,out double)" />.
    /// </remarks>
    [Pure]
    public static Maybe<double> TryParseDouble(this string? value, NumberStyles styles, IFormatProvider provider) =>
        double.TryParse(s: value, style: styles, provider: provider, result: out double result)
            ? Maybe.Some(result)
            : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="decimal" /> from this string, if it holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="decimal.TryParse(string,out decimal)" />, so this reads
    ///     <c>NumberStyles.Number</c> in the current culture. Use
    ///     <see cref="TryParseDecimal(string,NumberStyles,IFormatProvider)" /> to say otherwise.
    /// </remarks>
    [Pure]
    public static Maybe<decimal> TryParseDecimal(this string? value) =>
        decimal.TryParse(s: value, result: out decimal result) ? Maybe.Some(result) : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="decimal" /> from this string in the given style and culture, if it
    ///     holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <param name="styles">The number styles permitted in <paramref name="value" />.</param>
    /// <param name="provider">The culture-specific formatting information to read with.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="decimal.TryParse(string,NumberStyles,IFormatProvider,out decimal)" />.
    /// </remarks>
    [Pure]
    public static Maybe<decimal> TryParseDecimal(this string? value, NumberStyles styles, IFormatProvider provider) =>
        decimal.TryParse(s: value, style: styles, provider: provider, result: out decimal result)
            ? Maybe.Some(result)
            : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="bool" /> from this string, if it holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="bool.TryParse(string,out bool)" />, which accepts only
    ///     <c>True</c> and <c>False</c> in any casing, with surrounding whitespace allowed. It
    ///     does not accept <c>1</c>, <c>0</c>, <c>yes</c> or <c>no</c>, and is not
    ///     culture-sensitive.
    /// </remarks>
    [Pure]
    public static Maybe<bool> TryParseBoolean(this string? value) =>
        bool.TryParse(value: value, result: out bool result) ? Maybe.Some(result) : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="char" /> from this string, if it holds exactly one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the single character of
    ///     <paramref name="value" />, and one containing no value if it is not exactly one
    ///     character long.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="char.TryParse(string,out char)" />. A surrogate pair is two characters
    ///     and so gives no value.
    /// </remarks>
    [Pure]
    public static Maybe<char> TryParseChar(this string? value) =>
        char.TryParse(s: value, result: out char result) ? Maybe.Some(result) : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="Guid" /> from this string, if it holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="Guid.TryParse(string,out Guid)" />, so any of the framework's
    ///     recognized layouts is accepted. Use
    ///     <see cref="TryParseGuidExact(string,string)" /> to insist on one of them.
    /// </remarks>
    [Pure]
    public static Maybe<Guid> TryParseGuid(this string? value) =>
        Guid.TryParse(input: value, result: out Guid result) ? Maybe.Some(result) : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="Guid" /> from this string in exactly the given layout, if it holds
    ///     one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <param name="format">
    ///     The layout <paramref name="value" /> must be in: <c>N</c>, <c>D</c>, <c>B</c>,
    ///     <c>P</c> or <c>X</c>.
    /// </param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> is not in that layout.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="Guid.TryParseExact(string,string,out Guid)" />, which allows no
    ///     surrounding whitespace.
    /// </remarks>
    [Pure]
    public static Maybe<Guid> TryParseGuidExact(this string? value, string format) =>
        Guid.TryParseExact(input: value, format: format, result: out Guid result) ? Maybe.Some(result) : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="DateTime" /> from this string, if it holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="DateTime.TryParse(string,out DateTime)" />, so this reads the current
    ///     culture. Date text is where that matters most - <c>03/04/2026</c> is two different days
    ///     depending on the culture reading it - so prefer
    ///     <see cref="TryParseDateTime(string,IFormatProvider,DateTimeStyles)" /> for anything not
    ///     typed by the user.
    /// </remarks>
    [Pure]
    public static Maybe<DateTime> TryParseDateTime(this string? value) =>
        DateTime.TryParse(s: value, result: out DateTime result) ? Maybe.Some(result) : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="DateTime" /> from this string in the given culture and style, if it
    ///     holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <param name="provider">The culture-specific formatting information to read with.</param>
    /// <param name="styles">The formatting options permitted in <paramref name="value" />.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="DateTime.TryParse(string,IFormatProvider,DateTimeStyles,out DateTime)" />.
    /// </remarks>
    [Pure]
    public static Maybe<DateTime> TryParseDateTime(
        this string value,
        IFormatProvider provider,
        DateTimeStyles styles) =>
        DateTime.TryParse(s: value, provider: provider, styles: styles, result: out DateTime result)
            ? Maybe.Some(result)
            : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="DateTime" /> from this string in exactly the given format, if it
    ///     holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <param name="format">The format <paramref name="value" /> must be in.</param>
    /// <param name="provider">The culture-specific formatting information to read with.</param>
    /// <param name="styles">The formatting options permitted in <paramref name="value" />.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> is not in that format.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="DateTime.TryParseExact(string,string,IFormatProvider,DateTimeStyles,out DateTime)" />.
    /// </remarks>
    [Pure]
    public static Maybe<DateTime> TryParseDateTimeExact(
        this string value,
        string format,
        IFormatProvider provider,
        DateTimeStyles styles) =>
        DateTime.TryParseExact(s: value, format: format, provider: provider, style: styles, result: out DateTime result)
            ? Maybe.Some(result)
            : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="DateTimeOffset" /> from this string, if it holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="DateTimeOffset.TryParse(string,out DateTimeOffset)" />, so this reads
    ///     the current culture.
    /// </remarks>
    [Pure]
    public static Maybe<DateTimeOffset> TryParseDateTimeOffset(this string? value) =>
        DateTimeOffset.TryParse(input: value, result: out DateTimeOffset result) ? Maybe.Some(result) : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="DateTimeOffset" /> from this string in the given culture and style,
    ///     if it holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <param name="provider">The culture-specific formatting information to read with.</param>
    /// <param name="styles">The formatting options permitted in <paramref name="value" />.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="DateTimeOffset.TryParse(string,IFormatProvider,DateTimeStyles,out DateTimeOffset)" />.
    /// </remarks>
    [Pure]
    public static Maybe<DateTimeOffset> TryParseDateTimeOffset(
        this string value,
        IFormatProvider provider,
        DateTimeStyles styles) =>
        DateTimeOffset.TryParse(
            input: value,
            formatProvider: provider,
            styles: styles,
            result: out DateTimeOffset result)
            ? Maybe.Some(result)
            : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="TimeSpan" /> from this string, if it holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="TimeSpan.TryParse(string,out TimeSpan)" />, so this reads the current
    ///     culture.
    /// </remarks>
    [Pure]
    public static Maybe<TimeSpan> TryParseTimeSpan(this string? value) =>
        TimeSpan.TryParse(s: value, result: out TimeSpan result) ? Maybe.Some(result) : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="TimeSpan" /> from this string in the given culture, if it holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <param name="provider">The culture-specific formatting information to read with.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="TimeSpan.TryParse(string,IFormatProvider,out TimeSpan)" />.
    /// </remarks>
    [Pure]
    public static Maybe<TimeSpan> TryParseTimeSpan(this string? value, IFormatProvider provider) =>
        TimeSpan.TryParse(input: value, formatProvider: provider, result: out TimeSpan result)
            ? Maybe.Some(result)
            : Maybe.None;

    /// <summary>
    ///     Parses an absolute <see cref="Uri" /> from this string, if it holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold an absolute URI.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="Uri.TryCreate(string,UriKind,out Uri)" /> with
    ///     <see cref="UriKind.Absolute" />. Use
    ///     <see cref="TryParseUri(string,UriKind)" /> to accept a relative one.
    /// </remarks>
    [Pure]
    public static Maybe<Uri> TryParseUri(this string? value) => value.TryParseUri(UriKind.Absolute);

    /// <summary>
    ///     Parses a <see cref="Uri" /> of the given kind from this string, if it holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <param name="uriKind">Whether an absolute URI, a relative one, or either is acceptable.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold a URI of that kind.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="Uri.TryCreate(string,UriKind,out Uri)" />.
    /// </remarks>
    [Pure]
    public static Maybe<Uri> TryParseUri(this string? value, UriKind uriKind) =>
        Uri.TryCreate(uriString: value, uriKind: uriKind, result: out Uri? result) ? Maybe.Some(result) : Maybe.None;

    /// <summary>
    ///     Parses a value of the given enumeration type from this string, if it holds one.
    /// </summary>
    /// <typeparam name="TEnum">The enumeration type to parse.</typeparam>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="Enum.TryParse{TEnum}(string,out TEnum)" />, and inherits its two
    ///     surprises. Matching is case-sensitive, which
    ///     <see cref="TryParseEnum{TEnum}(string,bool)" /> can turn off. More importantly, a
    ///     string of digits parses to that number whether or not the enumeration declares it, so
    ///     <c>"37"</c> succeeds for an enumeration with three members - use
    ///     <see cref="TryParseDefinedEnum{TEnum}(string)" /> where only a declared member will do.
    /// </remarks>
    [Pure]
    public static Maybe<TEnum> TryParseEnum<TEnum>(this string? value)
        where TEnum : struct =>
        Enum.TryParse(value: value, result: out TEnum result) ? Maybe.Some(result) : Maybe.None;

    /// <summary>
    ///     Parses a value of the given enumeration type from this string, if it holds one,
    ///     optionally ignoring case.
    /// </summary>
    /// <typeparam name="TEnum">The enumeration type to parse.</typeparam>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <param name="ignoreCase">Whether to match member names without regard to case.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="Enum.TryParse{TEnum}(string,bool,out TEnum)" />. A string of digits
    ///     parses to that number whether or not the enumeration declares it; see
    ///     <see cref="TryParseDefinedEnum{TEnum}(string,bool)" />.
    /// </remarks>
    [Pure]
    public static Maybe<TEnum> TryParseEnum<TEnum>(this string? value, bool ignoreCase)
        where TEnum : struct =>
        Enum.TryParse(value: value, ignoreCase: ignoreCase, result: out TEnum result)
            ? Maybe.Some(result)
            : Maybe.None;

    /// <summary>
    ///     Parses a declared member of the given enumeration type from this string, if it holds
    ///     one.
    /// </summary>
    /// <typeparam name="TEnum">The enumeration type to parse.</typeparam>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value if the enumeration declares it,
    ///     and one containing no value otherwise.
    /// </returns>
    /// <remarks>
    ///     <see cref="TryParseEnum{TEnum}(string)" /> plus the check that the result is a member
    ///     the enumeration actually declares, which is what keeps <c>"37"</c> from parsing to a
    ///     value nothing will ever handle. This is the one to reach for when the string came from
    ///     outside the program.
    ///     Not for an enumeration marked <see cref="FlagsAttribute" />: a combination of declared
    ///     flags is a perfectly good value but is not itself declared, so it gives no value here.
    ///     Use <see cref="TryParseEnum{TEnum}(string)" /> for those.
    /// </remarks>
    [Pure]
    public static Maybe<TEnum> TryParseDefinedEnum<TEnum>(this string? value)
        where TEnum : struct =>
        value.TryParseEnum<TEnum>().Where(static v => Enum.IsDefined(enumType: typeof(TEnum), value: v));

    /// <summary>
    ///     Parses a declared member of the given enumeration type from this string, if it holds
    ///     one, optionally ignoring case.
    /// </summary>
    /// <typeparam name="TEnum">The enumeration type to parse.</typeparam>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <param name="ignoreCase">Whether to match member names without regard to case.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value if the enumeration declares it,
    ///     and one containing no value otherwise.
    /// </returns>
    /// <remarks>
    ///     Not for an enumeration marked <see cref="FlagsAttribute" />; see
    ///     <see cref="TryParseDefinedEnum{TEnum}(string)" />.
    /// </remarks>
    [Pure]
    public static Maybe<TEnum> TryParseDefinedEnum<TEnum>(this string? value, bool ignoreCase)
        where TEnum : struct =>
        value.TryParseEnum<TEnum>(ignoreCase).Where(static v => Enum.IsDefined(enumType: typeof(TEnum), value: v));
}
