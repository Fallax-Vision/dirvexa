using System.Globalization;
using Dirvexa.Core;
using Microsoft.Data.Sqlite;

namespace Dirvexa.App.Infrastructure;

public sealed class SqliteScanRepository(AppPaths paths) : IScanRepository
{
  private const int HistoryLimit = 20;
  private const int FolderChildLimit = 2000;
  private readonly SemaphoreSlim _writeGate = new(1, 1);
  private readonly string _connectionString = new SqliteConnectionStringBuilder
  {
    DataSource = paths.Database,
    Mode = SqliteOpenMode.ReadWriteCreate,
    Cache = SqliteCacheMode.Private,
    DefaultTimeout = 5
  }.ToString();

  public async Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    await using var connection = await OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = """
      PRAGMA journal_mode = WAL;
      PRAGMA foreign_keys = ON;
      PRAGMA busy_timeout = 5000;
      PRAGMA synchronous = NORMAL;
      PRAGMA temp_store = MEMORY;
      PRAGMA cache_size = -16384;

      CREATE TABLE IF NOT EXISTS schema_migrations (
        version INTEGER PRIMARY KEY,
        applied_utc TEXT NOT NULL
      );

      CREATE TABLE IF NOT EXISTS scans (
        id TEXT PRIMARY KEY,
        root_path TEXT NOT NULL,
        display_name TEXT NOT NULL,
        started_utc TEXT NOT NULL,
        completed_utc TEXT NULL,
        status INTEGER NOT NULL,
        total_bytes INTEGER NOT NULL DEFAULT 0,
        file_count INTEGER NOT NULL DEFAULT 0,
        folder_count INTEGER NOT NULL DEFAULT 0,
        hidden_file_count INTEGER NOT NULL DEFAULT 0,
        hidden_folder_count INTEGER NOT NULL DEFAULT 0,
        issue_count INTEGER NOT NULL DEFAULT 0
      );

      CREATE TABLE IF NOT EXISTS scan_entries (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        scan_id TEXT NOT NULL REFERENCES scans(id) ON DELETE CASCADE,
        parent_path TEXT NOT NULL COLLATE NOCASE,
        full_path TEXT NOT NULL COLLATE NOCASE,
        name TEXT NOT NULL COLLATE NOCASE,
        extension TEXT NOT NULL COLLATE NOCASE,
        kind INTEGER NOT NULL,
        category INTEGER NOT NULL,
        size_bytes INTEGER NOT NULL,
        modified_utc TEXT NOT NULL,
        is_hidden INTEGER NOT NULL
      );

      CREATE TABLE IF NOT EXISTS scan_category_totals (
        scan_id TEXT NOT NULL REFERENCES scans(id) ON DELETE CASCADE,
        category INTEGER NOT NULL,
        file_count INTEGER NOT NULL,
        total_bytes INTEGER NOT NULL,
        PRIMARY KEY (scan_id, category)
      );

      CREATE TABLE IF NOT EXISTS scan_issues (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        scan_id TEXT NOT NULL REFERENCES scans(id) ON DELETE CASCADE,
        path TEXT NOT NULL,
        message TEXT NOT NULL
      );

      CREATE INDEX IF NOT EXISTS idx_scans_completed ON scans(completed_utc DESC);
      CREATE INDEX IF NOT EXISTS idx_entries_scan_parent ON scan_entries(scan_id, parent_path, kind, name);
      CREATE INDEX IF NOT EXISTS idx_entries_scan_category ON scan_entries(scan_id, category, size_bytes DESC);
      CREATE INDEX IF NOT EXISTS idx_entries_scan_name ON scan_entries(scan_id, name);
      CREATE INDEX IF NOT EXISTS idx_entries_scan_path ON scan_entries(scan_id, full_path);
      CREATE INDEX IF NOT EXISTS idx_entries_scan_size ON scan_entries(scan_id, size_bytes DESC);

      INSERT OR IGNORE INTO schema_migrations(version, applied_utc) VALUES (1, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
      """;
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  public async Task<Guid> CreateScanAsync(string rootPath, string displayName, DateTimeOffset startedAt, CancellationToken cancellationToken)
  {
    return await ExecuteWriteAsync(async token =>
    {
      var id = Guid.NewGuid();
      await using var connection = await OpenAsync(token);
      await using var command = connection.CreateCommand();
      command.CommandText = "INSERT INTO scans(id, root_path, display_name, started_utc, status) VALUES(@id, @root, @name, @started, @status);";
      command.Parameters.AddWithValue("@id", id.ToString());
      command.Parameters.AddWithValue("@root", rootPath);
      command.Parameters.AddWithValue("@name", displayName);
      command.Parameters.AddWithValue("@started", ToDatabase(startedAt));
      command.Parameters.AddWithValue("@status", (int)ScanStatus.Running);
      await command.ExecuteNonQueryAsync(token);
      return id;
    }, cancellationToken);
  }

