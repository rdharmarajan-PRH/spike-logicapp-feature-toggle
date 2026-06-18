using System.Net;
using System.Text;
using System.Text.Json;
using LogicAppUnit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FeatureToggle.LogicAppUnit.Tests;

/// <summary>
/// Sample 03 - feature toggle via the PARAMETERS FILE, tested with LogicAppUnit.
/// The boolean flag flows from app settings through parameters.json; we override the
/// app setting per-test and assert the email branch.
/// </summary>
[TestClass]
public class ParametersFileToggleTests : TestBase
{
    private const string Workflow = "03-parameters-file-toggle";

    [TestInitialize]
    public void Setup() => InitFor(Workflow);

    [TestCleanup]
    public void Cleanup() => Close();

    private static StringContent Invoice() =>
        new(JsonSerializer.Serialize(new { invoiceId = "INV-555", customerEmail = "test@example.com" }),
            Encoding.UTF8, "application/json");

    [TestMethod]
    public void EmailNotifications_On_SendsEmail()
    {
        using var testRunner = CreateTestRunner(new Dictionary<string, string>
        {
            { "FeatureFlag_SendEmailNotifications", "true" }
        });

        var response = testRunner.TriggerWorkflow(Invoice(), HttpMethod.Post);
        var body = response.Content.ReadAsStringAsync().Result;

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(WorkflowRunStatus.Succeeded, testRunner.WorkflowRunStatus);
        using var doc = JsonDocument.Parse(body);
        Assert.IsTrue(doc.RootElement.GetProperty("emailSent").GetBoolean());
        Assert.AreEqual("parameters.json -> app settings",
            doc.RootElement.GetProperty("flagSource").GetString());
    }

    [TestMethod]
    public void EmailNotifications_Off_DoesNotSendEmail()
    {
        using var testRunner = CreateTestRunner(new Dictionary<string, string>
        {
            { "FeatureFlag_SendEmailNotifications", "false" }
        });

        var response = testRunner.TriggerWorkflow(Invoice(), HttpMethod.Post);
        var body = response.Content.ReadAsStringAsync().Result;

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.IsFalse(doc.RootElement.GetProperty("emailSent").GetBoolean());
    }
}
