# Nguyên lý VFX cho game mobile, và nguồn

Phiên bản 0.2 · 2026-09-30 · Lý do của các luật trong `GameVFX_QuyChuan.md` và nguồn của chúng. Quy chuẩn giữ luật; tài
liệu này giữ vì sao, tóm tắt tài liệu gốc, và số đo đi kèm. Mã [n] trỏ tới danh sách nguồn ở mục 9.

---

## 1. VFX để làm gì

Riot đặt bốn mục tiêu cho VFX của Liên Minh Huyền Thoại [1]:

1. Cho gameplay rõ ràng.
2. Bớt rối mắt.
3. Hợp chủ đề của nhân vật.
4. Làm người chơi bất ngờ và thích thú.

Thứ tự đó cũng là thứ tự ưu tiên: khi sửa VFX, Riot sửa lỗi gameplay trước (indicator sai hoặc thiếu, hiệu ứng hỏng, nói sai),
rồi độ dễ đọc và cân bằng độ sáng, cuối cùng mới tới chủ đề, và chỉ khi không đụng hai việc trước [4]. "Rõ ràng" theo Riot là
người chơi hiểu chuyện đang xảy ra và phản ứng kịp: nhận ra nhân vật, chiêu, đòn nhanh; tối thượng và khống chế nổi nhất; hình và
tiếng đều ít nhiễu, vì nhiễu làm chậm phản ứng lúc đánh nhau đông [2].

Trên mobile, cả bốn mục tiêu khó hơn: màn hình nhỏ, ngón tay che một phần màn hình, máy yếu, pin và nhiệt. Mục 3 và 5 là phần
riêng của mobile.

---

## 2. Tài liệu VFX của Liên Minh Huyền Thoại [1]

*The Complete Guide to Creating Visual Effects within League of Legends*, của bộ phận VFX của LMHT, 37 trang, 6 phần. File nằm
trong thư viện Visual Library của team (`VFX/VFX_Styleguide.pdf`). Tài liệu chủ yếu bằng hình; số trong mục này đo trên các
trang render 1400 px (vị trí vạch trên biểu đồ, màu lấy mẫu pixel), không phải số Riot in ra.

### 2.1 Gameplay: vùng tiêu điểm (trang 5–11)

- Mỗi hiệu ứng có yếu tố chính và yếu tố phụ. Chính: tiêu điểm, mục đích của chiêu, nói gameplay rõ và đúng; dải sáng cao,
  silhouette rõ, hình mạnh, tương phản mạnh, đục, chuyển động mạnh. Phụ: tăng chủ đề, đỡ yếu tố chính bằng sáng và đậm, dùng
  nhiều tông hơn; dải sáng thấp hơn, mờ, đơn giản, nhỏ, trong, chuyển động nhẹ. Ví dụ Leona W: viền vòng là chính (biết bán
  kính để né), tia điện mờ bên trong là phụ.
- Vùng tác dụng đúng: nấm của Teemo nổ bán kính khoảng 600 đơn vị mà hiệu ứng chỉ phủ một nửa, không có vòng chỉ vùng (sai);
  bản Omega Squad có vòng mảnh và vụ nổ to đúng vùng (đúng).
- Hitbox đúng: tối thượng cũ của Sona rộng hơn hitbox thật, người chơi thấy trúng mà không bị tính; bản DJ Sona vẽ đúng hình chữ
  nhật, sát mặt đất để góc camera không làm lệch.
- Mức quan trọng: độ "ồn" của hiệu ứng nói nó quan trọng cỡ nào. Riot gắn các thuộc tính VFX với các mốc sức mạnh của chiêu, theo
  ba tiêu chí: đọc được (hiểu ngay mục đích), nhấn (kéo mắt vào chiêu quan trọng, bớt nhiễu lúc đông), đúng tỉ lệ (lớn nhỏ theo
  tầm quan trọng). Đòn thường của Lux luôn nhỏ hơn tối thượng.
- Thang sáu mức (trang 11), điều khiển bằng kích thước, hình, timing, sáng, đậm, đục:

| Mức | Thuộc tính Riot ghi |
|---|---|
| Hạt nhàn rỗi | đục thấp, hình mờ, chuyển động tinh tế |
| Đòn thường | nhỏ |
| Chiêu phòng thủ | đậm thấp, đục thấp, hình mờ, chuyển động tinh tế |
| Chiêu sát thương | đậm cao, đục cao, silhouette rõ |
| Chiêu thay đổi thế trận | đậm cao, đục cao, dải sáng cao, silhouette rõ, chuyển động mạnh |
| Tối thượng | đậm cao nhất, đục cao nhất, dải sáng cao nhất, lớn, animation ấn tượng |

Quy chuẩn 2.1 đổi tên sáu mức thành Ambient, Basic, Support, Damage, Major, Ultimate, điền các ô Riot bỏ trống theo cùng logic, và
thêm kích thước, thời gian, ngân sách.

### 2.2 Dải sáng (trang 12–17)

- Luật: dải sáng cao kéo mắt; tương phản tạo vùng rõ; tránh 100 % và 0 % vì lẫn với môi trường hoặc UI.
- Biểu đồ "dải sáng theo mảng" (trang 13) là một thanh từ đen tới trắng với vạch cho từng mảng. Đọc vạch trên hình (thanh từ y
  200 tới 785 ở bản render): UI 92–100 %, VFX 2–95 %, nhân vật 5–58 %, môi trường 8–45 %. Trang 14 là cùng ý trên một ảnh gameplay
  đen trắng: môi trường xám tối vừa, VFX sáng gần trắng tới xám giữa, nhân vật từ sáng tới tối, UI trắng.