  public async Task AppendBatchAsync(
    Guid scanId,
    IReadOnlyCollection<ScanEntryDraft> entries,
    IReadOnlyCollection<ScanIssueDraft> issues,
    CancellationToken cancellationToken)
  {
    await ExecuteWriteAsync(async token =>
    {
      await using var connection = await OpenAsync(token);
      await using var transaction = await connection.BeginTransactionAsync(token);
      var scanIdText = scanId.ToString();

      await using var entryCommand = connection.CreateCommand();
      entryCommand.Transaction = (SqliteTransaction)transaction;
      entryCommand.CommandText = """
      INSERT INTO scan_entries(scan_id, parent_path, full_path, name, extension, kind, category, size_bytes, modified_utc, is_hidden)
      VALUES(@scan, @parent, @path, @name, @extension, @kind, @category, @size, @modified, @hidden);
      """;
      var scanParameter = entryCommand.Parameters.Add("@scan", SqliteType.Text);
      var parentParameter = entryCommand.Parameters.Add("@parent", SqliteType.Text);
      var pathParameter = entryCommand.Parameters.Add("@path", SqliteType.Text);
      var nameParameter = entryCommand.Parameters.Add("@name", SqliteType.Text);
      var extensionParameter = entryCommand.Parameters.Add("@extension", SqliteType.Text);
      var kindParameter = entryCommand.Parameters.Add("@kind", SqliteType.Integer);
      var categoryParameter = entryCommand.Parameters.Add("@category", SqliteType.Integer);
      var sizeParameter = entryCommand.Parameters.Add("@size", SqliteType.Integer);
      var modifiedParameter = entryCommand.Parameters.Add("@modified", SqliteType.Text);
      var hiddenParameter = entryCommand.Parameters.Add("@hidden", SqliteType.Integer);
      entryCommand.Prepare();

      foreach (var entry in entries)
      {
        scanParameter.Value = scanIdText;
        parentParameter.Value = entry.ParentPath;
        pathParameter.Value = entry.FullPath;
        nameParameter.Value = entry.Name;
        extensionParameter.Value = entry.Extension;
        kindParameter.Value = (int)entry.Kind;
        categoryParameter.Value = (int)entry.Category;
        sizeParameter.Value = entry.SizeBytes;
        modifiedParameter.Value = ToDatabase(entry.ModifiedAt);
        hiddenParameter.Value = entry.IsHidden ? 1 : 0;
        await entryCommand.ExecuteNonQueryAsync(token);
      }

      await using var issueCommand = connection.CreateCommand();
      issueCommand.Transaction = (SqliteTransaction)transaction;
      issueCommand.CommandText = "INSERT INTO scan_issues(scan_id, path, message) VALUES(@scan, @path, @message);";
      var issueScanParameter = issueCommand.Parameters.Add("@scan", SqliteType.Text);
      var issuePathParameter = issueCommand.Parameters.Add("@path", SqliteType.Text);
      var issueMessageParameter = issueCommand.Parameters.Add("@message", SqliteType.Text);
      issueCommand.Prepare();
      foreach (var issue in issues)
      {
        issueScanParameter.Value = scanIdText;
        issuePathParameter.Value = issue.Path;
        issueMessageParameter.Value = issue.Message;
        await issueCommand.ExecuteNonQueryAsync(token);
      }

      await transaction.CommitAsync(token);
    }, cancellationToken);
  }

