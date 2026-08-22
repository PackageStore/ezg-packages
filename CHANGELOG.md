# Changelog — ezg-packages

Tổng hợp thay đổi của cả repo. Chi tiết xem changelog riêng ở `templates/unity-project/` và `packages/<tên-package>/`.

Định dạng mục: **Added** / **Changed** / **Fixed**, mới nhất ở trên cùng.

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
