# BQR Public API — Dart/Flutter SDK

This is the mobile/client-facing SDK for **BQR Secure Manager's `v1.public` API**
— OAuth2 token exchange, BanglaQR P2P generation (static + dynamic), and QR
validation. It's a generated client (`openapi-generator-cli`, `dart-dio`
generator) built from the same OpenAPI document served live at
`/openapi/v1.public.json`, so its shapes always match the real API.

If you just want to call the API from a Flutter app, you don't need to read
the generator internals below — jump to **Quick start**.

## Coming from Swagger? Start here

If you're used to exploring an API through Swagger UI, you're not starting
from zero here — everything below is generated from (or maps 1:1 onto) the
same OpenAPI 3.0 document a Swagger-style explorer would show you.

**1. Browse and try requests first, before writing any Dart.**
This API's interactive docs run on [Scalar](https://scalar.com) instead of
classic Swagger UI, but it's the same idea — pick an operation, expand it,
hit "Test Request", see the real response:

- Local/dev: `http://localhost:5001/docs/public`
- The raw spec it's built from: `http://localhost:5001/openapi/v1.public.json`

Point that URL at anything you already know how to use — Postman's
"Import → Link", Insomnia, or your own `swagger-codegen`/`openapi-generator`
run for a different platform (Kotlin/Swift SDKs would start from the exact
same file). This Dart package is just one such generation target, already
done for you.

**2. The mapping from "Swagger tag" to "Dart class" is exact:**

| In the OpenAPI doc / Scalar UI | In this SDK |
|---|---|
| Operations under the **OAuth** tag | `client.getOAuthApi()` → `OAuthApi` |
| Operations under the **QrGeneration** tag | `client.getQrGenerationApi()` → `QrGenerationApi` |
| Operations under the **QrValidation** tag | `client.getQrValidationApi()` → `QrValidationApi` |
| An operation's `operationId` (e.g. `v1QrGenerateStaticPost`) | The exact method name on that class |
| A `requestBody` schema (e.g. `GenerateStaticQrRequest`) | A `built_value` class of the same name, built via a `(b) => b..field = value` builder instead of a raw JSON map |
| The "Schemas" section at the bottom of the docs page | `doc/*.md` inside `sdks/dart/bqr_public_client/` — one Markdown file per model/API class, generated alongside the code |
| Clicking "Try it" and reading the response body | Calling the method and reading `response.data` (already deserialized into the matching model — no manual `jsonDecode`) |

**3. What Swagger UI can't show you, because the raw spec doesn't declare it,
this SDK still needed to work — so it's patched in at generation time:**
the `Authorization: Bearer <token>` header handling, and the actual
parameter list for `POST /v1/oauth/token` (Swagger shows that operation with
no request body — see **Known gap** at the bottom). Everything else you see
in Scalar is exactly what you get here.

If you'd rather skip the generated client entirely and call the API with raw
`dio`/`http` requests the way you'd hand-roll calls from Swagger's example
`curl` command, that works too — every endpoint, header, and status code
documented below applies whether you use `bqr_public_client` or your own
HTTP calls. The SDK just saves you from hand-writing the request/response
models and remembering header casing.

## Package

`sdks/dart/bqr_public_client/` — a standalone Dart package (`dio` + `built_value`
under the hood). It has no dependency on the rest of this repo; copy or
reference the folder directly.

## Installing it in your Flutter app

This SDK isn't published to pub.dev — add it as a **path** or **git**
dependency in your app's `pubspec.yaml`.

**Path dependency** (if the mobile app and this repo live in the same
workspace / are checked out side-by-side):

```yaml
dependencies:
  bqr_public_client:
    path: ../rvl-secure-bqr-manager/sdks/dart/bqr_public_client
```

**Git dependency** (recommended for a mobile repo that doesn't check out the
whole backend monorepo):

```yaml
dependencies:
  bqr_public_client:
    git:
      url: <this repo's git URL>
      path: sdks/dart/bqr_public_client
      ref: main   # or a tag once the SDK is versioned/tagged
```

Then:

```bash
flutter pub get

# Required — the models use built_value, and this package ships without
# the generated .g.dart part files. Without this step you'll get
# "Error: Part file '...g.dart' can't be loaded" and the package won't compile.
dart run build_runner build --delete-conflicting-outputs
```

