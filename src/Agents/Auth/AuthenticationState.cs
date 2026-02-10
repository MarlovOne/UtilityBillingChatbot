// Copyright (c) Microsoft. All rights reserved.

namespace UtilityBillingChatbot.Agents.Auth;

/// <summary>
/// Tracks the authentication state during a verification flow.
/// </summary>
public enum AuthenticationState
{
    /// <summary>User has not been identified.</summary>
    Anonymous,

    /// <summary>User has been found, verification in progress.</summary>
    Verifying,

    /// <summary>User identity has been verified.</summary>
    Authenticated,

    /// <summary>Too many failed attempts, locked out.</summary>
    LockedOut
}
