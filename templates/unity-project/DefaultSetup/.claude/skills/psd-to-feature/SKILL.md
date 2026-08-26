---
name: psd-to-feature
description: Dựng màn hình/scene Unity từ file PSD mockup của hoạ sĩ — xuất từng layer ra PNG + manifest toạ độ, set import settings, rồi viết Editor importer dựng lại prefab/scene đúng bố cục PSD và wire vào controller. Dùng khi được giao "ghép PSD vào màn", "import PSD", "áp art mockup vào feature", hoặc khi hoạ sĩ gửi bản PSD mới cần chạy lại.
argument-hint: [đường dẫn PSD + feature đích — vd Art/Shop.psd vào Storefront]
---

# PSD → màn hình Unity

Biến một file PSD mockup thành prefab/scene chạy được, bằng **tool chạy lại được** chứ không
phải kéo tay trong Inspector. Hoạ sĩ nhích một layer → chạy lại 2 lệnh là UI về đúng chỗ.

Bản mẫu đã chạy thật (đọc khi cần đối chiếu):

| Vai trò | File |
|---|---|
| Importer phần Canvas | [ShopPsdHudImporter.cs](../../../Assets/_Project/Editor/ProjectSpecific/PsdLayoutImport/ShopPsdHudImporter.cs) |
| Importer prop trong scene | [ShopPsdSceneImporter.cs](../../../Assets/_Project/Editor/ProjectSpecific/PsdLayoutImport/ShopPsdSceneImporter.cs) |
| Kết quả | `screen_storefront_hud.prefab` + `StorefrontScene.unity` |

Script + code dùng chung của skill:

- `scripts/psd_export.py` — `--tree` soi layer, `--text` soi font/size/baseline, xuất PNG + manifest.
- `scripts/apply_sprite_meta.py` — set pixels-per-unit / pivot / 9-slice thẳng vào `.png.meta`.
- `reference/PsdLayout.cs` — loader manifest + helper đặt node. **Copy một lần** vào
  `Assets/_Project/Editor/ProjectSpecific/PsdLayoutImport/` nếu chưa có, rồi mọi importer dùng chung.

Cài trước (1 lần/máy): `python3 -m pip install 'psd-tools[composite]'`

---

## STEP 0 — Chốt phạm vi trước khi đụng vào PSD

Mockup vẽ **cả màn**, nhưng phần lớn thứ trong đó game đã có rồi. Trả lời 3 câu này trước,
sai ở đây là dựng xong phải đập:

1. **Cái gì là art mới** (xuất PNG) — nền, khung, icon, thẻ, thanh tab.
2. **Cái gì game đã có, PSD chỉ vẽ lại** — thanh tiền, nút setting, nhân vật có animation,
   popup dùng chung. Những cái này **CLONE prefab sẵn có rồi đắp art lên**, không dựng tay:
   dựng tay là mất sạch hành vi (đếm số, tiền bay, âm thanh, tracking) và màn này lệch với
   cả game. Xem `ClonePill` trong bản mẫu.
3. **Cái gì trong PSD chỉ để chỉ chỗ** — hình nhân vật tĩnh trong mockup. Cho vào `anchors`:
   chỉ lấy toạ độ, không xuất PNG.

Chốt luôn **nhánh nào được dựng lại**. Importer xoá sạch con của nhánh đó rồi dựng lại, nên
nhánh phải là của riêng feature. Đụng vào template dùng chung (`popup_template`,
`full_screen_template`, `screen_currency_bar`) là hỏng mọi màn khác.

## STEP 1 — Soi cây layer, viết map

```bash
python3 .claude/skills/psd-to-feature/scripts/psd_export.py --psd <đường dẫn>.psd --tree
```

Viết `<tên>_map.json` cạnh file PSD:

```json
{
  "manifest": "shop_layout.json",
  "layers":  { "re_white coin": ["ui", "pill_currency", "nền pill trắng của ô tiền"] },
  "anchors": { "Group 1 copy": "anchor_catcher" },
  "show":    ["icon_sound_Off"]
}
```

Luật viết map:

