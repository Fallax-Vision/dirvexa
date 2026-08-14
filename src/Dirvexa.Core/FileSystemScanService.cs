using System.Diagnostics;

namespace Dirvexa.Core;

public sealed class FileSystemScanService : IScanService
{
  private const int EntryBatchSize = 1024;
  private const int IssueBatchSize = 64;
  private const int MaxPendingDirectories = 100_000;
  private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);
  private readonly IScanRepository _repository;
  private readonly IDirectoryTraversalGuard _traversalGuard;

  public FileSystemScanService(IScanRepository repository, IDirectoryTraversalGuard? traversalGuard = null)
  {
    _repository = repository;
    _traversalGuard = traversalGuard ?? new ManagedDirectoryTraversalGuard();
  }

  public Task<ScanOutcome> StartAsync(
    ScanRequest request,
    IProgress<ScanProgress>? progress,
    CancellationToken cancellationToken = default) =>
    Task.Run(() => RunAsync(request, progress, cancellationToken), CancellationToken.None);

  private async Task<ScanOutcome> RunAsync(
    ScanRequest request,
    IProgress<ScanProgress>? progress,
    CancellationToken cancellationToken)
  {
    var validation = PathValidator.ValidateLocalDirectory(request.RootPath);
    if (!validation.IsValid || validation.NormalizedPath is null)
      throw new ArgumentException(validation.Error, nameof(request));

    var rootPath = validation.NormalizedPath;
    var startedAt = DateTimeOffset.UtcNow;
    var scanId = await _repository.CreateScanAsync(rootPath, GetDisplayName(rootPath), startedAt, cancellationToken);
    var stopwatch = Stopwatch.StartNew();
    var lastProgress = TimeSpan.Zero;
    var counters = new ScanCounters();
    var entries = new List<ScanEntryDraft>(EntryBatchSize);
    var issues = new List<ScanIssueDraft>(IssueBatchSize);
    var directories = new Stack<string>();
    directories.Push(rootPath);
    var currentPath = rootPath;

    try
    {
      while (directories.Count > 0)
      {
        cancellationToken.ThrowIfCancellationRequested();
        currentPath = directories.Pop();
        ReportIfDue(force: false);

        try
        {
          var traversalLease = _traversalGuard.Acquire(rootPath, currentPath);
          if (!traversalLease.IsValid || traversalLease.Lease is null)
          {
            AddIssue(currentPath, traversalLease.Error ?? "Skipped an unsafe directory.");
            if (issues.Count >= IssueBatchSize) await FlushAsync(cancellationToken);
            continue;
          }
          using var lease = traversalLease.Lease;

          foreach (var item in lease.Enumerate())
          {
            cancellationToken.ThrowIfCancellationRequested();
            currentPath = item.FullPath;

            try
            {
              var attributes = item.Attributes;
              if ((attributes & FileAttributes.ReparsePoint) != 0)
              {
                AddIssue(item.FullPath, "Skipped reparse point.");
                if (issues.Count >= IssueBatchSize) await FlushAsync(cancellationToken);
                continue;
              }

              var hidden = (attributes & FileAttributes.Hidden) != 0;
              if ((attributes & FileAttributes.Directory) != 0)
              {
                counters.FolderCount++;
                if (hidden) counters.HiddenFolderCount++;
                entries.Add(new ScanEntryDraft(
                  Path.GetDirectoryName(item.FullPath) ?? rootPath,
                  item.FullPath,
                  item.Name,
                  string.Empty,
                  EntryKind.Directory,
                  FileCategory.Other,
                  0,
                  item.ModifiedAt,
                  hidden));
                if (directories.Count >= MaxPendingDirectories)
                  AddIssue(item.FullPath, "Skipped because the bounded traversal queue is full.");
                else
                  directories.Push(item.FullPath);
              }
              else
              {
                var size = item.Length;
                var extension = Path.GetExtension(item.Name) ?? string.Empty;
                var category = FileClassifier.Classify(extension);
                if (counters.AddFile(size, hidden, category))
                  AddIssue(item.FullPath, "Logical size exceeded the supported limit. Byte totals were capped.");
                entries.Add(new ScanEntryDraft(
                  Path.GetDirectoryName(item.FullPath) ?? rootPath,
                  item.FullPath,
                  item.Name,
                  extension,
                  EntryKind.File,
                  category,
                  size,
                  item.ModifiedAt,
                  hidden));
              }
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
              AddIssue(item.FullPath, exception.Message);
            }

            if (entries.Count >= EntryBatchSize || issues.Count >= IssueBatchSize)
              await FlushAsync(cancellationToken);

            ReportIfDue(force: false);
          }
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
          AddIssue(currentPath, exception.Message);
          if (issues.Count >= IssueBatchSize) await FlushAsync(cancellationToken);
        }
      }

      await FlushAsync(cancellationToken);
      var completedTotals = counters.ToTotals();
      await _repository.FinalizeScanAsync(scanId, ScanStatus.Completed, completedTotals, DateTimeOffset.UtcNow, CancellationToken.None);
      ReportIfDue(force: true);
      return new ScanOutcome(scanId, ScanStatus.Completed, completedTotals);
    }
    catch (OperationCanceledException)
    {
      await FlushAsync(CancellationToken.None);
      var partialTotals = counters.ToTotals();
      await _repository.FinalizeScanAsync(scanId, ScanStatus.CanceledPartial, partialTotals, DateTimeOffset.UtcNow, CancellationToken.None);
      ReportIfDue(force: true);
      return new ScanOutcome(scanId, ScanStatus.CanceledPartial, partialTotals);
    }
    catch (Exception exception)
    {
      AddIssue(currentPath, exception.Message);
      await FlushAsync(CancellationToken.None);
      var failedTotals = counters.ToTotals();
      await _repository.FinalizeScanAsync(scanId, ScanStatus.Failed, failedTotals, DateTimeOffset.UtcNow, CancellationToken.None);
      return new ScanOutcome(scanId, ScanStatus.Failed, failedTotals);
    }

    async Task FlushAsync(CancellationToken token)
    {
      if (entries.Count == 0 && issues.Count == 0) return;
      var entryBatch = entries.ToArray();
      var issueBatch = issues.ToArray();
      await _repository.AppendBatchAsync(scanId, entryBatch, issueBatch, token);
      entries.Clear();
      issues.Clear();
    }

    void AddIssue(string path, string message)
    {
      counters.IssueCount++;
      issues.Add(new ScanIssueDraft(path, message));
    }

    void ReportIfDue(bool force)
    {
      if (!force && stopwatch.Elapsed - lastProgress < ProgressInterval) return;
      lastProgress = stopwatch.Elapsed;
      progress?.Report(new ScanProgress(
        scanId,
        currentPath,
        counters.FileCount,
        counters.FolderCount,
        counters.TotalBytes,
        counters.IssueCount,
        stopwatch.Elapsed));
    }
  }

  private static bool IsRecoverable(Exception exception) =>
    exception is UnauthorizedAccessException or IOException or PathTooLongException or DirectoryNotFoundException or FileNotFoundException;

  private static string GetDisplayName(string rootPath)
  {
    var directoryName = new DirectoryInfo(rootPath).Name;
    if (!string.IsNullOrWhiteSpace(directoryName)) return directoryName;
    try
    {
      var drive = new DriveInfo(rootPath);
      return string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.Name : drive.VolumeLabel;
    }
    catch
    {
      return rootPath;
    }
  }

  private sealed class ScanCounters
  {
    private readonly Dictionary<FileCategory, (long Count, long Bytes)> _categories =
      Enum.GetValues<FileCategory>().ToDictionary(category => category, _ => (0L, 0L));
    private bool _sizeOverflowed;

    public long TotalBytes { get; private set; }
    public long FileCount { get; private set; }
    public long FolderCount { get; set; }
    public long HiddenFileCount { get; private set; }
    public long HiddenFolderCount { get; set; }
    public long IssueCount { get; set; }

    public bool AddFile(long size, bool hidden, FileCategory category)
    {
      FileCount++;
      if (hidden) HiddenFileCount++;
      var current = _categories[category];
      TotalBytes = SaturatingMath.AddNonNegative(TotalBytes, size, out var totalSaturated);
      var categoryBytes = SaturatingMath.AddNonNegative(current.Bytes, size, out var categorySaturated);
      _categories[category] = (current.Count + 1, categoryBytes);
      var newlyOverflowed = !_sizeOverflowed && (totalSaturated || categorySaturated);
      _sizeOverflowed |= totalSaturated || categorySaturated;
      return newlyOverflowed;
    }

    public ScanTotals ToTotals() => new(
      TotalBytes,
      FileCount,
      FolderCount,
      HiddenFileCount,
      HiddenFolderCount,
      IssueCount,
      _categories.Select(pair => new CategoryTotal(pair.Key, pair.Value.Count, pair.Value.Bytes)).ToArray());
  }
}
