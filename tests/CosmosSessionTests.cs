using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace UtilityBillingChatbot.Tests;

/// <summary>
/// Integration tests showing how to use CosmosChatHistoryProvider
/// for persistent, shared conversation history across agents.
///
/// Prerequisites:
///   - Cosmos DB account provisioned
///   - Database "chatbot-db" with container "conversations"
///   - Container partition key: /conversationId
///   - Set env var COSMOS_CONNECTION_STRING
///
/// These tests are skipped if COSMOS_CONNECTION_STRING is not set.
/// </summary>
public class CosmosSessionTests
{
    private static readonly string? CosmosConnectionString =
        Environment.GetEnvironmentVariable("COSMOS_CONNECTION_STRING");

    private const string DatabaseId = "chatbot-db";
    private const string ContainerId = "conversations";

    [Description("Gets the current weather for a location")]
    private static string GetWeather(string location) => $"72°F and sunny in {location}";

    [Description("Looks up a customer's account balance")]
    private static string GetAccountBalance(string accountId) => $"Account {accountId} balance: $142.50";

    private sealed class CapturingChatClient : IChatClient
    {
        private readonly Queue<ChatResponse> _responses = new();
        public List<(List<ChatMessage> Messages, ChatOptions? Options)> Invocations { get; } = [];

