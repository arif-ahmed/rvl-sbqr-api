import 'package:test/test.dart';
import 'package:bqr_public_client/bqr_public_client.dart';

// tests for TokenRequest
void main() {
  final instance = TokenRequestBuilder();
  // TODO add properties to the builder and call build()

  group(TokenRequest, () {
    // Must be \"client_credentials\".
    // String grantType
    test('to test the property `grantType`', () async {
      // TODO
    });

    // The client identifier (platform bootstrap client or a tenant FI credential).
    // String clientId
    test('to test the property `clientId`', () async {
      // TODO
    });

    // The client secret. Never logged, never persisted.
    // String clientSecret
    test('to test the property `clientSecret`', () async {
      // TODO
    });

    // Optional. Mobile-app package identifier (Android applicationId or iOS bundle ID) for the FR-AUTH-002 allow-list check. Tenant backends omit this; mobile apps calling the platform directly should send it.
    // String packageId
    test('to test the property `packageId`', () async {
      // TODO
    });

  });
}
