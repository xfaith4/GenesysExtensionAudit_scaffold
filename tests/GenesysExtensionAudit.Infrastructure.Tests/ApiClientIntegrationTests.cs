using System.Net;
using System.Text;
using GenesysExtensionAudit.Infrastructure.Genesys.Clients;
using GenesysExtensionAudit.Infrastructure.Genesys.Pagination;
using GenesysExtensionAudit.Infrastructure.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GenesysExtensionAudit.Infrastructure.Tests;

/// <summary>
/// Integration-style tests for the paginated Genesys API clients using
/// a lightweight mock HttpMessageHandler that matches on path+query.
/// </summary>
public sealed class ApiClientIntegrationTests
{
    // ──────────────────────────────────────────────
    // GenesysUsersClient + Paginator
    // ──────────────────────────────────────────────

    [Fact]
    public async Task GenesysUsersClient_GetPage_FiltersActiveOnly_WhenIncludeInactiveFalse()
    {
        var handler = new RouteMockHttpMessageHandler();

        handler.WhenGet("/api/v2/users?pageSize=2&pageNumber=1&expand=locations,station,lasttokenissued&state=active",
            json: """
                  {
                    "entities": [
                      { "id": "u1", "name": "User 1", "state": "active" },
                      { "id": "u2", "name": "User 2", "state": "active" }
                    ],
                    "pageSize": 2, "pageNumber": 1, "pageCount": 1, "total": 2
                  }
                  """);

        var client = BuildUsersClient(handler, "mypurecloud.com");

        var page = await client.GetUsersPageAsync(
            pageNumber: 1, pageSize: 2, includeInactive: false, ct: default);

        Assert.Equal(2, page.Items.Count);
        Assert.Equal("u1", page.Items[0].Id);
        handler.AssertCalledTimes("/api/v2/users?pageSize=2&pageNumber=1&expand=locations,station,lasttokenissued&state=active", 1);
    }

