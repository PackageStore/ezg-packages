# Changelog — Unity Project Template

Các thay đổi đáng chú ý của template Unity (`templates/unity-project/`, gồm builder + `DefaultSetup/`) được ghi tại đây.

Định dạng mục: **Added** / **Changed** / **Fixed**, mới nhất ở trên cùng.

## 2026-08-05

Đợt cập nhật lớn: rebase toàn bộ nội dung Unity + AI tooling của template theo project `Tank` sau khi nó được refactor thành "template thuần, không gameplay".

**Added**
- `DefaultSetup/ProjectSettings/` nay ship thêm `ProjectSettings.asset`, `QualitySettings.asset`, `GraphicsSettings.asset` và `ProjectVersion.txt` (pin **Unity 6000.3.16f1**) — trước đây chỉ có `TagManager.asset` + `EditorBuildSettings.asset`, nên mỗi project init ra lại tự sinh graphics/quality settings khác nhau. `ProjectSettings.asset` đã được sanitize: `productName` → `Unity Game Template`, `productGUID` → zero; `companyName`, keystore, `appleDeveloperTeamID`, `cloudProjectId`/`organizationId` vốn đã rỗng ở nguồn.
- `ezg.base.features.unitypackage` nhận thêm 6 file lẻ ở gốc `Assets/` trước đây không thuộc package nào: `Editor/DisableBitcode.cs`, `Editor/SpineSettings.asset`, `ProjectSettings/UI{Effect,Particle,SoftMask}ProjectSettings.asset`, `Resources/BillingMode.json`.
- Khôi phục **283 asset art** bị mất GUID (sprite/texture/font/material/shader) từ các project nguồn `a004_temp` / `m1` / `sm006`, giữ nguyên GUID nên prefab tự bind lại mà không phải sửa YAML. Art thuộc UI kit dùng chung về đúng chỗ cũ (`Visual/ArtAsset/{Layer Lab,Shared,Fonts,UI/common,…}`); phần còn lại nằm dưới `Visual/ArtAsset/_Recovered/` để dễ truy vết và sắp xếp lại sau.

**Changed**
- `ezg.base.features.unitypackage`: 817 → **767 entry** (`sha256 6ec2a9d1…`). Bỏ 60 entry gameplay-specific (HomeScene infinite-scroll/streak controllers, Shop Store data của game cũ, `_Shared/RpgStats/*`, `GameData/Core/ItemMerge*`, `GameCheat/AdminToolManager/SaveFoundElement`, `UnlockFeature` bản `_old`, `TestScene.unity`…), thêm 5 entry mới (`TemplatePurchaseHooks`, `PsdToUnityConfigCollection`, `UIFrameworkAdapter`, `tool_tip_item.prefab`, `AdsConfig.asset`) + 6 file lẻ nói trên.
- `ezg.base.visuals.unitypackage`: 1219 → **1170 entry**, 148,879,275 → **138,412,922 B** (`sha256 2f44b358…`). Bản cũ trên R2 là bản 21/06/2026 — có từ *trước* đợt refactor và chứa toàn bộ VFX/material của merge-game (`vfx_impact_merge_*`, `speed_boost`, `sandglass_boost`, `unlimit_energy`, `Materials/{Block,BoxTape,Booster,Missiles,Cup,Freeze}`) cùng hệ thống `SoftTut` (kèm `.cs`). Bản mới bỏ hết, thêm Layer Lab UI kit + art khôi phục.
- `ezg.base.default.unitypackage`: 368 entry, 473,659 → **822,530 B** (`sha256 c92ff146…`), do 4 file ProjectSettings mới.
- `DefaultSetup/`: đồng bộ 23 file AI tooling từ Tank (8 `.claude/commands`, 2 `.claude/rules`, 1 `.claude/docs`, 9 SKILL/reference, `CLAUDE.md`, `.mcp.json`). `CLAUDE.md` mô tả đúng hiện trạng: `DataManager.Generated.cs` + `CsvAssetDir.cs`, danh sách collection và module PlayerData thật, ~280 script, nêu rõ template **không ship gameplay bucket** nào.
- `TagManager.asset` lấy từ Tank: giữ layer `ui_hide_by_cheat` @ index 8 (bắt buộc — `FeatureBaseController.cs:276` gọi `ChangeLayerUI(GameConstant.LayerUIHideByCheat)`, chưa khai thì `LayerMask.NameToLayer` trả `-1` và Unity báo lỗi), nhưng **xoá 4 tag rác** của game cũ (`button_shop`, `button_level_to_level_pass`, `button_build_up_goal_home`, `button_play`) — đã verify 0 tham chiếu trong `.cs` lẫn `m_TagString` của prefab/scene.
- Gỡ khỏi `dependencies` (109 → **107**):
  - `com.ezg.rpg-stats` — code dùng nó (`_Shared/RpgStats/*`) đã bị xoá khỏi template.
  - `com.ezg.fast-script-reload` — `EnableExperimentalAddedFieldsSupport` mặc định **bật** và chỉ tắt được qua `EditorPrefs` (per-máy, không đi kèm project), gây `HarmonyLib … mprotect returned EACCES` mỗi lần domain reload trên Apple Silicon. Ai cần thì tự thêm rồi tắt tay ở `Window → Fast Script Reload → Welcome Screen → New Fields`.