        public void EnqueueResponse(params ChatMessage[] messages)
            => _responses.Enqueue(new ChatResponse([.. messages]));

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken ct = default)
        {
            Invocations.Add((messages.ToList(), options));
            return Task.FromResult(_responses.Count > 0
                ? _responses.Dequeue()
                : new ChatResponse([new(ChatRole.Assistant, "No more responses.")]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var r = await GetResponseAsync(messages, options, ct);
            foreach (var u in r.ToChatResponseUpdates()) yield return u;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    /// <summary>
    /// Helper: create a CosmosChatHistoryProvider bound to a specific conversationId.
    /// The stateInitializer is a delegate that provides the routing info (conversationId)
    /// on first invocation. This is how you control which Cosmos partition the
    /// messages go to.
    /// </summary>
    private static CosmosChatHistoryProvider CreateCosmosProvider(string conversationId)
        => new(
            connectionString: CosmosConnectionString!,
            databaseId: DatabaseId,
            containerId: ContainerId,
            stateInitializer: _ => new CosmosChatHistoryProvider.State(
                conversationId: conversationId,
                tenantId: "",
                userId: ""));

    /// <summary>
    /// Helper: create a CosmosChatHistoryProvider with hierarchical partition keys.
    /// </summary>
    private static CosmosChatHistoryProvider CreateCosmosProvider(
        string tenantId, string userId, string sessionId)
        => new(
            connectionString: CosmosConnectionString!,
            databaseId: DatabaseId,
            containerId: ContainerId,
            stateInitializer: _ => new CosmosChatHistoryProvider.State(
                conversationId: sessionId,
                tenantId: tenantId,
                userId: userId));

    // ──────────────────────────────────────────────────────────────────────
    // Test 1: Two agents share Cosmos-backed history via the same
    //         conversationId on a shared session
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cosmos_SharedSession_Agent2_Sees_Agent1_History()
    {
        if (CosmosConnectionString is null)
        {
            // Skip — no Cosmos DB available
            return;
        }

        var mockClient1 = new CapturingChatClient();
        var mockClient2 = new CapturingChatClient();

        mockClient1.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Agent1 reply"));
        mockClient2.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Agent2 reply"));

        // Use a unique conversationId per test run to avoid collisions
        var conversationId = $"test-{Guid.NewGuid():N}";

        // Each agent gets its OWN provider instance, but both point
        // to the same conversationId in Cosmos → shared history
        var agent1 = mockClient1.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = "Weather assistant" },
            Name = "WeatherAgent",
            ChatHistoryProvider = CreateCosmosProvider(conversationId)
        });

        var agent2 = mockClient2.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = "Billing assistant" },
            Name = "BillingAgent",
            ChatHistoryProvider = CreateCosmosProvider(conversationId)
        });

        var session = await agent1.CreateSessionAsync();
        await agent1.RunAsync("What's the weather?", session);
        await agent2.RunAsync("What's my balance?", session);

        // Agent2 should see agent1's full history from Cosmos
        var agent2Messages = mockClient2.Invocations[0].Messages;
        var userMsgs = agent2Messages.Where(m => m.Role == ChatRole.User).ToList();
        var asstMsgs = agent2Messages.Where(m => m.Role == ChatRole.Assistant).ToList();

        Assert.Equal(2, userMsgs.Count);
        Assert.Equal("What's the weather?", userMsgs[0].Text);
        Assert.Equal("What's my balance?", userMsgs[1].Text);
        Assert.Single(asstMsgs);
        Assert.Equal("Agent1 reply", asstMsgs[0].Text);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Test 2: Different conversationIds = isolated history, even with
    //         the same Cosmos container
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cosmos_DifferentConversationIds_Are_Isolated()
    {
        if (CosmosConnectionString is null) return;

        var mockClient1 = new CapturingChatClient();
        var mockClient2 = new CapturingChatClient();

        mockClient1.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Agent1 reply"));
        mockClient2.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Agent2 reply"));

        var agent1 = mockClient1.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = "Agent1" },
            Name = "Agent1",
            ChatHistoryProvider = CreateCosmosProvider($"test-{Guid.NewGuid():N}")
        });

        var agent2 = mockClient2.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = "Agent2" },
            Name = "Agent2",
            ChatHistoryProvider = CreateCosmosProvider($"test-{Guid.NewGuid():N}")
        });

        var session1 = await agent1.CreateSessionAsync();
        var session2 = await agent2.CreateSessionAsync();

        await agent1.RunAsync("msg1", session1);
        await agent2.RunAsync("msg2", session2);

        // Agent2 should NOT see agent1's messages
        var agent2Messages = mockClient2.Invocations[0].Messages;
        Assert.Single(agent2Messages);
        Assert.Equal("msg2", agent2Messages[0].Text);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Test 3: Hierarchical partition keys (tenant/user/session)
    //         for multi-tenant scenarios
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cosmos_HierarchicalPartitioning_MultiTenant()
    {
        if (CosmosConnectionString is null) return;

        var mockClient1 = new CapturingChatClient();
        var mockClient2 = new CapturingChatClient();

        mockClient1.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Tenant1 reply"));
        mockClient2.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Tenant2 reply"));

        var sessionId = $"test-{Guid.NewGuid():N}";

        // Same sessionId but different tenants → isolated
        var agent1 = mockClient1.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = "Agent for Tenant1" },
            Name = "Agent1",
            ChatHistoryProvider = CreateCosmosProvider("tenant-1", "user-A", sessionId)
        });

        var agent2 = mockClient2.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = "Agent for Tenant2" },
            Name = "Agent2",
            ChatHistoryProvider = CreateCosmosProvider("tenant-2", "user-A", sessionId)
        });

        var session1 = await agent1.CreateSessionAsync();
        var session2 = await agent2.CreateSessionAsync();

        await agent1.RunAsync("msg from tenant1", session1);
        await agent2.RunAsync("msg from tenant2", session2);

        var agent2Messages = mockClient2.Invocations[0].Messages;
        Assert.Single(agent2Messages);
        Assert.Equal("msg from tenant2", agent2Messages[0].Text);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Test 4: Tool calls persist in Cosmos and are visible to agent2
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cosmos_ToolCalls_Persist_And_Visible_To_Agent2()
    {
        if (CosmosConnectionString is null) return;

        var mockClient1 = new CapturingChatClient();
        var mockClient2 = new CapturingChatClient();

        mockClient1.EnqueueResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call_1", "GetWeather",
                new Dictionary<string, object?> { ["location"] = "Seattle" })]));
        mockClient1.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "72°F in Seattle!"));
        mockClient2.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Your balance: $142.50"));

        var conversationId = $"test-{Guid.NewGuid():N}";

        var agent1 = mockClient1.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new()
            {
                Instructions = "Weather assistant",
                Tools = [AIFunctionFactory.Create(GetWeather)]
            },
            Name = "WeatherAgent",
            ChatHistoryProvider = CreateCosmosProvider(conversationId)
        });

        var agent2 = mockClient2.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new()
            {
                Instructions = "Billing assistant",
                Tools = [AIFunctionFactory.Create(GetAccountBalance)]
            },
            Name = "BillingAgent",
            ChatHistoryProvider = CreateCosmosProvider(conversationId)
        });

        var session = await agent1.CreateSessionAsync();
        await agent1.RunAsync("Weather in Seattle?", session);
        await agent2.RunAsync("My balance?", session);

        // Agent2 should see agent1's FunctionCallContent from Cosmos
        var agent2Messages = mockClient2.Invocations[0].Messages;
        var functionCalls = agent2Messages
            .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
            .ToList();

        Assert.NotEmpty(functionCalls);
        Assert.Contains(functionCalls, fc => fc.Name == "GetWeather");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Test 5: The real pattern — how your orchestrator would wire it up
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cosmos_Orchestrator_Pattern()
    {
        if (CosmosConnectionString is null) return;

        var mockClient = new CapturingChatClient();
        mockClient.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "FAQ answer"));
        mockClient.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Account data"));

        // Simulate what your orchestrator would do:
        // 1. User connects → orchestrator assigns a session ID
        var sessionId = $"session-{Guid.NewGuid():N}";

        // 2. First request routes to FAQ agent
        var faqAgent = mockClient.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = "You answer billing FAQs." },
            Name = "FAQAgent",
            ChatHistoryProvider = CreateCosmosProvider(sessionId)
        });

        // 3. Second request routes to UtilityData agent
        var dataAgent = mockClient.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = "You look up account data." },
            Name = "UtilityDataAgent",
            ChatHistoryProvider = CreateCosmosProvider(sessionId)
        });

        // 4. Both use the same session
        var session = await faqAgent.CreateSessionAsync();
        await faqAgent.RunAsync("How is my bill calculated?", session);
        await dataAgent.RunAsync("Show me my last 3 bills", session);

        // UtilityDataAgent sees the FAQ conversation
        var dataAgentMessages = mockClient.Invocations[1].Messages;
        var userMsgs = dataAgentMessages.Where(m => m.Role == ChatRole.User).ToList();

        Assert.Equal(2, userMsgs.Count);
        Assert.Equal("How is my bill calculated?", userMsgs[0].Text);
        Assert.Equal("Show me my last 3 bills", userMsgs[1].Text);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Test 6: Using the extension method (simplest API)
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cosmos_ExtensionMethod_Pattern()
    {
        if (CosmosConnectionString is null) return;

        var mockClient = new CapturingChatClient();
        mockClient.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Reply"));

        var sessionId = $"session-{Guid.NewGuid():N}";

        // The extension method sets up ChatHistoryProviderFactory,
        // which creates the provider lazily when CreateSessionAsync is called.
        var options = new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = "Test agent" },
            Name = "TestAgent"
        };
        options.WithCosmosDBChatHistoryProvider(
            CosmosConnectionString!,
            DatabaseId,
            ContainerId,
            stateInitializer: _ => new CosmosChatHistoryProvider.State(
                conversationId: sessionId,
                tenantId: "",
                userId: ""));

        var agent = mockClient.AsAIAgent(options);
        var session = await agent.CreateSessionAsync();
        await agent.RunAsync("Hello", session);

        Assert.Single(mockClient.Invocations);
    }
}
