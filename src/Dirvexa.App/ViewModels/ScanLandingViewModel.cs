using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dirvexa.Core;

namespace Dirvexa.App.ViewModels;

public sealed partial class ScanLandingViewModel(
  IScanRepository repository,
  Func<Task<string?>> pickFolder,
  Func<string, Task> startScan,
  Func<Guid, Task> openScan,
  Func<Task> showHistory) : ObservableObject
{
  public ObservableCollection<ScanListItemViewModel> RecentScans { get; } = [];

  [ObservableProperty]
  private string _pathInput = string.Empty;

  [ObservableProperty]
  private string _errorMessage = string.Empty;

  [ObservableProperty]
  private bool _isStarting;

  public bool HasRecentScans => RecentScans.Count > 0;
  public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

  partial void OnErrorMessageChanged(string value) => OnPropertyChanged(nameof(HasError));

  public async Task ReloadAsync()
  {
    var scans = await repository.GetRecentScansAsync(5);
    RecentScans.Clear();
    foreach (var scan in scans) RecentScans.Add(new ScanListItemViewModel(scan));
    OnPropertyChanged(nameof(HasRecentScans));
  }

  [RelayCommand]
  private async Task BrowseAsync()
  {
    var selected = await pickFolder();
    if (!string.IsNullOrWhiteSpace(selected))
    {
      PathInput = selected;
      ErrorMessage = string.Empty;
    }
  }

  [RelayCommand]
  private async Task StartAsync()
  {
    if (IsStarting) return;
    IsStarting = true;
    ErrorMessage = string.Empty;
    try { await startScan(PathInput); }
    finally { IsStarting = false; }
  }

  [RelayCommand]
  private Task OpenRecentAsync(ScanListItemViewModel? scan) => scan is null ? Task.CompletedTask : openScan(scan.Id);

  [RelayCommand]
  private Task ViewAllAsync() => showHistory();
}
