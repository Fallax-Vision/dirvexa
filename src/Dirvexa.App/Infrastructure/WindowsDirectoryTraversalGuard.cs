using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Dirvexa.Core;
using Microsoft.Win32.SafeHandles;

namespace Dirvexa.App.Infrastructure;

public sealed class WindowsDirectoryTraversalGuard : IDirectoryTraversalGuard
{
  private const uint FileReadAttributes = 0x0080;
  private const uint FileShareRead = 0x00000001;
  private const uint FileShareWrite = 0x00000002;
  private const uint OpenExisting = 3;
  private const uint FileFlagBackupSemantics = 0x02000000;
  private const uint FileFlagOpenReparsePoint = 0x00200000;
  private const uint FileAttributeReparsePoint = 0x00000400;

  public TraversalLeaseResult Acquire(string rootPath, string directoryPath)
  {
    var validation = PathValidator.ValidateTraversalDirectory(rootPath, directoryPath);
    if (!validation.IsValid || validation.NormalizedPath is null)
      return TraversalLeaseResult.Failure(validation.Error ?? "Skipped an unsafe directory.");
    if (!OperatingSystem.IsWindows())
      return TraversalLeaseResult.Success(EmptyLease.Instance);

    var handles = new List<SafeFileHandle>();
    try
    {
      foreach (var componentPath in EnumerateComponents(validation.NormalizedPath))
      {
        var handle = CreateFileW(
          componentPath,
          FileReadAttributes,
          FileShareRead | FileShareWrite,
          0,
          OpenExisting,
          FileFlagBackupSemantics | FileFlagOpenReparsePoint,
          0);
        if (handle.IsInvalid)
        {
          var error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
          handle.Dispose();
          return Failure(handles, $"Skipped an unavailable directory: {error}");
        }
        handles.Add(handle);

        if (!GetFileInformationByHandleEx(
              handle,
              FileInfoByHandleClass.FileAttributeTagInfo,
              out var tagInfo,
              (uint)Marshal.SizeOf<FileAttributeTagInfo>()))
        {
          var error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
          return Failure(handles, $"Skipped a directory whose attributes could not be verified: {error}");
        }
        if ((tagInfo.FileAttributes & FileAttributeReparsePoint) != 0)
          return Failure(handles, "Skipped a reparse point.");

        var resolvedPath = GetResolvedPath(handle);
        if (!string.Equals(NormalizePath(resolvedPath), NormalizePath(componentPath), StringComparison.OrdinalIgnoreCase))
          return Failure(handles, "Skipped a directory whose resolved path changed during validation.");
      }

      return TraversalLeaseResult.Success(new DirectoryLease(handles));
    }
    catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or Win32Exception)
    {
      return Failure(handles, $"Skipped an unavailable directory: {exception.Message}");
    }
  }

  private static IEnumerable<string> EnumerateComponents(string directoryPath)
  {
    var fullPath = Path.GetFullPath(directoryPath);
    var root = Path.GetPathRoot(fullPath) ?? throw new IOException("The directory has no local volume root.");
    var current = root;
    yield return current;

    var relative = Path.GetRelativePath(root, fullPath);
    if (relative == ".") yield break;
    foreach (var component in relative.Split(
               [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
               StringSplitOptions.RemoveEmptyEntries))
    {
      current = Path.Combine(current, component);
      yield return current;
    }
  }

  private static string GetResolvedPath(SafeFileHandle handle)
  {
    var required = GetFinalPathNameByHandleW(handle, null, 0, 0);
    if (required == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    var builder = new StringBuilder((int)required + 1);
    var written = GetFinalPathNameByHandleW(handle, builder, (uint)builder.Capacity, 0);
    if (written == 0 || written >= builder.Capacity) throw new Win32Exception(Marshal.GetLastWin32Error());
    return builder.ToString();
  }

  private static string NormalizePath(string path)
  {
    if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
      path = @"\\" + path[8..];
    else if (path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
      path = path[4..];
    return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
  }

  private static TraversalLeaseResult Failure(List<SafeFileHandle> handles, string error)
  {
    foreach (var handle in handles) handle.Dispose();
    return TraversalLeaseResult.Failure(error);
  }

  private sealed class DirectoryLease(List<SafeFileHandle> handles) : IDisposable
  {
    public void Dispose()
    {
      for (var index = handles.Count - 1; index >= 0; index--)
        handles[index].Dispose();
    }
  }

  private sealed class EmptyLease : IDisposable
  {
    public static EmptyLease Instance { get; } = new();
    public void Dispose() { }
  }

  private enum FileInfoByHandleClass
  {
    FileAttributeTagInfo = 9
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct FileAttributeTagInfo
  {
    public uint FileAttributes;
    public uint ReparseTag;
  }

  [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  private static extern SafeFileHandle CreateFileW(
    string fileName,
    uint desiredAccess,
    uint shareMode,
    nint securityAttributes,
    uint creationDisposition,
    uint flagsAndAttributes,
    nint templateFile);

  [DllImport("kernel32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool GetFileInformationByHandleEx(
    SafeFileHandle file,
    FileInfoByHandleClass fileInformationClass,
    out FileAttributeTagInfo fileInformation,
    uint bufferSize);

  [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  private static extern uint GetFinalPathNameByHandleW(
    SafeFileHandle file,
    StringBuilder? filePath,
    uint filePathLength,
    uint flags);
}
