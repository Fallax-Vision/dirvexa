using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dirvexa.App.Infrastructure;
using Dirvexa.Core;

namespace Dirvexa.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
  private readonly AppSettingsService _settingsService;
  private readonly IScanRepository _repository;
  private readonly Action<ThemePreference> _applyTheme;
  private readonly Func<string, string, Task<bool>> _confirm;
  private readonly AppLog _log;

  public SettingsViewModel(
    AppSettingsService settingsService,
    AppPaths paths,
    IScanRepository repository,
    Action<ThemePreference> applyTheme,
    Func<string, string, Task<bool>> confirm,
    AppLog log)
  {
    _settingsService = settingsService;
    _repository = repository;
    _applyTheme = applyTheme;
    _confirm = confirm;
    _log = log;
    DataFolder = paths.Root;
    _selectedTheme = settingsService.Load().Theme;
  }

  public string DataFolder { get; }

  [ObservableProperty]
  private ThemePreference _selectedTheme;

  [ObservableProperty]
  private string _storageUsed = "0 B";

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

  private async Task SaveThemeAsync(ThemePreference value)
  {
    try { await _settingsService.SaveAsync(new AppSettings(value)); }
    catch (Exception exception) { await _log.WriteAsync("Could not save the theme preference.", exception); }
  }
}