- Đẩy sáng vào tâm (quả bom của Ziggs): có tâm thì biết chỗ nổ mạnh nhất, dễ thấy lúc đông; không tâm thì chìm vào nền.
- Loé (glow) diễn tả phép thuật và nói hướng, thời gian của hiệu ứng.
- Tiêu điểm do tương phản: rất tối hoặc rất sáng so với xung quanh đều là tiêu điểm, xám giữa thì không. Đặt nền tối sau hiệu
  ứng giúp được, nhưng lạm dụng thì tranh nhau lúc đông.

### 2.3 Màu (trang 18–26)

- Luật đậm giống luật sáng: đậm cao kéo mắt, tương phản tạo tiêu điểm, tránh 100 % và 0 %. Biểu đồ trang 19: UI 93–100 %, VFX
  4–95 %, nhân vật 6–73 %, môi trường 9–45 %.
- VFX của một nhân vật có dải sáng và đậm cao hơn, rộng hơn model (trang 20). Lấy mẫu hai ví dụ:

| Nhân vật | Bảng màu model | Bảng màu VFX |
|---|---|---|
| Aether Wing Kayle | `#5E441F` `#3872B2` `#18152A` `#B7A068` (đậm 43–69 %, sáng 16–72 %) | `#B9D1E8` `#F0ED57` `#FEFCD4` `#4C8BDB` (sáng 86–100 %) |
| Dragon Slayer Braum | `#7C2E2A` `#57483C` `#62506B` `#AA8573` `#C4957E` (đậm 25–66 %, sáng 34–77 %) | `#5F1709` `#C4341D` `#FAE65A` `#381309` (đậm 64–91 %, sáng 22–98 %) |

- Màu bổ túc (trang 21): nên dùng màu kề nhau; có hai màu bổ túc thì một màu phải là phụ, vì hai màu đối nhau luôn tranh làm
  tiêu điểm, kể cả khi khác độ sáng. Sai: khiên của Lulu, hai màu bổ túc cùng đậm, cùng đục. Đúng: Bard Q, tím nhạt trong suốt
  làm nền cho vàng sáng.
- Mười hai bảng màu thường dùng (trang 22–25), đo tỉ lệ bằng cách lấy mẫu 180 điểm trên một vòng quanh tâm mỗi biểu đồ tròn:

| Bảng | Màu (tỉ lệ) |
|---|---|
| Void | `#6C0364` 50 % · `#4919A8` 17 % · `#30002A` 13 % · `#EA53E7` 12 % · `#D02F28` 5 % |
| Poison | `#09C93E` 49 % · `#0B5F23` 30 % · `#3F114E` 12 % · `#C9E545` 7 % |
| Heal | `#58F377` 50 % · `#76C057` 31 % · `#F5D753` 12 % · `#F0F879` 7 % |
| Frost | `#6ED6E9` 55 % · `#375DE7` 29 % · `#1F03AB` 8 % · `#A3FCFD` 6 % |
| Gun powder | `#F5C83D` 54 % · `#B9351C` 18 % · `#5B3923` 13 % · `#E9F042` 7 % · `#F6F98D` 6 % |
| Arcane | `#3E59AC` 43 % · `#25F5F6` 28 % · `#A92CF9` 13 % · `#2106A4` 9 % · `#A3FCFD` 6 % |
| Shadow Isle | `#5FFCD5` 54 % · `#30A597` 21 % · `#19D55E` 9 % · `#E1F5B9` 7 % · `#1F03AB` 7 % |
| Nature | `#78FF6D` 54 % · `#F8FE59` 17 % · `#EEBB44` 13 % · `#1DBB3D` 7 % · `#F2F8BD` 6 % |
| Celestial | `#F6F65D` 55 % · `#F9BD3D` 24 % · `#D7396D` 7 % · `#F64CC6` 7 % · `#FBDADA` 6 % |
| Wind | `#9EB7B5` 53 % · `#50C6BE` 24 % · `#3780A1` 9 % · `#C1FDF8` 7 % · `#2A2B20` 4 % |
| Hextech | `#7DFEFF` 48 % · `#2D49DE` 22 % · `#A71DDA` 10 % · `#FEE7FE` 6 % · `#DBC9FD` 6 % · `#E0FFFF` 5 % |
| Water | `#30618E` 52 % · `#2A5175` 24 % · `#11273B` 11 % · `#5CADF3` 6 % · `#C5F7FF` 5 % |

  Quy luật rút ra (quy chuẩn 5.1): một tông chính 43–55 %, một tông kề 17–31 %, một tới ba màu nhấn 4–13 %; chín bảng có một màu
  rất nhạt của chính tông (đậm 9–43 %, sáng 96–100 %) làm tâm nóng; không bảng nào có trắng tinh hay đen tinh; màu bổ túc nếu có
  thì nhỏ (đỏ 5 % trong Void, hồng 7 % trong Celestial).
- Màu phe (trang 26): cùng một vòng chỉ vùng, đồng minh dải xanh lơ → tím (`#39EAFA` `#38C6F9` `#365DF5` `#431AF4` `#6E1CF4`),
  địch dải hồng đỏ → đỏ cam (`#FA2B73` `#FA2C62` `#FA2B35` `#F93E2A` `#F9662F`); đậm 77–89 %, sáng 96–98 %.

### 2.4 Hình (trang 27–30)

- Phong cách: chi tiết gọn, texture vẽ tay, trộn hình mềm và sắc, silhouette rõ, texture tự mang chuyển động. Tránh texture ảnh
  chụp và chi tiết thừa: tạo nhiễu.
