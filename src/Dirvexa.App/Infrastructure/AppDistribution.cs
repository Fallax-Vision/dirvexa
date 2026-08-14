namespace Dirvexa.App.Infrastructure;

public enum AppDistribution
{
  Portable,
  Installed
}

public static class AppDistributionDetector
{
  public static AppDistribution Detect(string executableDirectory, string localAppData)
  {
    var executablePath = EnsureTrailingSeparator(Path.GetFullPath(executableDirectory));
    var installedPath = EnsureTrailingSeparator(Path.GetFullPath(Path.Combine(localAppData, "Programs", "Dirvexa")));
    return executablePath.StartsWith(installedPath, StringComparison.OrdinalIgnoreCase)
      ? AppDistribution.Installed
      : AppDistribution.Portable;
  }

  private static string EnsureTrailingSeparator(string path) =>
    path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
}
