# bqr_public_client.model.TokenRequest

## Load the model package
```dart
import 'package:bqr_public_client/api.dart';
```

## Properties
Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**grantType** | **String** | Must be \"client_credentials\". | 
**clientId** | **String** | The client identifier (platform bootstrap client or a tenant FI credential). | 
**clientSecret** | **String** | The client secret. Never logged, never persisted. | 
**packageId** | **String** | Optional. Mobile-app package identifier (Android applicationId or iOS bundle ID) for the FR-AUTH-002 allow-list check. Tenant backends omit this; mobile apps calling the platform directly should send it. | [optional] 

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)