- Silhouette rõ giảm nhiễu lúc đông: nhiều chi tiết và tương phản thì không biết vật đang đi đâu, hình gì. Hình đơn giản mà đủ
  dải sáng là có tiêu điểm.
- Nhoè tạo cảm giác chuyển động và hướng. Hạt nhanh không nhoè gây nhiễu và ảo giác rớt frame.

### 2.5 Timing (trang 31–36)

- Mọi hiệu ứng có chuẩn bị và tan. Phần ra là phụ: sáng, đậm, đục thấp hơn. Năng lượng tắt dần bằng đổi sáng, tông, đậm, đục hoặc
  kích thước. Vòng cảnh báo Q của Sion đếm thời gian tới lúc choáng đủ tầm.
- Thùng thuốc súng của Gangplank: lấy đà (thùng đỏ rồi vàng) → nổ chính → tan (khói, mảnh). Tài liệu không cho số thời gian.
- Chuyển động đúng chất liệu: tường gió của Yasuo, than hồng của Diana, giấy rơi theo trọng lực của Mundo.
- Timing động: đường ease-out (nhanh rồi chậm) cho khoảnh khắc mạnh hơn tuyến tính (Ekko R).
- Bớt thời gian hiệu ứng ở lại màn hình: W của Syndra bản Snow Day đục và ở lâu, át hiệu ứng khác; bản Justicar tan nhanh và hơi
  trong ngay cả ở đỉnh.

### 2.6 Những gì tài liệu Riot không có

Không số thời gian, không số hạt, không ngân sách hiệu năng, không luật cho mobile, không luật kỹ thuật engine. Quy chuẩn lấy các
phần đó từ các nguồn ở mục 3–7 và từ số đo trên game của team (mục 8); phần không có nguồn ghi *(đề xuất)*.

### 2.7 Bộ slide tiếng Việt trong thư viện [39]

Thư viện có 14 slide tiếng Việt vẽ lại tài liệu này (`VFX/Docs_VFX_LOL`, 2026-04). Slide dễ đọc, nhưng có chỗ lệch tài liệu gốc;
dùng tài liệu gốc khi hai bên khác nhau:

| Slide | Slide nói | Tài liệu gốc |
|---|---|---|
| 6, thang tầm quan trọng | đòn thường "độ bão hoà cao"; phòng thủ "bão hoà trung bình", "lớn / bao quanh"; thay đổi thế trận "hỗn loạn / lâu"; tối thượng "cực lớn / toàn màn hình" | đòn thường chỉ "nhỏ"; phòng thủ đậm thấp, đục thấp, mờ, tinh tế; thay đổi thế trận chuyển động mạnh, và phần timing đòi bớt thời gian ở lại; tối thượng "lớn" |
| 10, màu bổ túc | hiệu quả = tông trung tính, giảm bão hoà | ưu tiên màu kề; màu bổ túc thì một màu làm phụ (nhạt, trong), như Bard Q |
| 11, bảng màu | ô màu khác hẳn: Thuốc súng xám, nâu; Hextech đồng, cam; Hồi máu trắng, hồng; Thiên thể trắng, vàng, xanh, bạc | Thuốc súng vàng cam 54 %; Hextech xanh lơ 48 % + xanh dương + tím; Hồi máu xanh lá 50 %; Thiên thể vàng 55 % + cam + hồng (2.3) |
| 13, Gangplank | chuẩn bị 0,5 s, tác động tức thì, hồi phục 1,0 s | không có số |
| 14, timing | gộp Syndra W (ở lâu) và Ekko R (timing động) thành một so sánh cũ / mới | hai ví dụ riêng cho hai luật |
| 12, tiêu đề | "độ rõ vàng" | độ rõ ràng |

---

## 3. Đọc được trên mobile: nguồn ngoài Riot

- **Silhouette và skin**: mọi skin qua bài thử "đây là tướng nào" như tướng gốc [3]; silhouette chính không đổi qua skin; skin
  phải rõ bằng hoặc hơn bản gốc; lượng thay đổi animation, VFX, âm thanh của một skin là một "cái xô" không được tràn [2]. Giá
  skin quyết độ thay đổi VFX (≤ 750 RP một chỉnh nhỏ; 975 RP đổi màu và thêm chi tiết; 1350 RP trở lên VFX mới; 1820 RP trở lên VFX
  mới và emote), còn cỡ indicator giữ nguyên [4]. Ý cho team: gắn ngân sách VFX với độ hiếm của vật phẩm, skin.
- **Phe**: Valorant tô viền đỏ cho địch, xanh cho đồng minh, và làm sáng nhân vật ở xa [5]; Brawl Stars và Clash Royale giữ đồng
  minh xanh, địch đỏ, hiệu ứng phải đọc được trên sa mạc, tuyết, hầm mỏ [10][11]. Chế độ mù màu của LMHT nhắm dạng mù màu đỏ /
  xanh lá, đổi thanh máu của mình sang vàng, đồng minh xanh [8]; người chơi phàn nàn nó đổi ít ngoài thanh máu. Game Accessibility
  Guidelines: không truyền thông tin chỉ bằng màu, cam / xanh dương hợp cả ba dạng mù màu phổ biến, không dùng filter toàn màn
  hình, 8–10 % nam giới mù màu đỏ / xanh lá [34].
- **Khối trong suốt**: Valorant đổi hình trụ trong suốt của một tối thượng thành viền wireframe, rẻ hơn và rõ hơn; hiệu ứng che
  tầm nhìn dùng khối có giới hạn rõ thay cho hiệu ứng màn hình mềm [5]. Bàn tròn VFX GDC 2017: khối lớn dùng lõi đục, chỉ rìa
  trong suốt [18].
