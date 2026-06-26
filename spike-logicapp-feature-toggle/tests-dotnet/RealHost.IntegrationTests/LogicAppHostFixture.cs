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

        // Start the real host. Corvus's FunctionProject.ResolvePath treats the first argument
        // as a single folder NAME, walks UP from the test working directory and returns the
        // first ancestor containing "<name>\bin\<debug|release>\<runtime>". The csproj copies
        // the Logic App into bin\<config>\LogicAppHost\bin\<config>\net8.0, so passing
        // "LogicAppHost" + "net8.0" resolves to that inner folder (which holds host.json and
        // the workflow folders) as the func host working directory.
        // - provider ("node") becomes the `--node` flag for `func host start`, matching the
        //   Logic Apps Standard Node worker.
        _functions.StartFunctionsInstanceAsync(
            "LogicAppHost",         // pathFragment (folder NAME, not an absolute path)
            port,
            "net8.0",               // runtime -> resolves the inner bin\<config>\net8.0
            "node",                 // provider -> `func host start --node`
            config).GetAwaiter().GetResult();

        Client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
    }

    /// <summary>POST a JSON body to a workflow's HTTP Request trigger.</summary>
    /// <remarks>
    /// Logic Apps Standard HTTP Request triggers are SAS-protected: a bare POST to
    /// <c>/api/&lt;wf&gt;/triggers/&lt;trigger&gt;/invoke</c> returns 401 Unauthorized because it
    /// lacks the <c>sig</c> signature. So we first ask the local workflow management endpoint
    /// for the signed callback URL (the func host grants this at Admin level locally, no key
    /// required) and then POST the body to that signed URL.
    /// </remarks>
    public async Task<(System.Net.HttpStatusCode Status, string Body)> InvokeAsync(
        string workflow, string trigger, object body)
    {
        var callbackUrl = await GetCallbackUrlAsync(workflow, trigger);

        var json = System.Text.Json.JsonSerializer.Serialize(body);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        // Use only the path+query against our known BaseAddress so the call always targets
        // this fixture's host/port regardless of the host name baked into the callback URL.
        var resp = await Client.PostAsync(callbackUrl.PathAndQuery, content);
        var respBody = await resp.Content.ReadAsStringAsync();
        return (resp.StatusCode, respBody);
    }

    /// <summary>
    /// Fetches the SAS-signed callback URL for a Request trigger from the local runtime's
    /// workflow management endpoint.
    /// </summary>
    private async Task<Uri> GetCallbackUrlAsync(string workflow, string trigger)
    {
        var mgmt = $"/runtime/webhooks/workflow/api/management/workflows/{workflow}" +
                   $"/triggers/{trigger}/listCallbackUrl?api-version=2022-05-01";
        using var resp = await Client.PostAsync(mgmt, content: null);
        var json = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Failed to get callback URL for '{workflow}/{trigger}' ({(int)resp.StatusCode} {resp.StatusCode}): {json}");
        }

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var value = doc.RootElement.GetProperty("value").GetString()
            ?? throw new InvalidOperationException($"Callback URL response had no 'value': {json}");
        return new Uri(value);
    }

    public void Dispose()
    {
        _functions.TeardownFunctionsAsync().GetAwaiter().GetResult();
        Client.Dispose();
        GC.SuppressFinalize(this);
    }
}
