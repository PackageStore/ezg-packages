# Quy chuẩn VFX (2D, 3D, mobile)

Phiên bản 0.2.4 (nháp) · 2026-10-07 · Module có thư viện hiệu ứng dùng chung đã chuẩn hoá theo tài liệu này (`GameVFX_ThuVien.md`:
hiệu ứng world và UI canvas, shader chung, component `FXEffect`) và skill Claude `game-vfx` (đọc, áp quy chuẩn, quét project);
gate trong Unity vẫn làm sau. Nguồn chính là hướng dẫn VFX công khai
(PDF, 37 trang, 6 phần) của một studio MOBA PC lớn cho tựa MOBA PC của họ, tài liệu công khai khác của studio đó, của một studio
mobile lớn, một publisher mobile lớn, của Unity, Arm, W3C, Microsoft, và số đo trên sáu game mobile đã ship của team (ba game 2D
ở bản 0.1, ba game 3D thêm ở bản 0.2). Lý do của từng luật và danh sách nguồn: `GameVFX_NguyenLy.md`.

Áp dụng cho VFX trong game Unity 6000.3 (URP hoặc Built-in), mobile trước, cả 2D và 3D: hiệu ứng trong thế giới game (đòn
đánh, kỹ năng, trúng đòn, trạng thái, xuất hiện, chết, môi trường) và hiệu ứng hạt, flipbook, shader trên UI (thưởng, mở
rương, nâng cấp). Không thuộc tài liệu này: chuyển động của element UI (tween, rung và loé màn hình, số sát thương, coin bay)
là của UI Motion (`UIMotion_QuyChuan.md`); animation nhân vật, frame data, hitstop, event là của Game Animation
(`GameAnimation_QuyChuan.md`). Mục 13 nói ba bên nối với nhau ở đâu.

Người đọc: VFX artist, tech art, game design, dev, art lead. Mục có **[VFX]** là việc của VFX artist, **[TA]** của tech art,
**[GD]** của game design, **[DEV]** của dev, **[ART]** của art lead; không ghi thì là việc chung.

**Số trong tài liệu là mặc định để bắt đầu.** Số có nguồn ghi nguồn trong tài liệu nguyên lý (mã [n]). Số ghi *(đề xuất)* là
điểm xuất phát chưa có nguồn chính thức. Số ghi *(đo)* đo trên ba game mobile 2D đã ship của team bằng script quét prefab của skill
`game-vfx` (khảo sát ở tài liệu phát triển `Docs/GameVFX/GameVFX_KhaoSat.md` của repo module, không đi kèm module): chỉ tính
hiệu ứng thật sự vào bản build, bỏ pack. Ba game 3D quét thêm ở bản 0.2 ghi riêng *(đo, 3D)* ở chỗ khác đáng kể. Chưa số nào đo trên máy thật: pilot sẽ
hiệu chỉnh (mục 12.3).

| Bạn là | Đọc trước |
|---|---|
| VFX artist | 0, 2, 3, 4, 5, 6.2, 7.1–7.4, 8, 9, 11 |
| Tech art | 0, 4.4–4.7, 6, 7, 8, 10 |
| Game design | 0, 2.1, 2.4–2.7, 3.1, 3.3, 9.2 |
| Dev | 3.3–3.4, 6.5–6.8, 7.8, 13 |
| Art lead | 0, 2, 5, 9, 12 |
| Người dùng pack VFX mua sẵn | 7.7, 8.3, 6.2 |
| Người lấy hiệu ứng có sẵn | `GameVFX_ThuVien.md`, 7.8 |

---

## 0. Bảy nguyên tắc gốc

1. **Gameplay trước, đẹp sau.** VFX nói đúng bốn điều: chuyện gì xảy ra, ở đâu (vùng, hitbox), khi nào (lúc có tác dụng),
   của ai (mình, đồng minh, địch). Sai một trong bốn là lỗi gameplay, sửa trước mọi việc khác. Studio MOBA PC xếp việc sửa
   VFX theo thứ tự: sửa gameplay, rồi độ dễ đọc, rồi chủ đề [4].
2. **To nhỏ theo tầm quan trọng.** Mỗi hiệu ứng có một cấp (2.1). Đòn thường nhỏ, tối thượng lớn; phần trang trí không được
   át cảnh báo và phản hồi.
3. **Một tiêu điểm.** Mỗi hiệu ứng có đúng một yếu tố chính mang thông tin gameplay; mọi thứ khác là phụ, tối hơn, mờ hơn,
   nhỏ hơn (2.2).
4. **Thứ bậc sáng và đậm.** UI sáng và đậm nhất, rồi VFX, rồi nhân vật, môi trường thấp nhất. VFX không dùng trắng tinh và đen
   tinh (2.3).
5. **Vào nhanh, tan gọn.** Chuẩn bị → bùng → tan; bùng đạt đỉnh ngay, tan dần; xong việc thì rời màn hình (3).
6. **Đọc được trên máy thật.** Duyệt ở camera gameplay, trên màn hình nhỏ nhất đang hỗ trợ, trên máy yếu nhất, lúc đông nhất
   (nhiều hiệu ứng cùng lúc), không chỉ trong scene thử.
7. **Ngân sách là luật.** Mỗi cấp có trần system, hạt, material, texture, thời gian (6.2). Vượt là lỗi, hoặc ghi nợ kèm lý do
   (10).

---

## 1. Thuật ngữ

| Từ | Nghĩa |
|---|---|
| VFX, hiệu ứng | Một prefab hiệu ứng phát như một khối (một cú chém, một quả cầu lửa, một vụ nổ thưởng) |
| System | Một `ParticleSystem`. Một hiệu ứng thường có nhiều system (lõi, loé, tia, khói) |
| Hạt (particle) | Một hình do system sinh ra. "Hạt đỉnh": số hạt sống cùng lúc nhiều nhất của một hiệu ứng |
| Burst | Phát một lượt nhiều hạt ở một thời điểm |
| Duration, lifetime | Thời gian system phát / thời gian sống của một hạt. Hiệu ứng kết thúc khi hạt cuối cùng chết |
| Flipbook (texture sheet) | Texture chứa nhiều khung hình xếp lưới (`4x4`), hoặc danh sách sprite, phát theo thời gian |
| Yếu tố chính / phụ | Phần mang thông tin gameplay, là tiêu điểm / phần tăng chủ đề, hỗ trợ yếu tố chính (2.2) |
| Cấp (tier) | Mức quan trọng của hiệu ứng với gameplay: Ambient, Basic, Support, Damage, Major, Ultimate (2.1) |
| Tier máy | Nhóm máy theo sức mạnh: Thấp, Vừa, Cao (6.1) |
| Chuẩn bị / bùng / tan | Anticipation / impact / dissipation: ba pha của một hiệu ứng (3.1) |
| Linger | Phần còn lại trên màn hình sau khi gameplay đã xong |
| Tâm nóng | Điểm sáng nhất ở giữa yếu tố chính: màu nhạt của chính tông đó, không phải trắng tinh (5.1) |
| Telegraph, indicator | Cảnh báo trước đòn của quái / vùng tác dụng vẽ lên mặt đất (2.4, 2.6) |
| Additive, alpha blend | Hai cách trộn chính: cộng sáng (chỉ làm sáng thêm) / phủ theo alpha (che được, tối được) (5.5) |
| Overdraw | Số lớp trong suốt chồng lên cùng một pixel. Chi phí chính của VFX trên mobile (6.3) |
| Phủ màn hình | Tổng diện tích các hạt trong suốt so với diện tích màn hình. 100 % = một lớp phủ kín màn hình |
| Procedural mode | Chế độ Unity tính trước được trạng thái của system: bỏ qua khi ngoài màn hình (6.5) |
| Pool | Kho object dùng lại, thay cho `Instantiate` / `Destroy` mỗi lần phát (6.8) |
| Scaled / unscaled time | Thời gian theo `Time.timeScale` (tốc độ game, pause) / thời gian thật (3.4) |
| Socket | Điểm gắn trên rig nhân vật (`Socket_Hand_R`), Game Animation 4.4 |
| Key VFX | Tên dùng để gọi hiệu ứng từ code, event, bảng dữ liệu (7.8, 8.2) |

---

## 2. Đọc được **[VFX] [GD]**

### 2.1 Cấp quan trọng

Mỗi hiệu ứng thuộc đúng một cấp, ghi trong brief (9.2). Cấp quyết độ sáng, độ đậm, độ đục, độ rõ của hình, chuyển động, kích
thước, thời gian và ngân sách. Ô in đậm lấy từ trang 11 của tài liệu gốc [1]; ô thường suy ra theo cùng logic; cột kích thước,
thời gian là *(đề xuất)*.

| Cấp | Gồm | Độ đục | Độ đậm (saturation) | Dải sáng (value) | Hình | Chuyển động | Kích thước so với nhân vật | Thời gian (3.1) |
|---|---|---|---|---|---|---|---|---|
| Ambient | hạt nhàn rỗi, môi trường, trang trí idle | **thấp** | thấp | hẹp | **mờ** | **tinh tế** | nhỏ | lặp |
| Basic | đòn thường, đầu nòng, trúng đòn thường, đạn thường | vừa | vừa | vừa | rõ, gọn | nhanh, ngắn | **nhỏ**: ≤ 1× chiều cao | ≤ 0,5 s |
| Support | phòng thủ, buff, hồi máu, khiên, trạng thái | **thấp** | **thấp** | vừa | **mờ** | **tinh tế** | bao quanh nhân vật | vào ≤ 0,5 s rồi lặp nền |
| Damage | kỹ năng gây sát thương | **cao** | **cao** | rộng | **rõ** | mạnh | 1–3× | ≤ 1,5 s |
| Major | thay đổi thế trận: khống chế diện rộng, triệu hồi, chiêu boss | **cao** | **cao** | **rộng** | **rõ** | **mạnh** | theo vùng tác dụng | ≤ 2 s |
| Ultimate | tối thượng, kết liễu, đổi phase boss | **cao nhất** | **cao nhất** | **rộng nhất** | rõ | **ấn tượng** | **lớn**, đúng vùng | ≤ 3 s |

- Ba tiêu chí của tài liệu gốc khi xếp cấp [1]: người xem hiểu ngay hiệu ứng để làm gì (readable); hiệu ứng quan trọng kéo mắt, bớt
  nhiễu lúc đánh nhau đông (emphasis); độ lớn khớp tầm quan trọng (scale). Đòn thường mà to như tối thượng thì người chơi
  rối và tối thượng mất sướng.
- Tầm quan trọng tăng theo sát thương, độ mạnh khống chế, việc có né được không, và ảnh hưởng tới thế trận [2]. GD xếp cấp,
  VFX không tự nâng.
- UI dùng cùng cách nghĩ cho hiệu ứng thưởng: đồ thường → Basic, hiếm → Damage, huyền thoại → Ultimate. Độ hiếm đọc bằng màu
  (5.3) và bằng quy mô, không chỉ bằng màu.
- Không dùng hết thang trong mọi hiệu ứng: game có ít Ultimate thì Ultimate mới nổi.

### 2.2 Yếu tố chính, yếu tố phụ

| | Yếu tố chính | Yếu tố phụ |
|---|---|---|
| Vai trò | tiêu điểm, mục đích chính của chiêu, nói gameplay rõ và đúng | tăng chủ đề của nhân vật / chiêu, đỡ cho yếu tố chính |
| Dải sáng | cao | thấp hơn |
| Tương phản | mạnh về sáng hoặc về đậm | ít |
| Độ đục | cao | thấp |
| Hình | rõ silhouette, hình khối mạnh | mờ, đơn giản, nhỏ |
| Chuyển động | mạnh | nhẹ |
| Màu | một tông chủ đạo | được nhiều tông hơn, dải đậm rộng hơn (5.1) |

- Ví dụ của tài liệu gốc [1]: chiêu khiên nổ của một tướng đỡ đòn có yếu tố chính là **viền vòng tròn** (người chơi cần biết
  bán kính để né), yếu tố phụ là tia điện mờ bên trong.
- Brief ghi yếu tố chính là gì (9.2). Tắt hết yếu tố phụ, hiệu ứng vẫn phải nói đủ gameplay.
- Trong prefab, yếu tố phụ đặt tên có hậu tố `_sec` (8.4) để tool giảm hoặc tắt ở tier máy thấp và lúc màn hình đông (2.7,
  6.4).

