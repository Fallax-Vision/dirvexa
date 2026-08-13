using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dirvexa.Core;

namespace Dirvexa.App.ViewModels;

public sealed partial class ProgressViewModel(string rootPath, Func<Task> requestCancel) : ObservableObject
{
  public string RootPath { get; } = rootPath;

  [ObservableProperty] private string _currentPath = rootPath;
  [ObservableProperty] private string _files = "0";
  [ObservableProperty] private string _folders = "0";
  [ObservableProperty] private string _size = "0 B";
  [ObservableProperty] private string _issues = "0";
  [ObservableProperty] private string _elapsed = "00:00";

  public void Update(ScanProgress progress)
  {
    CurrentPath = progress.CurrentPath;
    Files = progress.FileCount.ToString("N0");
    Folders = progress.FolderCount.ToString("N0");
    Size = ByteFormatter.Format(progress.TotalBytes);
    Issues = progress.IssueCount.ToString("N0");
    Elapsed = progress.Elapsed.ToString(progress.Elapsed.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss");
  }

  [RelayCommand]
  private Task CancelAsync() => requestCancel();
}
