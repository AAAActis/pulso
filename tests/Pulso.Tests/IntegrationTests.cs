using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pulso.Api;
using Xunit;

namespace Pulso.Tests;

public class IntegrationTests : IClassFixture<PulsoWebApplicationFactory>, IAsyncLifetime
{
    private readonly PulsoWebApplicationFactory _factory;

    public IntegrationTests(PulsoWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient CreateClientWithCors()
    {
        var client = _factory.CreateClient();
        // In Development, CORS policy "dev" allows localhost:5000
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5000");
        return client;
    }

    [Fact]
    public async Task Cors_AllowedOrigin_ReturnsSuccess()
    {
        var client = CreateClientWithCors();

        var response = await client.GetAsync("/api/v1/summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Contain("http://localhost:5000");
    }

    [Fact]
    public async Task Cors_DisallowedOrigin_ReturnsNoCorsHeader()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "https://evil.com");

        var response = await client.GetAsync("/api/v1/summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Summary_ETag_Returns304_WhenIfNoneMatchMatches()
    {
        var client = CreateClientWithCors();

        var firstResponse = await client.GetAsync("/api/v1/summary");
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var etag = firstResponse.Headers.ETag?.Tag;
        etag.Should().NotBeNull();

        var secondRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/summary");
        secondRequest.Headers.Add("Origin", "http://localhost:5000");
        secondRequest.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag!));

        var secondResponse = await client.SendAsync(secondRequest);

        secondResponse.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task LabSimulateFailure_ReturnsSuccess_AndActivatesSimulation()
    {
        var client = CreateClientWithCors();
        var endpoint = "/api/v1/lab/simulate-failure";

        var response = await client.PostAsync(endpoint, new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("Simulación de caída activada");
        json.Should().Contain("OPEN");
    }

    [Fact]
    public async Task Services_NoInternalExposure_ResponseDoesNotContainInternalNamesOrPorts()
    {
        var client = CreateClientWithCors();

        var response = await client.GetAsync("/api/v1/services");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        var services = doc.RootElement.GetProperty("services");
        var forbiddenTerms = new[] { "nutricion-api", "n8n-app", "web-portfolio", ":8080", ":5678", "arch-net", "192.168", "10.0.", "172.16." };

        foreach (var service in services.EnumerateArray())
        {
            var serviceJson = service.GetRawText();
            foreach (var term in forbiddenTerms)
            {
                serviceJson.Should().NotContain(term, $"Response should not expose internal term: {term}");
            }
        }
    }
}