- **Theo người xem**: Overwatch giảm lượng hiệu ứng địch thấy khi trúng một chiêu, giảm tiếng trúng đòn lặp lại [13]; LMHT chọn
  ai nghe được câu thoại nào (câu về nhà chỉ mình nghe) [3]. VFX có thể khác theo mình / đồng minh / địch / nạn nhân; trúng lặp
  lại thì nhẹ hơn lần đầu.
- **Chạm**: Wild Rift đổi nhiều chiêu chỉ-và-bấm thành chiêu ngắm vì chọn mục tiêu bằng ngón tay khó, và thêm phần bấm cho nội tại
  bị động [7]: vệt ngắm, cảnh báo quan trọng hơn trên PC.
- **Đông**: game kiểu survivor như Vampire Survivors có tuỳ chọn tắt hiệu ứng chớp và số sát thương [14]. Thanh trượt độ đục
  không đủ: nhiều lớp trong suốt vẫn phủ gần kín [14]. Cần trần số bản, làm nhẹ bản lặp, và một dải màu riêng cho đạn địch.
- **Brawl Stars làm với rất ít**: bản đầu không có shader riêng, không mesh particle, chỉ hạt phẳng và trộn cơ bản; bù bằng rất
  nhiều animation của vài hình gọn (xoay, scale, đổi màu), vẽ từng khung trong Flash, tách vụ nổ thành phần trên không và mặt
  đất, đổi biến thể mây để khỏi lặp; tia điện ngắn, mảnh, quản khung cẩn thận để khỏi nhiễu [10].
- **Sáu nguyên lý** của VFX Apprentice (trường của Jason Keyser, cựu VFX Riot): thông tin gameplay trước; hình hơn chi tiết
  texture; dải sáng là một "ngân sách" chia cho nhân vật và môi trường; màu chính và phụ trong một hiệu ứng; thời gian sống ngắn,
  rời màn hình khi đã nói xong (đỡ rối và đỡ tốn); bố cục dẫn mắt [15].
- **Mẹo duyệt** từ bàn tròn VFX GDC 2017: xem cảnh bằng đen trắng và lật ngang, dọc để nhìn bố cục; có một style guide VFX; tool lọc
  hiệu ứng theo loại (môi trường, nhân vật, va chạm) để tìm chỗ nặng; nhánh debug trong shader để xem UV, vertex color, overdraw
  [18].
- **Chuyển giao**: Legends of Runeterra (Unity, mobile và PC): artist dựng chuỗi VFX, animation, âm thanh bằng Timeline hoặc
  PlayMaker; code chỉ gọi các móc có nghĩa ("khi trúng") qua một giao diện chung, không biết artist dùng tool nào [9]. Quy chuẩn
  7.8 lấy ý này: code gọi key, VFX giữ nội dung.

---

## 4. Timing, va chạm, flipbook

- Ba pha được gọi bằng nhiều tên (wind-up → climax → fall-off) [16]. Trong game đối kháng, hiệu ứng phải rơi đúng frame sát thương:
  độ dài chuẩn bị và tan do thiết kế quyết, không do VFX [16].
- Nhịp tim: xung nhanh hơn khoảng 60 nhịp mỗi phút đọc là hung hăng, chậm hơn đọc là êm [16] (thông lệ).
- Đọc cách làm của Genshin (bài phân tích của cộng đồng, không phải HoYoverse): 1–2 frame trắng hoặc mất màu toàn màn hình rồi một
  frame đảo màu khi va chạm; rút đời hạt còn một nửa cho gọn; dừng hình rất ngắn khi trúng; tối thượng làm mất màu cả màn trừ phần
  chính; hình sắc như vector, mesh vệt chém bất đối xứng, sprite nét tốc độ kiểu manga, texture vẽ tay thay mô phỏng chất lỏng
  [17]. Khung trắng toàn màn hình mâu thuẫn với luật chớp (mục 6) và tốn fill rate trên mobile: quy chuẩn 2.8 chỉ cho một nhịp ở
  Ultimate.
- Rung camera theo mô hình trauma (GDC 2016): trauma 0–1, mỗi cú đánh cộng thêm, giảm tuyến tính theo thời gian; độ rung =
  trauma² hoặc trauma³ (với mũ 3: trauma 0,3 / 0,6 / 0,9 cho 3 % / 22 % / 73 % mức rung); góc và độ dời nhân nhiễu Perlin, không
  dùng số ngẫu nhiên; 3D chỉ xoay, 2D xoay và dời [19].
- Hitstop: Smash Ultimate khoảng sát thương × 0,65 + 6 frame, trần 30 frame ở 60 fps (số khai thác từ game); Final Fight 6 frame
  cho mọi đòn (cộng đồng đếm); Sakurai: dừng cả hai bên, dài theo lực, có ở đòn kết liễu [20]. Số dùng trong team nằm ở Game
  Animation 3.4.
- Flipbook: 4 × 4 = 16 khung gần như là tối đa cho game stylized; lưới lũy thừa của 2; sheet 512 / 1024 / 2048, tránh 4K; lưới không
  vuông được (8 × 2 cho vệt ngang); nhiều khung thì mỗi khung nhỏ đi; tên kèm lưới; cắt sát khung; sửa màu rìa để khỏi viền tối
  [21][36].

---

## 5. Hiệu năng mobile

- **Khung thời gian**: chỉ dùng khoảng 65 % thời gian của frame để máy nguội: khoảng 22 ms ở 30 fps, 11 ms ở 60 fps [28]. Không
  nguồn chính thức nào cho phần của VFX; quy chuẩn 6.1.3 đề xuất 20 %.
