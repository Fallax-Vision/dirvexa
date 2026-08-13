using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dirvexa.Core;

namespace Dirvexa.App.ViewModels;

public sealed partial class HistoryViewModel(
  IScanRepository repository,
  Func<Guid, Task> openScan,
  Func<string, string, Task<bool>> confirm) : ObservableObject
{
  public ObservableCollection<ScanListItemViewModel> Scans { get; } = [];

  [ObservableProperty]
  private bool _isLoading;

  public bool HasScans => Scans.Count > 0;

  public async Task ReloadAsync()
  {
    IsLoading = true;
    try
    {
      var scans = await repository.GetHistoryAsync();
      Scans.Clear();
      foreach (var scan in scans) Scans.Add(new ScanListItemViewModel(scan));
      OnPropertyChanged(nameof(HasScans));
    }
    finally
    {
      IsLoading = false;
    }
  }

  [RelayCommand]
  private Task OpenAsync(ScanListItemViewModel? scan) => scan is null ? Task.CompletedTask : openScan(scan.Id);

  [RelayCommand]
  private async Task DeleteAsync(ScanListItemViewModel? scan)
  {
    if (scan is null) return;
    if (!await confirm("Delete this scan?", $"The saved results for {scan.Name} will be removed. Files on disk are never changed.")) return;
    await repository.DeleteScanAsync(scan.Id);
    await ReloadAsync();
  }

  [RelayCommand]
  private async Task ClearAsync()
  {
    if (!HasScans) return;
    if (!await confirm("Clear scan history?", "All saved scan snapshots will be removed. Files on disk are never changed.")) return;
    await repository.ClearHistoryAsync();
    await ReloadAsync();
  }
}
