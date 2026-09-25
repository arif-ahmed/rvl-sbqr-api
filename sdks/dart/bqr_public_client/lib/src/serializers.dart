//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_import

import 'package:one_of_serializer/any_of_serializer.dart';
import 'package:one_of_serializer/one_of_serializer.dart';
import 'package:built_collection/built_collection.dart';
import 'package:built_value/json_object.dart';
import 'package:built_value/serializer.dart';
import 'package:built_value/standard_json_plugin.dart';
import 'package:built_value/iso_8601_date_time_serializer.dart';
import 'package:bqr_public_client/src/date_serializer.dart';
import 'package:bqr_public_client/src/model/date.dart';

import 'package:bqr_public_client/src/model/generate_dynamic_qr_request.dart';
import 'package:bqr_public_client/src/model/generate_qr_response.dart';
import 'package:bqr_public_client/src/model/generate_static_qr_request.dart';
import 'package:bqr_public_client/src/model/o_auth_error_response.dart';
import 'package:bqr_public_client/src/model/problem_details.dart';
import 'package:bqr_public_client/src/model/token_request.dart';
import 'package:bqr_public_client/src/model/token_response.dart';
import 'package:bqr_public_client/src/model/validate_qr_request.dart';
import 'package:bqr_public_client/src/model/validate_qr_response.dart';

part 'serializers.g.dart';

@SerializersFor([
  GenerateDynamicQrRequest,
  GenerateQrResponse,
  GenerateStaticQrRequest,
  OAuthErrorResponse,
  ProblemDetails,
  TokenRequest,
  TokenResponse,
  ValidateQrRequest,
  ValidateQrResponse,
])
Serializers serializers = (_$serializers.toBuilder()
      ..add(const OneOfSerializer())
      ..add(const AnyOfSerializer())
      ..add(const DateSerializer())
      ..add(Iso8601DateTimeSerializer())
    ).build();

Serializers standardSerializers =
    (serializers.toBuilder()..addPlugin(StandardJsonPlugin())).build();
