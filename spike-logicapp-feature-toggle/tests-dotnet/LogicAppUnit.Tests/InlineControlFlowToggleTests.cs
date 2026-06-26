using System.Net;
using System.Text;
using System.Text.Json;
using LogicAppUnit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FeatureToggle.LogicAppUnit.Tests;

/// <summary>
/// Sample 04 - feature toggle via INLINE CONTROL-FLOW (Switch routing, canary split,
/// mock branch), tested with LogicAppUnit.
/// </summary>
[TestClass]
public class InlineControlFlowToggleTests : TestBase
{
    private const string Workflow = "04-inline-controlflow-toggle";

    // InitFor per-test (Initialize stores per-instance state); Close() once per class
    // (it disposes a shared static HttpClient, so must not run after every test).
    [TestInitialize]
    public void Setup() => InitFor(Workflow);

    [ClassCleanup]
    public static void Teardown() => Close();

    private static StringContent Fulfillment(string id, bool? useMock = null)
    {
        object payload = useMock.HasValue
            ? new { orderId = id, useMock = useMock.Value }
            : new { orderId = id };
        return new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
    }

    [TestMethod]
    public void Routing_Legacy_GoesToLegacy()
    {
        using var testRunner = CreateTestRunner(new Dictionary<string, string>
        {
            { "Routing_FulfillmentVersion", "legacy" }
        });

        var response = testRunner.TriggerWorkflow(Fulfillment("ORD-2002"), HttpMethod.Post);
        var body = response.Content.ReadAsStringAsync().Result;

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.AreEqual("fulfillment-legacy", doc.RootElement.GetProperty("handledBy").GetString());
    }

    [TestMethod]
    public void Routing_V2_GoesToV2()
    {
        using var testRunner = CreateTestRunner(new Dictionary<string, string>
        {
            { "Routing_FulfillmentVersion", "v2" }
        });

        var response = testRunner.TriggerWorkflow(Fulfillment("ORD-2002"), HttpMethod.Post);
        var body = response.Content.ReadAsStringAsync().Result;

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.AreEqual("fulfillment-v2", doc.RootElement.GetProperty("handledBy").GetString());
    }

    [TestMethod]
    public void Routing_Unknown_ReturnsBadRequest()
    {
        using var testRunner = CreateTestRunner(new Dictionary<string, string>
        {
            { "Routing_FulfillmentVersion", "bogus" }
        });

        var response = testRunner.TriggerWorkflow(Fulfillment("ORD-2002"), HttpMethod.Post);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public void MockBranch_ShortCircuits()
    {
        using var testRunner = CreateTestRunner(new Dictionary<string, string>
        {
            { "Routing_FulfillmentVersion", "v2" }
        });

        var response = testRunner.TriggerWorkflow(Fulfillment("ORD-2003", useMock: true), HttpMethod.Post);
        var body = response.Content.ReadAsStringAsync().Result;

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.AreEqual("MOCK", doc.RootElement.GetProperty("handledBy").GetString());
    }

    [TestMethod]
    public void Canary_100Percent_AlwaysCanary()
    {
        using var testRunner = CreateTestRunner(new Dictionary<string, string>
        {
            { "Routing_FulfillmentVersion", "canary" },
            { "Routing_CanaryPercentage", "100" }
        });

        var response = testRunner.TriggerWorkflow(Fulfillment("ORD-2004"), HttpMethod.Post);
        var body = response.Content.ReadAsStringAsync().Result;

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.AreEqual("fulfillment-v2", doc.RootElement.GetProperty("handledBy").GetString());
        Assert.AreEqual("canary", doc.RootElement.GetProperty("cohort").GetString());
    }

    [TestMethod]
    public void Canary_0Percent_AlwaysControl()
    {
        using var testRunner = CreateTestRunner(new Dictionary<string, string>
        {
            { "Routing_FulfillmentVersion", "canary" },
            { "Routing_CanaryPercentage", "0" }
        });

        var response = testRunner.TriggerWorkflow(Fulfillment("ORD-2005"), HttpMethod.Post);
        var body = response.Content.ReadAsStringAsync().Result;

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.AreEqual("fulfillment-legacy", doc.RootElement.GetProperty("handledBy").GetString());
        Assert.AreEqual("control", doc.RootElement.GetProperty("cohort").GetString());
    }
}
