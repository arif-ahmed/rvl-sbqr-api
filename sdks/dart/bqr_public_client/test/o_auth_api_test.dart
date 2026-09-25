import 'package:test/test.dart';
import 'package:bqr_public_client/bqr_public_client.dart';


/// tests for OAuthApi
void main() {
  final instance = BqrPublicClient().getOAuthApi();

  group(OAuthApi, () {
    //Future<TokenResponse> v1OauthTokenPost(String grantType, String clientId, String clientSecret, { String packageId }) async
    test('test v1OauthTokenPost', () async {
      // TODO
    });

  });
}
