# bqr_public_client.api.SBQRApiApi

## Load the API package
```dart
import 'package:bqr_public_client/api.dart';
```

All URIs are relative to *http://localhost:5001*

Method | HTTP request | Description
------------- | ------------- | -------------
[**healthLiveGet**](SBQRApiApi.md#healthliveget) | **GET** /health/live | 
[**healthReadyGet**](SBQRApiApi.md#healthreadyget) | **GET** /health/ready | 
[**rootGet**](SBQRApiApi.md#rootget) | **GET** / | 


# **healthLiveGet**
> healthLiveGet()



### Example
```dart
import 'package:bqr_public_client/api.dart';

final api = BqrPublicClient().getSBQRApiApi();

try {
    api.healthLiveGet();
} on DioException catch (e) {
    print('Exception when calling SBQRApiApi->healthLiveGet: $e\n');
}
```

### Parameters
This endpoint does not need any parameter.

### Return type

void (empty response body)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: Not defined

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **healthReadyGet**
> healthReadyGet()



### Example
```dart
import 'package:bqr_public_client/api.dart';

final api = BqrPublicClient().getSBQRApiApi();

try {
    api.healthReadyGet();
} on DioException catch (e) {
    print('Exception when calling SBQRApiApi->healthReadyGet: $e\n');
}
```

### Parameters
This endpoint does not need any parameter.

### Return type

void (empty response body)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: Not defined

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **rootGet**
> rootGet()



### Example
```dart
import 'package:bqr_public_client/api.dart';

final api = BqrPublicClient().getSBQRApiApi();

try {
    api.rootGet();
} on DioException catch (e) {
    print('Exception when calling SBQRApiApi->rootGet: $e\n');
}
```

### Parameters
This endpoint does not need any parameter.

### Return type

void (empty response body)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: Not defined

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

