using System.Windows;
using AnchorFeeds.Services;

namespace AnchorFeeds;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.yaml");
            var settings = new SettingsService(settingsPath).Load();
            LocalizationService.Configure(settings.ApplicationLanguage);
            var mainWindow = new MainWindow();
            if (!mainWindow.StartupSucceeded)
            {
                Shutdown(-1);
                return;
            }

            MainWindow = mainWindow;
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
}
