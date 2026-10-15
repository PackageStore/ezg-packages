# Changelog — ezg-packages

Tổng hợp thay đổi của cả repo. Chi tiết xem changelog riêng ở `templates/unity-project/` và `packages/<tên-package>/`.

Định dạng mục: **Added** / **Changed** / **Fixed**, mới nhất ở trên cùng.

## 2026-10-15

**Changed**
- `/auto-test-device` 1.1: một lệnh `start` lo hết preflight (BugHub, Pillow, probe, chọn máy, cài app). Project có `com.ezg.cheat-gate`: máy nằm trong danh sách máy test tự mở cheat + probe ngay trên bản release, không cần tap version; máy chưa có tên thì skill in Device ID và hỏi một câu để thêm bằng `test_device_add`.

**Added**
- Skill `/auto-test-device <device id> <tính năng>` vào DefaultSetup: agent QA chơi game trên Android (adb/emulator), iOS Simulator, iPhone hoặc Unity Editor như người chơi thật, test visual + logic theo charter, bắt crash/ANR/exception từ log, rồi tự log bug lên BugHub đúng contract Bug Logger (ảnh + log + bước tái hiện, chống trùng). Kèm EZG QA Probe (cài vào `Assets/EzgQaProbe/`) cho cây UI, audit visual tự động, đọc state và gọi cheat.

## 2026-10-14

**Added**
- `com.ezg.cheat-gate` 0.1: bản release chỉ mở cheat cho máy test đã được Lead thêm qua `test_device_add`. Máy được cấp quyền giữ grant ký RSA, dùng offline 7 ngày; người chơi thường không gửi request nào. Base features của template chuyển từ whitelist Remote Config sang CheatGate.
- `com.ezg.buglogger` 0.2.3: Bug Logger (BugHub client + UI) chuyển từ bản embedded của sm007 lên registry và ship sẵn trong template.
- `/fix-bug --watch` + `bughub-setup` vào DefaultSetup: bot tự nhận bug BugHub, sửa trên nhánh bot rồi merge về base.
  - mở Unity Editor trước khi claim, không push fix chưa compile
  - tự merge conflict với base, gặp lỗi API/server thì 30s retry một lần
  - watchdog khởi động lại claude nếu treo 20 phút; session mới nhận lại bug đang làm dở
  - resolve kèm cause/fix/qa_steps; đọc được bug nhập từ thread Discord ("Đưa vào BugHub")
- Skill `create-vfx` (flipbook VFX vẽ bằng code) và `create-sfx` (SFX tổng hợp bằng numpy/scipy, tự QA loudness, gắn sẵn vào SoundConfig/SoundPlayController) ship trong DefaultSetup. ArtStyle thêm mục §6b VFX và §6c SFX.
- `/new-ui` Phase D: phủ art, FX và motion lên layout mockup đã chốt mà không xê dịch layout; nếu lỗi thì quay về Phase C.
- `game-docs` vào DefaultSetup: đọc/sửa Google Sheet tài liệu game qua EZG MCP, có dry-run, undo và xoá dòng.
- Rule `tracking-attribution` quy định thứ tự boot AppsFlyer → GA và luật event/ad revenue. `validate-release` báo lỗi khi GameAnalytics thiếu game key cho platform build.
- Backlog loop tự hồi phục: task bị kẹt được chạy lại tối đa 3 lần rồi park. Loop chờ hết usage limit, retry lỗi API và resume được khi bị ngắt.

**Changed**
- `CLAUDE.md` tách đôi: phần chung do template quản lý (project không sửa, update sẽ ghi đè) và `CLAUDE_PROJECT.md` riêng từng game, được import ở cuối.
- `add-localize` và `game-docs` ghi sheet qua EZG MCP bằng service account chung, không cần file key nữa. EzgKit 1.0.1 bỏ ô chọn key, chỉ hiện email cần share sheet. `add-localize` chạy batch bao nhiêu key cũng không bị timeout.
- Backlog loop nạp ít hơn ~14K token mỗi turn, hành vi giữ nguyên.
- `game-vfx` 0.2.3–0.2.5: thêm motion hạt cho hiệu ứng va chạm và khuôn prefab root → containers → lớp. Tạo VFX mới giờ mặc định đi thẳng vào flipbook generator.
- `figma-components`: State được tính là một trục; set có nhiều trục báo lỗi validate.
- auto-clear thêm `--then` để gõ tiếp prompt sau /clear; ui-kit-sync quét đệ quy; preflight cho khai báo file được phép dùng PlayerPrefs.

