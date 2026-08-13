using Dirvexa.App.Infrastructure;
using Dirvexa.Core;

namespace Dirvexa.Tests;

public sealed class SettingsTests
{
  [Theory]
  [InlineData(ThemePreference.System)]
  [InlineData(ThemePreference.Light)]
  [InlineData(ThemePreference.Dark)]
  public async Task ThemePreferenceRoundTrips(ThemePreference preference)
  {
    using var directory = TemporaryDirectory.Create();
    var service = new AppSettingsService(new AppPaths(directory.Path));
    await service.SaveAsync(new AppSettings(preference));
    Assert.Equal(preference, service.Load().Theme);
    Assert.False(File.Exists(Path.Combine(directory.Path, "settings.json.tmp")));
  }

  [Fact]
  public async Task ConcurrentThemeSavesPersistTheNewestRequestWithoutTemporaryFiles()
  {
    using var directory = TemporaryDirectory.Create();
    var service = new AppSettingsService(new AppPaths(directory.Path));

    var saves = new[]
    {
      service.SaveAsync(new AppSettings(ThemePreference.Light)),
      service.SaveAsync(new AppSettings(ThemePreference.System)),
      service.SaveAsync(new AppSettings(ThemePreference.Dark))
    };
    await Task.WhenAll(saves);

    Assert.Equal(ThemePreference.Dark, service.Load().Theme);
    Assert.Empty(Directory.EnumerateFiles(directory.Path, "settings.json.*.tmp"));
  }

  [Fact]
  public async Task InvalidThemeValueFallsBackToSystem()
  {
    using var directory = TemporaryDirectory.Create();
    var paths = new AppPaths(directory.Path);
    await File.WriteAllTextAsync(paths.Settings, "{\"Theme\":999}");

    Assert.Equal(ThemePreference.System, new AppSettingsService(paths).Load().Theme);
  }
}
