#!/usr/bin/env node
// Patches a bearer-auth security scheme onto a fetched v1.public OpenAPI
// document before it is fed to openapi-generator-cli.
//
// Why this exists: SBQR.Api uses the native Microsoft.AspNetCore.OpenApi
// generator (see src/Host/SBQR.Api/Program.cs §8), which does NOT emit an
// OpenAPI `securitySchemes` entry or per-operation `security` requirement
// from [Authorize(Policy = ...)] alone — verified against a running
// instance: components.securitySchemes is empty in the raw document. Feeding
// that document straight to openapi-generator-cli produces a Dart client
// with no Authorization-header plumbing at all, which is the wrong
// experience for SDK consumers (the mobile/tenant-backend team still needs
// to attach a bearer token to every call except the token endpoint itself).
//
// This script is generation-tooling only. It never touches the real API
// contract or Program.cs — it patches a throwaway copy of the fetched JSON
// so the generated Dart client ships with a working `setBearerAuth(token)` /
// `ApiClient(authentication: ...)` seam.
//
// Usage: node patch-openapi-security.js <input.json> <output.json>

const fs = require('fs');

const [, , inputPath, outputPath] = process.argv;
if (!inputPath || !outputPath) {
  console.error('Usage: node patch-openapi-security.js <input.json> <output.json>');
  process.exit(2);
}

const doc = JSON.parse(fs.readFileSync(inputPath, 'utf8'));

// Endpoints that are genuinely anonymous per AGENTS.md / the controllers
// themselves (OAuthController is [AllowAnonymous] by definition — the
// client_secret IS the authentication; health/root carry no auth at all).
const ANONYMOUS_PATHS = new Set([
  '/v1/oauth/token',
  '/health/live',
  '/health/ready',
  '/',
]);

doc.components = doc.components || {};
doc.components.schemas = doc.components.schemas || {};

// ---------------------------------------------------------------------------
// POST /v1/oauth/token has no requestBody in the raw document: OAuthController
// (src/Modules/IdentityAccess/.../Controllers/OAuthController.cs) reads the
// body manually via Request.ReadFormAsync()/ReadFromJsonAsync<TokenRequest>()
// rather than a typed [FromBody]/[FromForm] action parameter, so ApiExplorer
// never sees a request-body shape to describe. Without this patch,
// openapi-generator emits a zero-argument v1OauthTokenPost() that cannot
// actually send credentials. The schema below mirrors
// SBQR.Modules.IdentityAccess.Api.Contracts.TokenRequest field-for-field
// (RFC 6749 §4.3 grant_type/client_id/client_secret + the FR-AUTH-002
// package_id allow-list field) — generation-tooling only, never touches the
// real API contract.
// ---------------------------------------------------------------------------
doc.components.schemas.TokenRequest = {
  type: 'object',
  required: ['grant_type', 'client_id', 'client_secret'],
  properties: {
    grant_type: { type: 'string', description: 'Must be "client_credentials".', example: 'client_credentials' },
    client_id: { type: 'string', description: 'The client identifier (platform bootstrap client or a tenant FI credential).' },
    client_secret: { type: 'string', description: 'The client secret. Never logged, never persisted.' },
    package_id: {
      type: 'string',
      description:
        'Optional. Mobile-app package identifier (Android applicationId or iOS bundle ID) for the ' +
        'FR-AUTH-002 allow-list check. Tenant backends omit this; mobile apps calling the platform ' +
        'directly should send it.',
    },
  },
};

const tokenOperation = (doc.paths || {})['/v1/oauth/token']?.post;
if (tokenOperation) {
  tokenOperation.requestBody = {
    required: true,
    content: {
      'application/x-www-form-urlencoded': {
        schema: { $ref: '#/components/schemas/TokenRequest' },
      },
      'application/json': {
        schema: { $ref: '#/components/schemas/TokenRequest' },
      },
    },
  };
}

doc.components.securitySchemes = {
  ...(doc.components.securitySchemes || {}),
  bearerAuth: {
    type: 'http',
    scheme: 'bearer',
    bearerFormat: 'JWT',
    description:
      'Access token minted by POST /v1/oauth/token (OAuth 2.1 client-credentials grant). ' +
      'Required on every other v1.public endpoint.',
  },
};

let patchedCount = 0;
for (const [pathKey, pathItem] of Object.entries(doc.paths || {})) {
  if (ANONYMOUS_PATHS.has(pathKey)) continue;
  for (const method of ['get', 'put', 'post', 'delete', 'patch', 'head', 'options']) {
    const operation = pathItem[method];
    if (!operation || typeof operation !== 'object') continue;
    operation.security = [{ bearerAuth: [] }];
    patchedCount++;
  }
}

fs.writeFileSync(outputPath, JSON.stringify(doc, null, 2));
console.log(`Patched bearerAuth security scheme onto ${patchedCount} operation(s).`);
console.log(`Wrote: ${outputPath}`);
