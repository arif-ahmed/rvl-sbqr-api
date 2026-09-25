//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:built_value/built_value.dart';
import 'package:built_value/serializer.dart';

part 'o_auth_error_response.g.dart';

/// OAuthErrorResponse
///
/// Properties:
/// * [error] 
@BuiltValue()
abstract class OAuthErrorResponse implements Built<OAuthErrorResponse, OAuthErrorResponseBuilder> {
  @BuiltValueField(wireName: r'error')
  String get error;

  OAuthErrorResponse._();

  factory OAuthErrorResponse([void updates(OAuthErrorResponseBuilder b)]) = _$OAuthErrorResponse;

  @BuiltValueHook(initializeBuilder: true)
  static void _defaults(OAuthErrorResponseBuilder b) => b;

  @BuiltValueSerializer(custom: true)
  static Serializer<OAuthErrorResponse> get serializer => _$OAuthErrorResponseSerializer();
}

class _$OAuthErrorResponseSerializer implements PrimitiveSerializer<OAuthErrorResponse> {
  @override
  final Iterable<Type> types = const [OAuthErrorResponse, _$OAuthErrorResponse];

  @override
  final String wireName = r'OAuthErrorResponse';

  Iterable<Object?> _serializeProperties(
    Serializers serializers,
    OAuthErrorResponse object, {
    FullType specifiedType = FullType.unspecified,
  }) sync* {
    yield r'error';
    yield serializers.serialize(
      object.error,
      specifiedType: const FullType(String),
    );
  }

  @override
  Object serialize(
    Serializers serializers,
    OAuthErrorResponse object, {
    FullType specifiedType = FullType.unspecified,
  }) {
    return _serializeProperties(serializers, object, specifiedType: specifiedType).toList();
  }

  void _deserializeProperties(
    Serializers serializers,
    Object serialized, {
    FullType specifiedType = FullType.unspecified,
    required List<Object?> serializedList,
    required OAuthErrorResponseBuilder result,
    required List<Object?> unhandled,
  }) {
    for (var i = 0; i < serializedList.length; i += 2) {
      final key = serializedList[i] as String;
      final value = serializedList[i + 1];
      switch (key) {
        case r'error':
          final valueDes = serializers.deserialize(
            value,
            specifiedType: const FullType(String),
          ) as String;
          result.error = valueDes;
          break;
        default:
          unhandled.add(key);
          unhandled.add(value);
          break;
      }
    }
  }

  @override
  OAuthErrorResponse deserialize(
    Serializers serializers,
    Object serialized, {
    FullType specifiedType = FullType.unspecified,
  }) {
    final result = OAuthErrorResponseBuilder();
    final serializedList = (serialized as Iterable<Object?>).toList();
    final unhandled = <Object?>[];
    _deserializeProperties(
      serializers,
      serialized,
      specifiedType: specifiedType,
      serializedList: serializedList,
      unhandled: unhandled,
      result: result,
    );
    return result.build();
  }
}