- **GPU theo ô**: Mali chia màn hình thành ô 16 × 16 px xử lý trên chip. Bật trộn là tắt early ZS và Forward Pixel Kill, phần cứng
  bỏ pixel bị che; ảnh hưởng "đặc biệt rõ với UI và game 2D nhiều lớp sprite". Nên: tắt trộn cho vật đục; theo dõi số lớp trộn
  trên mỗi pixel; tách hình lớn thành phần đục và phần trong suốt. Không nên: để trộn bật cho mọi thứ, "tắt trộn" bằng alpha 1, trộn
  trên render target float [25].
- **Texture** (Arm): độ phân giải thấp nhất có thể; nén ASTC (hoặc ETC2); luôn mipmap texture của cảnh 3D; trilinear tốn gấp đôi;
  sampler mediump [25]. Unity: ASTC là mặc định trên Android, ETC2 dự phòng cho máy GLES 3.0 cũ; ASTC từ 8 bit mỗi pixel (4 × 4)
  xuống 0,89 (12 × 12) [26]. Mặt nạ xám nhỏ để R8 không nén cũng được: R8 là 8 bit mỗi pixel như ASTC 4 × 4 [30].
- **Unity cho mobile**: vật trong suốt luôn tốn hơn vật đục; bớt số hạt và kích thước hạt; dùng shader hạt dựng sẵn, unlit rẻ nhất;
  hiệu ứng glow toàn màn hình rất nặng; mỗi camera thêm tốn tới khoảng 1 ms CPU ở máy yếu (không dùng camera riêng cho VFX); bật
  SRP Batcher; dùng `sharedMaterial`, không `material`; mọi system trong một hiệu ứng chung một material / atlas [22].
- **Culling**: system procedural được bỏ qua khi ngoài màn hình và tua nhanh khi vào lại; non-procedural phải chạy tiếp. Mất
  procedural khi: simulation space World, gravity, emission theo quãng đường, external forces, limit velocity, rotation by speed,
  collision, trigger, sub emitter, noise, trails, curve hơn 8 key, script đổi giá trị lúc chạy. Inspector báo bằng biểu tượng cảnh
  báo; hiệu ứng buộc phải non-procedural thì tự cull bằng `CullingGroup` [23]. Bài viết năm 2016; kiểm lại trên Unity 6.
- **URP**: soft particle, camera fading, distortion của shader Particles Unlit chỉ chạy khi Depth Texture bật trong URP Asset, tức
  tốn cho cả camera; soft particle và flip-book blending "có thể giảm hiệu năng"; blend mode, emission, normal map chia batch
  [24]. GPU instancing của particle chỉ ở render mode Mesh, với shader hỗ trợ [24].
- **Pool**: `Instantiate` / `Destroy` liên tục sinh rác, GC giật. `UnityEngine.Pool.ObjectPool<T>` có từ 2021 LTS; ví dụ sức chứa
  20, tối đa 100; làm ấm lúc load; reset trạng thái khi trả [27].
- **Công cụ**: Overdraw view của Rendering Debugger (URP) → Frame Debugger → chụp trên máy yếu bằng RenderDoc / Arm Performance
  Studio / Snapdragon Profiler [29][18][25].
- **Số của cộng đồng** [30]: mỗi Particle System tốn khoảng 50 KB lúc chạy (5 system đuốc × 200 bản ≈ 49 MB); nhiều system nhỏ tốn
  CPU hơn một system cùng tổng số hạt; khoảng 150–300 hạt sống trên mobile so với 500–800 trên PC (chưa kiểm chứng).
- **Honor of Kings**: 1080p / 60 fps từ máy rẻ tới máy đầu bảng; người chơi chọn chất lượng hạt Thấp / Vừa / Cao, bản Thấp là hiệu
  ứng kỹ năng đơn giản hoá [12]. Bài của dev Trung Quốc: hạt là nguồn overdraw số một; một hiệu ứng ghép ≤ 5 system; giới hạn diện
  tích hạt trên màn hình; tier thấp thì ít hạt, ít module hơn [12] (thông lệ).
- **Máy tham chiếu**: cấu hình tối thiểu của Wild Rift: Android 8, RAM 3 GB, CPU 4 nhân Cortex-A53 2,0 GHz, GPU Mali-G52 MP1 /
  PowerVR GE8320 / Adreno 610; iOS: iPhone 6s (A9), RAM 2 GB, iOS 15 [6].

---

## 6. An toàn thị giác

- **WCAG 2.3.1** [31]: đạt nếu không quá 3 lần chớp thường và 3 lần chớp đỏ trong bất kỳ 1 giây nào, hoặc vùng chớp dưới ngưỡng.
  Chớp thường: một cặp tăng giảm độ sáng tương đối ≥ 10 % mức tối đa, bên tối dưới 0,80. Chớp đỏ: một bên là đỏ đậm, R / (R + G +
  B) ≥ 0,8. Vùng ngưỡng: 0,006 steradian trong trường nhìn 10° (khoảng 25 % trường đó), tương đương khung 341 × 256 px trên màn hình
  1024 × 768 (khoảng 11 % màn hình). W3C nhắc: người xem gần hơn khoảng cách giả định (cầm điện thoại) bị ảnh hưởng cả ở vùng đã đạt
  chuẩn.
- **Xbox XAG 118** [32]: khoảng 1 trên 4000 người có cơn động kinh do ánh sáng; mọi game nên được kiểm; chớp quá khoảng 3 lần mỗi
  giây trên khoảng 20 % màn hình là trượt; mẫu sọc tương phản > 10 % trên khoảng 20 % màn hình là trượt; kiểm bằng Harding FPA.
