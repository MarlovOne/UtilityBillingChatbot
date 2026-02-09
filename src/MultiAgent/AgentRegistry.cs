// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI;
using UtilityBillingChatbot.MultiAgent.Agents;

namespace UtilityBillingChatbot.MultiAgent;

/// <summary>
/// Central registry for all multi-agent workflow participants.
/// Holds both the definition and built ChatClientAgent instance for each agent.
/// </summary>
public class AgentRegistry
{
    private readonly Dictionary<string, AgentEntry> _agents = new();

    public record AgentEntry(IAgentDefinition Definition, ChatClientAgent Instance);

    public void Register(IAgentDefinition definition, ChatClientAgent instance)
    {
        _agents[definition.Id] = new AgentEntry(definition, instance);
    }

    public ChatClientAgent GetAgent(string agentId)
    {
        return _agents.TryGetValue(agentId, out var entry)
            ? entry.Instance
            : throw new KeyNotFoundException($"Agent '{agentId}' not found in registry.");
    }

    public IAgentDefinition GetDefinition(string agentId)
    {
        return _agents.TryGetValue(agentId, out var entry)
            ? entry.Definition
            : throw new KeyNotFoundException($"Agent definition '{agentId}' not found in registry.");
    }

    public IReadOnlyList<AgentEntry> GetAll() => _agents.Values.ToList();

    public bool TryGetAgent(string agentId, out ChatClientAgent? agent)
    {
        if (_agents.TryGetValue(agentId, out var entry))
        {
            agent = entry.Instance;
            return true;
        }
        agent = null;
        return false;
    }
}
