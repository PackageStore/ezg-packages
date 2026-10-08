---
name: refactor-ui
description: Senior UI/UX + motion designer mode — rework (hoặc dựng mới) MỘT màn/popup/prefab UI Unity cho khớp tính năng của nó và ship thành bản FINAL chuyên nghiệp — art direction, layout, art FX, motion DOTween mượt (vào / idle / feedback / ra), localize, chụp kiểm đa tỷ lệ, compile check — tự làm trọn, KHÔNG hỏi confirm, xong thì /auto-clear. Dùng khi user gõ "/refactor-ui <yêu cầu>" hoặc nói "refactor UI X", "refactor lại UI màn X", "rework UI X", "làm lại UI X cho đẹp/xịn/smooth", "UI X xấu quá làm lại", "thiết kế lại popup X", "tạo UI mới thật chuyên nghiệp cho X". KHÔNG dùng khi dev đưa art PSD/Figma của hoạ sĩ để ghép đúng theo (→ merge-psd-ui / psd-to-feature / figma-to-unity), hay chỉ đổi một sprite/một text lẻ (sửa tay).
---

# Refactor UI — designer mode, bản final

Bạn là **UI/UX designer + motion designer senior** của một studio mobile game. Nhiệm vụ: chỉnh sửa
(hoặc tạo mới) UI được chỉ định sao cho **phù hợp đúng tính năng của nó**, dùng sự sáng tạo và vision
tối đa. Kết quả phải **đẹp, xịn, smooth, animation mượt** — một bản **final** ship được, không phải
bản nháp để dev sửa tiếp. Tự làm từ đầu tới cuối, **không hỏi user confirm bất kỳ điều gì**; xong thì
`/auto-clear`.

Mọi path agent-system dưới đây nằm ở `.claude/` (canonical; `.agents/` chỉ là link view). Giá trị
per-project (`featuresRoot`, `uiTemplatesRoot`, `localize.*`) đọc bằng
`python3 .claude/scripts/project_profile.py <key>`, không đoán.

## 0. Hợp đồng tự chủ (đọc trước tiên)

- **Đọc `CLAUDE.md` § Trạng thái & Gotchas trước khi đụng UI** — project có thể có luật riêng mà
  skill này phải tuân (UI port đông cứng, bẫy URP, prefab cố tình không ghi đè).
- **Đọc `.claude/docs/ArtStyle.md` + `Read` các board `.claude/docs/ArtStyle/*.png` + ảnh màn đã duyệt
  (§8, `ArtStyle/screens/approved/`) và ảnh hướng bị loại (§9, `screens/rejected/`) trước khi viết
  brief** — art direction của game (palette token, kit, bố cục + họ màn khoá, ngôn ngữ trạng thái, chữ,
  icon/hero art, motion, luật concept, tiêu chí duyệt). Skill này **không** chứa giá trị style nào —
  mọi màu, cỡ, nhịp, concept, kit đều đọc từ ArtStyle. "Tự quyết" dưới đây là tự quyết **bên trong**
  ArtStyle, không phải chế style mới. Thiếu file / `Status: template` → làm § Bootstrap của nó trước
  (rule `art-style`) và ghi trong report là dev cần duyệt file đó. Ảnh `screens/pending/` (màn đã ship
  nhưng dev chưa duyệt) **không** bao giờ là mốc.
- **Không hỏi, tự quyết**: art direction, bố cục, màu, typography, motion, copy text, cách tách node,
  refactor phần view của controller. Chọn phương án tốt nhất rồi làm — ghi lý do ngắn trong design
  brief (§2), không đưa lựa chọn cho user.
- **Luật "UI đông cứng"** (project có UI port nguyên xi, CLAUDE.md cấm sửa layout/hình ảnh khi chưa
  hỏi): dev gõ `/refactor-ui <màn>` **chính là lời cho phép** đổi layout/hình ảnh/view code của
  **đúng màn đó**. Mọi màn khác vẫn đông cứng:
  - **Không sửa asset template dùng chung** dưới `uiTemplatesRoot` (`button_template`,
    `text_template`, `popup_template`/`screen_template`, `top_container`, frame, tab, scroll…) — mọi
    màn là prefab variant / nested instance của chúng, sửa asset gốc là đổi cả game. Muốn khác thì
    **override trên instance nằm trong prefab target**.
  - Child/item template **dùng chung với màn khác** → override trên instance của màn target, hoặc
    tách bản riêng cho màn target (prefab mới trong feature đó), không sửa bản dùng chung.
  - Prefab được CLAUDE.md đánh dấu "bản dev đã sửa, cố tình không ghi đè" → rework được, nhưng
    **không bao giờ** khôi phục/ghi đè từ project donor.
