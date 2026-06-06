namespace Pos.Application.Abstractions;

/// <summary>Hashes and verifies PINs/passwords. Plaintext never leaves this seam.</summary>
public interface IPasswordHasher
{
    string Hash(string input);
    bool Verify(string hash, string input);
}