  public async Task FinalizeScanAsync(Guid scanId, ScanStatus status, ScanTotals totals, DateTimeOffset completedAt, CancellationToken cancellationToken)
  {
    await ExecuteWriteAsync(async token =>
    {
      await using var connection = await OpenAsync(token);
      await using var transaction = await connection.BeginTransactionAsync(token);
      var scanIdText = scanId.ToString();
      await using var command = connection.CreateCommand();
      command.Transaction = (SqliteTransaction)transaction;
      command.CommandText = """
      UPDATE scans SET completed_utc = @completed, status = @status, total_bytes = @bytes,
        file_count = @files, folder_count = @folders, hidden_file_count = @hiddenFiles,
        hidden_folder_count = @hiddenFolders, issue_count = @issues
      WHERE id = @id;
      """;
      command.Parameters.AddWithValue("@completed", ToDatabase(completedAt));
      command.Parameters.AddWithValue("@status", (int)status);
      command.Parameters.AddWithValue("@bytes", totals.TotalBytes);
      command.Parameters.AddWithValue("@files", totals.FileCount);
      command.Parameters.AddWithValue("@folders", totals.FolderCount);
      command.Parameters.AddWithValue("@hiddenFiles", totals.HiddenFileCount);
      command.Parameters.AddWithValue("@hiddenFolders", totals.HiddenFolderCount);
      command.Parameters.AddWithValue("@issues", totals.IssueCount);
      command.Parameters.AddWithValue("@id", scanIdText);
      await command.ExecuteNonQueryAsync(token);

      await using var categoryCommand = connection.CreateCommand();
      categoryCommand.Transaction = (SqliteTransaction)transaction;
      categoryCommand.CommandText = """
      INSERT INTO scan_category_totals(scan_id, category, file_count, total_bytes)
      VALUES(@scan, @category, @count, @bytes)
      ON CONFLICT(scan_id, category) DO UPDATE SET file_count = excluded.file_count, total_bytes = excluded.total_bytes;
      """;
      var categoryScanParameter = categoryCommand.Parameters.Add("@scan", SqliteType.Text);
      var categoryParameter = categoryCommand.Parameters.Add("@category", SqliteType.Integer);
      var countParameter = categoryCommand.Parameters.Add("@count", SqliteType.Integer);
      var bytesParameter = categoryCommand.Parameters.Add("@bytes", SqliteType.Integer);
      categoryCommand.Prepare();
      categoryScanParameter.Value = scanIdText;

      foreach (var category in totals.Categories)
      {
        categoryParameter.Value = (int)category.Category;
        countParameter.Value = category.FileCount;
        bytesParameter.Value = category.TotalBytes;
        await categoryCommand.ExecuteNonQueryAsync(token);
      }

      await transaction.CommitAsync(token);
    }, cancellationToken);
  }

  public Task DiscardScanAsync(Guid scanId, CancellationToken cancellationToken = default) =>
    DeleteScanAsync(scanId, cancellationToken);

  public async Task<ScanRecord?> GetScanAsync(Guid scanId, CancellationToken cancellationToken = default)
  {
    var records = await ReadScansAsync("WHERE id = @id", command => command.Parameters.AddWithValue("@id", scanId.ToString()), cancellationToken);
    return records.FirstOrDefault();
  }

