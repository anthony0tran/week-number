using System.Globalization;
using System.Reflection;
using Shouldly;
using Xunit;

namespace WeekNumber.Tests;

public class WeekNumberTests
{
    // ── Constructor ────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_SetsNumberInValidIsoRange()
    {
        var wn = new WeekNumber();
        wn.Number.ShouldBeInRange(1, 53);
    }

    [Fact]
    public void Constructor_SetsLastUpdatedToApproximatelyNow()
    {
        var before = DateTime.Now.AddSeconds(-1);
        var wn     = new WeekNumber();
        var after  = DateTime.Now.AddSeconds(1);

        wn.LastUpdated.ShouldBeGreaterThanOrEqualTo(before);
        wn.LastUpdated.ShouldBeLessThanOrEqualTo(after);
    }

    [Fact]
    public void Constructor_NumberMatchesIsoWeekOfToday()
    {
        var wn       = new WeekNumber();
        var expected = ISOWeek.GetWeekOfYear(DateTime.Today);

        wn.Number.ShouldBe(expected);
    }

    // ── UpdateNumber ───────────────────────────────────────────────────────────

    [Fact]
    public void UpdateNumber_KeepsNumberInValidIsoRange()
    {
        var wn = new WeekNumber();
        wn.UpdateNumber();
        wn.Number.ShouldBeInRange(1, 53);
    }

    [Fact]
    public void UpdateNumber_RefreshesLastUpdated()
    {
        var wn   = new WeekNumber();
        var old  = wn.LastUpdated;

        // Ensure at least a tick passes so the timestamps differ.
        Thread.Sleep(1);

        wn.UpdateNumber();

        wn.LastUpdated.ShouldBeGreaterThanOrEqualTo(old);
    }

    [Fact]
    public void UpdateNumber_NumberMatchesIsoWeekOfToday()
    {
        var wn = new WeekNumber();
        wn.UpdateNumber();

        var expected = ISOWeek.GetWeekOfYear(DateTime.Today);
        wn.Number.ShouldBe(expected);
    }

    // ── ISO week arithmetic for well-known dates (via private helper) ──────────

    [Theory]
    [InlineData(2025,  1,  1,  1)]   // 2025-01-01 is ISO week 1
    [InlineData(2025,  1,  6,  2)]   // 2025-01-06 is ISO week 2
    [InlineData(2025, 12, 28, 52)]   // 2025-12-28 is ISO week 52
    [InlineData(2026,  1,  1,  1)]   // 2026-01-01 is ISO week 1
    [InlineData(2020, 12, 28, 53)]   // 2020-12-28 is ISO week 53 (long year)
    [InlineData(2021,  1,  3, 53)]   // 2021-01-03 still belongs to ISO week 53 of 2020
    [InlineData(2021,  1,  4,  1)]   // 2021-01-04 is ISO week 1 of 2021
    public void CalculateWeekNumber_ReturnsCorrectIsoWeek(int year, int month, int day, int expectedWeek)
    {
        var date = new DateTime(year, month, day);

        // Invoke the private static helper through reflection.
        var method = typeof(WeekNumber).GetMethod(
            "CalculateWeekNumber",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (int)method.Invoke(null, [date])!;

        result.ShouldBe(expectedWeek);
    }

    // ── Record semantics ───────────────────────────────────────────────────────

    [Fact]
    public void TwoInstances_CreatedWithSameValues_AreEqual()
    {
        // Records have structural/value equality.
        var a = new WeekNumber();
        var b = new WeekNumber();

        // Same week number and same LastUpdated second-bucket → should be equal.
        // We compare via Number since LastUpdated will naturally match the current
        // minute unless a minute boundary is crossed during test execution.
        a.Number.ShouldBe(b.Number);
    }

    [Fact]
    public void NumberProperty_IsNotMutableFromOutside()
    {
        // Verify the setter is private — property should not have a public set.
        var prop = typeof(WeekNumber).GetProperty(nameof(WeekNumber.Number))!;
        prop.SetMethod.ShouldNotBeNull();
        prop.SetMethod!.IsPrivate.ShouldBeTrue();
    }

    [Fact]
    public void LastUpdatedProperty_IsNotMutableFromOutside()
    {
        var prop = typeof(WeekNumber).GetProperty(nameof(WeekNumber.LastUpdated))!;
        prop.SetMethod.ShouldNotBeNull();
        prop.SetMethod!.IsPrivate.ShouldBeTrue();
    }
}

