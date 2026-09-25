import 'package:test/test.dart';
import 'package:bqr_public_client/bqr_public_client.dart';


/// tests for QrGenerationApi
void main() {
  final instance = BqrPublicClient().getQrGenerationApi();

  group(QrGenerationApi, () {
    //Future<GenerateQrResponse> v1QrGenerateDynamicPost(GenerateDynamicQrRequest generateDynamicQrRequest, { String idempotencyKey }) async
    test('test v1QrGenerateDynamicPost', () async {
      // TODO
    });

    //Future<GenerateQrResponse> v1QrGenerateStaticPost(GenerateStaticQrRequest generateStaticQrRequest, { String idempotencyKey }) async
    test('test v1QrGenerateStaticPost', () async {
      // TODO
    });

  });
}
