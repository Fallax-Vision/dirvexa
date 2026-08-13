namespace Dirvexa.Core;

public static class FileClassifier
{
  private static readonly HashSet<string> ImageExtensions = Build(
    ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".svg", ".ico", ".heic", ".avif", ".raw");

  private static readonly HashSet<string> AudioExtensions = Build(
    ".mp3", ".wav", ".flac", ".aac", ".m4a", ".ogg", ".opus", ".wma", ".aiff", ".mid", ".midi");

  private static readonly HashSet<string> VideoExtensions = Build(
    ".mp4", ".mkv", ".mov", ".avi", ".wmv", ".webm", ".m4v", ".mpeg", ".mpg", ".3gp");

  private static readonly HashSet<string> DocumentExtensions = Build(
    ".txt", ".md", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".csv", ".tsv", ".rtf", ".odt", ".ods", ".odp", ".epub", ".json", ".xml", ".yaml", ".yml");

  private static readonly HashSet<string> ProgramExtensions = Build(
    ".exe", ".msi", ".msix", ".appx", ".appxbundle", ".com", ".bat", ".cmd", ".ps1", ".dll", ".scr");

  private static readonly HashSet<string> CompressedExtensions = Build(
    ".zip", ".7z", ".rar", ".tar", ".gz", ".gzip", ".bz2", ".xz", ".cab", ".iso", ".tgz", ".zst");

  public static FileCategory Classify(string? extension)
  {
    var normalized = Normalize(extension);
    if (ImageExtensions.Contains(normalized)) return FileCategory.Images;
    if (AudioExtensions.Contains(normalized)) return FileCategory.Audio;
    if (VideoExtensions.Contains(normalized)) return FileCategory.Videos;
    if (DocumentExtensions.Contains(normalized)) return FileCategory.Documents;
    if (ProgramExtensions.Contains(normalized)) return FileCategory.AppsPrograms;
    if (CompressedExtensions.Contains(normalized)) return FileCategory.Compressed;
    return FileCategory.Other;
  }

  private static HashSet<string> Build(params string[] extensions) =>
    new(extensions, StringComparer.OrdinalIgnoreCase);

  private static string Normalize(string? extension)
  {
    if (string.IsNullOrWhiteSpace(extension)) return string.Empty;
    var trimmed = extension.Trim();
    return trimmed.StartsWith('.') ? trimmed : $".{trimmed}";
  }
}