- **Map theo TÊN layer, không theo index.** Hoạ sĩ lưu lại PSD một lần là thứ tự layer xáo hết
  (bản Shop.psd 2026-08-06 đổi gần như toàn bộ index); map theo index thì lần sau ra ảnh sai
  tên mà không báo lỗi gì. Tên trùng nhau ở hai nhánh → dùng key `"Group/Child"`; trùng trong
  CÙNG một nhánh (hoạ sĩ copy nút rồi quên đổi tên) → key `"tên#N"`, `--tree` in sẵn `N` phải
  dùng. Không có `#N` thì script chặn hẳn chứ không đoán bừa.
- **Đừng tin tên layer, soi ảnh rồi mới đặt tên file.** Setting.psd có layer tên
  `icon_haptic_On` nhưng vẽ art OFF (bản copy của hàng trên, quên đổi tên). Xuất cả cụm ra một
  contact sheet rồi nhìn một lượt trước khi chốt map — rẻ hơn nhiều so với phát hiện lúc bấm
  thử thấy toggle hiện ngược trạng thái.
- Tên layer trong PSD lộn xộn (`Layer 331`, `icon ctach`) → **tên file đặt theo VAI TRÒ**
  (`nav_tab_selected`, `card_upgrade_bg`), và ghi chú tiếng Việt cạnh mỗi dòng map cho biết nó
  là cái gì trong màn. Sáu tháng sau không ai đoán ra `Layer 219 copy` là gì.
- Group: `ui` = đi vào Canvas, `prop` = SpriteRenderer trong scene. Tách từ đầu vì hai loại
  này khác pixels-per-unit, khác cách đặt toạ độ.
- **Bỏ layer bẹt.** PSD hay có bản nháp gộp sẵn cả cụm để hoạ sĩ xem tổng thể, trùng nội dung
  với các layer rời — import vào chỉ tổ đè lên nhau.
- Layer chưa map được liệt kê ở cuối mỗi lần chạy. Đọc danh sách đó, đừng bỏ qua: art mới của
  hoạ sĩ nằm ở đấy.

## STEP 2 — Xuất PNG + manifest

```bash
python3 .claude/skills/psd-to-feature/scripts/psd_export.py \
  --psd Assets/_Project/Features/<F>/Art/<Tên>.psd \
  --map Assets/_Project/Features/<F>/Art/<tên>_map.json \
  --out Assets/_Project/Features/<F>/<Feature>/Visuals/<Tên>Psd
```

Ra `<out>/{ui,prop}/*.png` + manifest `{name, group, psdName, x, y, w, h}` — bbox này là thứ
bản `.jsx` "Export Layers to Files (Fast)" **không** xuất, và là lý do có tool riêng.

Nhiều artboard trong một file (bản mockup + ảnh chụp game dán làm tham chiếu) thì thêm
`"origin": [x, y, w, h]` — rect artboard, copy từ dòng `[artboard]` của `--tree`. Manifest quy
mọi toạ độ về gốc artboard nên importer viết y như PSD một artboard; thiếu nó thì cả màn lệch
đúng `x` px và trôi ra ngoài canvas.

Art nền hoạ sĩ hay vẽ tràn ra ngoài artboard cả nghìn px (smart object phóng to) — phần ngoài
thường là **rác**: vệt trắng, pixel kéo giãn. Xuất nguyên bản thì texture vượt `maxTextureSize`
và bị ép nhỏ, mất nét. Dùng `crops` trong map, và **soi ảnh crop trước khi tin**: crop rộng hơn
artboard chỉ đúng khi hoạ sĩ thật sự vẽ rộng ra.

Đọc kỹ 3 cảnh báo cuối output:

- **"có trong map nhưng PSD không còn"** — hoạ sĩ xoá/đổi tên layer. PNG cũ thành mồ côi, và
  nếu prefab đang dùng nó thì phải quyết: bỏ khỏi thiết kế hay hoạ sĩ đặt tên khác.