**Fixed**
- `com.ezg.tracking` 0.3.0: event AppsFlyer bị mất khi Firebase init chậm hoặc lỗi; conversion data về trước Firebase giờ được giữ lại.
- `com.ezg.ads` 0.3.1: interstitial không truyền placement cho MAX nên report revenue bị trống placement; banner bị gán nhầm placement của quảng cáo fullscreen.
- `com.ezg.iap` 0.3.4: doanh thu IAP iOS (StoreKit 2) không lên Firebase/GA4.
- `com.ezg.cheat-gate` 0.1.1: code game trong namespace `Ezg.*` gọi CheatGate thì không compile được.
- figma-bridge 0.8.5–0.8.13:
  - variant đổi màu qua variable mode bị mất màu
  - component nằm trên trang screens không được nhận là screen
  - auto layout first-on-top bị vẽ ngược, image FILL không được crop, text stroke/shadow bị lệch
  - import hỏng khi REST bị rớt stream (curl 92) hoặc node bị xoá giữa lúc tải
  - trang screens/components không khớp khi tên khác hoa thường hoặc có khoảng trắng
  - frame HUG không co theo nội dung và tính thiếu stroke
- Template: base features mới upload đè key cũ trên R2 nên build project mới lỗi SHA-256; giờ base features mới nằm ở key riêng.
- `add-localize` viết lại cả file CSV, đổi luôn line ending, nên diff ~580 dòng mỗi ngôn ngữ; giờ chỉ patch đúng dòng cần sửa.
- Preflight báo nhầm id game dạng `sk_...` là secret, làm backlog loop park task.
- Windows: bughub-watch crash khi output bị pipe; watch treo khi `python3` là stub của Microsoft Store; hook auto-clear bị project cũ hạ version; watch bị dính session Claude cha.

## 2026-10-06

**Added**
- `/setup-project`: một lối setup duy nhất cho project mới. Skill mở đúng mục EzgKit, in trạng thái từng mục, nhận giá trị dev đưa trong chat rồi chạy dry-run → diff → Apply.
- `com.ezg.autotest` 0.1: hệ thống test tự động gồm static check, smoke PlayMode, UI audit, button sweep, monkey, economy, performance, visual regression và device (adb), xuất report HTML/JSON/JUnit, chạy được từ CLI cho CI.
- `com.ezg.user-segment` 0.1.0: SDK phân khúc người chơi tách từ template (config, evaluator, experiment, cooldown/cap). Giới hạn reward/difficulty khai riêng theo từng game.
- `com.ezg.figma-feature-importer` 0.1.0: tách từ i001, chạy sau bridge để dựng màn Figma thành variant của vỏ template kèm codegen controller. Không còn phụ thuộc type của game hay Odin.
- figma-bridge 0.7–0.8, nguồn **Bridge**: đọc thẳng file Figma đang mở qua plugin EZG Tools, không tốn quota REST và không cần token. Cửa sổ có Source panel chia tab REST / Bridge.
- figma-bridge 0.5–0.6: render 9-slice, variant swap, visual check, frame Clip content thành mask, import chọn lọc theo tick, cache render theo hash, vẽ shape frame tại chỗ, text gradient, texture override ASTC.
- `ArtStyle.md`: mỗi game có một nguồn art direction duy nhất, mọi skill/agent làm hình ảnh đều đọc file này trước khi chọn màu, sprite, bố cục hay sinh art.
- Skill mới: `game-vfx`, `game-animation`, `blender-rig-animate`, `ui-motion`, `refactor-ui`, `figma-build`, `add-localize`, `push-in-session`, `auto-clear`, `restart-unity`.
- Rule `no-external-game-refs`: tài liệu và skill ship ra không nhắc tên game, studio hay nhân vật của bên thứ ba.
- Asset catalog có thêm R001_TalentTree, R001_UIEffect, R001_Feel, UIMotion, GameAnimation, GameVFX.

**Changed**
- EzgKit 1.0.0: UI viết lại toàn bộ bằng UI Toolkit, có thêm mục Thông tin dự án, Ads & Privacy, Gói bán, ArtStyle, Localization. Mọi lần ghi đều qua bảng cũ → mới, có xác nhận và backup.
- Template base features/visuals đã dọn sạch dấu vết game cũ:
  - features 43,5 → 17,2 MB, visuals 138 → 48,6 MB
  - không còn GUID treo
  - ảnh mang nhận diện game cũ được thay bằng placeholder
- 7 skill trong AIFeatures giờ ship sẵn trong DefaultSetup, nên project mới có đủ skill của catalog.
- Figma skills: kiểm tra hygiene gom về một audit engine (`rules.json` + `audit.mjs`). Luật mới: tên chỉ dùng gạch nối, clip content tắt mặc định, popup containment, bán kính góc đồng tâm, canvas gọn, mỗi component set một trục.
- figma-tokens chỉ còn một collection Semantic với giá trị literal, bỏ tầng primitive.
- figma-bridge 0.4.0 bỏ `FigmaImage`, chỉ dùng `Image` thường, và thêm marker `[ignore]` để bỏ qua node.
- Backlog:
  - `execute-backlog-tasks` chạy theo mặc định, không hỏi lại
  - `run-backlog` commit + push theo kiểu push-in-session, title dạng `<prefix> Tag: <subject>` bằng tiếng Anh ngắn
- Republish base R001 cùng 53 feature.