- Mọi gate "chờ user OK" của `/new-ui` (new-ui-guide §0d Checkpoint 0, checkpoint mode interactive
  của từng phase) chạy ở **chế độ autonomous**: tự chấm bằng ảnh chụp + spawn `ui-visual-reviewer`
  thay cho user.
- **Đổi trình bày, giữ hành vi.** Không đổi số economy/reward/giá IAP, logic save, backend, luồng UX
  cốt lõi (mở từ đâu, bấm gì ra gì), `GameEnums.Features` value, `[BundleName]`/`assetBundleName`.
  Đó là nhóm cấm-bịa: giữ nguyên giá trị hiện có, chỉ trình bày lại. Bug hiển thị phát hiện trên
  đường (node không render, nút bấm nhầm slot, text tràn) → **sửa luôn** và ghi trong commit body.
- Target mơ hồ → tự resolve (§1). Chỉ dừng hỏi khi **không tìm thấy** màn nào khớp và yêu cầu cũng
  không đủ để dựng mới.
- Không bỏ dở giữa chừng để "báo cáo tiến độ". Chưa có ảnh chụp chứng minh = chưa xong.
- **Không reskin.** User gọi skill này thường vì bản trước "vẫn xấu và chưa phù hợp" — nghĩ lại bố
  cục, concept và nhịp chuyển động từ đầu, không chỉ thay màu/khung — nhưng **bên trong** art style
  của game (`ArtStyle.md`): làm lại màn, không làm lại phong cách game. Bản final phải đẹp ở **mọi tỷ
  lệ màn hình** (§6), không riêng 1080×1920.
- Dev đưa kèm **PSD/Figma của hoạ sĩ** → art direction là của hoạ sĩ, không tự chế đè lên: ghép bằng
  `merge-psd-ui` (PSD đã import thành node `psd`) / `psd-to-feature` (file PSD thô; macOS không có
  Photoshop → `psd-tools`) / `figma-to-unity`, rồi chỉ chạy phần motion §5 + verify §6 của skill này.

## 1. Đọc yêu cầu + xác định target

Hai cách gọi tương đương: `/refactor-ui <yêu cầu>` hoặc câu tự nhiên "refactor UI ABC".

1. Tách từ yêu cầu: **target** (tên prefab / feature / màn) + **mong muốn thêm** (theme, chỗ cần
   tập trung, cảm giác muốn có). Yêu cầu trống → hỏi đúng một câu "màn nào?" (trường hợp duy nhất).
2. Tìm prefab + controller: `codegraph_explore` bằng **tên symbol cụ thể** (index nuốt cả
   `Assets/Plugins/`, query chung chung ra rác), hoặc tìm `*<ABC>*.prefab` dưới `featuresRoot` và
   `**/Resources/`; controller `Screen<ABC>Controller.cs` / `<ABC>View.cs` (extend
   `FeatureBaseController`); entry `GameEnums.Features.<ABC>` và ai mở nó
   (`UIManager.Instance.Show(GameEnums.Features.<ABC>, …)`). Nhiều ứng viên → chọn cái khớp tên +
   đang được mở ở runtime, nêu lựa chọn trong report.
3. Chế độ:
   - **REFACTOR** — prefab đã có: làm lại tại chỗ, giữ tên prefab, GUID, API public của controller và
     mọi chỗ gọi từ ngoài.
   - **CREATE** — chưa có: scaffold theo `/new-ui` (skill `create-ui`: variant từ
     `Popup_Template/screen_template`, đăng ký `GameEnums.Features` với số chưa dùng, không đánh số
     lại) rồi chạy designer pass §2–§7 lên trên. **Bỏ Phase D của create-ui**
     (`.claude/docs/ui-designer-pass.md`): §2–§7 ở đây là bản đầy đủ hơn và được đổi layout, chạy cả
     hai là làm hai lần. Cần cả feature (data + logic) → scaffold theo
     `/new-feature` trước, phần UI vẫn đi qua skill này.

## 2. Hiểu tính năng → design brief (trước khi đụng Unity)

