using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dirvexa.Core;

namespace Dirvexa.App.ViewModels;

public sealed partial class ResultsViewModel(
  IScanRepository repository,
  IPlatformFileActions fileActions,
  Func<Task> newScan) : ObservableObject
{
  private const int PageSize = 200;
  private Guid _scanId;
  private EntrySort _sort = EntrySort.Size;
  private SortDirection _sortDirection = SortDirection.Descending;

  public ObservableCollection<SummaryMetricViewModel> Metrics { get; } = [];
  public ObservableCollection<EntryItemViewModel> Entries { get; } = [];
  public ObservableCollection<FolderNodeViewModel> FolderRoots { get; } = [];
  public ObservableCollection<string> Issues { get; } = [];
  public ObservableCollection<CategoryFilterViewModel> CategoryFilters { get; } =
  [
    new("All categories", null),
    .. Enum.GetValues<FileCategory>().Select(category => new CategoryFilterViewModel(CategoryNames.For(category), category))
  ];
  public ObservableCollection<HiddenFilterViewModel> HiddenFilters { get; } =
  [
    new("All items", HiddenFilter.All),
    new("Hidden only", HiddenFilter.HiddenOnly),
    new("Visible only", HiddenFilter.VisibleOnly)
  ];

  [ObservableProperty] private string _title = "Results";
  [ObservableProperty] private string _rootPath = string.Empty;
  [ObservableProperty] private string _statusLabel = string.Empty;
  [ObservableProperty] private string _searchText = string.Empty;
  [ObservableProperty] private string _selectedFolderPath = string.Empty;
  [ObservableProperty] private CategoryFilterViewModel? _selectedCategoryFilter;
  [ObservableProperty] private HiddenFilterViewModel? _selectedHiddenFilter;
  [ObservableProperty] private EntryItemViewModel? _selectedEntry;
  [ObservableProperty] private int _currentPage = 1;
  [ObservableProperty] private int _totalPages = 1;
  [ObservableProperty] private string _resultCount = "0 items";
  [ObservableProperty] private string _notice = string.Empty;
  [ObservableProperty] private bool _isLoading;
  [ObservableProperty] private bool _areIssuesVisible;
  [ObservableProperty] private bool _isTreeVisible = true;

  public bool HasIssues => Issues.Count > 0;
  public bool HasNotice => !string.IsNullOrWhiteSpace(Notice);
  public bool CanGoBack => CurrentPage > 1;
  public bool CanGoForward => CurrentPage < TotalPages;
  public bool HasSelectedEntry => SelectedEntry is not null;
  public bool HasEntries => Entries.Count > 0;

  partial void OnNoticeChanged(string value) => OnPropertyChanged(nameof(HasNotice));
  partial void OnSelectedEntryChanged(EntryItemViewModel? value) => OnPropertyChanged(nameof(HasSelectedEntry));

  public async Task LoadAsync(Guid scanId)
  {
    _scanId = scanId;
    var scan = await repository.GetScanAsync(scanId) ?? throw new InvalidOperationException("The scan could not be found.");
    Title = scan.DisplayName;
    RootPath = scan.RootPath;
    SelectedFolderPath = scan.RootPath;
    StatusLabel = scan.Status switch
    {
      ScanStatus.CanceledPartial => "Canceled / Partial",
      ScanStatus.Failed => "Completed with errors",
      _ => "Completed"
    };

    BuildMetrics(scan);
    Issues.Clear();
    foreach (var issue in await repository.GetIssuesAsync(scanId, 100))
      Issues.Add($"{issue.Path} · {issue.Message}");
    OnPropertyChanged(nameof(HasIssues));

    FolderRoots.Clear();
    var rootNode = new FolderNodeViewModel(scan.RootPath, scan.DisplayName, scan.Totals.FolderCount > 0, LoadChildrenAsync)
    {
      IsExpanded = true
    };
    FolderRoots.Add(rootNode);
    SelectedCategoryFilter = CategoryFilters[0];
    SelectedHiddenFilter = HiddenFilters[0];
    CurrentPage = 1;
    await LoadEntriesAsync();
  }

  public async Task SelectFolderAsync(FolderNodeViewModel? folder)
  {
    if (folder is null || string.IsNullOrWhiteSpace(folder.FullPath)) return;
    SelectedFolderPath = folder.FullPath;
    SearchText = string.Empty;
    CurrentPage = 1;
    await LoadEntriesAsync();
  }

  public async Task SortAsync(EntrySort sort)
  {
    if (_sort == sort)
      _sortDirection = _sortDirection == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending;
    else
    {
      _sort = sort;
      _sortDirection = sort is EntrySort.Name or EntrySort.Path or EntrySort.Category
        ? SortDirection.Ascending
        : SortDirection.Descending;
    }
    CurrentPage = 1;
    await LoadEntriesAsync();
  }

  [RelayCommand]
  private async Task ApplyFiltersAsync()
  {
    CurrentPage = 1;
    await LoadEntriesAsync();
  }

  [RelayCommand]
  private async Task ClearSearchAsync()
  {
    SearchText = string.Empty;
    CurrentPage = 1;
    await LoadEntriesAsync();
  }

  [RelayCommand]
  private async Task PreviousPageAsync()
  {
    if (!CanGoBack) return;
    CurrentPage--;
    await LoadEntriesAsync();
  }

  [RelayCommand]
  private async Task NextPageAsync()
  {
    if (!CanGoForward) return;
    CurrentPage++;
    await LoadEntriesAsync();
  }

  [RelayCommand]
  private Task NewScanAsync() => newScan();

  [RelayCommand]
  private async Task OpenEntryAsync() => await RunFileActionAsync(path => fileActions.OpenAsync(path));

  [RelayCommand]
  private async Task RevealEntryAsync() => await RunFileActionAsync(path => fileActions.RevealAsync(path));

  [RelayCommand]
  private async Task CopyPathAsync()
  {
    await RunFileActionAsync(path => fileActions.CopyPathAsync(path));
    if (SelectedEntry is not null) Notice = "Path copied to the clipboard.";
  }

  [RelayCommand]
  private void ToggleIssues() => AreIssuesVisible = !AreIssuesVisible;

  [RelayCommand]
  private void ToggleTree() => IsTreeVisible = !IsTreeVisible;

  private async Task LoadEntriesAsync()
  {
    if (_scanId == Guid.Empty) return;
    IsLoading = true;
    Notice = string.Empty;
    try
    {
      var result = await repository.QueryEntriesAsync(new EntryQuery(
        _scanId,
        SelectedFolderPath,
        SearchText,
        SelectedCategoryFilter?.Value,
        SelectedHiddenFilter?.Value ?? HiddenFilter.All,
        _sort,
        _sortDirection,
        CurrentPage,
        PageSize));
      Entries.Clear();
      foreach (var item in result.Items) Entries.Add(new EntryItemViewModel(item));
      OnPropertyChanged(nameof(HasEntries));
      TotalPages = result.TotalPages;
      ResultCount = $"{result.TotalCount:N0} {(result.TotalCount == 1 ? "item" : "items")}";
      OnPropertyChanged(nameof(CanGoBack));
      OnPropertyChanged(nameof(CanGoForward));
    }
    finally
    {
      IsLoading = false;
    }
  }

  private async Task LoadChildrenAsync(FolderNodeViewModel node)
  {
    var page = await repository.GetChildFoldersAsync(_scanId, node.FullPath);
    node.Children.Clear();
    foreach (var child in page.Items)
      node.Children.Add(new FolderNodeViewModel(child.FullPath, child.Name, child.HasChildren, LoadChildrenAsync));
    if (page.IsTruncated)
      Notice = "This folder has more than 2,000 direct subfolders. Use scan-wide search to inspect the remaining items.";
  }

  private void BuildMetrics(ScanRecord scan)
  {
    Metrics.Clear();
    Metrics.Add(new SummaryMetricViewModel("Folder size", ByteFormatter.Format(scan.Totals.TotalBytes), "Logical size"));
    Metrics.Add(new SummaryMetricViewModel("All folders", scan.Totals.FolderCount.ToString("N0")));
    Metrics.Add(new SummaryMetricViewModel("Hidden folders", scan.Totals.HiddenFolderCount.ToString("N0")));
    Metrics.Add(new SummaryMetricViewModel("All files", scan.Totals.FileCount.ToString("N0")));
    Metrics.Add(new SummaryMetricViewModel("Hidden files", scan.Totals.HiddenFileCount.ToString("N0")));
    var totals = scan.Totals.Categories.ToDictionary(total => total.Category);
    foreach (var category in Enum.GetValues<FileCategory>())
    {
      var total = totals.GetValueOrDefault(category, new CategoryTotal(category, 0, 0));
      Metrics.Add(new SummaryMetricViewModel(CategoryNames.For(category), total.FileCount.ToString("N0"), ByteFormatter.Format(total.TotalBytes)));
    }
  }

  private async Task RunFileActionAsync(Func<string, Task> action)
  {
    if (SelectedEntry is null) return;
    Notice = string.Empty;
    try { await action(SelectedEntry.FullPath); }
    catch (Exception exception) { Notice = exception.Message; }
  }
}
