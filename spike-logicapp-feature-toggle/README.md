# Logic Apps Standard — Feature Toggle Spike

A code spike demonstrating **four different ways to do feature toggling in Azure Logic Apps (Standard)**, with one self-contained workflow sample per approach. Every workflow uses an HTTP `Request` trigger and `Response`/`Compose` actions, so the toggling behavior is the focus and **no managed connections are needed to run the core demos**.

| # | Approach | Workflow | Toggle source | Flip without redeploy? |
|---|----------|----------|---------------|------------------------|
| 01 | **App Settings** (`appsetting()`) | `01-appsettings-toggle/` | App settings / env vars | Restart needed |
| 02 | **Azure App Configuration** | `02-appconfiguration-toggle/` | Central App Config store (REST) | **Yes — instant** |
| 03 | **Parameters file** (`parameters.json`) | `03-parameters-file-toggle/` | `parameters.json` → app settings | Restart needed |
| 04 | **Inline control-flow** | `04-inline-controlflow-toggle/` | Switch / Condition / canary split | Depends on driver |

---

## Project layout

```
spike-logicapp-feature-toggle/
├── FeatureToggleSpike.sln          # Root solution — open in Visual Studio / Rider
├── host.json                       # Functions/Logic Apps host config (extension bundle)
├── connections.json                # Empty — no connections required for the demos
├── parameters.json                 # Shared workflow parameters (used by sample 03)
├── local.settings.json.example     # Copy to local.settings.json to run locally
├── .gitignore                      # Logic Apps / Functions / IDE / .NET ignores
├── .funcignore                     # Files excluded from deployment
├── .vscode/                        # Recommended extensions + run settings
│
│   # Each workflow lives in its OWN folder at the project root (Logic Apps
│   # Standard convention — a sibling of host.json, NOT under a workflows/ folder).
├── 01-appsettings-toggle/workflow.json
├── 02-appconfiguration-toggle/workflow.json
├── 03-parameters-file-toggle/workflow.json
├── 04-inline-controlflow-toggle/workflow.json
│
├── tests-dotnet/                   # C# tests (runnable from Visual Studio / Rider)
│   ├── FeatureToggleTests.sln      #   tests-only solution
│   ├── LogicAppUnit.Tests/         #   real engine, mocked edges (fast)
│   └── RealHost.IntegrationTests/  #   true integration via a real func host (Corvus)
│
└── test/
    └── sample-requests.http        # Ready-to-send sample POST bodies (REST Client)
```

> The old `workflows/` subfolder is obsolete (workflows now sit at the root). If it is
> still present in your checkout, delete it.

---

## Feature toggling in plain terms (for the team)

**What a feature toggle is:** a switch in configuration that turns a piece of behavior on
or off **without changing code**. Flip the switch and the app behaves differently. Teams
use toggles to release features gradually, to turn something off instantly in an emergency
(a "kill switch"), and to make the *same* build behave differently in dev, test and prod.

This spike shows **four ways** to do that in Logic Apps Standard. They differ mainly in
*where the switch lives* and *how fast you can flip it*:

**1. App Settings — the simple on/off switch.**
The switch is a plain value in the app's configuration. Easiest to understand, needs no
extra tools. *Trade-off:* changing it in Azure restarts the app, and it's only true/false
(no "roll out to 10% of users"). Best for a few switches that rarely change.

**2. Azure App Configuration — the central switchboard.**
The switches live in a shared Azure service that many apps can read. The big win: you can
**flip a switch instantly without redeploying or restarting**, and it supports gradual
rollout (e.g. "turn this on for 20% of traffic"). *Trade-off:* it's an extra Azure resource
to set up and pay for. Best when flags change often or are shared across services.

**3. Parameters file — the tidy settings file.**
Switches (and richer settings, not just on/off) live in one organized config file that
ships with the app. Good when you want a single, typed place for per-environment settings.
*Trade-off:* still needs a restart/redeploy to change. Best for shared, structured config.

