# CHUỖI PROMPT PHÂN RÃ TÍNH NĂNG - BƯỚC 3: IMPLEMENTATION MAPPING

## BƯỚC 4: Implementation Mapping (Ánh Xạ Triển Khai)

**System/Role:** Bạn là Senior Unity Developer, chuyên gia triển khai feature theo workflow chuẩn.

**Instruction:** Dựa trên Technical Spec được cung cấp (từ Bước 2), hãy tạo bản ánh xạ triển khai chi tiết để developer có thể thực thi ngay với workflow `/new-feature`.

**Yêu cầu output:**

```markdown
# 10. Implementation Mapping

## 10.1 Sub-Features
Liệt kê các feature con cần tạo riêng (mỗi feature = 1 folder trong `Assets/_Project/Features/<Domain>/`, với Domain ∈ Meta, Monetization, Onboarding, Social, System, Events, Gameplay — phần lớn UI screen nằm dưới Meta):
| Feature Name | Domain | Controller | Manager Type | Cần Model/Collection? |
|---|---|---|---|---|
| [PascalCase] | [Meta/...] | Có/Không | Static / DataPlayerBase | Có/Không |

## 10.2 Player Save Data
Liệt kê tất cả field cần lưu vào PlayerData (persistent qua session):
- Field name (type): Mô tả

## 10.3 ScriptableObject Collections
Mỗi collection cần tạo:
| Collection Name | CSV Columns | CSV File Name | Mô tả |
|---|---|---|---|

> **Quy tắc bảo vệ File Config:**
> - CSV File Name PHẢI được định nghĩa rõ ràng. Tên file CSV = tên class Collection BỎ hậu tố `Collection` (Ví dụ: `FeatureNameCollection` -> file CSV là `FeatureName`).
> - CSV sống trong folder `CsvConfig/` của feature; collection là một `ScriptableObject` có field mảng `dataGroups`, KHÔNG dùng `[CreateAssetMenu]`, KHÔNG tự viết hàm load — `com.ezg.csv-reader` tự load.
> - Khớp chính xác tên biến này khi ráp vào Workflow `/new-feature`.

> **CSV Resource Validation (Bắt buộc):**
> Nếu collection có chứa phần thưởng (reward), chi phí (cost), hoặc bất kỳ tài nguyên nào → CSV Columns PHẢI bao gồm đầy đủ 6 Resource fields: `res_type, res_id, res_number, bonus, stage_bonus, custom_value`.
> Tham khảo: `.agents/skills/csv-config/SKILL.md` → mục "Resource Configuration".

## 10.4 UI Screens
Liệt kê tất cả prefab UI cần tạo:
| Screen Name | Loại (Panel/Popup/Widget) | Mô tả |
|---|---|---|

## 10.5 Event Definitions
Liệt kê tất cả custom events (dùng TigerForge `EventManager`, đặt tên trong hằng `EventName`):
| Event Name | Payload | Publisher → Subscriber |
|---|---|---|

> Tham khảo: `.agents/skills/event-manager/SKILL.md` (StartListening / StopListening / EmitEvent / EmitEventData).

## 10.6 Dependency Graph
Khi có nhiều sub-features, liệt kê thứ tự tạo feature để đảm bảo dependency đúng:
```
[Thứ tự] [FeatureName] → phụ thuộc vào: [danh sách feature cần tạo trước]
```
Ví dụ:
```
1. CoreFeature → (không phụ thuộc)
2. SubFeatureA → phụ thuộc: CoreFeature
3. SubFeatureB → phụ thuộc: CoreFeature
```
> Mục tiêu: Developer chạy `/new-feature` theo đúng thứ tự này để tránh lỗi dependency.
```

## 10.7 Registration Points
Với mỗi sub-feature cần Model/Collection hoặc PlayerData, liệt kê các file hệ thống cần cập nhật:

| Sub-Feature | PlayerDataManager.cs (chỉ khi persist data) | DataManager facade (collection property) |
|---|---|---|
| [FeatureName] | Có/Không (chỉ khi Manager = DataPlayerBase) | `DataManager.[FeatureName]` |

> Mục tiêu: Developer biết rõ side-effect ngoài folder feature khi chạy `/new-feature`.

**Mô hình đăng ký của [Project Name] (KHÔNG có `CsvAssetDir.cs`, KHÔNG có `DataManagerAutoGenerate.cs`):**

1. **PlayerData** — chỉ khi Manager/module có persist data. PlayerData class kế thừa `DataPlayerBaseGeneric<T>`. Đăng ký trong `Assets/_Project/Features/_Shared/GameData/PlayerDataManager.cs` theo property pattern:
   ```csharp
   public static YourFeaturePlayerData _yourFeaturePlayerData;
   public static YourFeaturePlayerData YourFeaturePlayerData
   {
       get { return _yourFeaturePlayerData ??= DataPlayer.GetModule<YourFeaturePlayerData>(); }
       set => _yourFeaturePlayerData = value;
   }
   ```

2. **Config Collection** — một `ScriptableObject` có field mảng `dataGroups`. CSV sống trong folder `CsvConfig/` của feature; tên file = tên class Collection BỎ hậu tố `Collection`; được `com.ezg.csv-reader` tự load (KHÔNG `[CreateAssetMenu]`, KHÔNG hàm load tùy biến). Expose trên facade `DataManager` (`Assets/_Project/Features/_Shared/GameData/DataManager.cs`) và đọc qua `DataManager.<CollectionName>`.

## 10.8 Execution Instructions
Liệt kê lệnh `/new-feature` cho từng sub-feature theo đúng dependency order từ 10.6:
```
/new-feature [FeatureName1]: [Mô tả ngắn từ Overview]
/new-feature [FeatureName2]: [Mô tả ngắn từ Overview]
```
> Lưu ý:
> - Chạy ĐÚNG thứ tự dependency.
> - UI Screens (10.4) KHÔNG được tạo bởi `/new-feature`. Dùng workflow `/new-ui` riêng sau khi code structure hoàn tất (`/new-ui` KHÔNG dùng `/new-feature` để tạo prefab UI).

**Nguyên tắc BẮT BUỘC:**
- Tên các Class và Feature PHẢI là PascalCase.
- Manager Type: chọn `Static` nếu không cần lưu data, chọn `DataPlayerBase` nếu cần PlayerData.
- Tên các trường (CSV Columns) PHẢI dùng snake_case.
- CSV Columns chứa resource PHẢI có đủ 6 fields nói trên.
- Mỗi UI screen phải có tên duy nhất, kết thúc bằng suffix phù hợp (Panel, Popup, Widget).
- Không giải thích chung chung. Chỉ xuất ra bảng mapping rõ ràng.
- Đầu ra của bước này phải tuyệt đối chính xác để nhập thẳng vào terminal execution.
