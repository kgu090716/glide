using Microsoft.UI.Xaml;
using System.Threading;

namespace ThreeFingerWindowDrag;

public partial class App : Application
{
    private MainWindow window;
    private Mutex instanceMutex;
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => {
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "crash.log"), e.Exception.ToString()); } catch { }
        };
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string[] commandLine = Environment.GetCommandLineArgs();
        int restartAt = Array.IndexOf(commandLine, "--elevated-restart");
        if (restartAt >= 0 && (restartAt + 1 >= commandLine.Length || !int.TryParse(commandLine[restartAt + 1], out int previous) ||
            !ElevationSupport.CompleteHandoff(previous, MainWindow.NativeTitle, MainWindow.QuitMessage)))
        { Exit(); return; }
        if (Environment.GetCommandLineArgs().Contains("--shutdown"))
        {
            Native.PostMessage(Native.FindWindow(null, MainWindow.NativeTitle), MainWindow.QuitMessage, IntPtr.Zero, IntPtr.Zero);
            Exit(); return;
        }
        bool smoke = Environment.GetCommandLineArgs().Contains("--smoke-test");
        instanceMutex = new Mutex(true, smoke ? "Local\\ThreeFingerWindowDrag.Smoke" : "Local\\ThreeFingerWindowDrag.1", out bool created);
        if (!created)
        {
            Native.PostMessage(Native.FindWindow(null, MainWindow.NativeTitle), MainWindow.ShowMessage, IntPtr.Zero, IntPtr.Zero);
            Exit(); return;
        }
        window = new MainWindow();
        if (Environment.GetCommandLineArgs().Contains("--tray") && !window.NeedsWelcome) window.HideToTray();
        else window.Activate();
    }
}
