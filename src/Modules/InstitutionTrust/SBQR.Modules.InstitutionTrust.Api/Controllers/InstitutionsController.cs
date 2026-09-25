using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SBQR.Modules.InstitutionTrust.Application.Commands;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.InstitutionTrust.Api.Controllers;

/// <summary>
/// HTTP surface for the manual trust directory (Sep-17 scope). The BB
/// Trust-Sync pipeline replaces this seeding endpoint post-deadline; the
/// read side (<c>GetInstitutionPublicKeyQuery</c>) stays unchanged.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/admin/institutions")]
[Authorize(Policy = PolicyNames.AdminCredentialTree)]
[ApiExplorerSettings(GroupName = "internal")]
[Produces("application/json")]
public sealed class InstitutionsController : ControllerBase
{
    private readonly ISender _mediator;

    public InstitutionsController(ISender mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    /// <summary>
    /// <c>POST /v1/admin/institutions</c> — register (or update) an institution
    /// in the trust directory and publish its ACTIVE public key, retiring
    /// the previous active version.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(UpsertedInstitution), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpsertAsync(
        [FromBody] UpsertInstitutionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UpsertInstitutionCommand(
                request.InstitutionCode,
                request.InstituteType,
                request.InstitutionName,
                request.PublicKeyPem),
            cancellationToken);

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : BadRequest(new { error = result.ErrorCode, message = result.ErrorMessage });
    }
}

/// <summary>Inbound JSON DTO for the trust-directory upsert.</summary>
public sealed record UpsertInstitutionRequest(
    string InstitutionCode,
    string InstituteType,
    string InstitutionName,
    string PublicKeyPem);
