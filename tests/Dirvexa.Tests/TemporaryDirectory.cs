namespace Dirvexa.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
  private TemporaryDirectory(string path)
  {
    Path = path;
    Directory.CreateDirectory(path);
  }

  public string Path { get; }

  public static TemporaryDirectory Create() =>
    new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Dirvexa.Tests", Guid.NewGuid().ToString("N")));

  public void Dispose()
  {
    try
    {
      foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
        File.SetAttributes(file, FileAttributes.Normal);
      Directory.Delete(Path, recursive: true);
    }
    catch
    {
      // Test cleanup should not hide the test result.
    }
  }
}
