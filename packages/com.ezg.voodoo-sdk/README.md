# com.ezg.voodoo-sdk

Tự động hoá cài đặt **TinySauce** (SDK publishing của Voodoo — đã bundle sẵn Adjust,
GameAnalytics, Facebook, VoodooAnalytics) vào project Unity.

Bạn chỉ cần điền **một file config**. Mọi thứ còn lại package tự lo, và các bản vá tự áp dụng
lại sau mỗi lần nâng cấp TinySauce.

## Dùng

```
Tools > Voodoo SDK > Create config template     # sinh ProjectSettings/voodoo-sdk.config.json
# điền các giá trị FILL_ME
Tools > Voodoo SDK > Install
Tools > Voodoo SDK > Validate
```

Batchmode:
```bash
Unity -batchmode -quit -projectPath <proj> \
      -executeMethod Ezg.VoodooSdk.Editor.VoodooSdkInstaller.InstallFromCommandLine
```

## Package làm gì

| Việc | Thành phần |
|---|---|
| Sinh `Assets/Resources/TinySauce/Settings.asset` từ config | `VoodooSdkSettingsGenerator` |
| Ghi FB App ID + client token vào `FacebookSettings.asset` | `VoodooSdkSettingsGenerator` |
| Gỡ callback AppLovin đã bị xoá khỏi `GAMaxIntegration.cs` | `VoodooSdkGaIlrdPatcher` |
| Tạo asmdef `GameAnalytics.Scripts` (bản vendor không có) | `VoodooSdkGaAsmdefPatcher` |
| Khai báo resource currency/itemType cho GameAnalytics | `VoodooSdkGaResourcePatcher` |
| Khôi phục Firebase Messaging + sửa `tools:replace` trong AndroidManifest | `VoodooSdkAndroidManifestFixer` |
| Mồi Apple Unity Plug-ins khi build iOS batchmode | `VoodooSdkApplePlugInPrimer` |
| Thêm define `NEWTONSOFT` + `EZG_GAMEANALYTICS`, đưa prefab vào scene đầu | `VoodooSdkInstaller` |
| Chặn build sớm khi cấu hình sai | `VoodooSdkPreflight` |

## Tại sao vá lại không mất khi nâng cấp TinySauce

- **AndroidManifest**: TinySauce ghi đè `Assets/Plugins/Android/AndroidManifest.xml` ở *mỗi* lần
  build (`AndroidPreBuild`, `callbackOrder = 0`). Package sửa **file đích** với
  `callbackOrder = 100` nên luôn vào sau — không đụng template trong thư mục SDK.
- **GA ILRD**: `AssetPostprocessor` phát hiện TinySauce được import lại và vá ngay.

## Ràng buộc

TinySauce **phải** nằm ở `Assets/VoodooPackages/TinySauce`. `GradleTemplateFilePathHelper` của
Voodoo hardcode đường dẫn này và ghép với `Application.dataPath`; chuyển sang `Packages/` thì cơ
chế merge gradle của họ im lặng ngừng chạy — không báo lỗi, chỉ thiếu dependency lúc build.

## Vẫn phải làm tay

1. Import `.unitypackage` TinySauce (Voodoo phát hành, không có link tải công khai)
2. `google-services.json` + `GoogleService-Info.plist` — artifact riêng từng Firebase project
3. Bundle ID phải khớp `GoogleService-Info.plist`
4. Xoá `Assets/FacebookSDK` nếu có — package chỉ cảnh báo, không tự xoá (có project cố ý dùng bản riêng)
5. Chứng chỉ ký / provisioning

## Lưu ý vận hành

Adjust mặc định chạy **Production mode**. Test nhiều sẽ bẩn dữ liệu dashboard thật.
