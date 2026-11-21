// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Buffers;

namespace NCode.Identity.Json.Tests;

public class StrictBase64UrlTests
{
    #region IsValid Tests

    [Theory]
    [InlineData("")]
    [InlineData("AQID")]
    [InlineData("AQI")]
    [InlineData("AQ")]
    [InlineData("-_-_")]
    public void IsValid_WhenTheTextIsStrict_ReturnsTrue(string text)
    {
        var utf8Text = SecureEncoding.ASCII.GetBytes(text);

        var isCharsValid = StrictBase64Url.IsValid(text.AsSpan());
        var isUtf8Valid = StrictBase64Url.IsValid(utf8Text);

        Assert.True(isCharsValid);
        Assert.True(isUtf8Valid);
    }

    [Theory]
    [InlineData("AQ==")]
    [InlineData("AQ=")]
    public void IsValid_Padding_ReturnsFalse(string text)
    {
        AssertNotValid(text);
    }

    [Theory]
    [InlineData("AQ I")]
    [InlineData("AQID\n")]
    public void IsValid_Whitespace_ReturnsFalse(string text)
    {
        AssertNotValid(text);
    }

    [Theory]
    [InlineData("+/AA")]
    public void IsValid_IllegalCharacter_ReturnsFalse(string text)
    {
        AssertNotValid(text);
    }

    [Theory]
    [InlineData("AQJ")]
    [InlineData("AR")]
    [InlineData("-_")]
    public void IsValid_NonZeroTrailingBits_ReturnsFalse(string text)
    {
        AssertNotValid(text);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("AQIDB")]
    public void IsValid_RemainderOfOneCharacter_ReturnsFalse(string text)
    {
        AssertNotValid(text);
    }

    private static void AssertNotValid(string text)
    {
        var utf8Text = SecureEncoding.ASCII.GetBytes(text);

        var isCharsValid = StrictBase64Url.IsValid(text.AsSpan());
        var isUtf8Valid = StrictBase64Url.IsValid(utf8Text);

        Assert.False(isCharsValid);
        Assert.False(isUtf8Valid);
    }

    #endregion

