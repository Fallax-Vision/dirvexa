using System.Net;
using System.Text;
using Dirvexa.App.Infrastructure;
using Dirvexa.Core;

namespace Dirvexa.Tests;

public sealed class UpdateTests
{
  [Theory]
  [InlineData("1.0.0", "1.0.1")]
  [InlineData("1.0.0", "1.1.0")]
  [InlineData("1.0.0", "2.0.0")]
  [InlineData("1.0.0-alpha", "1.0.0")]
  [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")]
  [InlineData("1.0.0-beta.2", "1.0.0-beta.11")]
  [InlineData("1.0.0+build.1", "1.0.1+build.1")]
  public void SemanticVersionPrecedenceMatchesTheSpecification(string older, string newer)
  {
    Assert.True(SemanticVersion.Parse(older) < SemanticVersion.Parse(newer));
  }

  [Theory]
  [InlineData("1.0")]
  [InlineData("01.0.0")]
  [InlineData("1.0.0-alpha.01")]
  [InlineData("1.0.0-")]
  [InlineData("not-a-version")]
  public void SemanticVersionRejectsInvalidValues(string value)
  {
    Assert.False(SemanticVersion.TryParse(value, out _));
  }

  [Fact]
  public void BuildMetadataDoesNotAffectVersionPrecedence()
  {
    Assert.Equal(0, SemanticVersion.Parse("1.2.3+one").CompareTo(SemanticVersion.Parse("1.2.3+two")));
  }

  [Fact]
  public async Task UpdateCheckAcceptsOnlyAnOfficialNewerRelease()
  {
    var handler = new StubHttpHandler(request =>
    {
      Assert.Equal("https://api.github.com/repos/Fallax-Vision/dirvexa/releases/latest", request.RequestUri?.AbsoluteUri);
      Assert.Contains(request.Headers.UserAgent, value => value.Product?.Name == "Dirvexa");
      return JsonResponse("""
        {"tag_name":"v0.2.0","html_url":"https://github.com/Fallax-Vision/dirvexa/releases/tag/v0.2.0","draft":false,"prerelease":false}
        """);
    });
    var service = new GitHubUpdateService(new HttpClient(handler), SemanticVersion.Parse("0.1.0"));

    var result = await service.CheckAsync();

    Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
    Assert.Equal("0.2.0", result.LatestVersion);
    Assert.Equal("https://github.com/Fallax-Vision/dirvexa/releases/tag/v0.2.0", result.ReleaseUri?.AbsoluteUri);
  }

  [Fact]
  public async Task UpdateCheckRejectsUntrustedReleaseLinks()
  {
    var handler = new StubHttpHandler(_ => JsonResponse("""
      {"tag_name":"9.0.0","html_url":"https://example.com/malware.exe","draft":false,"prerelease":false}
      """));
    var service = new GitHubUpdateService(new HttpClient(handler), SemanticVersion.Parse("0.2.0"));

    var result = await service.CheckAsync();

    Assert.Equal(UpdateCheckStatus.Unavailable, result.Status);
    Assert.Null(result.ReleaseUri);
  }

  [Theory]
  [InlineData("v0.2.0", "https://github.com/Fallax-Vision/dirvexa/releases/download/v0.2.0/Dirvexa.exe")]
  [InlineData("v0.2.0", "https://github.com:444/Fallax-Vision/dirvexa/releases/tag/v0.2.0")]
  [InlineData("v0.2.0", "https://github.com/Fallax-Vision/dirvexa/releases/tag/v0.3.0")]
  [InlineData("v0.2.0", "https://github.com/Fallax-Vision/dirvexa/releases/tag/v0.2.0?download=1")]
  [InlineData("v0.2.0", "https://github.com/Fallax-Vision/dirvexa/releases/tag/v0.2.0#files")]
  [InlineData("v0.2.0", "https://github.com/Fallax-Vision/dirvexa/releases/tag/%760.2.0")]
  public async Task UpdateCheckRejectsNoncanonicalReleaseLinks(string tag, string releaseUrl)
  {
    Assert.False(OfficialReleaseUriValidator.TryValidate(releaseUrl, tag, out _), releaseUrl);
    var handler = new StubHttpHandler(_ => JsonResponse($$"""
      {"tag_name":"{{tag}}","html_url":"{{releaseUrl}}","draft":false,"prerelease":false}
      """));
    var service = new GitHubUpdateService(new HttpClient(handler), SemanticVersion.Parse("0.1.0"));

    var result = await service.CheckAsync();

    Assert.Equal(UpdateCheckStatus.Unavailable, result.Status);
    Assert.Null(result.ReleaseUri);
  }

  [Theory]
  [InlineData("\"draft\":false")]
  [InlineData("\"draft\":null,\"prerelease\":false")]
  [InlineData("\"draft\":\"false\",\"prerelease\":false")]
  [InlineData("\"draft\":false,\"prerelease\":0")]
  [InlineData("\"draft\":false,\"prerelease\":true")]
  public async Task UpdateCheckRejectsMissingOrMalformedStableReleaseFlags(string flags)
  {
    var handler = new StubHttpHandler(_ => JsonResponse($$"""
      {"tag_name":"v0.2.0","html_url":"https://github.com/Fallax-Vision/dirvexa/releases/tag/v0.2.0",{{flags}}}
      """));
    var service = new GitHubUpdateService(new HttpClient(handler), SemanticVersion.Parse("0.1.0"));

    var result = await service.CheckAsync();

    Assert.Equal(UpdateCheckStatus.Unavailable, result.Status);
  }

  [Fact]
  public async Task UpdateCheckRejectsAPrereleaseTagEvenWhenMetadataClaimsItIsStable()
  {
    var handler = new StubHttpHandler(_ => JsonResponse("""
      {"tag_name":"v0.3.0-beta.1","html_url":"https://github.com/Fallax-Vision/dirvexa/releases/tag/v0.3.0-beta.1","draft":false,"prerelease":false}
      """));
    var service = new GitHubUpdateService(new HttpClient(handler), SemanticVersion.Parse("0.2.0"));

    var result = await service.CheckAsync();

    Assert.Equal(UpdateCheckStatus.Unavailable, result.Status);
  }

  [Fact]
  public async Task UpdateCheckHandlesARepositoryWithoutPublishedReleases()
  {
    var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
    var service = new GitHubUpdateService(new HttpClient(handler), SemanticVersion.Parse("0.2.0"));

    var result = await service.CheckAsync();

    Assert.Equal(UpdateCheckStatus.NoPublishedRelease, result.Status);
  }

  [Fact]
  public async Task UpdateCheckHandlesRateLimitingWithoutRetrying()
  {
    var requests = 0;
    var handler = new StubHttpHandler(_ =>
    {
      requests++;
      return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
    });
    var service = new GitHubUpdateService(new HttpClient(handler), SemanticVersion.Parse("0.2.0"));

    var result = await service.CheckAsync();

    Assert.Equal(UpdateCheckStatus.Unavailable, result.Status);
    Assert.Equal(1, requests);
  }

  [Fact]
  public async Task UpdateCheckRejectsOversizedResponses()
  {
    var handler = new StubHttpHandler(_ => JsonResponse(new string(' ', 70 * 1024)));
    var service = new GitHubUpdateService(new HttpClient(handler), SemanticVersion.Parse("0.2.0"));

    var result = await service.CheckAsync();

    Assert.Equal(UpdateCheckStatus.Unavailable, result.Status);
  }

  [Fact]
  public async Task UpdateCheckReportsTheCurrentVersionAsUpToDate()
  {
    var handler = new StubHttpHandler(_ => JsonResponse("""
      {"tag_name":"v0.2.0","html_url":"https://github.com/Fallax-Vision/dirvexa/releases/tag/v0.2.0","draft":false,"prerelease":false}
      """));
    var service = new GitHubUpdateService(new HttpClient(handler), SemanticVersion.Parse("0.2.0"));

    var result = await service.CheckAsync();

    Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    Assert.Null(result.ReleaseUri);
  }

  [Fact]
  public void DistributionDetectionWorksForInstalledAndPortableExecutables()
  {
    var localAppData = Path.Combine(Path.GetTempPath(), "DirvexaDistributionTest");
    Assert.Equal(
      AppDistribution.Installed,
      AppDistributionDetector.Detect(Path.Combine(localAppData, "Programs", "Dirvexa"), localAppData));
    Assert.Equal(
      AppDistribution.Portable,
      AppDistributionDetector.Detect(Path.Combine(localAppData, "Downloads"), localAppData));
  }

  private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
  {
    Content = new StringContent(json, Encoding.UTF8, "application/json")
  };

  private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
      Task.FromResult(respond(request));
  }
}
