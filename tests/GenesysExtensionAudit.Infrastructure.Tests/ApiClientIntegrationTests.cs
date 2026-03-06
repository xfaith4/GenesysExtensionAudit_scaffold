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
    // GroupDto.DateModified empty-string tolerance
    // ──────────────────────────────────────────────

    [Fact]
    public async Task GenesysGroupsClient_GetPage_DateModified_EmptyString_ReturnsNullDate()
    {
        // Genesys Cloud may return "" instead of null for groups that have never
        // been modified. Without the global NullableDateTimeConverter this would
        // throw a JsonException and crash the audit run.
        var handler = new RouteMockHttpMessageHandler();

        handler.WhenGet("/api/v2/groups?pageSize=1&pageNumber=1",
            json: """
                  {
                    "entities": [
                      { "id": "g1", "name": "Empty Group", "state": "active", "memberCount": 0, "dateModified": "" }
                    ],
                    "pageSize": 1, "pageNumber": 1, "pageCount": 1, "total": 1
                  }
                  """);

        var client = BuildGroupsClient(handler, "mypurecloud.com");
        var page = await client.GetGroupsPageAsync(pageNumber: 1, pageSize: 1, ct: default);

        Assert.Single(page.Items);
        Assert.Null(page.Items[0].DateModified);
    }

    [Fact]
    public async Task GenesysGroupsClient_GetPage_DateModified_NullValue_ReturnsNullDate()
    {
        var handler = new RouteMockHttpMessageHandler();

        handler.WhenGet("/api/v2/groups?pageSize=1&pageNumber=1",
            json: """
                  {
                    "entities": [
                      { "id": "g1", "name": "Group", "state": "active", "memberCount": 1, "dateModified": null }
                    ],
                    "pageSize": 1, "pageNumber": 1, "pageCount": 1, "total": 1
                  }
                  """);

        var client = BuildGroupsClient(handler, "mypurecloud.com");
        var page = await client.GetGroupsPageAsync(pageNumber: 1, pageSize: 1, ct: default);

        Assert.Single(page.Items);
        Assert.Null(page.Items[0].DateModified);
    }

    [Fact]
    public async Task GenesysGroupsClient_GetPage_DateModified_ValidDate_ParsedCorrectly()
    {
        var handler = new RouteMockHttpMessageHandler();

        handler.WhenGet("/api/v2/groups?pageSize=1&pageNumber=1",
            json: """
                  {
                    "entities": [
                      { "id": "g1", "name": "Group", "state": "active", "memberCount": 2, "dateModified": "2024-06-01T12:00:00.000Z" }
                    ],
                    "pageSize": 1, "pageNumber": 1, "pageCount": 1, "total": 1
                  }
                  """);

        var client = BuildGroupsClient(handler, "mypurecloud.com");
        var page = await client.GetGroupsPageAsync(pageNumber: 1, pageSize: 1, ct: default);

        Assert.Single(page.Items);
        Assert.NotNull(page.Items[0].DateModified);
        Assert.Equal(new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc), page.Items[0].DateModified!.Value.ToUniversalTime());
    }

    // ──────────────────────────────────────────────
    // QueueDto date fields empty-string tolerance
    // ──────────────────────────────────────────────

    [Fact]
    public async Task GenesysQueuesClient_GetPage_DateModified_EmptyString_ReturnsNullDate()
    {
        // Same Genesys API pattern: "" instead of null for queues with no modification date.
        var handler = new RouteMockHttpMessageHandler();

        handler.WhenGet("/api/v2/routing/queues?pageSize=1&pageNumber=1",
            json: """
                  {
                    "entities": [
                      { "id": "q1", "name": "Support", "memberCount": 0, "dateModified": "" }
                    ],
                    "pageSize": 1, "pageNumber": 1, "pageCount": 1, "total": 1
                  }
                  """);

        var client = BuildQueuesClient(handler, "mypurecloud.com");
        var page = await client.GetQueuesPageAsync(pageNumber: 1, pageSize: 1, ct: default);

        Assert.Single(page.Items);
        Assert.Null(page.Items[0].DateModified);
    }

    [Fact]
    public async Task GenesysQueuesClient_GetPage_DateCreated_EmptyString_ReturnsNullDate()
    {
        var handler = new RouteMockHttpMessageHandler();

        handler.WhenGet("/api/v2/routing/queues?pageSize=1&pageNumber=1",
            json: """
                  {
                    "entities": [
                      { "id": "q1", "name": "Support", "memberCount": 0, "dateCreated": "" }
                    ],
                    "pageSize": 1, "pageNumber": 1, "pageCount": 1, "total": 1
                  }
                  """);

        var client = BuildQueuesClient(handler, "mypurecloud.com");
        var page = await client.GetQueuesPageAsync(pageNumber: 1, pageSize: 1, ct: default);

        Assert.Single(page.Items);
        Assert.Null(page.Items[0].DateCreated);
    }

    // ──────────────────────────────────────────────
    // FlowDto / FlowPublishedVersionDto date fields
    // ──────────────────────────────────────────────

    [Fact]
    public async Task GenesysFlowsClient_GetPage_PublishedDate_EmptyString_ReturnsNullDate()
    {
        // Flows whose publishedVersion.publishedDate is "" must not crash deserialization.
        var handler = new RouteMockHttpMessageHandler();

        handler.WhenGet("/api/v2/flows?pageSize=1&pageNumber=1&include=publishedVersion",
            json: """
                  {
                    "entities": [
                      {
                        "id": "f1", "name": "Main IVR", "type": "INBOUNDCALL",
                        "publishedVersion": { "id": "v1", "publishedDate": "" }
                      }
                    ],
                    "pageSize": 1, "pageNumber": 1, "pageCount": 1, "total": 1
                  }
                  """);

        var client = BuildFlowsClient(handler, "mypurecloud.com");
        var page = await client.GetFlowsPageAsync(pageNumber: 1, pageSize: 1, ct: default);

        Assert.Single(page.Items);
        Assert.NotNull(page.Items[0].PublishedVersion);
        Assert.Null(page.Items[0].PublishedVersion!.PublishedDate);
    }

    [Fact]
    public async Task GenesysFlowsClient_GetPage_DateModified_EmptyString_ReturnsNullDate()
    {
        var handler = new RouteMockHttpMessageHandler();

        handler.WhenGet("/api/v2/flows?pageSize=1&pageNumber=1&include=publishedVersion",
            json: """
                  {
                    "entities": [
                      { "id": "f1", "name": "Draft Flow", "type": "OUTBOUNDCALL", "dateModified": "" }
                    ],
                    "pageSize": 1, "pageNumber": 1, "pageCount": 1, "total": 1
                  }
                  """);

        var client = BuildFlowsClient(handler, "mypurecloud.com");
        var page = await client.GetFlowsPageAsync(pageNumber: 1, pageSize: 1, ct: default);

        Assert.Single(page.Items);
        Assert.Null(page.Items[0].DateModified);
    }

    [Fact]
    public async Task GenesysFlowsClient_GetPage_DateCreated_EmptyString_ReturnsNullDate()
    {
        var handler = new RouteMockHttpMessageHandler();

        handler.WhenGet("/api/v2/flows?pageSize=1&pageNumber=1&include=publishedVersion",
            json: """
                  {
                    "entities": [
                      { "id": "f1", "name": "Draft Flow", "type": "INBOUNDCALL", "dateCreated": "" }
                    ],
                    "pageSize": 1, "pageNumber": 1, "pageCount": 1, "total": 1
                  }
                  """);

        var client = BuildFlowsClient(handler, "mypurecloud.com");
        var page = await client.GetFlowsPageAsync(pageNumber: 1, pageSize: 1, ct: default);

        Assert.Single(page.Items);
        Assert.Null(page.Items[0].DateCreated);
    }

    // ──────────────────────────────────────────────
    // OutboundEventDto.Timestamp empty-string tolerance
    // ──────────────────────────────────────────────

    [Fact]
    public async Task GenesysOutboundEventsClient_GetPage_Timestamp_EmptyString_ReturnsNullDate()
    {
        // OutboundEventDto.Timestamp is DateTimeOffset? — same issue class as lastTokenIssued.
        var handler = new RouteMockHttpMessageHandler();

        handler.WhenGet("/api/v2/outbound/events?pageSize=1&pageNumber=1",
            json: """
                  {
                    "entities": [
                      { "id": "ev1", "name": "Event 1", "timestamp": "", "level": "ERROR" }
                    ],
                    "pageSize": 1, "pageNumber": 1, "pageCount": 1, "total": 1
                  }
                  """);

        var client = BuildOutboundEventsClient(handler, "mypurecloud.com");
        var page = await client.GetOutboundEventsPageAsync(pageNumber: 1, pageSize: 1, ct: default);

        Assert.Single(page.Items);
        Assert.Null(page.Items[0].Timestamp);
    }

    [Fact]
    public async Task GenesysOutboundEventsClient_GetPage_Timestamp_ValidDate_ParsedCorrectly()
    {
        var handler = new RouteMockHttpMessageHandler();

        handler.WhenGet("/api/v2/outbound/events?pageSize=1&pageNumber=1",
            json: """
                  {
                    "entities": [
                      { "id": "ev1", "name": "Event 1", "timestamp": "2024-09-20T15:00:00.000Z", "level": "INFO" }
                    ],
                    "pageSize": 1, "pageNumber": 1, "pageCount": 1, "total": 1
                  }
                  """);

        var client = BuildOutboundEventsClient(handler, "mypurecloud.com");
        var page = await client.GetOutboundEventsPageAsync(pageNumber: 1, pageSize: 1, ct: default);

        Assert.Single(page.Items);
        Assert.NotNull(page.Items[0].Timestamp);
        Assert.Equal(
            new DateTimeOffset(2024, 9, 20, 15, 0, 0, TimeSpan.Zero),
            page.Items[0].Timestamp!.Value);
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

    private static GenesysGroupsClient BuildGroupsClient(RouteMockHttpMessageHandler handler, string region)
    {
        var http = new HttpClient(handler);
        var tokenProvider = new FakeTokenProvider("test-token");
        var regionOptions = Options.Create(new GenesysRegionOptions { Region = region });
        return new GenesysGroupsClient(http, tokenProvider, regionOptions, NullLogger<GenesysGroupsClient>.Instance);
    }

    private static GenesysQueuesClient BuildQueuesClient(RouteMockHttpMessageHandler handler, string region)
    {
        var http = new HttpClient(handler);
        var tokenProvider = new FakeTokenProvider("test-token");
        var regionOptions = Options.Create(new GenesysRegionOptions { Region = region });
        return new GenesysQueuesClient(http, tokenProvider, regionOptions, NullLogger<GenesysQueuesClient>.Instance);
    }

    private static GenesysFlowsClient BuildFlowsClient(RouteMockHttpMessageHandler handler, string region)
    {
        var http = new HttpClient(handler);
        var tokenProvider = new FakeTokenProvider("test-token");
        var regionOptions = Options.Create(new GenesysRegionOptions { Region = region });
        return new GenesysFlowsClient(http, tokenProvider, regionOptions, NullLogger<GenesysFlowsClient>.Instance);
    }

    private static GenesysOutboundEventsClient BuildOutboundEventsClient(RouteMockHttpMessageHandler handler, string region)
    {
        var http = new HttpClient(handler);
        var tokenProvider = new FakeTokenProvider("test-token");
        var regionOptions = Options.Create(new GenesysRegionOptions { Region = region });
        return new GenesysOutboundEventsClient(http, tokenProvider, regionOptions, NullLogger<GenesysOutboundEventsClient>.Instance);
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
            _routes["GET:" + pathAndQuery] = _ => new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        public void WhenPost(string pathAndQuery, string json, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _routes["POST:" + pathAndQuery] = _ => new HttpResponseMessage(statusCode)
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

            var routeKey = request.Method.Method + ":" + key;
            if (_routes.TryGetValue(routeKey, out var factory))
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