- `scripts/sync-unity-template-deps.mjs`: cả hai package trên được thêm vào `SKIP_NEW_PACKAGES`. Nếu không, `AUTO_ADD_SCOPE = "com.ezg."` sẽ tự thêm chúng trở lại ở lần publish kế tiếp và việc gỡ trên trở thành vô nghĩa.

**Fixed**
- **324 → 60 GUID chết** (477 → ~70 cặp file × GUID; 189 → 39 file dính). Đây là hư hỏng có từ trước đợt refactor — prefab bê từ `m1`/`a004`/`sm006` mà không mang art theo — và bản `ezg.base.visuals` đang ship cũng dính y hệt, nên mọi project init ra trước bản này đều có UI template thiếu sprite.
- Xoá 49 file mồ côi (không asset nào tham chiếu, không nằm trong `Resources/`): 35 TMP font material (`Panton-ExtraBold *`, `Barlow-Black *`, `ARIAL`, `LiberationSans`, `TextMeshPro_Sprite`) trỏ vào atlas của font **không hề có trong project**; 12 `.mat`/`.controller` VFX di sản; và 2 asset `Visual/ArtAsset/Shared/Transition/Data/` (`UnityScreenNavigatorSettings.asset`, `Demo Scene UI Layer Setting.asset`) vốn là **missing script** vì chỉ phần `Transition/Shared/*.cs` được copy sang. Sau đợt này **không còn missing script nào**.
- `Visual/ArtAsset/UI/_Legacy/` đã được dọn: 18 asset (đều đang được tham chiếu thật) move sang `UI/common/{Badge,Button,Frame,Pattern}` kèm `.meta` nên giữ nguyên GUID; thư mục `_Legacy/` bị xoá.
- Skill `create-ui` không còn tham chiếu `TriggerTemplate` — prefab này đã bị refactor xoá khỏi Tank nhưng `SKILL.md` và `references/prefab-templates.md` vẫn hướng dẫn dùng. Không khôi phục nó từ bản R2 cũ vì bản đó kéo theo 23 GUID chết + 1 missing script và toàn bộ variant đều là của game cũ (`Employee`, `Shelf`, `BuyParkingSlot`, `Bus stop`…). 14 prefab template còn lại mà skill tham chiếu đã verify là tồn tại đủ.
- Gỡ khỏi `DefaultSetup/`: 4 script `ns-*.ps1`, `.claude/settings.local.json`, 7 file `.DS_Store` và thư mục rỗng `.claude/worktrees/`.

