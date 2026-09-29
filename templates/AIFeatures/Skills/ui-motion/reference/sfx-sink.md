# Sfx UI qua audio manager của project

Mọi component của module bắn sự kiện qua `UIMotionFeedback` (quy chuẩn mục 5). Hai cách phát:

| Cách | Khi nào | Làm gì |
|---|---|---|
| Player kèm sẵn | Project không có audio manager, hoặc muốn tiếng UI riêng | Kéo AudioClip vào dòng sự kiện của role trong RoleMap, chọn mixer group trong Settings. Không code |
| Sink riêng **[DEV]** | Project có audio manager (survey mục Âm thanh) | Một class implement `IUIMotionSfxSink` trong code của project, chọn trong Settings > Sink sfx |

## Viết sink

1. Chép `<module>/Docs/Samples/UIMotionSfxSink.cs.txt` thành một file `.cs` **trong code của project** (cạnh audio manager),
   không trong folder module. Đổi namespace, tên class.
2. Lấp các chỗ TODO bằng lệnh phát của project, theo cách gọi survey tìm ra (ví dụ gọi, kiểu tham số: enum hay class hằng
   chuỗi). Constructor phải rỗng.
3. Map chỉ những sự kiện project ĐÃ có tiếng; sự kiện khác để im — giữ thiết kế âm thanh cũ. Bảng map ghi vào mục Âm thanh
   của `UIMotionProject.md`:

   | Sự kiện UI Motion | Role | Thường map sang |
   |---|---|---|
   | `Click` | Button, Toggle, Tab, Card, Dropdown | tiếng bấm nút |
   | `Show` / `Hide` | Screen, Popup, Sheet | tiếng mở / đóng popup |
   | `Arrive` | Counter (coin bay tới) | tiếng thu tài nguyên |
   | `Error` | InputField, ScreenFx | tiếng lỗi (nếu có) |
   | `Increase` / `Low` / `Complete` | Counter, Bar, Timer | thường để im, trừ khi game có sẵn |

   Key dạng `ui.<role>.<event>` (chữ thường) có trong `request.Key`: audio manager nhận key chuỗi thì truyền thẳng key, hoặc
   ghi Key riêng trên dòng RoleMap.
4. Chọn class: Settings > Sink sfx, hoặc `"settings": { "sfxSinkType": "<namespace>.<Class>" }` trong
   `UIMotionProject.json` rồi áp luật riêng. Có sink riêng thì gate M-7 (clip trong RoleMap) bỏ qua.
5. Haptic / analytics: nghe `UIMotionFeedback.Emitted`, không cần sink.

## Class tự có motion không bắn sự kiện

Class của project khai `ownMotion: true` (screen / popup / nút tự tween) thì module không gắn component lên object đó, nên
**không có sự kiện nào của nó đi qua hub** — sink không nghe được popup đó mở / đóng. Chọn một:

- Giữ tiếng cũ class đó đang tự phát (nếu có), sink chỉ lo object do module gắn.
- Cho class đó tự bắn qua hub: `UIMotionFeedback.Emit(UIRole.Popup, UIMotionEvent.Show, this)` lúc mở, `Hide` lúc đóng (sửa
  code của project, không sửa module), và implement `IUIMotionHideable` nếu muốn `UIMotion.SetActiveAnimated` chạy ẩn của
  nó xong mới tắt. Tiếng lúc đó đi qua sink như mọi role khác.

Ghi lựa chọn vào Quyết định của `UIMotionProject.md`.

## Tiếng kép

Code cũ đã tự phát tiếng (survey: "Code bấm được đang tự phát tiếng"): nút có class đó được khai `ownMotion: true` thì tool
không gắn `UIMotionButton` lên nên không kêu hai lần. Nút KHÔNG khai mà code cũ vẫn kêu (vd `onClick` gọi audio manager) →
sink map `Click` sẽ kêu chồng: hoặc bỏ tiếng ở code cũ, hoặc sink bỏ qua role Button, hoặc để trống sự kiện đó. Ghi lựa chọn
vào Quyết định.

Kiểm: vào Play, bấm nút, mở / đóng popup, nghe đúng một tiếng mỗi lần; Console không có cảnh báo sink.
