# Plan: Stage 3 Auth Agent Tests

## Objective

Implement tests for the Auth Agent following the same pattern as `ClassifierAgentTests` and `FAQAgentTests` - real LLM integration tests using DI.

## Test Pattern (from existing tests)

```csharp
public class AuthAgentTests : IAsyncLifetime
{
    private IHost _host = null!;
    private AuthAgentFactory _authAgentFactory = null!;

    public Task InitializeAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.SetBasePath(AppContext.BaseDirectory);
        builder.Configuration.AddJsonFile("appsettings.json", optional: false);
        builder.Configuration.AddEnvironmentVariables();

        builder.Services.AddUtilityBillingChatbot(builder.Configuration);

        _host = builder.Build();
        _authAgentFactory = _host.Services.GetRequiredService<AuthAgentFactory>();

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }
}
```

## Test Cases

### Auth Flow Tests

| Test | Description | Assertions |
|------|-------------|------------|
| `AuthAgent_AsksForIdentifier_WhenAnonymous` | Start conversation, agent should ask for phone/email | Response mentions phone, email, or account number |
| `AuthAgent_FindsCustomer_ByPhone` | Provide phone number | `authProvider.AuthState == Verifying`, `authProvider.CustomerName == "John Smith"` |
| `AuthAgent_FindsCustomer_ByEmail` | Provide email | `authProvider.AuthState == Verifying` |
| `AuthAgent_FindsCustomer_ByAccountNumber` | Provide account number | `authProvider.AuthState == Verifying` |
| `AuthAgent_AsksVerificationQuestion_AfterLookup` | After customer found | Response asks for SSN or DOB |
| `AuthAgent_Authenticates_WithCorrectSSN` | Provide correct SSN | `authProvider.IsAuthenticated == true` |
| `AuthAgent_Authenticates_WithCorrectDOB` | Provide correct DOB | `authProvider.IsAuthenticated == true` |
| `AuthAgent_TracksFailedAttempts` | Provide wrong SSN once | Response mentions "2 attempts remaining" |
| `AuthAgent_LocksOut_AfterThreeFailures` | Provide wrong SSN 3 times | `authProvider.AuthState == LockedOut` |
| `AuthAgent_FullFlow_PhoneAndSSN` | Complete flow: question → phone → SSN | `authProvider.IsAuthenticated`, `authProvider.CustomerName` set |

### Session Persistence Tests

| Test | Description | Assertions |
|------|-------------|------------|
| `AuthAgent_SessionSerialize_PreservesState` | Serialize mid-flow, deserialize, continue | Auth completes successfully |
| `AuthAgent_SessionRestore_ContinuesVerification` | Serialize after lookup, restore, verify SSN | `authProvider.IsAuthenticated` |

## Implementation

### tests/AuthAgentTests.cs

