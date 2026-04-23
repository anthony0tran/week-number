using Shouldly;
using WeekNumber.Helpers;
using Xunit;

namespace WeekNumber.Tests;

public class VersionHelperTests
{
    [Fact]
    public void GetAppVersion_ReturnsNonEmptyString()
    {
        var version = VersionHelper.GetAppVersion();
        version.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GetAppVersion_DoesNotThrow()
    {
        Should.NotThrow(() => VersionHelper.GetAppVersion());
    }

    [Fact]
    public void GetAppVersion_ReturnsAtMostThreeDotSeparatedParts()
    {
        var version = VersionHelper.GetAppVersion();

        // The method trims to at most "major.minor.patch" so there should be ≤ 3 parts.
        var parts = version.Split('.');
        parts.Length.ShouldBeLessThanOrEqualTo(3);
    }

    [Fact]
    public void GetAppVersion_DoesNotContainPlusSuffix()
    {
        // Any +commit metadata must be stripped.
        var version = VersionHelper.GetAppVersion();
        version.ShouldNotContain("+");
    }

    [Fact]
    public void GetAppVersion_AllPartsAreNumeric()
    {
        var version = VersionHelper.GetAppVersion();
        var parts   = version.Split('.');

        foreach (var part in parts)
            int.TryParse(part, out _).ShouldBeTrue($"Part '{part}' of version '{version}' is not numeric");
    }
}