    [Fact]
    public async Task GenesysUsersClient_GetPage_OmitsState_WhenIncludeInactiveTrue()
    {
        var handler = new RouteMockHttpMessageHandler();

        handler.WhenGet("/api/v2/users?pageSize=3&pageNumber=1&expand=locations,station,lasttokenissued",
            json: """
                  {
                    "entities": [
                      { "id": "u1", "name": "User 1", "state": "inactive" }
                    ],
                    "pageSize": 3, "pageNumber": 1, "pageCount": 1, "total": 1
                  }
                  """);

        var client = BuildUsersClient(handler, "mypurecloud.com");
        var page = await client.GetUsersPageAsync(
            pageNumber: 1, pageSize: 3, includeInactive: true, ct: default);

        Assert.Single(page.Items);
        Assert.DoesNotContain(handler.Calls.Keys, k => k.Contains("state=active", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Paginator_FetchesAllPages_ViaUsersClient()
    {
        var handler = new RouteMockHttpMessageHandler();

        handler.WhenGet("/api/v2/users?pageSize=2&pageNumber=1&expand=locations,station,lasttokenissued&state=active",
            json: """
                  {
                    "entities": [
                      { "id": "u1", "name": "User 1", "state": "active" },
                      { "id": "u2", "name": "User 2", "state": "active" }
                    ],
                    "pageSize": 2, "pageNumber": 1, "pageCount": 2, "total": 4
                  }
                  """);

        handler.WhenGet("/api/v2/users?pageSize=2&pageNumber=2&expand=locations,station,lasttokenissued&state=active",
            json: """
                  {
                    "entities": [
                      { "id": "u3", "name": "User 3", "state": "active" },
                      { "id": "u4", "name": "User 4", "state": "active" }
                    ],
                    "pageSize": 2, "pageNumber": 2, "pageCount": 2, "total": 4
                  }
                  """);

        var client = BuildUsersClient(handler, "mypurecloud.com");
        var paginator = new Paginator(NullLogger<Paginator>.Instance);

        var users = await paginator.FetchAllAsync(
            pageNumber => client.GetUsersPageAsync(pageNumber, 2, false, default),
            default);

        Assert.Equal(4, users.Count);
        Assert.Equal(new[] { "u1", "u2", "u3", "u4" }, users.Select(u => u.Id).ToArray());
    }

    // ──────────────────────────────────────────────
    // lastTokenIssued / NullableDateTimeOffsetConverter
    // ──────────────────────────────────────────────

    [Fact]
    public async Task GenesysUsersClient_GetPage_LastTokenIssued_EmptyString_ReturnsNullToken()
    {
        // Genesys Cloud sometimes returns "" instead of null for users who have
        // never issued an OAuth token.  The converter must not throw and must
        // expose null on the DTO.
        var handler = new RouteMockHttpMessageHandler();

        handler.WhenGet("/api/v2/users?pageSize=1&pageNumber=1&expand=locations,station,lasttokenissued&state=active",
            json: """
                  {
                    "entities": [
                      { "id": "u1", "name": "No Token User", "state": "active", "lasttokenissued": "" }
                    ],
                    "pageSize": 1, "pageNumber": 1, "pageCount": 1, "total": 1
                  }
                  """);

        var client = BuildUsersClient(handler, "mypurecloud.com");
        var page = await client.GetUsersPageAsync(pageNumber: 1, pageSize: 1, includeInactive: false, ct: default);

        Assert.Single(page.Items);
        Assert.Null(page.Items[0].TokenLastIssuedDate);
    }

    [Fact]
    public async Task GenesysUsersClient_GetPage_LastTokenIssued_NullValue_ReturnsNullToken()
    {
        var handler = new RouteMockHttpMessageHandler();

        handler.WhenGet("/api/v2/users?pageSize=1&pageNumber=1&expand=locations,station,lasttokenissued&state=active",
            json: """
                  {
                    "entities": [
                      { "id": "u1", "name": "Service Account", "state": "active", "lasttokenissued": null }
                    ],
                    "pageSize": 1, "pageNumber": 1, "pageCount": 1, "total": 1
                  }
                  """);

        var client = BuildUsersClient(handler, "mypurecloud.com");
        var page = await client.GetUsersPageAsync(pageNumber: 1, pageSize: 1, includeInactive: false, ct: default);

        Assert.Single(page.Items);
        Assert.Null(page.Items[0].TokenLastIssuedDate);
    }

    [Fact]
    public async Task GenesysUsersClient_GetPage_LastTokenIssued_ValidDate_ParsedCorrectly()
    {
        var handler = new RouteMockHttpMessageHandler();

        handler.WhenGet("/api/v2/users?pageSize=1&pageNumber=1&expand=locations,station,lasttokenissued&state=active",
            json: """
                  {
                    "entities": [
                      { "id": "u1", "name": "Active User", "state": "active", "lasttokenissued": "2024-11-15T08:30:00.000Z" }
                    ],
                    "pageSize": 1, "pageNumber": 1, "pageCount": 1, "total": 1
                  }
                  """);

        var client = BuildUsersClient(handler, "mypurecloud.com");
        var page = await client.GetUsersPageAsync(pageNumber: 1, pageSize: 1, includeInactive: false, ct: default);

        Assert.Single(page.Items);
        Assert.NotNull(page.Items[0].TokenLastIssuedDate);
        Assert.Equal(
            new DateTimeOffset(2024, 11, 15, 8, 30, 0, TimeSpan.Zero),
            page.Items[0].TokenLastIssuedDate!.Value);
    }

    // ──────────────────────────────────────────────
    // GenesysExtensionsClient + Paginator
    // ──────────────────────────────────────────────

    [Fact]
    public async Task GenesysExtensionsClient_Paginator_FetchesAllExtensionPages()
    {
        var handler = new RouteMockHttpMessageHandler();

        handler.WhenGet("/api/v2/telephony/providers/edges/extensions?pageSize=2&pageNumber=1",
            json: """
                  {
                    "entities": [
                      { "id": "e1", "extension": "1001" },
                      { "id": "e2", "extension": "1002" }
                    ],
                    "pageSize": 2, "pageNumber": 1, "pageCount": 2, "total": 4
                  }
                  """);

        handler.WhenGet("/api/v2/telephony/providers/edges/extensions?pageSize=2&pageNumber=2",
            json: """
                  {
                    "entities": [
                      { "id": "e3", "extension": "1003" },
                      { "id": "e4", "extension": "1004" }
                    ],
                    "pageSize": 2, "pageNumber": 2, "pageCount": 2, "total": 4
                  }
                  """);

        var client = BuildExtensionsClient(handler, "mypurecloud.com");
        var paginator = new Paginator(NullLogger<Paginator>.Instance);

        var extensions = await paginator.FetchAllAsync(
            pageNumber => client.GetExtensionsPageAsync(pageNumber, 2, default),
            default);

        Assert.Equal(4, extensions.Count);
        Assert.Equal(new[] { "1001", "1002", "1003", "1004" },
            extensions.Select(e => e.Extension).ToArray());

        handler.AssertCalledTimes("/api/v2/telephony/providers/edges/extensions?pageSize=2&pageNumber=1", 1);
        handler.AssertCalledTimes("/api/v2/telephony/providers/edges/extensions?pageSize=2&pageNumber=2", 1);
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    private static GenesysUsersClient BuildUsersClient(RouteMockHttpMessageHandler handler, string region)
    {
        var http = new HttpClient(handler);
        var tokenProvider = new FakeTokenProvider("test-token");
        var regionOptions = Options.Create(new GenesysRegionOptions { Region = region });
        return new GenesysUsersClient(http, tokenProvider, regionOptions, NullLogger<GenesysUsersClient>.Instance);
    }

    private static GenesysExtensionsClient BuildExtensionsClient(RouteMockHttpMessageHandler handler, string region)
    {
        var http = new HttpClient(handler);
        var tokenProvider = new FakeTokenProvider("test-token");
        var regionOptions = Options.Create(new GenesysRegionOptions { Region = region });
        return new GenesysExtensionsClient(http, tokenProvider, regionOptions, NullLogger<GenesysExtensionsClient>.Instance);
    }

    // ──────────────────────────────────────────────
    // Mock infrastructure
    // ──────────────────────────────────────────────

    private sealed class RouteMockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes
            = new(StringComparer.Ordinal);

        public Dictionary<string, int> Calls { get; } = new(StringComparer.Ordinal);

        public void WhenGet(string pathAndQuery, string json, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _routes[pathAndQuery] = _ => new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        public void AssertCalledTimes(string pathAndQuery, int times)
        {
            Calls.TryGetValue(pathAndQuery, out var actual);
            Assert.Equal(times, actual);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = request.RequestUri is null
                ? ""
                : request.RequestUri.AbsolutePath + request.RequestUri.Query;

            Calls[key] = Calls.TryGetValue(key, out var count) ? count + 1 : 1;

            if (request.Method != HttpMethod.Get)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.MethodNotAllowed));

            if (_routes.TryGetValue(key, out var factory))
                return Task.FromResult(factory(request));

            var msg = $"No mock route registered for: {request.Method} {key}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(msg, Encoding.UTF8, "text/plain")
            });
        }
    }

    private sealed class FakeTokenProvider : ITokenProvider
    {
        private readonly string _token;
        public FakeTokenProvider(string token) => _token = token;
        public Task<string> GetAccessTokenAsync(CancellationToken ct) => Task.FromResult(_token);
        public Task ForceRefreshAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
