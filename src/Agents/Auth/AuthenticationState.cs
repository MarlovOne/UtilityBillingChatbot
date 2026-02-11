// Copyright (c) Microsoft. All rights reserved.

namespace UtilityBillingChatbot.Agents.Auth;

/// <summary>
/// Tracks the authentication state during a verification flow.
/// </summary>
public enum AuthenticationState
{
    /// <summary>User has not been identified.</summary>
    Anonymous,

    /// <summary>Authentication flow in progress (identifying user).</summary>
    InProgress,

    /// <summary>User has been found, verification in progress.</summary>
    Verifying,

    /// <summary>User identity has been verified.</summary>
    Authenticated,

    /// <summary>Session has expired, re-authentication required.</summary>
    Expired,

    /// <summary>Too many failed attempts, locked out.</summary>
    LockedOut
}
