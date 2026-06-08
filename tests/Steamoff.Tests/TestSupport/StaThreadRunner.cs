using System.Windows.Threading;

namespace Steamoff.Tests.TestSupport;

/// <summary>
/// WPF Window/Dispatcher lifecycle tests need a real STA thread with a running
/// message loop — xUnit's thread-pool threads are MTA and never pump a Dispatcher,
/// so Window.Show()/Close() and Dispatcher.BeginInvoke callbacks would either throw
/// or simply never run. Runs <paramref name="action"/> to completion on a fresh STA
/// thread (re-throwing any exception on the calling thread) and exposes a small
/// "DoEvents"-style pump so deferred (Background-priority) callbacks can execute
/// before assertions run.
/// </summary>
internal static class StaThreadRunner
{
    public static void Run(Action<Dispatcher> action)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(() =>
            {
                try
                {
                    action(dispatcher);
                }
                catch (Exception ex)
                {
                    captured = ex;
                }
                finally
                {
                    dispatcher.InvokeShutdown();
                }
            });
            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (captured is not null)
        {
            throw captured;
        }
    }

    /// <summary>Pumps the dispatcher queue (including deferred Background-priority callbacks) until <paramref name="condition"/> is true or the timeout elapses.</summary>
    public static void PumpUntil(Dispatcher dispatcher, Func<bool> condition, int timeoutMilliseconds = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException("Условие не выполнено за отведённое время ожидания диспетчера. / Condition was not met within the dispatcher pump timeout.");
            }

            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }
}
