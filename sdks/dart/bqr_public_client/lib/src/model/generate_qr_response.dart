//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:built_value/json_object.dart';
import 'package:built_value/built_value.dart';
import 'package:built_value/serializer.dart';

part 'generate_qr_response.g.dart';

/// GenerateQrResponse
///
/// Properties:
/// * [qrPayload] 
/// * [payloadHash] 
/// * [qrType] 
/// * [signatureKeyVersion] 
@BuiltValue()
abstract class GenerateQrResponse implements Built<GenerateQrResponse, GenerateQrResponseBuilder> {
  @BuiltValueField(wireName: r'qrPayload')
  String get qrPayload;

  @BuiltValueField(wireName: r'payloadHash')
  String get payloadHash;

  @BuiltValueField(wireName: r'qrType')
  String get qrType;

  @BuiltValueField(wireName: r'signatureKeyVersion')
  JsonObject? get signatureKeyVersion;

  GenerateQrResponse._();

  factory GenerateQrResponse([void updates(GenerateQrResponseBuilder b)]) = _$GenerateQrResponse;

  @BuiltValueHook(initializeBuilder: true)
  static void _defaults(GenerateQrResponseBuilder b) => b;

  @BuiltValueSerializer(custom: true)
  static Serializer<GenerateQrResponse> get serializer => _$GenerateQrResponseSerializer();
}

class _$GenerateQrResponseSerializer implements PrimitiveSerializer<GenerateQrResponse> {
  @override
  final Iterable<Type> types = const [GenerateQrResponse, _$GenerateQrResponse];

  @override
  final String wireName = r'GenerateQrResponse';

  Iterable<Object?> _serializeProperties(
    Serializers serializers,
    GenerateQrResponse object, {
    FullType specifiedType = FullType.unspecified,
  }) sync* {
    yield r'qrPayload';
    yield serializers.serialize(
      object.qrPayload,
      specifiedType: const FullType(String),
    );
    yield r'payloadHash';
    yield serializers.serialize(
      object.payloadHash,
      specifiedType: const FullType(String),
    );
    yield r'qrType';
    yield serializers.serialize(
      object.qrType,
      specifiedType: const FullType(String),
    );
    yield r'signatureKeyVersion';
    yield object.signatureKeyVersion == null ? null : serializers.serialize(
      object.signatureKeyVersion,
      specifiedType: const FullType.nullable(JsonObject),
    );
  }

  @override
  Object serialize(
    Serializers serializers,
    GenerateQrResponse object, {
    FullType specifiedType = FullType.unspecified,
  }) {
    return _serializeProperties(serializers, object, specifiedType: specifiedType).toList();
  }

  void _deserializeProperties(
    Serializers serializers,
    Object serialized, {
    FullType specifiedType = FullType.unspecified,
    required List<Object?> serializedList,
    required GenerateQrResponseBuilder result,
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
        case r'payloadHash':
          final valueDes = serializers.deserialize(
            value,
            specifiedType: const FullType(String),
          ) as String;
          result.payloadHash = valueDes;
          break;
        case r'qrType':
          final valueDes = serializers.deserialize(
            value,
            specifiedType: const FullType(String),
          ) as String;
          result.qrType = valueDes;
          break;
        case r'signatureKeyVersion':
          final valueDes = serializers.deserialize(
            value,
            specifiedType: const FullType.nullable(JsonObject),
          ) as JsonObject?;
          if (valueDes == null) continue;
          result.signatureKeyVersion = valueDes;
          break;
        default:
          unhandled.add(key);
          unhandled.add(value);
          break;
      }
    }
  }

  @override
  GenerateQrResponse deserialize(
    Serializers serializers,
    Object serialized, {
    FullType specifiedType = FullType.unspecified,
  }) {
    final result = GenerateQrResponseBuilder();
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


