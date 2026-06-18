using System.Reflection;
using Corvus.Testing.AzureFunctions;
using Microsoft.Extensions.Logging;

namespace FeatureToggle.RealHost.IntegrationTests;

/// <summary>
/// Boots a REAL Logic Apps Standard host (via `func host start`, started by Corvus's
/// FunctionsController) with a specific set of feature-flag app settings, and exposes an
/// HttpClient for making real HTTP calls to the workflows.
///
/// Because Logic Apps Standard reads app settings at startup, each distinct flag
/// combination needs its own host instance. Derive a fixture per flag-set (see the
/// *Fixture classes below) and share it across a test class with xUnit's IClassFixture.
///
/// Prerequisites to actually RUN these: Azure Functions Core Tools v4 on PATH, Azurite
/// running locally (AzureWebJobsStorage=UseDevelopmentStorage=true), and network access
/// to download the Logic Apps extension bundle on first run.
/// </summary>
public abstract class LogicAppHostFixture : IDisposable
{
    private readonly FunctionsController _functions;
    public HttpClient Client { get; }
    public int Port { get; }

    protected LogicAppHostFixture(IDictionary<string, string> featureFlags, int port = 7075)
    {
        Port = port;

        using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        var logger = loggerFactory.CreateLogger<LogicAppHostFixture>();
        _functions = new FunctionsController(logger);

        var config = new FunctionConfiguration();
        // Baseline settings the runtime needs, then overlay the test's feature flags.
        config.EnvironmentVariables.Add("AzureWebJobsStorage", "UseDevelopmentStorage=true");
        config.EnvironmentVariables.Add("FUNCTIONS_WORKER_RUNTIME", "node");
        config.EnvironmentVariables.Add("WORKFLOWS_SUBSCRIPTION_ID", "");
        foreach (var kv in featureFlags)
        {
            config.EnvironmentVariables[kv.Key] = kv.Value;
        }

        // Start the real host pointing at the copied Logic App project in the output dir.
        // - runtime ("net8.0") is used by Corvus only for project-path resolution; our
        //   copied LogicApp folder has no TFM subfolder so the path is used as-is.
        // - provider ("node") becomes the `--node` flag for `func host start`, matching
        //   the Logic Apps Standard Node worker.
        _functions.StartFunctionsInstanceAsync(
            LogicAppProjectPath(),
            port,
            "net8.0",               // runtime (for path resolution)
            "node",                 // provider -> `func host start --node`
            config).GetAwaiter().GetResult();

        Client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
    }

    /// <summary>POST a JSON body to a workflow's HTTP Request trigger.</summary>
    public async Task<(System.Net.HttpStatusCode Status, string Body)> InvokeAsync(
        string workflow, string trigger, object body)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(body);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var uri = $"/api/{workflow}/triggers/{trigger}/invoke?api-version=2022-05-01";
        var resp = await Client.PostAsync(uri, content);
        var respBody = await resp.Content.ReadAsStringAsync();
        return (resp.StatusCode, respBody);
    }

    private static string LogicAppProjectPath()
    {
        // The csproj copies the Logic App into <output>\LogicApp.
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        return Path.Combine(asmDir, "LogicApp");
    }

    public void Dispose()
    {
        _functions.TeardownFunctionsAsync().GetAwaiter().GetResult();
        Client.Dispose();
        GC.SuppressFinalize(this);
    }
}
