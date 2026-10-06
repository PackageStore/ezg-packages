# Changelog — ezg-packages

Tổng hợp thay đổi của cả repo. Chi tiết xem changelog riêng ở `templates/unity-project/` và `packages/<tên-package>/`.

Định dạng mục: **Added** / **Changed** / **Fixed**, mới nhất ở trên cùng.

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
