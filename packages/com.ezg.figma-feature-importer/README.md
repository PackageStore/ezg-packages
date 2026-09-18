# EZG Figma Feature Importer

Biến prefab thô do [`com.ezg.figma-bridge`](../com.ezg.figma-bridge) ghi ra thành **màn hình feature dùng
được**: mỗi frame Figma trở thành **prefab variant của vỏ `screen_template` của project**, instance
component Figma được thay bằng **template của project** (nút, scroll, currency…), sprite trùng byte được
tái dùng thay vì copy, chữ chuẩn hoá về **một font**, và controller được gắn + wire sẵn
(`MainUI`, `_closeButtons`, `FeatureType`, field `[SerializeField]`), kèm codegen partial class tuỳ chọn.

Bridge chỉ dựng lại Figma 1:1. Package này là lớp "biến bản dựng đó thành màn của game".

## Cài

```jsonc
// Packages/manifest.json
"scopedRegistries": [
  { "name": "Easygoing code base", "url": "https://upm-registry-worker.developer-a1f.workers.dev", "scopes": ["com.ezg"] }
],
"dependencies": {
  "com.ezg.figma-feature-importer": "0.1.0"
}
```

`com.ezg.figma-bridge` (≥ 0.4.0) tự về theo dependency.

**Peer requirement:** TextMeshPro (`Unity.TextMeshPro`, có sẵn trong `com.unity.ugui`). Odin **không**
bắt buộc — inspector của package là IMGUI thuần.

## Chạy

1. Bridge: **Tools ▸ EZG Technical Art ▸ Figma Bridge** → `Sync Document`.
   Hook `FigmaFeatureImportHook` (`IFigmaImportPostProcessor`) tự chạy sau mỗi lần Sync / `Run
   Post-Processors (no Sync)` — không phải bấm gì thêm.
2. Muốn dựng lại tay: **Tools ▸ EZG Technical Art ▸ Figma Feature Importer ▸ Rebuild Screens (ship + in place)**.
3. Kiểm: **… ▸ Audit Screens** (hard-check cấu trúc + snapshot PNG).
4. Settings: **… ▸ Select Import Settings** (tạo mới ở `Assets/FigmaFeatureImportSettings.asset` nếu chưa có).

Hai đầu ra mỗi lần chạy:

| Đầu ra | Ở đâu | Khi nào |
|---|---|---|
| **Variant tại chỗ** | chính prefab trong thư mục Screens của bridge | mọi frame, kể cả frame chưa khai entry |
| **Bản ship** | `outputPrefab` của entry (trong `Features/…`) | frame có entry `enabled` — kèm dời sprite, controller, codegen |

Bản thô của bridge được chụp sang `rawScreensFolder` trước khi ghi đè, nên chạy lại bao nhiêu lần cũng ra
cùng kết quả (idempotent) và không cần Sync lại.

## Thứ tự dựng một màn

`ConfigureShell` (bật nhánh popup/full, chọn node nhận thân) → `RemoveOldBody` → `InstantiateRaw` (unpack
bản thô) → đánh số node trùng tên → `renameNodes` → `dropNodes` → **map component → template project** →
`buttonNodes` → `FigmaImage` → `Image` → chuẩn hoá chữ/font → dời sprite (bản ship) → dọn
`LayoutElement`/`ContentSizeFitter`/marker bridge → neo thân → gắn + wire controller → codegen.

## Settings asset

Asset được tìm **theo type** ở bất cứ đâu trong `Assets/`; chưa có thì tạo tại
`Assets/FigmaFeatureImportSettings.asset` với seed mặc định của template EZG.

### Tab "Màn hình" — mỗi frame một entry

| Field | Nghĩa |
|---|---|
| `figmaScreen` | tên frame trên Figma |
| `outputPrefab` | prefab bản ship (`…/Features/<Domain>/<Feature>/Resources/screen_x.prefab`) |
| `spriteFolder` | nơi nhận PNG copy khi project chưa có ảnh trùng byte |
| `layout` | `Popup` / `FullScreen` |
| `bodyAnchor` | `CenterFixed` (đúng khổ frame, neo tâm) / `Stretch` |
| `replaceTemplateFrame` | Figma đã vẽ khung riêng → tắt khung của template popup |
| `keepFullScreenChrome`, `fullScreenBackdrop`, `keepFrameFill`, `backgroundAlpha`, `clickBackgroundToExit` | tinh chỉnh vỏ |
| `controllerType` | assembly-qualified (`Ns.XController, Asm`). Trống = chỉ dựng cấu trúc |
| `featureType` | tên member của enum trên field `FeatureType` của controller |
| `dropNodes`, `renameNodes`, `buttonNodes`, `dynamicTextNodes`, `bindings` | can thiệp theo node |
| `generateBindings`, `localizeKeyPrefix` | codegen `<X>Controller.Figma.cs` (controller phải là `partial`) |