### 2.3 Dải sáng và dải đậm theo mảng

Tài liệu gốc cho biểu đồ dải sáng (value) và dải đậm (saturation) của từng mảng [1]. Số dưới đây đo trên hình biểu đồ (thang
0–100 %), là tỉ lệ để hiểu, không phải số studio đó công bố:

| Mảng | Dải sáng | Dải đậm |
|---|---|---|
| UI | 92–100 % (hẹp, sáng nhất) | 93–100 % (đậm nhất) |
| VFX | 2–95 % (rộng nhất) | 4–95 % (rộng nhất) |
| Nhân vật | 5–58 % | 6–73 % |
| Môi trường | 8–45 % (hẹp, tối vừa) | 9–45 % (thấp nhất) |

Luật:

1. Dải sáng cao kéo mắt; tương phản tạo vùng tác dụng rõ. Hiệu ứng quan trọng dùng phần trên của dải VFX, trên hẳn môi trường
   và nhân vật của game đó.
2. Không trắng tinh, không đen tinh: 100 % và 0 % dễ lẫn với UI hoặc với môi trường [1]. Chỗ sáng nhất là tâm nóng (5.1).
3. Đẩy sáng vào **tâm** của yếu tố chính: hiệu ứng không có tâm thì chìm vào nền, người chơi không biết chỗ nào đau nhất [1].
4. Loé sáng (glow, illumination) diễn tả phép thuật, hướng và thời gian của hiệu ứng [1]. Loé là yếu tố phụ, không che yếu
   tố chính.
5. Đặt nền tối sau hiệu ứng để tăng tương phản được, nhưng dè dặt: lạm dụng thì các hiệu ứng tranh nhau lúc đánh nhau đông [1].
6. Mỗi game đo dải sáng, dải đậm của môi trường và nhân vật của mình (chụp màn hình gameplay, đổi sang đen trắng) và ghi vào
   brief của game (9.2); VFX duyệt theo dải đó.
7. Duyệt bằng ảnh đen trắng: hiệu ứng vẫn đọc được khi bỏ màu, yếu tố chính sáng hơn yếu tố phụ. Lật ảnh ngang để nhìn bố cục
   bằng mắt mới [18].
8. Độ sáng thật trên màn hình là texture × màu hạt × cường độ × bloom. Kiểm dải trên ảnh chụp trong game, không kiểm trên
   start color: ba game của team để start color trắng ở 42–64 % số system, màu nằm trong texture *(đo)*; một game cho phần lớn
   các lớp dùng chung một material HDR ×3 rồi bật bloom 3,6: lớp chính và lớp phụ cháy trắng như nhau, mất thứ bậc *(đo)*.

### 2.4 Vùng tác dụng và hitbox khớp gameplay **[VFX] [GD] [DEV]**

1. Hiệu ứng vùng (AoE) phủ đúng bán kính gameplay: vụ nổ to bằng vùng sát thương, không nhỏ hơn một nửa như cái bẫy của một
   tướng ở bản cũ trong tài liệu gốc; có vòng chỉ vùng nhẹ khi cần [1].
2. Hình hiệu ứng khớp hitbox: chiêu hình chữ nhật vẽ đúng chữ nhật, không rộng hơn thật (người chơi thấy trúng mà không bị
   tính) [1]. Đạn to bằng collider, không gấp đôi.
3. Vòng, vùng trên mặt đất vẽ **nằm trên mặt đất** (3D: render mode Horizontal hoặc mesh phẳng; 2D top-down: layer mặt đất),
   để góc camera không làm lệch vị trí [1].
4. Số đo lấy từ dữ liệu: bán kính, dài, rộng của chiêu nằm trong bảng dữ liệu của GD; prefab vùng scale theo số đó lúc chạy
   (7.8), không vẽ theo mắt.
5. Studio MOBA PC giữ nguyên cỡ indicator qua mọi skin; skin không được rõ kém bản gốc [4][2]. Cùng luật cho mọi biến thể (skin, nâng
   cấp, độ hiếm) của một chiêu: đổi màu và chi tiết phụ, không đổi yếu tố chính.

### 2.5 Màu phe **[GD] [VFX]**

| Phe | Mặc định | Ghi chú |
|---|---|---|
| Mình (người chơi) | vàng / vàng kim | quy ước của chế độ mù màu trong tựa MOBA PC: thanh máu của mình vàng [8] |
| Đồng minh | xanh lơ → xanh dương → tím: `#39EAFA` → `#365DF5` → `#6E1CF4` | đo trên trang 26 của tài liệu gốc [1] |
| Địch | hồng đỏ → đỏ → đỏ cam: `#FA2B73` → `#FA2B35` → `#F9662F` | như trên |

1. Phe đọc bằng màu **và** bằng hình: không bao giờ chỉ bằng màu [34]. Mặc định *(đề xuất)*: cảnh báo của địch có viền cứng,
   vạch hoặc răng cưa hướng vào tâm; vùng của đồng minh viền mềm, không vạch.
2. Cam và xanh dương phân biệt được với cả ba dạng mù màu phổ biến; không dựa vào cặp đỏ / xanh lá [34]. Không dùng filter mù
   màu toàn màn hình [34].
3. Dải màu của địch là dải riêng: hiệu ứng của người chơi không dùng tông đỏ cam đó cho yếu tố chính, để đạn và cảnh báo của
   địch luôn nổi [14]. Một tựa shooter đấu trường trên mobile, một tựa chiến thuật thời gian thực trên mobile và một tựa
   tactical shooter đều giữ đồng minh xanh, địch đỏ [10][11][5].
4. Hiệu ứng có thể khác theo người xem: địch thấy ít hơn, lặp lại thì nhẹ hơn lần đầu [13]; tiếng lặp giảm theo [3].
5. Game chốt hex thật của ba phe trong brief (9.2); mọi hiệu ứng có phe lấy màu từ đó.

### 2.6 Cảnh báo (telegraph) **[GD] [VFX]**

1. Cảnh báo bật cùng lúc quái bắt đầu lấy đà, kéo dài đúng startup của đòn trong bảng frame data, tắt ở frame active đầu tiên.
   Độ dài theo Game Animation 3.3 (đòn quái thường ≥ 500 ms, đòn mạnh 600–1000 ms).
2. Cảnh báo cho thấy **vùng** và **thời gian**: vùng đúng hình hitbox (2.4), thời gian bằng phần lấp đầy chạy từ tâm ra hoặc
   viền co lại (trong tài liệu gốc, vòng tụ lực của một tướng đếm tới lúc choáng đủ tầm [1]).
3. Cảnh báo là yếu tố chính của lúc đó: sáng hơn mọi hiệu ứng phụ quanh nó, cùng dải màu địch (2.5), không bị hiệu ứng của
   người chơi che (sorting, 7.3).
4. Đòn khác nhau thì cảnh báo khác nhau ngay từ frame đầu (hình, không chỉ màu).
5. Trên màn hình cảm ứng, chiêu cần ngắm cần vệt ngắm và cảnh báo rõ hơn trên PC: bản MOBA mobile của cùng studio đổi nhiều
   chiêu chỉ-và-bấm thành chiêu ngắm vì chạm khó chọn mục tiêu [7].

### 2.7 Màn hình đông **[GD] [DEV] [VFX]**

1. Thứ tự ưu tiên khi đông: cảnh báo của địch → phản hồi đòn của người chơi → hiệu ứng của đồng minh → trang trí. Thứ đứng sau
   giảm trước.
2. Mỗi key VFX có trần số bản cùng lúc (mặc định *(đề xuất)*: Basic 6, Damage 3, Major và Ultimate 1–2); vượt thì bỏ bản cũ
   nhất hoặc không phát bản mới, do pool lo (6.8, 7.8).
3. Trên một số hiệu ứng cùng lúc (mặc định *(đề xuất)* 20 hiệu ứng hoặc 300 hạt trên màn hình), tắt yếu tố phụ `_sec` của hiệu
   ứng mới.
4. Trúng liên tiếp cùng một mục tiêu: hiệu ứng lần sau nhỏ và nhẹ hơn lần đầu [13].
5. Thanh trượt độ đục không giải quyết chồng lớp: nhiều lớp trong suốt vẫn phủ gần kín màn hình [14]. Giảm số lớp và diện
   tích, không chỉ giảm alpha.
6. Game nhiều đạn (kiểu survivor): có tuỳ chọn tắt hiệu ứng chớp và số sát thương, như một tựa survivor (bullet heaven) nổi
   tiếng [14].

### 2.8 Chớp sáng, rung, màu: an toàn cho người xem **[VFX] [GD] [DEV]**

1. **Chớp**: không quá 3 lần trong bất kỳ 1 giây nào cho vùng chớp lớn hơn khoảng 10 % màn hình [31]. Một lần chớp là sáng
   tăng rồi giảm ≥ 10 % độ sáng tối đa, bên tối dưới 0,80. Chớp đỏ đậm (R / (R + G + B) ≥ 0,8) tính riêng và cũng ≤ 3 lần
   mỗi giây. WCAG cho vùng ngưỡng khoảng 11 % màn hình 1024 × 768, Xbox 20 %, Game Accessibility Guidelines 25 % [31][32][34];
   điện thoại cầm gần mắt nên lấy mức chặt nhất.
2. Loé toàn màn hình chỉ là một nhịp ngắn (≤ 100 ms, 3 frame ở 30 fps), không lặp dồn; làm bằng `UIMotionScreenFx` (Flash) để
   chung công tắc giảm chuyển động với UI Motion. Kiểu "khung hình va chạm" trắng toàn màn của anime [17] chỉ cho Ultimate, một
   lần, và vẫn trong luật 1.
3. Không có vạch sọc tương phản cao chuyển động phủ trên 25 % màn hình (≥ 5 vạch) [34].
4. Game có tuỳ chọn cho người chơi: rung màn hình 0–100 %, hiệu ứng chớp / toàn màn hình bật tắt [33]. Không đặt tên tuỳ chọn
   là "an toàn cho người động kinh" [34]; gọi đúng thứ nó tắt.
5. Màu: luật 2.5.1–2.5.2.

---

## 3. Timing **[VFX] [GD]**

### 3.1 Ba pha

Mọi hiệu ứng có chuẩn bị và tan [1]. Chuẩn bị có thể nằm trong animation của nhân vật (đòn thường: pose lấy đà là chuẩn bị),
khi đó VFX bắt đầu thẳng bằng pha bùng.

| Cấp | Chuẩn bị | Bùng: tới đỉnh | Tan | Tổng, không lặp |
|---|---|---|---|---|
| Basic: trúng đòn, chém | 0 (animation lo) | 0–2 frame sau frame va chạm | 100–300 ms | ≤ 0,4 s |
| Basic: đầu nòng, tia vung | 0 | 0–1 frame | ≤ 100 ms | ≤ 0,2 s |
| Đạn | 0–100 ms lúc bắn | bay theo gameplay; trúng như trúng đòn | ≤ 300 ms sau khi trúng | theo gameplay |
| Support: vào trạng thái | 100–200 ms | 100–200 ms | nền lặp ≤ 30 % độ đục cho tới khi hết trạng thái; ra ≤ 300 ms | vào ≤ 0,5 s |
| Damage | 150–400 ms (bằng startup GD cho) | 100–200 ms | 200–600 ms | ≤ 1,5 s |
| Major, Ultimate | 300–1000 ms (telegraph, cast) | 150–300 ms | 400–1000 ms | ≤ 3 s |

Cả bảng là *(đề xuất)* (tài liệu gốc không cho số). Tham chiếu *(đo)* trên ba game của team: tổng thời gian của hiệu ứng
không lặp có trung vị 1–1,9 s, 10 % dài nhất 4–10 s; mẫu tiêu biểu: trúng đòn cận chiến 0,22 s, nổ cầu lửa 0,4 s, chết 0,65 s,
xuất hiện 1,4 s (chuẩn bị 0,5 s). Pha bùng bắt đầu đúng frame va chạm (Game Animation 3.7), không trễ hơn, và tới đỉnh trong 0–2 frame (bảng trên).