- **"layer bị tắt mắt"** — giữ nguyên PNG cũ, KHÔNG ghi đè. Layer tắt mắt vẫn composite ra ảnh
  trong suốt đúng kích thước; ghi đè là mất sprite mà không ai biết. Entry vẫn vào manifest,
  không thì prop mất ảnh + nhảy vị trí. **Ngoại lệ:** mockup chỉ bày được MỘT trạng thái nên
  trạng thái kia (icon toggle OFF, nút đang tắt) bị tắt mắt dù vẫn là art phải dùng — liệt kê
  đúng những layer đó vào `show` để ép xuất. Opt-in từng layer, không nới luật chung.
- **"layer chưa map"** — soát art mới.

## STEP 3 — Import settings cho PNG

Mở Unity một lượt cho nó sinh `.meta`, rồi:

```bash
python3 .claude/skills/psd-to-feature/scripts/apply_sprite_meta.py \
  --dir <out> --config <tên>_sprite_meta.json --dry-run   # xem trước rồi bỏ --dry-run
```

- **Bước này KHÔNG bỏ được**, kể cả khi chỉ có sprite UI: PNG mới import theo default của
  project (thường là Texture, `spriteMode 0`), `LoadAssetAtPath<Sprite>` trả null và importer
  chạy sạch mà không gán được ảnh nào. Script luôn ép `textureType 8` + `spriteMode 1`. Ảnh
  hoạ sĩ gửi rời cũng dính (LOGO.png của splash đang là `spriteMode 2` mà không có slice nào).
- **`prop` phải đúng px/unit của camera**: `pxPerUnit = bề ngang PSD (px) / bề ngang khung hình (unit)`.
  Shop: camera ghim nửa bề ngang 4.0 unit → 1112 / 8.0 = **139**. Sai số này là sprite to/nhỏ
  lệch hẳn so với mockup. Camera phải ghim theo BỀ NGANG thì con số mới đúng ở mọi tỉ lệ màn.
- **`ui` để 100** — RectTransform tính theo pixel, px/unit không ảnh hưởng.
- **9-slice** chỉ cho sprite bị kéo giãn (nền thẻ, pill, thanh bar, nền tab). Border đo từ
  góc bo của chính ảnh đó. Icon để 0.
- **Pivot** giữa (mặc định); vật/nhân vật đứng trên sàn để `[0.5, 0]` vì code đặt
  `transform.position` = chỗ chân chạm đất.
- **Độ nét mép** — script tự ép `enableMipMap 0`, `wrap Clamp`, `meshType FullRect` (khi có
  border). Đây là ba default của Unity SAI cho sprite UI, và cả ba đều im lặng: chỉ thấy mép
  nút "nham nhở" chứ không có log nào. Mipmap là nặng nhất — canvas gần như luôn thu nhỏ
  (Expand, ref 1080x2400, game view lùn → ~0.7) nên Unity nhảy sang mip 1 và viền tối bệt ra.
  Đừng đi soi lại wiring hay art khi thấy mép bẩn, soi `.png.meta` trước.
- **Nén theo platform** — `platform` (theo group) hoặc `platforms` (theo từng ảnh, key
  `group/tên`) ghi `[format, quality]` cho Android/iOS. Convention project: art UI mép cứng
  dùng `[48, 100]` = ASTC_4x4 q100 (618 file đang dùng), khớp `Textures/Buttons/9Slice/Bt_*`
  và `SD_Iocn_quit`. Không đặt cho ảnh khổ lớn: ASTC 4x4 là 8bpp, một tấm khung 971x1619 ngốn
  1.5MB VRAM mà nó là mảng màu phẳng — nén mạnh vẫn đẹp.

## STEP 4 — Chốt hệ toạ độ

**Canvas.** ĐỌC CanvasScaler thật của màn đó, đừng đoán — `m_UiScaleMode`, `m_ReferenceResolution`
và nhất là `m_ScreenMatchMode`. Hệ số quy đổi PSD → unit canvas suy ra từ trục bị GHIM:

| Screen match mode | scaleFactor | Trục ghim (máy dọc) | Hệ số |
|---|---|---|---|
| Expand | `min(w/refW, h/refH)` | bề ngang | `refW / psdW` |
| Shrink | `max(w/refW, h/refH)` | chiều cao | `refH / psdH` |
| MatchWidthOrHeight, match=0 | theo bề ngang | bề ngang | `refW / psdW` |

