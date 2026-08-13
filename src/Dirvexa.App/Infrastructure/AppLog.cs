namespace Dirvexa.App.Infrastructure;

public sealed class AppLog(AppPaths paths)
{
  private readonly SemaphoreSlim _gate = new(1, 1);

  public async Task WriteAsync(string message, Exception? exception = null)
  {
    try
    {
      await _gate.WaitAsync();
      var file = Path.Combine(paths.Logs, $"dirvexa-{DateTime.UtcNow:yyyyMMdd}.log");
      var line = $"{DateTimeOffset.UtcNow:O} {message}";
      if (exception is not null) line += $"{Environment.NewLine}{exception}";
      await File.AppendAllTextAsync(file, line + Environment.NewLine);
      TrimOldLogs();
    }
    catch
    {
      // Diagnostics must never interrupt the application.
    }
    finally
    {
      if (_gate.CurrentCount == 0) _gate.Release();
    }
  }

  private void TrimOldLogs()
  {
    foreach (var file in Directory.EnumerateFiles(paths.Logs, "dirvexa-*.log")
               .OrderByDescending(File.GetLastWriteTimeUtc)
               .Skip(7))
    {
      try { File.Delete(file); } catch { }
    }
  }
}