- **Xbox XAG 117** [33]: tránh rung camera, nhún camera, nhoè chuyển động, hoặc cho tắt; Halo Infinite có thanh 0–100 % riêng cho
  nhoè, rung, hiệu ứng toàn màn hình, nét tốc độ.
- **Game Accessibility Guidelines** [34]: tránh chuỗi chớp dài hơn 5 s; 3 lần chớp mỗi giây trên 25 % màn hình; sọc chuyển động (từ
  5 vạch) trên 25 %, sọc tĩnh (từ 8 vạch) trên 40 %; không gọi tuỳ chọn là "an toàn cho người động kinh".
- Ba nguồn lệch nhau về vùng (khoảng 11 % của màn hình tham chiếu, 20 %, 25 %). Quy chuẩn 2.8 lấy 10 %, mức chặt nhất, vì điện thoại
  ở gần mắt.

---

## 7. Quy ước sản xuất

- **Tên**: không có tiền tố VFX chung cho ngành. Epic hiện dùng `FXS_` (system), `FXE_` (emitter), cùng `M_`, `MI_`, `T_`, `SM_`, theo
  mẫu `[Loại]_[Tên]_[Mô tả]_[Biến thể]` [35]; cộng đồng dùng `FX_`, `CH_`, `EN_` cùng `T_`, flipbook kèm lưới `_2x4`; tiền tố có ích khi
  profiler chỉ in tên texture, không in đường dẫn [36]. Quy chuẩn 8 chọn `FX_` + mã loại vì các game của team đã bắt đầu dùng
  `FX_TX_`, `FX_MT_`.
- **Quy trình của Riot** [4]: chọn tướng → bản gốc có vòng phản hồi → các skin → thử → máy chủ thử nghiệm công khai → gom phản hồi →
  lên bản chính, rồi chỉnh theo bản vá. Phản hồi được lọc theo ba tiêu chí: làm được, khách quan, có đồng thuận.
- **Tài liệu học** Riot gợi ý [38]: sách *Elemental Magic* của Joseph Gilland, diễn đàn và Discord Real-Time VFX, kênh của Jason
  Keyser. GDC 2017 có buổi VFX Bootcamp về nguyên lý nghệ thuật (Hadidjah Chamberlain, Blizzard; Jason Keyser, Riot) [37].

---

## 8. Khảo sát các game của team

Chi tiết, đường dẫn, lệnh chạy lại: tài liệu phát triển `Docs/GameVFX/GameVFX_KhaoSat.md` của repo module (không đi kèm module).
Ba game mobile 2D đã ship: hai game thủ thành cùng một codebase (Built-in RP, một game là bản reskin của game kia), một game đánh
bài đánh theo lượt (URP). Cùng một template của studio: cùng pool, cùng công cụ dựng hiệu ứng, cùng một pack VFX mua sẵn.

Số đo trên hiệu ứng thật sự vào bản build (đi được từ scene build và folder `Resources`), bỏ folder pack: 520–910 hiệu ứng, 3768–4413
system mỗi game. Những điều quy chuẩn lấy làm căn cứ:

| Phát hiện | Số | Luật |
|---|---|---|
| Hạt gameplay chạy giờ thật, thời gian tắt đếm bằng giờ game; game đổi tốc độ x1–x3 và pause bằng `timeScale` | 69–73 % system gameplay dùng unscaled time | 3.4 |
| Thời gian sống gõ tay, không stop action | stop action None ở 99,7–100 % system; có chỗ 5 s cho cú trúng 0,22 s | 6.8 |
| `Max Particles` để mặc định | 63–98 % system để 1000 | 7.2 |
| Sorting lộn xộn | 41–76 % system ở layer `Default`; layer không tên; tên layer vật lý dùng làm sorting layer | 7.3 |
| Sheet nguồn quá lớn, override xuống | 16–27 % texture nguồn cạnh > 1024, tới 8192 × 5120 | 4.2 |
| Mất procedural, mô phỏng khi ngoài màn hình | 19–30 % system mất procedural; 16–32 % Always Simulate | 6.5 |
| System vô ích | một game: 434 system tắt renderer, 265 trên object tắt, 151 system mặc định của Unity | 6.5 |
| Chi phí cả camera cho vài hiệu ứng | một game bật Depth + Opaque Texture, distortion chép cả màn hình mỗi frame, cho 18 prefab | 6.6 |
| Thứ bậc sáng bị bloom san phẳng | một game: phần lớn các lớp chung một material HDR ×3, bloom 3,6 | 2.3, 5.5 |
| Nạp đồng bộ ở lần dùng đầu | `Resources.Load` lúc trúng đòn đầu tiên, không nạp trước | 6.8 |
| Pack để trong project, bị tham chiếu | pack khoảng 1200–1300 prefab mỗi game, 17–18 prefab trong folder pack vào build; một game có 179 prefab dùng material của pack | 7.7 |
| Hai thư viện UI particle cùng lúc | hai game dùng cả hai | 7.5 |
| Điểm tốt | một đường spawn chung, key lấy từ dữ liệu, vài material chung cho rất nhiều hiệu ứng, hiệu ứng trúng đòn rẻ bằng 1–3 hạt flipbook, hiệu ứng cast và impact tách riêng, độ hiếm đọc bằng màu, API rung camera tập trung | 6.7, 7.8 |

Timing của các mẫu tiêu biểu nằm trong khung của tài liệu Riot: trúng đòn cận chiến 0,22 s, nổ cầu lửa 0,4 s, chết 0,65 s, khiên 0,5
s; chỉ hiệu ứng xuất hiện có pha chuẩn bị riêng (0,5 s trên tổng 1,4 s).

