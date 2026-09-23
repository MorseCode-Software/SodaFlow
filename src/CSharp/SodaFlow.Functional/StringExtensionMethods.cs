using System;
using System.Globalization;
using JetBrains.Annotations;

// Every method here is an extension on a string, and ReSharper offers to gather them into
// extension blocks. Declined for the file rather than method by method, because the cause applies
// to all of them, and to each one added after this.
//
// The block version emits the same public API: the same static methods, with the same
// signatures and the same ExtensionAttribute. But it rewrites the documentation, which is most
// of what this file is. Each public method then holds an <inheritdoc /> that points at a
// compiler-generated type whose name is a hash. Its summary, parameters, and returns move to
// that name. A reader of the XML that does not resolve inheritdoc then shows nothing for a
// method that this file documents in full. IntelliSense over a package reference is one.
// ReSharper disable ConvertToExtensionBlock

namespace SodaFlow.Functional;

/// <summary>
///     The parsing operations on a string which answer with a <see cref="Maybe{T}" />.
/// </summary>
/// <remarks>
///     Each of these wraps the framework's own <c>TryParse</c> for the type it names, so what
///     counts as parseable is what that method accepts. What changes is the shape of the answer.
///     It is a <see cref="Maybe{T}" /> that code can map, filter, and put together. It replaces a
///     <see cref="bool" /> with an output parameter that no code can read until the
///     <see cref="bool" />.
///     The overloads with no <see cref="IFormatProvider" /> parameter use the current culture, again
///     to agree with the method below it. Give <see cref="CultureInfo.InvariantCulture" />
///     explicitly for text which is not meant to follow the user's culture - a configuration
///     file, a transmission format, or a machine-written record.
///     A <see langword="null" /> string parses as no value in each member here, because that is what each
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
    /// <param name="provider">The formatting information of the culture to read with.</param>
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
    /// <param name="provider">The formatting information of the culture to read with.</param>
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
    /// <param name="provider">The formatting information of the culture to read with.</param>
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
    /// <param name="provider">The formatting information of the culture to read with.</param>
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
    /// <param name="provider">The formatting information of the culture to read with.</param>
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
    /// <param name="provider">The formatting information of the culture to read with.</param>
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
    /// <param name="provider">The formatting information of the culture to read with.</param>
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
    /// <param name="provider">The formatting information of the culture to read with.</param>
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
    /// <param name="provider">The formatting information of the culture to read with.</param>
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
    /// <param name="provider">The formatting information of the culture to read with.</param>
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
    /// <param name="provider">The formatting information of the culture to read with.</param>
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
    ///     <c>True</c> and <c>False</c> in any casing, and whitespace on each side is correct. It
    ///     does not accept <c>1</c>, <c>0</c>, <c>yes</c> or <c>no</c>, and is not
    ///     culture-sensitive.
    /// </remarks>
    [Pure]
    public static Maybe<bool> TryParseBoolean(this string? value) =>
        bool.TryParse(value: value, result: out bool result) ? Maybe.Some(result) : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="char" /> from this string, if it holds one character and no more.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> that holds the one character of <paramref name="value" />, and
    ///     one that holds no value when the length is not one character.
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
    ///     one of the layouts that it recognizes. Use
    ///     <see cref="TryParseGuidExact(string,string)" /> to insist on one of them.
    /// </remarks>
    [Pure]
    public static Maybe<Guid> TryParseGuid(this string? value) =>
        Guid.TryParse(input: value, result: out Guid result) ? Maybe.Some(result) : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="Guid" /> from this string in the given layout, if it holds
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
    ///     whitespace on each side.
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
    ///     culture. Date text is where that matters most, because <c>03/04/2026</c> is two different
    ///     days and this changes with the culture that reads it. Thus, prefer
    ///     <see cref="TryParseDateTime(string,IFormatProvider,DateTimeStyles)" /> for text that the
    ///     user did not type.
    /// </remarks>
    [Pure]
    public static Maybe<DateTime> TryParseDateTime(this string? value) =>
        DateTime.TryParse(s: value, result: out DateTime result) ? Maybe.Some(result) : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="DateTime" /> from this string in the given culture and style, if it
    ///     holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <param name="provider">The formatting information of the culture to read with.</param>
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
        this string? value,
        IFormatProvider provider,
        DateTimeStyles styles) =>
        DateTime.TryParse(s: value, provider: provider, styles: styles, result: out DateTime result)
            ? Maybe.Some(result)
            : Maybe.None;

    /// <summary>
    ///     Parses a <see cref="DateTime" /> from this string in the given format, if it
    ///     holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <param name="format">The format <paramref name="value" /> must be in.</param>
    /// <param name="provider">The formatting information of the culture to read with.</param>
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
        this string? value,
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
    /// <param name="provider">The formatting information of the culture to read with.</param>
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
        this string? value,
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
    /// <param name="provider">The formatting information of the culture to read with.</param>
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
    ///     Parses a <see cref="Uri" /> of the given type from this string, if it holds one.
    /// </summary>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <param name="uriKind">The permitted type: absolute, relative, or one of the two.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold a URI of that type.
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
    ///     <see cref="TryParseEnum{TEnum}(string,bool)" /> can disable. More important is that a
    ///     string of digits parses to that number when the enumeration does not declare it. Thus,
    ///     <c>"37"</c> succeeds for an enumeration with three members. Use
    ///     <see cref="TryParseDefinedEnum{TEnum}(string)" /> where only a declared member is correct.
    /// </remarks>
    [Pure]
    public static Maybe<TEnum> TryParseEnum<TEnum>(this string? value)
        where TEnum : struct =>
        Enum.TryParse(value: value, result: out TEnum result) ? Maybe.Some(result) : Maybe.None;

    /// <summary>
    ///     Parses a value of the given enumeration type from this string, if it holds one,
    ///     The compare can be case-insensitive.
    /// </summary>
    /// <typeparam name="TEnum">The enumeration type to parse.</typeparam>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <param name="ignoreCase">True for a case-insensitive compare of the member names.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value, and one containing no value if
    ///     <paramref name="value" /> does not hold one.
    /// </returns>
    /// <remarks>
    ///     Wraps <see cref="Enum.TryParse{TEnum}(string,bool,out TEnum)" />. A string of digits
    ///     parses to that number, and it makes no difference if the enumeration declares it. See
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
    ///     <see cref="TryParseEnum{TEnum}(string)" /> with one more check: the result must be a member
    ///     that the enumeration declares. That is what keeps <c>"37"</c> from a parse to a value that
    ///     no code handles. Use this one when the string came from
    ///     out of the process.
    ///     Not for an enumeration marked <see cref="FlagsAttribute" />. A combination of declared
    ///     flags is a good value, but the enumeration does not declare it. Thus, it gives no value
    ///     here.
    ///     Use <see cref="TryParseEnum{TEnum}(string)" /> for those.
    /// </remarks>
    [Pure]
    public static Maybe<TEnum> TryParseDefinedEnum<TEnum>(this string? value)
        where TEnum : struct =>
        value.TryParseEnum<TEnum>().Where(static v => Enum.IsDefined(enumType: typeof(TEnum), value: v));

    /// <summary>
    ///     Parses a declared member of the given enumeration type from this string, if it holds
    ///     one, and the compare can be case-insensitive.
    /// </summary>
    /// <typeparam name="TEnum">The enumeration type to parse.</typeparam>
    /// <param name="value">The string to parse. A <see langword="null" /> string gives no value.</param>
    /// <param name="ignoreCase">True for a case-insensitive compare of the member names.</param>
    /// <returns>
    ///     A <see cref="Maybe{T}" /> containing the parsed value if the enumeration declares it,
    ///     and one containing no value otherwise.
    /// </returns>
    /// <remarks>
    ///     Not for an enumeration marked <see cref="FlagsAttribute" />. See
    ///     <see cref="TryParseDefinedEnum{TEnum}(string)" />.
    /// </remarks>
    [Pure]
    public static Maybe<TEnum> TryParseDefinedEnum<TEnum>(this string? value, bool ignoreCase)
        where TEnum : struct =>
        value.TryParseEnum<TEnum>(ignoreCase).Where(static v => Enum.IsDefined(enumType: typeof(TEnum), value: v));
}
