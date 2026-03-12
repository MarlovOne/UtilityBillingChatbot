using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace UtilityBillingChatbot.Tests;

/// <summary>
/// Tests that verify behavior when two agents with different tools/instructions share
/// the same AgentSession.
///
/// KEY FINDINGS (verified empirically):
///
/// 1. In rc3+, agent2 DOES see agent1's history by default (AsAIAgent without explicit
///    ChatHistoryProvider). Both agents share history via the session's StateBag with
///    the default InMemoryChatHistoryProvider. (This was broken in rc1 due to lazy-init.)
///
/// 2. Providing an explicit ChatHistoryProvider still works and is recommended for clarity.
///    You can share the same instance or give each agent its own — they share state via
///    the session's StateBag using the same StateKey.
///
/// 3. Each agent always uses its OWN instructions and tools — those are never shared.
///
/// 4. When the LLM hallucinates a tool call for an unregistered tool, FunctionInvokingChatClient
///    returns an error message ("Error: Requested function X not found.") back to the LLM.
///    It does NOT throw an exception.
///
/// 5. When agents share history via ChatHistoryProvider, agent2 DOES see agent1's
///    FunctionCallContent and FunctionResultContent in the history — there is NO filtering.
/// </summary>
public class SharedSessionTests
{
    // ──────────────────────────────────────────────────────────────────────
    // Test tools — simple AIFunctions that record when they're called
    // ──────────────────────────────────────────────────────────────────────

    private static readonly List<string> ToolCallLog = [];

    [Description("Gets the current weather for a location")]
    private static string GetWeather(string location)
    {
        ToolCallLog.Add($"GetWeather({location})");
        return $"72°F and sunny in {location}";
    }

    [Description("Looks up a customer's account balance")]
    private static string GetAccountBalance(string accountId)
    {
        ToolCallLog.Add($"GetAccountBalance({accountId})");
        return $"Account {accountId} balance: $142.50";
    }

    // ──────────────────────────────────────────────────────────────────────
    // Mock IChatClient — records all messages sent to the LLM and returns
    // scripted responses. This lets us inspect exactly what each agent sees.
    // ──────────────────────────────────────────────────────────────────────

    private sealed class CapturingChatClient : IChatClient
    {
        private readonly Queue<ChatResponse> _responses = new();

        public List<(List<ChatMessage> Messages, ChatOptions? Options)> Invocations { get; } = [];

        public void EnqueueResponse(params ChatMessage[] messages)
            => _responses.Enqueue(new ChatResponse([.. messages]));

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var msgList = messages.ToList();
            Invocations.Add((msgList, options));

            if (_responses.Count == 0)
                return Task.FromResult(new ChatResponse([new(ChatRole.Assistant, "No more scripted responses.")]));

            return Task.FromResult(_responses.Dequeue());
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (var update in response.ToChatResponseUpdates())
                yield return update;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    // ══════════════════════════════════════════════════════════════════════
    // DEFAULT BEHAVIOR (no explicit ChatHistoryProvider)
    // ══════════════════════════════════════════════════════════════════════

