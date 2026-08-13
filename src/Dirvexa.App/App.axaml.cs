using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Dirvexa.App.Infrastructure;
using Dirvexa.App.ViewModels;
using Dirvexa.App.Views;
using Dirvexa.Core;

namespace Dirvexa.App;

public partial class App : Application
{
  public override void Initialize() => AvaloniaXamlLoader.Load(this);

  public override void OnFrameworkInitializationCompleted()
  {
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
      var paths = new AppPaths();
      var log = new AppLog(paths);
      var settingsService = new AppSettingsService(paths);
      var repository = new SqliteScanRepository(paths);
      repository.InitializeAsync().GetAwaiter().GetResult();
      var settings = settingsService.Load();
      ApplyTheme(settings.Theme);

      MainWindow? window = null;
      async Task<string?> PickFolderAsync()
      {
        if (window is null) return null;
        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
          Title = "Choose a folder to scan",
          AllowMultiple = false
        });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
      }

      var fileActions = new WindowsPlatformFileActions(() => window);
      var scanService = new FileSystemScanService(repository, new WindowsDirectoryTraversalGuard());
      var viewModel = new MainWindowViewModel(
        scanService,
        repository,
        settingsService,
        paths,
        fileActions,
        PickFolderAsync,
        ApplyTheme,
        log);
      window = new MainWindow { DataContext = viewModel };
      desktop.MainWindow = window;
      _ = viewModel.InitializeAsync();
    }

    base.OnFrameworkInitializationCompleted();
  }

  private void ApplyTheme(ThemePreference preference)
  {
    RequestedThemeVariant = preference switch
    {
      ThemePreference.Light => ThemeVariant.Light,
      ThemePreference.Dark => ThemeVariant.Dark,
      _ => ThemeVariant.Default
    };
  }
}