**Ba game 3D (bản 0.2).** Ba game mobile 3D dọc (URP, Gamma), cùng pack Epic Toon FX, dùng UIParticle cho hạt trên UI: 63–95 hiệu
ứng tự làm vào build mỗi game, 391–552 system. Khác game 2D: chỉ 5–13 % system chạy giờ thật (không có lỗi 3.4 ở quy mô lớn).
Giống game 2D: `maxParticles` 1000 ở 90–92 % system, stop action None ở 98,6–100 %, sorting layer `Default` ở 92–98 %, có layer
mồ côi. Một game có 50 % system Always Simulate và 60 % bật limit velocity. Một game 3D mang lại mọi hiệu ứng của game 3D trước nó;
một game có sprite và material đã mất mà prefab vẫn trỏ tới (hạt vẽ ô trắng). Các số này dùng làm nguồn cho thư viện
(`GameVFX_ThuVien.md`) và ghi *(đo, 3D)* trong quy chuẩn.

---

## 9. Nguồn

O = chính thức (hãng engine, hãng GPU, studio), T = bài nói hội thảo, C = cộng đồng. Ngày truy cập: 2026-09-30.

1. Riot Games, League of Legends VFX discipline. *The Complete Guide to Creating Visual Effects within League of Legends* (PDF, 37
   trang). Thư viện Visual Library của team: `VFX/VFX_Styleguide.pdf`. (O)
2. Riot Games. "Clarity in League", 2021-03-12. https://www.leagueoflegends.com/en-gb/news/dev/clarity-in-league/ (O)
3. Riot Games. "Ask Riot: Let's Talk Clarity", 2021-04-09. https://www.leagueoflegends.com/en-gb/news/dev/ask-riot-let-s-talk-clarity/ (O)
4. Riot Sirhaian, Riot Riru. "/Dev: Behind the Scenes of VFX Updates", 2022-04-25.
   https://www.leagueoflegends.com/en-us/news/dev/dev-behind-the-scenes-of-vfx-updates/ (O)
5. Brandon Wang, Riot Games. "VALORANT Shaders and Gameplay Clarity", 2020-06-30.
   https://www.riotgames.com/en/news/valorant-shaders-and-gameplay-clarity (O)
6. Riot Games Support. "Wild Rift minimum system requirements", 2026.
   https://support.riotgames.com/en-us/wild-rift/performance/wild-rift-minimum-system-requirements (O)
7. Pocket Gamer (Stephen Gregson-Wood), dẫn lời Brian Feeney (Riot). Wild Rift dev update, 2020-03-19.
   https://www.pocketgamer.com/wild-rift/league-of-legends-wild-rifts-latest-dev-update-details-the-teams-approach-to-adj/ (C)
8. Riot Games Support. "Colorblind Mode", 2026. https://support.riotgames.com/en-us/league-of-legends/gameplay/colorblind-mode (O)
9. Jules Glegg, Riot Games. "Bringing Features to Life in Legends of Runeterra", 2019-11-26.
   https://www.riotgames.com/en/news/bringing-features-life-legends-runeterra (O)
10. Woohyun Kim (Supercell). "Very Cartoonic VFX for Brawl Stars made with Flash", realtimevfx.com, 2019-11-03.
    https://realtimevfx.com/t/very-cartoonic-vfx-for-brawl-stars-made-with-flash/11032 (C, artist của studio)
11. Maciej Górnicki. "Game design UX best practices: detailed breakdown of Clash Royale", The Rookies, 2020-02-24.
    https://www.therookies.co/blog/education/game-design-ux-best-practices-detailed-breakdown-of-clash-royale (C)
12. Xiaoxin Guo, Tencent. "Practical High-Performance Rendering On Mobile Platforms" (Honor of Kings), GDC 2023.
    https://www.gdcvault.com/play/1029284 (T); tuỳ chọn chất lượng hạt của Honor of Kings (hỏi đáp của người chơi, C); quy tắc hạt của
    dev Trung Quốc, https://zhuanlan.zhihu.com/p/142478356 (C)
13. Blizzard. Overwatch patch notes, 2026-09. https://overwatch.blizzard.com/en-us/news/patch-notes/live/2026/09/ (O)
14. Vampire Survivors, trang Options của wiki cộng đồng. https://vampire.survivors.wiki/w/Options (C, ghi lại tuỳ chọn chính thức của
    game); thảo luận Steam về độ đục hiệu ứng trong game kiểu survivor (C)
15. VFX Apprentice. "The 6 Artistic Principles of Game VFX", 2023-12-05.
    https://www.vfxapprentice.com/blog/five-artistic-principles-gaming-vfx (C)
16. VFX Apprentice. "The Soul of Effects: What is Timing in VFX?", 2025-08-20.
    https://www.vfxapprentice.com/blog/the-soul-of-effects-what-is-timing-in-vfx (C)
17. VFX Apprentice. "Genshin Impact VFX Breakdown: Stylized Technical Art Guide", 2026-09-18.
    https://www.vfxapprentice.com/blog/genshin-impact-stylized-vfx-breakdown (C, không phải của HoYoverse)
18. Drew Skillman (chủ trì). "GDC 2017 Visual Effects Artist Roundtable Summary". https://www.drewskillman.com/gdc2017_vfxroundtable.pdf (T)
19. Squirrel Eiserloh. "Math for Game Programmers: Juicing Your Cameras With Math", GDC 2016.
    https://gdcvault.com/play/1023146 (T)
20. SmashWiki, "Hitlag", https://www.ssbwiki.com/Hitlag (C); Sakurai, cột Famitsu số 490 (Source Gaming dịch, 2015),
    https://sourcegaming.info/2015/11/11/thoughts-on-hitstop-sakurais-famitsu-column-vol-490-1/ (O, dịch); Shane Sicienski, hitstop
    của beat 'em up Capcom, https://shane-sicienski.com/blog/blog-post-title-one-55pmn (C)
