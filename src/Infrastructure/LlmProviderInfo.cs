// Copyright (c) Microsoft. All rights reserved.

namespace UtilityBillingChatbot.Infrastructure;

/// <summary>
/// Immutable display info about the active LLM provider.
/// </summary>
public record LlmProviderInfo(string ProviderName, string ModelDisplayName);