    // ──────────────────────────────────────────────────────────────────────
    // Test 1: Without explicit ChatHistoryProvider, agent2 DOES see
    //         agent1's history. (Fixed in rc3 — rc1 had a lazy-init bug
    //         where the ChatHistoryProvider was null on agent2 at read time.)
    //         In rc3, both agents share history via the session's StateBag
    //         with the default InMemoryChatHistoryProvider.
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Default_Agent2_Sees_Agent1_History()
    {
        var mockClient1 = new CapturingChatClient();
        var mockClient2 = new CapturingChatClient();

        mockClient1.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Agent1 reply"));
        mockClient2.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Agent2 reply"));

        var agent1 = mockClient1.AsAIAgent(instructions: "Agent1", name: "Agent1");
        var agent2 = mockClient2.AsAIAgent(instructions: "Agent2", name: "Agent2");

        var session = await agent1.CreateSessionAsync();
        await agent1.RunAsync("msg1", session);
        await agent2.RunAsync("msg2", session);

        // In rc3: agent2 sees agent1's full history via shared StateBag
        var agent2Messages = mockClient2.Invocations[0].Messages;
        var userMsgs = agent2Messages.Where(m => m.Role == ChatRole.User).ToList();
        var asstMsgs = agent2Messages.Where(m => m.Role == ChatRole.Assistant).ToList();

        Assert.Equal(2, userMsgs.Count);
        Assert.Equal("msg1", userMsgs[0].Text);
        Assert.Equal("msg2", userMsgs[1].Text);
        Assert.Single(asstMsgs);
        Assert.Equal("Agent1 reply", asstMsgs[0].Text);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Test 2: Each agent uses its OWN instructions, not the other agent's
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Each_Agent_Uses_Own_Instructions()
    {
        var mockClient1 = new CapturingChatClient();
        var mockClient2 = new CapturingChatClient();

        mockClient1.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Response1"));
        mockClient2.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Response2"));

        var agent1 = mockClient1.AsAIAgent(
            instructions: "You are a WEATHER assistant.",
            name: "WeatherAgent");
        var agent2 = mockClient2.AsAIAgent(
            instructions: "You are a BILLING assistant.",
            name: "BillingAgent");

        var session = await agent1.CreateSessionAsync();
        await agent1.RunAsync("Hello", session);
        await agent2.RunAsync("Hello again", session);

        Assert.Contains("WEATHER", mockClient1.Invocations[0].Options?.Instructions);
        Assert.Contains("BILLING", mockClient2.Invocations[0].Options?.Instructions);
        Assert.DoesNotContain("BILLING", mockClient1.Invocations[0].Options?.Instructions ?? "");
        Assert.DoesNotContain("WEATHER", mockClient2.Invocations[0].Options?.Instructions ?? "");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Test 3: Each agent sends only its OWN tools, not the other agent's
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Each_Agent_Sends_Only_Own_Tools()
    {
        var mockClient1 = new CapturingChatClient();
        var mockClient2 = new CapturingChatClient();

        mockClient1.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Hello from agent1!"));
        mockClient2.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Hello from agent2!"));

        var agent1 = mockClient1.AsAIAgent(
            instructions: "Weather assistant.",
            name: "WeatherAgent",
            tools: [AIFunctionFactory.Create(GetWeather)]);
        var agent2 = mockClient2.AsAIAgent(
            instructions: "Billing assistant.",
            name: "BillingAgent",
            tools: [AIFunctionFactory.Create(GetAccountBalance)]);

        var session = await agent1.CreateSessionAsync();
        await agent1.RunAsync("Hello", session);
        await agent2.RunAsync("Hello again", session);

        var agent1Tools = mockClient1.Invocations[0].Options?.Tools?.Select(t => t.Name).ToList() ?? [];
        var agent2Tools = mockClient2.Invocations[0].Options?.Tools?.Select(t => t.Name).ToList() ?? [];

        Assert.Contains("GetWeather", agent1Tools);
        Assert.DoesNotContain("GetAccountBalance", agent1Tools);

        Assert.Contains("GetAccountBalance", agent2Tools);
        Assert.DoesNotContain("GetWeather", agent2Tools);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Test 4: Hallucinated tool call returns error message, not exception
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Hallucinated_ToolCall_Returns_Error_Not_Exception()
    {
        var mockClient = new CapturingChatClient();
        ToolCallLog.Clear();

        // LLM hallucinates a call to GetWeather (agent only has GetAccountBalance)
        mockClient.EnqueueResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call_hallucinated", "GetWeather",
                new Dictionary<string, object?> { ["location"] = "Portland" })]));
        // After getting the error, LLM self-corrects
        mockClient.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Sorry, I can't check weather."));

        var agent = mockClient.AsAIAgent(
            instructions: "Billing assistant.",
            name: "BillingAgent",
            tools: [AIFunctionFactory.Create(GetAccountBalance)]);

        // Act — should NOT throw
        var session = await agent.CreateSessionAsync();
        var response = await agent.RunAsync("What's the weather?", session);

