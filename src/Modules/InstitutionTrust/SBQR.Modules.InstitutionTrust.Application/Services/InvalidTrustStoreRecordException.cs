namespace SBQR.Modules.InstitutionTrust.Application.Services;

/// <summary>
/// Thrown by <see cref="InstitutionUpsertService"/> when an institution
/// record fails the shape guard (institution code, name length, PEM block)
/// BEFORE anything is written. Distinguishable on purpose: <c>DailyTrustSyncService</c>
/// catches it to report "this institution's data was invalid — skipped"
/// rather than "unexpected sync error", and it keeps malformed trust-store
/// data from reaching the CHECK constraints in db/migrations (where the
/// resulting SaveChanges failure would poison the shared change tracker).
/// </summary>
public sealed class InvalidTrustStoreRecordException : Exception
{
    public InvalidTrustStoreRecordException(string institutionCode, string reason)
        : base($"Invalid institution record '{institutionCode}': {reason}")
    {
        InstitutionCode = institutionCode;
        Reason = reason;
    }

    public InvalidTrustStoreRecordException(string message)
        : base(message)
    {
        InstitutionCode = string.Empty;
        Reason = message;
    }

    public InvalidTrustStoreRecordException(string message, Exception innerException)
        : base(message, innerException)
    {
        InstitutionCode = string.Empty;
        Reason = message;
    }

    public InvalidTrustStoreRecordException()
        : base("Invalid institution record.")
    {
        InstitutionCode = string.Empty;
        Reason = "Invalid institution record.";
    }

    /// <summary>The offending institution code, as reported (untrimmed of intent).</summary>
    public string InstitutionCode { get; }

    /// <summary>Human-readable statement of which rule was violated.</summary>
    public string Reason { get; }
}