**Research** (đọc thật, đừng đoán):
- Controller/service/view của màn + CSV config (`<Feature>/CsvConfig/`) + GDD/TechSpec của feature
  (`GDD/`, `GDD/Final/`, `TechSpec/`) nếu có + localize key đang dùng (`LocalizeHelper.Key` trong prefab).
- Liệt kê **mọi trạng thái** (locked / available / claimed / cooldown / empty / maxed / premium vs free /
  lỗi mạng / chưa mở khoá feature…) và **biên dữ liệu thật** — đếm số phần tử lớn nhất từ CSV, không từ ảnh cũ.
- Chụp ảnh **"before"** (cách chụp: §6) để làm mốc so sánh.
- Đọc luật UI: `.claude/docs/new-ui-guide.md` §0c (spec-sheet; cỡ chữ theo ArtStyle §5), §3b–3e (localize,
  containment, template catalog, layout-group-first), §5 hard checklist; skill `create-ui`; cách làm
  bằng MCP trong `.claude/docs/ui-mcp-playbook.md` §0, §3–§5, §8. UI-kit contract: `ui-kit.json` do
  skill `ui-kit` sinh + luật ghép `.claude/ui-kit/ui-kit-usage.json` (kit stale → `ui-kit-sync.py` trước).
- Mốc chất lượng: **chỉ** các màn trong bảng "Đã duyệt" của ArtStyle §8 — `Read` ảnh `approved/` của
  chúng, ưu tiên màn cùng họ §4b. Màn làm lại gần đây (`git log … UI: rework|redesign|restyle`) mà chưa
  vào bảng đó **không phải mốc**: chưa duyệt thì có thể chính là hướng sắp bị loại, lấy làm mẫu là nhân
  lỗi sang màn sau. §8 trống → mốc là board kit §3 + chính bản đang ship, bản mới phải vượt rõ nó.
  Màn port từ project donor (CLAUDE.md § Project nguồn) → mở bản gốc bên donor để hiểu hành vi
  (read-only, không copy asset về).
- Prefab/child template của màn có được **prefab khác dùng lại** không (item element, popup con) —
  đổi nó là đổi cả màn kia (§0 luật đông cứng), phải chụp kiểm cả hai.
- Nguyên liệu hình: `Read` các board của ArtStyle (`.claude/docs/ArtStyle/*.png`) trước, rồi dựng
  **contact sheet** (Pillow ghép thumbnail) các sprite của kit + FX dùng chung khai ở ArtStyle §3,
  template prefab và art feature (`<Feature>/Visuals/`), `Read` ảnh đó để chọn — nhìn thấy rồi mới
  chọn, không đoán theo tên file.

**Design brief** (ghi vào scratchpad, 10–20 dòng — đây là "vision", tự quyết):
- **Họ màn**: màn thuộc họ nào trong ArtStyle §4b (hay không thuộc họ nào) → ghi phần khoá + phần
  được đổi. Mọi quyết định dưới đây chỉ chạm phần được đổi.
- **Concept / ẩn dụ** gắn với tính năng — lấy từ nguồn mà **luật concept ArtStyle §1** cho phép, không
  thuộc danh sách cấm của nó; đọc thêm §8 màn mẫu rồi art sẵn có. Màn không phải "một list trong
  khung". Họ màn khoá → concept chỉ là phần được đổi (thường là hero + họ màu). Concept tả bằng lời
  trung tính — **không** "giống game X" (rule `no-external-game-refs`).
- **Focal point** duy nhất (phần thưởng lớn / CTA chính / hero) — mọi thứ khác lùi lại.
- **Phân cấp**: 1 CTA chính nổi nhất, phụ mờ hơn; mọi trạng thái của màn (§2 research) thể hiện theo
  **ngôn ngữ trạng thái ArtStyle §4c** — trạng thái chưa có dòng ở đó → đề xuất vào §4c, không tự chế
  riêng cho màn.
- **Palette** 3–5 màu, **chỉ chọn token trong ArtStyle §2** (kể cả họ recolor của nó) — không đặt hex
  mới; thiếu thì thêm token vào ArtStyle kèm lý do. Độ tương phản chữ đủ đọc trên nền;
  **typography** theo bảng chữ + khung cỡ ArtStyle §5.
- **Kiểm hướng đã loại**: đối chiếu concept + palette + bố cục với ArtStyle §9 (cả ảnh `rejected/`) và
  "Từ khoá KHÔNG" §1 — trùng dòng nào thì đổi ngay trong brief, trước khi dựng.
