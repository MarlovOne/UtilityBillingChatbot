// Copyright (c) Microsoft. All rights reserved.

using UtilityBillingChatbot.Agents.Auth;
using UtilityBillingChatbot.Agents.Classifier;
using UtilityBillingChatbot.Agents.NextBestAction;

namespace UtilityBillingChatbot.Orchestration;

/// <summary>
/// Base type for all events emitted by the streaming pipeline.
/// </summary>
public abstract record ChatEvent;

/// <summary>LLM text token.</summary>
public record TextChunk(string Text) : ChatEvent;

/// <summary>Classification result from the ClassifierAgent.</summary>
public record ClassificationEvent(QuestionCategory? Category, double Confidence) : ChatEvent;

/// <summary>Authentication state change from the AuthAgent.</summary>
public record AuthStateEvent(
    AuthenticationState State,
    string? CustomerId,
    string? CustomerName,
    AuthFlowState? FlowState) : ChatEvent;

/// <summary>Answer confidence from FAQ or UtilityData agents.</summary>
public record AnswerConfidenceEvent(bool FoundAnswer) : ChatEvent;

/// <summary>Suggested follow-up questions.</summary>
public record SuggestionsEvent(List<SuggestedAction> Suggestions) : ChatEvent;

/// <summary>Debug info about classification and routing.</summary>
public record DebugEvent(QuestionCategory? Category, RequiredAction Action) : ChatEvent;
