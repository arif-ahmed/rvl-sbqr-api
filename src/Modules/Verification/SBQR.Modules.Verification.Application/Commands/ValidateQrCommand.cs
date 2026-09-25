using FluentValidation;
using MediatR;

namespace SBQR.Modules.Verification.Application.Commands;

/// <summary>
/// Verify one raw QR payload string end-to-end (replay window check → parse
/// → CRC → key resolution → Ed25519 verify) and record the outcome.
/// Fail-closed: every indeterminate or failed step is a recorded rejection
/// verdict — there is no configuration flag anywhere that turns a failed
/// verification into an acceptance. The (tenant, request id) pair is
/// single-use: a replayed request is rejected without re-recording.
/// </summary>
public sealed record ValidateQrCommand(
    string QrPayload,
    string RequestId,
    DateTimeOffset RequestTimestamp) : IRequest<ValidateQrResult>;

/// <summary>Stable verdicts. INVALID_* and KEY_* outcomes are rejections; NON_P2P is informational.</summary>
public enum QrVerdict
{
    /// <summary>Signature verified against a trusted key.</summary>
    VALID,

    /// <summary>Signature present but cryptographically invalid (or absent/malformed).</summary>
    INVALID_SIGNATURE,

    /// <summary>Structural failure — CRC mismatch, malformed TLV, missing mandatory tags. ReasonCode carries the codec's stable code.</summary>
    STRUCTURAL_INVALID,

    /// <summary>Issuer's key not found in the trust store.</summary>
    KEY_NOT_FOUND,

    /// <summary>Issuer's trusted key is suspended — fail closed (C16).</summary>
    KEY_SUSPENDED,

    /// <summary>Issuer's trusted key is revoked (compromised) — fail closed (C16).</summary>
    KEY_REVOKED,

    /// <summary>Issuer's trusted key exists but is not ACTIVE (PENDING/GENERATING/RETIRING).</summary>
    KEY_NOT_ACTIVE,

    /// <summary>Structurally valid but not a P2P QR (Tag 52 ≠ 4829). Not an error.</summary>
    NON_P2P,

    /// <summary>Request timestamp outside the ±5 min replay window — never evaluated (C6).</summary>
    REQUEST_STALE,

    /// <summary>The (tenant, request id) pair was already used — replayed request, rejected (C6).</summary>
    REQUEST_REPLAYED,
}

/// <summary>
/// Where the trusted key came from. Per spec Annex B, Verification resolves
/// ALL issuers through the single BB trust store (TRUST_DIRECTORY); there
/// is no own-custody branch — own-tenant keys are published into the same
/// store by KeyCustody. NONE = not evaluated (structural failure,
/// non-P2P, stale, replay).
/// </summary>
public enum QrTrustSource
{
    TRUST_DIRECTORY,
    NONE,
}

/// <summary>The verification outcome plus the recorded metadata.</summary>
public sealed record ValidateQrResult(
    QrVerdict Verdict,
    QrTrustSource? TrustSource,
    string? ReasonCode,
    string? InstitutionCode,
    string PayloadHash,
    string? RecipientName,
    string? RecipientPan,
    string QrClassification);

public sealed class ValidateQrValidator : AbstractValidator<ValidateQrCommand>
{
    private static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    public ValidateQrValidator()
    {
        RuleFor(x => x.QrPayload).NotEmpty().MaximumLength(2048);
        RuleFor(x => x.RequestId).NotEmpty().MaximumLength(100)
            .Matches("^[A-Za-z0-9._-]{8,100}$")
            .WithMessage("RequestId must be 8-100 characters of [A-Za-z0-9._-].");
        RuleFor(x => x.RequestTimestamp).NotNull()
            .Must(ts => Math.Abs((DateTimeOffset.UtcNow - ts).TotalMinutes) <= MaxClockSkew.TotalMinutes)
            .WithMessage("RequestTimestamp must be within ±5 minutes of server time (C6 replay window).");
    }
}
