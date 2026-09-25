namespace SBQR.Api.Infrastructure.OpenApi;

/// <summary>
/// Marker constant for the internal-admin document split. Controllers
/// carrying <c>[ApiExplorerSettings(GroupName = GroupNameDocumentFilter.InternalGroup)]</c>
/// flow into the <c>v1.internal-admin</c> OpenAPI document; everything else
/// flows into <c>v1.public</c>.
/// </summary>
/// <remarks>
/// <para>
/// The split is enforced via <c>OpenApiOptions.ShouldInclude</c> in
/// <c>Program.cs</c> (one predicate per document, checked against each
/// action's <see cref="Microsoft.AspNetCore.Mvc.ApiExplorer.ApiDescription.GroupName"/>)
/// — see
/// <c>docs/superpowers/specs/2026-09-04-public-vs-internal-api-docs-design.md</c>
/// §3a. An earlier version of this file implemented the split with two
/// <c>IOpenApiDocumentTransformer</c>s that filtered on
/// <c>OpenApiOperation.Tags</c>, on the assumption that
/// <c>ApiExplorerSettings.GroupName</c> becomes an operation tag. It does
/// not: <c>Microsoft.AspNetCore.OpenApi</c>'s native generator never
/// populates that tag from <c>GroupName</c>, so that approach silently
/// dropped every internal-tagged action from <c>v1.internal-admin</c> — the
/// document explorer group was reachable via <c>/admin/_routes</c> in the
/// live route table but never appeared in the internal-admin OpenAPI
/// document. Verified against a running instance in
/// <c>docs/superpowers/specs/2026-09-04-public-vs-internal-api-docs-design.md</c>.
/// </para>
/// </remarks>
public static class GroupNameDocumentFilter
{
    /// <summary>The canonical group name for the internal-admin document.</summary>
    public const string InternalGroup = "internal";
}
