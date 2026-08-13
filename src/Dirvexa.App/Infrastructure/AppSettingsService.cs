using System.Text.Json;
using Dirvexa.Core;

namespace Dirvexa.App.Infrastructure;

public sealed record AppSettings(ThemePreference Theme = ThemePreference.System);

public sealed class AppSettingsService(AppPaths paths)
{
  private const long MaxSettingsBytes = 64 * 1024;
  private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
  private readonly SemaphoreSlim _saveGate = new(1, 1);
  private long _requestedGeneration;

  public AppSettings Load()
  {
    try
    {
      var file = new FileInfo(paths.Settings);
      if (!file.Exists || file.Length > MaxSettingsBytes) return new AppSettings();
      var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(paths.Settings), JsonOptions) ?? new AppSettings();
      return Enum.IsDefined(settings.Theme) ? settings : new AppSettings();
    }
    catch
    {
      return new AppSettings();
    }
  }

  public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
  {
    var generation = Interlocked.Increment(ref _requestedGeneration);
    await _saveGate.WaitAsync(cancellationToken);
    string? temporaryPath = null;
    try
    {
      if (generation != Volatile.Read(ref _requestedGeneration)) return;
      temporaryPath = $"{paths.Settings}.{Guid.NewGuid():N}.tmp";
      await using (var stream = new FileStream(
                     temporaryPath,
                     FileMode.CreateNew,
                     FileAccess.Write,
                     FileShare.None,
                     4096,
                     FileOptions.Asynchronous | FileOptions.WriteThrough))
      {
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
      }
      File.Move(temporaryPath, paths.Settings, overwrite: true);
      temporaryPath = null;
    }
    finally
    {
      if (temporaryPath is not null)
      {
        try { File.Delete(temporaryPath); } catch { }
      }
      _saveGate.Release();
    }
  }
}
