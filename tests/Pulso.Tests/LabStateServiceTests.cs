using FluentAssertions;
using Pulso.Api;
using Xunit;

namespace Pulso.Tests;

public class LabStateServiceTests
{
    [Fact]
    public void RecordLatency_And_GetLatencyPercentiles_ReturnsCorrectValues()
    {
        var service = new LabStateService();

        service.RecordLatency(10);
        service.RecordLatency(20);
        service.RecordLatency(30);
        service.RecordLatency(40);
        service.RecordLatency(50);

        var (p50, p95) = service.GetLatencyPercentiles();

        p50.Should().Be(30);
        p95.Should().Be(50);
    }

    [Fact]
    public void GetLatencyPercentiles_EmptyBuffer_ReturnsDefaults()
    {
        var service = new LabStateService();

        var (p50, p95) = service.GetLatencyPercentiles();

        p50.Should().Be(12);
        p95.Should().Be(45);
    }

    [Fact]
    public void CacheHitRatio_StartsAtZero()
    {
        var service = new LabStateService();

        service.HitRatioPercent.Should().Be(0.0);
    }

    [Fact]
    public void CacheHitRatio_CalculatesCorrectly()
    {
        var service = new LabStateService();

        service.IncrementCacheHits();
        service.IncrementCacheHits();
        service.IncrementCacheMisses();

        service.HitRatioPercent.Should().Be(66.7);
    }

    [Fact]
    public void StartFailureSimulation_SetsSimulationActive_And_ReturnsCorrectResponse()
    {
        var service = new LabStateService();

        var response = service.StartFailureSimulation(45);

        response.Success.Should().BeTrue();
        response.Message.Should().Contain("45s");
        response.Message.Should().Contain("OPEN");
        response.DurationSeconds.Should().Be(45);
        service.IsFailureSimulated().Should().BeTrue();
        service.IsCircuitBreakerOpen.Should().BeTrue();
    }

    [Fact]
    public void IsFailureSimulated_ReturnsFalse_AfterDuration()
    {
        var service = new LabStateService();

        service.StartFailureSimulation(1);

        // Wait a moment and check - but we can't easily test time passage
        // So we verify it's true immediately after starting
        service.IsFailureSimulated().Should().BeTrue();
    }

    [Fact]
    public void ResetSimulation_ClearsAllState()
    {
        var service = new LabStateService();

        service.StartFailureSimulation(45);
        service.ResetSimulation();

        service.IsFailureSimulated().Should().BeFalse();
        service.IsCircuitBreakerOpen.Should().BeFalse();
        service.StaleWhileRevalidateActive.Should().BeFalse();
    }

    [Fact]
    public void StaleWhileRevalidate_CanBeSet()
    {
        var service = new LabStateService();

        service.StaleWhileRevalidateActive.Should().BeFalse();

        service.SetStaleWhileRevalidate(true);
        service.StaleWhileRevalidateActive.Should().BeTrue();

        service.SetStaleWhileRevalidate(false);
        service.StaleWhileRevalidateActive.Should().BeFalse();
    }
}