Re-run `build_runner build` any time you pull a new version of this SDK.

## Quick start

Each step below names the operation as it appears in Scalar/the OpenAPI doc,
so you can cross-reference the docs page and the code side by side.

### 1. Get an access token

**Swagger/Scalar: `OAuth` tag → `POST /v1/oauth/token`**

Every endpoint except `POST /v1/oauth/token` requires a bearer token. Tokens
are minted via the OAuth 2.1 client-credentials grant — your tenant's
`client_id` / `client_secret` (issued by the BQR platform team, not something
a mobile app generates itself).

```dart
import 'package:bqr_public_client/bqr_public_client.dart';

final client = BqrPublicClient(basePathOverride: 'https://api.bqr.example.com');

final tokenResponse = await client.getOAuthApi().v1OauthTokenPost(
  grantType: 'client_credentials',
  clientId: 'YOUR_CLIENT_ID',
  clientSecret: 'YOUR_CLIENT_SECRET',
  // Optional — send your app's package/bundle id if your tenant has
  // enabled the mobile-app allow-list (FR-AUTH-002). Omit if unused.
  packageId: 'com.example.bqrapp',
);

final accessToken = tokenResponse.data!.accessToken;

// Attach it to every subsequent call on this client instance.
client.setBearerAuth('bearerAuth', accessToken);
```

`expiresIn` (seconds) tells you when to refresh — there is no refresh-token
grant, just repeat the client-credentials exchange when the token expires.

> **Never ship `client_secret` inside the mobile app binary.** For a
> device-facing app, the client credentials belong to your backend, which
> should mint a token and hand a short-lived one to the device — or the app
> talks to your own backend, which in turn calls BQR. Treat this SDK's OAuth
> call as a **server-to-server** credential exchange unless your tenant has
> explicitly opted into the FR-AUTH-002 mobile allow-list flow with
> `packageId`.

### 2. Generate a static QR (no fixed amount)

**Swagger/Scalar: `QrGeneration` tag → `POST /v1/qr/generate/static`**

```dart
final response = await client.getQrGenerationApi().v1QrGenerateStaticPost(
  generateStaticQrRequest: GenerateStaticQrRequest((b) => b
    ..recipientName = 'Areeba Nawar'
    ..recipientCity = 'Dhaka'
    ..recipientPan = '01711111111'
    ..postalCode = '1207'
  ),
  // Required — see "Idempotency" below.
  idempotencyKey: 'a-uuid-you-generate-per-attempt',
);

final qr = response.data!;
print(qr.qrPayload);   // the TLV-encoded, signed QR string
print(qr.qrType);      // "static"
```

### 3. Generate a dynamic QR (fixed amount)

**Swagger/Scalar: `QrGeneration` tag → `POST /v1/qr/generate/dynamic`**

```dart
final response = await client.getQrGenerationApi().v1QrGenerateDynamicPost(
  generateDynamicQrRequest: GenerateDynamicQrRequest((b) => b
    ..transactionAmount = '500.00'
    ..recipientName = 'Areeba Nawar'
    ..recipientCity = 'Dhaka'
    ..recipientPan = '01711111111'
  ),
  idempotencyKey: 'a-uuid-you-generate-per-attempt',
);
```

### 4. Validate a scanned/uploaded QR

**Swagger/Scalar: `QrValidation` tag → `POST /v1/qr/validate`**

```dart
final response = await client.getQrValidationApi().v1QrValidatePost(
  validateQrRequest: ValidateQrRequest((b) => b
    ..qrPayload = scannedRawString
  ),
);

final result = response.data!;
switch (result.verdict) {
  case 'VALID':
    // Show result.recipientName / result.recipientPan on the pre-transaction
    // review screen (AGENTS.md §6) before letting the user confirm payment.
    break;
  default:
    // Every non-VALID verdict is a rejection — see "Verdicts" below.
    break;
}
```

## Verdicts (`ValidateQrResponse.verdict`)

A `200` from `/v1/qr/validate` is **always** returned — a rejection is a
normal response, not an HTTP error. Check `verdict`:

