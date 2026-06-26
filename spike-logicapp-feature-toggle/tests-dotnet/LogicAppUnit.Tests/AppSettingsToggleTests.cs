using System.Net;
using System.Text;
using System.Text.Json;
using LogicAppUnit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FeatureToggle.LogicAppUnit.Tests;

/// <summary>
/// Sample 01 - feature toggle via APP SETTINGS, tested with LogicAppUnit.
/// Flag values are injected per-test via CreateTestRunner(overrides), so the real engine
/// evaluates the real Condition actions. No host restart, no Azure.
/// </summary>
[TestClass]
public class AppSettingsToggleTests : TestBase
{
    private const string Workflow = "01-appsettings-toggle";

    // Initialize() stores per-instance workflow state, and MSTest creates a fresh instance
    // for each test, so initialise per-test in [TestInitialize]. Close() disposes a shared
    // static HttpClient, so call it ONCE in [ClassCleanup], never per test.
    [TestInitialize]
    public void Setup() => InitFor(Workflow);

    [ClassCleanup]
    public static void Teardown() => Close();

    private static StringContent Order(string id, decimal amount) =>
        new(JsonSerializer.Serialize(new { orderId = id, amount }), Encoding.UTF8, "application/json");

    [TestMethod]
    public void NewPricingEngine_On_AppliesPromo()
    {
        using var testRunner = CreateTestRunner(new Dictionary<string, string>
        {
            { "FeatureFlag_GlobalKillSwitch", "false" },
            { "FeatureFlag_NewPricingEngine", "true" }
        });

        var response = testRunner.TriggerWorkflow(Order("ORD-1001", 200), HttpMethod.Post);
        var body = response.Content.ReadAsStringAsync().Result;

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(WorkflowRunStatus.Succeeded, testRunner.WorkflowRunStatus);
        using var doc = JsonDocument.Parse(body);
        Assert.AreEqual("new-pricing-engine-v2", doc.RootElement.GetProperty("engine").GetString());
        Assert.AreEqual(180m, doc.RootElement.GetProperty("computedTotal").GetDecimal());
    }

    [TestMethod]
    public void NewPricingEngine_Off_UsesLegacy()
    {
        using var testRunner = CreateTestRunner(new Dictionary<string, string>
        {
            { "FeatureFlag_GlobalKillSwitch", "false" },
            { "FeatureFlag_NewPricingEngine", "false" }
        });

        var response = testRunner.TriggerWorkflow(Order("ORD-1001", 200), HttpMethod.Post);
        var body = response.Content.ReadAsStringAsync().Result;

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.AreEqual("legacy-pricing-engine", doc.RootElement.GetProperty("engine").GetString());
        Assert.AreEqual(200m, doc.RootElement.GetProperty("computedTotal").GetDecimal());
    }

    [TestMethod]
    public void GlobalKillSwitch_On_ReturnsServiceUnavailable()
    {
        using var testRunner = CreateTestRunner(new Dictionary<string, string>
        {
            { "FeatureFlag_GlobalKillSwitch", "true" }
        });

        var response = testRunner.TriggerWorkflow(Order("ORD-1001", 200), HttpMethod.Post);
        var body = response.Content.ReadAsStringAsync().Result;

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.AreEqual("disabled", doc.RootElement.GetProperty("status").GetString());
    }
}
