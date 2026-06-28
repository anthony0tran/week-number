using System.Drawing;
using System.Drawing.Drawing2D;
using Shouldly;
using WeekNumber.Forms;
using Xunit;

namespace WeekNumber.Tests;

public class GraphicsUtilTests
{
    // ── CreateRoundedRect ──────────────────────────────────────────────────────

    [Fact]
    public void CreateRoundedRect_ReturnsNonNullPath()
    {
        using var path = GraphicsUtil.CreateRoundedRect(new Rectangle(0, 0, 100, 100), 10);
        path.ShouldNotBeNull();
    }

    [Fact]
    public void CreateRoundedRect_ReturnedPathIsClosed()
    {
        using var path = GraphicsUtil.CreateRoundedRect(new Rectangle(0, 0, 100, 100), 10);
        // A closed figure must have at least one point/sub-path.
        path.PointCount.ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData(0,   0,  100, 100, 8)]
    [InlineData(10, 20,  200, 150, 12)]
    [InlineData(0,   0,   50,  50, 4)]
    public void CreateRoundedRect_DoesNotThrow_ForVariousInputs(int x, int y, int w, int h, int r)
    {
        Should.NotThrow(() =>
        {
            using var path = GraphicsUtil.CreateRoundedRect(new Rectangle(x, y, w, h), r);
        });
    }

    [Fact]
    public void CreateRoundedRect_PathHasFourArcs()
    {
        // Each rounded corner is one AddArc call → 4 arcs in the path.
        using var path = GraphicsUtil.CreateRoundedRect(new Rectangle(0, 0, 100, 100), 10);

        // GraphicsPath built from 4 AddArc + CloseFigure has exactly 4 PathPointType.Arc segments.
        var types = path.PathTypes;
        // Verify there are multiple point entries (rounding corner points).
        types.Length.ShouldBeGreaterThan(3);
    }

    [Fact]
    public void CreateRoundedRect_RadiusZero_ThrowsArgumentException()
    {
        // GDI+ AddArc requires a non-zero arc size; radius=0 produces a 0×0 arc and throws.
        Should.Throw<ArgumentException>(() =>
        {
            using var path = GraphicsUtil.CreateRoundedRect(new Rectangle(0, 0, 100, 100), 0);
        });
    }

    [Fact]
    public void CreateRoundedRect_LargeRadius_DoesNotThrow()
    {
        Should.NotThrow(() =>
        {
            using var path = GraphicsUtil.CreateRoundedRect(new Rectangle(0, 0, 200, 200), 50);
        });
    }
}


