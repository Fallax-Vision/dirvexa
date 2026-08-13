using Dirvexa.App.Infrastructure;
using Dirvexa.Core;

namespace Dirvexa.Tests;

public sealed class RepositoryAndScannerTests
{
  [Fact]
  public async Task PersistsAndReopensACompleteSnapshot()
  {
    using var data = TemporaryDirectory.Create();
    using var root = TemporaryDirectory.Create();
    Directory.CreateDirectory(Path.Combine(root.Path, "nested"));
    await File.WriteAllTextAsync(Path.Combine(root.Path, "photo.png"), "image-data");
    await File.WriteAllTextAsync(Path.Combine(root.Path, "nested", "notes.txt"), "notes");
    await File.WriteAllBytesAsync(Path.Combine(root.Path, "archive.zip"), new byte[64]);

    var repository = new SqliteScanRepository(new AppPaths(data.Path));
    await repository.InitializeAsync();
    var service = new FileSystemScanService(repository, new WindowsDirectoryTraversalGuard());
    var outcome = await service.StartAsync(new ScanRequest(root.Path), progress: null);

    var failureDetails = outcome.Status == ScanStatus.Completed
      ? string.Empty
      : string.Join(Environment.NewLine, (await repository.GetIssuesAsync(outcome.ScanId, 100)).Select(issue => $"{issue.Path}: {issue.Message}"));
    Assert.True(outcome.Status == ScanStatus.Completed, failureDetails);
    Assert.Equal(3, outcome.Totals.FileCount);
    Assert.Equal(1, outcome.Totals.FolderCount);
    Assert.Equal(79, outcome.Totals.TotalBytes);

    var saved = await repository.GetScanAsync(outcome.ScanId);
    Assert.NotNull(saved);
    Assert.Equal(3, saved.Totals.FileCount);
    Assert.Equal(1, saved.Totals.Categories.Single(item => item.Category == FileCategory.Images).FileCount);
    Assert.Equal(1, saved.Totals.Categories.Single(item => item.Category == FileCategory.Documents).FileCount);
    Assert.Equal(1, saved.Totals.Categories.Single(item => item.Category == FileCategory.Compressed).FileCount);

    var page = await repository.QueryEntriesAsync(new EntryQuery(
      outcome.ScanId, root.Path, null, null, HiddenFilter.All,
      EntrySort.Size, SortDirection.Descending, 1, 20));
    Assert.Equal(3, page.TotalCount);
    Assert.Contains(page.Items, entry => entry.Name == "nested" && entry.Kind == EntryKind.Directory);
  }

  [Fact]
  public async Task SearchFilteringAndPagingAreStable()
  {
    using var data = TemporaryDirectory.Create();
    var repository = new SqliteScanRepository(new AppPaths(data.Path));
    await repository.InitializeAsync();
    var scanId = await repository.CreateScanAsync(@"C:\Data", "Data", DateTimeOffset.UtcNow, CancellationToken.None);
    var entries = Enumerable.Range(1, 45)
      .Select(index => new ScanEntryDraft(@"C:\Data", $@"C:\Data\report-{index:00}.txt", $"report-{index:00}.txt", ".txt",
        EntryKind.File, FileCategory.Documents, index, DateTimeOffset.UtcNow, index % 2 == 0))
      .ToArray();
    await repository.AppendBatchAsync(scanId, entries, [], CancellationToken.None);
    var totals = new ScanTotals(entries.Sum(entry => entry.SizeBytes), 45, 0, 22, 0, 0,
      [new CategoryTotal(FileCategory.Documents, 45, entries.Sum(entry => entry.SizeBytes))]);
    await repository.FinalizeScanAsync(scanId, ScanStatus.Completed, totals, DateTimeOffset.UtcNow, CancellationToken.None);

    var page = await repository.QueryEntriesAsync(new EntryQuery(scanId, @"C:\Data", "report-2", FileCategory.Documents,
      HiddenFilter.All, EntrySort.Name, SortDirection.Ascending, 1, 20));
    Assert.Equal(10, page.TotalCount);
    Assert.Equal(10, page.Items.Count);

    var hidden = await repository.QueryEntriesAsync(new EntryQuery(scanId, @"C:\Data", null, null,
      HiddenFilter.HiddenOnly, EntrySort.Name, SortDirection.Ascending, 2, 10));
    Assert.Equal(22, hidden.TotalCount);
    Assert.Equal(10, hidden.Items.Count);
    Assert.All(hidden.Items, item => Assert.True(item.IsHidden));
  }

