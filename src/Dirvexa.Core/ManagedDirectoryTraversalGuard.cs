namespace Dirvexa.Core;

public sealed class ManagedDirectoryTraversalGuard : IDirectoryTraversalGuard
{
  public TraversalLeaseResult Acquire(string rootPath, string directoryPath)
  {
    var validation = PathValidator.ValidateTraversalDirectory(rootPath, directoryPath);
    return validation.IsValid && validation.NormalizedPath is not null
      ? TraversalLeaseResult.Success(new ManagedLease(validation.NormalizedPath))
      : TraversalLeaseResult.Failure(validation.Error ?? "Skipped an unsafe directory.");
  }

  private sealed class ManagedLease(string directoryPath) : IDirectoryTraversalLease
  {
    public IEnumerable<DirectoryEntrySnapshot> Enumerate()
    {
      var options = new EnumerationOptions
      {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        ReturnSpecialDirectories = false,
        AttributesToSkip = 0
      };
      foreach (var item in new DirectoryInfo(directoryPath).EnumerateFileSystemInfos("*", options))
      {
        var attributes = item.Attributes;
        var length = (attributes & FileAttributes.Directory) == 0
          ? (item as FileInfo ?? new FileInfo(item.FullName)).Length
          : 0;
        yield return new DirectoryEntrySnapshot(item.FullName, item.Name, attributes, length, item.LastWriteTimeUtc);
      }
    }

    public void Dispose() { }
  }
}