Frame **không** khai entry vẫn được dựng variant tại chỗ: popup hay full screen đoán theo
`popupMarkerPattern` (mặc định node tên `Bg_Dim` / `Bg_Popup*` / `Container_Popup`).

### Tab "Template map" — component Figma → template project

`figmaComponent` khớp **prefix đường dẫn** prefab component của bridge (ăn mọi variant), `template` là
đường dẫn tính từ `templatesRoot`, kèm `textSlot` / `iconSlot` / `faceSlot` / `childrenSlot` /
`deactivate` / `isCloseButton`.

### Tab "Vỏ & codegen" — mọi thứ phụ thuộc project

| Field | Mặc định (template EZG) |
|---|---|
| `screenTemplatePath` | `…/Prefabs/Templates/Popup_Template/screen_template.prefab` |
| `templatesRoot` | `…/Prefabs/Templates/` |
| `rawScreensFolder` | `Assets/Figma/RawScreens` |
| `spriteReuseRoot` | `Assets/_Project/Features` |
| `defaultFontPath` / `font` | `TiltWarp2 SDF` |
| `backgroundNode` / `popupNode` / `fullScreenNode` | `background_button` / `popup_template` / `full_screen_template` |
| `popupBodyPath` | `popup_container/container_content/container` |
| `popupFrameNodes` | `popup_container/BG`, `popup_container/top_container_popup` |
| `fullScreenContentNode` / `fullScreenChromeNodes` | `content` / `top_view`, `botview` |
| `buttonFaceSlot` / `buttonTextSlot` | `btn_container/btn` / `btn_container/container/text_body` |
| `popupMarkerPattern` | `^(Bg_Dim\|Bg_Popup\|Container_Popup)` |
| `snapshotFolder` | `.claude/tmp` |
| `codegenUsings` | `Ezg.Feature.Shared.Localize`, `TMPro`, `UnityEngine`, `UnityEngine.UI` |
| `localizeCallFormat` | `LocalizeFallback.Get({0}, {1})` — `{0}` = const KEY, `{1}` = const FALLBACK; để trống thì gán thẳng FALLBACK |
| `baseControllerTypeName` | `FeatureBaseController` |

## Contract với project

Package **không** tham chiếu assembly của game. Nó chỉ cần, và chỉ khi project có:

- Một prefab vỏ ở `screenTemplatePath` có ba nhánh con theo đúng thứ tự `backgroundNode`, `popupNode`,
  `fullScreenNode`, trong đó nhánh popup có chuỗi `popupBodyPath` và nhánh full có `fullScreenContentNode`.
- Controller kế thừa một lớp cơ sở tên `baseControllerTypeName`, với field serialize `FeatureType`
  (enum), `MainUI` (Transform), `_closeButtons` (Button[]), `ClickBackgroundToExit` (bool),
  `_backgroundAlpha` (float). Field nào không có thì bước wire đó tự bỏ qua — không lỗi.
- Không có controller (`controllerType` trống) thì vẫn dựng được cấu trúc + template + sprite + chữ.

Enum `FeatureType` được đọc/ghi qua `SerializedProperty.enumNames` nên enum đánh số thưa vẫn đúng giá trị.

## Audit

`FigmaImportAudit.Run("<Frame>")` / `RunInPlace` / `RunAllInPlace` trả JSON: prefab có đúng là variant của
vỏ không, đúng một nhánh bật, thân nằm đúng chỗ, controller + `FeatureType` + `MainUI` + `_closeButtons`,
không còn tham chiếu `FigmaImage` của bridge 0.3.x, không override `m_RenderMode`, mọi TMP dùng đúng font,
không còn missing script, kèm snapshot PNG 1080×2400 chụp không cần Play.

## Nguồn

Tách từ `Assets/_Project/Editor/ProjectSpecific/FigmaImport/` của project i001 (Backyard Empire), bỏ mọi
tham chiếu type của game và mọi đường dẫn hằng — nay là data trong settings asset.