Lấy nhầm trục là mọi thứ sai tỉ lệ đúng một hằng số — splash lấy nhầm `1080/1112` thay cho
`1920/2400` thì to hơn 21%, thanh loading rộng hơn cả canvas và tràn ra ngoài hai mép, mà nhìn
màn hình thì chỉ thấy "hơi to", không thấy sai ở đâu. Trục KHÔNG ghim thì bề rộng canvas đổi
theo máy → thứ nằm giữa phải neo giữa (`PsdCanvas.Center`), đừng neo mép.

So khổ artboard với reference resolution. Shop: canvas game
1080×2400 mode Expand, artboard 1112×2400 → dọc map 1:1, ngang map theo tỉ lệ. Đặt node bằng
`PsdCanvas` — **neo MÉP, không neo theo tỉ lệ**: máy chạy từ 9:20 tới iPad, neo tỉ lệ thì trên
máy rộng cả cụm trôi vào giữa và lề hai bên phình không đều.

- Cụm bám một cạnh → `Top/Bottom`.
- Thanh/hàng thẻ thiết kế cho chạm gần hai cạnh → `StretchEdge` + `HorizontalLayoutGroup`,
  không đặt cỡ cố định (trên iPad sẽ thành cụm nhỏ lọt thỏm giữa màn).
- Con nằm trong một khối → `StretchInParent` / `RightInParent` / `InParent`, giữ đúng lề PSD
  vẽ giữa hai rect.
- Art tràn khỏi artboard (thanh tab cao 254 mà artboard cắt ở 2400) là **cố ý**: dựng đúng
  chiều cao art rồi đẩy tụt xuống phần dôi, đừng nén cho vừa.

**Scene.** `PsdWorld(psdW, psdH, pxPerUnit)` → `Center()` cho prop, `Feet()` cho chỗ đứng agent.

## STEP 5 — Chữ

```bash
python3 .claude/skills/psd-to-feature/scripts/psd_export.py --psd <...>.psd --text
```

Ra font, **size thật** (`FontSize × scale` của transform — số trong panel Character của
Photoshop là số CHƯA nhân transform), baseline `ty`, và màu từng run.

- Đặt chữ theo **baseline** (`TextAlignmentOptions.Baseline`, rect cao ~1.6× size). Baseline
  là con số PSD cho sẵn và **không trôi khi autosize co chữ**; canh giữa khối chữ thì trôi.
- Chữ trong game dài hơn mockup ("Work Speed" thay cho "Speed") → bật autosize với
  `fontSizeMax` = size PSD, `fontSizeMin` đủ nhỏ cho ca dài nhất thật (vd `Lv 21` khi max
  level 20). Không thì chữ tràn sang icon/mép thẻ.
- Layer bẹt không có engine data → đo từ pixel, sai số ~1px vì antialias. Ghi rõ trong comment
  con số nào lấy từ PSD, con số nào đo tay.
- **Font: KHÓA Ở `TiltWarp2 SDF`** — guardrail `[FONT]`, chốt user 2026-08-20, chi tiết trong
  `.claude/rules/code-style.md`. Report `--text` ra tên font gì cũng **không import `.ttf/.otf`
  mới, không tạo TMP font asset mới, không set `fallbackFontAssetTable`**. Dựng bằng TiltWarp2
  rồi **báo lại user** là mockup dùng font khác — đừng tự thêm font cho giống PSD, cũng đừng
  xin font trong file order art.
- Chữ có dấu **không cần font fallback**: `TiltWarp2 SDF` là Dynamic atlas
  (`m_AtlasPopulationMode: 1`) trỏ về `TiltWarp2.ttf`, ttf phủ đủ 90/90 codepoint
  `U+1EA0–U+1EF9` + `đ/Đ/ơ/ư/ă/â` → glyph tự bake lúc runtime. Bảng glyph trong `.asset` chỉ
  có 71 ký tự là **bình thường** (mới bake tới đó), không phải thiếu font.
