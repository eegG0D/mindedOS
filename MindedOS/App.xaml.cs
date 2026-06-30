using System.IO;
using System.Windows;
using System.Windows.Threading;
using MindedOS.Shell;

namespace MindedOS;

public partial class App : Application
{
    public static OsContext Os { get; private set; } = null!;

    private static readonly string LogPath =
        Path.Combine(AppContext.BaseDirectory, "mindedos-startup.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log("AppDomain unhandled: " + args.ExceptionObject);

        try
        {
            File.WriteAllText(LogPath, $"start {DateTime.Now}\n");
            base.OnStartup(e);
            Os = new OsContext();
            Os.Initialize();
            Log($"initialized: {Os.Programs.Count} programs, {Os.Actions.Count} actions, lexicon {Os.Lexicon.Count}");
        }
        catch (Exception ex)
        {
            Log("OnStartup failed: " + ex);
            throw;
        }
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Log only — never pop a modal dialog (it would hang headless --shot runs).
        Log("Dispatcher unhandled: " + e.Exception);
        e.Handled = true;
    }

    private static void Log(string message)
    {
        try { File.AppendAllText(LogPath, message + "\n"); } catch { /* ignore */ }
    }
}
