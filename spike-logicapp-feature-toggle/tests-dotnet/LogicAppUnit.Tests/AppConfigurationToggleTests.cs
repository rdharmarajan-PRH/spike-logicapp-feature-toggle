using System.Net;
using System.Text;
using System.Text.Json;
using LogicAppUnit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FeatureToggle.LogicAppUnit.Tests;

// =============================================================================
// NOTE ON THE MOCK API:
// LogicAppUnit's outgoing-HTTP mocking API has two styles across versions:
//   (a) Delegate:   testRunner.AddApiMocks = (request) => new HttpResponseMessage(...);
//   (b) Fluent:     testRunner.AddMockResponse(MockRequestMatcher.Create()...)
//                             .RespondWith(MockResponseBuilder.Create()...);
// This file uses the delegate style (a). If your installed LogicAppUnit version
// exposes the fluent style instead, swap the AddApiMocks assignments for the
// equivalent AddMockResponse(...) calls (see the wiki: "Mocking of Outgoing HTTP
// Calls"). The assertions below do not change either way.
// =============================================================================



/// <summary>
/// Sample 02 - feature toggle via AZURE APP CONFIGURATION, tested with LogicAppUnit.
///
/// The workflow makes a real HTTP GET to the App Configuration REST API. LogicAppUnit
/// redirects that call (the host is registered in testConfiguration.json under
/// externalApiUrlsToMock) to its built-in mock server, so we can return a canned
/// feature-flag document and assert how the workflow routes. This proves the parsing +
/// branching logic WITHOUT a live App Configuration store.
///
/// (A real end-to-end check against an actual store lives in the RealHost project.)
/// </summary>
[TestClass]
public class AppConfigurationToggleTests : TestBase
{
    private const string Workflow = "02-appconfiguration-toggle";

    [TestInitialize]
    public void Setup() => InitFor(Workflow);

    [TestCleanup]
    public void Cleanup() => Close();

    private static StringContent Shipment() =>
        new(JsonSerializer.Serialize(new { shipmentId = "SHP-77", destination = "Seattle" }),
            Encoding.UTF8, "application/json");

    // App Configuration returns the flag as a key-value whose "value" is a JSON string
    // describing the feature flag. enabled=true with no client filters => always on.
    private static string FeatureFlagKv(bool enabled, int? percentage = null)
    {
        var ff = percentage.HasValue
            ? $$"""{"id":"BetaShippingProvider","enabled":{{enabled.ToString().ToLower()}},"conditions":{"client_filters":[{"name":"Microsoft.Percentage","parameters":{"Value":{{percentage}}}}]}}"""
            : $$"""{"id":"BetaShippingProvider","enabled":{{enabled.ToString().ToLower()}},"conditions":{"client_filters":[]}}""";

        // The KV envelope returned by the App Configuration REST API.
        var kv = new
        {
            key = ".appconfig.featureflag/BetaShippingProvider",
            value = ff,
            content_type = "application/vnd.microsoft.appconfig.ff+json;charset=utf-8"
        };
        return JsonSerializer.Serialize(kv);
    }

    [TestMethod]
    public void FlagEnabled_NoFilter_UsesBetaProvider()
    {
        using var testRunner = CreateTestRunner();

        // Mock the App Configuration GET -> return an enabled flag with no filters.
        testRunner
            .AddApiMocks = (request) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(FeatureFlagKv(enabled: true),
                        Encoding.UTF8, "application/json")
                };
                return response;
            };

        var result = testRunner.TriggerWorkflow(Shipment(), HttpMethod.Post);
        var body = result.Content.ReadAsStringAsync().Result;

        Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.AreEqual("BETA-FastShip", doc.RootElement.GetProperty("provider").GetString());
    }

    [TestMethod]
    public void FlagDisabled_UsesStandardProvider()
    {
        using var testRunner = CreateTestRunner();

        testRunner.AddApiMocks = (request) =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(FeatureFlagKv(enabled: false),
                    Encoding.UTF8, "application/json")
            };

        var result = testRunner.TriggerWorkflow(Shipment(), HttpMethod.Post);
        var body = result.Content.ReadAsStringAsync().Result;

        Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.AreEqual("Standard-Carrier", doc.RootElement.GetProperty("provider").GetString());
    }

    [TestMethod]
    public void FlagEnabled_100PercentFilter_UsesBetaProvider()
    {
        using var testRunner = CreateTestRunner();

        testRunner.AddApiMocks = (request) =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(FeatureFlagKv(enabled: true, percentage: 100),
                    Encoding.UTF8, "application/json")
            };

        var result = testRunner.TriggerWorkflow(Shipment(), HttpMethod.Post);
        var body = result.Content.ReadAsStringAsync().Result;

        Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.AreEqual("BETA-FastShip", doc.RootElement.GetProperty("provider").GetString());
    }
}
