using System.Drawing;
using System.Reflection;
using NSubstitute;
using Shouldly;
using Xunit;

namespace WeekNumber.Tests;

public class NotificationAreaIconTests : IDisposable
{
    private readonly IIconFactory _iconFactorySub;
    private readonly NotificationAreaIcon _instance;
    private readonly Icon _testIcon;

    public NotificationAreaIconTests()
    {
        // Create a dedicated icon for testing to avoid disposing system icons.
        using var bmp = new Bitmap(32, 32);
        _testIcon = Icon.FromHandle(bmp.GetHicon());

        _iconFactorySub = Substitute.For<IIconFactory>();
        _iconFactorySub
            .CreateNumberIcon(Arg.Any<int>(), Arg.Any<Font>(), Arg.Any<Brush>(), Arg.Any<int>())
            .Returns(_ =>
            {
                using var b = new Bitmap(32, 32);
                return Icon.FromHandle(b.GetHicon());
            });

        _instance = (NotificationAreaIcon)Activator.CreateInstance(
            typeof(NotificationAreaIcon),
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [_iconFactorySub],
            null
        )!;
    }
    
    [Fact]
    public void Instance_ShouldReturnSameObject()
    {
        var first = NotificationAreaIcon.Instance;
        var second = NotificationAreaIcon.Instance;

        first.ShouldBeSameAs(second);
    }

    [Fact]
    public void Dispose_ShouldNotThrow_WhenCalledMultipleTimes()
    {
        _instance.Dispose();
        Should.NotThrow(() => _instance.Dispose());
    }

    [Fact]
    public void UpdateIcon_ShouldCallIconFactory()
    {
        // Act
        _instance.UpdateIcon();

        // Assert
        _iconFactorySub.Received()
            .CreateNumberIcon(Arg.Any<int>(), Arg.Any<Font>(), Arg.Any<Brush>(), Arg.Any<int>());
    }

    [Fact]
    public void UpdateText_ShouldUpdateNotifyIconText()
    {
        // Act
        _instance.UpdateText();

        // Assert
        _instance.NotifyIcon.Text.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void UpdateText_ShouldNotExceed63Characters()
    {
        // The NotifyIcon.Text property has a 63-character limit.
        _instance.UpdateText();

        _instance.NotifyIcon.Text.Length.ShouldBeLessThanOrEqualTo(63);
    }

    public void Dispose()
    {
        _instance.Dispose();
        _testIcon.Dispose();
        GC.SuppressFinalize(this);
    }
}