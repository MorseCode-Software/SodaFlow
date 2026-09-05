using System;
using System.Globalization;
using System.Threading.Tasks;
using SodaFlow.Functional;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests;

public class StringExtensionMethodsTests
{
    // ReSharper disable UnusedMember.Local
    private enum Color
    {
        Red = 0,
        Green = 1,
        Blue = 2
    }
    // ReSharper restore UnusedMember.Local

    [Test]
    public async Task TestTryParseInt32()
    {
        await Assert.That("42".TryParseInt32()).IsEqualTo(Maybe.Some(42));
        await Assert.That("-42".TryParseInt32()).IsEqualTo(Maybe.Some(-42));
        await Assert.That("x".TryParseInt32()).IsEqualTo(Maybe<int>.None);
        await Assert.That(string.Empty.TryParseInt32()).IsEqualTo(Maybe<int>.None);
        await Assert.That(((string?)null).TryParseInt32()).IsEqualTo(Maybe<int>.None);
        await Assert.That("2147483648".TryParseInt32()).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestTryParseInt32WithStyles()
    {
        await Assert.That("1,234".TryParseInt32(
                styles: NumberStyles.Integer | NumberStyles.AllowThousands,
                provider: CultureInfo.InvariantCulture)).IsEqualTo(Maybe.Some(1234));

        await Assert.That("1,234".TryParseInt32(styles: NumberStyles.Integer, provider: CultureInfo.InvariantCulture)).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestTryParseIntegralTypes()
    {
        await Assert.That("7".TryParseByte()).IsEqualTo(Maybe.Some((byte)7));
        await Assert.That("256".TryParseByte()).IsEqualTo(Maybe<byte>.None);
        await Assert.That("-7".TryParseSByte()).IsEqualTo(Maybe.Some((sbyte)-7));
        await Assert.That("-7".TryParseInt16()).IsEqualTo(Maybe.Some((short)-7));
        await Assert.That("7".TryParseUInt16()).IsEqualTo(Maybe.Some((ushort)7));
        await Assert.That("7".TryParseUInt32()).IsEqualTo(Maybe.Some(7u));
        await Assert.That("-7".TryParseInt64()).IsEqualTo(Maybe.Some(-7L));
        await Assert.That("7".TryParseUInt64()).IsEqualTo(Maybe.Some(7ul));
        await Assert.That("-7".TryParseUInt32()).IsEqualTo(Maybe<uint>.None);
    }

    [Test]
    public async Task TestTryParseRealTypes()
    {
        await Assert.That("1.5".TryParseSingle(styles: NumberStyles.Float, provider: CultureInfo.InvariantCulture)).IsEqualTo(Maybe.Some(1.5f));

        await Assert.That("1.5".TryParseDouble(styles: NumberStyles.Float, provider: CultureInfo.InvariantCulture)).IsEqualTo(Maybe.Some(1.5d));

        await Assert.That("1.5".TryParseDecimal(styles: NumberStyles.Number, provider: CultureInfo.InvariantCulture)).IsEqualTo(Maybe.Some(1.5m));

        await Assert.That("x".TryParseDouble(styles: NumberStyles.Float, provider: CultureInfo.InvariantCulture)).IsEqualTo(Maybe<double>.None);
    }

    [Test]
    public async Task TestTryParseBoolean()
    {
        await Assert.That("true".TryParseBoolean()).IsEqualTo(Maybe.Some(true));
        await Assert.That(" TRUE ".TryParseBoolean()).IsEqualTo(Maybe.Some(true));
        await Assert.That("False".TryParseBoolean()).IsEqualTo(Maybe.Some(false));
        await Assert.That("1".TryParseBoolean()).IsEqualTo(Maybe<bool>.None);
        await Assert.That(((string?)null).TryParseBoolean()).IsEqualTo(Maybe<bool>.None);
    }

    [Test]
    public async Task TestTryParseChar()
    {
        await Assert.That("a".TryParseChar()).IsEqualTo(Maybe.Some('a'));
        await Assert.That("ab".TryParseChar()).IsEqualTo(Maybe<char>.None);
        await Assert.That(string.Empty.TryParseChar()).IsEqualTo(Maybe<char>.None);
    }

    [Test]
    public async Task TestTryParseGuid()
    {
        Guid g = Guid.NewGuid();

        await Assert.That(g.ToString("D").TryParseGuid()).IsEqualTo(Maybe.Some(g));
        await Assert.That(g.ToString("N").TryParseGuid()).IsEqualTo(Maybe.Some(g));
        await Assert.That("not-a-guid".TryParseGuid()).IsEqualTo(Maybe<Guid>.None);
    }

    [Test]
    public async Task TestTryParseGuidExact()
    {
        Guid g = Guid.NewGuid();

        await Assert.That(g.ToString("N").TryParseGuidExact("N")).IsEqualTo(Maybe.Some(g));
        await Assert.That(g.ToString("D").TryParseGuidExact("N")).IsEqualTo(Maybe<Guid>.None);
    }

    [Test]
    public async Task TestTryParseDateTime()
    {
        await Assert.That("2026-03-04".TryParseDateTime(
                provider: CultureInfo.InvariantCulture,
                styles: DateTimeStyles.None)).IsEqualTo(Maybe.Some(new DateTime(year: 2026, month: 3, day: 4)));

        await Assert.That("not a date".TryParseDateTime(
                provider: CultureInfo.InvariantCulture,
                styles: DateTimeStyles.None)).IsEqualTo(Maybe<DateTime>.None);
    }

    [Test]
    public async Task TestTryParseDateTimeExact()
    {
        await Assert.That("04/03/2026".TryParseDateTimeExact(
                format: "dd/MM/yyyy",
                provider: CultureInfo.InvariantCulture,
                styles: DateTimeStyles.None)).IsEqualTo(Maybe.Some(new DateTime(year: 2026, month: 3, day: 4)));

        await Assert.That("2026-03-04".TryParseDateTimeExact(
                format: "dd/MM/yyyy",
                provider: CultureInfo.InvariantCulture,
                styles: DateTimeStyles.None)).IsEqualTo(Maybe<DateTime>.None);
    }

    [Test]
    public async Task TestTryParseDateTimeOffset()
    {
        await Assert.That("2026-03-04T00:00:00+00:00".TryParseDateTimeOffset(
                provider: CultureInfo.InvariantCulture,
                styles: DateTimeStyles.None)).IsEqualTo(Maybe.Some(
                new DateTimeOffset(
                    year: 2026,
                    month: 3,
                    day: 4,
                    hour: 0,
                    minute: 0,
                    second: 0,
                    offset: TimeSpan.Zero)));

        await Assert.That("x".TryParseDateTimeOffset(
                provider: CultureInfo.InvariantCulture,
                styles: DateTimeStyles.None)).IsEqualTo(Maybe<DateTimeOffset>.None);
    }

    [Test]
    public async Task TestTryParseTimeSpan()
    {
        await Assert.That("01:30:00".TryParseTimeSpan(CultureInfo.InvariantCulture)).IsEqualTo(Maybe.Some(TimeSpan.FromMinutes(90)));

        await Assert.That("x".TryParseTimeSpan(CultureInfo.InvariantCulture)).IsEqualTo(Maybe<TimeSpan>.None);
    }

    [Test]
    public async Task TestTryParseUri()
    {
        await Assert.That("https://example.com/a".TryParseUri()).IsEqualTo(Maybe.Some(new Uri("https://example.com/a")));

        await Assert.That("/a/b".TryParseUri()).IsEqualTo(Maybe<Uri>.None);

        await Assert.That("/a/b".TryParseUri(UriKind.Relative)).IsEqualTo(Maybe.Some(new Uri(uriString: "/a/b", uriKind: UriKind.Relative)));
    }

    [Test]
    public async Task TestTryParseEnum()
    {
        await Assert.That("Green".TryParseEnum<Color>()).IsEqualTo(Maybe.Some(Color.Green));
        await Assert.That("green".TryParseEnum<Color>()).IsEqualTo(Maybe<Color>.None);
        await Assert.That("green".TryParseEnum<Color>(true)).IsEqualTo(Maybe.Some(Color.Green));
        await Assert.That("Mauve".TryParseEnum<Color>()).IsEqualTo(Maybe<Color>.None);
        await Assert.That(((string?)null).TryParseEnum<Color>()).IsEqualTo(Maybe<Color>.None);
    }

    [Test]
    public async Task TestTryParseEnumAcceptsUndeclaredNumbers() =>
        await Assert.That("37".TryParseEnum<Color>()).IsEqualTo(Maybe.Some((Color)37));

    [Test]
    public async Task TestTryParseDefinedEnum()
    {
        await Assert.That("Green".TryParseDefinedEnum<Color>()).IsEqualTo(Maybe.Some(Color.Green));
        await Assert.That("1".TryParseDefinedEnum<Color>()).IsEqualTo(Maybe.Some(Color.Green));
        await Assert.That("37".TryParseDefinedEnum<Color>()).IsEqualTo(Maybe<Color>.None);
        await Assert.That("green".TryParseDefinedEnum<Color>()).IsEqualTo(Maybe<Color>.None);
        await Assert.That("green".TryParseDefinedEnum<Color>(true)).IsEqualTo(Maybe.Some(Color.Green));
    }
}
