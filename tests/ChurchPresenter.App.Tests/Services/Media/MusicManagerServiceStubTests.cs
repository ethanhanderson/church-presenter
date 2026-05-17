using System.Net;
using System.Net.Http;
using System.Text;


using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace ChurchPresenter.App.Tests.Services.Media;

public sealed class MusicManagerServiceTests
{
    [Fact]
    public async Task GetGroupNamesAsync_returns_empty_list_when_not_configured()
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(s => s.Settings).Returns(new AppSettingsDto());

        var svc = new MusicManagerService(
            new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))),
            settings.Object,
            NullLogger<MusicManagerService>.Instance);

        var groups = await svc.GetGroupNamesAsync();
        groups.Should().BeEmpty();
    }

    [Fact]
    public async Task GetGroupNamesAsync_uses_music_schema_headers_and_returns_names()
    {
        HttpRequestMessage? captured = null;
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(s => s.Settings).Returns(new AppSettingsDto
        {
            Integrations =
            {
                MusicManager =
                {
                    SupabaseUrl = "https://example.supabase.co",
                    PublishableKey = "test-key",
                },
            },
        });

        var svc = new MusicManagerService(
            new HttpClient(new StubHandler(request =>
            {
                captured = request;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""[{ "name": "Main" }, { "name": "Students" }]""", Encoding.UTF8, "application/json"),
                };
            })),
            settings.Object,
            NullLogger<MusicManagerService>.Instance);

        var groups = await svc.GetGroupNamesAsync();

        groups.Should().Equal("Main", "Students");
        captured.Should().NotBeNull();
        captured!.Headers.Should().Contain(header => header.Key == "apikey");
        captured.Headers.Should().Contain(header => header.Key == "Accept-Profile" && header.Value.Contains("music"));
        captured.RequestUri!.ToString().Should().Contain("/rest/v1/music_groups");
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}