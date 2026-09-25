import 'package:test/test.dart';
import 'package:bqr_public_client/bqr_public_client.dart';


/// tests for SBQRApiApi
void main() {
  final instance = BqrPublicClient().getSBQRApiApi();

  group(SBQRApiApi, () {
    //Future healthLiveGet() async
    test('test healthLiveGet', () async {
      // TODO
    });

    //Future healthReadyGet() async
    test('test healthReadyGet', () async {
      // TODO
    });

    //Future rootGet() async
    test('test rootGet', () async {
      // TODO
    });

  });
}
