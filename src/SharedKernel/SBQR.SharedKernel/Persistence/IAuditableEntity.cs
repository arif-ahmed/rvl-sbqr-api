namespace SBQR.SharedKernel.Persistence;

/// <summary>
/// Marker interface for entities that participate in the standard audit-column
/// convention: <c>created_by</c>, <c>created_at</c>, <c>modified_by</c>,
/// <c>modified_at</c>. Each module's audit-column interceptor uses this to
/// detect which entries to stamp on every save. Lives in the Domain-facing
/// shared-kernel namespace so aggregates opt in directly; persistence ignores
/// the interface — the columns are physical on the table.
/// </summary>
/// <remarks>
/// Properties are intentionally <c>string?</c> / nullable <see cref="DateTimeOffset"/>
/// to mirror the schema, where audit columns are nullable until the first
/// insert is observed.
///
/// Promoted from Tenancy.Domain.Interfaces when the OAuth2 client-credentials
/// surface moved to the IdentityAccess bounded context — Tenant, CryptoKey
/// (Tenancy) and ApiCredential (IdentityAccess) all opt in.
/// </remarks>
public interface IAuditableEntity
{
    /// <summary>Identity of the principal that created this row.</summary>
    string? CreatedBy { get; set; }

    /// <summary>UTC timestamp of when the row was inserted.</summary>
    DateTimeOffset CreatedAt { get; set; }

    /// <summary>Identity of the principal that last modified this row.</summary>
    string? ModifiedBy { get; set; }

    /// <summary>UTC timestamp of the last modification; null if never updated.</summary>
    DateTimeOffset? ModifiedAt { get; set; }
}