    #region GetDecodedLength Tests

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 3)]
    [InlineData(8, 6)]
    public void GetDecodedLength_ReturnsTheExactLength(int textLength, int expected)
    {
        var length = StrictBase64Url.GetDecodedLength(textLength);

        Assert.Equal(expected, length);
    }

    [Fact]
    public void GetDecodedLength_WhenTheRemainderIsOne_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => StrictBase64Url.GetDecodedLength(5));
    }

    [Fact]
    public void GetDecodedLength_WhenNegative_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StrictBase64Url.GetDecodedLength(-1));
    }

    #endregion

    #region TryDecode Tests

    [Fact]
    public void TryDecode_WhenTheTextIsStrict_DecodesIt()
    {
        var destination = new byte[3];

        var decoded = StrictBase64Url.TryDecode("AQID".AsSpan(), destination, out var written);

        Assert.True(decoded);
        Assert.Equal(3, written);
        Assert.Equal(new byte[] { 1, 2, 3 }, destination);

        decoded = StrictBase64Url.TryDecode("-_-_".AsSpan(), destination, out written);

        Assert.True(decoded);
        Assert.Equal(3, written);
        Assert.Equal(new byte[] { 0xFB, 0xFF, 0xBF }, destination);
    }

    [Fact]
    public void TryDecode_WhenTheDestinationIsTooShort_ReturnsFalse()
    {
        var destination = new byte[2];

        var decoded = StrictBase64Url.TryDecode("AQID".AsSpan(), destination, out var written);

        Assert.False(decoded);
        Assert.Equal(0, written);
    }

    [Theory]
    [InlineData("AQ==")]
    [InlineData("AQJ")]
    public void TryDecode_WhenTheTextIsNotStrict_ThrowsFormatException(string text)
    {
        var destination = new byte[8];

        Assert.Throws<FormatException>(() =>
            StrictBase64Url.TryDecode(text.AsSpan(), destination, out _)
        );
    }

    [Fact]
    public void TryDecode_Utf8_WhenTheTextIsStrict_DecodesIt()
    {
        var destination = new byte[3];

        var decoded = StrictBase64Url.TryDecode("AQID"u8, destination, out var written);

        Assert.True(decoded);
        Assert.Equal(3, written);
        Assert.Equal(new byte[] { 1, 2, 3 }, destination);

        decoded = StrictBase64Url.TryDecode("-_-_"u8, destination, out written);

        Assert.True(decoded);
        Assert.Equal(3, written);
        Assert.Equal(new byte[] { 0xFB, 0xFF, 0xBF }, destination);
    }

    [Fact]
    public void TryDecode_Utf8_WhenTheDestinationIsTooShort_ReturnsFalse()
    {
        var destination = new byte[2];

        var decoded = StrictBase64Url.TryDecode("AQID"u8, destination, out var written);

        Assert.False(decoded);
        Assert.Equal(0, written);
    }

    [Theory]
    [InlineData("AQ==")]
    [InlineData("AQJ")]
    public void TryDecode_Utf8_WhenTheTextIsNotStrict_ThrowsFormatException(string text)
    {
        var destination = new byte[8];
        var utf8Text = SecureEncoding.ASCII.GetBytes(text);

        Assert.Throws<FormatException>(() =>
            StrictBase64Url.TryDecode(utf8Text, destination, out _)
        );
    }

    #endregion

    #region Decode Tests

    [Fact]
    public void Decode_WhenTheTextIsStrict_ReturnsTheBytes()
    {
        var bytes = StrictBase64Url.Decode("AQID".AsSpan());

        Assert.Equal(new byte[] { 1, 2, 3 }, bytes);
    }

    [Theory]
    [InlineData("AQ==")]
    [InlineData("AQ=")]
    public void Decode_Padding_Throws(string text)
    {
        Assert.Throws<FormatException>(() => StrictBase64Url.Decode(text.AsSpan()));
    }

    [Theory]
    [InlineData("AQ I")]
    [InlineData("AQID\n")]
    public void Decode_Whitespace_Throws(string text)
    {
        Assert.Throws<FormatException>(() => StrictBase64Url.Decode(text.AsSpan()));
    }

    [Theory]
    [InlineData("+/AA")]
    public void Decode_IllegalCharacter_Throws(string text)
    {
        Assert.Throws<FormatException>(() => StrictBase64Url.Decode(text.AsSpan()));
    }

    [Theory]
    [InlineData("AQJ")]
    [InlineData("AR")]
    [InlineData("-_")]
    public void Decode_NonZeroTrailingBits_Throws(string text)
    {
        Assert.Throws<FormatException>(() => StrictBase64Url.Decode(text.AsSpan()));
    }

    [Theory]
    [InlineData("A")]
    [InlineData("AQIDB")]
    public void Decode_RemainderOfOneCharacter_Throws(string text)
    {
        Assert.Throws<FormatException>(() => StrictBase64Url.Decode(text.AsSpan()));
    }

    [Fact]
    public void Decode_Writer_WhenTheTextIsStrict_WritesTheBytes()
    {
        var writer = BufferFactory.CreateArrayBufferWriter();

        var written = StrictBase64Url.Decode("AQID".AsSpan(), writer);

        Assert.Equal(3, written);
        Assert.Equal(3, writer.WrittenCount);
        Assert.Equal(new byte[] { 1, 2, 3 }, writer.WrittenSpan.ToArray());
    }

    [Theory]
    [InlineData("AQ==")]
    [InlineData("AQJ")]
    public void Decode_Writer_WhenTheTextIsNotStrict_ThrowsFormatExceptionAndWritesNothing(
        string text
    )
    {
        var writer = BufferFactory.CreateArrayBufferWriter();

        Assert.Throws<FormatException>(() => StrictBase64Url.Decode(text.AsSpan(), writer));

        Assert.Equal(0, writer.WrittenCount);
    }

    [Fact]
    public void Decode_Writer_WhenTheWriterIsNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => StrictBase64Url.Decode("AQID".AsSpan(), null!));
    }

    #endregion

    #region RentDecoded Tests

    [Fact]
    public void RentDecoded_WhenSensitive_DecodesIt()
    {
        using var lease = StrictBase64Url.RentDecoded(
            "AQID",
            isSensitive: true,
            out ReadOnlySpan<byte> bytes
        );

        Assert.Equal(new byte[] { 1, 2, 3 }, bytes.ToArray());
    }

    [Fact]
    public void RentDecoded_WhenNotSensitive_DecodesIt()
    {
        using var lease = StrictBase64Url.RentDecoded(
            "-_-_",
            isSensitive: false,
            out ReadOnlyMemory<byte> bytes
        );

        Assert.Equal(new byte[] { 0xFB, 0xFF, 0xBF }, bytes.ToArray());
    }

    [Theory]
    [InlineData("AQ I")]
    [InlineData("AR")]
    public void RentDecoded_WhenTheTextIsNotStrict_ThrowsFormatException(string text)
    {
        Assert.Throws<FormatException>(() =>
        {
            using var lease = StrictBase64Url.RentDecoded(
                text,
                isSensitive: true,
                out ReadOnlySpan<byte> _
            );
        });
        Assert.Throws<FormatException>(() =>
        {
            using var lease = StrictBase64Url.RentDecoded(
                text,
                isSensitive: false,
                out ReadOnlyMemory<byte> _
            );
        });
    }

    #endregion
}