- **Layout**: Popup vs FullScreen (theo §4/§4b), container nào nhận chiều cao dư ở 20:9/21:9,
  grid/row tính số thực (`N × cell + (N−1) × spacing ≤ usable width`) cho **worst case**, khoảng cách
  theo thang ArtStyle §4.
- **Motion language** (§5): easing + nhịp lấy từ ArtStyle §7, beat nào là "khoảnh khắc sướng" của màn.

Sau brief là **spec-sheet** new-ui-guide §0c (bắt buộc, số cho từng node) — autonomous nên không chờ duyệt.

## 3. Art

Thứ tự ưu tiên — **tái dụng trước, vẽ sau**:
1. Kit UI khai ở ArtStyle §3 (frame, button, X, ribbon, header, tiến độ, FX dùng chung) + template
   prefab (`uiTemplatesRoot` — title pill, scroll, tab, money bar, effect) + sprite có sẵn của feature
   (`<Feature>/Visuals/`).
2. **Sprite FX procedural bằng Pillow** (`python3`): glow, rays, ring/halo, shine sweep, sparkle,
   vignette, gradient nền, ribbon/stamp đơn giản. Vẽ **trắng + alpha** (tint trong Unity), supersample
   4× rồi thu nhỏ LANCZOS, cạnh mềm, không viền răng cưa. Lưu `<Feature>/Visuals/<prefix>_fx_<name>.png`
   (một prefix cho cả màn), art lớn `<prefix>_<tên>.png`. Import: Sprite (2D and UI) Single, tắt
   mipmap, alpha is transparency, Clamp, Bilinear, mesh FullRect, tắt fallback physics shape, max
   size 512 cho FX / 2048 cho nền; 9-slice (`spriteBorder`) cho khung co giãn. Chép `.meta` từ một
   sprite FX dùng chung (ArtStyle §3) là cách nhanh nhất để đúng chuẩn (đổi GUID).
3. Emblem / hero art cần vẽ tay → công cụ sinh ảnh AI nếu máy có (command `/gen-icon` →
   `generate_image`, hoặc MCP sinh ảnh của studio nếu đã authenticate; luôn **mở ảnh ra xem** trước
   khi dùng — tool có thể lặng lẽ trả hình vẽ bằng code), không có thì ghép layer từ sprite sẵn có (art feature,
   kit + nguồn ghép layer dự phòng khai ở ArtStyle §3) + Pillow. Không để ô trống / placeholder trong bản final.

**House style cho mọi art mới** (FX, emblem, hero AI, recolor): theo ArtStyle §6 — prompt nền, ảnh
tham chiếu đính kèm và luật kỹ thuật ở đó; recolor kit có sẵn theo họ màu ArtStyle cho phép. Đặt art
mới cạnh board kit: lộ ra là "một họ art khác" thì sinh lại.

**Material trên UI (URP)** — project chạy URP thì:
- Graphic UI (Image/RawImage) **không** dùng shader `Universal Render Pipeline/Particles/*`: shader
  đó đọc `_BaseMap`, UI bind sprite vào `_MainTex` → ra mảng trắng phẳng. Glow additive trên UI dùng
  shader `UI/Additive` (`Visual/ArtAsset/Shared/Shaders/UIAdditive.shader`) hoặc material đã convert
  sẵn (`_mat_add.mat` / `_mat_additive.mat`); không cần additive thì để material mặc định (null).
- Material lấy từ chỗ khác về kiểm blend: Additive `Src=5 Dst=1 ZW=0`, AlphaBlended `Src=5 Dst=10
  ZW=0`. `_SrcBlend 1 / _DstBlend 0` = opaque → glow thành khối trắng đục.

## 4. Dựng prefab

Theo new-ui-guide + ui-mcp-playbook + `create-ui` — không tự chế phần tài liệu đã quy định: template
instance cho mọi khung/scroll/tab, layout-group-first, containment, localize component cho mọi label
tĩnh (§7), neo đúng mép cho đa tỷ lệ, nền full-bleed, không `SetActive()` để mở/đóng màn (qua
`UIManager`), không Screen Space – Overlay, pivot tâm.

**REFACTOR mode** — giữ những gì bên ngoài phụ thuộc:
- Liệt kê serialized reference của controller + tên node được `Find`/tham chiếu trong code trước khi
  tái cấu trúc; dời/đổi node thì nối lại reference và chạy playbook §8
  (`unity_search_missing_references` = 0 — trừ các GUID sprite đã treo sẵn từ project donor mà
  CLAUDE.md liệt kê, đừng "sửa" chúng).
