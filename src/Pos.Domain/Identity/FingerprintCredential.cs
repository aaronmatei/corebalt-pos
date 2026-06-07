using Pos.SharedKernel;
using Pos.SharedKernel.Ids;

namespace Pos.Domain.Identity;

/// <summary>
/// One enrolled fingerprint for a <see cref="User"/> (a user may enrol several fingers). Stores ONLY the
/// reader SDK's extracted TEMPLATE — never the raw image — as base64; the column is encrypted at rest by
/// the same Data-Protection secret converter as the M-Pesa/eTIMS secrets. Matching is done LOCALLY by the
/// reader SDK against these templates; they never leave the store server (no cloud). Carries the audit
/// trail of a supervised enrolment: who enrolled it, when, and that explicit consent was recorded.
/// </summary>
public sealed class FingerprintCredential : Entity
{
    public Guid UserId { get; private set; }

    /// <summary>Base64 of the SDK template bytes. ENCRYPTED at rest (EF value converter). Never the image.</summary>
    public string Template { get; private set; } = string.Empty;

    /// <summary>Optional human label for the enrolled finger (e.g. "Right thumb").</summary>
    public string? FingerLabel { get; private set; }

    public Guid EnrolledByUserId { get; private set; }
    public DateTimeOffset EnrolledAtUtc { get; private set; }

    /// <summary>The data subject gave explicit consent (always true — enrolment refuses without it).</summary>
    public bool ConsentGiven { get; private set; }
    public DateTimeOffset ConsentRecordedAtUtc { get; private set; }

    private FingerprintCredential() { } // EF

    internal static FingerprintCredential Create(Guid userId, byte[] template, string? fingerLabel,
        Guid enrolledByUserId, DateTimeOffset now)
    {
        if (template is null || template.Length == 0) throw new ArgumentException("Template is required.", nameof(template));
        return new FingerprintCredential
        {
            Id = Uuid7.NewGuid(),
            UserId = userId,
            Template = Convert.ToBase64String(template),
            FingerLabel = string.IsNullOrWhiteSpace(fingerLabel) ? null : fingerLabel.Trim(),
            EnrolledByUserId = enrolledByUserId,
            EnrolledAtUtc = now,
            ConsentGiven = true,
            ConsentRecordedAtUtc = now,
        };
    }

    /// <summary>The decoded template bytes, for the reader SDK's local match.</summary>
    public byte[] TemplateBytes => Convert.FromBase64String(Template);
}
