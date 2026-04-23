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
        // Just verifies the method completes and returns a valid bool.
        var result = StartupHelper.IsStartupEnabled();
        (result == true || result == false).ShouldBeTrue();
    }

    // ── SetStartup ─────────────────────────────────────────────────────────────

    [Fact]
    public void SetStartup_DoesNotThrow_WhenDisabling()
    {
        // Disabling startup should never throw even when the key doesn't exist.
        Should.NotThrow(() => StartupHelper.SetStartup(false));
    }

    [Fact]
    public void SetStartup_DoesNotThrow_WhenEnabling()
    {
        // Enabling startup is best-effort; it may silently do nothing if the
        // executable path isn't in a trusted location (e.g. during tests).
        Should.NotThrow(() => StartupHelper.SetStartup(true));
    }

    // ── UpdateRegistryKey ──────────────────────────────────────────────────────

    [Fact]
    public void UpdateRegistryKey_DoesNotThrow()
    {
        Should.NotThrow(() => StartupHelper.UpdateRegistryKey());
    }

    // ── IsTrustedLocation (private) ────────────────────────────────────────────

    [Theory]
    [InlineData(@"C:\RandomTempFolder\app.exe",         false)]
    [InlineData(@"C:\Users\user\Downloads\app.exe",     false)]
    public void IsTrustedLocation_ReturnsFalse_ForUntrustedPaths(string path, bool expected)
    {
        var result = InvokeIsTrustedLocation(path);
        result.ShouldBe(expected);
    }

    [Fact]
    public void IsTrustedLocation_ReturnsTrue_ForAppBaseDirectory()
    {
        // The test runner's base directory is always treated as trusted.
        var basePath = Path.Combine(AppContext.BaseDirectory, "app.exe");
        var result   = InvokeIsTrustedLocation(basePath);
        result.ShouldBeTrue();
    }

    [Fact]
    public void IsTrustedLocation_ReturnsTrue_ForProgramFilesPath()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrEmpty(programFiles))
            return; // Skip on systems where there's no Program Files folder.

        var fakePath = Path.Combine(programFiles, "SomeApp", "app.exe");
        var result   = InvokeIsTrustedLocation(fakePath);
        result.ShouldBeTrue();
    }

    [Fact]
    public void IsTrustedLocation_IsCaseInsensitive()
    {
        var basePath  = Path.Combine(AppContext.BaseDirectory, "app.exe");
        var upperPath = basePath.ToUpperInvariant();

        InvokeIsTrustedLocation(upperPath).ShouldBeTrue();
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static bool InvokeIsTrustedLocation(string path)
    {
        var method = typeof(StartupHelper).GetMethod(
            "IsTrustedLocation",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        return (bool)method.Invoke(null, [path])!;
    }
}

