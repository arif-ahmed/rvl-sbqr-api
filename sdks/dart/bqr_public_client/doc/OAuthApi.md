# bqr_public_client.api.OAuthApi

## Load the API package
```dart
import 'package:bqr_public_client/api.dart';
```

All URIs are relative to *http://localhost:5001*

Method | HTTP request | Description
------------- | ------------- | -------------
[**v1OauthTokenPost**](OAuthApi.md#v1oauthtokenpost) | **POST** /v1/oauth/token | 


# **v1OauthTokenPost**
> TokenResponse v1OauthTokenPost(grantType, clientId, clientSecret, packageId)



### Example
```dart
import 'package:bqr_public_client/api.dart';

final api = BqrPublicClient().getOAuthApi();
final String grantType = grantType_example; // String | Must be \\\"client_credentials\\\".
final String clientId = clientId_example; // String | The client identifier (platform bootstrap client or a tenant FI credential).
final String clientSecret = clientSecret_example; // String | The client secret. Never logged, never persisted.
final String packageId = packageId_example; // String | Optional. Mobile-app package identifier (Android applicationId or iOS bundle ID) for the FR-AUTH-002 allow-list check. Tenant backends omit this; mobile apps calling the platform directly should send it.

try {
    final response = api.v1OauthTokenPost(grantType, clientId, clientSecret, packageId);
    print(response);
} on DioException catch (e) {
    print('Exception when calling OAuthApi->v1OauthTokenPost: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **grantType** | **String**| Must be \\\"client_credentials\\\". | 
 **clientId** | **String**| The client identifier (platform bootstrap client or a tenant FI credential). | 
 **clientSecret** | **String**| The client secret. Never logged, never persisted. | 
 **packageId** | **String**| Optional. Mobile-app package identifier (Android applicationId or iOS bundle ID) for the FR-AUTH-002 allow-list check. Tenant backends omit this; mobile apps calling the platform directly should send it. | [optional] 

### Return type

[**TokenResponse**](TokenResponse.md)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: application/x-www-form-urlencoded, application/json
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

