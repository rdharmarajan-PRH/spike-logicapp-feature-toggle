using LogicAppUnit;

namespace FeatureToggle.LogicAppUnit.Tests;

/// <summary>
/// Common base for all LogicAppUnit tests.
///
/// The spike's Logic App project (host.json, parameters.json, connections.json and the
/// per-workflow folders) is copied into the test build output under a "LogicApp" folder
/// by the CopyLogicApp target in the csproj. LogicAppUnit then loads the REAL
/// workflow.json and executes it through the actual Logic Apps engine, automatically
/// rewriting external HTTP / connector calls to a managed mock HTTP server.
///
/// Lifecycle note: LogicAppUnit's Initialize() reads the workflow files once, and Close()
/// disposes a SHARED static HttpClient. So Initialize is called once per test class via
/// [ClassInitialize] and Close once via [ClassCleanup] — NOT per test (calling Close after
/// every test would dispose the shared client and break later classes). Feature-flag values
/// are injected per test through CreateTestRunner(overrides), which is the per-test step.
/// </summary>
public abstract class TestBase : WorkflowTestBase
{
    /// <summary>
    /// Initialise the framework for a single workflow. Call from a [ClassInitialize] method.
    /// </summary>
    /// <param name="workflowName">The workflow folder name, e.g. "01-appsettings-toggle".</param>
    protected void InitFor(string workflowName)
    {
        // Signature: Initialize(logicAppBasePath, workflowName, [localSettingsFilename]).
        // logicAppBasePath points at the copied Logic App root (the folder that holds
        // host.json). The framework finds the "<workflowName>/workflow.json" beneath it.
        Initialize("LogicApp", workflowName);
    }
}