**Fixed**
- `com.ezg.iap` 0.3.3, sửa 4 lỗi:
  - nút Buy kẹt cả session
  - mua thất bại mà không báo
  - store không tự kết nối lại
  - đã trả tiền nhưng không được cấp khi purchase về trước catalog
- EzgKit giờ ghi AppSecrets vào `AppSecretsConfig.asset`. Bản 0.x ghi vào `GameConstant`, chỗ template mới không còn đọc nữa, nên giá trị bị mất và Readiness báo lỗi sai.
- figma-bridge 0.6.7–0.8.4:
  - Sync treo ở "Downloading file" khi có text có stroke
  - shader hoặc paint lạ làm hỏng decode
  - re-import đổi sprite và mất override
  - đổi file Bridge thì import nhầm trang của file cũ
  - batch render đứng ở 0/N
  - báo "Figma file disconnected"
  - layer bị mask mất theo instance "Use as mask"
- `com.ezg.user-segment` 0.0.3: debug overlay ném exception mỗi frame ở project chỉ bật Input System.
- Template:
  - `AddCurrency` cộng Energy 2 lần
  - icon tiền tệ không bao giờ load được
  - simple message không xuống dòng khi text dài
  - ShowingObjectController bị nhấp nháy
- `CLAUDE.md` của template có 9 chỗ lệch so với hệ agent đang ship (sentinel, nhánh base, frontmatter của skill trung tâm).

## 2026-09-10

**Added**
- `com.ezg.figma-bridge` v0.3.2 — import Figma thành prefab UGUI (screen, component, image fill, font), có pattern fill tiled và re-import offline.
- `com.ezg.visual-road-builder` v0.2.1 — tool dựng đường tách từ sm006, thêm block Station 2 và skill Claude đi kèm.
- Tab **AI Feature** (Feature Hub 0.4.0) — project cũ cài lẻ từng skill, không phải dựng lại từ template.
- CI `publish-ai.yml` — push main là tự publish AI catalog + `defaultsetup.tgz`, khỏi cần R2 credentials.
- `/publish-feature` + 51 feature R001 lên tab Features.
- Skill mới: 5 skill pipeline PSD → Figma → Unity, `psd-to-feature`, `publish-unity-package`, `validate-release`.
- EzgKit 0.3.0 — tab Readiness, tab Social, bộ chuyển SDK theo nhà phát hành.

**Changed**
- EzgKit 0.5.0 — tab IAP lấy `ShopService.GetAllProductId()` làm nguồn chuẩn và đối chiếu SKU với store thật; đường cũ bỏ lọt 45/100 product id.
- Pipeline agent bỏ sonnet, opus làm mức sàn — sonnet sai rõ ở vai reviewer/verifier.
- Cửa sổ terminal của backlog loop mang tên `[Project] - [Task]`.
- Stats Overlay mặc định thu gọn còn dòng FPS.

**Fixed**
- `com.ezg.ads` 0.3.0 — cả session chỉ hiện được 1 interstitial; thêm `onFail` luôn được gọi.
- `com.ezg.iap` 0.3.1/0.3.2 — đơn StoreKit 2 bị từ chối như receipt giả, khoá bypass `isTestIAP`, bỏ MiniJson để compile được trên device.
- Feature Hub sinh URL feature lặp tiền tố `unity-template` nên 404.
- figma-bridge — báo nhầm "no glyph", mất sidecar screen bị loại trừ, settings provider crash lúc khởi động.

## 2026-08-22

**Added**
- Bắt đăng nhập Google mới build được, chỉ tài khoản `@easygoing.vn`.
- Đăng nhập lại ngay trong Unity: `Ezg > Đăng nhập EZG`, khỏi phải ra ngoài chạy script.
- `com.ezg.ezgkit` v0.1.0 — cửa sổ `Ezg > EzgKit`, có tab Marketing và tab Firebase.
- `/auto-build-setup` — dựng sẵn nhánh build, biến CI/CD và lịch pipeline trên GitLab.
- Bootstrap có trên `/boot/`, máy còn bản cũ tự curl về được.
- Bước giải nén `.unitypackage` có thanh tiến trình, thấy được đang ghi file nào.

**Changed**
- Phiên 6 tiếng giờ tính theo lúc không dùng — dùng đều thì không bị đá ra (trần 30 ngày).
- Bật/tắt đăng nhập bằng cờ trên server, không phải sửa code; máy dùng bootstrap cũ vẫn build được.
- URL asset chuyển hết sang gateway, token không lọt ra host ngoài.
- Sync version UPM deps vào `unity-template.json`.

**Fixed**
- Giải nén `.unitypackage` nhanh hơn hẳn: 5 package từ ~11 phút còn 2,5 giây, file ra y hệt.
- `--logout` xoá luôn token trong `~/.upmconfig.toml`, hết lỗi 401 khó hiểu.
- `--update-urls` không còn bỏ sót 22/24 file vẫn trỏ địa chỉ chết.
- Khung thông báo đăng nhập hết lệch mép khi có chữ tiếng Việt.
