using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Dirvexa.Core;

namespace Dirvexa.App.Infrastructure;

public sealed class WindowsPlatformFileActions(Func<Window?> getWindow) : IPlatformFileActions
{
  public Task OpenAsync(string path)
  {
    EnsureExists(path);
    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    return Task.CompletedTask;
  }

  public Task RevealAsync(string path)
  {
    EnsureExists(path);
    var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
    startInfo.ArgumentList.Add($"/select,{path}");
    Process.Start(startInfo);
    return Task.CompletedTask;
  }

  public async Task CopyPathAsync(string path)
  {
    var clipboard = getWindow()?.Clipboard;
    if (clipboard is null) throw new InvalidOperationException("The clipboard is unavailable.");
    await clipboard.SetTextAsync(path);
  }

  private static void EnsureExists(string path)
  {
    if (!File.Exists(path) && !Directory.Exists(path))
      throw new FileNotFoundException("This item has moved or no longer exists.", path);
  }
}
