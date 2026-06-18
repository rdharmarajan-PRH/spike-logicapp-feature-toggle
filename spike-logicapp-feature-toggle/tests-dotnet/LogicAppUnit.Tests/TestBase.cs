using LogicAppUnit;

namespace FeatureToggle.LogicAppUnit.Tests;

/// <summary>
/// Common base for all LogicAppUnit tests.
///
/// The spike's Logic App project (host.json, parameters.json, connections.json and the
/// per-workflow folders) is copied into the test build output under a "LogicApp" folder
/// by the CopyLogicApp target in the csproj. LogicAppUnit then loads the REAL
/// workflow.json and executes it through the actual Logic Apps engine, automatically
/// rewriting external HTTP / connector calls to a managed mock HTTP server. This gives
/// "real engine, mocked edges" tests that run from Visual Studio Test Explorer with no
/// Azure resources and no manual host management.
/// </summary>
public abstract class TestBase : WorkflowTestBase
{
    /// <summary>
    /// Initialise the framework for a single workflow.
    /// </summary>
    /// <param name="workflowName">The workflow folder name, e.g. "01-appsettings-toggle".</param>
    protected void InitFor(string workflowName)
    {
        // Signature: Initialize(logicAppBasePath, workflowName, [localSettingsFilename])
        // logicAppBasePath points at the copied Logic App root (the folder that holds
        // host.json). The framework finds the workflow folder beneath it.
        Initialize("LogicApp", workflowName);
    }
}