**Known issues**
- Còn **60 GUID chết** rải trong 39 file (17 features/scenes + 22 visuals) — không truy hồi được từ bất kỳ project nguồn nào. Gồm 28 `m_Sprite`, 25 `objectReference` (override sprite của prefab instance), 4 `target` (nested prefab), 1 shader và 1 `m_LightingDataAsset` (vô hại). Ảnh hưởng: một vài ô icon/sprite trong UI template hiển thị trống. Không có missing script, không ảnh hưởng compile. Nặng nhất: `TabHelper_NavigationBar_mainMenu` (10), `screen_shop` (8), `screen_save_found` (4), `shop_item_raw_pack` (4).
- Chưa có asset `LanguageData` nào trong template (`find Assets -iname "*LanguageData*"` → 0). 21 key đang được code gọi sẽ render ra raw key dạng `#common_key` trên UI. Giữ nguyên hiện trạng — project mới tự tạo bộ ngôn ngữ của mình.
- `ezg.base.features` và `ezg.base.visuals` **không độc lập**: `Assets/_Project/Visual/Ezg.Features.asmref` trỏ vào `Ezg.Features.asmdef` (24 file `.cs` trong `Visual/` compile thẳng vào `Ezg.Features.dll`), và `Ezg.Features.asmdef` reference `UnityScreenNavigator.Runtime` — asmdef này nằm trong `Visual/ArtAsset/Shared/Transition/`, tức thuộc gói **visuals**. Cài `features` mà thiếu/lệch phiên bản `visuals` thì `Ezg.Features.dll` không compile được. Hai gói phải luôn re-export và bump hash cùng lúc.

## 2026-08-03

**Fixed**
- `com.coffee.ui-effect` không cài được ("Repository does not contain a package manifest") — Tag `5.11.1` của UIEffect là một Unity project đầy đủ, `package.json` nằm ở `Packages/src` chứ không ở root. Bổ sung `?path=Packages/src` vào git URL.
- Project init từ template không compile được: `error CS0246: The type or namespace name 'UIShadow' could not be found` tại `Assets/_Project/Editor/Shared/PsdLayerImporter.cs` — UIEffect v5 đã chuyển `UIShadow`/`UIGradient`/`UIShiny` ra `Samples~/v4 Compatible Components/` (thư mục `Samples~` không được Unity compile), trong khi `PsdLayerImporter.cs` và 6 prefab trong `ezg.base.features.unitypackage` vẫn dùng API v4. Bổ sung sample "v4 Compatible Components" (`Assets/Samples/UI Effect/5.11.1/`) vào `ezg.base.features.unitypackage`, giữ nguyên GUID gốc nên prefab tự bind lại (`UIShadow` = `0848bff1…`, `UIGradient` = `3fb48d82…`, `UIShiny` = `f19b7e22…`) và khớp 2 assembly `UIEffect`/`UIEffect-Editor` mà `Ezg.Editor.asmdef` đã tham chiếu sẵn.
- `asset-catalog.json` khai sai SHA-256 của `ezg.base.features.unitypackage` (`911b3683…`) so với file thật trên R2 → Feature Hub tab "Unity Packages" fail verify khi cài package này. Đồng bộ lại hash ở cả `asset-catalog.json` và `unity-template.json` (`181c8b18…`) và republish catalog.
- Init project mới báo `error CS2001: Source file '…/Assets/Plugins/Sirenix/Odin Inspector/OdinUpgrader.cs' could not be found` — `OdinUpgrader.cs` là script `[InitializeOnLoadMethod]` của Sirenix, chạy xong thì **tự xoá chính nó** bằng `File.Delete` thô (không qua `AssetDatabase`), nên Unity vẫn giữ file trong source list và compile fail 1 lần trước khi `AssetDatabase.Refresh(ForceUpdate)` của nó dọn lại. Lỗi tự khỏi nhưng hiện đỏ ở mọi lần init. Đã loại `OdinUpgrader.cs` khỏi `Odin Inspector 4.0.1.3.unitypackage` (47 → 46 entry, 46 entry còn lại byte-identical). An toàn vì upgrader chỉ dọn Odin đời cũ (`.mdb`, `Demos` giải nén cũ, `Odin Inspector/Scripts` compat layer) — fresh install không có; `SirenixAssetsPath` mà nó xoá vô điều kiện thì package không ship; không asset/script nào tham chiếu GUID `9a8a412f…`. `EnsureCorrectOdinVersion.cs` được giữ nguyên (nó rename các file `_tmp`).

