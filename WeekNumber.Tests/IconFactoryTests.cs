using System.Drawing;
using Shouldly;
using Xunit;

namespace WeekNumber.Tests;

public class IconFactoryTests : IDisposable
{
    private readonly IconFactory _factory = new();
    private readonly Font        _font    = new("Arial", 32, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly SolidBrush  _brush   = new(Color.White);

    // ── CreateNumberIcon ───────────────────────────────────────────────────────

    [Fact]
    public void CreateNumberIcon_ReturnsNonNullIcon()
    {
        using var icon = _factory.CreateNumberIcon(1, _font, _brush, 32);
        icon.ShouldNotBeNull();
    }

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(48)]
    public void CreateNumberIcon_IconHasRequestedSize(int size)
    {
        using var icon = _factory.CreateNumberIcon(1, _font, _brush, size);
        icon.Width.ShouldBe(size);
        icon.Height.ShouldBe(size);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(25)]
    [InlineData(42)]
    [InlineData(53)]
    public void CreateNumberIcon_DoesNotThrow_ForValidWeekNumbers(int weekNumber)
    {
        Should.NotThrow(() =>
        {
            using var icon = _factory.CreateNumberIcon(weekNumber, _font, _brush, 32);
        });
    }

    [Fact]
    public void CreateNumberIcon_WorksWithBoldFont()
    {
        using var font = new Font("Arial", 32, FontStyle.Bold, GraphicsUnit.Pixel);
        using var icon = _factory.CreateNumberIcon(7, font, _brush, 32);
        icon.ShouldNotBeNull();
    }

    [Fact]
    public void CreateNumberIcon_WorksWithItalicFont()
    {
        using var font = new Font("Arial", 32, FontStyle.Italic, GraphicsUnit.Pixel);
        using var icon = _factory.CreateNumberIcon(7, font, _brush, 32);
        icon.ShouldNotBeNull();
    }

    [Fact]
    public void CreateNumberIcon_WorksWithColoredBrush()
    {
        using var brush = new SolidBrush(Color.Yellow);
        using var icon  = _factory.CreateNumberIcon(12, _font, brush, 32);
        icon.ShouldNotBeNull();
    }

    [Fact]
    public void CreateNumberIcon_WorksWithNonSolidBrush()
    {
        // When brush is not a SolidBrush the factory should fall back to white text.
        using var hatch = new System.Drawing.Drawing2D.HatchBrush(
            System.Drawing.Drawing2D.HatchStyle.Cross, Color.Red);
        using var icon = _factory.CreateNumberIcon(5, _font, hatch, 32);
        icon.ShouldNotBeNull();
    }

    [Fact]
    public void CreateNumberIcon_EachCallReturnsNewInstance()
    {
        using var a = _factory.CreateNumberIcon(1, _font, _brush, 32);
        using var b = _factory.CreateNumberIcon(1, _font, _brush, 32);
        a.ShouldNotBeSameAs(b);
    }

    // ── IIconFactory contract ──────────────────────────────────────────────────

    [Fact]
    public void IconFactory_ImplementsIIconFactory()
    {
        (_factory as IIconFactory).ShouldNotBeNull();
    }

    // ── Cleanup ────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _font.Dispose();
        _brush.Dispose();
    }
}