- Tìm chỗ khác dùng prefab/child template (item template, popup con) trước khi đổi cấu trúc chúng.
- Dọn node mồ côi của bản cũ (ẩn không dùng, sprite cũ không còn ai tham chiếu) — bản final không mang rác.
- **Không đụng `.meta` của prefab** (mất `assetBundleName` như `features__shop` → `UIManager` không
  load được từ AssetBundle).

**Bẫy đã gặp thật** (kiểm trước khi mất nhiều vòng chụp):
- Node active, rect/màu/alpha đúng mà không hiện → kiểm `localPosition.z` (di sản bản cũ, vd −5760)
  trên canvas Screen Space – Camera; quét cây và đặt z = 0. Hiện thành khối trắng → material URP (§3).
- `SetParent` con của một nested prefab instance trong `LoadPrefabContents` lặng lẽ không có tác dụng
  → ẩn node cũ, tạo node mới ở chỗ cần, nối lại reference.
- Field template có thể trỏ thẳng **prefab asset** chứ không phải child → sửa trên chính asset đó
  (nếu asset đó là template dùng chung → §0: tách bản riêng cho màn target).
- `CanvasGroup` alpha trên container chứa item dùng SoftMask làm icon **biến mất** thay vì mờ →
  dim bằng Image tối phủ lên; hiệu ứng vào dùng scale/slide.
- Remap reference qua `SerializedObject` đụng `Transform.m_Children` → crash khi save prefab; chỉ remap
  field của MonoBehaviour.
- Capture phải trả canvas về **đúng trạng thái serialized ban đầu** — không để lọt override
  `m_RenderMode` vào prefab (`grep -n "propertyPath: m_RenderMode" <prefab>` phải rỗng; getter
  `Canvas.renderMode` nói dối khi `m_Camera` null — playbook §5).
- Chữ sáng trên nền vàng/sáng thiếu tương phản → đặt plate tối dưới chữ, đừng hạ độ sáng cả khung.
- Pivot đáy trên button làm nút đè lên nhãn → giữ pivot tâm.
- Chuỗi tiếng Việt / Unicode gõ trong `unity_execute_code` bị đổi thành `?` → đọc text từ file
  (CSV localize) trong code C#, hoặc sửa YAML prefab bằng escape `\uXXXX`.
- Build prefab bằng `unity_execute_code`: `PrefabUtility.LoadPrefabContents` → sửa →
  `SaveAsPrefabAsset` → `UnloadPrefabContents` trong `try/finally`, làm theo từng chặng và chụp
  sau mỗi chặng. Script mới chưa compile xong mà cần gắn field → chờ compile rồi mới wire.

## 5. Motion — "smooth" nghĩa là gì

Mỗi màn có đủ 4 lớp, cùng một "cảm giác tay". **Easing, thời lượng, biên độ của từng lớp lấy từ
ArtStyle §7** — bảng dưới chỉ là cấu trúc, không phải giá trị:

| Lớp | Nội dung |
|---|---|
| **Vào** | nền → khung chính → phần tử con stagger |
| **Idle** | chỉ trên focal point (rays / glow / shine / float…), biên độ nhỏ, không giật mắt |
| **Feedback** | nhấn nút (press có sẵn của button template), claim/purchase: punch / stamp / ripple / sparkle, số đếm tăng dần, đổi trạng thái crossfade — có "khoảnh khắc sướng" rõ |
| **Ra / đổi tab** | nhanh, gọn |

§7 thiếu giá trị cho lớp nào → chọn, ghi vào §7 (`Status: draft`) rồi mới dùng.

Dùng thứ project đã có trước, viết mới sau:
- **Vào/Ra của khung chính đã có sẵn**: `FeatureBaseController.AnimOpenUI/AnimCloseUI` (virtual) chạy
  `UITransition.PlayOpen/PlayClose` khi prefab có component đó (fade nền + scale `MainUI`, hoặc
  `TransitionAnimationObject` gán trên prefab), `OnComplete` mở lại touch. Popup → pop-in; FullScreen
  → fade `CanvasGroup` vì `MainUI` để trống (new-ui-guide §0 Layout mode → `MainUI`, đừng wire nó).
  **Không** viết intro thứ hai tween cùng root — đổi cảm giác thì chỉnh `TransitionAnimationObject` /
  tham số `UITransition`; stagger phần tử con thì `override AnimOpenUI()`, gọi `base.AnimOpenUI()`
  rồi mới chạy sequence của màn.
