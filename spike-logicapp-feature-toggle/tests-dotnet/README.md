# Feature-toggle tests (C#, runnable from Visual Studio)

Two complementary test projects, both opened via `tests-dotnet/FeatureToggleTests.sln`
and run from **Visual Studio Test Explorer** (or `dotnet test`):

| Project | Style | What's real | What's mocked | Speed |
|---------|-------|-------------|---------------|-------|
| **LogicAppUnit.Tests** | "real engine, mocked edges" | The Logic Apps engine runs the real `workflow.json` | External HTTP / connector calls (mock server) | fast |
| **RealHost.IntegrationTests** | true integration | A real `func host` is started; real HTTP calls | nothing (hits the running host) | slow |

Both prove the same thing — that flipping a feature flag changes workflow behavior — at
different levels of fidelity. Use LogicAppUnit for fast inner-loop and CI; use the
real-host suite when you want end-to-end confidence.

---

## LogicAppUnit.Tests

Uses the [LogicAppUnit](https://github.com/LogicAppUnit/TestingFramework) NuGet package
(v1.12.0, MSTest-based). It loads each workflow, runs it through the actual engine, and
rewrites any outbound calls to a built-in mock server. Feature flags are injected
per-test by overriding local settings — **no host restart, no Azure, no Azurite setup**.

```csharp
using var testRunner = CreateTestRunner(new Dictionary<string, string>
{
    { "FeatureFlag_NewPricingEngine", "true" }
});
var response = testRunner.TriggerWorkflow(order, HttpMethod.Post);
// assert engine == "new-pricing-engine-v2", total == 180, etc.
```

Covers samples **01** (app settings), **03** (parameters), **04** (inline control-flow),
and **02** (App Configuration) by mocking the App Config REST response and asserting the
routing/percentage logic.

> **Mock-API note:** the App Configuration test uses LogicAppUnit's delegate-style mock
> (`testRunner.AddApiMocks = ...`). If your installed version exposes the fluent
> `AddMockResponse(...)` API instead, swap those two assignments (the file has a comment
> explaining this). Assertions are unchanged.

**Prerequisites:** .NET 8 SDK, and Azure Functions Core Tools v4 on PATH (LogicAppUnit
launches the workflow runtime under the hood). The csproj copies the spike's Logic App
(host.json, parameters.json, connections.json and each workflow folder) into the test
output under `LogicApp\`.

**Run:**

```bash
dotnet test tests-dotnet/LogicAppUnit.Tests
```

---

## RealHost.IntegrationTests

Uses [Corvus.Testing.AzureFunctions](https://www.nuget.org/packages/Corvus.Testing.AzureFunctions)
(v5.0.1, xUnit). `FunctionsController` starts a **real `func host start`** from C#, then
the tests make **real HTTP POSTs** to the workflows. Because Logic Apps Standard reads
app settings at startup, each flag combination gets its own host instance (one xUnit
fixture per flag-set, each on its own port).

```csharp
public sealed class NewPricingOnFixture : LogicAppHostFixture
{
    public NewPricingOnFixture() : base(new() {
        ["FeatureFlag_NewPricingEngine"] = "true"
    }, port: 7081) { }
}

public class AppSettings_NewPricingOn(NewPricingOnFixture fx)
    : IClassFixture<NewPricingOnFixture>
{
    [Fact] public async Task Applies_new_engine_and_promo() { ... }
}
```

**Prerequisites:** .NET 8 SDK, Azure Functions Core Tools v4 on PATH, **Azurite running**
(`AzureWebJobsStorage=UseDevelopmentStorage=true`), and internet on first run (to fetch
the Logic Apps extension bundle). Start Azurite, then:

```bash
dotnet test tests-dotnet/RealHost.IntegrationTests
```

> These tests start one runtime per fixture, so the suite takes noticeably longer than
> LogicAppUnit and needs free ports 7081–7087.

---

## Scenario matrix (both suites)

| Sample | Scenario | Flags | Expected |
|--------|----------|-------|----------|
| 01 | New pricing ON | `FeatureFlag_NewPricingEngine=true` | `engine=new-pricing-engine-v2`, total 200→180 |
| 01 | New pricing OFF | `=false` | `engine=legacy-pricing-engine`, total 200 |
| 01 | Kill switch ON | `FeatureFlag_GlobalKillSwitch=true` | 503, `status=disabled` |
| 02 | App Config flag enabled | mocked flag `enabled=true` | `provider=BETA-FastShip` |
| 02 | App Config flag disabled | mocked flag `enabled=false` | `provider=Standard-Carrier` |
| 03 | Email ON | `FeatureFlag_SendEmailNotifications=true` | `emailSent=true` |
| 03 | Email OFF | `=false` | `emailSent=false` |
| 04 | Route v2 | `Routing_FulfillmentVersion=v2` | `handledBy=fulfillment-v2` |
| 04 | Route unknown | `=bogus` | 400 |
| 04 | Mock branch | body `useMock=true` | `handledBy=MOCK` |
| 04 | Canary 100% | `Routing_CanaryPercentage=100` | `cohort=canary` |
| 04 | Canary 0% | `=0` | `cohort=control` |

(The real-host suite covers the offline-runnable subset; sample 02's *true* end-to-end
path needs a live App Configuration store + managed identity, as described in the main
project README.)

---

## Notes & honesty

- These projects were authored and **statically verified** (syntax, project/JSON
  validity, assertions cross-checked against each workflow's responses) but **not compiled
  or executed here** — the authoring environment has no .NET SDK or `func`. Expect to run
  `dotnet restore` first; pin any package versions your org requires.
- `local.settings.json` is generated into the build output from the example and is
  git-ignored.
- Why two suites: LogicAppUnit can't prove the *real* network call to App Configuration
  works (it mocks it); the real-host suite can't cheaply assert deep action-level state
  the way LogicAppUnit can. Together they cover both.
