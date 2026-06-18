namespace FeatureToggle.RealHost.IntegrationTests;

// Each fixture starts ONE real host with a fixed flag set, on its own port so xUnit can
// run the classes in parallel without port clashes. App settings are read at startup, so
// a new flag combination => a new fixture/host.

public sealed class NewPricingOnFixture : LogicAppHostFixture
{
    public NewPricingOnFixture() : base(new Dictionary<string, string>
    {
        ["FeatureFlag_GlobalKillSwitch"] = "false",
        ["FeatureFlag_NewPricingEngine"] = "true"
    }, port: 7081) { }
}

public sealed class NewPricingOffFixture : LogicAppHostFixture
{
    public NewPricingOffFixture() : base(new Dictionary<string, string>
    {
        ["FeatureFlag_GlobalKillSwitch"] = "false",
        ["FeatureFlag_NewPricingEngine"] = "false"
    }, port: 7082) { }
}

public sealed class KillSwitchOnFixture : LogicAppHostFixture
{
    public KillSwitchOnFixture() : base(new Dictionary<string, string>
    {
        ["FeatureFlag_GlobalKillSwitch"] = "true"
    }, port: 7083) { }
}

public sealed class EmailOnFixture : LogicAppHostFixture
{
    public EmailOnFixture() : base(new Dictionary<string, string>
    {
        ["FeatureFlag_SendEmailNotifications"] = "true"
    }, port: 7084) { }
}

public sealed class EmailOffFixture : LogicAppHostFixture
{
    public EmailOffFixture() : base(new Dictionary<string, string>
    {
        ["FeatureFlag_SendEmailNotifications"] = "false"
    }, port: 7085) { }
}

public sealed class RoutingV2Fixture : LogicAppHostFixture
{
    public RoutingV2Fixture() : base(new Dictionary<string, string>
    {
        ["Routing_FulfillmentVersion"] = "v2"
    }, port: 7086) { }
}

public sealed class Canary100Fixture : LogicAppHostFixture
{
    public Canary100Fixture() : base(new Dictionary<string, string>
    {
        ["Routing_FulfillmentVersion"] = "canary",
        ["Routing_CanaryPercentage"] = "100"
    }, port: 7087) { }
}
