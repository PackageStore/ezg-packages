# Stage 06 — Implementation Mapping (Design Pipeline)

Stage cuối khối thiết kế: nhận Technical Spec (từ [05-tech-spec.md](05-tech-spec.md)) → `Implementation Mapping` tuân thủ cực kỳ chuẩn xác các ràng buộc hệ thống.

> **Hai chế độ gọi — handoff cuối KHÁC NHAU (bước 5):**
> - **Standalone** (user yêu cầu chạy stage này): giữ nguyên handoff cổ điển — xuất chuỗi lệnh `/new-feature` cho user tự chạy.
> - **Dưới `/planning-system`** (prompt có `invoked-by: planning-system`): KHÔNG bảo user paste lệnh — trả stage-result JSON với `artifact_path`; orchestrator sẽ tự batch-ground mapping thành các task trong `backlog/planning/`.

## Quy trình

1. **Đọc đầu vào:**
   - Đọc file `TechSpec` được sinh ra từ stage 05 (mặc định `TechSpec/[FeatureName]-TechSpec.md`) bằng công cụ `Read`.
   - Prompt chuẩn của stage này nằm ngay **bên dưới** trong file này.
   - Nếu TechSpec còn marker `[DECISION NEEDED]` → DỪNG, trả `status=QUESTIONS` (dưới planning-system) hoặc hỏi user (standalone). Mapping sinh từ spec chưa chốt là mapping rác.

2. **Sinh Implementation Mapping:**
   - Áp dụng **BƯỚC 4 (Implementation Mapping)** từ Prompt chuẩn để sinh Phần 10. Implementation Mapping.
   - Output phải bao gồm: Sub-Features list, Player Save Data, ScriptableObject Collections, UI Screens, Event Definitions, **Dependency Graph**, và **Registration Points**.

3. **CSV Resource Validation & Config Protection (Bắt buộc):**
   - Đọc skill `.claude/skills/csv-config/SKILL.md` → mục "Resource Configuration".
   - Duyệt qua từng ScriptableObject Collection. Đảm bảo mỗi collection có khai báo rõ **CSV File Name** (thường không có chữ `Collection` ở đoạn cuối).
   - Nếu collection chứa reward, cost, hoặc tài nguyên (giá bán, quà tặng..) → CSV Columns **PHẢI BAO GỒM ĐỦ QUY TẮC BẮT BUỘC** 6 Resource fields: `res_type, res_id, res_number, bonus, stage_bonus, custom_value`.
   - Mọi tên field CSV phải là `snake_case`. Mọi Class/Folder Name phải là `PascalCase`.

4. **NHẤT QUÁN NỘI BỘ (Cross-Section Consistency - Bắt Buộc):**
   - Sub-feature ↔ Collection Matching: Mỗi sub-feature có "Cần Model/Collection? = Có" thì BẮT BUỘC phải có một dòng tương ứng trong bảng ScriptableObject Collections.
   - Dependency Graph: Nếu có >= 2 feature con, phải có thứ tự build feature rõ ràng (Feature nào tạo trước, feature nào tạo sau). **UI Screens (10.4) cũng phải xuất hiện trong graph**: mỗi screen phụ thuộc feature sở hữu nó và xếp SAU mọi sub-feature code.
   - **EPIC mode (per-module):** khi prompt liệt kê các module khác của hệ thống, dependency chéo module trong §10.6 phải ghi bằng format `[<OtherModule>] <SubFeature>` (vd `2. GuildShop → phụ thuộc: [GuildCore] GuildManager`) — orchestrator dùng prefix này để merge các mapping per-module thành một graph chung. KHÔNG kê sub-feature của module khác vào §10.1 của module mình.

5. **Lưu file & Handoff (theo chế độ gọi):**
   - Dùng công cụ `Write` lưu Mapping thành **file riêng, đường dẫn cố định**: `TechSpec/[FeatureName]-Implementation.md` (KHÔNG append vào cuối TechSpec.md — orchestrator và `/new-feature` đều parse file này theo đường dẫn chuẩn).
   - **Standalone:** ánh xạ trực tiếp lệnh `/new-feature [FeatureName]: [Mô tả]` theo đúng order từ Dependency Graph và in cho user. Lưu ý User: đính kèm file Mapping khi paste vào terminal để chạy `/new-feature`. UI Screens (10.4) KHÔNG được tạo bởi `/new-feature` — dùng `/new-ui` riêng sau khi code xong.
   - **Dưới `/planning-system`:** KHÔNG in lệnh cho user. Trả stage-result JSON (dưới đây) — orchestrator đọc `artifact_path` và tự chạy batch-ground.

## Stage-result contract (chỉ khi `invoked-by: planning-system`)

Kết thúc bằng đúng MỘT JSON object, không kèm prose:

```json
{
  "status": "OK | QUESTIONS | ABORT",
  "artifact_path": "TechSpec/[FeatureName]-Implementation.md",
  "questions": ["chỉ khi status=QUESTIONS"],
  "abort_reason": "chỉ khi status=ABORT"
}
```

## Prompt chuẩn

# CHUỖI PROMPT PHÂN RÃ TÍNH NĂNG - BƯỚC 3: IMPLEMENTATION MAPPING

## BƯỚC 4: Implementation Mapping (Ánh Xạ Triển Khai)

**System/Role:** Bạn là Senior Unity Developer, chuyên gia triển khai feature theo workflow chuẩn.

**Instruction:** Dựa trên Technical Spec được cung cấp (từ Bước 2), hãy tạo bản ánh xạ triển khai chi tiết để developer có thể thực thi ngay với workflow `/new-feature`.

**Yêu cầu output:**

```markdown
# 10. Implementation Mapping

## 10.1 Sub-Features
Liệt kê các feature con cần tạo riêng (mỗi feature = 1 folder trong feature tree được `.claude/rules/project-structure.md` quy định; chọn Domain bucket từ sibling code thật, không hardcode domain của một game khác):
| Feature Name | Domain | Controller | Manager Type | Cần Model/Collection? |
|---|---|---|---|---|
| [PascalCase] | <real domain> | Có/Không | Static / DataPlayerBase | Có/Không |

## 10.2 Player Save Data
Liệt kê tất cả field cần lưu vào PlayerData (persistent qua session):
- Field name (type): Mô tả

## 10.3 ScriptableObject Collections
Mỗi collection cần tạo:
| Collection Name | CSV Columns | CSV File Name | Mô tả |
|---|---|---|---|

> **Quy tắc bảo vệ File Config:**
> - CSV File Name PHẢI được định nghĩa rõ ràng. Tên file CSV thường không có chữ `Collection` ở cuối (Ví dụ: `FeatureNameCollection` -> file CSV là `FeatureName`).
> - Khớp chính xác tên biến này khi ráp vào Workflow `/new-feature`.

> **CSV Resource Validation (Bắt buộc):**
> Nếu collection có chứa phần thưởng (reward), chi phí (cost), hoặc bất kỳ tài nguyên nào → CSV Columns PHẢI bao gồm đầy đủ 6 Resource fields: `res_type, res_id, res_number, bonus, stage_bonus, custom_value`.
> Tham khảo: `.claude/skills/csv-config/SKILL.md` → mục "Resource Configuration".

## 10.4 UI Screens
Liệt kê tất cả prefab UI cần tạo:
| Screen Name | Loại (Panel/Popup/Widget) | Mô tả |
|---|---|---|

## 10.5 Event Definitions
Liệt kê tất cả custom events (dùng TigerForge EventManager + `EventName` constants):
| Event Name | Payload | Publisher → Subscriber |
|---|---|---|

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
Với mỗi sub-feature cần Model/Collection, liệt kê các file hệ thống cần cập nhật:
| Sub-Feature | CsvAssetDir.cs | DataManager facade | PlayerDataManager.cs |
|---|---|---|---|
| [FeatureName] | `public const string [FeatureName]Config = "[FeatureName]Config"` | expose accessor `DataManager.[FeatureName]` (`_Shared/GameData/DataManager.cs` / `DataManager.Generated.cs`) | Có/Không (chỉ khi Manager = DataPlayerBase) |

> Mục tiêu: Developer biết rõ side-effect ngoài folder feature khi chạy `/new-feature`.

## 10.8 Execution Instructions
Liệt kê lệnh `/new-feature` cho từng sub-feature theo đúng dependency order từ 10.6:
```
/new-feature [FeatureName1]: [Mô tả ngắn từ Overview]
/new-feature [FeatureName2]: [Mô tả ngắn từ Overview]
```
> Lưu ý:
> - Chạy ĐÚNG thứ tự dependency.
> - UI Screens (10.4) KHÔNG được tạo bởi `/new-feature`. Dùng workflow `/new-ui` riêng sau khi code structure hoàn tất.

**Nguyên tắc BẮT BUỘC:**
- Tên các Class và Feature PHẢI là PascalCase.
- Manager Type: chọn `Static` nếu không cần lưu data, chọn `DataPlayerBase` nếu cần PlayerData.
- Tên các trường (CSV Columns) PHẢI dùng snake_case.
- CSV Columns chứa resource PHẢI có đủ 6 fields nói trên.
- Mỗi UI screen phải có tên duy nhất, kết thúc bằng suffix phù hợp (Panel, Popup, Widget).
- Không giải thích chung chung. Chỉ xuất ra bảng mapping rõ ràng.
- Đầu ra của bước này phải tuyệt đối chính xác để nhập thẳng vào terminal execution.
