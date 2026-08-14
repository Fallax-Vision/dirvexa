namespace Dirvexa.Core;

public interface IScanService
{
  Task<ScanOutcome> StartAsync(
    ScanRequest request,
    IProgress<ScanProgress>? progress,
    CancellationToken cancellationToken = default);
}

public interface IDirectoryTraversalGuard
{
  TraversalLeaseResult Acquire(string rootPath, string directoryPath);
}

public interface IDirectoryTraversalLease : IDisposable
{
  IEnumerable<DirectoryEntrySnapshot> Enumerate();
}

public interface IScanRepository
{
  Task InitializeAsync(CancellationToken cancellationToken = default);
  Task<Guid> CreateScanAsync(string rootPath, string displayName, DateTimeOffset startedAt, CancellationToken cancellationToken);
  Task AppendBatchAsync(
    Guid scanId,
    IReadOnlyCollection<ScanEntryDraft> entries,
    IReadOnlyCollection<ScanIssueDraft> issues,
    CancellationToken cancellationToken);
  Task FinalizeScanAsync(
    Guid scanId,
    ScanStatus status,
    ScanTotals totals,
    DateTimeOffset completedAt,
    CancellationToken cancellationToken);
  Task DiscardScanAsync(Guid scanId, CancellationToken cancellationToken = default);
  Task<ScanRecord?> GetScanAsync(Guid scanId, CancellationToken cancellationToken = default);
  Task<IReadOnlyList<ScanRecord>> GetRecentScansAsync(int limit, CancellationToken cancellationToken = default);
  Task<IReadOnlyList<ScanRecord>> GetHistoryAsync(CancellationToken cancellationToken = default);
  Task<PagedResult<ScanEntryRecord>> QueryEntriesAsync(EntryQuery query, CancellationToken cancellationToken = default);
  Task<FolderPage> GetChildFoldersAsync(Guid scanId, string parentPath, CancellationToken cancellationToken = default);
  Task<IReadOnlyList<ScanIssueRecord>> GetIssuesAsync(Guid scanId, int limit, CancellationToken cancellationToken = default);
  Task DeleteScanAsync(Guid scanId, CancellationToken cancellationToken = default);
  Task ClearHistoryAsync(CancellationToken cancellationToken = default);
  Task EnforceRetentionAsync(int keepCount, CancellationToken cancellationToken = default);
  long GetStorageSizeBytes();
}

public interface IPlatformFileActions
{
  Task OpenAsync(string path);
  Task RevealAsync(string path);
  Task CopyPathAsync(string path);
  Task OpenUriAsync(Uri uri);
}