**4. Inline control-flow — switching built into the workflow.**
The decision logic lives *inside the workflow itself* — route to "version 1" vs "version
2", send a slice of traffic down a new path, or flip into a test/mock mode. Most flexible
for staging a migration or A/B-style rollouts. *Trade-off:* changing the routing shape
means editing and redeploying the workflow.

**Rule of thumb:** start with **App Settings** for simple per-environment switches; move to
**App Configuration** when you need instant changes, sharing, or percentage rollouts; use
**Parameters** for tidy structured config; reach for **inline control-flow** when you're
routing between two implementations or dark-launching.

The same four are described in implementation detail below.

---

## The four approaches (implementation detail)

### 01 — App Settings (the simplest, most common pattern)

Read a flag straight from app settings with the **`appsetting('KEY')`** function and branch with a `Condition`.

```jsonc
"value": "@bool(appsetting('FeatureFlag_NewPricingEngine'))"
```

The sample also demonstrates a **global kill switch** (`FeatureFlag_GlobalKillSwitch`): if on, the workflow short-circuits with `503` before doing any work.

- **Pros:** zero dependencies, trivial to understand, works locally and in Azure identically.
- **Cons:** app settings are read at runtime but changing them in Azure **restarts the app**; not ideal for high-frequency flag changes; no targeting/percentage rollout out of the box.
- **Use when:** you have a small number of boolean flags that change rarely and per-environment.

### 02 — Azure App Configuration (centralized, change-without-redeploy)

Fetch a feature flag at runtime from a central **Azure App Configuration** store via its REST API, authenticated with the Logic App's **managed identity**. The sample parses App Configuration's native feature-flag JSON schema, including a **`Microsoft.Percentage`** client filter, so you get percentage rollout.

Flags in App Configuration are stored under the reserved key prefix `.appconfig.featureflag/<Name>` with content type `application/vnd.microsoft.appconfig.ff+json`. The workflow GETs `.../kv/.appconfig.featureflag%2FBetaShippingProvider`.

- **Pros:** flip flags **instantly without redeploy or restart**; central management across many apps; built-in targeting, percentage, and time-window filters; full audit history.
- **Cons:** extra Azure resource and cost; needs managed identity + the **App Configuration Data Reader** role; one HTTP call per evaluation (cache if hot).
- **Use when:** flags change often, you want one source of truth across services, or you need gradual/percentage rollouts.

**Setup required to run this one for real:**
1. Create an App Configuration store and a feature flag named `BetaShippingProvider`.
2. Enable a **system-assigned managed identity** on the Logic App.
3. Grant that identity the **App Configuration Data Reader** role on the store.
4. Set `AppConfig_Endpoint` in app settings to `https://<your-store>.azconfig.io`.

### 03 — Parameters file (`parameters.json`)

`parameters.json` holds workflow parameters that are referenced with **`@parameters('name')`**. Two flavors are shown:

