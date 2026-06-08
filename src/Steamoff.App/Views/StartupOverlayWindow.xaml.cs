using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Steamoff.App.Status;

namespace Steamoff.App.Views;

public partial class StartupOverlayWindow : Window, INotifyPropertyChanged
{
    private readonly DispatcherTimer _timer;
    private int _index;
    private RobotStatusKind _currentIcon = RobotStatusKind.Waiting;

    public StartupOverlayWindow()
    {
        InitializeComponent();
        DataContext = this;
        Icon = RobotTrayIconFactory.CreateBitmapSource(RobotStatusKind.Online);

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _timer.Tick += (_, _) => Cycle();
        _timer.Start();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RobotStatusKind CurrentIcon
    {
        get => _currentIcon;
        private set
        {
            if (_currentIcon == value)
            {
                return;
            }

            _currentIcon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentIcon)));
        }
    }

    private void Cycle()
    {
        var states = new[] { RobotStatusKind.Waiting, RobotStatusKind.Online, RobotStatusKind.Offline };
        CurrentIcon = states[_index % states.Length];
        _index++;
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }
}
