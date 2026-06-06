namespace Pos.Application.Abstractions;

/// <summary>Encrypts/decrypts per-tenant integration secrets (M-Pesa keys, eTIMS CMC key) at rest.</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}
