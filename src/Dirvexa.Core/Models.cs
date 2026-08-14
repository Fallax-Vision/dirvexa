namespace Dirvexa.Core;

public enum ThemePreference
{
  System,
  Light,
  Dark
}

public enum ScanStatus
{
  Running,
  Completed,
  CanceledPartial,
  Failed
}

public enum EntryKind
{
  File,
  Directory
}

public enum FileCategory
{
  Images,
  Audio,
  Videos,
  Documents,
  AppsPrograms,
  Compressed,
  Other
}

public enum HiddenFilter
{
  All,
  HiddenOnly,
  VisibleOnly
}

public enum EntrySort
{
  Name,
  Size,
  Modified,
  Path,
  Category
}

public enum SortDirection
{
  Ascending,
  Descending
}

public sealed record ScanRequest(string RootPath);

public sealed record ScanProgress(
  Guid ScanId,
  string CurrentPath,
  long FileCount,
  long FolderCount,
  long TotalBytes,
  long IssueCount,
  TimeSpan Elapsed);

public sealed record CategoryTotal(FileCategory Category, long FileCount, long TotalBytes);

public sealed record ScanTotals(
  long TotalBytes,
  long FileCount,
  long FolderCount,
  long HiddenFileCount,
  long HiddenFolderCount,
  long IssueCount,
  IReadOnlyList<CategoryTotal> Categories);

public sealed record ScanOutcome(Guid ScanId, ScanStatus Status, ScanTotals Totals);

public sealed record DirectoryEntrySnapshot(
  string FullPath,
  string Name,
  FileAttributes Attributes,
  long Length,
  DateTimeOffset ModifiedAt);

public sealed record TraversalLeaseResult(bool IsValid, IDirectoryTraversalLease? Lease, string? Error)
{
  public static TraversalLeaseResult Success(IDirectoryTraversalLease lease) => new(true, lease, null);
  public static TraversalLeaseResult Failure(string error) => new(false, null, error);
}

public sealed record ScanRecord(
  Guid Id,
  string RootPath,
  string DisplayName,
  DateTimeOffset StartedAt,
  DateTimeOffset? CompletedAt,
  ScanStatus Status,
  ScanTotals Totals);

public sealed record ScanEntryDraft(
  string ParentPath,
  string FullPath,
  string Name,
  string Extension,
  EntryKind Kind,
  FileCategory Category,
  long SizeBytes,
  DateTimeOffset ModifiedAt,
  bool IsHidden);

public sealed record ScanEntryRecord(
  long Id,
  Guid ScanId,
  string ParentPath,
  string FullPath,
  string Name,
  string Extension,
  EntryKind Kind,
  FileCategory Category,
  long SizeBytes,
  DateTimeOffset ModifiedAt,
  bool IsHidden);

public sealed record ScanIssueDraft(string Path, string Message);

public sealed record ScanIssueRecord(long Id, Guid ScanId, string Path, string Message);

public sealed record FolderRecord(string FullPath, string Name, bool HasChildren);

public sealed record FolderPage(IReadOnlyList<FolderRecord> Items, bool IsTruncated);

public sealed record EntryQuery(
  Guid ScanId,
  string? FolderPath,
  string? SearchText,
  FileCategory? Category,
  HiddenFilter Hidden,
  EntrySort Sort,
  SortDirection Direction,
  int Page,
  int PageSize);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, long TotalCount, int Page, int PageSize)
{
  public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
