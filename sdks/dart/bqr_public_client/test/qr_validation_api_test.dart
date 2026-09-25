import 'package:test/test.dart';
import 'package:bqr_public_client/bqr_public_client.dart';


/// tests for QrValidationApi
void main() {
  final instance = BqrPublicClient().getQrValidationApi();

  group(QrValidationApi, () {
    //Future<ValidateQrResponse> v1QrValidatePost(ValidateQrRequest validateQrRequest) async
    test('test v1QrValidatePost', () async {
      // TODO
    });

  });
}
