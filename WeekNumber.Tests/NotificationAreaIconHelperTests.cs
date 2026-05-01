using System.Drawing;
using System.Reflection;
using Shouldly;
using Xunit;

namespace WeekNumber.Tests;

/// <summary>
/// Tests for private helpers inside <see cref="NotificationAreaIcon"/>
/// that cannot be reached through the public API alone.
/// </summary>
public class NotificationAreaIconHelperTests
{
    // ── ValidateFontStyle ──────────────────────────────────────────────────────

    [Theory]
    [InlineData((int)FontStyle.Regular,   FontStyle.Regular)]
    [InlineData((int)FontStyle.Bold,      FontStyle.Bold)]
    [InlineData((int)FontStyle.Italic,    FontStyle.Italic)]
    [InlineData((int)FontStyle.Strikeout, FontStyle.Strikeout)]
    public void ValidateFontStyle_RetainsAllowedFlags(int raw, FontStyle expected)
    {
        InvokeValidateFontStyle(raw).ShouldBe(expected);
    }

    [Fact]
    public void ValidateFontStyle_StripsUnderline()
    {
        // Underline is explicitly excluded from the allowed mask.
        var result = InvokeValidateFontStyle((int)FontStyle.Underline);
        result.ShouldBe(FontStyle.Regular);
    }

    [Fact]
    public void ValidateFontStyle_StripsUnknownBits()
    {
        // High bits that don't correspond to any FontStyle should be cleared.
        var result = InvokeValidateFontStyle(0xFF_FF);
        // Should only keep Bold | Italic | Strikeout = 1|2|8 = 11
        const FontStyle allowed = FontStyle.Bold | FontStyle.Italic | FontStyle.Strikeout;
        (result & ~allowed).ShouldBe((FontStyle)0);
    }

    [Fact]
    public void ValidateFontStyle_RetainsMultipleAllowedFlags()
    {
        var input    = (int)(FontStyle.Bold | FontStyle.Italic);
        var expected = FontStyle.Bold | FontStyle.Italic;
        InvokeValidateFontStyle(input).ShouldBe(expected);
    }

    [Fact]
    public void ValidateFontStyle_Zero_ReturnsRegular()
    {
        InvokeValidateFontStyle(0).ShouldBe(FontStyle.Regular);
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static FontStyle InvokeValidateFontStyle(int raw)
    {
        var method = typeof(NotificationAreaIcon).GetMethod(
            "ValidateFontStyle",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        return (FontStyle)method.Invoke(null, [raw])!;
    }
}

