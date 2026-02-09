// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UtilityBillingChatbot.Telemetry;

namespace UtilityBillingChatbot.MultiAgent.Agents;

/// <summary>
/// Defines an agent's identity, configuration, and how to build its ChatClientAgent instance.
/// Implementations encapsulate all agent-specific logic (instructions, response format, tools).
/// </summary>
public interface IAgentDefinition
{
    /// <summary>
    /// Unique identifier for this agent (e.g., "classifier", "faq", "orchestrator")
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Human-readable name for the agent
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Brief description of the agent's purpose
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Optional per-agent telemetry configuration.
    /// Returns null to use default telemetry settings.
    /// </summary>
    AgentTelemetryConfiguration? TelemetryConfiguration => null;

    /// <summary>
    /// Builds a ChatClientAgent instance using the provided chat client.
    /// The definition controls all agent configuration (instructions, response format, tools).
    /// </summary>
    /// <param name="chatClient">The underlying chat client to use</param>
    /// <returns>A configured ChatClientAgent ready for use</returns>
    ChatClientAgent Build(IChatClient chatClient);
}
