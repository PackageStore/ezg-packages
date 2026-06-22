---
description: Bước 3 - Tạo ánh xạ triển khai (Implementation Mapping) và Setup lệnh Execution
---

# Workflow: Ánh Xạ Triển Khai (03-implementation-mapping)

Bước cuối trong luồng thiết kế. Nhận Technical Spec (từ Workflow 02) và sinh `Implementation Mapping` tuân thủ chuẩn xác ràng buộc hệ thống của Merge Two, dựa trên `.agents/docs/feature-spec-prompt/03-implementation-mapping-prompt.md`.

## Các bước thực hiện:

1. **Đọc đầu vào:**
   - Đọc file `TechSpec` (từ Bước 2) bằng `Read`.
   - Đọc file tiêu chuẩn Mapping: `.agents/docs/feature-spec-prompt/03-implementation-mapping-prompt.md`.

2. **Sinh Implementation Mapping (Phần 10):**
   - Output gồm: Sub-Features list, Player Save Data, ScriptableObject Collections, UI Screens, Event Definitions (TigerForge), **Dependency Graph**, và **Registration Points**.

3. **CSV Resource Validation & Config Protection (Bắt buộc):**
   - Đọc skill `.agents/skills/csv-config/SKILL.md` → mục "Resource Configuration".
   - Mỗi Collection có khai báo rõ **CSV File Name** (tên file = tên Collection bỏ hậu tố `Collection`; CSV đặt trong `CsvConfig/` của feature; auto-load bởi `com.ezg.csv-reader`, KHÔNG `[CreateAssetMenu]`).
   - Nếu collection chứa reward/cost/tài nguyên → CSV Columns **PHẢI** đủ 6 Resource fields: `res_type, res_id, res_number, bonus, stage_bonus, custom_value`.
   - CSV field: `snake_case`. Class/Folder: `PascalCase`.

4. **NHẤT QUÁN NỘI BỘ (Cross-Section Consistency — Bắt buộc):**
   - Sub-feature ↔ Collection: mỗi sub-feature "Cần Model/Collection? = Có" phải có dòng tương ứng trong bảng ScriptableObject Collections.
   - Dependency Graph: nếu ≥ 2 feature con, phải có thứ tự build rõ ràng.

5. **Registration Points (model Merge Two — KHÔNG dùng CsvAssetDir/DataManagerAutoGenerate):**
   - **PlayerData** (chỉ khi module lưu data): đăng ký trong `Assets/_Project/Features/_Shared/GameData/PlayerDataManager.cs` theo pattern `??= DataPlayer.GetModule<T>()`; class kế thừa `DataPlayerBaseGeneric<T>`.
   - **Collection config**: expose trên facade `DataManager` (`Assets/_Project/Features/_Shared/GameData/DataManager.cs`), đọc qua `DataManager.<CollectionName>`.

6. **Sinh Execution Instructions:**
   - Ánh xạ lệnh `/new-feature [FeatureName]: [Mô tả]` theo đúng order từ Dependency Graph.
   - UI Screens KHÔNG tạo bởi `/new-feature` — dùng `/new-ui` riêng sau khi code structure xong.
   - Có thể dùng `Write` nối phần Mapping vào cuối file `TechSpec`, hoặc tạo file `[FeatureName]-Implementation.md` riêng tùy request.