- **Idle**: gắn component `Features/_Shared/UI/Fx/` — `UIFxPulse`, `UIFxSpin`, `UIFxFloat`, `UIFxFade`,
  `UIFxShineSweep`, `UIFxSparkle`, `UIFxWobble`, `UIFxPopIn` (tự chạy OnEnable, tự kill + trả trạng
  thái gốc OnDisable, `_randomDelay` lệch pha). Nhiều effect trên một object được nếu đụng property
  khác nhau; hai effect cùng tween scale sẽ giành nhau.
- Beat feedback riêng của màn → static class `<Feature>Motion` (PopIn / Punch / Stamp / Ripple) trong
  `<Feature>/Scripts/Controller/`; thiếu mode idle chung thì thêm vào `_Shared/UI/Fx/` (kế thừa
  `UIFxBase`) thay vì đẻ bản riêng trong feature.

Luật kỹ thuật (bắt buộc):
- **DOTween**, `SetUpdate(true)` cho mọi tween UI, `SetLink(gameObject)` (hoặc `SetTarget(this)`),
  kill ở `OnDisable` và ở đầu intro; `Sequence` cho mỗi beat; hằng số duration/ease gom một chỗ
  (`const`, không magic number); region + comment tiếng Việt theo rule `code-style`.
- Async dùng `UniTask`, không coroutine, không `async void` (trừ handler), không `Update()` tự chạy
  timer, không alloc mỗi frame; thời gian qua `TimeManager`, không `DateTime.Now`.
- Mở lại màn phải **idempotent**: reset trạng thái tween trước khi phát lại, không cộng dồn scale;
  `DOKill(true)` trước khi punch đọc scale gốc.
- Tween trong layout group chỉ đụng `localScale`/alpha/rotation (vị trí bị layout ghi đè); muốn đổi
  kích thước thì tween `LayoutElement.preferredHeight/Width`, không tween `sizeDelta`.
- **Không scale root của button**: button template mang `JumpInJumpOut` + `DOTweenAnimation` chụp
  `localScale` gốc để làm press effect → tách một child "visual root" (Card) để animate, hoặc slide + alpha.
- Beat claim/purchase: khóa touch bằng `UIManager.EnableTouch(false)` trong lúc beat chạy,
  `EnableTouch(true)` trong `finally`; gắn CancellationToken hủy theo vòng đời view. Phần thưởng vẫn
  được cấp **đúng một lần** qua `RewardsService`/`PurchaseManager` dù view bị đóng giữa beat —
  animation không bao giờ được là điều kiện để grant chạy (hay chạy hai lần).
- FullScreen: tuân luật `MainUI` của new-ui-guide §0 Layout mode (không để màn full-screen "pop").

## 6. Verify — không có ảnh là chưa xong

- **Chụp edit-mode trên URP**: canvas Screen-Space **không** render qua `cam.Render()` (ra ảnh trắng
  trơn) → dựng preview scene (`EditorSceneManager.NewPreviewScene`), instantiate màn nền + màn cần
  chụp, **đổi canvas của instance preview sang World Space** kích thước W×H (scale 0.01), camera
  ortho `size = H/200` (1920 → 9.6) tại `z = −10`, render vào RenderTexture W×H, `ReadPixels` → PNG.
  Đây là cùng cách project dùng để pixel-diff UI (CLAUDE.md § Cách nghiệm thu). Chỉ đổi canvas trên
  **instance preview**, không bao giờ trên asset. Nạp dữ liệu thật (gọi hàm init/refresh bằng
  reflection nếu cần), `ForceRebuildLayoutImmediate` vài vòng trước khi render.
- Play mode: `unity_screenshot_game` ở Game view pin 1080×1920 (playbook §0).
- Text: `LocalizeHelper` không chạy ở edit mode (return khi `!isPlaying`) → gán text thật đọc từ
  `<localize.csvRoot>/<lang>/<tab>.csv` (`key~value`) bằng code đọc file, **không** gõ Unicode trong
  code MCP; chụp cả **VI và EN** để bắt tràn chữ (nên thêm một ngôn ngữ chữ dài như DE).
- **Mọi trạng thái** ở §2 (ít nhất: mặc định, đã nhận/khóa, CTA chính) + **frame giữa animation**
  (DOTween manual update theo bước để callback chạy thật — xem bẫy editor dùng chung dưới đây).
