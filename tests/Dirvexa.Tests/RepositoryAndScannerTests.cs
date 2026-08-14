using Dirvexa.App.Infrastructure;
using Dirvexa.Core;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

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

  [Fact]
  public void WindowsTraversalLeaseEnumeratesTheVerifiedHandleAfterAReparseRace()
  {
    if (!OperatingSystem.IsWindows()) return;
    using var directory = TemporaryDirectory.Create();
    using var target = TemporaryDirectory.Create();
    var control = Path.Combine(directory.Path, "control");
    var child = Path.Combine(directory.Path, "child");
    Directory.CreateDirectory(control);
    Directory.CreateDirectory(child);
    File.WriteAllText(Path.Combine(target.Path, "outside.txt"), "outside");
    var reparseData = CreateMountPointReparseData(target.Path);

    using (var controlHandle = OpenDirectoryForReparse(control))
    {
      Assert.False(controlHandle.IsInvalid, $"Control directory could not be opened: {Marshal.GetLastWin32Error()}");
      Assert.True(SetReparsePoint(controlHandle, reparseData), $"Control reparse point could not be created: {Marshal.GetLastWin32Error()}");
      Assert.True(DeleteReparsePoint(controlHandle), $"Control reparse point could not be removed: {Marshal.GetLastWin32Error()}");
    }

    var result = new WindowsDirectoryTraversalGuard().Acquire(directory.Path, child);
    Assert.True(result.IsValid, result.Error);
    Assert.NotNull(result.Lease);

    using (result.Lease)
    using (var writeHandle = OpenDirectoryForReparse(child))
    {
      if (writeHandle.IsInvalid)
      {
        Assert.Equal(32, Marshal.GetLastWin32Error());
        return;
      }

      var changed = SetReparsePoint(writeHandle, reparseData);
      Assert.True(changed, $"The race setup could not convert the directory: {Marshal.GetLastWin32Error()}");
      try
      {
        Assert.Empty(result.Lease.Enumerate());
      }
      finally
      {
        Assert.True(DeleteReparsePoint(writeHandle), $"The raced reparse point could not be removed: {Marshal.GetLastWin32Error()}");
      }
    }
  }

  [Fact]
  public async Task ReparsePointIssuesRemainInBoundedBatches()
  {
    if (!OperatingSystem.IsWindows()) return;
    using var root = TemporaryDirectory.Create();
    var target = Path.Combine(root.Path, "target.txt");
    await File.WriteAllTextAsync(target, "target");
    var links = new List<string>();
    try
    {
      for (var index = 0; index < 70; index++)
      {
        var link = Path.Combine(root.Path, $"link-{index:00}.txt");
        try { File.CreateSymbolicLink(link, target); }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or NotSupportedException)
        {
          return;
        }
        links.Add(link);
      }

      var repository = new RecordingRepository();
      var service = new FileSystemScanService(repository, new WindowsDirectoryTraversalGuard());
      var outcome = await service.StartAsync(new ScanRequest(root.Path), progress: null);

      Assert.Equal(ScanStatus.Completed, outcome.Status);
      Assert.Equal(70, outcome.Totals.IssueCount);
      Assert.InRange(repository.MaximumIssueBatch, 1, 64);
    }
    finally
    {
      foreach (var link in links)
        try { File.Delete(link); } catch { }
    }
  }

  private static ScanTotals EmptyTotals() => new(0, 0, 0, 0, 0, 0,
    Enum.GetValues<FileCategory>().Select(category => new CategoryTotal(category, 0, 0)).ToArray());

  [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  private static extern SafeFileHandle CreateFileW(
    string fileName,
    uint desiredAccess,
    uint shareMode,
    nint securityAttributes,
    uint creationDisposition,
    uint flagsAndAttributes,
    nint templateFile);

  [DllImport("kernel32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool DeviceIoControl(
    SafeFileHandle device,
    uint controlCode,
    byte[] input,
    uint inputSize,
    nint output,
    uint outputSize,
    out uint bytesReturned,
    nint overlapped);

  private static SafeFileHandle OpenDirectoryForReparse(string path) => CreateFileW(
    path,
    0x40000000,
    0x00000001 | 0x00000002 | 0x00000004,
    0,
    3,
    0x02000000 | 0x00200000,
    0);

  private static bool SetReparsePoint(SafeFileHandle handle, byte[] data) =>
    DeviceIoControl(handle, 0x000900A4, data, (uint)data.Length, 0, 0, out _, 0);

  private static bool DeleteReparsePoint(SafeFileHandle handle)
  {
    var data = new byte[8];
    BitConverter.GetBytes(0xA0000003u).CopyTo(data, 0);
    return DeviceIoControl(handle, 0x000900AC, data, (uint)data.Length, 0, 0, out _, 0);
  }

  private static byte[] CreateMountPointReparseData(string targetPath)
  {
    var substituteName = System.Text.Encoding.Unicode.GetBytes(@"\??\" + targetPath);
    var printName = System.Text.Encoding.Unicode.GetBytes(targetPath);
    var data = new byte[16 + substituteName.Length + 2 + printName.Length + 2];
    BitConverter.GetBytes(0xA0000003u).CopyTo(data, 0);
    BitConverter.GetBytes((ushort)(8 + substituteName.Length + 2 + printName.Length + 2)).CopyTo(data, 4);
    BitConverter.GetBytes((ushort)0).CopyTo(data, 8);
    BitConverter.GetBytes((ushort)substituteName.Length).CopyTo(data, 10);
    BitConverter.GetBytes((ushort)(substituteName.Length + 2)).CopyTo(data, 12);
    BitConverter.GetBytes((ushort)printName.Length).CopyTo(data, 14);
    substituteName.CopyTo(data, 16);
    printName.CopyTo(data, 16 + substituteName.Length + 2);
    return data;
  }

  private sealed class RecordingRepository : IScanRepository
  {
    private readonly Guid _scanId = Guid.NewGuid();

    public int MaximumIssueBatch { get; private set; }

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<Guid> CreateScanAsync(string rootPath, string displayName, DateTimeOffset startedAt, CancellationToken cancellationToken) =>
      Task.FromResult(_scanId);

    public Task AppendBatchAsync(Guid scanId, IReadOnlyCollection<ScanEntryDraft> entries, IReadOnlyCollection<ScanIssueDraft> issues, CancellationToken cancellationToken)
    {
      MaximumIssueBatch = Math.Max(MaximumIssueBatch, issues.Count);
      return Task.CompletedTask;
    }

    public Task FinalizeScanAsync(Guid scanId, ScanStatus status, ScanTotals totals, DateTimeOffset completedAt, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task DiscardScanAsync(Guid scanId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<ScanRecord?> GetScanAsync(Guid scanId, CancellationToken cancellationToken = default) => Task.FromResult<ScanRecord?>(null);
    public Task<IReadOnlyList<ScanRecord>> GetRecentScansAsync(int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ScanRecord>>([]);
    public Task<IReadOnlyList<ScanRecord>> GetHistoryAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ScanRecord>>([]);
    public Task<PagedResult<ScanEntryRecord>> QueryEntriesAsync(EntryQuery query, CancellationToken cancellationToken = default) => Task.FromResult(new PagedResult<ScanEntryRecord>([], 0, 1, 1));
    public Task<FolderPage> GetChildFoldersAsync(Guid scanId, string parentPath, CancellationToken cancellationToken = default) => Task.FromResult(new FolderPage([], false));
    public Task<IReadOnlyList<ScanIssueRecord>> GetIssuesAsync(Guid scanId, int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ScanIssueRecord>>([]);
    public Task DeleteScanAsync(Guid scanId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ClearHistoryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task EnforceRetentionAsync(int keepCount, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public long GetStorageSizeBytes() => 0;
  }
}