21. VFX Apprentice. "What are Flipbooks in Games", 2025-09-24. https://www.vfxapprentice.com/blog/what-are-flipbooks-in-games (C);
    VFXDoc, "Flipbooks and Texture Sheets", https://vfxdoc.readthedocs.io/en/latest/textures/flipbooks/ (C)
22. Unity. "Art optimization tips for mobile game developers, part 2" (trích e-book *Optimize your game performance for mobile*).
    https://unity.com/how-to/mobile-game-optimization-tips-part-2 (O)
23. Karl Jones, Unity. "ParticleSystem Performance – Culling", 2016-12-20.
    https://unity.com/blog/engine-platform/particlesystem-performance-culling-tips (O)
24. Unity Manual (6000.0). "Particle System GPU instancing", https://docs.unity3d.com/Manual/PartSysInstancing.html; "Particles Unlit
    shader (URP)", https://docs.unity3d.com/6000.0/Documentation/Manual/urp/particles-unlit-shader.html (O)
25. Arm. *Arm GPU Best Practices Developer Guide* (101897), 2025. https://developer.arm.com/documentation/101897 (O)
26. Unity Manual (6000.2). "Choose a GPU texture format by platform".
    https://docs.unity3d.com/6000.2/Documentation/Manual/texture-choose-format-by-platform.html (O)
27. Unity Learn. "Use object pooling to boost performance of C# scripts in Unity" (Design Patterns, Unity 6).
    https://learn.unity.com/course/design-patterns-unity-6/tutorial/use-object-pooling-to-boost-performance-of-c-scripts-in-unity (O)
28. Thomas Krogh-Jacobsen, Unity. "Optimize your mobile game performance: tips on profiling, memory, and code architecture",
    2021-06-23. https://unity.com/blog/games/optimize-your-mobile-game-performance-tips-on-profiling-memory-and-code-architecture-from (O)
29. Unity Manual (6000.1). "Rendering Debugger window reference for URP".
    https://docs.unity3d.com/6000.1/Documentation/Manual/urp/features/rendering-debugger-reference.html (O)
30. Real Time VFX forum. "Performance optimization for mobile games", 2023-10.
    https://realtimevfx.com/t/performance-optimization-for-mobile-games/24644 (C)
31. W3C WAI. "Understanding Success Criterion 2.3.1: Three Flashes or Below Threshold" (WCAG 2.2).
    https://www.w3.org/WAI/WCAG22/Understanding/three-flashes-or-below-threshold.html (O)
32. Microsoft. "Xbox Accessibility Guideline 118: Photosensitivity", 2022 (cập nhật 2026).
    https://learn.microsoft.com/en-us/gaming/accessibility/xbox-accessibility-guidelines/118 (O)
33. Microsoft. "Xbox Accessibility Guideline 117: Visual distractions and motion settings", 2022.
    https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/117 (O)
34. Game Accessibility Guidelines. https://gameaccessibilityguidelines.com (các mục về chớp, sọc, và màu) (C, tiêu chuẩn của ngành)
35. Epic Games. "Recommended Asset Naming Conventions in Unreal Engine projects" (UE 5.8).
    https://dev.epicgames.com/documentation/en-us/unreal-engine/recommended-asset-naming-conventions-in-unreal-engine-projects (O)
36. VFXDoc. "Textures overview". https://vfxdoc.readthedocs.io/en/latest/textures/overview/ (C)
37. Hadidjah Chamberlain (Blizzard), Jason Keyser (Riot). "Visual Effects Bootcamp: Artistic Principles of VFX", GDC 2017.
    https://www.gdcvault.com/play/1024439 (T, chỉ có tóm tắt)
38. Riot Games. ArtEdu, "Visual Effects". https://www.riotgames.com/en/artedu/visual-effects (O)
39. Team. 14 slide "Mục tiêu thiết kế VFX – Liên Minh Huyền Thoại", thư viện Visual Library `VFX/Docs_VFX_LOL`, 2026-04. (bản vẽ lại
    của [1], có chỗ lệch, mục 2.7)

---

## 10. Luật và nguồn

| Mục quy chuẩn | Nguồn |
|---|---|
| 0 nguyên tắc gốc | [1] [2] [4] |
| 2.1 cấp | [1] [2] |
| 2.2 chính / phụ | [1] |
| 2.3 dải sáng, đậm | [1] [18]; số đo mục 8 |
| 2.4 vùng, hitbox | [1] [2] [4] |
| 2.5 màu phe | [1] [5] [8] [10] [11] [13] [14] [34] |
| 2.6 cảnh báo | [1] [7]; Game Animation 3.3 |
| 2.7 màn hình đông | [13] [14] |
| 2.8 chớp, rung | [17] [31] [32] [33] [34] |
| 3.1–3.2 ba pha, nhịp | [1] [15] [16]; số đo mục 8 |
| 3.3 khớp va chạm | [19]; Game Animation 3.4, 7.4, 7.5 |
| 3.4 giờ game / giờ thật | số đo mục 8 |
| 4 hình, texture | [1] [5] [10] [18] [21] [25] [26] [30] [36] |
| 5 màu | [1] [10] |
| 6 ngân sách | [6] [12] [18] [22] [23] [24] [25] [27] [28] [29] [30]; số đo mục 8 |
| 7 Unity | [22] [23] [24] [27]; số đo mục 8 |
| 8 tên | [35] [36]; số đo mục 8 |
| 9 quy trình | [4] [9] [18] |