Luật:

1. Pha tan là yếu tố phụ: sáng, đậm, đục thấp hơn pha bùng [1].
2. Năng lượng tắt dần bằng đổi sáng, tông, đậm, độ đục hoặc kích thước; màu, sáng, đục đổi theo thời gian của hiệu ứng [1].
3. Rời màn hình ngay khi đã nói xong [15]: vừa đỡ rối, vừa đỡ tốn. Thứ còn lại sau khi gameplay đã xong (vết cháy, khói) ≤ 1 s,
   độ đục ≤ 30 % *(đề xuất)*. Vùng còn tác dụng (đất cháy gây sát thương) thì còn đúng bằng thời gian gameplay và là yếu tố
   chính.
4. Ngay ở đỉnh, hiệu ứng lớn vẫn hơi trong để hiệu ứng khác đọc được qua nó [1].
5. Hiệu ứng nhiều giai đoạn (nổ thùng thuốc súng của một tướng trong tài liệu gốc: lấy đà đổi màu đỏ → vàng, nổ chính, tan
   thành khói và mảnh [1])
   thì mỗi giai đoạn đọc được riêng, đỉnh sáng nhất ở giai đoạn có tác dụng.

### 3.2 Nhịp động, không tuyến tính

1. Chuyển động và kích thước đi theo đường ease-out: nhanh lúc đầu, chậm dần về cuối; tuyến tính làm khoảnh khắc nhạt [1].
   Cách dựng bằng module của hạt cho hiệu ứng va chạm: 3.7.
2. Mặc định *(đề xuất)*: kích thước đạt 80–100 % trong 20 % đầu đời hạt; độ đục giữ, rồi tắt dần trong 30–60 % cuối.
3. Hạt bay nhanh có nhoè chuyển động (stretched billboard hoặc hình vẽ sẵn độ nhoè): hạt nhanh mà sắc nét tạo nhiễu và ảo giác
   rớt frame [1].
4. Chuyển động có "vật lý" đúng chất liệu: gió cuộn, than hồng bay lên, giấy rơi theo trọng lực [1].
5. Nhịp lặp: xung chậm hơn khoảng 60 nhịp mỗi phút đọc là êm (hồi máu, buff), nhanh hơn đọc là gấp (nguy hiểm, sắp nổ) [16]
   (thông lệ, không phải số đo).

### 3.3 Khớp với animation, âm thanh, hitstop, camera **[VFX] [DEV]**

1. Hiệu ứng của đòn bật bằng event `AE_Vfx` (Game Animation 7.4) ở đúng frame, vị trí lấy từ socket của rig (`Socket_Hand_R`);
   hiệu ứng trúng đòn bật ở frame active đầu tiên của bảng frame data (Game Animation 3.7), do code gọi khi trúng thật.
2. Tiếng và hình cùng frame; hình không bao giờ đi sau tiếng (Game Animation 7.4 luật 9).
3. Hitstop dừng nhân vật bằng `Animator.speed` (Game Animation 7.5), không dừng particle: hiệu ứng trúng đòn chạy tiếp trong
   lúc hai bên đứng hình, chính nó là cái "bụp" của cú đánh.
4. Rung camera: `AE_Shake` / `UIMotionScreenFx`, mức theo Game Animation 3.4 và trục Impact của mood (3.5). Hiệu ứng không tự
   rung camera. Rung theo mô hình trauma: mỗi cú đánh cộng trauma, rung = trauma² hoặc trauma³, nhiễu Perlin; 3D chỉ xoay, 2D xoay
   và dời [19].
5. Số sát thương, HP tụt, coin bay là UI Motion (`UIDamageTextSpawner`, `UIMotionBar`, `UIMotionFlyTo`).

### 3.4 Giờ game hay giờ thật **[DEV] [TA]**

| Hiệu ứng | Thời gian | Vì sao |
|---|---|---|
| Trong thế giới game (đòn, trạng thái, môi trường) | giờ game: `useUnscaledTime` **tắt** | theo tốc độ x2, x3 và dừng khi pause, khớp với nhân vật và gameplay |
| Trên UI (thưởng, popup, màn pause) | giờ thật: `useUnscaledTime` **bật** | vẫn chạy khi game pause, như tween của UI Motion |

1. Thời gian sống của object hiệu ứng (lúc trả về pool) đo cùng loại giờ với hạt của nó. Không trộn: đếm bằng
   `WaitForSeconds` (giờ game) mà hạt chạy giờ thật thì ở x2, x3 hiệu ứng bị cắt giữa chừng, lúc pause hạt vẫn chạy.
2. Tốt nhất không đếm giờ tay: hiệu ứng tự báo xong bằng stop action (7.2), pool nhận lại lúc đó.
3. Ba game của team có đúng lỗi này *(đo)*: công cụ dựng hiệu ứng của template đặt `useUnscaledTime = true` cho mọi system,
   nên 69–73 % system của hiệu ứng gameplay chạy giờ thật, trong khi thời gian tắt đếm bằng `WaitForSeconds`; game đổi tốc độ
   x1–x3 và pause bằng `Time.timeScale`.

### 3.5 Mood theo loại game

VFX dùng chung 5 trục mood với Profile của UI Motion và Game Animation 2.1, để hiệu ứng, UI và nhân vật của một game cùng cảm
giác. Game đã chọn preset thì VFX theo preset đó.

| Trục | Với VFX |
|---|---|
| Tempo | thời gian ba pha: Tempo cao dùng cận dưới của mỗi dải ở 3.1, thấp dùng cận trên |
| Bounce | độ vọt quá khi hiệu ứng nở ra (pop), nảy của mảnh vỡ |
| Impact | kích thước và độ sáng của hiệu ứng trúng đòn, mức rung (3.3) |
| Liveliness | lượng hiệu ứng môi trường và idle (Ambient), nhịp lặp của buff |
| Softness | hình mềm hay sắc (4.1), đuôi ease dài hay ngắn |

### 3.6 Hiệu ứng lặp

1. Gồm ba phần khi cần: vào (`_Start`), lặp (`_Loop`), ra (`_End`). Lặp khớp đầu cuối, không giật.
2. Lặp ở mức nền: độ đục ≤ 30 %, đậm thấp, chuyển động nhẹ (cấp Support, Ambient), trừ khi trạng thái đó là yếu tố chính của lúc
   đó (sắp nổ).
3. Có người dừng: chủ của trạng thái gọi dừng khi trạng thái hết, hiệu ứng chạy phần ra rồi tự trả pool (6.8). Hiệu ứng lặp
   không ai dừng là rò rỉ.

### 3.7 Motion của hạt: hiệu ứng va chạm **[VFX]**

Phạm vi: mục này viết **chủ yếu cho hiệu ứng va chạm (impact)**: trúng đòn, nổ, va đập, phần "trúng" của đạn và kỹ năng, chết
dạng nổ. Đó là một burst ngắn ở một điểm, bùng rồi tan trong khoảng 1 s. Loại khác (đạn đang bay, vệt chém, vùng, buff, trạng
thái, hiệu ứng lặp, môi trường) lấy được từng cách làm khi hợp (Size over Lifetime, so le lớp, xoay ngẫu nhiên), nhưng không
mặc định áp cả mục. Ngoại lệ Limit Velocity của 6.5.2 chỉ dành cho burst va chạm.

Dựng chuyển động bằng module của `ParticleSystem`. Animation bằng texture và shader (flipbook, ăn mòn, UV cuộn) ở 4.4 và 4.7.
Số trong bảng *(đề xuất)* lấy từ một hiệu ứng nổ va chạm đã duyệt, rộng khoảng 2–3 đơn vị: hiệu ứng cỡ khác thì nhân tốc độ và
`Limit` theo cùng tỉ lệ.

| Cần | Làm bằng | Số tham chiếu *(đề xuất)* |
|---|---|---|
| Tia, mảnh vỡ, bụi bắn ra có lực | `Start Speed` lớn + **Limit Velocity over Lifetime** (`Limit` nhỏ, `Dampen`) | speed 9–20, Limit 1, Dampen 0,15, lifetime 0,3–0,5 s |
| Đầu tia kéo dài theo tốc độ | Stretched Billboard: `Length Scale` là độ dài nền, `Speed Scale` nhỏ | Length Scale 2,8, Speed Scale 0,05 |
| Khối mềm trôi chậm (khói, quả cầu) | Velocity over Lifetime, `Speed Modifier` theo đời hạt | 1 → 0,05 |
| Nở bùng (loé, lõi) | Size over Lifetime: key đầu thấp, tiếp tuyến dốc, tới 1 sớm, rồi vẫn nở tiếp | 0,16–0,3 → 1 ở 20–28 % đời (tiếp tuyến 6–11) → 1,15–1,2 |
| Sóng lan (vòng) | như trên, chậm hơn | 0,1 → 0,88 ở 50 % đời → 1 |
| Nhịp chuẩn bị của hiệu ứng tự mang chuẩn bị (nổ, xuất hiện) | một hạt **tối**, alpha blend, bật ra rồi co lại ngay trước loé | sống 0,09 s; loé trễ 0,07 s |
| Mỗi lần phát một khác | `Start Rotation` ngẫu nhiên 0–360°, quay nhẹ theo đời | 0,5–0,8 rad/s |

1. **Tia và mảnh vỡ hãm bằng Limit Velocity, không bằng `Speed Modifier`.** `Dampen` cắt một phần tốc độ vượt `Limit` ở mỗi
   bước mô phỏng: hạt bật ra rất nhanh rồi khựng gọn, hạt càng nhanh càng mất nhiều. `Speed Modifier` hãm theo phần trăm đời
   hạt: hạt nhanh và chậm cùng một dáng hãm, hạt sống dài hãm muộn, cú bắn nhũn đi. `Limit` để lớn hơn 0 thì hạt còn trôi
   chứ không đứng khựng.
2. Quãng bay do `Start Speed` và `Dampen` quyết. Muốn tia bay xa hay gần thì chỉnh hai số đó, không kéo dài lifetime. Đối chiếu
   quãng bay với vùng tác dụng và khung camera của game (2.4).
3. `Dampen` áp theo bước mô phỏng nên nhịp hãm có thể đổi theo fps: xem ở cả 30 và 60 fps.
4. Stretched Billboard đi cùng nhịp hãm: tia dài khi nhanh, co lại khi chậm, đọc ra cú giật (3.2.3). `Speed Scale` lớn thì lúc
   đỉnh tốc tia thành vạch dài hết khung.
5. Ít hạt to, sáng đọc mạnh hơn nhiều hạt nhỏ: hiệu ứng nổ đã duyệt hạ số tia từ 24 xuống 15 và đọc rõ lực hơn.
6. So le các lớp bằng `Start Delay` theo vai: chuẩn bị (0) → tia, loé, vệt, lõi cùng frame bùng (khoảng 0,06–0,07 s) → khối,
   khói trễ thêm khoảng 0,06 s. Mọi lớp cùng t = 0 thì chồng thành một mảng trắng (5.5.2).
7. Không đứng yên giữa đời: sau pha nở, kích thước vẫn tăng nhẹ hoặc hạt quay chậm. Hạt đứng im đọc ra như dán lên màn hình.
8. Hai lớp phải chạy trùng khít nhau (tia và quầng sáng của nó): tắt `Auto Random Seed`, cùng `Random Seed`, và trùng **mọi**
   thông số mô phỏng (tốc độ, lifetime, burst, shape, Limit Velocity, Velocity over Lifetime, Size over Lifetime, gravity).
   Lệch một số là hai lớp tách nhau.
9. Limit Velocity làm mất procedural mode (6.5.2): chấp nhận được cho burst ngắn ở điểm va chạm, xem ngoại lệ ở 6.5.2.

---

## 4. Hình và texture **[VFX] [TA]**

### 4.1 Ngôn ngữ hình

1. Texture vẽ tay hoặc stylized, chi tiết gọn, trộn hình mềm và hình sắc; không ảnh chụp, không chi tiết thừa (nhiễu) [1].
2. Silhouette rõ: hình đơn giản nhưng đủ dải sáng để có tiêu điểm; nhiều chi tiết và tương phản thì không biết vật đang đi đâu
   [1].
