using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dirvexa.App.Infrastructure;
using Dirvexa.Core;

namespace Dirvexa.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
  private readonly AppSettingsService _settingsService;
  private readonly IScanRepository _repository;
  private readonly IPlatformFileActions _fileActions;
  private readonly IUpdateService _updateService;
  private readonly Action<ThemePreference> _applyTheme;
  private readonly Func<string, string, Task<bool>> _confirm;
  private readonly AppLog _log;

  public SettingsViewModel(
    AppSettingsService settingsService,
    AppPaths paths,
    IScanRepository repository,
    IPlatformFileActions fileActions,
    IUpdateService updateService,
    SemanticVersion currentVersion,
    AppDistribution distribution,
    Action<ThemePreference> applyTheme,
    Func<string, string, Task<bool>> confirm,
    AppLog log)
  {
    _settingsService = settingsService;
    _repository = repository;
    _fileActions = fileActions;
    _updateService = updateService;
    _applyTheme = applyTheme;
    _confirm = confirm;
    _log = log;
    DataFolder = paths.Root;
    VersionLabel = $"Version {currentVersion} · {distribution}";
    _selectedTheme = settingsService.Load().Theme;
  }

  public string DataFolder { get; }
  public string VersionLabel { get; }

  [ObservableProperty]
  private ThemePreference _selectedTheme;

  [ObservableProperty]
  private string _storageUsed = "0 B";

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(HasReleaseLink))]
  private Uri? _releaseUri;

  [ObservableProperty]
  private string _updateMessage = "Updates are checked only when you choose.";

  [ObservableProperty]
  private bool _isCheckingForUpdates;

  public bool HasReleaseLink => ReleaseUri is not null;

  public bool IsSystemTheme
  {
    get => SelectedTheme == ThemePreference.System;
    set { if (value) SelectedTheme = ThemePreference.System; }
  }

  public bool IsLightTheme
  {
    get => SelectedTheme == ThemePreference.Light;
    set { if (value) SelectedTheme = ThemePreference.Light; }
  }

  public bool IsDarkTheme
  {
    get => SelectedTheme == ThemePreference.Dark;
    set { if (value) SelectedTheme = ThemePreference.Dark; }
  }

  partial void OnSelectedThemeChanged(ThemePreference value)
  {
    OnPropertyChanged(nameof(IsSystemTheme));
    OnPropertyChanged(nameof(IsLightTheme));
    OnPropertyChanged(nameof(IsDarkTheme));
    _applyTheme(value);
    _ = SaveThemeAsync(value);
  }

  public Task ReloadAsync()
  {
    StorageUsed = ByteFormatter.Format(_repository.GetStorageSizeBytes());
    return Task.CompletedTask;
  }

  [RelayCommand]
  private void SetTheme(string? theme)
  {
    if (Enum.TryParse<ThemePreference>(theme, true, out var preference))
    {
      SelectedTheme = preference;
    }
  }

  [RelayCommand]
  private async Task ClearHistoryAsync()
  {
    if (!await _confirm("Clear scan history?", "All saved snapshots will be removed and the local database will be compacted.")) return;
    await _repository.ClearHistoryAsync();
    await ReloadAsync();
  }

  [RelayCommand(AllowConcurrentExecutions = false)]
  private async Task CheckForUpdatesAsync()
  {
    IsCheckingForUpdates = true;
    ReleaseUri = null;
    UpdateMessage = "Checking GitHub for the latest release…";
    try
    {
      var result = await _updateService.CheckAsync();
      UpdateMessage = result.Message;
      if (result.Status == UpdateCheckStatus.UpdateAvailable) ReleaseUri = result.ReleaseUri;
    }
    catch (Exception exception)
    {
      UpdateMessage = "The update check failed unexpectedly.";
      await _log.WriteAsync("The manual update check failed.", exception);
    }
    finally
    {
      IsCheckingForUpdates = false;
    }
  }

  [RelayCommand]
  private async Task OpenReleaseAsync()
  {
    if (ReleaseUri is null) return;
    try { await _fileActions.OpenUriAsync(ReleaseUri); }
    catch (Exception exception)
    {
      UpdateMessage = "The release page could not be opened.";
      await _log.WriteAsync("Could not open the official release page.", exception);
    }
  }

  private async Task SaveThemeAsync(ThemePreference value)
  {
    try { await _settingsService.SaveAsync(new AppSettings(value)); }
    catch (Exception exception) { await _log.WriteAsync("Could not save the theme preference.", exception); }
  }
}
