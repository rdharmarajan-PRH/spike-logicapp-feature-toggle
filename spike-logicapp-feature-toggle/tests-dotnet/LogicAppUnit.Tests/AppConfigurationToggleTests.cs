using System.Net;
using System.Text;
using System.Text.Json;
using LogicAppUnit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FeatureToggle.LogicAppUnit.Tests;

// The workflow makes ONE outbound HTTP GET to Azure App Configuration. We intercept it
// with LogicAppUnit's delegate mock (ITestRunner.AddApiMocks, available in 1.12.0) and
// return a canned feature-flag document. The framework redirects the call to its mock
// server because the App Config host is listed in testConfiguration.json
// (externalApiUrlsToMock) and strips the ManagedServiceIdentity auth automatically.

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

    // InitFor per-test (Initialize stores per-instance state); Close() once per class
    // (it disposes a shared static HttpClient, so must not run after every test).
    [TestInitialize]
    public void Setup() => InitFor(Workflow);

    [ClassCleanup]
    public static void Teardown() => Close();

    private static StringContent Shipment() =>
        new(JsonSerializer.Serialize(new { shipmentId = "SHP-77", destination = "Seattle" }),
            Encoding.UTF8, "application/json");

    // App Configuration returns the flag as a key-value whose "value" is a JSON STRING
    // (itself JSON) describing the feature flag. We build it with objects + JsonSerializer
    // to avoid any hand-written brace escaping. enabled=true with no client filters => on.
    private static string FeatureFlagKv(bool enabled, int? percentage = null)
    {
        object conditions = percentage.HasValue
            ? new
            {
                client_filters = new[]
                {
                    new
                    {
                        name = "Microsoft.Percentage",
                        parameters = new Dictionary<string, object> { ["Value"] = percentage.Value }
                    }
                }
            }
            : new { client_filters = Array.Empty<object>() };

        var featureFlag = new
        {
            id = "BetaShippingProvider",
            enabled,
            conditions
        };

        // The KV envelope returned by the App Configuration REST API. The "value" field
        // is the feature flag serialized as a JSON string.
        var kv = new
        {
            key = ".appconfig.featureflag/BetaShippingProvider",
            value = JsonSerializer.Serialize(featureFlag),
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
