namespace Dirvexa.App.Infrastructure;

public sealed class AppPaths
{
  public AppPaths(string? root = null)
  {
    Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dirvexa");
    Data = Path.Combine(Root, "data");
    Logs = Path.Combine(Root, "logs");
    Database = Path.Combine(Data, "dirvexa.db");
    Settings = Path.Combine(Root, "settings.json");

    Directory.CreateDirectory(Root);
    Directory.CreateDirectory(Data);
    Directory.CreateDirectory(Logs);
  }

  public string Root { get; }
  public string Data { get; }
  public string Logs { get; }
  public string Database { get; }
  public string Settings { get; }
}
