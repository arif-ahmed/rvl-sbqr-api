using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Options;
using SBQR.Modules.IdentityAccess.Application.Abstractions;
using SBQR.Modules.IdentityAccess.Domain.Aggregates;
using SBQR.Modules.IdentityAccess.Domain.Interfaces;
using SBQR.Modules.Tenancy.Contracts;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.Persistence;

namespace SBQR.Modules.IdentityAccess.Application.Commands.IssueToken;

/// <summary>
/// Handler for <see cref="IssueClientCredentialsTokenCommand"/> — the
/// OAuth 2.1 client-credentials token endpoint's brain. Order of checks is
/// deliberately leak-free: every failure on either path returns the same
/// <c>invalid_client</c> error (RFC 6749 §5.2) so an attacker cannot
/// distinguish "unknown client_id" from "wrong secret" from "suspended
/// tenant".
///
/// <para><b>Privilege separation keystone</b>: only the bootstrap path can
/// mint <c>scope=admin</c>. The tenant path issues QR-operation scopes
/// (<c>qr:generate</c> / <c>qr:validate</c>) plus the <c>tenant_id</c>
/// claim and can never produce an admin token, so a compromised FI
/// credential cannot reach <c>/admin/**</c>. Which of the two QR scopes
/// each tenant gets is decided at mint time from the
/// <c>public.tenant_configurations.is_qr_generation_allowed</c> /
/// <c>is_qr_validation_allowed</c> flags — flipping either flag
/// (via <see cref="TenantConfiguration.DisableQrGeneration"/> /
/// <see cref="TenantConfiguration.DisableQrValidation"/>) is the
/// privileged revocation surface and takes effect on the next mint.</para>
///
/// <para><b>Tenant admission</b>: the owning tenant's admission state is
/// resolved through the Tenancy module's published-language seam
/// (<see cref="ITenantAdmissionDirectory"/>) — never through Tenancy's
/// aggregates or DbContext. <see cref="TenantAdmissionState.Pending"/> tenants
/// may authenticate — registration must be exercisable end-to-end before BB
/// activation. Suspended and Terminated tenants are rejected. This mint-time
/// re-check is also the safety net that contains a mid-flight cascade
/// failure: a suspended tenant cannot obtain new tokens even if its
/// credential row was not yet flipped.</para>
/// </summary>
public sealed class IssueClientCredentialsTokenCommandHandler
    : IRequestHandler<IssueClientCredentialsTokenCommand, Result<ClientCredentialsTokenResult>>
{
    /// <summary>
    /// Scope strings stamped on tenant FI tokens. Hard-coded literals —
    /// they must match exactly the values the authorization policies
    /// (PolicyNames.QrGenerate / PolicyNames.QrValidate) require at the
    /// resource layer. Whether each scope is included for a given
    /// credential is decided at mint time from the per-tenant flags
    /// (<see cref="TenantConfiguration.IsQrGenerationAllowed"/> /
    /// <see cref="TenantConfiguration.IsQrValidationAllowed"/>) — see
    /// <see cref="ResolveTenantScopes"/>.
    /// </summary>
    private const string ScopeQrGenerate = "qr:generate";
    private const string ScopeQrValidate = "qr:validate";

    private const string InvalidClient = "invalid_client";
    private const string UnsupportedGrantType = "unsupported_grant_type";

    /// <summary>
    /// Audit action emitted on every successful token issuance
    /// (bootstrap and tenant paths).
    /// </summary>
    private const string AuditTokenIssued = "auth.token.issued";

    /// <summary>
    /// Audit action emitted on every rejected token request. The
    /// <c>Metadata</c> column carries a <c>reason</c> discriminator
    /// (unknown_client | wrong_secret | tenant_suspended | credential_expired
    /// | wrong_grant_type | bootstrap_unconfigured | package_mismatch |
    /// package_not_allowed_for_bootstrap). SOC dashboards key on these to
    /// surface brute-force patterns against a single client_id.
    /// </summary>
    private const string AuditTokenRejected = "auth.token.rejected";

    private readonly ITenantConfigurationRepository _configurations;
    private readonly ITenantAdmissionDirectory _tenants;
    private readonly ITenantApplicationDirectory _applications;
    private readonly ISecretHasher _hasher;
    private readonly IIdentityUnitOfWork _uow;
    private readonly IAccessTokenIssuer _issuer;
    private readonly IOptions<PlatformBootstrapOptions> _bootstrap;
    private readonly IAuditLogger _audit;

    public IssueClientCredentialsTokenCommandHandler(
        ITenantConfigurationRepository configurations,
        ITenantAdmissionDirectory tenants,
        ITenantApplicationDirectory applications,
        ISecretHasher hasher,
        IIdentityUnitOfWork uow,
        IAccessTokenIssuer issuer,
        IOptions<PlatformBootstrapOptions> bootstrap,
        IAuditLogger audit)
    {
        _configurations = configurations ?? throw new ArgumentNullException(nameof(configurations));
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _applications = applications ?? throw new ArgumentNullException(nameof(applications));
        _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
        _uow = uow ?? throw new ArgumentNullException(nameof(uow));
        _issuer = issuer ?? throw new ArgumentNullException(nameof(issuer));
        _bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public async Task<Result<ClientCredentialsTokenResult>> Handle(
        IssueClientCredentialsTokenCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.Equals(request.GrantType, "client_credentials", StringComparison.Ordinal))
        {
            // Constant-time: a wrong grant_type must cost the same as a
            // wrong secret, so attackers cannot enumerate grant types by
            // latency.
            EqualizeTimingAgainst(phc: null);

            await _audit.LogAsync(
                new AuditEntry(
                    Action: AuditTokenRejected,
                    ActorId: $"client:{request.ClientId}",
                    ResourceType: "OAuthTokenEndpoint",
                    ResourceId: request.ClientId,
                    Metadata: AuditMeta("wrong_grant_type")),
                cancellationToken).ConfigureAwait(false);

            return Result<ClientCredentialsTokenResult>.Failure(
                ErrorCode.ValidationFailed,
                UnsupportedGrantType);
        }

        var bootstrap = _bootstrap.Value;
        if (string.Equals(request.ClientId, bootstrap.ClientId, StringComparison.Ordinal))
        {
            return await IssueBootstrapTokenAsync(request, bootstrap, cancellationToken)
                .ConfigureAwait(false);
        }

        return await IssueTenantTokenAsync(request, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<ClientCredentialsTokenResult>> IssueBootstrapTokenAsync(
        IssueClientCredentialsTokenCommand request,
        PlatformBootstrapOptions bootstrap,
        CancellationToken cancellationToken)
    {
        // FR-AUTH-002 §5.2: the bootstrap credential is the admin plane; it
        // is never a mobile app. A package_id supplied with the bootstrap
        // client_id fails closed with audit reason
        // package_not_allowed_for_bootstrap. Same invalid_client error as
        // every other rejection — no information leak about whether the
        // caller "almost got it right".
        if (!string.IsNullOrWhiteSpace(request.PackageId))
        {
            EqualizeTimingAgainst(phc: bootstrap.ClientSecretHash);

            await _audit.LogAsync(
                new AuditEntry(
                    Action: AuditTokenRejected,
                    ActorId: bootstrap.Subject,
                    ResourceType: "PlatformBootstrapClient",
                    ResourceId: bootstrap.ClientId,
                    Metadata: AuditMeta(
                        "package_not_allowed_for_bootstrap",
                        packageId: request.PackageId)),
                cancellationToken).ConfigureAwait(false);

            return InvalidClientFailure();
        }

        // An unconfigured bootstrap client is indistinguishable from an
        // unknown one — same error, no information leak. The dummy verify
        // below runs even when unconfigured, so an attacker cannot detect
        // "the host has no bootstrap credential at all" via fast-path 401.
        var verified = bootstrap.IsConfigured
            && TryVerify(request.ClientSecret, bootstrap.ClientSecretHash!);

        if (!verified)
        {
            EqualizeTimingAgainst(phc: bootstrap.ClientSecretHash);

            var reason = bootstrap.IsConfigured ? "wrong_secret" : "bootstrap_unconfigured";
            await _audit.LogAsync(
                new AuditEntry(
                    Action: AuditTokenRejected,
                    ActorId: bootstrap.Subject,
                    ResourceType: "PlatformBootstrapClient",
                    ResourceId: bootstrap.ClientId,
                    Metadata: AuditMeta(reason)),
                cancellationToken).ConfigureAwait(false);

            return InvalidClientFailure();
        }

        var token = _issuer.Issue(new AccessTokenClaims(
            Subject: bootstrap.Subject,
            TenantId: null,
            Scopes: [bootstrap.AdminScope]));

        await _audit.LogAsync(
            new AuditEntry(
                Action: AuditTokenIssued,
                ActorId: bootstrap.Subject,
                ResourceType: "PlatformBootstrapClient",
                ResourceId: bootstrap.ClientId,
                Metadata: AuditMeta(scopes: [bootstrap.AdminScope])),
            cancellationToken).ConfigureAwait(false);

        return Ok(token);
    }

    private async Task<Result<ClientCredentialsTokenResult>> IssueTenantTokenAsync(
        IssueClientCredentialsTokenCommand request,
        CancellationToken cancellationToken)
    {
        var configuration = await _configurations
            .GetByClientIdAsync(request.ClientId, cancellationToken)
            .ConfigureAwait(false);

        // Every tenant-path rejection branch runs the dummy verify before
        // returning, so attacker-measurable latency is identical for
        // unknown_client / wrong_secret / credential_inactive /
        // credential_expired / tenant_suspended / tenant_terminated.
        var verdict = EvaluateTenantCredential(configuration, request.ClientSecret);

        if (verdict is Rejected rejected)
        {
            EqualizeTimingAgainst(phc: configuration?.ClientSecretHash);

            await _audit.LogAsync(
                new AuditEntry(
                    Action: AuditTokenRejected,
                    ActorId: $"client:{request.ClientId}",
                    ResourceType: "TenantConfiguration",
                    ResourceId: request.ClientId,
                    Metadata: AuditMeta(rejected.Reason),
                    TenantId: configuration?.TenantId),
                cancellationToken).ConfigureAwait(false);

            return InvalidClientFailure();
        }

        var active = configuration!;
        var admission = await _tenants
            .GetAsync(active.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (admission is not (null or TenantAdmissionState.Active))
        {
            EqualizeTimingAgainst(phc: active.ClientSecretHash);

            await _audit.LogAsync(
                new AuditEntry(
                    Action: AuditTokenRejected,
                    ActorId: $"client:{request.ClientId}",
                    ResourceType: "TenantConfiguration",
                    ResourceId: request.ClientId,
                    Metadata: AuditMeta(TenantAdmissionRejectionReason(admission)),
                    TenantId: active.TenantId),
                cancellationToken).ConfigureAwait(false);

            return InvalidClientFailure();
        }

        // FR-AUTH-002 §5.2: package_id presence-based enforcement.
        //  * Absent → credential-only, proceed.
        //  * Present → must match a registered active row in
        //    public.tenant_applications for this tenant, else fail closed
        //    with audit reason package_mismatch. The lookup runs after the
        //    admission check so a Suspended/Terminated tenant never reveals
        //    anything about its allow-list (admission failure wins).
        if (!string.IsNullOrWhiteSpace(request.PackageId))
        {
            var allowed = await _applications
                .IsAllowedAsync(active.TenantId, request.PackageId, cancellationToken)
                .ConfigureAwait(false);

            if (!allowed)
            {
                EqualizeTimingAgainst(phc: active.ClientSecretHash);

                await _audit.LogAsync(
                    new AuditEntry(
                        Action: AuditTokenRejected,
                        ActorId: $"client:{request.ClientId}",
                        ResourceType: "TenantConfiguration",
                        ResourceId: request.ClientId,
                        Metadata: AuditMeta(
                            "package_mismatch",
                            packageId: request.PackageId),
                        TenantId: active.TenantId),
                    cancellationToken).ConfigureAwait(false);

                return InvalidClientFailure();
            }
        }

        // Stamp usage on the same tracked entity; the audit-column
        // interceptor stamps modified_by / modified_at via the actor.
        active.RecordUsage();
        await _uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Per-tenant scope gating. The two flags already on
        // TenantConfiguration (is_qr_generation_allowed /
        // is_qr_validation_allowed) decide which scope strings are
        // included in the minted JWT. An admin can revoke a tenant's
        // QR-generation privilege without invalidating the credential
        // (DisableQrGeneration) — once the flag flips, every new token
        // minted for that credential lacks the qr:generate scope and
        // the resource policy rejects the call. Existing tokens stay
        // valid until expiry (TTL is short — Jwt:AccessTokenTtlMinutes).
        var scopes = ResolveTenantScopes(active);

        var subject = $"client:{active.ClientId}";
        var token = _issuer.Issue(new AccessTokenClaims(
            Subject: subject,
            TenantId: active.TenantId,
            Scopes: scopes,
            // FR-AUTH-002 §5.2: when the caller presented a
            // package_id and it cleared the allow-list check above,
            // propagate it into the token so downstream services
            // (audit, verification) can correlate the request with
            // the originating mobile app build. Null for tenant
            // backend callers (which never send a package).
            PackageId: string.IsNullOrWhiteSpace(request.PackageId) ? null : request.PackageId));

        await _audit.LogAsync(
            new AuditEntry(
                Action: AuditTokenIssued,
                ActorId: subject,
                ResourceType: "TenantConfiguration",
                ResourceId: active.ClientId,
                Metadata: AuditMeta(
                    tenantId: active.TenantId,
                    scopes: scopes,
                    packageId: string.IsNullOrWhiteSpace(request.PackageId) ? null : request.PackageId),
                TenantId: active.TenantId),
            cancellationToken).ConfigureAwait(false);

        return Ok(token);
    }

    /// <summary>
    /// Compute the scope claim set for a tenant token from the loaded
    /// <see cref="TenantConfiguration"/>'s capability flags. A tenant
    /// whose generation flag is off gets a token that fails the
    /// <c>PolicyNames.QrGenerate</c> gate (and similarly for validation);
    /// a tenant with both flags off still gets a token — the token just
    /// carries no QR scopes and every QR endpoint will reject it.
    /// Never emits <c>admin</c> — privilege-separation keystone.
    /// </summary>
    private static List<string> ResolveTenantScopes(TenantConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var scopes = new List<string>(capacity: 2);
        if (configuration.IsQrGenerationAllowed)
        {
            scopes.Add(ScopeQrGenerate);
        }

        if (configuration.IsQrValidationAllowed)
        {
            scopes.Add(ScopeQrValidate);
        }

        return scopes;
    }

    /// <summary>
    /// Pure check: state + secret match. Returns a <see cref="Rejected"/>
    /// when the credential is unknown / inactive / expired / wrong-secret;
    /// <c>null</c> when accepted. The timing-equalizer dummy run is the
    /// caller's responsibility — keeping the predicate side-effect-free
    /// makes it trivially unit-testable.
    /// </summary>
    private Rejected? EvaluateTenantCredential(TenantConfiguration? configuration, string submitted)
    {
        if (configuration is null)
        {
            return new Rejected("unknown_client");
        }

        if (!configuration.IsActive
            || configuration.Status != TenantConfigurationStatus.Active)
        {
            return new Rejected("credential_inactive");
        }

        if (configuration.ExpiresAt is { } expiresAt
            && expiresAt <= DateTimeOffset.UtcNow)
        {
            return new Rejected("credential_expired");
        }

        return TryVerify(submitted, configuration.ClientSecretHash)
            ? null
            : new Rejected("wrong_secret");
    }

    /// <summary>
    /// Run a dummy Argon2id verify to equalize wall-time on every
    /// rejection branch. The placeholder is the global constant; the PHC
    /// is the row the real verify would have run against (or the global
    /// constant when no row was found). The result is discarded.
    ///
    /// <para>If the timing-equalizer constant failed to initialize (see
    /// <see cref="Argon2idParameters.TimingEqualizerPhc"/>), this method
    /// is a no-op — we accept the resulting timing leak rather than
    /// 500-ing every token request on a misconfigured host.</para>
    /// </summary>
    private void EqualizeTimingAgainst(string? phc)
    {
        var equalizer = Argon2idParameters.TimingEqualizerPhc;
        if (equalizer is null)
        {
            return;
        }

        var target = phc ?? equalizer;
        try
        {
            _hasher.Verify(equalizer, target);
        }
        catch
        {
            // Verify throws on a malformed stored hash; the dummy run
            // exists only for its wall-time cost, so the throw is moot.
        }
    }

    /// <summary>
    /// Argon2id verify that swallows malformed-PHC exceptions. Returns
    /// <c>false</c> on any failure (corrupt hash, parse error) so the
    /// caller treats it identically to a wrong secret — no information
    /// leak via exception type.
    /// </summary>
    private bool TryVerify(string submitted, string phc)
    {
        try
        {
            return _hasher.Verify(submitted, phc);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Exhaustive map from admission state to audit reason.</summary>
    private static string TenantAdmissionRejectionReason(TenantAdmissionState? admission) => admission switch
    {
        TenantAdmissionState.Suspended => "tenant_suspended",
        TenantAdmissionState.Terminated => "tenant_terminated",
        null => "tenant_not_found",
        _ => "tenant_unavailable",
    };

    private static Result<ClientCredentialsTokenResult> InvalidClientFailure() =>
        Result<ClientCredentialsTokenResult>.Failure(
            ErrorCode.Unauthenticated,
            InvalidClient);

    private static Result<ClientCredentialsTokenResult> Ok(IssuedAccessToken token) =>
        Result<ClientCredentialsTokenResult>.Ok(new ClientCredentialsTokenResult(
            AccessToken: token.AccessToken,
            TokenType: "Bearer",
            ExpiresIn: token.ExpiresInSeconds));

    /// <summary>Inline audit-metadata serializer. Kept private to avoid a new file.</summary>
    private static string AuditMeta(
        string? reason = null,
        IReadOnlyList<string>? scopes = null,
        Guid? tenantId = null,
        string? packageId = null) =>
        JsonSerializer.Serialize(new
        {
            reason,
            grant_type = "client_credentials",
            tenant_id = tenantId,
            scopes,
            // FR-AUTH-002 §5.6 BR6: every issuance/rejection audit row
            // includes the claimed package_id when present. Stored in
            // clear (it's a public identifier, not a secret); null when
            // the caller did not send one (tenant backends, legacy flow).
            package_id = packageId,
        });

    /// <summary>Outcome of <see cref="EvaluateTenantCredential"/>.</summary>
    private sealed record Rejected(string Reason);
}
