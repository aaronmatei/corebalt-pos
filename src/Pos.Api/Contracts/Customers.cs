namespace Pos.Api.Contracts;

public sealed record CreateCustomerRequest(
    string Name, string? Phone = null, string? Email = null, string? KraPin = null, string? NationalId = null);

public sealed record UpdateCustomerRequest(
    string Name, string? Phone, string? Email, string? KraPin, string? NationalId, bool IsActive = true);

public sealed record AdjustPointsRequest(int Delta);

public sealed record CustomerResponse(
    Guid Id, string Name, string? Phone, string? Email, string? KraPin, string? NationalId,
    int LoyaltyPoints, bool IsActive);