## 2026-07-30

**Added**
- Thêm asset TinySauce-8.2.0 (Voodoo Sauce SDK) vào `asset-catalog.json` — SDK bên thứ ba, cài optional qua Asset Installer, file + catalog đã publish lên R2.

## 2026-07-17

**Added**
- Pipeline `/planning-system` theo hướng design-first — Hỗ trợ quy trình từ phân tích tính năng, viết thiết kế kỹ thuật đến phân bổ task theo thứ tự phụ thuộc và tiếp tục từ mapping artifact.
- Bộ công cụ UI mockup và review theo hướng spec-first — Tích hợp approval gate (cần người duyệt), visual reviewer và capability gate cho catalog để tránh đóng gói catalog của game gốc vào template.
- Bộ unit tests cho backlog và UI review — Kiểm thử tự động tính đúng đắn của mockup-promotion contract và các quyết định duyệt UI.

**Changed**
- Cải tiến backlog contract — Đổi tên file task thành `NNN-TIER-slug.md`, kiểm tra ràng buộc tier/dependency/mockup preflight, hỗ trợ `defer`, `Requires: unity-editor` và xử lý branch chung.
- Nâng cấp cơ chế loop runner và thông báo — Bổ sung trạng thái `VISUAL_BLOCKED`, `MOCKUP_BLOCKED`, `EDITOR_REQUIRED` và tiếp tục vòng lặp khi gặp task bị hoãn (`DEFERRED`).
- Chuẩn hóa và tổng quát hóa tài liệu hướng dẫn — Loại bỏ các đường dẫn đặc thù của dự án QUIVER/A004 và các giả định gameplay để giữ template độc lập.

**Fixed**
- Sửa lỗi đường dẫn DefaultSetup trong `backlog-ops.py` — Tìm đúng thư mục DefaultSetup khi chạy trong monorepo và tự động fallback kiểm tra `clone:<Prefab>` qua Assets khi chưa xuất catalog.

## 2026-07-09

**Added**
- `backlog-ops.py` — Kịch bản quản lý backlog tự động giúp chuyển đổi trạng thái và cập nhật `BACKLOG.md` nhất quán, tránh sai sót khi sửa bằng tay.
- `codegraph-doctor.sh` — Kịch bản kiểm tra trạng thái cài đặt CLI và cấu hình MCP CodeGraph trước khi làm việc với UI Catalog.
- Cổng kiểm thử tự động Runtime Smoke — Tích hợp bước chạy game ở Play Mode và kiểm tra lỗi qua Unity MCP server trong luồng xử lý backlog của agent.

**Changed**
- Đồng bộ hóa thao tác backlog qua script mới — Cập nhật các skill (`add-to-backlog`, `run-backlog`) và hướng dẫn trong `CLAUDE.md`, `BACKLOG.md` để sử dụng `backlog-ops.py`.
- Tối ưu hóa vòng lặp chạy backlog tự động — Cập nhật các script liên quan đến loop runner và script notify để hỗ trợ gửi thông báo trạng thái tin cậy hơn.

## 2026-07-02

**Added**
- `DefaultSetup/backlog/_TEMPLATE_WF.md` — Thêm template task hỗ trợ workflow-backed scaffolding.
- `DefaultSetup/.claude/scripts/run-backlog-loop.sh` — Thêm script loop runner hỗ trợ chạy backlog tasks tự động trên macOS/Linux.

**Changed**
- `unity-template.json` — Nâng cấp package `com.ezg.iap` lên phiên bản mới nhất `0.2.0`.
- Cấu hình nhánh phát triển (`branch model`) — Cập nhật branch `agent/dev` tự động lấy base branch hiện tại (`$BASE_BRANCH`) thay vì hardcode `develop`.
- Hỗ trợ task dạng hybrid — Cập nhật `_TEMPLATE_M.md`, `_TEMPLATE_L.md` và các skill liên quan hỗ trợ task kết hợp workflow scaffold và logic tùy biến.

