using Avalonia;

namespace Dirvexa.App;

internal static class Program
{
  private static Mutex? _singleInstance;

  [STAThread]
  public static void Main(string[] args)
  {
    _singleInstance = new Mutex(initiallyOwned: true, @"Local\Dirvexa.Desktop.SingleInstance", out var createdNew);
    if (!createdNew) return;

    try
    {
      BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    finally
    {
      _singleInstance.ReleaseMutex();
      _singleInstance.Dispose();
    }
  }

  public static AppBuilder BuildAvaloniaApp() =>
    AppBuilder.Configure<App>()
      .UsePlatformDetect()
      .With(new Win32PlatformOptions
      {
        RenderingMode =
        [
          Win32RenderingMode.AngleEgl,
          Win32RenderingMode.Software
        ],
        CompositionMode =
        [
          Win32CompositionMode.WinUIComposition,
          Win32CompositionMode.DirectComposition,
          Win32CompositionMode.RedirectionSurface
        ],
        ShouldRenderOnUIThread = false
      })
      .WithInterFont()
      .LogToTrace();
}