- Viền/bóng/gradient theo PSD → **material preset của chính TiltWarp2**, không phải font khác
  (mẫu sẵn: `…/EndResultPsd/generated/TiltWarp2 SDF - EndResult *.mat` và `TiltWarp2-Num-*.mat`
  cạnh font asset). Shader phải là `TextMeshPro/Distance Field` — `Mobile/Distance Field` vẽ
  ra trống trơn, không log lỗi gì.
- Text hiển thị vẫn phải qua localize ([LOCALIZE]) — nhưng repo **chưa có `LanguageData` nào**,
  nên `GameSystems.Localize(key)` trả `"Common: <key>"` cho MỌI key (kiểm chứng bằng cách gọi
  thẳng hàm đó lúc Play, đừng suy đoán từ prefab: `LocalizeHelper` chỉ ghi đè khi object ACTIVE
  và đang Play, nên nhìn prefab vẫn thấy chữ đẹp). Gắn `LocalizeHelper` trần vào nhãn ngắn là
  lúc chạy "ON" biến thành "Common: on", tràn khỏi nút. Dùng
  `LocalizeTextFallback` (`Features/_Shared/Localize/`) — vẫn đi qua localize nhưng giữ chữ
  tiếng Anh trong mockup khi bảng còn thiếu key.

## STEP 6 — Viết importer

Một file `<Tên>PsdHudImporter.cs` (Canvas) và/hoặc `<Tên>PsdSceneImporter.cs` (scene) trong
`Assets/_Project/Editor/ProjectSpecific/PsdLayoutImport/`, `#if UNITY_EDITOR`, MenuItem
`Tools/<Game>/PSD/...` (**MenuItem chỉ ASCII** — có dấu là gọi qua MCP không ra).

Khung: load `PsdLayout` → `PrefabUtility.LoadPrefabContents` → xoá con của đúng nhánh feature
→ dựng từng khối → wire → `SaveAsPrefabAsset` trong `try/finally` có `UnloadPrefabContents`.

**Dựng lại hay chỉ đắp art?** Xoá-rồi-dựng-lại chỉ được phép khi KHÔNG ai giữ ref tới node cũ.
Node là instance của prefab template dùng chung, hoặc có controller trong scene trỏ vào (splash:
`SplashSceneController._loadingSlider`), thì phải **đắp art tại chỗ** — xoá đi dựng lại là ref
trong scene thành null và màn chết ở `Awake`. Và khi đắp lên instance template, mọi thay đổi
phải nằm ở INSTANCE, đừng sửa file template (`SliderTemplate.prefab`) — sửa ở đó là đổi mọi
thanh trượt của cả game.

Luật bắt buộc:

- **Missing script chặn lưu prefab.** `SaveAsPrefabAsset` ném lỗi nếu prefab còn component có
  script đã bị xoá khỏi project (dead ref của game template rất hay còn sót). Triệu chứng dễ
  hiểu nhầm: menu chạy xong, log báo thành công, mà file prefab không đổi một byte. Gọi
  `GameObjectUtility.RemoveMonoBehavioursWithMissingScript` trên nhánh đang sửa trước khi lưu,
  và log lại đã gỡ gì.

- **Chạy lại phải ra kết quả y hệt (idempotent).** Đừng cộng dồn độ lệch: lần hai node đã nằm
  ở đích, độ lệch tính ra 0 và nó đứng im — ép giá trị tuyệt đối (`localPosition = 0` rồi đặt
  cha) thay vì cộng thêm.
- **Scene importer: chặn Play mode.** Sửa lúc đang Play thì thoát Play là mất sạch, mà
  `MarkSceneDirty` còn ném giữa chừng để lại scene sửa dở.
- **Tắt renderer, đừng tắt GameObject**, khi có hệ thống runtime bật lại object đó mỗi lần
  boot (khoá/mở trạm, red dot). Tắt object là bị bật lại ngay; tắt renderer còn giữ được FX
  nhân bản sprite từ node đó.
