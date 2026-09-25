//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:built_value/built_value.dart';
import 'package:built_value/serializer.dart';

part 'validate_qr_response.g.dart';

/// ValidateQrResponse
///
/// Properties:
/// * [verdict] 
/// * [trustSource] 
/// * [reasonCode] 
/// * [institutionCode] 
/// * [payloadHash] 
/// * [recipientName] 
/// * [recipientPan] 
/// * [qrClassification] 
@BuiltValue()
abstract class ValidateQrResponse implements Built<ValidateQrResponse, ValidateQrResponseBuilder> {
  @BuiltValueField(wireName: r'verdict')
  String get verdict;

  @BuiltValueField(wireName: r'trustSource')
  String? get trustSource;

  @BuiltValueField(wireName: r'reasonCode')
  String? get reasonCode;

  @BuiltValueField(wireName: r'institutionCode')
  String? get institutionCode;

  @BuiltValueField(wireName: r'payloadHash')
  String get payloadHash;

  @BuiltValueField(wireName: r'recipientName')
  String? get recipientName;

  @BuiltValueField(wireName: r'recipientPan')
  String? get recipientPan;

  @BuiltValueField(wireName: r'qrClassification')
  String get qrClassification;

  ValidateQrResponse._();

  factory ValidateQrResponse([void updates(ValidateQrResponseBuilder b)]) = _$ValidateQrResponse;

  @BuiltValueHook(initializeBuilder: true)
  static void _defaults(ValidateQrResponseBuilder b) => b;

  @BuiltValueSerializer(custom: true)
  static Serializer<ValidateQrResponse> get serializer => _$ValidateQrResponseSerializer();
}

class _$ValidateQrResponseSerializer implements PrimitiveSerializer<ValidateQrResponse> {
  @override
  final Iterable<Type> types = const [ValidateQrResponse, _$ValidateQrResponse];

  @override
  final String wireName = r'ValidateQrResponse';

  Iterable<Object?> _serializeProperties(
    Serializers serializers,
    ValidateQrResponse object, {
    FullType specifiedType = FullType.unspecified,
  }) sync* {
    yield r'verdict';
    yield serializers.serialize(
      object.verdict,
      specifiedType: const FullType(String),
    );
    yield r'trustSource';
    yield object.trustSource == null ? null : serializers.serialize(
      object.trustSource,
      specifiedType: const FullType.nullable(String),
    );
    yield r'reasonCode';
    yield object.reasonCode == null ? null : serializers.serialize(
      object.reasonCode,
      specifiedType: const FullType.nullable(String),
    );
    yield r'institutionCode';
    yield object.institutionCode == null ? null : serializers.serialize(
      object.institutionCode,
      specifiedType: const FullType.nullable(String),
    );
    yield r'payloadHash';
    yield serializers.serialize(
      object.payloadHash,
      specifiedType: const FullType(String),
    );
    yield r'recipientName';
    yield object.recipientName == null ? null : serializers.serialize(
      object.recipientName,
      specifiedType: const FullType.nullable(String),
    );
    yield r'recipientPan';
    yield object.recipientPan == null ? null : serializers.serialize(
      object.recipientPan,
      specifiedType: const FullType.nullable(String),
    );
    yield r'qrClassification';
    yield serializers.serialize(
      object.qrClassification,
      specifiedType: const FullType(String),
    );
  }

  @override
  Object serialize(
    Serializers serializers,
    ValidateQrResponse object, {
    FullType specifiedType = FullType.unspecified,
  }) {
    return _serializeProperties(serializers, object, specifiedType: specifiedType).toList();
  }

  void _deserializeProperties(
    Serializers serializers,
    Object serialized, {
    FullType specifiedType = FullType.unspecified,
    required List<Object?> serializedList,
    required ValidateQrResponseBuilder result,
    required List<Object?> unhandled,
  }) {
    for (var i = 0; i < serializedList.length; i += 2) {
      final key = serializedList[i] as String;
      final value = serializedList[i + 1];
      switch (key) {
        case r'verdict':
          final valueDes = serializers.deserialize(
            value,
            specifiedType: const FullType(String),
          ) as String;
          result.verdict = valueDes;
          break;
        case r'trustSource':
          final valueDes = serializers.deserialize(
            value,
            specifiedType: const FullType.nullable(String),
          ) as String?;
          if (valueDes == null) continue;
          result.trustSource = valueDes;
          break;
        case r'reasonCode':
          final valueDes = serializers.deserialize(
            value,
            specifiedType: const FullType.nullable(String),
          ) as String?;
          if (valueDes == null) continue;
          result.reasonCode = valueDes;
          break;
        case r'institutionCode':
          final valueDes = serializers.deserialize(
            value,
            specifiedType: const FullType.nullable(String),
          ) as String?;
          if (valueDes == null) continue;
          result.institutionCode = valueDes;
          break;
        case r'payloadHash':
          final valueDes = serializers.deserialize(
            value,
            specifiedType: const FullType(String),
          ) as String;
          result.payloadHash = valueDes;
          break;
        case r'recipientName':
          final valueDes = serializers.deserialize(
            value,
            specifiedType: const FullType.nullable(String),
          ) as String?;
          if (valueDes == null) continue;
          result.recipientName = valueDes;
          break;
        case r'recipientPan':
          final valueDes = serializers.deserialize(
            value,
            specifiedType: const FullType.nullable(String),
          ) as String?;
          if (valueDes == null) continue;
          result.recipientPan = valueDes;
          break;
        case r'qrClassification':
          final valueDes = serializers.deserialize(
            value,
            specifiedType: const FullType(String),
          ) as String;
          result.qrClassification = valueDes;
          break;
        default:
          unhandled.add(key);
          unhandled.add(value);
          break;
      }
    }
  }

  @override
  ValidateQrResponse deserialize(
    Serializers serializers,
    Object serialized, {
    FullType specifiedType = FullType.unspecified,
  }) {
    final result = ValidateQrResponseBuilder();
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


