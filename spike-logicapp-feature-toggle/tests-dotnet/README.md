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

## How these tests actually work (plain-English, for the team)

**The one-sentence version:** Logic Apps have no built-in test capability, so these tests
start a real (mini) copy of the Logic App on your own machine, send it test requests, and
check it behaves correctly — which means your machine needs a small Microsoft tool called
**Azure Functions Core Tools** (the `func` command) installed first.

**The analogy:** think of a workflow as a *recipe* and `func` as the *oven*.

- The tests don't just *read* the recipe — they actually **cook the dish in a real oven and
  taste it** to confirm it came out right.
- If there's no oven plugged in, the test can't run. That's the whole story behind the
  error we saw: *"PATH does not include the path for the 'func' executable"* simply means
  **"no oven is plugged in"** on that machine. It is a one-time machine-setup step, not a
  bug in our code or our tests.

**Why there are two test projects (same dish, different kitchens):**

- **LogicAppUnit** cooks the real dish but uses **fake ingredients for anything that would
  reach outside** (e.g. a call to Azure App Configuration is faked). Fast, self-contained,
  great for everyday development and CI. ("Real oven, fake outside ingredients.")
- **Real-host** cooks the dish with **real ingredients and real outside calls**. Slower and
  needs more setup, but it's the closest thing to production. ("Real oven, real
  ingredients.")

**What a passing test proves:** when we flip a feature flag (a simple on/off switch), the
workflow really does change its behavior — e.g. flag ON applies the new pricing engine,
flag OFF uses the old one, the kill-switch returns "service paused", and so on. That's the
whole point of the spike: showing feature toggles work, and proving it automatically.

**What a developer needs once, to run them:** install `func` and Azurite (a local stand-in
for Azure storage), restart the IDE, and run the tests. Step-by-step commands are in the
next section.

---

## ⚠️ Setup prerequisites (read this first)

**Both** suites shell out to **Azure Functions Core Tools** (`func`) to run the Logic Apps
runtime. The most common failure is:

> `LogicAppUnit.TestException: The environment variable PATH does not include the path for the 'func' executable.`

Fix it once:

```powershell
# 1. Install Core Tools v4 (or: winget install Microsoft.Azure.FunctionsCoreTools)
npm install -g azure-functions-core-tools@4 --unsafe-perm true

# 2. Install Azurite (local storage emulator the runtime needs)
npm install -g azurite

# 3. RESTART your IDE so it picks up the updated PATH, then verify:
func --version      # expect 4.x
```

Before running tests, **start Azurite** in a terminal (leave it running):

```powershell
azurite --silent
```

> The very first test run also downloads the Logic Apps extension bundle, so it is slower
> and needs internet access.

### Gotcha: `npm`-installed Core Tools puts `func.cmd` on PATH, not `func.exe`

LogicAppUnit searches PATH specifically for **`func.exe`**. A global **npm** install
(`npm install -g azure-functions-core-tools@4`) only puts the shims `func`, `func.cmd` and
`func.ps1` on PATH — the real `func.exe` lives one level down in
`…\npm\node_modules\azure-functions-core-tools\bin\func.exe`. So `func --version` works in a
shell, yet LogicAppUnit still throws *"PATH does not include the path for the 'func'
executable"*. Fix by putting the folder that actually contains `func.exe` on PATH, e.g.:

```powershell
# add the Core Tools bin folder (the one holding func.exe) to PATH for the session
$env:PATH = "$env:APPDATA\npm\node_modules\azure-functions-core-tools\bin;$env:PATH"
```

…or install Core Tools via the MSI/winget package (`winget install
Microsoft.Azure.FunctionsCoreTools`), which places `func.exe` directly on PATH. Restart your
IDE afterwards so it inherits the change. (Corvus's real-host suite is more forgiving here —
it runs `where func` and accepts the `func.cmd` shim — but keeping `func.exe` on PATH makes
both suites work the same way.)

---

## LogicAppUnit.Tests

Uses the [LogicAppUnit](https://github.com/LogicAppUnit/TestingFramework) NuGet package
(v1.12.0, MSTest-based). It loads each workflow, runs it through the actual engine, and
rewrites any outbound calls to a built-in mock server. Feature flags are injected
per-test by overriding local settings — **no host restart and no Azure cloud resources**. (It still launches the workflow runtime locally, so it needs `func` on PATH; see Prerequisites.)

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

- Both suites have now been **compiled and executed** against the real runtime and pass
  (LogicAppUnit 14/14, real-host 8/8) on .NET 8 with Core Tools v4 + Azurite. Getting there
  required three runtime-accuracy fixes to the Logic App itself: app-setting-sourced
  parameters in `parameters.json` must be typed **`String`** (the file allows only
  `@appsetting()`, which returns a string) and coerced with `bool()`/`int()` in the
  workflow; and an `InitializeVariable` cannot be nested inside a `Switch`/`If` (sample 04's
  canary roll is now evaluated inline). Expect to run `dotnet restore` first; pin any
  package versions your org requires.
- `local.settings.json` is generated into the build output from the example and is
  git-ignored.
- Why two suites: LogicAppUnit can't prove the *real* network call to App Configuration
  works (it mocks it); the real-host suite can't cheaply assert deep action-level state
  the way LogicAppUnit can. Together they cover both.
