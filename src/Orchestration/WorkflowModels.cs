// Copyright (c) Microsoft. All rights reserved.

using UtilityBillingChatbot.Agents.Classifier;

namespace UtilityBillingChatbot.Orchestration;

/// <summary>
/// Output of the ClassifierExecutor. Carries the classification result plus
/// the original user message (so downstream executors can use it) and all
/// ChatEvents collected during streaming.
/// </summary>
public record ClassifierResult(
    QuestionCategory? Category,
    double Confidence,
    string OriginalMessage,
    List<ChatEvent> CollectedEvents);

/// <summary>
/// Output of the FAQExecutor. Carries whether an answer was found and
/// all ChatEvents collected during streaming.
/// </summary>
public record FAQResult(
    bool FoundAnswer,
    List<ChatEvent> CollectedEvents);

/// <summary>
/// Output of the DefaultHandlerExecutor for non-BillingFAQ categories.
/// </summary>
public record DefaultHandlerResult(
    string Message,
    List<ChatEvent> CollectedEvents);
