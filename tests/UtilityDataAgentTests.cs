// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UtilityBillingChatbot.Agents.Auth;
using UtilityBillingChatbot.Agents.UtilityData;
using UtilityBillingChatbot.Infrastructure;

namespace UtilityBillingChatbot.Tests;

/// <summary>
/// Integration tests for the UtilityData Agent.
/// These tests require a configured LLM endpoint (see appsettings.json).
/// </summary>
[Collection("Sequential")]
public class UtilityDataAgentTests : IAsyncLifetime
{
    private IHost _host = null!;
    private AuthAgent _authAgent = null!;
    private UtilityDataAgent _utilityDataAgent = null!;

    public Task InitializeAsync()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration.SetBasePath(AppContext.BaseDirectory);
        builder.Configuration.AddJsonFile("appsettings.json", optional: false);
        builder.Configuration.AddEnvironmentVariables();

        builder.Services.AddUtilityBillingChatbot(builder.Configuration);

        _host = builder.Build();
        _authAgent = _host.Services.GetRequiredService<AuthAgent>();
        _utilityDataAgent = _host.Services.GetRequiredService<UtilityDataAgent>();

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task DataAgent_ThrowsException_WhenNoAuthSession()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _utilityDataAgent.RunAsync("What is my balance?"));
    }

    [Fact]
    public async Task DataAgent_ThrowsException_WhenNotAuthenticated()
    {
        // Get a session that's in Verifying state (not yet authenticated)
        var r1 = await _authAgent.RunAsync("I need help");
        var r2 = await _authAgent.RunAsync("555-1234", r1.Session);

        Assert.Equal(AuthenticationState.Verifying, r2.AuthState);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _utilityDataAgent.CreateSessionAsync(r2.Session));
    }

    [Fact]
    public async Task DataAgent_FetchesBalance_WhenAuthenticated()
    {
        // Authenticate John Smith
        var r1 = await _authAgent.RunAsync("I need help with my account");
        var r2 = await _authAgent.RunAsync("555-1234", r1.Session);
        var r3 = await _authAgent.RunAsync("1234", r2.Session);
        Assert.True(r3.IsAuthenticated);

        // Query balance
        var response = await _utilityDataAgent.RunAsync(
            "What is my current balance?",
            authSession: r3.Session);

        Assert.Contains("187", response.Text);
        Assert.Equal("John Smith", response.CustomerName);
        Assert.Equal("1234567890", response.AccountNumber);
    }

    [Fact]
    public async Task DataAgent_AnalyzesUsage_WithComparison()
    {
        // Authenticate John Smith (has significant usage increase)
        var r1 = await _authAgent.RunAsync("Check my bill");
        var r2 = await _authAgent.RunAsync("555-1234", r1.Session);
        var r3 = await _authAgent.RunAsync("1234", r2.Session);
        Assert.True(r3.IsAuthenticated);

        // Ask about high bill
        var response = await _utilityDataAgent.RunAsync(
            "Why is my bill so high?",
            authSession: r3.Session);

        // Should mention usage or increase
        Assert.True(
            response.Text.Contains("usage", StringComparison.OrdinalIgnoreCase) ||
            response.Text.Contains("increase", StringComparison.OrdinalIgnoreCase) ||
            response.Text.Contains("higher", StringComparison.OrdinalIgnoreCase),
            $"Expected response about usage. Got: {response.Text}");
    }
}
