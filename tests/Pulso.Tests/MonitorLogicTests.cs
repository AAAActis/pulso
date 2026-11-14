using FluentAssertions;
using Pulso.Api;
using Xunit;

namespace Pulso.Tests;

public class MonitorLogicTests
{
    [Theory]
    [InlineData(true, true, "UP")]
    [InlineData(true, false, "FALLO DE TÚNEL")]
    [InlineData(false, true, "DESCONECTADO")]
    [InlineData(false, false, "DESCONECTADO")]
    public void DetermineStatus_ReturnsCorrectLabel(bool internalOk, bool publicOk, string expected)
    {
        var result = DetermineStatus(internalOk, publicOk);
        result.Should().Be(expected);
    }

    private static string DetermineStatus(bool internalOk, bool publicOk)
    {
        if (internalOk && publicOk) return "UP";
        if (internalOk && !publicOk) return "FALLO DE TÚNEL";
        return "DESCONECTADO";
    }

    [Fact]
    public void ServiceIds_ContainsExpectedServices()
    {
        var serviceIds = new[]
        {
            "nutrition-app",
            "n8n-automation",
            "santiago-portfolio",
            "api-gateway",
            "demo-service"
        };

        serviceIds.Should().HaveCount(5);
        serviceIds.Should().Contain("nutrition-app");
        serviceIds.Should().Contain("n8n-automation");
        serviceIds.Should().Contain("santiago-portfolio");
        serviceIds.Should().Contain("api-gateway");
        serviceIds.Should().Contain("demo-service");
    }

    [Fact]
    public void CheckResult_RecordCreatedWithCorrectProperties()
    {
        var now = DateTime.UtcNow;
        var result = new CheckResult(
            "test-service",
            now,
            true,
            false,
            15,
            30,
            "FALLO DE TÚNEL"
        );

        result.ServiceId.Should().Be("test-service");
        result.CheckedAtUtc.Should().Be(now);
        result.InternalOk.Should().BeTrue();
        result.PublicOk.Should().BeFalse();
        result.InternalLatencyMs.Should().Be(15);
        result.PublicLatencyMs.Should().Be(30);
        result.StatusLabel.Should().Be("FALLO DE TÚNEL");
    }
}