3. Hình theo chuyển động: hình kéo dài theo hướng bay, vệt, nhoè (3.2.3) [1].
4. Khối trong suốt lớn thay bằng viền, lõi đục: một tựa tactical shooter đổi hình trụ trong suốt thành viền wireframe, vừa rẻ
   vừa rõ [5]; khối có lõi đục và chỉ trong suốt ở rìa [18].
5. Một tựa shooter đấu trường trên mobile làm được nhiều bằng ít: vài hình vẽ gọn, xoay, scale, đổi màu, và tách vụ nổ thành phần trên không và phần mặt
   đất [10].

### 4.2 Kích thước texture theo kích thước trên màn hình

1. Cạnh texture (hoặc một khung flipbook) ≈ số pixel lớn nhất hình đó chiếm trên màn hình tham chiếu, làm tròn lên lũy thừa
   của 2. Hạt chiếm tối đa 200 px thì khung 256 là đủ.
2. File nguồn trong `Assets` ≤ 2048 mỗi cạnh; lớn hơn để ở file nguồn (8.5). Ba game của team có sheet nguồn tới 8192 × 5120
   và 5608 × 5664 cho một cú nổ 0,4 s, rồi override Android xuống 256–2048 *(đo)*: một sheet 4 × 5 khung ở 256 chỉ còn khung
   51 × 51 px, và không ai biết trước chất lượng cuối.
3. Trần theo cấp: 6.2.

### 4.3 Texture xám và tô màu

1. Texture dùng chung (khói, tia, vòng, loé) vẽ xám, màu lấy từ màu hạt (start color, color over lifetime): một texture phục
   vụ nhiều hiệu ứng, nhiều bảng màu.
2. Shader riêng đọc nhiều mặt nạ thì gộp kênh: R, G, B, A mỗi kênh một mặt nạ, mặt nạ tương phản cao nhất vào A [36].
3. Flipbook màu vẽ sẵn (kiểu 2D frame-by-frame) được, khi đó màu hạt để trắng; đổi tông thì làm texture khác, không nhân màu
   tối lên texture sáng.

### 4.4 Flipbook

Game không dùng frame-by-frame (ghi trong brief, 9.2): bỏ mục này, làm theo 4.7.

1. Lưới lũy thừa của 2 (2 × 2, 4 × 4, 8 × 2 cho vệt chém ngang); 16 khung (4 × 4) gần như là tối đa cho game stylized; sheet
   512, 1024, tối đa 2048, không 4K [21].
2. Tên có hậu tố lưới: `FX_TX_Smoke_4x4` (8.2) [36].
3. Cắt sát từng khung, bớt vùng trong suốt (bớt overdraw); sửa màu rìa bán trong suốt để không viền đen [21].
4. Nhịp 2D frame-by-frame: 12 hình mỗi giây (on twos) là gốc, đoạn nhanh 24 (Game Animation 3.1). Hiệu ứng flipbook ngắn: đủ số
   khung cho đúng thời gian ở 3.1, không kéo dài ra để dùng hết khung.
5. Chế độ Sprites của Texture Sheet Animation (danh sách sprite trong atlas) được dùng, khi sprite nằm trong một atlas VFX
   chung (6.7). Ba game của team dùng chế độ này ở 57–60 % system; trung vị 1 sprite (dùng để gán sprite atlas cho hạt), 10 %
   nhiều nhất 15–32 khung *(đo)*.

### 4.5 Import **[TA]**

| Thuộc tính | Mặc định | Ghi chú |
|---|---|---|
| Nén Android | ASTC 6 × 6 cho sheet màu; 4 × 4 cho dải màu mịn, chi tiết sắc, UI; 8 × 8 cho loé mềm lớn | ASTC là mặc định của Unity, ETC2 dự phòng cho máy GLES 3.0 cũ [26]. Mức cho từng loại là thông lệ, thử bằng mắt trên máy |
| Nén iOS | ASTC, cùng mức | |
| Max size | override theo 4.2 và trần 6.2 | không để mặc định 2048 cho texture 128 px trên màn hình |
| Mipmap | tắt cho 2D camera orthographic và UI; bật cho 3D phối cảnh | Arm: luôn mipmap texture của cảnh 3D [25] |
| Filter | Bilinear | trilinear tốn gấp đôi [25] |
| Wrap | Clamp; Repeat chỉ cho texture cuộn | |
| Read / Write | tắt | |
| Texture Type | Sprite cho flipbook chế độ Sprites và SpriteRenderer; Default cho texture của material | |
| Mặt nạ xám nhỏ | ASTC 4 × 4 hoặc R8 | R8 cũng 8 bit mỗi pixel như ASTC 4 × 4: với mặt nạ nhỏ là chuyện chất lượng, không phải bộ nhớ [30] |

*(đo)* Texture của hiệu ứng đã ship ở ba game: 41–85 % có override Android ASTC (4 × 4 và 6 × 6 là chính; còn lại để mặc
định), mipmap tắt ở 85–96 %, 16–27 % có cạnh nguồn trên 1024.

### 4.6 2D: đơn vị, PPU, sorting

1. Sprite VFX dùng cùng PPU với nhân vật của game.
2. Pivot của hiệu ứng đặt ở điểm chạm đất (hoặc điểm va chạm) để sort theo trục Y đúng với nhân vật (7.4).
3. Hiệu ứng mặt đất (vùng, vết, bóng) ở layer dưới nhân vật; hiệu ứng trúng đòn, trên đầu ở layer trên nhân vật (7.3).

### 4.7 Không frame-by-frame: hạt và shader **[VFX] [TA]**

Game chọn không dùng frame-by-frame (ghi trong brief, 9.2) thì chuyển động của hình làm bằng shader và module của hạt, không
chạy khung. Sheet chỉ để mỗi hạt lấy ngẫu nhiên một hình tĩnh (Frame over Time hằng số hoặc random giữa hai hằng số, chế độ
Lifetime) không phải frame-by-frame, dùng được. Thư viện của module không có frame-by-frame từ bản 0.2.2.

| Cần | Làm bằng | Shader chung `EZG/VFX/Particle` (6.6.8) |
|---|---|---|
| Khói, mây, bụi tan | một hình, ăn mòn theo noise, cỡ tăng theo đời hạt | Erosion, Softness cao (khoảng 0,35) cho tan mềm, không vỡ vụn |
| Lửa, nổ | một hình đầy, ăn mòn theo noise trôi lên; hai lớp (cộng ở lõi, trộn ở rìa) | Erosion, Noise Scroll khoảng (0, −0,6) |
| Đĩa nổ, vòng sóng, vòng mở rộng | ăn mòn hướng tâm: đĩa khoét thành vòng, vòng mỏng dần từ trong ra | Erosion + texture hướng tâm, Noise Random 0, Erosion Range theo bán kính thật của hình |
| Vệt chém, tia, sóng chạy | mesh hoặc trail, UV cuộn trong mặt nạ | UV Scroll + Mask |
| Đổi tông theo nguyên tố, phe | texture xám tô màu theo ramp (4.3) | Ramp |

1. Alpha của hạt (Color over Lifetime) là ngưỡng ăn mòn: hiện dần ở đầu đời nếu cần, giữ nguyên, tan ở khoảng 40 % cuối đời.
   Không dùng alpha để nhấp nháy giữa đời: hình bị ăn lỗ lúc đang chạy (lõi của hiệu ứng lặp hay dính).
2. Số lần đọc texture vẫn theo 6.6.2: Erosion, Mask, Ramp mỗi thứ thêm một lần. Basic, Support, Ambient chỉ được bật một thứ.
3. Noise và texture hướng tâm (256, xám) dùng chung cho cả game: tính vào trần số texture của hiệu ứng (6.2), nhưng chỉ nạp một
   lần cho cả game.
4. Mỗi hạt lệch noise một khác (custom vertex stream StableRandom.x đặt ngay sau UV), không thì mọi hạt của một system tan y hệt
   nhau. Hạt trên UI qua UIParticle chưa dùng stream này: mỗi system UI ít hạt thì không lộ.
5. Hình vẽ tay đổi nhiều qua các khung (lửa 2D vẽ tay, vật biến hình) mất nét khi chỉ giữ một khung rồi ăn mòn. Game không
   frame-by-frame thì vẽ hình cho shader ngay từ đầu (một hình đầy, rìa rõ, texture xám), không cắt một khung từ sheet cũ.
6. Noise trôi và UV cuộn chạy theo `_Time` (giờ game): lúc pause, phần cuộn đứng, kể cả ở hiệu ứng UI chạy giờ thật (3.4).
7. Duyệt như mọi hiệu ứng (9.3), thêm: ảnh đen trắng của pha tan (2.3.7) vẫn đọc được, viền mòn không thành răng cưa ở cỡ thật
   trên máy.

---

## 5. Màu **[VFX] [ART]**

### 5.1 Cấu trúc bảng màu của một hiệu ứng

Đo trên 12 bảng màu mẫu của tài liệu gốc (lấy mẫu pixel trên từng biểu đồ tròn) [1]:

| Phần | Tỉ lệ | Là gì |
|---|---|---|
| Tông chính | 43–55 % | một họ màu |
| Tông thứ hai | 17–31 % | màu kề tông chính trên vòng màu |
| Nhấn | 4–13 % mỗi màu, 1–3 màu | có thể một màu bổ túc nhỏ |
| Tâm nóng | 5–7 % | màu rất nhạt của chính tông (đậm 9–43 %, sáng 96–100 %), không phải trắng; 9 trong 12 bảng có |

Không bảng nào có trắng tinh hay đen tinh; chỗ tối nhất vẫn có màu (`#30002A` của Void, `#11273B` của Water).

### 5.2 Màu bổ túc

1. Ưu tiên màu kề nhau. Có hai màu bổ túc trong một hiệu ứng thì một màu phải là phụ: nhạt hơn, trong hơn [1].
2. Hai màu bổ túc đều đậm, đều đục thì luôn tranh nhau làm tiêu điểm, kể cả khi khác độ sáng (khiên của một tướng hỗ trợ ở
   bản cũ). Đúng: một chiêu của tướng khác trong tài liệu gốc, tím nhạt độ đục thấp làm nền cho vàng sáng [1].

### 5.3 Bảng màu theo nguyên tố

Điểm xuất phát, lấy từ tài liệu gốc [1] (tỉ lệ trong ngoặc). Mỗi game chốt bảng màu nguyên tố của mình trong brief (9.2).

| Nguyên tố (tên tiếng Anh) | Tông chính | Tông thứ hai | Nhấn, tâm nóng |
|---|---|---|---|
| Hư không (Void) | `#6C0364` (50 %) | `#4919A8` (17 %) | `#30002A`, `#EA53E7`, đỏ `#D02F28` (5 %) |
| Độc (Poison) | `#09C93E` (49 %) | `#0B5F23` (30 %) | tím `#3F114E`, `#C9E545` |
| Hồi máu (Heal) | `#58F377` (50 %) | `#76C057` (31 %) | `#F5D753`, `#F0F879` |
| Băng (Frost) | `#6ED6E9` (55 %) | `#375DE7` (29 %) | `#1F03AB`, `#A3FCFD` |
| Thuốc súng, lửa (Gun powder) | `#F5C83D` (54 %) | `#B9351C` (18 %) | `#5B3923`, `#E9F042`, `#F6F98D` |
| Bí thuật (Arcane) | `#3E59AC` (43 %) | `#25F5F6` (28 %) | `#A92CF9`, `#2106A4`, `#A3FCFD` |
| Ma, linh hồn (Spirit) | `#5FFCD5` (54 %) | `#30A597` (21 %) | `#19D55E`, `#E1F5B9`, `#1F03AB` |
| Thiên nhiên (Nature) | `#78FF6D` (54 %) | `#F8FE59` (17 %) | `#EEBB44`, `#1DBB3D`, `#F2F8BD` |
| Thiên thể, thánh (Celestial) | `#F6F65D` (55 %) | `#F9BD3D` (24 %) | `#D7396D`, `#F64CC6`, `#FBDADA` |
| Gió (Wind) | `#9EB7B5` (53 %) | `#50C6BE` (24 %) | `#3780A1`, `#C1FDF8`, `#2A2B20` |
| Công nghệ (Tech) | `#7DFEFF` (48 %) | `#2D49DE` (22 %) | tím `#A71DDA`, `#FEE7FE`, `#DBC9FD`, `#E0FFFF` |
| Nước (Water) | `#30618E` (52 %) | `#2A5175` (24 %) | `#11273B`, `#5CADF3`, `#C5F7FF` |

