using System.Drawing;
using System.Reflection;
using Shouldly;
using WeekNumber.Helpers;
using Xunit;

namespace WeekNumber.Tests;

public class StartupHelperTests
{
    // ── IsStartupEnabled ───────────────────────────────────────────────────────

    [Fact]
    public void IsStartupEnabled_DoesNotThrow()
    {
        Should.NotThrow(() => StartupHelper.IsStartupEnabled());
    }

    [Fact]
    public void IsStartupEnabled_ReturnsBooleanValue()
    {
        var result = StartupHelper.IsStartupEnabled();
        (result == true || result == false).ShouldBeTrue();
    }

    // ── SetStartup ─────────────────────────────────────────────────────────────

    [Fact]
    public void SetStartup_DoesNotThrow_WhenDisabling()
    {
        Should.NotThrow(() => StartupHelper.SetStartup(false));
    }

    [Fact]
    public void SetStartup_DoesNotThrow_WhenEnabling()
    {
        Should.NotThrow(() => StartupHelper.SetStartup(true));
    }

    // ── UpdateRegistryKey ──────────────────────────────────────────────────────

    [Fact]
    public void UpdateRegistryKey_DoesNotThrow()
    {
        Should.NotThrow(() => StartupHelper.UpdateRegistryKey());
    }

    // ── ContainsDangerousCharacters (private) ──────────────────────────────────

    [Theory]
    [InlineData(@"C:\Program Files\WeekNumber\app.exe", false)]
    [InlineData(@"C:\Users\user\AppData\Local\app.exe", false)]
    [InlineData(@"C:\Normal Path\app.exe", false)]
    public void ContainsDangerousCharacters_ReturnsFalse_ForSafePaths(string path, bool expected)
    {
        InvokeContainsDangerousCharacters(path).ShouldBe(expected);
    }

    [Theory]
    [InlineData("C:\\app|malicious.exe", true)]
    [InlineData("C:\\app&calc.exe", true)]
    [InlineData("C:\\app>output.exe", true)]
    [InlineData("C:\\app<input.exe", true)]
    [InlineData("C:\\app%PATH%.exe", true)]
    [InlineData("C:\\app^escape.exe", true)]
    [InlineData("C:\\app`tick.exe", true)]
    [InlineData("C:\\app$var.exe", true)]
    [InlineData("C:\\app{brace}.exe", true)]
    [InlineData("C:\\app!bang.exe", true)]
    public void ContainsDangerousCharacters_ReturnsTrue_ForDangerousPaths(string path, bool expected)
    {
        InvokeContainsDangerousCharacters(path).ShouldBe(expected);
    }

    [Fact]
    public void ContainsDangerousCharacters_ReturnsTrue_ForControlCharacters()
    {
        var pathWithNull = "C:\\app\x00.exe";
        InvokeContainsDangerousCharacters(pathWithNull).ShouldBeTrue();
    }

    [Fact]
    public void ContainsDangerousCharacters_ReturnsTrue_ForNewline()
    {
        var pathWithNewline = "C:\\app\n.exe";
        InvokeContainsDangerousCharacters(pathWithNewline).ShouldBeTrue();
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static bool InvokeContainsDangerousCharacters(string path)
    {
        var method = typeof(StartupHelper).GetMethod(
            "ContainsDangerousCharacters",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        return (bool)method.Invoke(null, [path])!;
    }
}