```csharp
// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UtilityBillingChatbot.Agents.Auth;
using UtilityBillingChatbot.Infrastructure;

namespace UtilityBillingChatbot.Tests;

/// <summary>
/// Integration tests for the Auth Agent.
/// These tests require a configured LLM endpoint (see appsettings.json).
/// </summary>
public class AuthAgentTests : IAsyncLifetime
{
    private IHost _host = null!;
    private AuthAgentFactory _authAgentFactory = null!;

    public Task InitializeAsync()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration.SetBasePath(AppContext.BaseDirectory);
        builder.Configuration.AddJsonFile("appsettings.json", optional: false);
        builder.Configuration.AddEnvironmentVariables();

        builder.Services.AddUtilityBillingChatbot(builder.Configuration);

        _host = builder.Build();
        _authAgentFactory = _host.Services.GetRequiredService<AuthAgentFactory>();

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
        // Arrange
        var (agent, authProvider) = _authAgentFactory.CreateAuthAgent();
        var session = await agent.CreateSessionAsync();

        // Act
        await agent.RunAsync("I want to check my bill", session);
        await agent.RunAsync("555-1234", session);

        // Assert
        Assert.Equal(AuthenticationState.Verifying, authProvider.AuthState);
        Assert.Equal("John Smith", authProvider.CustomerName);
        Assert.Equal("1234567890", authProvider.CustomerId);
    }

    [Fact]
    public async Task AuthAgent_Authenticates_WithCorrectSSN()
    {
        // Arrange
        var (agent, authProvider) = _authAgentFactory.CreateAuthAgent();
        var session = await agent.CreateSessionAsync();

        // Act
        await agent.RunAsync("I need help with my account", session);
        await agent.RunAsync("555-1234", session);
        await agent.RunAsync("1234", session);  // Correct SSN

        // Assert
        Assert.True(authProvider.IsAuthenticated);
        Assert.Equal("John Smith", authProvider.CustomerName);
    }

    [Fact]
    public async Task AuthAgent_Authenticates_WithCorrectDOB()
    {
        // Arrange
        var (agent, authProvider) = _authAgentFactory.CreateAuthAgent();
        var session = await agent.CreateSessionAsync();

        // Act
        await agent.RunAsync("Check my balance", session);
        await agent.RunAsync("555-1234", session);
        await agent.RunAsync("03/15/1985", session);  // Correct DOB

        // Assert
        Assert.True(authProvider.IsAuthenticated);
    }

    [Fact]
    public async Task AuthAgent_LocksOut_AfterThreeFailures()
    {
        // Arrange
        var (agent, authProvider) = _authAgentFactory.CreateAuthAgent();
        var session = await agent.CreateSessionAsync();

        // Act
        await agent.RunAsync("I want to see my bill", session);
        await agent.RunAsync("555-1234", session);
        await agent.RunAsync("0000", session);  // Wrong
        await agent.RunAsync("1111", session);  // Wrong
        await agent.RunAsync("2222", session);  // Wrong - should lock out

        // Assert
        Assert.Equal(AuthenticationState.LockedOut, authProvider.AuthState);
    }

    [Fact]
    public async Task AuthAgent_FindsCustomer_ByEmail()
    {
        // Arrange
        var (agent, authProvider) = _authAgentFactory.CreateAuthAgent();
        var session = await agent.CreateSessionAsync();

        // Act
        await agent.RunAsync("Help with my account", session);
        await agent.RunAsync("maria.garcia@example.com", session);

        // Assert
        Assert.Equal(AuthenticationState.Verifying, authProvider.AuthState);
        Assert.Equal("Maria Garcia", authProvider.CustomerName);
    }

    [Fact]
    public async Task AuthAgent_FullFlow_PhoneAndSSN()
    {
        // Arrange
        var (agent, authProvider) = _authAgentFactory.CreateAuthAgent();
        var session = await agent.CreateSessionAsync();

        // Act - Complete auth flow
        var response1 = await agent.RunAsync("Did you receive my payment?", session);
        var response2 = await agent.RunAsync("My phone number is 555-1234", session);
        var response3 = await agent.RunAsync("The last 4 digits are 1234", session);

        // Assert
        Assert.True(authProvider.IsAuthenticated);
        Assert.Equal("John Smith", authProvider.CustomerName);
        Assert.Equal("1234567890", authProvider.CustomerId);
    }

    [Fact]
    public async Task AuthAgent_SessionRestore_ContinuesVerification()
    {
        // Arrange - Start auth flow
        var (agent, authProvider) = _authAgentFactory.CreateAuthAgent();
        var session = await agent.CreateSessionAsync();

        await agent.RunAsync("Check my bill", session);
        await agent.RunAsync("555-1234", session);

        Assert.Equal(AuthenticationState.Verifying, authProvider.AuthState);

        // Act - Serialize and restore
        var serialized = session.Serialize();
        var restoredSession = await agent.DeserializeSessionAsync(serialized);

        // Continue with verification
        await agent.RunAsync("1234", restoredSession);

        // Assert - Get restored provider and check state
        var restoredProvider = restoredSession.GetService<AuthenticationContextProvider>();
        Assert.NotNull(restoredProvider);
        Assert.True(restoredProvider.IsAuthenticated);
    }
}
```

## Required Changes

### 1. Register AuthAgentFactory in DI

In `src/Agents/Auth/AuthAgentExtensions.cs`:

```csharp
public static class AuthAgentExtensions
{
    public static IServiceCollection AddAuthAgent(this IServiceCollection services)
    {
        services.AddSingleton<MockCISDatabase>();
        services.AddSingleton<AuthAgentFactory>();
        return services;
    }
}
```

### 2. Update ServiceCollectionExtensions.cs

```csharp
public static IServiceCollection AddUtilityBillingChatbot(
    this IServiceCollection services,
    IConfiguration configuration)
{
    // ... existing code ...
    services.AddClassifierAgent();
    services.AddFAQAgent();
    services.AddAuthAgent();  // Add this
    // ...
}
```

### 3. Create the Auth Agent files

| File | Purpose |
|------|---------|
| `src/Agents/Auth/AuthenticationState.cs` | Enum |
| `src/Agents/Auth/AuthVerificationResult.cs` | Response model |
| `src/Agents/Auth/LookupResult.cs` | Response model |
| `src/Agents/Auth/MockCISDatabase.cs` | Mock data |
| `src/Agents/Auth/AuthenticationContextProvider.cs` | Main provider |
| `src/Agents/Auth/AuthAgentFactory.cs` | Factory + DI extension |
| `tests/AuthAgentTests.cs` | Tests |

## Execution

```bash
# Run just auth tests
dotnet test --filter "FullyQualifiedName~AuthAgentTests"

# Run all tests
dotnet test
```

## Verification

- [ ] AuthAgentFactory registered in DI
- [ ] All test cases pass with real LLM
- [ ] Session serialization/deserialization works
- [ ] Tests follow same pattern as ClassifierAgentTests/FAQAgentTests