Bộ slide tiếng Việt về tài liệu này trong thư viện của team có bảng màu khác hẳn tài liệu gốc (`GameVFX_NguyenLy.md` mục 2.7):
dùng bảng trên.

### 5.4 VFX so với model

Bảng màu VFX của một nhân vật sáng hơn và rộng hơn bảng màu model của chính nhân vật đó, cả về sáng lẫn đậm [1]. Đo trên hai ví
dụ của tài liệu gốc: model đậm 25–69 %, sáng 16–77 %; VFX đậm tới 91 %, sáng tới 98–100 %.

### 5.5 Additive và alpha blend

1. Additive chỉ làm sáng: trên nền sáng (tuyết, sa mạc, UI trắng) hiệu ứng additive biến mất. Hiệu ứng phải đọc được trên mọi
   nền của game [10]: yếu tố chính dùng alpha blend (có viền, lõi tối hơn), hoặc additive kèm một lớp alpha blend tối bên dưới.
2. Additive chồng nhiều lớp thì cháy trắng, phá luật 2.3.2: giới hạn số lớp additive chồng nhau ở yếu tố chính.
3. HDR, bloom: bloom tăng mọi thứ trên ngưỡng như nhau. Chỉ tâm nóng của yếu tố chính được vượt ngưỡng bloom; lớp phụ dưới
   ngưỡng. Không nhân một cường độ HDR chung cho mọi material.

---

## 6. Ngân sách mobile **[TA] [VFX]**

### 6.1 Máy tham chiếu, khung thời gian

1. Ba tier máy. Tier Thấp tham chiếu cấu hình tối thiểu của bản MOBA mobile của studio MOBA PC: Android 8, RAM 3 GB, GPU
   Mali-G52 MP1 / Adreno 610 / PowerVR GE8320; iOS: iPhone 6s [6]. Game ghi danh sách máy thử của từng tier trong brief.
2. Chỉ dùng khoảng 65 % thời gian của một frame, phần còn lại để máy nguội: khoảng 22 ms ở 30 fps, 11 ms ở 60 fps [28].
3. Phần của VFX *(đề xuất)*: ≤ 20 % thời gian GPU của frame ở cảnh nặng nhất, trên máy tier Thấp. Unity không cho số này; pilot
   đo và chỉnh (12.3).

### 6.2 Trần theo cấp *(đề xuất)*

Cho một bản hiệu ứng ở tier máy Vừa. Tier Thấp: yếu tố phụ tắt (6.4), hạt còn khoảng một nửa. Cột System không tính root điều
khiển (7.1.1: không phát hạt). Cột texture: cạnh của một ảnh; sheet flipbook tính theo một khung (4.2.1), thư viện của module
cho cả sheet gấp đôi cạnh trần. Hiệu ứng trên UI lấy trần theo hai hàng UI; cấp ở 2.1 (đồ thường Basic, hiếm Damage, huyền
thoại Ultimate) chỉ quyết độ sáng, độ đậm, quy mô trong hàng đó.

| Cấp | System | Hạt đỉnh | Material | Texture: số / cạnh (Android) | Phủ màn hình ở đỉnh | Thời gian không lặp |
|---|---|---|---|---|---|---|
| Ambient | ≤ 2 | ≤ 15 | 1 | 1 / 256 | ≤ 5 % | lặp |
| Basic | ≤ 3 | ≤ 20 | ≤ 2 | ≤ 2 / 256 | ≤ 3 % | ≤ 0,5 s |
| Support | ≤ 4 | ≤ 30 | ≤ 2 | ≤ 3 / 512 | ≤ 10 % | vào ≤ 0,5 s |
| Damage | ≤ 5 | ≤ 60 | ≤ 3 | ≤ 4 / 512 | ≤ 20 % | ≤ 1,5 s |
| Major | ≤ 6 | ≤ 100 | ≤ 4 | ≤ 5 / 1024 | ≤ 35 % | ≤ 2 s |
| Ultimate | ≤ 8 | ≤ 150 | ≤ 5 | ≤ 6 / 1024 | ≤ 60 %, không quá 0,5 s | ≤ 3 s |
| UI nhỏ (lấp lánh icon, nút) | ≤ 2 | ≤ 20 | 1 | 1 / 256 | ≤ 5 % | lặp nhẹ |
| UI lớn (kết quả gacha, lên cấp) | ≤ 8 | ≤ 150 | ≤ 5 | ≤ 6 / 1024 | ≤ 60 % | ≤ 3 s rồi lặp nền |

Cả màn hình *(đề xuất)*: ≤ 300 hạt sống ở tier Thấp, ≤ 600 ở tier Vừa, Cao (thông lệ cộng đồng cho mobile là 150–300 hạt [30]);
tổng phủ của VFX ≤ 100 % màn hình lúc thường, ≤ 200 % trong tối đa 0,5 s.

Căn cứ: quy tắc của dev Trung Quốc "một hiệu ứng ghép ≤ 5 system" [12] (thông lệ); số *(đo)* trên ba game của team (hiệu ứng đã
ship, bỏ pack):

| Đo trên hiệu ứng đã ship | Trung vị | 10 % lớn nhất từ | Lớn nhất |
|---|---|---|---|
| System mỗi hiệu ứng | 3–4 | 11–14 | 51–459 |
| Hạt đỉnh (ước tính) | 5–11 | 51–63 | 597–3045 |
| Material | 1–3 | 4–7 | 11–27 |
| Texture | 2–4 | 7–18 | 19–43 |
| Cạnh texture lớn nhất (Android) | 512–720 | 1024–2048 | 2048 |

Theo số đo đó, 14–22 % hiệu ứng đã ship vượt cả trần cao nhất (8 system), 27–42 % vượt mốc 5 system của cấp Damage: phần vượt
thành nợ, sửa dần (10).

### 6.3 Overdraw

1. Overdraw là chi phí chính: GPU mobile chia màn hình thành ô 16 × 16 px; bật trộn (blend) là tắt phần cứng bỏ pixel bị che,
   "đặc biệt rõ với UI và game 2D nhiều lớp sprite" [25]. Theo dõi số lớp trộn trên mỗi pixel [25].
2. Giảm số hạt **và** kích thước hạt trên màn hình [22]. Hạt to phủ kín màn tốn hơn nhiều hạt nhỏ.
3. Cắt sát sprite và dùng mesh ôm hình (sprite mesh Tight) để bớt vùng trong suốt [21]. Khối lớn: lõi đục, rìa trong suốt [18];
   tách hình lớn thành phần đục và phần trong suốt [25].
4. `Max Particle Size` của renderer (tỉ lệ màn hình, mặc định 0,5) giảm cho hiệu ứng có hạt lớn.
5. Không quad toàn màn hình cho loé, tối màn: một nhịp ngắn của `UIMotionScreenFx` (2.8), không chồng nhiều.
6. Kiểm bằng Overdraw view của Rendering Debugger (URP) hoặc Scene view Overdraw, rồi Frame Debugger, rồi RenderDoc / Arm
   Performance Studio / Snapdragon Profiler trên máy tier Thấp [29][18][25].

### 6.4 Theo tier máy

| Tier | Yếu tố phụ `_sec` | Hạt (`emission.rateOverTimeMultiplier`, burst) | Tính năng |
|---|---|---|---|
| Thấp | tắt | × 0,5 | không distortion, không soft particle, không bloom trên VFX |
| Vừa | bật | × 1 | soft particle chỉ khi pipeline đã có depth texture vì lý do khác |
| Cao | bật | × 1 | distortion được, trong trần 6.2 |

1. Một tựa MOBA mobile lớn ở thị trường châu Á cho người chơi chọn chất lượng hạt Thấp / Vừa / Cao; bản thấp là hiệu ứng kỹ năng đơn giản hoá [12].
2. Game đọc tier máy lúc khởi động và áp cho mọi hiệu ứng khi spawn (7.8). Một game của team đã tính tier máy mà không nơi nào
   đọc *(đo)*.
3. Tier máy không thay luật đọc được: yếu tố chính không bị tắt ở tier nào. Giảm hạt × 0,5 chỉ bớt số hạt, không được làm mất
   hình của yếu tố chính (yếu tố chính chỉ có vài hạt, như một vòng hay một mesh, thì giữ nguyên số hạt).

### 6.5 CPU: số system, culling **[TA] [DEV]**

1. Nhiều system nhỏ tốn CPU hơn một system có cùng tổng số hạt; mỗi system tốn khoảng 50 KB bộ nhớ lúc chạy [30] (số của cộng
   đồng). Gộp các system cùng material, cùng hành vi.
2. System ở procedural mode được Unity bỏ qua khi ngoài màn hình rồi tua nhanh khi vào lại. Những thứ làm mất procedural mode:
   simulation space World, gravity, emission theo quãng đường, external forces, limit velocity, rotation by speed, collision,
   trigger, sub emitter, noise, trails, curve quá 8 key, script sửa giá trị lúc chạy [23] (bài 2016; Inspector của Unity 6
   vẫn báo bằng biểu tượng cảnh báo ở Culling Mode, rê chuột xem lý do). Chỉ dùng các module đó khi hiệu ứng cần thật.
   Ngoại lệ: Limit Velocity của burst ngắn ở điểm va chạm (không lặp, sống dưới 1 s) không cần ghi lý do. Procedural mode chỉ
   có lợi khi system ở ngoài màn hình, mà burst đó luôn ở chỗ người chơi đang nhìn; Limit Velocity là cách chuẩn để tia, mảnh
   vỡ bắn ra có lực (3.7). Vẫn tránh ở hiệu ứng lặp, sống lâu, môi trường.
3. *(đo)* 19–30 % system đã ship của ba game mất procedural mode; lý do nhiều nhất là limit velocity (489–772 system), rồi World,
   gravity, noise, sub emitter. Số này chưa tách burst với lặp: phần burst bật limit velocity nhiều khả năng đúng cách làm ở 3.7,
   không phải nợ. Tách trước khi tính vào nợ.
4. `Culling Mode` Automatic. Always Simulate chỉ cho UI hoặc hiệu ứng phải giữ nhịp tuyệt đối, ghi lý do. *(đo)* 16–32 % system
   để Always Simulate.
5. System có renderer tắt, hoặc nằm trên object đã tắt trong prefab, vẫn tốn: system tắt renderer vẫn mô phỏng mỗi frame;
   object tắt vẫn nạp material và texture cùng prefab. Xoá khỏi prefab đã ship. Một game của team có 434 system tắt renderer
   và 265 system trên object tắt trong hiệu ứng đã ship *(đo)*. Không áp cho system dưới UIParticle: UIParticle tắt renderer
   của system rồi tự vẽ hạt trên canvas.
6. Không để system mặc định của Unity (duration 5, lifetime 5, speed 5, rate 10) trong prefab đã ship: một game có 151 cái *(đo)*.

### 6.6 Shader **[TA]**

1. Chỉ shader unlit. Built-in: nhóm `Mobile/Particles/*` hoặc shader VFX chung của game; URP: `Universal Render
   Pipeline/Particles/Unlit` hoặc shader VFX chung (Shader Graph) của game [22]. Danh sách shader được dùng ghi trong brief của
   game; ngoài danh sách là cảnh báo (10).
2. Số lần đọc texture *(đề xuất)*: ≤ 2 (màu + mặt nạ) cho Basic, Support, Ambient; ≤ 3 cho cấp trên.
3. Soft particle, camera fading, distortion cần Depth Texture (distortion thường cần cả Opaque Texture): chi phí cho **cả
   camera**, không cho riêng một hiệu ứng [24]. Không bật hai texture đó chỉ để vài hiệu ứng dùng. Một game của team bật cả hai
   và có renderer feature distortion chép cả màn hình mỗi frame cho mọi camera, trong khi chỉ 18 prefab đã dùng dùng
   distortion *(đo)*.
4. Không GrabPass (Built-in), không Opaque Texture ở tier Thấp.
5. Không Lights module, không Light component trong hiệu ứng: ba game của team gần như không dùng (0 Lights module, 1 Light
   component ở hai game), giữ vậy *(đo)*.
