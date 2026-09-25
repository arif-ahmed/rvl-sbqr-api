# bqr_public_client.api.QrGenerationApi

## Load the API package
```dart
import 'package:bqr_public_client/api.dart';
```

All URIs are relative to *http://localhost:5001*

Method | HTTP request | Description
------------- | ------------- | -------------
[**v1QrGenerateDynamicPost**](QrGenerationApi.md#v1qrgeneratedynamicpost) | **POST** /v1/qr/generate/dynamic | 
[**v1QrGenerateStaticPost**](QrGenerationApi.md#v1qrgeneratestaticpost) | **POST** /v1/qr/generate/static | 


# **v1QrGenerateDynamicPost**
> GenerateQrResponse v1QrGenerateDynamicPost(generateDynamicQrRequest, idempotencyKey)



### Example
```dart
import 'package:bqr_public_client/api.dart';

final api = BqrPublicClient().getQrGenerationApi();
final GenerateDynamicQrRequest generateDynamicQrRequest = ; // GenerateDynamicQrRequest | 
final String idempotencyKey = idempotencyKey_example; // String | 

try {
    final response = api.v1QrGenerateDynamicPost(generateDynamicQrRequest, idempotencyKey);
    print(response);
} on DioException catch (e) {
    print('Exception when calling QrGenerationApi->v1QrGenerateDynamicPost: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **generateDynamicQrRequest** | [**GenerateDynamicQrRequest**](GenerateDynamicQrRequest.md)|  | 
 **idempotencyKey** | **String**|  | [optional] 

### Return type

[**GenerateQrResponse**](GenerateQrResponse.md)

### Authorization

[bearerAuth](../README.md#bearerAuth)

### HTTP request headers

 - **Content-Type**: application/json, text/json, application/*+json
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **v1QrGenerateStaticPost**
> GenerateQrResponse v1QrGenerateStaticPost(generateStaticQrRequest, idempotencyKey)



### Example
```dart
import 'package:bqr_public_client/api.dart';

final api = BqrPublicClient().getQrGenerationApi();
final GenerateStaticQrRequest generateStaticQrRequest = ; // GenerateStaticQrRequest | 
final String idempotencyKey = idempotencyKey_example; // String | 

try {
    final response = api.v1QrGenerateStaticPost(generateStaticQrRequest, idempotencyKey);
    print(response);
} on DioException catch (e) {
    print('Exception when calling QrGenerationApi->v1QrGenerateStaticPost: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **generateStaticQrRequest** | [**GenerateStaticQrRequest**](GenerateStaticQrRequest.md)|  | 
 **idempotencyKey** | **String**|  | [optional] 

### Return type

[**GenerateQrResponse**](GenerateQrResponse.md)

### Authorization

[bearerAuth](../README.md#bearerAuth)

### HTTP request headers

 - **Content-Type**: application/json, text/json, application/*+json
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