## 2026-07-01

**Added**
- Cấu trúc dự án mẫu — Thêm quy tắc định nghĩa phân chia cấu trúc dự án (framework-standard vs gameplay độc lập) cùng cấu trúc thư mục chuẩn.

**Changed**
- Tinh gọn quy tắc Claude trong DefaultSetup — Rút gọn nội dung compile-validation và định dạng đầu ra (output-format) theo dạng tổng quát hóa, không mang tính dự án cụ thể.
- DefaultSetup CLAUDE.md — Liên kết thêm quy tắc cấu trúc dự án mới để Claude/Agent nắm bắt thông tin.

## 2026-06-30

**Changed**
- unity-template.json — Nâng loạt package Unity lên bản mới nhất chạy được Unity 6.3: addressables 2.7.2→3.1.0, animation.rigging 1.3.0→1.4.1, cinemachine 2.10.4→2.10.7, collab-proxy 2.9.3→2.12.4, formats.fbx 5.1.4→5.1.6, recorder 5.1.2→5.1.6, timeline 1.8.9→1.8.12, visualscripting 1.9.7→1.9.11.
- unity-template.json — Cập nhật package mob-sakai: com.coffee.ui-particle 4.11.2→4.13.2; com.coffee.ui-effect chuyển từ branch `#upm` sang pin tag 5.11.1.
- Publish lại manifest template lên R2 (`unity-template/latest.json`) để client nhận version mới; đồng thời đồng bộ com.unity.purchasing 5.3.1 khớp bản IAP v5.

## 2026-06-27

**Added**
- backlog/_GUARDRAILS.md — Thêm tài liệu định nghĩa chi tiết và cách kiểm thử cho các thẻ guardrails để chuẩn hóa quy trình review.

**Changed**
- unity-template.json — Cập nhật com.ezg.core lên 0.1.2 và com.ezg.featurehub lên 0.1.7.
- Tối ưu hóa backlog review loop — Chỉ chạy performance-reviewer khi phát hiện thay đổi nhạy cảm về hiệu năng, đồng thời tinh gọn prompt gửi cho reviewer để tiết kiệm token.
- Cập nhật các template task — Thay đổi các file mẫu L, M, S để tham chiếu tới danh sách guardrails chung trong _GUARDRAILS.md thay vì liệt kê inline.

## 2026-06-25

**Changed**
- CLAUDE.md Auto-Inject Rule — Bổ sung quy tắc trong run-backlog SKILL để ngăn chặn việc đọc lại CLAUDE.md nhằm tránh lãng phí tokens.
- Reviewer Models — Thay đổi mô hình AI thực thi cho các agent code-reviewer, performance-reviewer, và security-auditor từ Opus sang Sonnet để tối ưu thời gian chạy.

## 2026-06-24

**Added**
- backlog-preflight.py — bản port Python của preflight, chạy được trên macOS/Linux không cần PowerShell (JSON output giống hệt bản .ps1)
- backlog/ scaffolding — 5 task template (_TEMPLATE + XS/S/M/L) và 4 thư mục vòng đời pending/todo/in-progress/done
- .gitignore Unity generic (ignore Library/, builds, .agents/*, .codegraph/)
- LOCAL-ONLY MODE cho /run-backlog: tự bỏ qua git fetch/pull/push origin khi checkout không có remote

**Changed**
- Preflight trong skill + CLAUDE.md: hỗ trợ song song Windows (.ps1) và macOS/Linux (.py)
- dotnet build: bỏ hardcode tên solution → tự dò .sln trong repo root
- .mcp.json: GITLAB_PROJECT_ID → placeholder ezg-puzzle-space/PROJECT_NAME

**Fixed**
- Sai tên solution m1.sln trong skill run-backlog
- .agents/ từ bản copy stale → symlink trỏ về .claude/ (sửa đổi ở .claude/ tự lan sang)