  public async Task<IReadOnlyList<ScanRecord>> GetRecentScansAsync(int limit, CancellationToken cancellationToken = default) =>
    await ReadScansAsync("WHERE status <> @running ORDER BY completed_utc DESC LIMIT @limit", command =>
    {
      command.Parameters.AddWithValue("@running", (int)ScanStatus.Running);
      command.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 100));
    }, cancellationToken);

  public async Task<IReadOnlyList<ScanRecord>> GetHistoryAsync(CancellationToken cancellationToken = default) =>
    await ReadScansAsync("WHERE status <> @running ORDER BY completed_utc DESC LIMIT @limit", command =>
    {
      command.Parameters.AddWithValue("@running", (int)ScanStatus.Running);
      command.Parameters.AddWithValue("@limit", HistoryLimit);
    }, cancellationToken);

  public async Task<PagedResult<ScanEntryRecord>> QueryEntriesAsync(EntryQuery query, CancellationToken cancellationToken = default)
  {
    var pageSize = Math.Clamp(query.PageSize, 1, 500);
    var page = Math.Max(1, query.Page);
    var where = new List<string> { "scan_id = @scan" };
    var hasSearch = !string.IsNullOrWhiteSpace(query.SearchText);
    if (hasSearch)
      where.Add("(name LIKE @search ESCAPE '\\' COLLATE NOCASE OR full_path LIKE @search ESCAPE '\\' COLLATE NOCASE)");
    else if (!string.IsNullOrWhiteSpace(query.FolderPath))
      where.Add("parent_path = @folder COLLATE NOCASE");
    if (query.Category is not null)
      where.Add("kind = @fileKind AND category = @category");
    if (query.Hidden == HiddenFilter.HiddenOnly) where.Add("is_hidden = 1");
    if (query.Hidden == HiddenFilter.VisibleOnly) where.Add("is_hidden = 0");
    var whereSql = string.Join(" AND ", where);

    await using var connection = await OpenAsync(cancellationToken);
    await using var countCommand = connection.CreateCommand();
    countCommand.CommandText = $"SELECT COUNT(*) FROM scan_entries WHERE {whereSql};";
    AddEntryQueryParameters(countCommand, query);
    var total = Convert.ToInt64(await countCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);

    var orderColumn = query.Sort switch
    {
      EntrySort.Name => "name",
      EntrySort.Modified => "modified_utc",
      EntrySort.Path => "full_path",
      EntrySort.Category => "category",
      _ => "size_bytes"
    };
    var direction = query.Direction == SortDirection.Ascending ? "ASC" : "DESC";
    await using var command = connection.CreateCommand();
    command.CommandText = $"""
      SELECT id, scan_id, parent_path, full_path, name, extension, kind, category, size_bytes, modified_utc, is_hidden
      FROM scan_entries WHERE {whereSql}
      ORDER BY {orderColumn} {direction}, name COLLATE NOCASE ASC
      LIMIT @limit OFFSET @offset;
      """;
    AddEntryQueryParameters(command, query);
    command.Parameters.AddWithValue("@limit", pageSize);
    command.Parameters.AddWithValue("@offset", (page - 1) * pageSize);
    var items = new List<ScanEntryRecord>();
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
      items.Add(new ScanEntryRecord(
        reader.GetInt64(0),
        Guid.Parse(reader.GetString(1)),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        (EntryKind)reader.GetInt32(6),
        (FileCategory)reader.GetInt32(7),
        reader.GetInt64(8),
        FromDatabase(reader.GetString(9)),
        reader.GetInt32(10) == 1));
    }

    return new PagedResult<ScanEntryRecord>(items, total, page, pageSize);
  }

  public async Task<FolderPage> GetChildFoldersAsync(Guid scanId, string parentPath, CancellationToken cancellationToken = default)
  {
    await using var connection = await OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = """
      SELECT e.full_path, e.name,
        EXISTS(SELECT 1 FROM scan_entries c WHERE c.scan_id = e.scan_id AND c.kind = @directory AND c.parent_path = e.full_path LIMIT 1)
      FROM scan_entries e
      WHERE e.scan_id = @scan AND e.kind = @directory AND e.parent_path = @parent COLLATE NOCASE
      ORDER BY e.name COLLATE NOCASE
      LIMIT @limit;
      """;
    command.Parameters.AddWithValue("@scan", scanId.ToString());
    command.Parameters.AddWithValue("@directory", (int)EntryKind.Directory);
    command.Parameters.AddWithValue("@parent", parentPath);
    command.Parameters.AddWithValue("@limit", FolderChildLimit + 1);
    var folders = new List<FolderRecord>(FolderChildLimit + 1);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
      folders.Add(new FolderRecord(reader.GetString(0), reader.GetString(1), reader.GetInt32(2) == 1));
    var isTruncated = folders.Count > FolderChildLimit;
    if (isTruncated) folders.RemoveAt(folders.Count - 1);
    return new FolderPage(folders, isTruncated);
  }

  public async Task<IReadOnlyList<ScanIssueRecord>> GetIssuesAsync(Guid scanId, int limit, CancellationToken cancellationToken = default)
  {
    await using var connection = await OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT id, scan_id, path, message FROM scan_issues WHERE scan_id = @scan ORDER BY id LIMIT @limit;";
    command.Parameters.AddWithValue("@scan", scanId.ToString());
    command.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 1000));
    var issues = new List<ScanIssueRecord>();
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
      issues.Add(new ScanIssueRecord(reader.GetInt64(0), Guid.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3)));
    return issues;
  }

  public async Task DeleteScanAsync(Guid scanId, CancellationToken cancellationToken = default)
  {
    await ExecuteWriteAsync(async token =>
    {
      await using var connection = await OpenAsync(token);
      await using var command = connection.CreateCommand();
      command.CommandText = "DELETE FROM scans WHERE id = @id;";
      command.Parameters.AddWithValue("@id", scanId.ToString());
      await command.ExecuteNonQueryAsync(token);
    }, cancellationToken);
  }

  public async Task ClearHistoryAsync(CancellationToken cancellationToken = default)
  {
    await ExecuteWriteAsync(async token =>
    {
      var hasRunningScan = false;
      await using (var connection = await OpenAsync(token))
      {
        await using (var runningCommand = connection.CreateCommand())
        {
          runningCommand.CommandText = "SELECT EXISTS(SELECT 1 FROM scans WHERE status = @running LIMIT 1);";
          runningCommand.Parameters.AddWithValue("@running", (int)ScanStatus.Running);
          hasRunningScan = Convert.ToInt32(await runningCommand.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) == 1;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM scans WHERE status <> @running;";
        command.Parameters.AddWithValue("@running", (int)ScanStatus.Running);
        await command.ExecuteNonQueryAsync(token);
      }

      if (!hasRunningScan)
      {
        await using var vacuumConnection = await OpenAsync(token);
        await using var vacuum = vacuumConnection.CreateCommand();
        vacuum.CommandText = "PRAGMA wal_checkpoint(TRUNCATE); VACUUM;";
        await vacuum.ExecuteNonQueryAsync(token);
      }
    }, cancellationToken);
  }

  public async Task EnforceRetentionAsync(int keepCount, CancellationToken cancellationToken = default)
  {
    await ExecuteWriteAsync(async token =>
    {
      await using var connection = await OpenAsync(token);
      await using var command = connection.CreateCommand();
      command.CommandText = """
        DELETE FROM scans
        WHERE id IN (
          SELECT id FROM scans WHERE status <> @running
          ORDER BY completed_utc DESC LIMIT -1 OFFSET @keep
        );
        """;
      command.Parameters.AddWithValue("@running", (int)ScanStatus.Running);
      command.Parameters.AddWithValue("@keep", Math.Max(1, keepCount));
      await command.ExecuteNonQueryAsync(token);
    }, cancellationToken);
  }

  public long GetStorageSizeBytes()
  {
    try
    {
      return new[] { paths.Database, paths.Database + "-wal", paths.Database + "-shm" }
        .Where(File.Exists)
        .Sum(file => new FileInfo(file).Length);
    }
    catch
    {
      return 0;
    }
  }

  private async Task<IReadOnlyList<ScanRecord>> ReadScansAsync(
    string clause,
    Action<SqliteCommand> configure,
    CancellationToken cancellationToken)
  {
    var headers = new List<(Guid Id, string Root, string Name, DateTimeOffset Started, DateTimeOffset? Completed, ScanStatus Status, long Bytes, long Files, long Folders, long HiddenFiles, long HiddenFolders, long Issues)>();
    await using var connection = await OpenAsync(cancellationToken);
    await using (var command = connection.CreateCommand())
    {
      command.CommandText = $"""
        SELECT id, root_path, display_name, started_utc, completed_utc, status, total_bytes,
          file_count, folder_count, hidden_file_count, hidden_folder_count, issue_count
        FROM scans {clause};
        """;
      configure(command);
      await using var reader = await command.ExecuteReaderAsync(cancellationToken);
      while (await reader.ReadAsync(cancellationToken))
      {
        headers.Add((
          Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), FromDatabase(reader.GetString(3)),
          reader.IsDBNull(4) ? null : FromDatabase(reader.GetString(4)), (ScanStatus)reader.GetInt32(5), reader.GetInt64(6),
          reader.GetInt64(7), reader.GetInt64(8), reader.GetInt64(9), reader.GetInt64(10), reader.GetInt64(11)));
      }
    }

    var categoryMap = headers.ToDictionary(
      header => header.Id,
      _ => Enum.GetValues<FileCategory>().ToDictionary(category => category, category => new CategoryTotal(category, 0, 0)));
    if (headers.Count > 0)
    {
      await using var categoryCommand = connection.CreateCommand();
      var ids = new string[headers.Count];
      for (var index = 0; index < headers.Count; index++)
      {
        ids[index] = $"@scan{index}";
        categoryCommand.Parameters.AddWithValue(ids[index], headers[index].Id.ToString());
      }
      categoryCommand.CommandText = $"SELECT scan_id, category, file_count, total_bytes FROM scan_category_totals WHERE scan_id IN ({string.Join(",", ids)});";
      await using var categoryReader = await categoryCommand.ExecuteReaderAsync(cancellationToken);
      while (await categoryReader.ReadAsync(cancellationToken))
      {
        var scanId = Guid.Parse(categoryReader.GetString(0));
        var category = (FileCategory)categoryReader.GetInt32(1);
        if (categoryMap.TryGetValue(scanId, out var totals) && totals.ContainsKey(category))
          totals[category] = new CategoryTotal(category, categoryReader.GetInt64(2), categoryReader.GetInt64(3));
      }
    }

    var result = new List<ScanRecord>(headers.Count);
    foreach (var header in headers)
    {
      var categories = categoryMap[header.Id].Values.OrderBy(value => value.Category).ToArray();
      result.Add(new ScanRecord(header.Id, header.Root, header.Name, header.Started, header.Completed, header.Status,
        new ScanTotals(header.Bytes, header.Files, header.Folders, header.HiddenFiles, header.HiddenFolders, header.Issues, categories)));
    }
    return result;
  }

  private static void AddEntryQueryParameters(SqliteCommand command, EntryQuery query)
  {
    command.Parameters.AddWithValue("@scan", query.ScanId.ToString());
    if (!string.IsNullOrWhiteSpace(query.SearchText))
      command.Parameters.AddWithValue("@search", $"%{EscapeLike(query.SearchText.Trim())}%");
    else if (!string.IsNullOrWhiteSpace(query.FolderPath))
      command.Parameters.AddWithValue("@folder", query.FolderPath);
    if (query.Category is not null)
    {
      command.Parameters.AddWithValue("@fileKind", (int)EntryKind.File);
      command.Parameters.AddWithValue("@category", (int)query.Category.Value);
    }
  }

  private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
  {
    var connection = new SqliteConnection(_connectionString);
    await connection.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000; PRAGMA synchronous = NORMAL; PRAGMA temp_store = MEMORY; PRAGMA cache_size = -16384;";
    await command.ExecuteNonQueryAsync(cancellationToken);
    return connection;
  }

  private async Task ExecuteWriteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
  {
    await _writeGate.WaitAsync(cancellationToken);
    try
    {
      await RetryBusyAsync(operation, cancellationToken);
    }
    finally
    {
      _writeGate.Release();
    }
  }

  private async Task<T> ExecuteWriteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
  {
    await _writeGate.WaitAsync(cancellationToken);
    try
    {
      return await RetryBusyAsync(operation, cancellationToken);
    }
    finally
    {
      _writeGate.Release();
    }
  }

  private static async Task RetryBusyAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
  {
    for (var attempt = 1; ; attempt++)
    {
      try
      {
        await operation(cancellationToken);
        return;
      }
      catch (SqliteException exception) when (IsBusy(exception) && attempt < 4)
      {
        await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken);
      }
    }
  }

  private static async Task<T> RetryBusyAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
  {
    for (var attempt = 1; ; attempt++)
    {
      try
      {
        return await operation(cancellationToken);
      }
      catch (SqliteException exception) when (IsBusy(exception) && attempt < 4)
      {
        await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken);
      }
    }
  }

  private static bool IsBusy(SqliteException exception) => exception.SqliteErrorCode is 5 or 6;

  private static string EscapeLike(string value) => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
  private static string ToDatabase(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
  private static DateTimeOffset FromDatabase(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
}
