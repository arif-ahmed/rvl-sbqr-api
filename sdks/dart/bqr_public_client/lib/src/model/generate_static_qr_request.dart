//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:built_value/built_value.dart';
import 'package:built_value/serializer.dart';

part 'generate_static_qr_request.g.dart';

/// GenerateStaticQrRequest
///
/// Properties:
/// * [recipientName] 
/// * [recipientCity] 
/// * [recipientPan] 
/// * [postalCode] 
/// * [customerLabel] 
/// * [purposeOfTransaction] 
@BuiltValue()
abstract class GenerateStaticQrRequest implements Built<GenerateStaticQrRequest, GenerateStaticQrRequestBuilder> {
  @BuiltValueField(wireName: r'recipientName')
  String get recipientName;

  @BuiltValueField(wireName: r'recipientCity')
  String get recipientCity;

  @BuiltValueField(wireName: r'recipientPan')
  String get recipientPan;

  @BuiltValueField(wireName: r'postalCode')
  String? get postalCode;

  @BuiltValueField(wireName: r'customerLabel')
  String? get customerLabel;

  @BuiltValueField(wireName: r'purposeOfTransaction')
  String? get purposeOfTransaction;

  GenerateStaticQrRequest._();

  factory GenerateStaticQrRequest([void updates(GenerateStaticQrRequestBuilder b)]) = _$GenerateStaticQrRequest;

  @BuiltValueHook(initializeBuilder: true)
  static void _defaults(GenerateStaticQrRequestBuilder b) => b;

  @BuiltValueSerializer(custom: true)
  static Serializer<GenerateStaticQrRequest> get serializer => _$GenerateStaticQrRequestSerializer();
}

class _$GenerateStaticQrRequestSerializer implements PrimitiveSerializer<GenerateStaticQrRequest> {
  @override
  final Iterable<Type> types = const [GenerateStaticQrRequest, _$GenerateStaticQrRequest];

  @override
  final String wireName = r'GenerateStaticQrRequest';

  Iterable<Object?> _serializeProperties(
    Serializers serializers,
    GenerateStaticQrRequest object, {
    FullType specifiedType = FullType.unspecified,
  }) sync* {
    yield r'recipientName';
    yield serializers.serialize(
      object.recipientName,
      specifiedType: const FullType(String),
    );
    yield r'recipientCity';
    yield serializers.serialize(
      object.recipientCity,
      specifiedType: const FullType(String),
    );
    yield r'recipientPan';
    yield serializers.serialize(
      object.recipientPan,
      specifiedType: const FullType(String),
    );
    if (object.postalCode != null) {
      yield r'postalCode';
      yield serializers.serialize(
        object.postalCode,
        specifiedType: const FullType.nullable(String),
      );
    }
    if (object.customerLabel != null) {
      yield r'customerLabel';
      yield serializers.serialize(
        object.customerLabel,
        specifiedType: const FullType.nullable(String),
      );
    }
    if (object.purposeOfTransaction != null) {
      yield r'purposeOfTransaction';
      yield serializers.serialize(
        object.purposeOfTransaction,
        specifiedType: const FullType.nullable(String),
      );
    }
  }

  @override
  Object serialize(
    Serializers serializers,
    GenerateStaticQrRequest object, {
    FullType specifiedType = FullType.unspecified,
  }) {
    return _serializeProperties(serializers, object, specifiedType: specifiedType).toList();
  }

  void _deserializeProperties(
    Serializers serializers,
    Object serialized, {
    FullType specifiedType = FullType.unspecified,
    required List<Object?> serializedList,
    required GenerateStaticQrRequestBuilder result,
    required List<Object?> unhandled,
  }) {
    for (var i = 0; i < serializedList.length; i += 2) {
      final key = serializedList[i] as String;
      final value = serializedList[i + 1];
      switch (key) {
        case r'recipientName':
          final valueDes = serializers.deserialize(
            value,
            specifiedType: const FullType(String),
          ) as String;
          result.recipientName = valueDes;
          break;
        case r'recipientCity':
          final valueDes = serializers.deserialize(
            value,
            specifiedType: const FullType(String),
          ) as String;
          result.recipientCity = valueDes;
          break;
        case r'recipientPan':
          final valueDes = serializers.deserialize(
            value,
            specifiedType: const FullType(String),
          ) as String;
          result.recipientPan = valueDes;
          break;
        case r'postalCode':
          final valueDes = serializers.deserialize(
            value,
            specifiedType: const FullType.nullable(String),
          ) as String?;
          if (valueDes == null) continue;
          result.postalCode = valueDes;
          break;
        case r'customerLabel':
          final valueDes = serializers.deserialize(
            value,
            specifiedType: const FullType.nullable(String),
          ) as String?;
          if (valueDes == null) continue;
          result.customerLabel = valueDes;
          break;
        case r'purposeOfTransaction':
          final valueDes = serializers.deserialize(
            value,
            specifiedType: const FullType.nullable(String),
          ) as String?;
          if (valueDes == null) continue;
          result.purposeOfTransaction = valueDes;
          break;
        default:
          unhandled.add(key);
          unhandled.add(value);
          break;
      }
    }
  }

  @override
  GenerateStaticQrRequest deserialize(
    Serializers serializers,
    Object serialized, {
    FullType specifiedType = FullType.unspecified,
  }) {
    final result = GenerateStaticQrRequestBuilder();
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


