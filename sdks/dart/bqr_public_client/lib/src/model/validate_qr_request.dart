//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:built_value/built_value.dart';
import 'package:built_value/serializer.dart';

part 'validate_qr_request.g.dart';

/// ValidateQrRequest
///
/// Properties:
/// * [qrPayload] 
/// * [requestId] 
/// * [requestTimestamp] 
@BuiltValue()
abstract class ValidateQrRequest implements Built<ValidateQrRequest, ValidateQrRequestBuilder> {
  @BuiltValueField(wireName: r'qrPayload')
  String get qrPayload;

  @BuiltValueField(wireName: r'requestId')
  String? get requestId;

  @BuiltValueField(wireName: r'requestTimestamp')
  DateTime? get requestTimestamp;

  ValidateQrRequest._();

  factory ValidateQrRequest([void updates(ValidateQrRequestBuilder b)]) = _$ValidateQrRequest;

  @BuiltValueHook(initializeBuilder: true)
  static void _defaults(ValidateQrRequestBuilder b) => b;

  @BuiltValueSerializer(custom: true)
  static Serializer<ValidateQrRequest> get serializer => _$ValidateQrRequestSerializer();
}

class _$ValidateQrRequestSerializer implements PrimitiveSerializer<ValidateQrRequest> {
  @override
  final Iterable<Type> types = const [ValidateQrRequest, _$ValidateQrRequest];

  @override
  final String wireName = r'ValidateQrRequest';

  Iterable<Object?> _serializeProperties(
    Serializers serializers,
    ValidateQrRequest object, {
    FullType specifiedType = FullType.unspecified,
  }) sync* {
    yield r'qrPayload';
    yield serializers.serialize(
      object.qrPayload,
      specifiedType: const FullType(String),
    );
    if (object.requestId != null) {
      yield r'requestId';
      yield serializers.serialize(
        object.requestId,
        specifiedType: const FullType.nullable(String),
      );
    }
    if (object.requestTimestamp != null) {
      yield r'requestTimestamp';
      yield serializers.serialize(
        object.requestTimestamp,
        specifiedType: const FullType.nullable(DateTime),
      );
    }
  }

  @override
  Object serialize(
    Serializers serializers,
    ValidateQrRequest object, {
    FullType specifiedType = FullType.unspecified,
  }) {
    return _serializeProperties(serializers, object, specifiedType: specifiedType).toList();
  }

  void _deserializeProperties(
    Serializers serializers,
    Object serialized, {
    FullType specifiedType = FullType.unspecified,
    required List<Object?> serializedList,
    required ValidateQrRequestBuilder result,
    required List<Object?> unhandled,
  }) {
    for (var i = 0; i < serializedList.length; i += 2) {
      final key = serializedList[i] as String;
      final value = serializedList[i + 1];
      switch (key) {
        case r'qrPayload':
          final valueDes = serializers.deserialize(
            value,
            specifiedType: const FullType(String),
          ) as String;
          result.qrPayload = valueDes;
          break;
        case r'requestId':
          final valueDes = serializers.deserialize(
            value,
            specifiedType: const FullType.nullable(String),
          ) as String?;
          if (valueDes == null) continue;
          result.requestId = valueDes;
          break;
        case r'requestTimestamp':
          final valueDes = serializers.deserialize(
            value,
            specifiedType: const FullType.nullable(DateTime),
          ) as DateTime?;
          if (valueDes == null) continue;
          result.requestTimestamp = valueDes;
          break;
        default:
          unhandled.add(key);
          unhandled.add(value);
          break;
      }
    }
  }

  @override
  ValidateQrRequest deserialize(
    Serializers serializers,
    Object serialized, {
    FullType specifiedType = FullType.unspecified,
  }) {
    final result = ValidateQrRequestBuilder();
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


