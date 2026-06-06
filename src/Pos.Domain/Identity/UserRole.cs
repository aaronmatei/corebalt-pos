namespace Pos.Domain.Identity;

/// <summary>Staff role, ordered by privilege. Cashier sells; Supervisor handles voids/refunds (later);
/// Manager runs the back office (catalogue, pricing, stock).</summary>
public enum UserRole { Cashier = 0, Supervisor = 1, Manager = 2 }