- A **boolean flag** whose value flows from app settings *through* a parameter. Inside
  `parameters.json` the **only** expression function allowed is `@appsetting(...)`, and it
  always returns a **string** — so the parameter is declared **`String`** (declaring it
  `Bool`/`Int` makes the runtime reject the file at startup with *"the provided value …
  is not valid"*), and the workflow coerces it with `bool(...)` / `int(...)` where a real
  boolean or number is needed:
  ```jsonc
  "FeatureFlag_SendEmailNotifications": { "type": "String", "value": "@appsetting('FeatureFlag_SendEmailNotifications')" }
  ```
- A **structured `Object` parameter** (`PricingConfig`) holding richer per-environment config (rounding mode, discount tiers) so you don't scatter many individual flags.

**Where does a parameter's value actually come from?** This trips people up, so it is worth
being explicit. A parameter in `parameters.json` gets its value from one of *two* places:

1. **From an app setting** — the parameter is just a *pointer*; the real value lives in app
   settings (`local.settings.json` locally, or App Service application settings in Azure).
   You can tell because the `value` is an `@appsetting(...)` expression:
   ```jsonc
   "FeatureFlag_SendEmailNotifications": {
     "type": "String",                                             // @appsetting() returns a string
     "value": "@appsetting('FeatureFlag_SendEmailNotifications')"   // value supplied in app settings
   }
   ```

2. **Inline in `parameters.json` itself** — the value is written directly in the file, with
   **no** `@appsetting(...)`. This is how `PricingConfig` is supplied — the whole object
   literal *is* the value, and it ships inside the deployable artifact:
   ```jsonc
   "PricingConfig": {
     "type": "Object",
     "value": {                                                    // value supplied right here
       "newEngine":    { "roundingMode": "bankers", "taxInclusive": true,  "discountTiers": [0,5,10,15] },
       "legacyEngine": { "roundingMode": "half-up", "taxInclusive": false, "discountTiers": [0,5] }
     }
   }
   ```

The workflow then reads it the same way regardless of source — `@parameters('PricingConfig')`
returns the whole object, and the sample picks a sub-block based on the boolean flag
(coerced from its string parameter with `bool(...)`):
```jsonc
"@if(bool(parameters('FeatureFlag_NewPricingEngine')),
     parameters('PricingConfig')['newEngine'],
     parameters('PricingConfig')['legacyEngine'])"
```

> **Per-environment caveat:** because inline values (like `PricingConfig`) live in the
> shipped artifact, changing them needs a **redeploy** — they cannot vary per environment on
> their own. If you need that, store the JSON as an app-setting string instead and parse it:
> `@json(appsetting('PricingConfig'))`. Then the *value* comes from app settings (point 1)
> while still being a structured object.

- **Pros:** one tidy place to manage config; supports rich objects, not just booleans; the *same artifact* deploys to every environment and behaves differently based on settings.
- **Cons:** values sourced from app settings still require a restart to change; inline values in `parameters.json` are part of the deployable artifact, so changing them needs a deploy.
- **Use when:** you want centralized, typed, possibly-structured config shared across all workflows in the app.

### 04 — Inline control-flow

Toggling done entirely *inside* the workflow with control-flow actions. Three patterns:

1. **Switch-based version routing** on `Routing_FulfillmentVersion` (`legacy` | `v2` | `canary`) — classic strangler-fig / blue-green style routing.
2. **Canary percentage split** — `@less(rand(0,100), int(parameters('Routing_CanaryPercentage')))` sends a configurable slice of traffic to v2. (The roll is evaluated inline in the canary branch's `If` condition because Logic Apps Standard does not allow an `InitializeVariable` nested inside a `Switch`/`If`; `int(...)` coerces the string parameter.)
3. **Mock / dummy branch** — caller passes `useMock: true` to get a canned response and skip real side effects (great for load tests).

- **Pros:** no external dependency; expressive (multi-way routing, cohorts, dark launches); visible in the designer.
- **Cons:** logic lives in the workflow, so changing the *shape* of routing needs a deploy; `rand()` gives a per-run split, not sticky per-user assignment.
- **Use when:** staging a migration between implementations, dark-launching, or you need branching richer than a single boolean.

---

## Running locally (VS Code)

**Prerequisites:** [VS Code](https://code.visualstudio.com/), the **Azure Logic Apps (Standard)** extension, **Azure Functions Core Tools v4**, and **Azurite** (local storage emulator). All are in `.vscode/extensions.json`.

1. Copy the settings template:
   ```bash
   cp local.settings.json.example local.settings.json
   ```
2. Start **Azurite** (Command Palette → *Azurite: Start*).
3. Press **F5** (or *Run → Start Debugging*) to launch the Logic Apps runtime on `http://localhost:7071`.
4. In the **Workflows** view, right-click a workflow → **Overview** to copy its callback URL, or use the bodies in `test/sample-requests.http`.

## Automated tests (C#, Visual Studio / Rider)

Open **`FeatureToggleSpike.sln`** at the repo root in Visual Studio or Rider — it includes
both test projects plus a solution folder exposing the workflows and config files. (A
tests-only `tests-dotnet/FeatureToggleTests.sln` is also available.) Run from the IDE's
test runner or with `dotnet test`. The two complementary C# test projects are:

- **LogicAppUnit.Tests** — runs the real workflow engine with external calls mocked
  ("real engine, mocked edges"). Fast; ideal for CI. Covers samples 01/03/04 and mocks
  02's App Configuration call.
- **RealHost.IntegrationTests** — a *true* integration suite: it starts a real `func host`
  (via Corvus.Testing.AzureFunctions) and makes real HTTP calls, flipping app settings
  per scenario.

```bash
dotnet test tests-dotnet/LogicAppUnit.Tests        # fast, mocked edges
dotnet test tests-dotnet/RealHost.IntegrationTests # true integration (needs Azurite + func)
```

See [`tests-dotnet/README.md`](tests-dotnet/README.md) for prerequisites and the full
scenario matrix.

### Try the toggles

| Flag (in `local.settings.json`) | Effect |
|---|---|
| `FeatureFlag_GlobalKillSwitch = true` | Sample 01 returns `503` (everything paused) |
| `FeatureFlag_NewPricingEngine = true/false` | Sample 01 & 03 switch pricing engine |
| `FeatureFlag_SendEmailNotifications = true/false` | Sample 03 sends / skips email |
| `Routing_FulfillmentVersion = legacy\|v2\|canary` | Sample 04 routes accordingly |
| `Routing_CanaryPercentage = 0..100` | Sample 04 canary split size |

After editing `local.settings.json`, **restart the debug session** so the runtime re-reads it (this is exactly why App Configuration, sample 02, exists — it avoids the restart).

---

## How the approaches compare

| Capability | 01 App Settings | 02 App Config | 03 Parameters | 04 Inline |
|---|:--:|:--:|:--:|:--:|
| Change without redeploy | restart | **instant** | restart | redeploy |
| Percentage / gradual rollout | ✗ | **✓** | ✗ | ✓ (per-run) |
| Central across many apps | ✗ | **✓** | ✗ | ✗ |
| Structured (non-boolean) config | ✗ | ✓ | **✓** | ✓ |
| Extra Azure resource | none | App Config | none | none |
| Audit history of flag changes | ✗ | **✓** | ✗ | ✗ |
| Complexity | lowest | medium | low | medium |

**Rule of thumb:** start with **App Settings** for simple per-environment booleans; graduate to **App Configuration** when flags change often, need rollouts, or must be shared; use **Parameters** for typed/structured shared config; reach for **inline control-flow** when you're routing between implementations or dark-launching.

---

## Notes & caveats

- This is a **spike** — the goal is to illustrate patterns clearly, not to be production-hardened. For example, sample 02 makes an HTTP call per evaluation; in production you'd cache the flag (e.g. per run or with a short TTL).
- `rand()` in sample 04 gives a fresh roll each run, so a given user isn't *stuck* in the same cohort. For sticky assignment, hash a stable key (e.g. customer id) instead.
- `local.settings.json` is **git-ignored** on purpose — it can hold secrets. Commit `local.settings.json.example` instead.
- Boolean app settings are strings; the workflows wrap them with `bool(...)` where a real boolean is needed.

## References

- [Edit app & host settings for Standard logic apps](https://learn.microsoft.com/en-us/azure/logic-apps/edit-app-settings-host-settings)
- [Create parameters for workflow inputs](https://learn.microsoft.com/en-us/azure/logic-apps/create-parameters-workflows)
- [Manage feature flags with Azure App Configuration](https://learn.microsoft.com/en-us/azure/azure-app-configuration/manage-feature-flags)
- [Workflow Definition Language schema reference](https://learn.microsoft.com/en-us/azure/logic-apps/workflow-definition-language-schema)