        // Completed without exception
        Assert.NotNull(response);

        // GetWeather was NOT executed
        Assert.DoesNotContain(ToolCallLog, s => s.StartsWith("GetWeather"));

        // LLM was called twice: hallucinated call → error → self-correction
        Assert.Equal(2, mockClient.Invocations.Count);

        // Second call contains error FunctionResultContent for the hallucinated function
        var errorResults = mockClient.Invocations[1].Messages
            .SelectMany(m => m.Contents.OfType<FunctionResultContent>())
            .ToList();
        Assert.NotEmpty(errorResults);
        var errorResult = errorResults.First(fr => fr.CallId == "call_hallucinated");
        Assert.Contains("GetWeather", errorResult.Result?.ToString() ?? "");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Test 5: Both agents can use their own tools on the shared session
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Both_Agents_Execute_Own_Tools_On_Shared_Session()
    {
        var mockClient1 = new CapturingChatClient();
        var mockClient2 = new CapturingChatClient();
        ToolCallLog.Clear();

        // Agent1: tool call → response
        mockClient1.EnqueueResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call_weather", "GetWeather",
                new Dictionary<string, object?> { ["location"] = "Denver" })]));
        mockClient1.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "It's 65°F in Denver."));

        // Agent2: tool call → response
        mockClient2.EnqueueResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call_balance", "GetAccountBalance",
                new Dictionary<string, object?> { ["accountId"] = "ACC-001" })]));
        mockClient2.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Your balance is $142.50."));

        var agent1 = mockClient1.AsAIAgent(
            instructions: "Weather assistant.",
            name: "WeatherAgent",
            tools: [AIFunctionFactory.Create(GetWeather)]);
        var agent2 = mockClient2.AsAIAgent(
            instructions: "Billing assistant.",
            name: "BillingAgent",
            tools: [AIFunctionFactory.Create(GetAccountBalance)]);

        var session = await agent1.CreateSessionAsync();

        var r1 = await agent1.RunAsync("Weather in Denver?", session);
        Assert.Contains("65°F", r1.Messages.Last().Text);

        var r2 = await agent2.RunAsync("Balance for ACC-001?", session);
        Assert.Contains("$142.50", r2.Messages.Last().Text);

        Assert.Contains("GetWeather(Denver)", ToolCallLog);
        Assert.Contains("GetAccountBalance(ACC-001)", ToolCallLog);
        Assert.Equal(2, ToolCallLog.Count);
    }

    // ══════════════════════════════════════════════════════════════════════
    // WITH SHARED ChatHistoryProvider (the fix)
    // ══════════════════════════════════════════════════════════════════════

    // ──────────────────────────────────────────────────────────────────────
    // Test 6: With a shared ChatHistoryProvider, agent2 DOES see agent1's
    //         full conversation history
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SharedProvider_Agent2_Sees_Agent1_History()
    {
        var mockClient1 = new CapturingChatClient();
        var mockClient2 = new CapturingChatClient();

        mockClient1.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Agent1 reply"));
        mockClient2.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Agent2 reply"));

        // KEY: both agents share the same InMemoryChatHistoryProvider
        var sharedHistory = new InMemoryChatHistoryProvider();

        var agent1 = mockClient1.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = "Agent1 instructions" },
            Name = "Agent1",
            ChatHistoryProvider = sharedHistory
        });
        var agent2 = mockClient2.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = "Agent2 instructions" },
            Name = "Agent2",
            ChatHistoryProvider = sharedHistory
        });

        var session = await agent1.CreateSessionAsync();
        await agent1.RunAsync("msg1", session);
        await agent2.RunAsync("msg2", session);

        // Agent2 should see agent1's history
        var agent2Messages = mockClient2.Invocations[0].Messages;
        var userMessages = agent2Messages.Where(m => m.Role == ChatRole.User).ToList();
        var assistantMessages = agent2Messages.Where(m => m.Role == ChatRole.Assistant).ToList();

        Assert.Equal(2, userMessages.Count);
        Assert.Equal("msg1", userMessages[0].Text);
        Assert.Equal("msg2", userMessages[1].Text);

        Assert.Single(assistantMessages);
        Assert.Equal("Agent1 reply", assistantMessages[0].Text);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Test 7: With a shared ChatHistoryProvider, agent2 sees agent1's
    //         tool call/result messages (FunctionCallContent + FunctionResultContent)
    //         — there is NO filtering based on available tools
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SharedProvider_Agent2_Sees_Agent1_ToolCalls()
    {
        var mockClient1 = new CapturingChatClient();
        var mockClient2 = new CapturingChatClient();
        ToolCallLog.Clear();

        // Agent1: tool call to GetWeather → response
        mockClient1.EnqueueResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call_1", "GetWeather",
                new Dictionary<string, object?> { ["location"] = "Seattle" })]));
        mockClient1.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "72°F in Seattle!"));

        // Agent2: simple response
        mockClient2.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Your balance is $50."));

        var sharedHistory = new InMemoryChatHistoryProvider();

        var agent1 = mockClient1.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new()
            {
                Instructions = "Weather assistant",
                Tools = [AIFunctionFactory.Create(GetWeather)]
            },
            Name = "WeatherAgent",
            ChatHistoryProvider = sharedHistory,
        });
        var agent2 = mockClient2.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new()
            {
                Instructions = "Billing assistant",
                Tools = [AIFunctionFactory.Create(GetAccountBalance)]
            },
            Name = "BillingAgent",
            ChatHistoryProvider = sharedHistory,
        });

        var session = await agent1.CreateSessionAsync();
        await agent1.RunAsync("Weather in Seattle?", session);
        Assert.Contains("GetWeather(Seattle)", ToolCallLog);

        await agent2.RunAsync("What's my balance?", session);

        // Agent2's LLM input should contain FunctionCallContent from agent1
        var agent2Messages = mockClient2.Invocations[0].Messages;

        var functionCalls = agent2Messages
            .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
            .ToList();
        Assert.NotEmpty(functionCalls);
        Assert.Contains(functionCalls, fc => fc.Name == "GetWeather");

        // And FunctionResultContent from agent1's tool execution
        var functionResults = agent2Messages
            .SelectMany(m => m.Contents.OfType<FunctionResultContent>())
            .ToList();
        Assert.NotEmpty(functionResults);
        Assert.Contains(functionResults, fr => fr.CallId == "call_1");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Test 8: With shared provider, history accumulates across 3 agents
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SharedProvider_History_Accumulates_Across_Three_Agents()
    {
        var mockClient1 = new CapturingChatClient();
        var mockClient2 = new CapturingChatClient();
        var mockClient3 = new CapturingChatClient();

        mockClient1.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Reply1"));
        mockClient2.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Reply2"));
        mockClient3.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Reply3"));

        var sharedHistory = new InMemoryChatHistoryProvider();

        var agent1 = mockClient1.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = "A1" },
            Name = "A1",
            ChatHistoryProvider = sharedHistory
        });
        var agent2 = mockClient2.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = "A2" },
            Name = "A2",
            ChatHistoryProvider = sharedHistory
        });
        var agent3 = mockClient3.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = "A3" },
            Name = "A3",
            ChatHistoryProvider = sharedHistory
        });

        var session = await agent1.CreateSessionAsync();
        await agent1.RunAsync("msg1", session);
        await agent2.RunAsync("msg2", session);
        await agent3.RunAsync("msg3", session);

        // Agent3 should see all prior history
        var agent3Messages = mockClient3.Invocations[0].Messages;
        var userMsgs = agent3Messages.Where(m => m.Role == ChatRole.User).ToList();
        var asstMsgs = agent3Messages.Where(m => m.Role == ChatRole.Assistant).ToList();

        Assert.Equal(3, userMsgs.Count);
        Assert.Equal("msg1", userMsgs[0].Text);
        Assert.Equal("msg2", userMsgs[1].Text);
        Assert.Equal("msg3", userMsgs[2].Text);

        Assert.Equal(2, asstMsgs.Count);
        Assert.Equal("Reply1", asstMsgs[0].Text);
        Assert.Equal("Reply2", asstMsgs[1].Text);
    }

    // ══════════════════════════════════════════════════════════════════════
    // SEPARATE SESSIONS (each agent gets its own session)
    // ══════════════════════════════════════════════════════════════════════

    // ──────────────────────────────────────────────────────────────────────
    // Test 9: With separate sessions, agents are fully isolated
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SeparateSessions_Agents_Are_Fully_Isolated()
    {
        var mockClient1 = new CapturingChatClient();
        var mockClient2 = new CapturingChatClient();

        mockClient1.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Agent1 reply"));
        mockClient2.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Agent2 reply"));

        var sharedHistory = new InMemoryChatHistoryProvider();

        var agent1 = mockClient1.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = "Agent1" },
            Name = "Agent1",
            ChatHistoryProvider = sharedHistory
        });
        var agent2 = mockClient2.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = "Agent2" },
            Name = "Agent2",
            ChatHistoryProvider = sharedHistory
        });

        // Each agent creates its OWN session
        var session1 = await agent1.CreateSessionAsync();
        var session2 = await agent2.CreateSessionAsync();

        await agent1.RunAsync("msg1", session1);
        await agent2.RunAsync("msg2", session2);

        // Agent1 should only see its own message
        var agent1Messages = mockClient1.Invocations[0].Messages;
        Assert.Single(agent1Messages);
        Assert.Equal("msg1", agent1Messages[0].Text);

        // Agent2 should only see its own message — no leakage from agent1
        var agent2Messages = mockClient2.Invocations[0].Messages;
        Assert.Single(agent2Messages);
        Assert.Equal("msg2", agent2Messages[0].Text);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Test 10: With separate sessions + shared provider, running agent1
    //          twice on session1 accumulates only in session1, not session2
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SeparateSessions_History_Does_Not_Leak_Between_Sessions()
    {
        var mockClient = new CapturingChatClient();

        mockClient.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Reply to msg1"));
        mockClient.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Reply to msg2"));
        mockClient.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Reply to msg3"));

        var sharedHistory = new InMemoryChatHistoryProvider();

        var agent = mockClient.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = "Test agent" },
            Name = "TestAgent",
            ChatHistoryProvider = sharedHistory
        });

        var session1 = await agent.CreateSessionAsync();
        var session2 = await agent.CreateSessionAsync();

        // Run twice on session1
        await agent.RunAsync("msg1", session1);
        await agent.RunAsync("msg2", session1);

        // Run once on session2
        await agent.RunAsync("msg3", session2);

        // session1's second call should see msg1 history
        var call2Messages = mockClient.Invocations[1].Messages;
        var call2UserMsgs = call2Messages.Where(m => m.Role == ChatRole.User).ToList();
        Assert.Equal(2, call2UserMsgs.Count);
        Assert.Equal("msg1", call2UserMsgs[0].Text);
        Assert.Equal("msg2", call2UserMsgs[1].Text);

        // session2's call should NOT see any of session1's history
        var call3Messages = mockClient.Invocations[2].Messages;
        var call3UserMsgs = call3Messages.Where(m => m.Role == ChatRole.User).ToList();
        Assert.Single(call3UserMsgs);
        Assert.Equal("msg3", call3UserMsgs[0].Text);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Test 11: With separate sessions and tool usage, tool call history
    //          stays in its own session
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SeparateSessions_ToolCalls_Stay_In_Own_Session()
    {
        var mockClient1 = new CapturingChatClient();
        var mockClient2 = new CapturingChatClient();
        ToolCallLog.Clear();

        // Agent1: tool call → response
        mockClient1.EnqueueResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call_1", "GetWeather",
                new Dictionary<string, object?> { ["location"] = "NYC" })]));
        mockClient1.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "72°F in NYC!"));

        // Agent2: simple response
        mockClient2.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Balance: $100"));

        var sharedHistory = new InMemoryChatHistoryProvider();

        var agent1 = mockClient1.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new()
            {
                Instructions = "Weather",
                Tools = [AIFunctionFactory.Create(GetWeather)]
            },
            Name = "WeatherAgent",
            ChatHistoryProvider = sharedHistory
        });
        var agent2 = mockClient2.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new()
            {
                Instructions = "Billing",
                Tools = [AIFunctionFactory.Create(GetAccountBalance)]
            },
            Name = "BillingAgent",
            ChatHistoryProvider = sharedHistory
        });

        // Each agent uses its OWN session
        var session1 = await agent1.CreateSessionAsync();
        var session2 = await agent2.CreateSessionAsync();

        await agent1.RunAsync("Weather in NYC?", session1);
        Assert.Contains("GetWeather(NYC)", ToolCallLog);

        await agent2.RunAsync("My balance?", session2);

        // Agent2 should NOT see agent1's FunctionCallContent
        var agent2Messages = mockClient2.Invocations[0].Messages;
        var functionCalls = agent2Messages
            .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
            .ToList();
        Assert.Empty(functionCalls);

        var functionResults = agent2Messages
            .SelectMany(m => m.Contents.OfType<FunctionResultContent>())
            .ToList();
        Assert.Empty(functionResults);
    }

    // ══════════════════════════════════════════════════════════════════════
    // SEPARATE PROVIDER INSTANCES, SHARED SESSION
    // (same StateKey "InMemoryChatHistoryProvider" but different objects)
    // ══════════════════════════════════════════════════════════════════════

    // ──────────────────────────────────────────────────────────────────────
    // Test 12: Two separate InMemoryChatHistoryProvider instances, same
    //          session — do they share history via the StateBag?
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SeparateProviders_SharedSession_Agent2_Sees_Agent1_History()
    {
        var mockClient1 = new CapturingChatClient();
        var mockClient2 = new CapturingChatClient();

        mockClient1.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Agent1 reply"));
        mockClient2.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Agent2 reply"));

        // KEY: each agent gets its OWN provider instance (not shared)
        var provider1 = new InMemoryChatHistoryProvider();
        var provider2 = new InMemoryChatHistoryProvider();

        var agent1 = mockClient1.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = "Agent1" },
            Name = "Agent1",
            ChatHistoryProvider = provider1
        });
        var agent2 = mockClient2.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = "Agent2" },
            Name = "Agent2",
            ChatHistoryProvider = provider2
        });

        // Same session
        var session = await agent1.CreateSessionAsync();
        await agent1.RunAsync("msg1", session);
        await agent2.RunAsync("msg2", session);

        var agent2Messages = mockClient2.Invocations[0].Messages;
        var userMsgs = agent2Messages.Where(m => m.Role == ChatRole.User).ToList();
        var asstMsgs = agent2Messages.Where(m => m.Role == ChatRole.Assistant).ToList();

        // Does agent2 see agent1's history via the shared StateBag key?
        // Both providers use the same default StateKey

        // If StateBag sharing works, we'd expect 2 user messages and 1 assistant
        // If NOT, we'd expect only 1 user message
        Assert.Equal(2, userMsgs.Count);
        Assert.Equal("msg1", userMsgs[0].Text);
        Assert.Equal("msg2", userMsgs[1].Text);
        Assert.Single(asstMsgs);
        Assert.Equal("Agent1 reply", asstMsgs[0].Text);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Test 13: Separate providers + tool calls — does agent2 see agent1's
    //          FunctionCallContent via StateBag?
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SeparateProviders_SharedSession_Agent2_Sees_Agent1_ToolCalls()
    {
        var mockClient1 = new CapturingChatClient();
        var mockClient2 = new CapturingChatClient();
        ToolCallLog.Clear();

        mockClient1.EnqueueResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call_w", "GetWeather",
                new Dictionary<string, object?> { ["location"] = "LA" })]));
        mockClient1.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Sunny in LA!"));
        mockClient2.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Balance: $200"));

        var agent1 = mockClient1.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new()
            {
                Instructions = "Weather",
                Tools = [AIFunctionFactory.Create(GetWeather)]
            },
            Name = "WeatherAgent",
            ChatHistoryProvider = new InMemoryChatHistoryProvider()
        });
        var agent2 = mockClient2.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new()
            {
                Instructions = "Billing",
                Tools = [AIFunctionFactory.Create(GetAccountBalance)]
            },
            Name = "BillingAgent",
            ChatHistoryProvider = new InMemoryChatHistoryProvider()
        });

        var session = await agent1.CreateSessionAsync();
        await agent1.RunAsync("Weather in LA?", session);
        Assert.Contains("GetWeather(LA)", ToolCallLog);

        await agent2.RunAsync("My balance?", session);

        var agent2Messages = mockClient2.Invocations[0].Messages;
        var functionCalls = agent2Messages
            .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
            .ToList();
        var functionResults = agent2Messages
            .SelectMany(m => m.Contents.OfType<FunctionResultContent>())
            .ToList();

        // If StateBag sharing works, agent2 sees agent1's tool calls
        Assert.NotEmpty(functionCalls);
        Assert.Contains(functionCalls, fc => fc.Name == "GetWeather");
        Assert.NotEmpty(functionResults);
    }

    // ══════════════════════════════════════════════════════════════════════
    // FRESH SESSION REUSE — does a brand new session see history from a
    // previous session when using the same provider?
    // ══════════════════════════════════════════════════════════════════════

    // ──────────────────────────────────────────────────────────────────────
    // Test 14: Same provider, but a BRAND NEW session (simulating a new
    //          HTTP request). Does agent2 still see agent1's history?
    //          This tests the "just create a fresh session" claim.
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FreshSession_SameProvider_Agent2_Sees_History()
    {
        var mockClient1 = new CapturingChatClient();
        var mockClient2 = new CapturingChatClient();

        mockClient1.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Agent1 reply"));
        mockClient2.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Agent2 reply"));

        var sharedHistory = new InMemoryChatHistoryProvider();

        var agent1 = mockClient1.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = "Agent1" },
            Name = "Agent1",
            ChatHistoryProvider = sharedHistory
        });
        var agent2 = mockClient2.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = "Agent2" },
            Name = "Agent2",
            ChatHistoryProvider = sharedHistory
        });

        // Session 1: agent1 runs
        var session1 = await agent1.CreateSessionAsync();
        await agent1.RunAsync("msg1", session1);

        // Session 2: BRAND NEW session — simulating a new request
        var session2 = await agent2.CreateSessionAsync();
        await agent2.RunAsync("msg2", session2);

        // Does agent2 see agent1's history from the previous session?
        var agent2Messages = mockClient2.Invocations[0].Messages;
        var userMsgs = agent2Messages.Where(m => m.Role == ChatRole.User).ToList();

        // With InMemoryChatHistoryProvider: history is in the StateBag of session1,
        // NOT in session2. A fresh session has a fresh StateBag.
        // So agent2 should NOT see agent1's history.
        Assert.Single(userMsgs);
        Assert.Equal("msg2", userMsgs[0].Text);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Test 15: Same as above but WITHOUT explicit provider (default rc3).
    //          Fresh session = fresh StateBag = no history.
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FreshSession_DefaultProvider_Agent2_Does_NOT_See_History()
    {
        var mockClient1 = new CapturingChatClient();
        var mockClient2 = new CapturingChatClient();

        mockClient1.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Agent1 reply"));
        mockClient2.EnqueueResponse(new ChatMessage(ChatRole.Assistant, "Agent2 reply"));

        var agent1 = mockClient1.AsAIAgent(instructions: "Agent1", name: "Agent1");
        var agent2 = mockClient2.AsAIAgent(instructions: "Agent2", name: "Agent2");

        // Session 1
        var session1 = await agent1.CreateSessionAsync();
        await agent1.RunAsync("msg1", session1);

        // Session 2: BRAND NEW session
        var session2 = await agent2.CreateSessionAsync();
        await agent2.RunAsync("msg2", session2);

        // Fresh session = no history
        var agent2Messages = mockClient2.Invocations[0].Messages;
        Assert.Single(agent2Messages);
        Assert.Equal("msg2", agent2Messages[0].Text);
    }
}