- SoftMask làm mất icon trong ảnh RenderTexture → tắt tạm SoftMask/SoftMaskable **trên instance
  preview** (không phải asset) trước khi render. Popup tự bật đè lên màn ở play mode → đóng trước khi chụp.
- **Aspect sweep** — bắt buộc, đây là chỗ bản rework trước bị chê: tối thiểu 1080×1920 (9:16),
  1125×2436 (19.5:9, safe area sim), 1080×2400 (20:9), 1080×2520 (21:9), 1536×2048 (3:4 iPad) — nên
  thêm 10:16 và 2:3 tablet. CanvasScaler Expand nên canvas không bao giờ nhỏ hơn 1080×1920: lỗi là
  **phần dư rơi sai chỗ**. Cách sửa đã chứng minh: chia chiều cao dư bằng spacer flex **có trọng số**
  (không dồn hết vào một khối), nền kéo tràn cả vùng safe area đáy, nội dung giữ bề rộng 1080 cố định
  ở giữa trên tablet, popup giữ bề rộng cố định neo tâm. Quét xong pin lại 1080×1920.
- **Tự phản biện ít nhất 2 vòng**: mỗi lần chụp, chấm theo checklist dưới, sửa chỗ yếu nhất, chụp lại.
  Hỏi thẳng: "Designer giỏi nhìn màn này có thấy nghiệp dư chỗ nào không?"
  - focal point rõ, CTA chính nổi nhất; căn lề/khoảng cách đều (layout group, thang ArtStyle §4);
  - không lỗ hổng, không chồng lấn, không chữ tràn/cắt, cỡ chữ trong bảng + khung ArtStyle §5;
  - trạng thái phân biệt ngay (ArtStyle §4c); nền phủ kín mọi tỷ lệ; art sắc nét, không vỡ/giãn méo,
    không khối trắng;
  - motion có nhịp (ArtStyle §7), không giật, không chờ lâu mới bấm được.
- **ArtStyle gate** (bắt buộc, trước reviewer): ghép ảnh after (tỷ lệ thiết kế) cạnh ảnh §8 `approved/`
  cùng họ và ảnh §9 `rejected/` của chính màn này (nếu có) thành một PNG so sánh, `Read` nó, rồi chấm
  **từng dòng ArtStyle §11** — mỗi dòng ghi pass/fail + bằng chứng (node, sprite, token, ảnh) vào
  `artstyle_gate.md` trong scratchpad. Dòng mức `block` fail → sửa, chụp lại, chấm lại. Không được
  "pass" một dòng mà không chỉ ra bằng chứng. §11 trống / `Status: template` → ghi rõ gate bị bỏ qua vì
  sao (report phải nói).
- Cuối cùng spawn `ui-visual-reviewer` (độc lập, tự chụp); `block` → sửa, tối đa 2 vòng. Prompt cho
  reviewer **phải** có đủ:
  - `mode: refactor`, `phase: C`, `port`, `targetPath` = path prefab asset (reviewer tự dựng preview,
    không sửa asset) + công thức chụp đa tỷ lệ của §6;
  - `groundTruth` = ArtStyle (§8 ảnh `approved/` cùng họ, §9 + ảnh `rejected/`, §11) — **không** đưa
    design brief hay màn chưa duyệt làm groundTruth; brief + `artstyle_gate.md` chỉ đi kèm dưới nhãn
    `builderClaims` (reviewer kiểm lại, không tin);
  - ảnh before (mốc so) + họ màn §4b + danh sách trạng thái cần chụp + lời chê của dev (nếu task có).
  Sau đó chạy new-ui-guide §5 hard checklist + playbook §8.
- `.cs` đã sửa → compile check theo rule `compile-validation` (`/compile-check`); Editor không kết nối
  → skip êm như rule quy định. Editor đang bị session khác giữ play → compile headless (Roslyn đi kèm
  Unity, từ csproj — thêm cả file `.cs` mới chưa track), **DLL output để ở scratchpad**, tuyệt đối
  không trong project.
- Có Unity MCP → smoke trong play mode: mở màn bằng `UIManager.Instance.Show(...)`, chạy thử CTA
  chính / flow claim. Flow làm đổi dữ liệu người chơi → snapshot dữ liệu (JSON của module
  `PlayerDataManager.<Module>`) trước và trả lại sau, không để save thật của dev bị lệch.
