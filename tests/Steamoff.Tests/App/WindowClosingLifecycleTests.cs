using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Steamoff.Tests.TestSupport;

namespace Steamoff.Tests.App;

/// <summary>
/// Regression coverage for the "нажимаю Открыть Steamoff и ничего не происходит"
/// bug. Root cause: WPF throws InvalidOperationException ("Во время закрытия окна
/// нельзя ... вызвать Show, ShowDialog, Close ...") whenever Show()/Close() is
/// invoked re-entrantly from inside a window's own Closing sequence — and
/// App.OnDispatcherUnhandledException swallows that exception (logs it, sets
/// e.Handled = true), so the failure is completely invisible to the user: the tray
/// "Открыть"/double-click and the post-restart re-show both silently no-op.
///
/// Two call sites triggered this:
///  - MainWindow.OnWindowClosing called Hide() synchronously while WPF was still
///    processing Closing, corrupting the window's internal close-state so the next
///    Show() (from App.ShowMainWindow, the tray "Open" path) threw.
///  - SettingsWindow.OnWindowClosing cancelled the close and synchronously executed
///    CancelCommand, which raises CloseRequested -> Close() — re-entering Close()
///    from inside Closing — every time the window was closed externally (e.g.
///    ExitApplication()/RestartApplication() calling _settingsWindow?.Close()
///    directly, which is exactly what "Restart now" after a language change does).
///
/// The fix in both handlers: cancel the close and defer the actual Hide()/re-Close
/// to the dispatcher (Background priority) so the current Closing sequence finishes
/// first. These tests reproduce both call patterns on a real STA Dispatcher thread
/// (Window.Show/Close need a running message loop, which xUnit's pool threads don't
/// provide — see StaThreadRunner) and assert the deferred approach never throws and
/// always leaves the window in the expected visible/closed state.
/// </summary>
public sealed class WindowClosingLifecycleTests
{
    [Fact]
    public void TrayResidentWindow_HiddenOnClose_ReopensRepeatedly_WithoutThrowing()
    {
        StaThreadRunner.Run(dispatcher =>
        {
            var window = new TrayResidentTestWindow();
            try
            {
                window.Show();
                Assert.True(window.IsVisible);

                for (var cycle = 0; cycle < 3; cycle++)
                {
                    // User clicks the window's [X] — Closing cancels and defers
                    // Hide() to the dispatcher (mirrors MainWindow.xaml.cs).
                    window.Close();
                    StaThreadRunner.PumpUntil(dispatcher, () => !window.IsVisible);
                    Assert.False(window.IsVisible);
                    Assert.False(window.WasActuallyClosed);

                    // Tray "Открыть Steamoff" / double-click — mirrors
                    // App.ShowMainWindow().
                    var ex = Record.Exception(() =>
                    {
                        window.Show();
                        if (window.WindowState == WindowState.Minimized)
                        {
                            window.WindowState = WindowState.Normal;
                        }

                        window.Activate();
                    });

                    Assert.Null(ex);
                    Assert.True(window.IsVisible);
                }
            }
            finally
            {
                window.AllowRealClose();
                window.Close();
            }
        });
    }

    [Fact]
    public void CancelOnCloseWindow_ClosedExternallyDuringExit_DoesNotThrow_AndStillCloses()
    {
        StaThreadRunner.Run(dispatcher =>
        {
            var window = new CancelOnCloseTestWindow();
            window.Show();
            Assert.True(window.IsVisible);

            // ExitApplication()/RestartApplication() call _settingsWindow?.Close()
            // directly — not via the Cancel/Save/Apply buttons — exactly the path
            // that produced the swallowed InvalidOperationException in the log
            // every time "Restart now" ran after a language change.
            var ex = Record.Exception(() => window.Close());
            Assert.Null(ex);

            StaThreadRunner.PumpUntil(dispatcher, () => window.WasActuallyClosed);
            Assert.True(window.WasActuallyClosed);
            Assert.Equal(1, window.CancelCommandInvocations);
        });
    }

    /// <summary>Mirrors MainWindow.OnWindowClosing: cancel the close, defer Hide() to the dispatcher instead of calling it inline.</summary>
    private sealed class TrayResidentTestWindow : Window
    {
        private bool _allowRealClose;

        public bool WasActuallyClosed { get; private set; }

        public TrayResidentTestWindow()
        {
            Width = 40;
            Height = 40;
            ShowInTaskbar = false;
            WindowStyle = WindowStyle.None;
            Closing += OnClosing;
            Closed += (_, _) => WasActuallyClosed = true;
        }

        public void AllowRealClose() => _allowRealClose = true;

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            if (_allowRealClose)
            {
                return;
            }

            e.Cancel = true;
            Dispatcher.BeginInvoke(Hide, DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// Mirrors SettingsWindow.OnWindowClosing: cancel the close and defer the
    /// cancel-command (which itself requests Close()) to the dispatcher, instead of
    /// invoking it inline — which would re-enter Close() from inside Closing.
    /// </summary>
    private sealed class CancelOnCloseTestWindow : Window
    {
        private bool _closingViaCommand;

        public int CancelCommandInvocations { get; private set; }
        public bool WasActuallyClosed { get; private set; }

        public CancelOnCloseTestWindow()
        {
            Width = 40;
            Height = 40;
            ShowInTaskbar = false;
            WindowStyle = WindowStyle.None;
            Closing += OnClosing;
            Closed += (_, _) => WasActuallyClosed = true;
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            if (_closingViaCommand)
            {
                return;
            }

            e.Cancel = true;
            _closingViaCommand = true;
            Dispatcher.BeginInvoke(RunCancelCommand, DispatcherPriority.Background);
        }

        private void RunCancelCommand()
        {
            CancelCommandInvocations++;
            Close();
        }
    }
}