  [Fact]
  public async Task RetentionKeepsNewestTwentySnapshots()
  {
    using var data = TemporaryDirectory.Create();
    var repository = new SqliteScanRepository(new AppPaths(data.Path));
    await repository.InitializeAsync();
    for (var index = 0; index < 23; index++)
    {
      var time = DateTimeOffset.UtcNow.AddMinutes(index);
      var id = await repository.CreateScanAsync($@"C:\Data\{index}", $"Data {index}", time, CancellationToken.None);
      await repository.FinalizeScanAsync(id, ScanStatus.Completed, EmptyTotals(), time, CancellationToken.None);
    }

    await repository.EnforceRetentionAsync(20);
    var history = await repository.GetHistoryAsync();
    Assert.Equal(20, history.Count);
    Assert.Equal("Data 22", history[0].DisplayName);
    Assert.DoesNotContain(history, scan => scan.DisplayName == "Data 0");
  }

  [Fact]
  public async Task DiscardRemovesAPartialSnapshot()
  {
    using var data = TemporaryDirectory.Create();
    var repository = new SqliteScanRepository(new AppPaths(data.Path));
    await repository.InitializeAsync();
    var id = await repository.CreateScanAsync(@"C:\Data", "Data", DateTimeOffset.UtcNow, CancellationToken.None);
    await repository.FinalizeScanAsync(id, ScanStatus.CanceledPartial, EmptyTotals(), DateTimeOffset.UtcNow, CancellationToken.None);
    await repository.DiscardScanAsync(id);
    Assert.Null(await repository.GetScanAsync(id));
  }

  [Fact]
  public async Task HistoryQueryIsHardLimitedToTwentySnapshots()
  {
    using var data = TemporaryDirectory.Create();
    var repository = new SqliteScanRepository(new AppPaths(data.Path));
    await repository.InitializeAsync();
    for (var index = 0; index < 25; index++)
    {
      var time = DateTimeOffset.UtcNow.AddMinutes(index);
      var id = await repository.CreateScanAsync($@"C:\Data\{index}", $"Data {index}", time, CancellationToken.None);
      await repository.FinalizeScanAsync(id, ScanStatus.Completed, EmptyTotals(), time, CancellationToken.None);
    }

    var history = await repository.GetHistoryAsync();

    Assert.Equal(20, history.Count);
    Assert.Equal("Data 24", history[0].DisplayName);
  }

  [Fact]
  public async Task FolderExpansionIsBoundedAndReportsTruncation()
  {
    using var data = TemporaryDirectory.Create();
    var repository = new SqliteScanRepository(new AppPaths(data.Path));
    await repository.InitializeAsync();
    var scanId = await repository.CreateScanAsync(@"C:\Data", "Data", DateTimeOffset.UtcNow, CancellationToken.None);
    var entries = Enumerable.Range(0, 2001)
      .Select(index => new ScanEntryDraft(@"C:\Data", $@"C:\Data\folder-{index:0000}", $"folder-{index:0000}", string.Empty,
        EntryKind.Directory, FileCategory.Other, 0, DateTimeOffset.UtcNow, false))
      .ToArray();
    await repository.AppendBatchAsync(scanId, entries, [], CancellationToken.None);

    var page = await repository.GetChildFoldersAsync(scanId, @"C:\Data");

    Assert.True(page.IsTruncated);
    Assert.Equal(2000, page.Items.Count);
  }

  [Fact]
  public void WindowsTraversalGuardLeasesAStableLocalDirectory()
  {
    using var directory = TemporaryDirectory.Create();
    var child = Path.Combine(directory.Path, "child");
    Directory.CreateDirectory(child);
    var result = new WindowsDirectoryTraversalGuard().Acquire(directory.Path, child);

    Assert.True(result.IsValid, result.Error);
    Assert.NotNull(result.Lease);
    result.Lease.Dispose();
  }

  private static ScanTotals EmptyTotals() => new(0, 0, 0, 0, 0, 0,
    Enum.GetValues<FileCategory>().Select(category => new CategoryTotal(category, 0, 0)).ToArray());
}
