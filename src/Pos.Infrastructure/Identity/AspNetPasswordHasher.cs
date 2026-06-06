using Microsoft.AspNetCore.Identity;
using Pos.Application.Abstractions;

namespace Pos.Infrastructure.Identity;

/// <summary>
/// IPasswordHasher backed by ASP.NET Core's <see cref="PasswordHasher{TUser}"/> (PBKDF2). This is the
/// only piece of Identity we use — no UserManager, no stores, no schema. The TUser is irrelevant to
/// the algorithm, so a shared sentinel is passed.
/// </summary>
public sealed class AspNetPasswordHasher : IPasswordHasher
{
    private static readonly object Sentinel = new();
    private readonly PasswordHasher<object> _inner = new();

    public string Hash(string input) => _inner.HashPassword(Sentinel, input);

    public bool Verify(string hash, string input) =>
        _inner.VerifyHashedPassword(Sentinel, hash, input) != PasswordVerificationResult.Failed;
}
