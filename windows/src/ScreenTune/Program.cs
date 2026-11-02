using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using ScreenTune.Interop;
using ScreenTune.Platform;

namespace ScreenTune;

public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // One copy per user session. A second launch (Start menu, double click) opens
        // the flyout of the running copy instead; "--quit" quits it.
        var quit = args.Contains("--quit", StringComparer.OrdinalIgnoreCase);
        using var mutex = new Mutex(initiallyOwned: true, @"Local\ScreenTune.Windows", out var isFirst);
        if (!isFirst || quit)
        {
            var running = Native.FindWindowW(SystemWindow.ClassName, null);
            if (running != IntPtr.Zero)
            {
                Native.AllowSetForegroundWindow(Native.ASFW_ANY);
                Native.PostMessageW(running, quit ? SystemWindow.QuitMessage : SystemWindow.ShowFlyoutMessage,
                    IntPtr.Zero, IntPtr.Zero);
            }
            return 0;
        }

        var background = args.Contains("--background", StringComparer.OrdinalIgnoreCase);
        Application.Start(_p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App(background);
        });
        return 0;
    }
}
