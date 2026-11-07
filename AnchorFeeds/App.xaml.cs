using System.Windows;
using AnchorFeeds.Services;
using Application = System.Windows.Application;

namespace AnchorFeeds;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private SingleInstanceService? _singleInstanceService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DiagnosticLog.LoggingFailed += message =>
            Dispatcher.BeginInvoke(() => MessageBox.Show(
                message,
                "Debug logging unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Warning));
        DispatcherUnhandledException += (_, args) =>
            DiagnosticLog.Exception("Unhandled UI exception", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                DiagnosticLog.Exception("Unhandled application exception", exception);
            }
            else
            {
                DiagnosticLog.Write("Unhandled application exception", args.ExceptionObject?.ToString() ?? "Unknown exception object.");
            }
        };

        try
        {
            var singleInstance = new SingleInstanceService();
            if (!singleInstance.IsPrimary)
            {
                if (!singleInstance.RequestActivation(TimeSpan.FromSeconds(5)))
                {
                    MessageBox.Show(
                        "AnchorFeeds is already running, but its window could not be activated.",
                        "AnchorFeeds is already running",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                singleInstance.Dispose();
                Shutdown();
                return;
            }

            _singleInstanceService = singleInstance;
            singleInstance.ActivationRequested += ActivateMainWindow;

            var settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.yaml");
            var settings = new SettingsService(settingsPath).Load();
            DiagnosticLog.Configure(settings.DebugLoggingEnabled);
            DiagnosticLog.Write("Startup", $"Application startup started. Base directory: {AppContext.BaseDirectory}");
            LocalizationService.Configure(settings.ApplicationLanguage);
            try
            {
                StartupRegistrationService.SetEnabled(settings.LaunchAtStartup);
            }
            catch (Exception ex)
            {
                DiagnosticLog.Exception("Application startup failed", ex);
                MessageBox.Show(
                    $"Could not configure Windows startup: {ex.Message}\n\nAnchorFeeds will continue running, but automatic launch may not work.",
                    "Startup registration failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            var mainWindow = new MainWindow();
            if (!mainWindow.StartupSucceeded)
            {
                Shutdown(-1);
                return;
            }

            MainWindow = mainWindow;
            mainWindow.StartMinimizedToTray = settings.LaunchAtStartup && settings.MinimizeToTray;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Could not start AnchorFeeds",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private void ActivateMainWindow()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (MainWindow is not { } window)
            {
                return;
            }

            window.ShowInTaskbar = true;
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            window.Show();
            window.Activate();
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DiagnosticLog.Write("Shutdown", $"Application exiting with code {e.ApplicationExitCode}.");
        _singleInstanceService?.Dispose();
        _singleInstanceService = null;
        base.OnExit(e);
    }
}