- Diff code đáng kể → spawn `code-reviewer` lên diff, thêm `performance-reviewer` khi có code motion /
  list động (alloc mỗi frame, layout rebuild); sửa mọi finding `block`/`warn` hợp lý.

**Editor dùng chung nhiều session** (thường gặp): `unity_agents_list` trước khi vào/thoát Play — session
khác đang dùng thì nhắn qua agent log, đừng stop hộ. Play mode có thể bị bật/tắt giữa hai lệnh → gói
smoke/capture trong **một** `unity_execute_code`; kiểm `Application.isPlaying` trước khi `Destroy`;
**không** `DOTween.KillAll()` (giết tween của session khác) — chỉ kill tween của mình; lưu và trả lại
`DOTween.timeScale` / `defaultUpdateType` trong `finally`; không `Assets/Refresh` khi session khác đang
play (import từng path bằng `ImportAsset`); lỗi compile ở file không thuộc session → chờ, không sửa hộ.
Console spam `TLS Allocator ALLOC_TEMP_TLS ... unfreed allocations` là lỗi môi trường (CLAUDE.md §
Gotchas — Metal Toolchain / shader keyword), không phải bug của UI vừa làm.

## 7. Localize

- Component localize của project là **`LocalizeHelper`** (`Key` = key lowercase **không** `#`,
  `Category` = tab CSV thuộc enum `LocalizeCategory` — `Common`, `Shop`, `Settings`, `Quest`…).
  new-ui-guide §3b gọi nó là `LocalizesUI` — cùng vai trò, cùng luật: label **tĩnh** bắt buộc có,
  label **động** (số, giá IAP, countdown, tên) tuyệt đối không.
- Key: tái dụng trước (grep `<localize.csvRoot>/en/common.csv` + tab của feature), tạo mới sau theo
  family `<featurename>_<element>`, title = `<featurename>_title`. Key mới đi qua skill **`/add-localize`**
  (sheet + CSV + `.asset`, EN + VI viết tay). **Không** bấm Download của localize downloader để "sync"
  — nó xoá mọi key không có trên sheet.
- File localize dùng chung nhiều session → **chỉ commit dòng của mình** (blob =
  `git show HEAD:<file>` + đúng các dòng key mới → `git hash-object -w --stdin` →
  `git update-index --cacheinfo 100644,<sha>,<path>`), không full-sync bảng sau khi thêm key.

## 8. Kết thúc

0. Ghi `TechSpec/<Screen>-UIRefactor.md` ngắn (concept, bảng node chính, art đã tạo, motion beat +
   hằng số, luật logic giữ nguyên) để lần chỉnh sau không phải đoán lại. Cập nhật `ArtStyle.md`:
   token/kit/kiểu art mới vừa tạo → §2/§3/§6; giá trị motion/trạng thái mới → §7/§4c; chép ảnh after
   (tỷ lệ thiết kế, thu rộng ~540 px) vào `.claude/docs/ArtStyle/screens/pending/<Màn>.png` và thêm
   dòng vào bảng "Chờ duyệt" của §8 (ảnh cũ cùng màn ở `pending/` bị thay thế). **Không** tự đưa màn
   vào bảng "Đã duyệt". Dev chê ở lượt sau → hướng đó vào §9, ảnh sang `rejected/` (rule `art-style`).
1. Report theo rule `output-format`: chỉ danh sách file đã đổi, mỗi file một dòng mô tả (link
   `file:///…` tuyệt đối). Thêm đúng 1 dòng trỏ ảnh before/after (đường dẫn scratchpad) nếu có.
2. **`/auto-clear * UI:`** (REFACTOR) hoặc **`/auto-clear + UI:`** (CREATE) — theo skill
   [`auto-clear`](../auto-clear/SKILL.md): `/push-in-session` với subject dạng
   `rework <màn> as <concept>` (commit body: art/localize/bug đã sửa), chỉ file của session này.
   Loại khỏi commit: file session khác lỡ bị cuốn vào stage (`git restore --staged`), scene/SO do
   editor tool tự sync sau `Assets/Refresh` (vd `BattleScene.unity` + waypoint SO — CLAUDE.md §
   Gotchas). Push xong mới bật cờ. Push lỗi / compile fail / còn block → **không** clear, báo lỗi
   nguyên văn.
3. Project chưa cài `auto-clear` → chạy `/push-in-session * UI:` (nếu có) rồi nhắc dev tự `/clear`.
4. Dưới `/goal`: chỉ push + bật cờ khi mọi bước trên đã xong trọn vẹn.
