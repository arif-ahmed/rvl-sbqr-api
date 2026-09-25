# bqr_public_client.api.QrValidationApi

## Load the API package
```dart
import 'package:bqr_public_client/api.dart';
```

All URIs are relative to *http://localhost:5001*

Method | HTTP request | Description
------------- | ------------- | -------------
[**v1QrValidatePost**](QrValidationApi.md#v1qrvalidatepost) | **POST** /v1/qr/validate | 


# **v1QrValidatePost**
> ValidateQrResponse v1QrValidatePost(validateQrRequest)



### Example
```dart
import 'package:bqr_public_client/api.dart';

final api = BqrPublicClient().getQrValidationApi();
final ValidateQrRequest validateQrRequest = ; // ValidateQrRequest | 

try {
    final response = api.v1QrValidatePost(validateQrRequest);
    print(response);
} on DioException catch (e) {
    print('Exception when calling QrValidationApi->v1QrValidatePost: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **validateQrRequest** | [**ValidateQrRequest**](ValidateQrRequest.md)|  | 

### Return type

[**ValidateQrResponse**](ValidateQrResponse.md)

### Authorization

[bearerAuth](../README.md#bearerAuth)

### HTTP request headers

 - **Content-Type**: application/json, text/json, application/*+json
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

