using System.Net;
using System.Text.Json;
using Xunit;

namespace FeatureToggle.RealHost.IntegrationTests;

// True integration tests: a REAL Logic Apps host is running and we make REAL HTTP calls.
// Each test class binds to one host fixture (one flag combination).

public class AppSettings_NewPricingOn(NewPricingOnFixture fx) : IClassFixture<NewPricingOnFixture>
{
    [Fact]
    public async Task Applies_new_engine_and_promo()
    {
        var (status, body) = await fx.InvokeAsync(
            "01-appsettings-toggle", "When_an_order_is_received",
            new { orderId = "ORD-1001", amount = 200 });

        Assert.Equal(HttpStatusCode.OK, status);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("new-pricing-engine-v2", doc.RootElement.GetProperty("engine").GetString());
        Assert.Equal(180m, doc.RootElement.GetProperty("computedTotal").GetDecimal());
    }
}

public class AppSettings_NewPricingOff(NewPricingOffFixture fx) : IClassFixture<NewPricingOffFixture>
{
    [Fact]
    public async Task Uses_legacy_engine()
    {
        var (status, body) = await fx.InvokeAsync(
            "01-appsettings-toggle", "When_an_order_is_received",
            new { orderId = "ORD-1001", amount = 200 });

        Assert.Equal(HttpStatusCode.OK, status);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("legacy-pricing-engine", doc.RootElement.GetProperty("engine").GetString());
        Assert.Equal(200m, doc.RootElement.GetProperty("computedTotal").GetDecimal());
    }
}

public class AppSettings_KillSwitchOn(KillSwitchOnFixture fx) : IClassFixture<KillSwitchOnFixture>
{
    [Fact]
    public async Task Returns_503_disabled()
    {
        var (status, body) = await fx.InvokeAsync(
            "01-appsettings-toggle", "When_an_order_is_received",
            new { orderId = "ORD-1001", amount = 200 });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("disabled", doc.RootElement.GetProperty("status").GetString());
    }
}

public class Parameters_EmailOn(EmailOnFixture fx) : IClassFixture<EmailOnFixture>
{
    [Fact]
    public async Task Sends_email()
    {
        var (status, body) = await fx.InvokeAsync(
            "03-parameters-file-toggle", "When_an_invoice_is_created",
            new { invoiceId = "INV-555", customerEmail = "test@example.com" });

        Assert.Equal(HttpStatusCode.OK, status);
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetProperty("emailSent").GetBoolean());
    }
}

public class Parameters_EmailOff(EmailOffFixture fx) : IClassFixture<EmailOffFixture>
{
    [Fact]
    public async Task Does_not_send_email()
    {
        var (status, body) = await fx.InvokeAsync(
            "03-parameters-file-toggle", "When_an_invoice_is_created",
            new { invoiceId = "INV-555", customerEmail = "test@example.com" });

        Assert.Equal(HttpStatusCode.OK, status);
        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.GetProperty("emailSent").GetBoolean());
    }
}

public class Inline_RoutingV2(RoutingV2Fixture fx) : IClassFixture<RoutingV2Fixture>
{
    [Fact]
    public async Task Routes_to_v2()
    {
        var (status, body) = await fx.InvokeAsync(
            "04-inline-controlflow-toggle", "When_fulfillment_is_requested",
            new { orderId = "ORD-2002" });

        Assert.Equal(HttpStatusCode.OK, status);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("fulfillment-v2", doc.RootElement.GetProperty("handledBy").GetString());
    }

    [Fact]
    public async Task Mock_branch_short_circuits()
    {
        var (status, body) = await fx.InvokeAsync(
            "04-inline-controlflow-toggle", "When_fulfillment_is_requested",
            new { orderId = "ORD-2003", useMock = true });

        Assert.Equal(HttpStatusCode.OK, status);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("MOCK", doc.RootElement.GetProperty("handledBy").GetString());
    }
}

public class Inline_Canary100(Canary100Fixture fx) : IClassFixture<Canary100Fixture>
{
    [Fact]
    public async Task Everyone_in_canary_cohort()
    {
        var (status, body) = await fx.InvokeAsync(
            "04-inline-controlflow-toggle", "When_fulfillment_is_requested",
            new { orderId = "ORD-2004" });

        Assert.Equal(HttpStatusCode.OK, status);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("fulfillment-v2", doc.RootElement.GetProperty("handledBy").GetString());
        Assert.Equal("canary", doc.RootElement.GetProperty("cohort").GetString());
    }
}
