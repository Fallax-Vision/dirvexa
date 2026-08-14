using System.Text.RegularExpressions;

namespace Dirvexa.Core;

public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
  private static readonly Regex Pattern = new(
    @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-((?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$",
    RegexOptions.CultureInvariant | RegexOptions.Compiled,
    TimeSpan.FromMilliseconds(100));

  private SemanticVersion(
    ulong major,
    ulong minor,
    ulong patch,
    string? preRelease,
    string? buildMetadata,
    string value)
  {
    Major = major;
    Minor = minor;
    Patch = patch;
    PreRelease = preRelease;
    BuildMetadata = buildMetadata;
    Value = value;
  }

  public ulong Major { get; }
  public ulong Minor { get; }
  public ulong Patch { get; }
  public string? PreRelease { get; }
  public string? BuildMetadata { get; }
  public string Value { get; }

  public static bool TryParse(string? value, out SemanticVersion? version)
  {
    version = null;
    if (string.IsNullOrWhiteSpace(value)) return false;
    var normalized = value.Trim();
    if (normalized.StartsWith('v')) normalized = normalized[1..];
    if (normalized.Length is 0 or > 256) return false;

    Match match;
    try { match = Pattern.Match(normalized); }
    catch (RegexMatchTimeoutException) { return false; }
    if (!match.Success
        || !ulong.TryParse(match.Groups[1].Value, out var major)
        || !ulong.TryParse(match.Groups[2].Value, out var minor)
        || !ulong.TryParse(match.Groups[3].Value, out var patch)) return false;

    version = new SemanticVersion(
      major,
      minor,
      patch,
      match.Groups[4].Success ? match.Groups[4].Value : null,
      match.Groups[5].Success ? match.Groups[5].Value : null,
      normalized);
    return true;
  }

  public static SemanticVersion Parse(string value) =>
    TryParse(value, out var version) && version is not null
      ? version
      : throw new FormatException($"'{value}' is not a valid Semantic Versioning 2.0.0 value.");

  public int CompareTo(SemanticVersion? other)
  {
    if (other is null) return 1;
    var comparison = Major.CompareTo(other.Major);
    if (comparison != 0) return comparison;
    comparison = Minor.CompareTo(other.Minor);
    if (comparison != 0) return comparison;
    comparison = Patch.CompareTo(other.Patch);
    if (comparison != 0) return comparison;
    if (PreRelease is null) return other.PreRelease is null ? 0 : 1;
    if (other.PreRelease is null) return -1;

    var left = PreRelease.Split('.');
    var right = other.PreRelease.Split('.');
    for (var index = 0; index < Math.Min(left.Length, right.Length); index++)
    {
      comparison = CompareIdentifier(left[index], right[index]);
      if (comparison != 0) return comparison;
    }
    return left.Length.CompareTo(right.Length);
  }

  public bool Equals(SemanticVersion? other) => CompareTo(other) == 0;
  public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);
  public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, PreRelease);
  public override string ToString() => Value;

  public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;
  public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;
  public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;
  public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;

  private static int CompareIdentifier(string left, string right)
  {
    var leftNumeric = left.All(character => character is >= '0' and <= '9');
    var rightNumeric = right.All(character => character is >= '0' and <= '9');
    if (leftNumeric && !rightNumeric) return -1;
    if (!leftNumeric && rightNumeric) return 1;
    if (!leftNumeric) return string.CompareOrdinal(left, right);
    var lengthComparison = left.Length.CompareTo(right.Length);
    return lengthComparison != 0 ? lengthComparison : string.CompareOrdinal(left, right);
  }
}