6. Blend mode, emission, normal map là biến thể shader, chia batch: bớt biến thể không cần [24].
7. Không đổi shader của material pack sang shader khác rồi giữ tên cũ; không dùng material của scene demo (một game dùng
   material ví dụ của một plugin cho vệt đạn) *(đo)*.
8. Game chưa có shader VFX chung thì dùng shader của thư viện, `EZG/VFX/Particle` (`GameVFX_ThuVien.md`): unlit, bốn kiểu trộn,
   chạy cả Built-in lẫn URP, có stencil cho Mask trên canvas. Bốn tính năng bật riêng từng cái, material không bật thì không tốn
   gì: Erosion (alpha của hạt ăn mòn hình theo noise hoặc hướng tâm), UV Scroll, Mask, Ramp (4.7). Không soft particle, không
   distortion: hiệu ứng cần thứ đó dùng shader riêng của game, ghi trong brief.

### 6.7 Material, atlas, batching **[TA] [DEV]**

1. Mọi system trong một hiệu ứng dùng chung ít material nhất có thể, lý tưởng là một material với atlas [22].
2. Material dùng chung cho cả game: một bộ nhỏ theo blend (`_Add`, `_AB`, `_Mul`, `_PM`) và theo atlas. Một game của team có vài
   material chung dùng cho rất nhiều hiệu ứng *(đo)*: giữ cách đó.
3. Đổi màu, độ đục từng bản bằng màu hạt, custom vertex streams hoặc `MaterialPropertyBlock`. Không `renderer.material` lúc chạy
   (tạo material mới, phá batch) [22].
4. Sprite VFX gom vào sprite atlas theo nhóm dùng cùng lúc (8.2), atlas ≤ 2048.
5. GPU instancing chỉ có tác dụng ở render mode Mesh, với shader hỗ trợ [24]; bật ở mesh particle, còn lại không quan trọng.

### 6.8 Pool, nạp trước **[DEV]**

1. Không `Instantiate` / `Destroy` trong lúc đánh: pool theo key VFX (`UnityEngine.Pool.ObjectPool<T>`) [27].
2. Nạp và làm ấm pool lúc load màn, theo danh sách hiệu ứng của màn (nhân vật, kỹ năng, quái có trong màn). Ba game của team
   nạp bằng `Resources.Load` đồng bộ ở lần dùng đầu (có cache), không có danh sách nạp trước cho hiệu ứng: lần đầu nạp ngay
   trong frame trúng đòn *(đo)*, giật lần đầu.
3. Trả về pool lúc hiệu ứng tự báo xong (stop action Callback trên root, 7.2), không theo số giây gõ tay: nghe
   `FXEffect.Finished` (7.8). *(đo)* Stop action là None ở 99,7–100 % system của ba game; thời gian tắt gõ tay, có chỗ 5 s cho một
   cú trúng 0,22 s, 3 s cho hiệu ứng chết 0,65 s. *(đo, 3D)* 98,6–100 %.
4. Trần số bản cùng lúc theo 2.7.2. Hiệu ứng gắn vào nhân vật (buff, trạng thái) trả về pool khi nhân vật chết hoặc về pool.

---

## 7. Làm trong Unity **[VFX] [TA] [DEV]**

### 7.1 Cấu trúc prefab

```
fx_hero_hit_fireball         root: ParticleSystem điều khiển (không phát hạt, renderer tắt), Stop Action = Callback
└─ containers                nhóm: ParticleSystem không phát hạt, renderer tắt, Stop Action = None
   ├─ impact_add             yếu tố chính
   ├─ flash_add_sec          loé ngắn ở pha bùng (loé là yếu tố phụ, 2.3.4)
   ├─ spark_add_sec          yếu tố phụ: tắt ở tier Thấp, lúc đông
   ├─ smoke_ab_sec
   └─ decal_ab               dấu trên mặt đất (nếu có)
```

Khuôn lấy từ prefab đã ship của team (2026-10). Hiệu ứng mới tạo khung bằng script `vfx_new.mjs` của skill `game-vfx`: đúng
cây, tên, thiết lập 7.2, material theo kiểu trộn (8.2).

1. Root là một `ParticleSystem` điều khiển: không emission, renderer tắt (Render Mode None), `Duration` = độ dài của cả
   hiệu ứng, `Looping` theo hiệu ứng. `Play` / `Stop` trên root chạy cả cây; root báo xong bằng Stop Action Callback.
   (Luật 6.5.5 không áp cho root điều khiển: nó không phát hạt nên không mô phỏng gì.)
2. Root có đúng một con là `containers`: `ParticleSystem` không phát hạt, renderer tắt, Stop Action None. Mọi lớp phát hạt là
   con trực tiếp của `containers`, không lồng sâu hơn. Luật 6.5.5 cũng không áp cho `containers`.
3. Root ở vị trí điểm chạm (trúng đòn) hoặc điểm chạm đất (vùng, xuất hiện), scale (1, 1, 1), không xoay trừ khi hiệu ứng có
   hướng (hướng +X cho 2D nhìn ngang, +Z cho 3D).
4. Lớp đặt tên theo 8.4: vai + kiểu trộn, yếu tố phụ có `_sec`.
5. Hiệu ứng có hướng (chém, tia) dựng theo một hướng chuẩn; code xoay root theo đòn.
6. Không collider, không logic gameplay trong prefab hiệu ứng: hitbox là của gameplay (Game Animation 7.4). Prefab gộp cả hình
   lẫn collider thì đổi hình là đụng gameplay. Ba game của team gộp hai thứ này *(đo)*; project cũ giữ, hiệu ứng mới tách.
7. Project có module GameVFX: root gắn component `FXEffect` (7.8): phát, dừng, báo xong theo stop action, tắt lớp phụ ở tier
   Thấp. Script tạo khung không gắn `FXEffect`. Thư viện của module còn theo khuôn cũ (tên PascalCase, không có `containers`):
   nợ, `GameVFX_ThuVien.md` 4.

### 7.2 Thiết lập ParticleSystem

| Thuộc tính | Mặc định | Ghi chú |
|---|---|---|
| Duration | đúng độ dài phần phát | |
| Looping | chỉ hiệu ứng lặp | |
| Prewarm | tắt | chỉ bật cho Ambient đặt sẵn trong scene. *(đo)* 14–25 % system đã ship bật prewarm, đều là system lặp: tốn ngay lúc spawn |
| Start Delay | xếp nhịp các lớp trong hiệu ứng | |
| Start Lifetime | trong thời gian của cấp (3.1) | |
| Simulation Space | Local | World chỉ cho khói, vệt phải ở lại sau vật đang bay; mất procedural (6.5) |
| Delta Time (`useUnscaledTime`) | theo 3.4 | |
| Scaling Mode | Hierarchy | hiệu ứng scale theo cỡ nhân vật, cỡ vùng |
| Play On Awake | bật | pool bật object là phát |
| Max Particles | số hạt cần thật × 1,2, trong trần 6.2 | *(đo)* 63–98 % system đã ship để nguyên 1000; *(đo, 3D)* 90–92 % |
| Auto Random Seed | bật | |
| Stop Action | root: Callback; `containers` và các lớp: None | 7.1 |
| Culling Mode | Automatic | 6.5 |
| Ring Buffer | tắt | |
| Emission | burst cho pha bùng, rate cho lặp | |
| Noise, Collision, Sub Emitters, Trails, External Forces | chỉ khi cần, ghi lý do trong brief | mất procedural (6.5) |
| Limit Velocity | tia, mảnh vỡ của burst ngắn (3.7): dùng được, không cần ghi lý do; hiệu ứng lặp, sống lâu: như dòng trên | mất procedural (6.5.2 ngoại lệ) |
| Lights | không dùng | 6.6.5 |

### 7.3 Renderer, sorting

| Thuộc tính | Mặc định | Ghi chú |
|---|---|---|
| Render Mode | Billboard | Stretched Billboard cho hạt nhanh (3.2.3); Horizontal hoặc mesh phẳng cho vòng, vùng trên đất (3D); Mesh khi cần khối (bật GPU instancing) |
| Material | trong bộ chung (6.7) | có material; không để trống |
| Max Particle Size | 0,5 | giảm cho hạt lớn (6.3) |
| Sorting Layer | layer VFX của game | không `Default`. *(đo)* 41–76 % system đã ship ở `Default`; có layer không tên; có prefab đặt tên layer vật lý vào sorting layer. *(đo, 3D)* 92–98 % ở `Default`, phần lớn chỗ còn lại trỏ vào layer không còn trong project; 3D sort theo khoảng cách nên ít hại hơn 2D, nhưng hiệu ứng phải nằm trên hoặc dưới một lớp cố định thì vẫn cần layer riêng |
| Order in Layer | theo vai: vùng, vết (dưới nhân vật); trúng đòn, trên đầu (trên nhân vật) | ghi bảng order của game trong brief |
| Sorting Fudge | 3D: đẩy lớp loé ra trước, khói ra sau | |

Mỗi game có bảng sorting layer đặt tên rõ cho VFX (ví dụ `FX_Ground`, `FX`, `FX_Top`, `UI`), ghi trong brief; không layer
nào không tên.

### 7.4 2D: sort theo Y

1. Game top-down đặt Transparency Sort Mode = Custom Axis (0, 1, 0): hiệu ứng sort theo Y của pivot, nên pivot đặt ở điểm chạm
   đất (4.6.2).
2. Hiệu ứng gắn vào nhân vật nằm trong Sorting Group của nhân vật (sort theo nhân vật) hoặc ở layer riêng trên nhân vật; chọn một
   cách cho cả game, ghi trong brief.
3. Camera phối cảnh nhìn nghiêng một game 2D: vòng, vùng trên đất vẫn nằm trên mặt phẳng đất (2.4.3).

### 7.5 VFX trên UI

1. Hạt trên Canvas cần thư viện UI particle. Mỗi game chọn **một** (ví dụ UIParticle của Coffee hoặc UIParticleSystem của UI
   Extensions) và ghi trong brief. Hai game của team dùng cả hai cùng lúc, có prefab trộn cả hai *(đo)*. Thư viện của module
   dùng UIParticle 4.13.2; ba game 3D của team đều có UIParticle (4.11–4.13) *(đo, 3D)*. Game chọn UIParticle thì lấy thẳng hiệu
   ứng UI của thư viện.
2. UI VFX chạy giờ thật (3.4), theo trần UI ở 6.2.
3. Không hiệu ứng lặp dưới item của list cuộn (UI Motion M-8); mask mềm (SoftMask) trên hạt tốn, chỉ khi cần.
4. Hiệu ứng thưởng theo độ hiếm: màu theo bảng độ hiếm của game, quy mô theo cấp (2.1).
5. Âm thanh của UI VFX đi qua `UIMotionFeedback` (UI Motion 5), không gắn `AudioSource` vào prefab hiệu ứng.
6. Hạt qua UIParticle vẽ theo thứ tự trong canvas (sorting layer của system không có tác dụng) và có renderer bị UIParticle tắt:
   gate V-14, V-17 bỏ qua. Tắt Raycast Target trên UIParticle để khung hiệu ứng không chặn chạm vào nút nằm dưới.

### 7.6 Âm thanh

1. Tiếng của hiệu ứng thế giới game do hệ âm thanh của game phát theo key, cùng frame với hình (3.3); không `AudioSource` trong
   prefab VFX.
2. Key tiếng cùng kiểu UI Motion và Game Animation: `vfx.<nhóm>.<tên>`, chữ thường (`vfx.hit.slash`).

### 7.7 Pack mua sẵn **[TA] [VFX]**

1. Pack để nguyên trong folder của pack, không sửa file trong đó, để còn cập nhật (như Game Animation 4.5).
2. Hiệu ứng dùng thật thì chép ra folder VFX của game, đặt tên theo 8, dùng material và texture của game (hoặc chép luôn
   material, texture đó ra), kiểm trần 6.2. Game không tham chiếu thẳng prefab, material, texture trong folder pack.
3. Ba game của team cùng mang một pack VFX khoảng 1200–1300 prefab và một bộ UI kit; chỉ 17–18 prefab trong folder pack vào build, nhưng
   179 prefab của một game vẫn dùng material của pack *(đo)*: xoá pack thì vỡ.
