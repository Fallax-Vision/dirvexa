namespace Dirvexa.Core;

public sealed record PathValidationResult(bool IsValid, string? NormalizedPath, string? Error)
{
  public static PathValidationResult Success(string path) => new(true, path, null);
  public static PathValidationResult Failure(string error) => new(false, null, error);
}

public static class PathValidator
{
  public static PathValidationResult ValidateLocalDirectory(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
      return PathValidationResult.Failure("Enter or choose a folder to scan.");

    var input = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
    if (HasNetworkOrDevicePrefix(input))
      return PathValidationResult.Failure("Network and UNC paths are not supported in this version.");

    try
    {
      if (!Path.IsPathFullyQualified(input))
        return PathValidationResult.Failure("Use an absolute local folder path.");

      var fullPath = Path.GetFullPath(input);
      if (HasNetworkOrDevicePrefix(fullPath))
        return PathValidationResult.Failure("Network and UNC paths are not supported in this version.");

      var root = Path.GetPathRoot(fullPath);
      if (string.IsNullOrWhiteSpace(root))
        return PathValidationResult.Failure("The folder is not on a supported local drive.");

      var drive = new DriveInfo(root);
      if (drive.DriveType is DriveType.Network or DriveType.Unknown or DriveType.NoRootDirectory)
        return PathValidationResult.Failure("Only available local drives are supported in this version.");

      var attributes = ValidatePathComponents(root, fullPath);
      if ((attributes & FileAttributes.Directory) == 0)
        return PathValidationResult.Failure("The selected path is a file. Choose a folder instead.");

      return PathValidationResult.Success(IsDriveRoot(fullPath) ? root : Path.TrimEndingDirectorySeparator(fullPath));
    }
    catch (Exception exception) when (exception is DirectoryNotFoundException or FileNotFoundException)
    {
      return PathValidationResult.Failure("That folder is unavailable or does not exist.");
    }
    catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or UnauthorizedAccessException or IOException)
    {
      return PathValidationResult.Failure($"The folder cannot be opened: {exception.Message}");
    }
  }

  public static PathValidationResult ValidateTraversalDirectory(string rootPath, string directoryPath)
  {
    try
    {
      var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
      var normalizedDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath));
      var relative = Path.GetRelativePath(normalizedRoot, normalizedDirectory);
      if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        return PathValidationResult.Failure("Skipped a directory outside the selected root.");

      var volumeRoot = Path.GetPathRoot(normalizedRoot);
      if (string.IsNullOrWhiteSpace(volumeRoot))
        return PathValidationResult.Failure("Skipped a directory without a local drive root.");
      var attributes = ValidatePathComponents(volumeRoot, normalizedDirectory);
      if ((attributes & FileAttributes.Directory) == 0)
        return PathValidationResult.Failure("Skipped an item that is no longer a directory.");
      return PathValidationResult.Success(normalizedDirectory);
    }
    catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or UnauthorizedAccessException or IOException)
    {
      return PathValidationResult.Failure($"Skipped an unavailable directory: {exception.Message}");
    }
  }

  private static FileAttributes ValidatePathComponents(string rootPath, string fullPath)
  {
    var current = Path.TrimEndingDirectorySeparator(rootPath);
    if (current.Length == 2 && current[1] == Path.VolumeSeparatorChar)
      current += Path.DirectorySeparatorChar;

    var attributes = File.GetAttributes(current);
    if ((attributes & FileAttributes.ReparsePoint) != 0)
      throw new IOException("A reparse point cannot be used in the scan path.");

    var relative = Path.GetRelativePath(current, fullPath);
    if (relative == ".") return attributes;

    foreach (var component in relative.Split(
               [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
               StringSplitOptions.RemoveEmptyEntries))
    {
      current = Path.Combine(current, component);
      attributes = File.GetAttributes(current);
      if ((attributes & FileAttributes.ReparsePoint) != 0)
        throw new IOException("A reparse point cannot be used in the scan path.");
    }

    return attributes;
  }

  private static bool HasNetworkOrDevicePrefix(string path) =>
    path.StartsWith(@"\\", StringComparison.Ordinal)
    || path.StartsWith("//", StringComparison.Ordinal);

  private static bool IsDriveRoot(string path) =>
    string.Equals(Path.GetPathRoot(path), Path.TrimEndingDirectorySeparator(path) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
    || Directory.GetParent(path) is null;
}
