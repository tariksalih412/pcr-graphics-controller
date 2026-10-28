using System.IO;
using System.Windows;

namespace PcrGraphics.App;

public partial class App : Application
{
    /// <summary>Next to the exe; if that folder is read-only, %LOCALAPPDATA%\PcrGraphics\crash.log.</summary>
    private static string CrashLog = Path.Combine(AppContext.BaseDirectory, "crash.log");

    public App()
    {
        // Errors on the UI thread: log, show, and keep the app running.
        DispatcherUnhandledException += (_, e) =>
        {
            Write("UI thread", e.Exception);
            MessageBox.Show(e.Exception.Message + "\n\nDetails in:\n" + CrashLog, "PCR Graphics error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
        // Errors on background threads and forgotten tasks.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write("background thread", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Write("task", e.Exception); e.SetObserved(); };
    }

    private static void Write(string where, Exception? ex)
    {
        var text = $"==== {DateTime.Now:yyyy-MM-dd HH:mm:ss} · {where}\n{ex}\n\n";
        try { File.AppendAllText(CrashLog, text); return; }
        catch { /* read-only folder: try AppData below */ }
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PcrGraphics");
            Directory.CreateDirectory(dir);
            CrashLog = Path.Combine(dir, "crash.log");
            File.AppendAllText(CrashLog, text);
        }
        catch { /* never crash while logging a crash */ }
    }
}