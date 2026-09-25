//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

import 'dart:async';

import 'package:built_value/json_object.dart';
import 'package:built_value/serializer.dart';
import 'package:dio/dio.dart';

import 'package:bqr_public_client/src/api_util.dart';
import 'package:bqr_public_client/src/model/o_auth_error_response.dart';
import 'package:bqr_public_client/src/model/problem_details.dart';
import 'package:bqr_public_client/src/model/token_response.dart';

class OAuthApi {

  final Dio _dio;

  final Serializers _serializers;

  const OAuthApi(this._dio, this._serializers);

  /// v1OauthTokenPost
  /// 
  ///
  /// Parameters:
  /// * [grantType] - Must be \\\"client_credentials\\\".
  /// * [clientId] - The client identifier (platform bootstrap client or a tenant FI credential).
  /// * [clientSecret] - The client secret. Never logged, never persisted.
  /// * [packageId] - Optional. Mobile-app package identifier (Android applicationId or iOS bundle ID) for the FR-AUTH-002 allow-list check. Tenant backends omit this; mobile apps calling the platform directly should send it.
  /// * [cancelToken] - A [CancelToken] that can be used to cancel the operation
  /// * [headers] - Can be used to add additional headers to the request
  /// * [extras] - Can be used to add flags to the request
  /// * [validateStatus] - A [ValidateStatus] callback that can be used to determine request success based on the HTTP status of the response
  /// * [onSendProgress] - A [ProgressCallback] that can be used to get the send progress
  /// * [onReceiveProgress] - A [ProgressCallback] that can be used to get the receive progress
  ///
  /// Returns a [Future] containing a [Response] with a [TokenResponse] as data
  /// Throws [DioException] if API call or serialization fails
  Future<Response<TokenResponse>> v1OauthTokenPost({ 
    required String grantType,
    required String clientId,
    required String clientSecret,
    String? packageId,
    CancelToken? cancelToken,
    Map<String, dynamic>? headers,
    Map<String, dynamic>? extra,
    ValidateStatus? validateStatus,
    ProgressCallback? onSendProgress,
    ProgressCallback? onReceiveProgress,
  }) async {
    final _path = r'/v1/oauth/token';
    final _options = Options(
      method: r'POST',
      headers: <String, dynamic>{
        ...?headers,
      },
      extra: <String, dynamic>{
        'secure': <Map<String, String>>[],
        ...?extra,
      },
      contentType: 'application/x-www-form-urlencoded',
      validateStatus: validateStatus,
    );

    dynamic _bodyData;

    try {
      _bodyData = <String, dynamic>{
        r'grant_type': encodeQueryParameter(_serializers, grantType, const FullType(String)),
        r'client_id': encodeQueryParameter(_serializers, clientId, const FullType(String)),
        r'client_secret': encodeQueryParameter(_serializers, clientSecret, const FullType(String)),
        if (packageId != null) r'package_id': encodeQueryParameter(_serializers, packageId, const FullType(String)),
      };

    } catch(error, stackTrace) {
      throw DioException(
         requestOptions: _options.compose(
          _dio.options,
          _path,
        ),
        type: DioExceptionType.unknown,
        error: error,
        stackTrace: stackTrace,
      );
    }

    final _response = await _dio.request<Object>(
      _path,
      data: _bodyData,
      options: _options,
      cancelToken: cancelToken,
      onSendProgress: onSendProgress,
      onReceiveProgress: onReceiveProgress,
    );

    TokenResponse? _responseData;

    try {
      final rawResponse = _response.data;
      _responseData = rawResponse == null ? null : _serializers.deserialize(
        rawResponse,
        specifiedType: const FullType(TokenResponse),
      ) as TokenResponse;

    } catch (error, stackTrace) {
      throw DioException(
        requestOptions: _response.requestOptions,
        response: _response,
        type: DioExceptionType.unknown,
        error: error,
        stackTrace: stackTrace,
      );
    }

    return Response<TokenResponse>(
      data: _responseData,
      headers: _response.headers,
      isRedirect: _response.isRedirect,
      requestOptions: _response.requestOptions,
      redirects: _response.redirects,
      statusCode: _response.statusCode,
      statusMessage: _response.statusMessage,
      extra: _response.extra,
    );
  }

}
