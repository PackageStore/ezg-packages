# Đọc kết quả khảo sát

`uimotion.mjs survey` đọc file text của project bằng regex: `.cs` (class, lớp cha, hàm, lệnh tween / âm thanh), `.prefab` và
`.unity` (object, component theo GUID script trong `.meta`), `ProjectSettings`, `Packages/manifest.json`. Không biên dịch,
không mở Unity. Kết quả là ứng viên kèm bằng chứng; kết luận ghi vào file riêng của project chỉ sau khi đã kiểm.

## Folder và bên thứ ba

- Prefab / code trong path chứa `/Plugins/`, `/ThirdParty/`, `/3rdParty/`, `/Samples/`, `/Demo/`, `/Examples/`, `/Models/`…,
  hoặc folder gốc tên kiểu SDK (quảng cáo, analytics, Firebase…) coi là bên thứ ba: không tính vào ứng viên, không vào folder
  quét đề xuất. Prefab trong `/Editor/` không tính là UI.
- "Folder quét đề xuất" = folder gốc chứa ~95% prefab UI của project. "Folder trông như pack" = tên folder có pack / download
  / vendor… và ≥ 3 prefab UI — thường là UI pack mua về, nên loại trừ.
- Pack UI mua về nằm lẫn trong folder của project mà tên folder không lộ ra: hỏi user, thêm vào `excludePathContains`.

## Class tự có motion

Ứng viên = class của project (kế thừa MonoBehaviour), được gắn trên object UI (prefab hoặc scene), trong code (hoặc lớp cha)
có tween DOTween lên UI, LeanTween / PrimeTween, Animator, hoặc fade CanvasGroup bằng tay.

| Cột | Đọc thế nào |
|---|---|
| Role | Đoán từ tên class (button, popup, list…) và chỗ gắn: ≥ 60% chỗ nằm cùng Button / Selectable → Button; ≥ 60% ở gốc prefab (hoặc con trực tiếp của Canvas trong scene) → Screen |
| Độ tin | cao = tên và chỗ gắn cùng chỉ một role; vừa = một trong hai; thấp = không đoán được role |
| Ghi chú | "lớp cha … đã phủ": luật đặt ở lớp cha là đủ (luật khớp cả lớp con) |

Trước khi ghi luật, mở class ra xem:

1. Tween có chạy trên **chính object mang component** khi hiện / ẩn / bấm không? Class chỉ tween object con, hiệu ứng phụ
   (sao lấp lánh, coin bay) thì không phải "đã có motion" cho object đó — không ghi `ownMotion`.
2. Class mở / đóng screen (Show / Hide, jump in / out, dim nền) → role Screen hoặc Popup, `ownMotion: true`. Module sẽ
   không gắn Panel / Backdrop lên nó và con trực tiếp (nền, khung).
3. Class nút tự nhún → role Button, `ownMotion: true`. Coi chừng nó cũng tự phát tiếng (mục Âm thanh).
4. Class list tự cho item hiện lần lượt → role List, `ownMotion: true`.
5. Không rõ role mà chắc là có motion → role `Unknown`, `ownMotion: true` (giữ role từ nguồn khác, chỉ chặn gắn chồng).
6. Class là khung / helper dùng khắp nơi (vd component tween chung gắn trên đủ loại object): hỏi user; thường ghi `Unknown`
   + `ownMotion: true`, hoặc bỏ và để module gắn motion của nó.

"Số đang dùng" liệt kê dòng tween có số (thời lượng, scale, ease) trong class đó: dùng để chỉnh Profile cho screen mới giống
cảm giác screen cũ (quy chuẩn 2.1). Đọc cả chỗ số lấy từ asset cấu hình (field / ScriptableObject) chứ không chỉ số viết thẳng.

## Class bấm được chưa có motion

Class implement `IPointerClickHandler` / `IPointerDownHandler`… hoặc `onClick.AddListener`, không kế thừa Button /
Selectable, không có tween. Module không nhận ra role → gate M-10. Ghi role `Button` (nút thật: module gắn nhấn), `Card`
(thẻ chọn), hoặc để user gắn `UIMotionRole` = None kèm lý do cho vùng chạm của gameplay (kéo map, joystick).

## Âm thanh

Audio manager = class có hàm Play…Sfx / PlaySound / PlayOneShot, dùng AudioSource, được gọi từ code khác. Cột "Kiểu tham số"
là enum hoặc class hằng chuỗi mà chỗ gọi truyền vào (tên tiếng của project) — dùng để map trong sink. "Code bấm được đang
tự phát tiếng" = nguy cơ tiếng kép khi gán clip click cho role trong RoleMap.

## Mở / đóng screen

"UI manager (ứng viên)" = file có hàm Show / Close / Open… kèm stack / list screen hoặc Instantiate. Đó là chỗ đổi sang
`UIMotion.SetActiveAnimated`. Số `SetActive(false)` là tổng trong code project — phần lớn không phải đóng screen, chỉ dùng để
biết project hay tắt thẳng tới đâu.

## List, prefab item, đặt tên

- "Từ trong tên prefab được tham chiếu": prefab được field của prefab khác trỏ tới (thường là item, popup sinh lúc chạy). Từ
  lặp ≥ 3 lần kiểu item / cell / view… mà chưa có trong từ nhận prefab item → đề xuất thêm vào `prefabWords.item`. Kiểm
  bằng mắt: `*_view` có thể là item ở project này nhưng là top bar ở project khác.
- "Từ hay gặp chưa khớp keyword": thói quen đặt tên của project. Viết tắt nhận ra được (pnl, dlg, tmr…) được đề xuất; từ khác
  chỉ để tham khảo, đừng thêm keyword cho tên đồ vật của game.

## Bẫy môi trường

- `fps-cap`: game đặt `Application.targetFrameRate` → test Play mode chờ `Time.frameCount` đổi, không tin một lần yield null.
- `editor-hook`: script Editor mở / nạp scene khi vào Play → có thể chặn test Play mode chạy batch.
- `odin-stubs`: hai bộ stub Odin cùng lúc khi không cài Odin → trùng class.
- `data-in-module`: asset của project nằm trong folder module → dời ra trước khi chép bản module mới.
- `no-dotween`: không thấy DOTween → module không compile.

Ghi các bẫy đã gặp thật vào mục Bẫy môi trường của `UIMotionProject.md`, để lần sau khỏi hiểu nhầm là module hỏng.