| Verdict | Meaning | Show the user |
|---|---|---|
| `VALID` | Signature verified against a trusted key | Proceed to the pre-transaction review screen |
| `INVALID_SIGNATURE` | Signature present but cryptographically invalid, or missing/malformed | Reject — "This QR could not be verified" |
| `STRUCTURAL_INVALID` | Malformed payload / CRC mismatch / missing mandatory tags | Reject — "This is not a valid BanglaQR code" |
| `KEY_NOT_FOUND` | Issuing institution's key isn't in the trust store | Reject |
| `KEY_SUSPENDED` | Issuing institution's key is suspended | Reject |
| `KEY_REVOKED` | Issuing institution's key is revoked (compromised) | Reject |
| `KEY_NOT_ACTIVE` | Key exists but isn't active yet (pending/generating/retiring) | Reject |
| `NON_P2P` | Structurally valid but not a P2P QR (not an error, just out of scope for this flow) | Route to P2M handling if your app supports it |
| `REQUEST_STALE` | Your `requestTimestamp` was outside the ±5 min server window | Retry with a fresh timestamp (the SDK sets this automatically if you omit it) |
| `REQUEST_REPLAYED` | This exact request was already validated once | Don't resubmit the same request id |

**Never initiate a transfer on anything other than `VALID`.**

## Idempotency

`Idempotency-Key` is **required** on both generate endpoints. Generate a
fresh UUID per user-initiated attempt; if you retry the *same* attempt
(e.g. after a network timeout), reuse the *same* key — the API returns `409`
if a key was already used with a different payload, which tells you the
first attempt already succeeded.

## Error handling

Non-2xx responses throw `DioException`. The response body is one of:

- `OAuthErrorResponse` (`{"error": "invalid_client" | "invalid_request" | "unsupported_grant_type"}`) — from `/v1/oauth/token` only.
- A `{"error": "...", "message": "..."}` shape from the QR-generation endpoints (`error` values: `Unauthenticated`, `NotFound`, `TENANT_NOT_ACTIVE`, `ValidationFailed`, `KEY_NOT_ACTIVE`, `DUPLICATE_IDEMPOTENCY_KEY`, `SIGNING_FAILED`, `GENERATION_FAILED`).
- `ProblemDetails` (RFC 7807) — validation failures and `429 Too Many Requests`.

```dart
try {
  await client.getQrGenerationApi().v1QrGenerateStaticPost(...);
} on DioException catch (e) {
  final status = e.response?.statusCode;
  final body = e.response?.data; // Map<String, dynamic> — inspect `error`/`message`
  // 401 -> token expired, re-authenticate
  // 403 -> tenant not active for QR generation
  // 409 -> Idempotency-Key already used
  // 422 -> tenant has no ACTIVE signing key (ops issue, not user-fixable)
  // 429 -> back off and retry
}
```

## Scopes

Your `client_id` must have been provisioned with the right scope by the
platform team — `qr:generate` for the generation endpoints, `qr:validate`
for validation. A `403` on an otherwise-valid token means your tenant
credential doesn't carry the scope the endpoint requires.

## Regenerating this SDK

You shouldn't need to — but if the backend team changes the `v1.public` API
surface and hands you a new SDK, drop the new `sdks/dart/bqr_public_client/`
in over the old one and re-run `flutter pub get`. See
`tools/dart-sdk-export/README.md` if you're the one regenerating it.

## Known gap (why the SDK shows more than Scalar/Swagger does)

Open `http://localhost:5001/docs/public` and look at `POST /v1/oauth/token`
yourself: it has no request body documented, and there's no padlock/security
scheme shown on any operation. That's not a doc bug you need to report — the
live OpenAPI document genuinely doesn't declare either one (the controller
reads the token request manually instead of via a typed parameter, and the
native ASP.NET Core OpenAPI generator doesn't infer a security scheme from
`[Authorize]` alone). Both gaps are patched onto a throwaway copy of the spec
at SDK-generation time only (see `tools/dart-sdk-export/patch-openapi-security.js`)
so `v1OauthTokenPost()` actually takes `grantType`/`clientId`/`clientSecret`/
`packageId`, and `setBearerAuth()` actually attaches the header. The real API
behavior is unchanged either way — this only affects what the generated
Dart method signatures look like versus what you'd see in the docs page.
