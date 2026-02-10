// Copyright (c) Microsoft. All rights reserved.

namespace UtilityBillingChatbot.Agents.Auth;

/// <summary>
/// Mock CIS (Customer Information System) database for utility billing prototyping.
/// Contains 3 test customers with different account states.
/// </summary>
public class MockCISDatabase
{
    private readonly Dictionary<string, UtilityCustomer> _customersByPhone = new()
    {
        ["555-1234"] = new UtilityCustomer
        {
            AccountNumber = "1234567890",
            Name = "John Smith",
            Phone = "555-1234",
            Email = "john.smith@example.com",
            ServiceAddress = "123 Main St, Anytown, ST 12345",
            LastFourSSN = "1234",
            DateOfBirth = new DateOnly(1985, 3, 15),
            AccountBalance = 187.43m,
            DueDate = DateOnly.FromDateTime(DateTime.Now.AddDays(12)),
            LastPaymentAmount = 142.50m,
            LastPaymentDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-18)),
            IsOnAutoPay = false,
            RateCode = "R1",  // Residential Standard
            MeterNumber = "MTR-00123456",
            BillingHistory = [
                new BillRecord("2024-01", 892, 142.50m, "A", DateOnly.FromDateTime(DateTime.Now.AddDays(-48))),
                new BillRecord("2024-02", 1247, 187.43m, "A", DateOnly.FromDateTime(DateTime.Now.AddDays(-18)))
            ],
            UsageHistory = [
                new UsageRecord("2024-01", 892, 28.8m),  // ~29 kWh/day
                new UsageRecord("2024-02", 1247, 44.5m)  // ~45 kWh/day (winter spike)
            ],
            DelinquencyStatus = "Current",
            EligibleForExtension = true
        },
        ["555-5678"] = new UtilityCustomer
        {
            AccountNumber = "9876543210",
            Name = "Maria Garcia",
            Phone = "555-5678",
            Email = "maria.garcia@example.com",
            ServiceAddress = "456 Oak Ave, Anytown, ST 12345",
            LastFourSSN = "5678",
            DateOfBirth = new DateOnly(1990, 7, 22),
            AccountBalance = 0.00m,
            DueDate = DateOnly.FromDateTime(DateTime.Now.AddDays(5)),
            LastPaymentAmount = 98.50m,
            LastPaymentDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-3)),
            IsOnAutoPay = true,
            RateCode = "R1",
            MeterNumber = "MTR-00789012",
            BillingHistory = [
                new BillRecord("2024-01", 654, 98.50m, "A", DateOnly.FromDateTime(DateTime.Now.AddDays(-33))),
                new BillRecord("2024-02", 687, 102.30m, "A", DateOnly.FromDateTime(DateTime.Now.AddDays(-3)))
            ],
            UsageHistory = [
                new UsageRecord("2024-01", 654, 23.4m),
                new UsageRecord("2024-02", 687, 24.5m)
            ],
            DelinquencyStatus = "Current",
            EligibleForExtension = false  // Already current
        },
        ["555-9999"] = new UtilityCustomer
        {
            AccountNumber = "5555555555",
            Name = "Robert Johnson",
            Phone = "555-9999",
            Email = "rjohnson@example.com",
            ServiceAddress = "789 Elm St, Anytown, ST 12345",
            LastFourSSN = "9999",
            DateOfBirth = new DateOnly(1972, 11, 8),
            AccountBalance = 423.67m,
            DueDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-5)),  // Past due!
            LastPaymentAmount = 150.00m,
            LastPaymentDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-45)),
            IsOnAutoPay = false,
            RateCode = "R1",
            MeterNumber = "MTR-00345678",
            BillingHistory = [
                new BillRecord("2024-01", 1456, 218.40m, "E", DateOnly.FromDateTime(DateTime.Now.AddDays(-60))),
                new BillRecord("2024-02", 1523, 228.45m, "A", DateOnly.FromDateTime(DateTime.Now.AddDays(-30)))
            ],
            UsageHistory = [
                new UsageRecord("2024-01", 1456, 52.0m),  // High usage
                new UsageRecord("2024-02", 1523, 54.4m)
            ],
            DelinquencyStatus = "PastDue",
            EligibleForExtension = true
        }
    };

    private readonly Dictionary<string, UtilityCustomer> _customersByEmail;
    private readonly Dictionary<string, UtilityCustomer> _customersByAccount;

    public MockCISDatabase()
    {
        _customersByEmail = _customersByPhone.Values.ToDictionary(u => u.Email.ToLower(), u => u);
        _customersByAccount = _customersByPhone.Values.ToDictionary(u => u.AccountNumber, u => u);
    }

    /// <summary>
    /// Find a customer by phone number, email, or account number.
    /// </summary>
    public UtilityCustomer? FindByIdentifier(string identifier)
    {
        identifier = identifier.Trim();

        // Try phone first
        if (_customersByPhone.TryGetValue(identifier, out var byPhone))
            return byPhone;

        // Try email
        if (_customersByEmail.TryGetValue(identifier.ToLower(), out var byEmail))
            return byEmail;

        // Try account number
        if (_customersByAccount.TryGetValue(identifier, out var byAccount))
            return byAccount;

        return null;
    }
}
