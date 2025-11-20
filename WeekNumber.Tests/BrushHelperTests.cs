using System.Drawing;
using Shouldly;
using WeekNumber.Helpers;
using Xunit;

namespace WeekNumber.Tests;

public class BrushHelperTests
{
    [Fact]
    public void GetBrushFromColor_ReturnsPredefinedBrush_WhenColorIsKnown()
    {
        // Arrange
        var color = Color.Red;

        // Act
        var brush = BrushHelper.GetBrushFromColor(color);

        // Assert
        brush.ShouldBeOfType<SolidBrush>();
        ((SolidBrush)brush).Color.ShouldBe(color);
        brush.ShouldBeSameAs(Brushes.Red);
    }

    [Fact]
    public void GetBrushFromColor_ReturnsNewSolidBrush_WhenColorIsCustom()
    {
        // Arrange
        var customColor = Color.FromArgb(123, 45, 67);

        // Act
        var brush = BrushHelper.GetBrushFromColor(customColor);

        // Assert
        brush.ShouldBeOfType<SolidBrush>();
        ((SolidBrush)brush).Color.ShouldBe(customColor);
        brush.ShouldNotBeSameAs(Brushes.Red);
        brush.ShouldNotBeSameAs(Brushes.Blue);
        brush.ShouldNotBeSameAs(Brushes.Green);
    }
}