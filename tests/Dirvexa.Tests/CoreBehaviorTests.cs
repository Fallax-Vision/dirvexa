using Dirvexa.Core;

namespace Dirvexa.Tests;

public sealed class CoreBehaviorTests
{
  [Theory]
  [InlineData(".png", FileCategory.Images)]
  [InlineData("JPG", FileCategory.Images)]
  [InlineData(".mp3", FileCategory.Audio)]
  [InlineData(".mkv", FileCategory.Videos)]
  [InlineData(".pdf", FileCategory.Documents)]
  [InlineData(".exe", FileCategory.AppsPrograms)]
  [InlineData(".7z", FileCategory.Compressed)]
  [InlineData("", FileCategory.Other)]
  [InlineData(".unknown", FileCategory.Other)]
  public void ClassifiesExtensions(string extension, FileCategory expected) =>
    Assert.Equal(expected, FileClassifier.Classify(extension));

  [Theory]
  [InlineData(0, "0 B")]
  [InlineData(1024, "1 KB")]
  [InlineData(1536, "1.5 KB")]
  [InlineData(1073741824, "1 GB")]
  public void FormatsBytes(long bytes, string expected) => Assert.Equal(expected, ByteFormatter.Format(bytes));

  [Fact]
  public void RejectsNetworkAndRelativePaths()
  {
    Assert.False(PathValidator.ValidateLocalDirectory(@"\\server\share").IsValid);
    Assert.False(PathValidator.ValidateLocalDirectory("//server/share").IsValid);
    Assert.False(PathValidator.ValidateLocalDirectory("relative-folder").IsValid);
  }

  [Fact]
  public void NormalizesAnExistingLocalDirectory()
  {
    using var directory = TemporaryDirectory.Create();
    var result = PathValidator.ValidateLocalDirectory($"  \"{directory.Path}\"  ");
    Assert.True(result.IsValid, result.Error);
    Assert.Equal(Path.TrimEndingDirectorySeparator(directory.Path), result.NormalizedPath);
  }

  [Fact]
  public void RejectsAPathBelowAReparseAncestorWhenSupported()
  {
    if (!OperatingSystem.IsWindows()) return;
    using var parent = TemporaryDirectory.Create();
    using var target = TemporaryDirectory.Create();
    Directory.CreateDirectory(Path.Combine(target.Path, "child"));
    var link = Path.Combine(parent.Path, "link");
    try
    {
      Directory.CreateSymbolicLink(link, target.Path);
    }
    catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or NotSupportedException)
    {
      var startInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe")
      {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardError = true
      };
      startInfo.ArgumentList.Add("/d");
      startInfo.ArgumentList.Add("/c");
      startInfo.ArgumentList.Add("mklink");
      startInfo.ArgumentList.Add("/J");
      startInfo.ArgumentList.Add(link);
      startInfo.ArgumentList.Add(target.Path);
      using var process = System.Diagnostics.Process.Start(startInfo);
      Assert.NotNull(process);
      process.WaitForExit();
      Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
    }

    try
    {
      Assert.False(PathValidator.ValidateLocalDirectory(Path.Combine(link, "child")).IsValid);
    }
    finally
    {
      Directory.Delete(link);
    }
  }
}