4. Pack không dùng thì không để trong project đang phát triển: nặng repo, chậm import, dễ bị tham chiếu nhầm.

### 7.8 Điều khiển từ code **[DEV]**

Một cửa duy nhất cho mọi hiệu ứng. API gợi ý (module sẽ có, 12.3):

| Gọi | Làm |
|---|---|
| `Vfx.Play(key, position, rotation)` | lấy từ pool, đặt, phát; trả handle |
| `Vfx.Play(key, socket)` / `Vfx.Attach(key, target)` | gắn vào socket, đi theo; tự trả pool khi target tắt |
| `Vfx.Play(key, position, radius)` | hiệu ứng vùng: scale theo số của gameplay (2.4.4) |
| `handle.Stop()` | dừng hiệu ứng lặp: chạy phần ra rồi trả pool |
| `Vfx.Preload(keys)` | nạp và làm ấm pool lúc load màn (6.8.2) |

1. Key = tên prefab (8.2). `AE_Vfx` của Game Animation mang đúng key này.
2. Cửa này áp tier máy (6.4), trần số bản (2.7), chế độ thời gian (3.4), màu phe (2.5, khi hiệu ứng có biến thể theo phe).
3. Không gọi `ParticleSystem.Play` rải rác trong code gameplay.
4. Game cũ có sẵn đường spawn riêng (ví dụ một `PoolService.Spawn(path)` dùng chung): giữ đường đó, thêm dần luật trên vào nó,
   không viết đường thứ hai. Một đường spawn chung và key lấy từ dữ liệu là điểm tốt của các game của team *(đo)*.

Bản 0.2 có phần của từng hiệu ứng, `FXEffect` trên root (`Runtime/FXEffect.cs`); cửa chung `Vfx` ở trên vẫn là việc sau:

| Gọi | Làm |
|---|---|
| bật object / `fx.Play()` | phát (Play On Awake); gọi khi đang chạy thì phát lại từ đầu |
| `fx.Stop()` / `fx.Stop(true)` | ngừng phát, hạt đang sống chạy nốt / xoá ngay |
| `fx.Finished` | báo khi hạt cuối cùng tắt (stop action Callback, không đếm giờ); pool thu lại ở đây |
| `fx.whenFinished` | `Disable` (mặc định, hợp pool), `Destroy`, `None` |
| `fx.autoStopAfter` | hiệu ứng lặp tự dừng sau số giây |
| `FXEffect.LowQuality` | tier Thấp: tắt con có đuôi `_Sec` (6.4). Bản hiện tại chưa nhận đuôi `_sec` của khuôn 8.4: nợ, CHANGELOG 0.2.4 |
| `FXEffect.DefaultSortingLayer` | layer VFX của game (7.3), áp cho mọi renderer của hiệu ứng lúc bật |

Đường spawn sẵn có của game gọi `Play` / nghe `Finished` thay cho hẹn giờ tắt; cách dùng với `ObjectPool`: `GameVFX_ThuVien.md` 2.

---

## 8. Đặt tên và thư mục

### 8.1 Luật chung

- ASCII, tiếng Anh, nối bằng `_`.
- Prefab, tên con trong prefab, material: **snake_case chữ thường** (`fx_hit_slash_01`, `impact_add`, `_mat_add`), theo khuôn
  prefab đã ship của team (7.1).
- Texture, mesh, shader, atlas: PascalCase từng đoạn (như Game Animation 4.1), tiền tố `FX_` rồi mã loại, nối tiếp cách các game
  của team đã bắt đầu làm (mỗi game có 9–21 texture `FX_TX_` *(đo)*).
- Tên đã vào code, dữ liệu, event thì không đổi. Game có quy ước tìm hiệu ứng theo đường dẫn, theo ID (ví dụ
  `Ability/<id>/ability_<id>_id_<n>`) thì giữ quy ước đó và ghi vào file riêng của game; hiệu ứng mới của game đó vẫn đặt tên
  con, material, texture theo 8.2.

### 8.2 Tên asset

| Loại | Mẫu | Ví dụ |
|---|---|---|
| Prefab hiệu ứng dùng chung | `fx_<nhóm>_<tên>[_<biến thể>]` | `fx_hit_slash_01`, `fx_buff_heal_loop` |
| Prefab của một đối tượng | `fx_<subject>_<nhóm>_<tên>[_<biến thể>]` | `fx_hero_cast_fireball`, `fx_goblin_death_burst` |
| Material dùng chung theo kiểu trộn | `_mat_add`, `_mat_ab` | lớp `_add` gán `_mat_add`, lớp `_ab` gán `_mat_ab` (8.4) |
| Material riêng | `mat_<tên>_<kiểu trộn>` | `mat_spark_add`, `mat_smoke_ab` |
| Texture | `FX_TX_<Tên>[_<Cột>x<Hàng>][_<Biến thể>]` | `FX_TX_Smoke_4x4`, `FX_TX_Glow_Soft` |
| Mesh | `FX_SM_<Tên>` | `FX_SM_SlashArc` |
| Shader | file `FX_SH_<Tên>`; menu `<Studio>/VFX/<Tên>` | `FX_SH_Dissolve` |
| Sprite atlas | `FX_AT_<Nhóm>` | `FX_AT_Hit` |

Kiểu trộn (tên lớp, tên material): `add` (additive), `ab` (alpha blend), `pm` (premultiplied), `mul` (multiply). Script tạo
khung hiện nhận `add`, `ab`.

Material dùng chung: script tạo khung tìm `_mat_add` / `_mat_ab` trong `Assets/` (không phân biệt hoa thường). Trùng tên thì
lấy bản được prefab VFX tham chiếu nhiều nhất. Chưa có thì tạo mới, shader `Mobile/Particles/Additive` / `Mobile/Particles/Alpha
Blended` (chỉ đúng ở Built-in; project URP thay shader), ở folder `Materials` cạnh folder `Prefabs` chứa prefab (không có folder
`Prefabs` thì `<folder chứa folder prefab>/Materials`).

Nhóm, danh sách cố định, viết thường trong tên prefab (cần nhóm mới thì sửa bảng này trước):

| Nhóm | Gồm |
|---|---|
| `hit` | trúng đòn, va chạm |
| `proj` | đạn, tên, cầu bay (cả vệt của nó) |
| `muzzle` | đầu nòng, lúc bắn |
| `slash` | vệt vũ khí, vệt chém |
| `cast` | lấy đà, niệm chiêu |
| `skill` | thân kỹ năng (khi không tách được cast / proj / hit) |
| `aoe` | vùng tác dụng |
| `warn` | cảnh báo, telegraph |
| `buff`, `debuff`, `heal`, `shield` | trạng thái có lợi, bất lợi, hồi máu, khiên |
| `status` | choáng, đóng băng, độc, cháy… |
| `aura` | hào quang lặp quanh nhân vật |
| `spawn`, `death` | xuất hiện, chết |
| `pickup` | nhặt đồ, rơi đồ |
| `env` | môi trường |
| `ui` | hiệu ứng trên UI |

Hậu tố: `_start`, `_loop`, `_end` cho hiệu ứng lặp nhiều phần; biến thể `_01`, `_02` hoặc chữ (`_fire`, `_rare`); phe `_ally`,
`_enemy` khi có biến thể theo phe; cỡ `_s`, `_m`, `_l` khi có nhiều cỡ.

### 8.3 Thư mục

Đề xuất. Project đã có cấu trúc riêng thì giữ, chỉ cần tên đúng 8.2.

```
Assets/_Project/Art/VFX/
  Shared/      Textures/  Materials/  Meshes/  Shaders/  Atlases/     dùng chung (4, 6.6, 6.7)
  Combat/      <nhóm>/fx_hit_slash_01.prefab
  Characters/  <subject>/fx_hero_cast_fireball.prefab                 VFX riêng của một nhân vật
  Env/  UI/
ArtSource/VFX/                                     ngang hàng Assets, Unity không import
  <Tên>/   file .psd nhiều layer, .blend, project After Effects / EmberGen / tool sinh flipbook, video tham khảo
```

1. Thứ trong `Resources/` luôn vào build, kể cả khi không dùng. Chỉ để ở đó hiệu ứng game gọi thật. Một game của team ship 46
   prefab hiệu ứng không bao giờ nạp, gồm bản `_backup`, `_test` *(đo)*.
2. Bản thử, bản sao lưu không nằm trong folder được build.
3. File nguồn (sheet gốc lớn, file nhiều layer) ở `ArtSource/VFX`, không ở `Assets` (4.2.2).

### 8.4 Tên con trong prefab

Root và `containers` theo 7.1. Mỗi lớp (con của `containers`) đặt tên chữ thường theo mẫu
`<vai>(_<vai>)*_<add|ab>(_<số>)?(_sec)?`:

- **Vai** lấy từ bảng dưới; ghép được nhiều vai (`fire_impact_ab`, `lightning_halfsphere_add`, `ring_shockwave_add`).
- **Kiểu trộn** `_add` / `_ab` bắt buộc: lớp gán `_mat_add` hay `_mat_ab` (8.2), người đọc biết ngay lớp cộng sáng hay phủ.
- **Số** `_2`, `_3` khi nhiều lớp trùng vai và kiểu trộn (`ring_add_2`).
- **`_sec`** ở cuối cho yếu tố phụ (`glow_ab_sec`, `ring_add_2_sec`): `FXEffect` và tool tìm yếu tố phụ theo đuôi tên.

Vai, danh sách cố định (cần vai mới thì sửa bảng này trước, rồi sửa `ROLES` trong `scripts/vfx_new.mjs` của skill `game-vfx`):

| Vai | Dùng cho |
|---|---|
| `impact` | yếu tố chính của cú va chạm |
| `glow`, `flash` | loé |
| `ring`, `shockwave`, `wave` | vòng, sóng |
| `fire`, `smoke`, `dust` | lửa, khói, bụi |
| `spark`, `debris` | tia, mảnh |
| `trail` | vệt |
| `lightning` | tia điện |
| `halfsphere` | bán cầu (mesh) |
| `decal` | dấu trên mặt đất |

⚠️ `FXEffect` bản hiện tại tìm đuôi `_Sec` (S hoa). Tới khi module sửa để nhận `_sec` (không phân biệt hoa thường), tier Thấp
chưa tắt được lớp phụ của khuôn này (CHANGELOG 0.2.4).

---

## 9. Quy trình và duyệt

### 9.1 Các mốc

| Mốc | Làm gì | Ai | Qua khi |
|---|---|---|---|
| 1 Brief | cấp, yếu tố chính, vùng, timing theo frame data, màu, phe, key tiếng, mức rung, ngân sách | GD + VFX | brief đủ các mục 9.2 |
| 2 Blockout | hình khối xám, đúng vùng, đúng timing, trong game | VFX | GD duyệt timing và vùng ở camera gameplay |
| 3 Màu và hình | bảng màu, texture, thứ bậc sáng | VFX | ảnh đen trắng đọc được (2.3.7) |
| 4 Hoàn thiện | yếu tố phụ, tan, âm thanh | VFX + audio | |
| 5 Tối ưu | kiểm trần, cài đặt 7.2, 7.3 | VFX + TA | gate 10 qua, hoặc nợ có lý do |
| 6 Duyệt trên máy | máy tier Thấp, màn hình nhỏ nhất, cảnh đông nhất | lead + GD | checklist 9.3 phần Trên máy |

Thứ tự khi có việc chen ngang như studio MOBA PC: lỗi gameplay trước, độ dễ đọc sau, chủ đề cuối [4].

### 9.2 Brief (mẫu)

| Mục | Ví dụ |
|---|---|
| Key | `fx_hero_cast_fireball`, `fx_hero_proj_fireball`, `fx_hero_hit_fireball` |
| Cấp | Damage |
| Yếu tố chính | quả cầu lửa khi bay; vòng nổ khi trúng (bán kính 2 m) |
| Vùng, hitbox | hình tròn bán kính theo dữ liệu kỹ năng |
| Timing | Cast f1–f8 (startup 8), bay theo tốc độ đạn, Hit: đỉnh ở frame trúng, tan 400 ms |
| Màu | bảng Lửa của game; phe mình |
| Tiếng, rung | `vfx.hit.fireball`, rung 2 |
| Ngân sách | theo cấp Damage (6.2) |
| Tier Thấp | tắt `spark_add_sec`, `smoke_ab_sec` |