- **Màn UI: THỨ TỰ CON là hợp đồng, không phải thẩm mỹ.** `FeatureBaseController.Awake` lấy
  cứng `GetChild(0)` làm nền mờ và `GetChild(1)` làm `MainUI` — cụm mà `UITransition` scale/fade
  lúc mở màn. Dựng nhánh mới rồi `SetAsLastSibling()` là `MainUI` vẫn trỏ vào nhánh CŨ vừa bị
  tắt: bấm nút mở màn ra **không thấy popup đâu, console sạch trơn, không một dòng lỗi**. Nhánh
  mới phải `SetSiblingIndex(1)`. Kiểm bằng cách in thứ tự con sau khi lưu, đừng tin mắt nhìn
  trong Editor — ở Editor prefab vẫn hiện đủ vì chưa ai chạy `Awake`.
- **Không đổi cha được node là prefab instance lồng bên trong.** `SetParent` lên nó ném
  "Setting the parent of a transform which resides in a Prefab instance is not possible", rồi
  node ĐỨNG NGUYÊN chỗ cũ — nếu chỗ cũ là nhánh vừa tắt thì màn mất luôn nút đó mà log tổng
  kết vẫn báo "xong". Muốn dùng lại một nút template (nút X, nút chung) thì **instantiate bản
  mới từ chính prefab đó** vào nhánh mới rồi trỏ ref sang, đừng kéo cái cũ. Node thường
  (không phải instance) thì kéo bình thường.
- **Wire bằng `SerializedObject`** (`PsdBuild.Set`) vì field controller là `[SerializeField]
  private`; thiếu field thì log warning rồi đi tiếp, đừng ném.
- **Thêm listener, đừng xoá sạch rồi add lại.** Prefab template mang sẵn `SoundPlayController
  .PlaySoundCustom` trong `onClick`; xoá hết persistent listener để tránh trùng là nút mất
  tiếng bấm. Mỗi lần chạy đã là instance mới nên không có chuyện trùng.
- Node thiết kế bỏ nhưng controller vẫn set (nhãn, dòng chữ cũ) → **giữ node và tắt đi**, để
  code runtime khỏi phải rẽ nhánh null.
- Không hardcode toạ độ lấy từ mockup vào code: đọc từ manifest. Số duy nhất được hardcode là
  số **không có trong PSD** (đo tay) — và phải có `const` + comment nói rõ đo ở đâu.
- Đụng gì tới node của gameplay (chỗ spawn, hàng chờ, collider vùng chạm) thì phải kéo theo
  cả cụm: dời mỗi sprite là nhãn/collider đứng lại chỗ cũ.

## STEP 7 — Chạy và kiểm chứng

1. Chạy MenuItem (hoặc `unity_execute_menu_item` qua MCP).
2. Đọc Console: mọi `[PSD]` warning là một sprite/field rơi mất — sửa hết rồi mới xem hình.
3. Chụp lại đối chiếu mockup bằng **`unity_graphics_game_capture`** (render camera ra ảnh mới).
   `unity_screenshot_game` chụp framebuffer của cửa sổ Game — ở Edit mode cửa sổ không tự vẽ
   lại, ảnh ra là frame CŨ trộn với frame mới: đã gặp cảnh chụp splash mà đáy màn còn nguyên UI
   loading của scene chạy Play trước đó, suýt đi sửa một cái bug không tồn tại.
4. Đối chiếu bằng SỐ, đừng chỉ nhìn: đọc lại rect đã lưu trong prefab và quy ngược ra px PSD.
   Sai tỉ lệ đều (kiểu lấy nhầm trục CanvasScaler) nhìn mắt thường không ra.
5. Soát ở **ít nhất 2 tỉ lệ màn** (9:20 và 4:3/iPad): cụm bám mép còn đúng khoảng cách, thẻ
   giãn không méo góc bo, thanh tab không lộ dải tràn, chữ dài nhất không đè lên icon.
6. Bấm thử: nút vẫn ăn (vùng chạm nằm trên node có `Image`, không phải node rỗng), popup mở
   đúng, số tiền/level cập nhật.

## Khi hoạ sĩ gửi PSD mới

Ghi đè file PSD → chạy lại STEP 2 → đọc 3 cảnh báo → chạy lại MenuItem. Chỉ khi có art mới
mới phải sửa map (STEP 1) và import settings (STEP 3). Không sửa prefab bằng tay giữa hai lần
chạy: lần chạy sau xoá sạch nhánh đó.
