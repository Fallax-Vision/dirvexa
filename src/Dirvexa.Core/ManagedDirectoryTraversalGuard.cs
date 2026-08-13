namespace Dirvexa.Core;

public sealed class ManagedDirectoryTraversalGuard : IDirectoryTraversalGuard
{
  public TraversalLeaseResult Acquire(string rootPath, string directoryPath)
  {
    var validation = PathValidator.ValidateTraversalDirectory(rootPath, directoryPath);
    return validation.IsValid
      ? TraversalLeaseResult.Success(EmptyLease.Instance)
      : TraversalLeaseResult.Failure(validation.Error ?? "Skipped an unsafe directory.");
  }

  private sealed class EmptyLease : IDisposable
  {
    public static EmptyLease Instance { get; } = new();
    public void Dispose() { }
  }
}
