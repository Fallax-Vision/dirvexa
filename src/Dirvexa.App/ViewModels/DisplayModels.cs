using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Dirvexa.Core;

namespace Dirvexa.App.ViewModels;

public sealed class ScanListItemViewModel(ScanRecord record)
{
  public ScanRecord Record { get; } = record;
  public Guid Id => Record.Id;
  public string Name => Record.DisplayName;
  public string RootPath => Record.RootPath;
  public string Size => ByteFormatter.Format(Record.Totals.TotalBytes);
  public string Status => Record.Status switch
  {
    ScanStatus.CanceledPartial => "Canceled / Partial",
    ScanStatus.Failed => "Completed with errors",
    _ => "Completed"
  };
  public string Completed => Record.CompletedAt?.ToLocalTime().ToString("MMM d, yyyy · h:mm tt") ?? "In progress";
  public string Counts => $"{Record.Totals.FileCount:N0} files · {Record.Totals.FolderCount:N0} folders";
}

public sealed record SummaryMetricViewModel(string Label, string Value, string Detail = "");

public sealed record CategoryFilterViewModel(string Label, FileCategory? Value)
{
  public override string ToString() => Label;
}

public sealed record HiddenFilterViewModel(string Label, HiddenFilter Value)
{
  public override string ToString() => Label;
}

public sealed class EntryItemViewModel(ScanEntryRecord entry)
{
  public ScanEntryRecord Entry { get; } = entry;
  public string Name => Entry.Name;
  public string Kind => Entry.Kind == EntryKind.Directory ? "Folder" : CategoryNames.For(Entry.Category);
  public string Size => Entry.Kind == EntryKind.Directory ? "—" : ByteFormatter.Format(Entry.SizeBytes);
  public string Modified => Entry.ModifiedAt.ToLocalTime().ToString("MMM d, yyyy  h:mm tt");
  public string FullPath => Entry.FullPath;
  public string Hidden => Entry.IsHidden ? "Hidden" : string.Empty;
}

public static class CategoryNames
{
  public static string For(FileCategory category) => category switch
  {
    FileCategory.Images => "Images",
    FileCategory.Audio => "Audio",
    FileCategory.Videos => "Videos",
    FileCategory.Documents => "Documents",
    FileCategory.AppsPrograms => "Apps / Programs",
    FileCategory.Compressed => "Compressed",
    _ => "Other"
  };
}

public sealed partial class FolderNodeViewModel : ObservableObject
{
  private readonly Func<FolderNodeViewModel, Task> _loadChildren;
  private bool _hasLoaded;

  public FolderNodeViewModel(string fullPath, string name, bool hasChildren, Func<FolderNodeViewModel, Task> loadChildren)
  {
    FullPath = fullPath;
    Name = name;
    HasChildren = hasChildren;
    _loadChildren = loadChildren;
    if (hasChildren) Children.Add(new FolderNodeViewModel(string.Empty, "Loading…", false, _ => Task.CompletedTask));
  }

  public string FullPath { get; }
  public string Name { get; }
  public bool HasChildren { get; }
  public ObservableCollection<FolderNodeViewModel> Children { get; } = [];

  [ObservableProperty]
  private bool _isExpanded;

  partial void OnIsExpandedChanged(bool value)
  {
    if (value && !_hasLoaded)
    {
      _hasLoaded = true;
      _ = _loadChildren(this);
    }
  }
}