Phần của cả game, ghi một lần trong file riêng của game (7.8, 12.2): preset mood, hex ba phe (2.5), bảng màu nguyên tố và độ hiếm
(5.3), dải sáng / đậm đo được của môi trường và nhân vật (2.3.6), sorting layer và order (7.3), shader được dùng (6.6), thư viện UI
particle (7.5), có dùng frame-by-frame hay không (4.4, 4.7), máy thử từng tier (6.1), quy ước key cũ nếu có (8.1).

### 9.3 Checklist duyệt

Đọc được:

- [ ] Biết ngay hiệu ứng là gì, của ai, ở đâu, khi nào (0.1)
- [ ] Cấp đúng brief; to nhỏ, sáng tối đúng cấp (2.1)
- [ ] Một yếu tố chính; tắt yếu tố phụ vẫn đủ thông tin (2.2)
- [ ] Ảnh đen trắng: yếu tố chính sáng nhất, trên môi trường và nhân vật; không trắng tinh, đen tinh (2.3)
- [ ] Vùng và hình khớp hitbox, nằm trên mặt đất (2.4)
- [ ] Màu phe đúng, có dấu hiệu hình (2.5)
- [ ] Không chớp quá 3 lần mỗi giây trên vùng lớn (2.8)

Timing:

- [ ] Có chuẩn bị (hoặc animation lo), bùng, tan; đỉnh ở frame va chạm (3.1)
- [ ] Ease-out, không tuyến tính; hạt nhanh có nhoè (3.2)
- [ ] Hiệu ứng va chạm: tia, mảnh vỡ bắn mạnh rồi hãm bằng Limit Velocity; các lớp so le theo vai; không lớp nào đứng im giữa
  đời (3.7)
- [ ] Tắt đúng lúc; không lưu lâu (3.1.3)
- [ ] Chạy đúng loại giờ; x2, x3, pause không cắt, không chạy lố (3.4)

Kỹ thuật:

- [ ] Trong trần cấp (6.2); cài đặt 7.2, 7.3; tên và thư mục 8
- [ ] Không tham chiếu thẳng pack (7.7); không system tắt renderer, không object tắt thừa (6.5.5)

Trên máy:

- [ ] Máy tier Thấp: fps giữ khi hiệu ứng nặng nhất của màn chạy cùng lúc (6.1)
- [ ] Màn hình nhỏ nhất: đọc được ở camera gameplay
- [ ] Cảnh đông nhất: cảnh báo của địch vẫn nổi (2.7)
- [ ] Nền sáng nhất và tối nhất của game: vẫn đọc được (5.5)

### 9.4 Góp ý khi duyệt

1. Góp ý làm được, khách quan, có đồng thuận: ba tiêu chí studio MOBA PC dùng để lọc phản hồi [4].
2. Chỉ vào mục của quy chuẩn ("lệch 2.3: khói sáng hơn lõi"), không nói chung chung ("chưa đẹp").
3. Chụp màn hình trong game kèm ảnh đen trắng khi góp ý về sáng tối.

---

## 10. Kiểm tra (gate, đề xuất)

Tool quét tĩnh sẽ kiểm các mục có "tĩnh"; phần "trên máy" kiểm ở mốc 6. Script quét của skill `game-vfx`
(`scripts/vfx_scan.mjs`, dùng cho khảo sát) đọc được hầu hết các số dưới đây; module sẽ đưa nó vào Unity (12.3).

| Mã | Kiểm | Cách | Mức |
|---|---|---|---|
| V-1 | Tên prefab, material, texture theo 8.2 (bỏ qua key cũ đã ghi ở file riêng của game) | tĩnh | cảnh báo |
| V-2 | Số system ≤ trần cấp | tĩnh | lỗi |
| V-3 | `Max Particles` không để 1000 mặc định, ≤ trần cấp | tĩnh | cảnh báo |
| V-4 | Hạt đỉnh ước tính (rate × lifetime + burst) ≤ trần cấp | tĩnh | lỗi |
| V-5 | Texture: số và cạnh Android ≤ trần cấp; nguồn ≤ 2048; có override ASTC | tĩnh | lỗi (cạnh), cảnh báo (còn lại) |
| V-6 | Material: số ≤ trần cấp; không material thiếu; không renderer không material | tĩnh | lỗi |
| V-7 | Shader trong danh sách của game; không GrabPass / Opaque Texture ở hiệu ứng dùng cho tier Thấp | tĩnh | lỗi |
| V-8 | Không Lights module, không Light component | tĩnh | lỗi |
| V-9 | Module làm mất procedural (6.5.2) chỉ khi brief ghi lý do; trừ Limit Velocity ở burst ngắn (6.5.2 ngoại lệ, 3.7) | tĩnh | cảnh báo |
| V-10 | Root có Stop Action Callback; hiệu ứng lặp có người dừng | tĩnh + chạy thử | lỗi |
| V-11 | `Culling Mode` không Always Simulate (trừ UI) | tĩnh | cảnh báo |
| V-12 | Prewarm tắt (trừ Ambient trong scene) | tĩnh | cảnh báo |
| V-13 | Hiệu ứng thế giới game giờ game, hiệu ứng UI giờ thật (3.4) | tĩnh | lỗi |
| V-14 | Sorting layer trong bảng của game, không `Default`, không layer không tên (hạt trên UI qua UIParticle bỏ qua: vẽ theo thứ tự canvas) | tĩnh | lỗi |
| V-15 | Thời gian không lặp ≤ trần cấp | tĩnh | cảnh báo |
| V-16 | Không tham chiếu asset trong folder pack | tĩnh | lỗi |
| V-17 | Không system tắt renderer, không object tắt, không system mặc định của Unity trong prefab đã ship (6.5.5–6.5.6; trừ root điều khiển, và system dưới UIParticle vì UIParticle tắt renderer để tự vẽ) | tĩnh | cảnh báo |
| V-18 | Không collider, không `AudioSource` trong prefab VFX mới (7.1.5, 7.6) | tĩnh | cảnh báo |
| V-19 | Chớp ≤ 3 lần mỗi giây trên vùng > 10 % màn hình (2.8) | quay màn hình, đo | lỗi |
| V-20 | Phủ màn hình, hạt trên màn hình trong trần (6.2), fps trên máy tier Thấp | trên máy | lỗi |

Hiệu ứng cũ trượt gate thì ghi nợ (danh sách trong file riêng của game) kèm lý do, sửa khi đụng tới hiệu ứng đó.

---

## 11. Định nghĩa "xong"

Một hiệu ứng xong khi:

- [ ] Có brief (9.2), cấp và yếu tố chính đã được GD chốt
- [ ] Qua checklist 9.3 cả phần Trên máy
- [ ] Gate 10 không lỗi; cảnh báo còn lại có lý do trong brief
- [ ] Tên, thư mục đúng 8; không file thử, bản sao trong folder được build
- [ ] Gọi được bằng key qua cửa chung (7.8); preload có trong danh sách của màn
- [ ] Có bản tier Thấp (yếu tố phụ đánh dấu `_sec`) và đã xem trên máy tier Thấp
- [ ] Âm thanh, rung, hitstop khớp frame (3.3)

---

## 12. Quyết định, câu hỏi mở, pilot

### 12.1 Mặc định của bản nháp

Chưa có gì được team chốt. Bản 0.1 lấy làm mặc định:

- Sáu cấp và luật đọc được theo tài liệu VFX của studio MOBA PC (2.1–2.3), cấp do GD xếp.
- Màu phe: mình vàng, đồng minh xanh, địch đỏ, luôn kèm dấu hiệu hình (2.5).
- Hiệu ứng thế giới game chạy giờ game, UI chạy giờ thật (3.4); hết giờ gõ tay, dùng stop action.
- Prefab tiền tố `fx_` (snake_case), texture, mesh, shader, atlas mã loại `FX_TX_`, `FX_SM_`, `FX_SH_`, `FX_AT_` (8.1, 8.2).
- Trần theo cấp (6.2) là *(đề xuất)*, hiệu chỉnh ở pilot.
- Bản 0.2: shader chung `EZG/VFX/Particle` và UIParticle là mặc định của thư viện (6.6.8, 7.5.1), chưa phải quyết định của team
  cho mọi game (12.2).

### 12.2 Còn mở

| Câu hỏi | Lựa chọn |
|---|---|
| Pipeline cho project mới | URP (không bật Depth / Opaque Texture trừ khi cần cả game) hay Built-in như hai game TD của team |
| Shader VFX chung | một Shader Graph "master" có bật tắt tính năng, hay bộ `Mobile/Particles` + vài shader riêng; thư viện 0.2 dùng `EZG/VFX/Particle` (viết tay, unlit, chạy cả hai pipeline) |
| Thư viện UI particle | UIParticle (Coffee) hay UIParticleSystem (UI Extensions): chọn một cho mọi game mới; thư viện 0.2 dùng UIParticle |
| Nạp hiệu ứng | `Resources` như hiện nay (mọi thứ trong folder vào build) hay Addressables |
| Ai giữ timing | GD giữ frame data, VFX theo; hay VFX đề xuất, GD duyệt |
| Danh sách máy thử | máy cụ thể cho tier Thấp, Vừa, Cao |
| Trần 6.2 | giữ, siết hay nới sau khi đo trên máy |
| File riêng của game | chỗ để brief chung của game: đề xuất `ProjectSettings/GameVFXProject.md`, như UI Motion |

### 12.3 Pilot và tool

1. Tool quét tĩnh (bản đầu là script Node của skill `game-vfx`, dùng cho khảo sát): đưa vào module thành tool Editor chạy gate
   10 cho từng prefab và cả project, xuất danh sách nợ.
2. Runtime: cửa chung 7.8 (pool theo key, preload, stop action, tier máy, trần số bản, chế độ giờ), dùng được trong project cũ
   qua lớp nối với đường spawn sẵn có. Bản 0.2 có phần của từng hiệu ứng (`FXEffect`); cửa chung chưa có.
3. Pilot trên một game đã ship của team: sửa lỗi giờ game / giờ thật (3.4), đo overdraw và thời gian GPU của VFX trên máy tier
   Thấp ở cảnh nặng nhất, rồi chỉnh trần 6.2 và 6.1.3. Đo luôn hiệu ứng của thư viện trên máy, trả nợ trần của nó
   (`GameVFX_ThuVien.md` 4), đánh dấu `_sec`.
4. Skill Claude đi kèm module (như `ui-motion`, `game-animation`): đọc quy chuẩn, duyệt hiệu ứng theo checklist, chạy tool quét.
   Bản 0.2.1 có skill `game-vfx` (`Skill~/game-vfx` của module; bản cài trên Feature Hub, tab AI Feature).

---

## 13. Nối với UI Motion và Game Animation

| Việc | Của | Chỗ |
|---|---|---|
| Hạt, flipbook, shader hiệu ứng, trên thế giới game và trên UI | VFX | tài liệu này |
| Tween của element UI, loé và rung màn hình, số sát thương, coin bay, sfx UI, giảm chuyển động | UI Motion | `UIMotion_QuyChuan.md` (roles ScreenFx, DamageText, Counter / FlyTo; Reduce motion) |
| Frame data, telegraph của quái, hitstop, event `AE_Vfx` / `AE_Shake`, socket, mood | Game Animation | `GameAnimation_QuyChuan.md` 2.1, 3.3, 3.4, 3.7, 4.4, 7.4, 7.5 |

- Một khoảnh khắc va chạm: pose (Game Animation) + hitstop (Game Animation, dev) + VFX trúng đòn (VFX) + tiếng + rung, HP, số
  (UI Motion), cùng một frame.
- Mood: một preset cho cả ba (3.5).
- Giảm chuyển động của UI Motion tắt luôn loé toàn màn hình và giảm rung (2.8).
- Key: `ui.<role>.<event>` (UI Motion), `anim.<subject>.<tên>` (Game Animation), `vfx.<nhóm>.<tên>` (tiếng của VFX, 7.6); key
  hiệu ứng là tên prefab (7.8).
