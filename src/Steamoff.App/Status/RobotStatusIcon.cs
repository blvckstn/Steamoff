using System.Windows;

namespace Steamoff.App.Status;

public sealed class RobotStatusIcon : FrameworkElement
{
    public static readonly DependencyProperty StatusKindProperty =
        DependencyProperty.Register(
            nameof(StatusKind),
            typeof(RobotStatusKind),
            typeof(RobotStatusIcon),
            new FrameworkPropertyMetadata(RobotStatusKind.Waiting, FrameworkPropertyMetadataOptions.AffectsRender));

    public RobotStatusKind StatusKind
    {
        get => (RobotStatusKind)GetValue(StatusKindProperty);
        set => SetValue(StatusKindProperty, value);
    }

    protected override void OnRender(System.Windows.Media.DrawingContext dc)
    {
        base.OnRender(dc);

        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0)
        {
            return;
        }

        var rect = new Rect((ActualWidth - size) / 2, (ActualHeight - size) / 2, size, size);
        dc.DrawImage(RobotTrayIconFactory.CreateBitmapSource(StatusKind, 256), rect);
    }
}
