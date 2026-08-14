using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Dirvexa.Core;

namespace Dirvexa.App.Infrastructure;

public enum UpdateCheckStatus
{
  UpdateAvailable,
  UpToDate,
  NoPublishedRelease,
  Unavailable
}

public sealed record UpdateCheckResult(
  UpdateCheckStatus Status,
  string Message,
  string? LatestVersion = null,
  Uri? ReleaseUri = null);

public interface IUpdateService
{
  Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}

public sealed class GitHubUpdateService(HttpClient client, SemanticVersion currentVersion) : IUpdateService
{
  private const int MaxResponseBytes = 64 * 1024;
  private static readonly Uri Endpoint = new("https://api.github.com/repos/Fallax-Vision/dirvexa/releases/latest");
  private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

  public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
  {
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(RequestTimeout);
    using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    request.Headers.UserAgent.ParseAdd($"Dirvexa/{currentVersion}");
    request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

    try
    {
      using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
      if (response.StatusCode == HttpStatusCode.NotFound)
        return new(UpdateCheckStatus.NoPublishedRelease, "No published Dirvexa release is available yet.");
      if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        return new(UpdateCheckStatus.Unavailable, "GitHub temporarily limited update checks. Please try again later.");
      if (!response.IsSuccessStatusCode)
        return new(UpdateCheckStatus.Unavailable, "The update service is temporarily unavailable.");
      var mediaType = response.Content.Headers.ContentType?.MediaType;
      if (!string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase)
          && !string.Equals(mediaType, "application/vnd.github+json", StringComparison.OrdinalIgnoreCase))
        return InvalidResponse();
      if (response.Content.Headers.ContentLength is > MaxResponseBytes)
        return InvalidResponse();

      var payload = await ReadBoundedAsync(response.Content, timeout.Token);
      using var document = JsonDocument.Parse(payload);
      var root = document.RootElement;
      if (!TryReadString(root, "tag_name", out var tag)
          || !SemanticVersion.TryParse(tag, out var latest)
          || latest is null
          || latest.PreRelease is not null
          || !TryReadString(root, "html_url", out var releaseUrl)
          || !OfficialReleaseUriValidator.TryValidate(releaseUrl, tag, out var releaseUri)
          || !TryReadBoolean(root, "draft", out var isDraft)
          || isDraft
          || !TryReadBoolean(root, "prerelease", out var isPrerelease)
          || isPrerelease) return InvalidResponse();

      return latest > currentVersion
        ? new(UpdateCheckStatus.UpdateAvailable, $"Dirvexa {latest} is available.", latest.ToString(), releaseUri)
        : new(UpdateCheckStatus.UpToDate, $"Dirvexa {currentVersion} is up to date.", latest.ToString());
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
      return new(UpdateCheckStatus.Unavailable, "The update check timed out. Please try again.");
    }
    catch (HttpRequestException)
    {
      return new(UpdateCheckStatus.Unavailable, "Could not reach GitHub. Check your connection and try again.");
    }
    catch (Exception exception) when (exception is JsonException or InvalidDataException)
    {
      return InvalidResponse();
    }
  }

  private static UpdateCheckResult InvalidResponse() =>
    new(UpdateCheckStatus.Unavailable, "GitHub returned an unexpected update response.");

  private static bool TryReadString(JsonElement root, string propertyName, out string value)
  {
    value = string.Empty;
    return root.TryGetProperty(propertyName, out var property)
      && property.ValueKind == JsonValueKind.String
      && !string.IsNullOrWhiteSpace(value = property.GetString() ?? string.Empty);
  }

  private static bool TryReadBoolean(JsonElement root, string propertyName, out bool value)
  {
    value = false;
    if (!root.TryGetProperty(propertyName, out var property)
        || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
    value = property.GetBoolean();
    return true;
  }

  private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
  {
    await using var stream = await content.ReadAsStreamAsync(cancellationToken);
    using var buffer = new MemoryStream();
    var block = new byte[8192];
    while (true)
    {
      var read = await stream.ReadAsync(block, cancellationToken);
      if (read == 0) return buffer.ToArray();
      if (buffer.Length + read > MaxResponseBytes) throw new InvalidDataException("Update response exceeded its size limit.");
      buffer.Write(block, 0, read);
    }
  }
}

public static class OfficialReleaseUriValidator
{
  private const string ReleaseTagPrefix = "/Fallax-Vision/dirvexa/releases/tag/";

  public static bool TryValidate(string value, string expectedTag, out Uri? uri)
  {
    uri = null;
    if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate)
        || !IsTrustedReleasePage(candidate)
        || !string.Equals(candidate.OriginalString, $"https://github.com{ReleaseTagPrefix}{expectedTag}", StringComparison.Ordinal)) return false;
    uri = candidate;
    return true;
  }

  public static bool IsTrustedReleasePage(Uri uri)
  {
    if (uri.Scheme != Uri.UriSchemeHttps
        || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        || !uri.IsDefaultPort
        || !string.IsNullOrEmpty(uri.UserInfo)
        || !string.IsNullOrEmpty(uri.Query)
        || !string.IsNullOrEmpty(uri.Fragment)
        || !uri.AbsolutePath.StartsWith(ReleaseTagPrefix, StringComparison.Ordinal)) return false;
    var tag = uri.AbsolutePath[ReleaseTagPrefix.Length..];
    return tag.Length > 0
      && !tag.Contains('/')
      && SemanticVersion.TryParse(tag, out _)
      && string.Equals(uri.OriginalString, $"https://github.com{ReleaseTagPrefix}{tag}", StringComparison.Ordinal);
  }
}
