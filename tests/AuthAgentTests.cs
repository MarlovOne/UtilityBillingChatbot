// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UtilityBillingChatbot.Agents;
using UtilityBillingChatbot.Agents.Auth;
using UtilityBillingChatbot.Infrastructure;

namespace UtilityBillingChatbot.Tests;

/// <summary>
/// Integration tests for the Auth Agent.
/// These tests require a configured LLM endpoint (see appsettings.json).
/// </summary>
[Collection("Sequential")]
public class AuthAgentTests : IAsyncLifetime
{
    private IHost _host = null!;
    private AuthAgent _authAgent = null!;

    public Task InitializeAsync()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration.SetBasePath(AppContext.BaseDirectory);
        builder.Configuration.AddJsonFile("appsettings.json", optional: false);
        builder.Configuration.AddEnvironmentVariables();

        builder.Services.AddUtilityBillingChatbot(builder.Configuration);

        _host = builder.Build();
        _authAgent = _host.Services.GetRequiredService<AuthAgent>();

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task AuthAgent_FindsCustomer_ByPhone()
    {
        var session = await _authAgent.CreateSessionAsync();

        var (text1, meta1) = await StreamingTestHelper.ConsumeAsync(
            _authAgent.StreamAsync("I want to check my bill", session));
        var (text2, meta2) = await StreamingTestHelper.ConsumeAsync(
            _authAgent.StreamAsync("555-1234", session));

        Assert.Equal(AuthenticationState.Verifying, meta2.State);
        Assert.Equal("John Smith", meta2.CustomerName);
        Assert.Equal("1234567890", meta2.CustomerId);
    }

    [Fact]
    public async Task AuthAgent_Authenticates_WithCorrectSSN()
    {
        var session = await _authAgent.CreateSessionAsync();

        var (_, m1) = await StreamingTestHelper.ConsumeAsync(
            _authAgent.StreamAsync("I need help with my account", session));
        var (_, m2) = await StreamingTestHelper.ConsumeAsync(
            _authAgent.StreamAsync("555-1234", session));
        var (_, m3) = await StreamingTestHelper.ConsumeAsync(
            _authAgent.StreamAsync("1234", session));

        Assert.Equal(AuthenticationState.Authenticated, m3.State);
        Assert.Equal("John Smith", m3.CustomerName);
    }

    [Fact]
    public async Task AuthAgent_LocksOut_AfterThreeFailures()
    {
        var session = await _authAgent.CreateSessionAsync();

        await StreamingTestHelper.ConsumeAsync(
            _authAgent.StreamAsync("Check my bill", session));
        await StreamingTestHelper.ConsumeAsync(
            _authAgent.StreamAsync("555-1234", session));
        await StreamingTestHelper.ConsumeAsync(
            _authAgent.StreamAsync("0000", session));
        await StreamingTestHelper.ConsumeAsync(
            _authAgent.StreamAsync("1111", session));
        var (_, m5) = await StreamingTestHelper.ConsumeAsync(
            _authAgent.StreamAsync("2222", session));

        Assert.Equal(AuthenticationState.LockedOut, m5.State);
    }

    [Fact]
    public async Task AuthAgent_FindsCustomer_ByEmail()
    {
        var session = await _authAgent.CreateSessionAsync();

        await StreamingTestHelper.ConsumeAsync(
            _authAgent.StreamAsync("Help with my account", session));
        var (_, m2) = await StreamingTestHelper.ConsumeAsync(
            _authAgent.StreamAsync("maria.garcia@example.com", session));

        Assert.Equal(AuthenticationState.Verifying, m2.State);
        Assert.Equal("Maria Garcia", m2.CustomerName);
    }

    [Fact]
    public async Task AuthAgent_FullFlow_PhoneAndSSN()
    {
        var session = await _authAgent.CreateSessionAsync();

        await StreamingTestHelper.ConsumeAsync(
            _authAgent.StreamAsync("Did you receive my payment?", session));
        await StreamingTestHelper.ConsumeAsync(
            _authAgent.StreamAsync("My phone number is 555-1234", session));
        var (_, m3) = await StreamingTestHelper.ConsumeAsync(
            _authAgent.StreamAsync("The last 4 digits are 1234", session));

        Assert.Equal(AuthenticationState.Authenticated, m3.State);
        Assert.Equal("John Smith", m3.CustomerName);
        Assert.Equal("1234567890", m3.CustomerId);
    }
}
