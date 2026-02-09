// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI;
using UtilityBillingChatbot.MultiAgent.Agents;

namespace UtilityBillingChatbot.MultiAgent;

/// <summary>
/// Central registry for all multi-agent workflow participants.
/// Holds both the definition, the base ChatClientAgent (for structured output),
/// and the instrumented AIAgent (with telemetry middleware) for each agent.
/// </summary>
public class AgentRegistry
{
    private readonly Dictionary<string, AgentEntry> _agents = new();

    /// <summary>
    /// Entry containing the agent definition and both agent instances.
    /// </summary>
    /// <param name="Definition">The agent definition</param>
    /// <param name="BaseAgent">The base ChatClientAgent (for structured output)</param>
    /// <param name="InstrumentedAgent">The instrumented AIAgent with telemetry middleware (may be same as BaseAgent if telemetry disabled)</param>
    public record AgentEntry(IAgentDefinition Definition, ChatClientAgent BaseAgent, AIAgent InstrumentedAgent);

    public void Register(IAgentDefinition definition, ChatClientAgent baseAgent, AIAgent instrumentedAgent)
    {
        _agents[definition.Id] = new AgentEntry(definition, baseAgent, instrumentedAgent);
    }

    /// <summary>
    /// Gets the instrumented agent (with telemetry) for general agent runs.
    /// </summary>
    public AIAgent GetAgent(string agentId)
    {
        return _agents.TryGetValue(agentId, out var entry)
            ? entry.InstrumentedAgent
            : throw new KeyNotFoundException($"Agent '{agentId}' not found in registry.");
    }

    /// <summary>
    /// Gets the base ChatClientAgent for structured output calls.
    /// </summary>
    public ChatClientAgent GetBaseAgent(string agentId)
    {
        return _agents.TryGetValue(agentId, out var entry)
            ? entry.BaseAgent
            : throw new KeyNotFoundException($"Agent '{agentId}' not found in registry.");
    }

    public IAgentDefinition GetDefinition(string agentId)
    {
        return _agents.TryGetValue(agentId, out var entry)
            ? entry.Definition
            : throw new KeyNotFoundException($"Agent definition '{agentId}' not found in registry.");
    }

    public IReadOnlyList<AgentEntry> GetAll() => _agents.Values.ToList();

    public bool TryGetAgent(string agentId, out AIAgent? agent)
    {
        if (_agents.TryGetValue(agentId, out var entry))
        {
            agent = entry.InstrumentedAgent;
            return true;
        }
        agent = null;
        return false;
    }
}
