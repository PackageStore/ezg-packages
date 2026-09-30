# 12 nguyên lý animation trong game 2D và 3D

Phiên bản 0.1.4 · 2026-09-28 · Tài liệu nền của `GameAnimation_QuyChuan.md`. Ở đây nói **vì sao**, quy chuẩn nói **luật**.

Người đọc: animator, rigger, tech art, game design, dev muốn hiểu vì sao quy chuẩn đặt luật như vậy. Số trong ngoặc vuông
`[n]` trỏ tới danh sách nguồn ở mục 7, `[n] tr. x` là trang in của sách; lời của tác giả đã được diễn lại bằng tiếng Việt.
Viết tắt: **FbF** = frame-by-frame
(vẽ từng hình, gồm pixel art), **SK** = 2D skeletal (xương và mesh: Unity 2D Animation, Spine).

---

## 0. Nguồn gốc

| Năm | Ai | Đóng góp |
|---|---|---|
| 1981 | Frank Thomas, Ollie Johnston (Disney), *The Illusion of Life* | 12 nguyên lý rút từ phim vẽ tay của Disney từ thập niên 1930 |
| 1987 | John Lasseter (Pixar), bài SIGGRAPH *Principles of Traditional Animation Applied to 3D Computer Animation* | Áp các nguyên lý vào 3D. Liệt kê 11 (không có solid drawing), coi tính cách là đích tới chứ không phải một nguyên lý [1] |
| 2019, 2021 | Jonathan Cooper, *Game Anim: Video Game Animation Explained* | Đọc lại 12 nguyên lý cho game, thêm 5 nền tảng riêng của game animation. Bản 2 có thêm chương 2D và pixel art [2][3][4] |
| 2001, 2009 | Richard Williams (đạo diễn animation của *Who Framed Roger Rabbit*), *The Animator's Survival Kit*, bản mở rộng 2009 | Gom cách làm của Ken Harris, Milt Kahl, Frank Thomas, Art Babbitt, Grim Natwick thành công thức: timing và spacing, key / extreme / breakdown, đi, chạy, lấy đà, accent, thoại, diễn. Mọi số tính ở 24 fps [44] |
| 2025 | Jonathan Annand (Disney, EA, Iron Galaxy: *Killer Instinct*, *Rumbleverse*), *Animation Craft for 3D and 2D Animators* | Từng nguyên lý làm thế nào trong 3D và 2D; thêm nguyên lý 13 "trọng lượng và thăng bằng"; cách góp ý khách quan và checklist cảnh [45] |
| 2008 → nay | Steve Swink (*Game Feel*), Mick West (đo độ nhạy), Masahiro Sakurai (kênh *Creating Games*) | Cảm giác điều khiển, độ trễ, hitstop, lấy đà của người chơi [7][14][15][16] |

---

## 1. Game khác phim ở chỗ nào

| Phim | Game | Hệ quả cho animation |
|---|---|---|
| Đạo diễn quyết lúc nào hành động bắt đầu | Người chơi quyết, bằng input | Clip phải bắt đầu ngay và ngắt được giữa chừng |
| Camera đặt sẵn cho từng shot | Camera do người chơi hoặc hệ thống điều khiển, nhìn từ mọi góc, trên màn hình nhỏ | Pose phải đọc được từ mọi góc có thể, ở kích thước thật |
| Mỗi cảnh chạy một lần | Clip lặp hàng nghìn lần | Loop liền, có biến thể, không chi tiết nào gây khó chịu khi lặp |
| Nối cảnh bằng cắt | Nối trạng thái bằng blend | Clip phải nối được với nhiều clip khác |
| Chỉ cần đẹp | Phải đúng gameplay: hitbox, tốc độ, frame data | Timing là số của thiết kế, không riêng của animator |
| Render offline | Chạy realtime trên điện thoại | Ngân sách xương, bộ nhớ, CPU, GPU |

---

## 2. Mười hai nguyên lý, thêm nguyên lý 13

Mỗi nguyên lý có: là gì, áp vào 2D (FbF, SK) và 3D, chỗ game khác phim, lỗi hay gặp, cách kiểm. Luật rút ra nằm ở quy
chuẩn mục 2. Dòng **Williams, Annand** là cách làm hai sách chỉ ra [44][45]; công thức đủ ở quy chuẩn 7.6–7.10. Mục 2.13 là
nguyên lý Annand thêm vào.

### 2.1 Nén và giãn (squash & stretch)

- **Là gì.** Biến dạng vật để cho thấy khối lượng, độ cứng, tốc độ. Luật chính: thể tích không đổi. Hình giãn còn nối hai
  frame của một chuyển động nhanh, thay cho motion blur, để mắt không thấy giật [1].
- **2D.** FbF: vẽ lại từng hình; hình nhanh nhất dùng smear hoặc hình kéo dài. Pixel art giữ tổng lượng pixel: lệch một
  pixel là lộ khi loop [36]. SK: xương scale được, mesh deform được (xương của Unity 2D Animation là Transform; xương Spine
  có xoay, dời, scale, shear) [27][30].
- **3D.** Lasseter scale theo hướng chuyển động và co hai trục còn lại để bù. Vật có khớp thì "nén" bằng cách gập, "giãn"
  bằng cách duỗi, không cần biến dạng [1]. Nhiều engine không mang scale xương vì tốn bộ nhớ, nên animator nén giãn bằng
  pose [2]. Unity Humanoid lưu pose bằng góc xoay (muscle), không có scale từng xương: nén giãn bằng scale mất khi dùng
  Humanoid; rig Generic giữ được curve scale [25][26]. Sakurai: scale được dùng ít hơn xoay và dời, nhưng đổi hẳn cảm giác
  của vật [7d].
- **Trong game.** Đụng với retarget, bộ nhớ, và hitbox: hình to ra mà hitbox không đổi là đánh lừa người chơi.
- **Williams, Annand.** Nén giãn là đổi hình dáng, không bắt buộc phải bóp méo: đồng xu cứng mà spacing đúng vẫn đọc ra trọng
  lượng, bóng golf không méo, lạm dụng thì mọi thứ như cao su [44] tr. 39. Annand cho mỗi nhân vật một biên độ nén, giãn cố
  định, luôn so với pose trung tính; nén quá to thì "loé", giữ quá lâu thì lệch model; nhân vật cứng (giáp) nén giãn bằng khớp
  gãy lần lượt; ở đỉnh cú nảy thể tích về trung tính [45] tr. 47–58.
- **Lỗi hay gặp.** Thể tích đổi, nhân vật như to lên; vật cứng cũng nén; nén giãn có trong phần mềm dựng nhưng mất khi
  import.
- **Kiểm.** Tua tới pose cực trong bản build, so rộng × cao của silhouette với pose nghỉ; xác nhận kiểu rig có giữ scale.
- **Số.** sx · sy · sz ≈ 1: giãn s lần theo một trục thì hai trục kia còn khoảng 1/√s (suy từ [1]).

### 2.2 Chuẩn bị (anticipation)

- **Là gì.** Động tác chuẩn bị trước hành động chính: vừa lấy đà cho cơ thể, vừa báo cho người xem điều sắp tới và dẫn mắt
  họ. Người xem đã đoán trước thì hành động chính được phép nhanh hơn [1].
- **2D.** FbF: một hai hình giữ ở pose ngược chiều. SK: key pose ngược chiều. Cả hai phải đọc được bằng silhouette.
- **3D.** Key pose ngược chiều. Với đòn của người chơi, cắt bớt đoạn lấy đà tự nhiên có sẵn trong mocap.
- **Trong game.** Cooper gọi đây là chủ đề gây tranh cãi trong game: ít quá thì đòn không có lực, dài quá thì nhân vật lì
  và người chơi mất quyền điều khiển. Lấy đà của quái (telegraph) thì nên dài hơn. Lực cũng bán được bằng cách khác ngoài
  thêm frame [2]. Sakurai có video về việc làm lấy đà của người chơi tức thì mà vẫn có lực [7b]. Mick West: đặt lực nhảy
  muộn trong clip nhìn có thể đẹp hơn nhưng chơi thấy tệ [15].
- **Williams, Annand.** Lấy đà luôn ngược chiều hành động, thường chậm và nhẹ hơn hành động; hành động quen tay (chuyển số, mặc
  áo) thì không cần lấy đà [44] tr. 274–275. Lấy đà "vô hình" 1–3 frame (24 fps) chỉ cảm được mà không thấy [44] tr. 283–284:
  cách cho đòn của người chơi có lực mà không trễ (quy chuẩn 3.2). Annand: lấy đà không lớn hơn hành động (trừ khi gây cười), là
  key phải có từ blocking, ease vào pose lấy đà khoảng 3 frame [45] tr. 189–197; sách không bàn lấy đà với độ nhạy của input.
- **Lỗi hay gặp.** Lấy đà tả thực cho input của người chơi; telegraph giống hệt đòn khác tới tận những frame cuối; lấy đà
  bị VFX hoặc camera che mất.
- **Kiểm.** Người chơi: frame đầu tiên sau input đã thấy đổi, pose chính tới trước frame active. Quay màn hình tốc độ cao,
  đếm frame từ lúc bấm tới lúc màn hình đổi (cách đo của Mick West [14]). Quái: từ tín hiệu đọc được đầu tiên tới frame
  active phải ≥ ngân sách phản xạ (mục 4.2).
- **Số.** Đòn thường của Street Fighter 6 có startup 4–10 frame ở 60 fps, khoảng 67–167 ms [13]. Trong phim ngắn *The
  Adventures of André & Wally B.*, cú phóng đi chỉ 3–4 frame, sau một đoạn lấy đà đủ lâu để đọc [1].

### 2.3 Dàn dựng (staging)

- **Là gì.** Trình bày một ý sao cho rõ, không thể hiểu nhầm: mỗi lúc một ý, dẫn mắt người xem, thể hiện hành động bằng
  silhouette, đặt nghiêng sang bên [1].
- **2D.** Camera ngang hoặc top-down cố định nên chủ động được silhouette. Đưa hành động ra ngoài thân, không diễn trước
  ngực [1].
- **3D.** Cooper: staging chỉ áp trực tiếp ở chỗ animator đặt camera, tức cinematic. Trong gameplay nó thành bố cục màn và
  ánh sáng dẫn đường người chơi [2]; phần còn lại thuộc nền tảng Readability: pose cho camera game, silhouette [3].
- **Trong game.** Người chơi cầm camera; màn hình nhỏ. Nhiều nhân vật cùng lúc thì ưu tiên mối nguy và phản hồi: không gì
  được át telegraph.
- **Williams, Annand.** Đổi biểu cảm trước hoặc sau một động tác lớn, không đổi giữa chừng [44] tr. 321–322; key, extreme nào
  cũng phải đọc được như silhouette, profile đọc nhanh nhất [44] tr. 251, 344–345. Annand: game thì pose cho camera gameplay,
  không che hành động; 3D camera tự do thì đường hành động, silhouette phải đọc được từ mọi góc; tay không che mặt [45] tr. 60,
  67–69, 228.
- **Lỗi hay gặp.** Pose chỉ đọc được từ camera trong phần mềm dựng; hành động diễn trước thân; idle ồn ào trong lúc quái
  lấy đà.
- **Kiểm.** Chụp không ánh sáng trên máy nhỏ nhất, ở khoảng cách gameplay: mỗi key pose đọc được như một silhouette phẳng.

### 2.4 Làm thẳng và pose-to-pose (straight ahead & pose to pose)

- **Là gì.** Làm thẳng: làm lần lượt từ đầu tới cuối, cho sự tự nhiên (hỗn loạn, hiệu ứng). Pose-to-pose: đặt pose chính
  trước rồi chèn giữa, để kiểm soát diễn xuất và timing [1].
- **2D.** Pose-to-pose cho nhân vật; làm thẳng cho lửa, khói, smear. Dead Cells làm mỗi đòn cho đúng timing với ít frame
  nhất rồi mới thêm hình chèn, nên đổi timing chỉ cần dời key [38].
- **3D.** Lasseter: key nguyên pose trên rig phức tạp cho inbetween khó đoán; ông làm từng lớp theo cây xương, ít pose cực,
  không key mọi kênh cùng một frame [1]. Mocap là dữ liệu dày kiểu làm thẳng; đội Uncharted cắt bớt, có lúc chỉ giữ key
  pose [5].
- **Trong game.** Thiết kế đổi liên tục. Cooper giữ clip ở mức pose-to-pose lâu nhất có thể và khuyên đừng tiếc phần đã
  làm [2].
- **Williams, Annand.** Williams chọn cách thứ ba, kết hợp: thumbnail → key → extreme (chạm, lấy đà) → breakdown, rồi làm thẳng
  từng phần trên khung đó, phần quan trọng trước, tóc, đuôi, vải sau cùng; ở mức key + extreme + breakdown đã có 3–4 pose mỗi
  giây, đủ để đọc timing [44] tr. 61–67. Chạy thử ở mọi bước [44] tr. 68–69, 338. Annand: blocking chỉ có pose kể chuyện ở key
  stepped, chưa thêm breakdown khi pose chưa chạy; đủ pose khi mô tả được cung, các lần quay đầu, mọi lần đổi hình dáng; lượt
  spline là lượt rút gọn, bỏ key thừa giữa hai extreme trên từng kênh [45] tr. 73–75, 199–211, 280.
- **Lỗi hay gặp.** Polish trước khi gameplay chốt; key dày khiến đổi timing tốn công.
- **Kiểm.** Đổi timing chỉ bằng dời key pose được không; blocking stepped đã được duyệt trong engine trước khi polish chưa.

### 2.5 Theo đà và chồng chéo (follow through & overlapping action)

- **Là gì.** Hành động đi quá điểm dừng; các bộ phận dẫn hoặc bị kéo theo, trễ và lắng lại theo khối lượng. Hành động sau
  chồng lên hành động trước, không bắt đầu từ đứng yên [1].
- **2D.** FbF: vẽ tóc, vải, vũ khí trễ theo, liền qua chỗ nối loop. SK: lệch key, hoặc physics constraint của Spine 4.2
  (xương trễ theo chuyển động, xuyên qua các clip và theo di chuyển trong màn) [30].
- **3D.** Lệch và kéo key; damping bằng thủ tục như Damped Transform của Animation Rigging [28]. Cooper: physics cho vải và
  tóc nâng chất lượng mà ít công, và tự nối sang clip kế tiếp [2].
- **Trong game.** Theo đà chính là recovery. Cooper muốn animator đánh dấu được frame người chơi lấy lại điều khiển trước
  khi clip kết thúc [2]. Sakurai: follow-through là thời gian hồi, và nó có thể dài hơn ta tưởng [7c]. Recovery là lúc bên
  đánh hở, dễ bị phạt [12].
- **Williams, Annand.** Annand tách ba nghĩa: overlap (các bộ phận nhân vật tự điều khiển lệch nhau), follow-through (phần bị
  kéo theo: tóc, vải, thịt) và recovery của game [45] tr. 169. Phần bị kéo trễ ít nhất 1–2 frame, mỗi đốt tóc, vải trễ thêm ít
  nhất 1 frame [45] tr. 176–177. Khớp gãy lần lượt (successive breaking of joints): khuỷu, cổ tay, bàn tay, ngón trễ nhau 4–5
  frame ở 24 fps nhưng chồng lên nhau; làm xong từng khớp một thì trông như robot [45] tr. 182–188. Williams: khớp không bao giờ
  cùng gập một lúc; lỗi "King Kong" là mọi phần đi cùng một lượng; việc lớn bắt đầu từ hông, việc nhỏ từ khuỷu, cổ tay [44]
  tr. 217, 235–237, 262.
- **Lỗi hay gặp.** Mọi bộ phận tới đích cùng lúc (lỗi kinh điển của người mới [2]); khoá điều khiển tới hết phần nhìn của
  clip; pop khi hành động bị cancel.
- **Kiểm.** Có frame trả điều khiển (cancel) trước cuối clip; cancel ở đúng frame đó sang né hoặc di chuyển mà blend không
  pop.
- **Số.** Street Fighter 6, Ryu, recovery ở 60 fps: đấm nhẹ 7, đấm nặng 18, Shoryuken nhẹ 21 cộng 12 sau khi đáp [13].

### 2.6 Chậm vào, chậm ra (slow in & slow out)

- **Là gì.** Chèn hình dày hơn gần pose cực, để chuyển động tăng tốc rồi giảm tốc [1].
- **2D.** FbF: nhiều hình gần key, ít hoặc không có ở giữa [38]. SK: tiếp tuyến của spline.
- **3D.** Lasseter chỉ ra spline vọt quá (đế đèn Luxo Jr. chìm xuống sàn); sửa bằng thêm key hoặc bẻ tiếp tuyến [1].
  Sakurai: pose và timing đúng hết mà vẫn trông chậm, lỗi nằm ở nội suy [7e].
- **Trong game.** Đòn của người chơi bắt đầu nhanh, không ease vào sau input; trọng lượng nằm ở phần lắng. Ví dụ của
  Cooper: phóng đại độ giật của súng mà vẫn giữ phát bắn tức thì [2]. Blend của engine tự thêm ease.
- **Williams, Annand.** Timing và spacing là hai việc: cùng 24 frame, đồng xu đi đều hay có ease cho hai chuyển động khác hẳn;
  spacing là nửa khó, phải học [44] tr. 36–39. Ease làm bằng chia đôi dần về phía pose (0, 1/8, 1/4, 1/2…) [44] tr. 48–51; va
  chạm cứng không có inbetween đi vào [44] tr. 237, 268. Annand: inbetween chỉ có ba kiểu (ease ra, ease vào, đều); nội suy mặc
  định của phần mềm 3D chia đều nên chuyển động trôi, nhũn; khoảng cuối trước va chạm quyết độ mạnh, rộng quá thì pop; động tác
  bật là pop một frame rồi cushion; tỉ lệ chia giữ nguyên ở 24, 30, 60 fps [45] tr. 145–168, 209–211.
- **Lỗi hay gặp.** Auto-ease mọi key (bồng bềnh); tuyến tính mọi nơi (như robot); vọt quá làm chân xuyên sàn.
- **Kiểm.** Graph editor: tiếp tuyến ở đầu hành động và lúc va chạm sắc, kênh chân chạm đất phẳng, pose giữ lắng mà không
  vọt. Rồi xem chỗ tiếp xúc trong engine.

### 2.7 Đường cong (arcs)

- **Là gì.** Đường đi giữa các pose cực là cung. Chèn theo đường thẳng làm mất cốt lõi của hành động [1].
- **2D.** Vẽ cung trên các key, kiểm bằng onion skin. SK: xoay xương tự ra cung, dời xương thì không.
- **3D.** Một spline vừa điều khiển timing vừa điều khiển đường đi thì hành động càng nhanh, cung càng dẹt: thêm breakdown
  [1]. Làm sạch mocap: bỏ những chỗ gãy cung quá lộ (lặp lại là thấy sai), nhưng cung hoàn hảo mọi nơi lại bồng bềnh; đầu
  giật sau cú đấm là chỗ gãy đúng [2].
- **Trong game.** Crossfade nội suy pose chứ không đi theo cung, nên blend có thể tạo đường thẳng. Đòn rất ngắn thì vệt vũ
  khí (VFX) gánh cung [20][38].
- **Williams, Annand.** Breakdown đặt trên cung; breakdown đặt theo đường thẳng làm tay ngắn lại; soi cung bằng cách lần một
  điểm (mắt, cổ tay) qua mọi hình [44] tr. 49, 90–92. Annand: vòng lặp không quay về trên đúng đường đi (trông như tua ngược);
  đòn thẳng (đấm, chỉ) bắt đầu và kết thúc bằng cung; hai bộ phận cùng chiều cùng lúc thì lệch cung hoặc lệch thời gian [45]
  tr. 119–126.
- **Lỗi hay gặp.** Vung vũ khí theo đường thẳng; cổ tay mocap rung; cung gãy ở chỗ nối loop.
- **Kiểm.** Motion trail hoặc onion skin: đường đi của tay, mũi vũ khí, đầu là cung sạch suốt hành động và qua chỗ nối loop.

### 2.8 Hành động phụ (secondary action)

- **Là gì.** Hành động nhỏ sinh ra từ hành động chính, bổ trợ mà không tranh. Biểu cảm mặt giữa một động tác lớn sẽ không
  ai thấy: đặt trước hoặc sau [1].
- **2D.** FbF: vài hình nhỏ (chớp mắt, vẫy tai). SK: swap bộ phận bằng Sprite Resolver [27].
- **3D.** Cooper khuyên dùng clip additive và clip từng phần để chồng phần phụ dài (mệt, biểu cảm) lên clip gameplay ngắn [2].
- **Trong game.** Clip gameplay ngắn, hay bị ngắt. Phần phụ không được che telegraph hay làm sai tầm với.
- **Williams, Annand.** Annand: một cử chỉ phụ mỗi nhịp là đủ, nhiều quá thì mất ý; làm sau khi thân chính đã chạy; tay không
  che mặt [45] tr. 221–228. Williams: phần phụ (tóc, vải, đuôi) làm sau cùng, mỗi phần một lượt riêng [44] tr. 63–67.
- **Lỗi hay gặp.** Phần phụ bake vào clip nền (không dùng lại, không tắt được); nhiễu át ý chính.
- **Kiểm.** Bật tắt A/B trong engine: tắt layer phụ và physics thì hành động chính đọc y như cũ; bật lên không đổi ý của key
  frame.

### 2.9 Nhịp (timing)

- **Là gì.** Số frame và cách chia frame; cho thấy trọng lượng, kích thước, tâm trạng. Dành đủ thời gian, không hơn, cho
  lấy đà, hành động, phản ứng [1]. Cooper coi timing là trung tâm của cảm giác (tốc độ = quãng đường ÷ thời gian), và khuyên
  giữ pose sau cú vung để người chơi kịp thấy [2].
- **2D.** Mỗi hình FbF giữ N frame game. Chạy nhanh cần hình mới mỗi frame [34]; hành động chậm giữ hình lâu hơn [36].
- **3D.** Key ở 30 hoặc 60 fps. Thời gian blend và tốc độ root motion đổi cảm nhận timing.
- **Trong game.** Timing chính là frame data: startup, active, recovery [12]. Sakurai có hẳn một video khuyên học đếm frame
  [7j]. Unity trên iOS và Android mặc định chạy 30 fps: độ mịn timing chỉ còn một nửa so với 60 fps [29].
- **Williams, Annand.** Ones và twos: twos (12 hình mỗi giây) cho hành động thường, ones cho hành động nhanh, chạy, chuyển động
  thưa; nhân vật on twos dưới camera pan on ones thì giật (strobing) [44] tr. 75–79, 346–347. Accent ngắn hơn 4 frame (24 fps)
  không đọc được, muốn đọc rõ cần khoảng 6 [44] tr. 271, 293. Lỗi lớn nhất của người mới là quá nhiều hành động trong quá ít
  thời gian [44] tr. 99. Annand: pose đứng yên hoàn toàn quá khoảng 1 s thì chết, dùng moving hold; frame mỗi nhịp = fps × 60 ÷
  bpm; ra số lẻ thì làm tròn và thử cả hai [45] tr. 155–158, 167–168.
- **Lỗi hay gặp.** Làm ở một fps, ship ở fps khác; bảng frame data và clip thật lệch nhau; blend ăn vào startup.
- **Kiểm.** Đo startup / active / recovery trong engine (debug hitbox, chạy từng frame trên máy) ở fps ship, so với bảng thiết
  kế.
- **Số.** 1 frame = 16,7 ms ở 60 fps, 33,3 ms ở 30 fps. Street Fighter 6 ở 60 fps: đấm nhẹ 4 / 3 / 7, đấm nặng 10 / 5 / 18
  [13]. Active thường chỉ 1–4 frame trên tổng khoảng 30 [12].

### 2.10 Cường điệu (exaggeration)

- **Là gì.** Đẩy cốt lõi của ý lên, không phải méo tuỳ tiện. Giữ cân bằng: một chi tiết cường điệu giữa cảnh tả thực sẽ lạc
  lõng [1].
- **2D.** Hình cực, smear, pop scale. Dead Cells dùng VFX để nói chuyển động, va chạm, sức mạnh [38].
- **3D và mocap.** Cooper: chuyển động thật đưa vào game thường trông chưa đủ thật; đẩy pose, giữ lâu hơn một chút, camera
  càng xa càng phải cộng thêm [2]. Đội Uncharted (GDC 2008) key đè lên mocap để cường điệu được nhiều hơn, cuối cùng khoảng
  40% mocap, 60% keyframe; bắt pose cực, bỏ khoảng dừng [5]. Sakurai: pose trông vô lý khi dừng hình lại đọc tốt khi chạy
  (video *Too Much is Just Right*), và cường điệu để bù phần thông tin bị mất [7f][7g].
- **Trong game.** Tả thực của mocap đối đầu với đọc được ở cỡ màn hình điện thoại. Cooper: mức cường điệu phải thống nhất
  cả project, giữ thống nhất là việc của animation lead [2].
- **Williams, Annand.** Live action vẽ lại đúng từng frame thì trôi, mất trọng lượng; cách sửa là đẩy nhẹ lên xuống và các pose
  cực, rồi biên tập: giữ cái làm rõ ý, bỏ phần còn lại. Mocap giữ được nét duyên nhưng mất trọng lượng nên phải sửa và cường
  điệu; nhân vật người càng gần thật càng lộ lỗi (uncanny valley), nên giữ cách điệu [44] tr. 371–376. Annand: soi tham chiếu
  bằng lưới ở đỉnh đầu và hông để tìm điểm cao, thấp, nghiêng rồi đẩy thêm; chỉ chọn pose kể chuyện, không chép từng frame [45]
  tr. 84–88, 276.
- **Lỗi hay gặp.** Mocap thô trong gameplay; mỗi animator một mức cường điệu.
- **Kiểm.** Xếp các đòn cùng loại cạnh nhau ở camera gameplay (mọi đòn nặng, mọi hit react): chung một mức cường điệu.

### 2.11 Khối vững (solid drawing)

- **Là gì.** Vẽ hình có thể tích, trọng lượng, thăng bằng trong không gian. Cooper chuyển sang 3D thành cơ học cơ thể: trọng
  tâm, thăng bằng, chuỗi phản ứng qua cơ thể; phải biết pose trông ra sao từ mọi góc nên không ăn gian được [2]. Danh sách
  năm 1987 của Lasseter không có nguyên lý này [1].
- **2D.** FbF, pixel art: giữ thể tích, tỉ lệ, cụm pixel sạch [36]. SK: bộ phận phẳng không xoay sang góc khác được, phải lên
  kế hoạch swap bộ phận cho các góc quay.
- **3D.** Trọng tâm nằm trên chân trụ; không bộ phận nào xuyên nhau. Readability của Cooper có mục va chạm, trọng tâm, thăng
  bằng [3].
- **Trong game.** Camera tự do nên không ăn gian góc được [2]. IK và đặt chân lúc chạy có thể phá thăng bằng.
- **Williams, Annand.** Annand: pose đứng mặc định contrapposto (dồn một chân, hông và vai nghiêng ngược nhau); twinning được
  nếu vào và ra bằng pose lệch [45] tr. 64–66. Williams: người ra lệnh hay cử chỉ đối xứng vì đối xứng đọc ra trật tự, uy
  quyền; làm mềm bằng cách cho một tay trễ 4–6 frame hoặc nghiêng [44] tr. 324–325, 343. Trọng tâm và thăng bằng: 2.13.
- **Kiểm.** Ở pose giữ, trọng tâm nằm trong vùng chân đỡ; pose chịu được một vòng xoay camera (turntable trong engine).
  Sprite: onion skin kiểm thể tích.

### 2.12 Cuốn hút (appeal)

- **Là gì.** Thứ người ta thích nhìn, không đồng nghĩa với dễ thương. Thiết kế rối, khó đọc là thiếu cuốn hút [1]. Cooper
  coi đây là sự tin được hơn là vẻ đẹp, dựng từ silhouette rõ và pose đơn giản [2].
- **2D và 3D.** Tránh "twins" (hai tay hoặc hai chân làm y hệt nhau) và mặt đối xứng hoàn toàn [1]. Hình khối đậm, đọc được
  ở cỡ điện thoại.
- **Trong game.** Clip gameplay lặp hàng nghìn lần; Cooper xếp việc lặp lại vào nền tảng Context [3].
- **Williams, Annand.** Không hai người đi giống nhau: nhìn từ sau vẫn đoán được tuổi, sức khoẻ, tâm trạng [44] tr. 103–104,
  161. Diễn: mỗi lúc một ý, biết nhân vật muốn gì và vì sao; có một thoáng suy nghĩ trước khi hành động [44] tr. 314–322.
  Annand: nhân vật khác cỡ phải có trọng lượng, nhịp khác nhau; idle của game cũng phải cho thấy nhân vật đang nghĩ gì [45] tr.
  59, 109–110.
- **Lỗi hay gặp.** Twins; chuyển động quá bận; idle fidget gây khó chịu sau nhiều vòng.
- **Kiểm.** Dừng ở từng key pose: đường hành động (line of action) rõ, không twins, silhouette đọc được. Rồi cho clip lặp
  một lúc lâu, xem có chi tiết nào gây khó chịu.

### 2.13 Trọng lượng và thăng bằng (weight & balance): nguyên lý 13

- **Là gì.** Annand thêm nguyên lý này vào 12 nguyên lý vì thiếu trọng lượng và thăng bằng là lỗi hay gặp nhất của animation 3D
  kém: chuyển động trôi [45] tr. 3–4. Bí quyết Milt Kahl truyền lại: biết trọng lượng nằm ở đâu trên mọi hình, từ đâu tới, đi
  qua đâu, sang đâu [45] tr. 94 · [44] tr. 256.
- **2D và 3D.** Trọng tâm nằm trên chỗ đỡ (bàn chân, tay, gối), không thì nhân vật đang ngã; trọng lượng sang chân trụ rồi chân
  kia mới nhấc; mỗi bước là một lần ngã được bàn chân đỡ lại [45] tr. 94–109. Nặng: trọng tâm thấp, ít chuyển động, đổi hướng
  lâu, nhiều frame chân chạm đất; kéo dài timing chỉ ra quay chậm, không ra nặng [45] tr. 104–106. Trọng lượng chỉ hiện qua hành
  động: sức phải bỏ ra để bắt đầu, đổi hướng, dừng [44] tr. 264–268; nâng vật nặng phải chuẩn bị [44] tr. 256–257.
- **Trong game.** Chân trượt trong clip không bao giờ chấp nhận được; blend giữa các clip thì trượt chút ít là khó tránh. Annand
  khuyên cho idle một thế chân trung tính, gót chạm đất, dễ vào dễ ra, và cho trọng lượng nằm trên cùng một chân ở hai clip
  blend với nhau: transition của game ngắn, frame chân phẳng càng ít thì càng mất trọng lượng [45] tr. 111, 117–118. Rig Generic
  không có Foot IK của Unity, nên phần này nằm ở tay animator.
- **Lỗi hay gặp.** Chạy như rối treo dây; xoay nửa người mà không bước; nhảy lên vật không tốn sức [45] tr. 93–94; nâng tảng đá
  như nâng cục xốp [44] tr. 256–257; chân lết thay vì nhấc lên bước [45] tr. 113–114.
- **Kiểm.** Dừng ở từng key, đường dọc qua trọng tâm rơi vào vùng chân đỡ; che nửa trên màn hình, hông vẫn nhún và trọng lượng
  vẫn dời [45] tr. 286–287; soi chân chạm đất từ nhiều góc [45] tr. 112–113.

---

## 3. Năm nền tảng của game animation

Cooper: 12 nguyên lý vẫn đúng nhưng phải đọc lại cho tương tác, và game cần thêm 5 nền tảng [2][3]. Cột "Nghĩa" là tài liệu
này diễn giải từ các mục con của Cooper.

| Nền tảng | Mục con (Cooper) | Nghĩa |
|---|---|---|
| Feel | phản hồi; quán tính và đà; phản hồi hình ảnh | Nhạy với input mà vẫn có trọng lượng, có phản hồi rõ |
| Fluidity | blend và chuyển tiếp; cycle liền; lắng | Không pop: chuyển sạch, chỗ nối loop vô hình, dừng lắng tự nhiên |
| Readability | pose cho camera game; silhouette; va chạm, trọng tâm, thăng bằng | Đọc ngay được từ camera gameplay, khớp với va chạm |
| Context | khác biệt và đồng nhất; lặp lại; vị trí trên màn hình | Hợp chỗ dùng: khác biệt khi cần, chịu được lặp, hợp kích thước trên màn hình |
| Elegance | thiết kế đơn giản; đáng công; dùng chung và chuẩn hoá | Bộ hệ thống và asset đơn giản nhất, dùng chung được nhiều nhất mà vẫn đủ |

Cooper tóm 12 nguyên lý khi vào game [2]:

- Engine hay không mang scale xương, nên nén giãn đưa vào pose.
- Staging chủ yếu cho cinematic.
- Lấy đà là chuyện thương lượng với game design.
- Theo đà cần một frame trả điều khiển.
- Ưu tiên pose-to-pose.
- Khối vững thành cơ học cơ thể.
- Giữ cường điệu thống nhất là việc của lead.

---

## 4. Kỹ thuật riêng của game

### 4.1 Frame data

- **Startup**: đoạn lấy đà trước khi đòn trúng được. **Active**: đoạn hitbox có tác dụng, thường 1–4 frame. **Recovery**:
  đoạn theo đà tới khi người chơi điều khiển lại [12].
- Cách đếm khác nhau giữa các cộng đồng: Street Fighter tính frame active đầu tiên vào startup [12][13]. Quy chuẩn chọn một
  cách: Startup = số frame **trước** frame active đầu tiên, để tổng clip = startup + active + recovery.
- Đòn mạnh trả giá bằng recovery dài [7c][12][13]. Hitstop không tính vào startup, active hay recovery [12].

### 4.2 Telegraph

- Lấy đà của quái cố ý dài hơn của người chơi [2]; truyền bằng animation cộng sfx, giọng, VFX, rung máy [21].
- Công thức thực hành: thời gian lấy đà = phản xạ của người chơi + thời gian kích hoạt kỹ năng đáp trả + đệm theo độ khó [20].
- Phản xạ thị giác đơn giản đo được trung bình 218–239 ms (200–222 ms sau khi trừ trễ phần cứng) [19]. Game đối kháng cho
  đòn "phản ứng được" khoảng 20–25 frame ở 60 fps (333–417 ms), dành cho người chơi giỏi [12]. Drive Impact của Street
  Fighter 6 có startup 26 frame (433 ms) [13].
- Mobile, người chơi phổ thông: khoảng 250 ms phản xạ + 50–200 ms trễ cảm ứng và màn hình + startup đòn né + đệm, thường ra
  400–600 ms trở lên (suy từ [18][19], cần playtest).
- Williams: lấy đà vừa báo trước cho người xem vừa dồn lực, nên hầu như hành động nào cũng có. Lấy đà quá quen thì nhàm: bất
  ngờ bằng lấy đà đôi, chờ lâu, hay lấy đà to cho hành động nhỏ [44] tr. 274–282. Đòn lớn có thể có lấy đà của lấy đà [45]
  tr. 191.

### 4.3 Cancel và input buffer

- Cancel: bỏ phần recovery để vào thẳng đòn khác [12]. Về phía animation, đó là frame trả điều khiển [2].
- Input buffer: một cửa sổ nhận input bấm sớm và chạy nó ở frame sớm nhất có thể [12]. Celeste giữ lệnh nhảy và nhảy đúng
  frame chạm đất [39]. Vì vậy clip phải có cửa sổ buffer và cancel đánh dấu rõ.

### 4.4 Hitstop

- Dừng hình ngắn cả bên đánh lẫn bên bị đánh lúc trúng, để bán lực va chạm [12]. Smash tăng hitstop theo sát thương [9].
  Sakurai: dùng được cho mọi khoảnh khắc lớn chứ không chỉ cú đánh; dừng ngắn rồi chậm dần về tốc độ thường còn mạnh hơn
  [7h].
- Các kỹ thuật Sakurai nêu cho Smash Ultimate (theo bản tóm tắt [8]): bên bị đánh rung nhiều hơn; không dời hitbox; rung
  ngang khi ở đất, dọc khi trên không; rung tắt dần; kiểm soát lượng hitstop; nội suy vào pose bị đánh; bên đánh vẫn nhúc
  nhích rất nhẹ; rung theo khoảng cách camera [7i].
- Quá nhiều hitstop làm chậm game có nhiều quái [11].
- Williams: cú đánh đọc ở frame đã lệch khỏi điểm chạm, không ở frame vừa chạm; studio thời đầu giữ pose chạm 4 frame và cú
  đánh trông mềm nhũn; búa chạm đe 1 frame rồi bật lại, accent là cú bật [44] tr. 280, 294–296. Rung, run làm bằng xếp lại thứ
  tự hình (stagger: 1, 3, 2, 4…; tắt dần: 1, 17, 2, 16…) [44] tr. 297–300. Annand: game cần trúng đúng một điểm của cú vung,
  nên pose trúng là key [45] tr. 166.

| Nguồn | Hitstop (frame ở 60 fps) |
|---|---|
| Smash Ultimate | floor(0,65 × sát thương + 6) × hệ số, tối đa 30: khoảng 6 ở 1%, 12 ở 10%, 19 ở 20% [9] |
| Smash Melee | floor(sát thương ÷ 3 + 3): 6 ở 10%, 9 ở 20% [9] |
| Beat 'em up của Capcom | Final Fight 6; Captain Commando 8; Knights of the Round 6–7; Warriors of Fate 4–5; The Punisher 6–8 bên đánh, 8–10 bên bị đánh [10] |

Ở 30 fps: chia đôi, làm tròn lên.

### 4.5 Độ trễ phản hồi

| Mốc | Giá trị | Nguồn |
|---|---|---|
| Game 60 fps (đo) | 3–4 frame từ input tới màn hình (50–67 ms); nhắm ≤ 4/60 s | [14] |
| Game 30 fps (đo) | 6–18/60 s; nhắm 100 ms, không tệ hơn 133 ms | [14] |
| Ngưỡng cảm nhận | khoảng 50 ms thấy tức thì; trên 100 ms thấy trễ; 200 ms thấy lì; 240 ms là một vòng nhận, quyết, làm của người | [16] |
| Giới hạn "tức thì" của giao diện | 0,1 s | [17] |
| Màn hình cảm ứng | chạm: trễ nhỏ nhất người cảm nhận được khoảng 69 ms; màn hình thương mại tự có 50–200 ms trễ | [18] |

- Mick West: lực di chuyển phải áp ở frame đầu, không đợi vài phần giây vào clip [15].
- Unity trên iOS, Android mặc định chạy 30 fps khi không đặt `targetFrameRate` [29].

### 4.6 Blend, pose matching, loop

- Pose matching: các cycle trong một blend nên cùng độ dài và bắt đầu cùng pose. Blend đi ↔ chạy hay lỗi vì pose giữa bước
  (passing) của đi là điểm cao nhất, của chạy là điểm thấp nhất [6]. Blend tree của Unity căn chân theo thời gian chuẩn hoá,
  ví dụ chân trái ở 0,0, chân phải ở 0,5 [24].
- Transition Unity tạo bằng tay: Has Exit Time bật, Fixed Duration 0,25 s, Exit Time = 1 − 0,25 ÷ độ dài clip [22].
  Transition do input điều khiển mà bật Has Exit Time thì phản hồi bị trễ [23].
- Inertialization (Gears of War 4, GDC 2018): thay vì trộn hai pose, chỉ tính clip mới và mang vận tốc của clip cũ sang [32];
  Unreal khuyên blend kiểu này dưới 0,4 s [31]. Unity không có sẵn.
- Loop: Loop Pose của Unity chia phần lệch đầu–cuối ra cả clip; đèn loop match xanh khi đầu và cuối khớp [25].
- Ví dụ lệch pha có đo: pack ExplosiveLLC để vòng Strafe lệch vòng Run 0,15–0,4 chu kỳ (so cùng một chân) trong cùng một
  blend tree, và chép một mốc chân cho cả 8 hướng chạy [40].
- Unity xét transition của Any State trước transition của state hiện tại, theo thứ tự trong danh sách [23]. Trigger chỉ tự
  xoá khi có transition dùng nó [41]. Mỗi state trống nằm giữa đường vào hành động tốn một frame [40].
- Annand: game blend nhiều nên cho idle một thế chân trung tính, và khớp chân chịu trọng lượng giữa hai clip [45] tr. 117–118.
  Vòng lặp không quay về trên đúng đường đi; soi chỗ nối bằng cách kéo cycle tới pose down kế tiếp [45] tr. 121, 134–135.
  Williams: cycle dễ trông máy móc, nên nửa sau khác nửa đầu một chút [44] tr. 111, 126, 181, 192.

### 4.7 Root motion và in-place

- Root motion: chuyển động của root mỗi frame được áp vào GameObject. Unity khuyên bake vào pose cho idle, và bake chuyển
  động dọc cho mọi clip trừ nhảy [25].
- In-place: tốc độ clip phải khớp tốc độ nhân vật. Whitaker và Halas đã nhắc: chuyển động tới phải đều, không thì chân "dính"
  [34].
- Quy chuẩn chọn in-place cho mọi clip, code dời nhân vật (quy chuẩn 7.2).
- Có hàm `OnAnimatorMove` trên object có Animator thì Unity thôi tự áp root motion và đưa nó cho hàm đó, kể cả khi Apply
  Root Motion tắt. Root motion Humanoid nhân theo `humanScale` của avatar; `averageSpeed` của clip có thể báo 0 dù clip vẫn
  nhả root motion. Cả ba điều đo trên pack ExplosiveLLC [40].
- Kiểu lai của pack ExplosiveLLC: locomotion và các hành động có quãng dời chạy bằng root motion, code cộng thêm một ít vận
  tốc, và bật / tắt Apply Root Motion lúc khoá di chuyển (không có tác dụng, vì đã có `OnAnimatorMove`). Hai nguồn dời cộng
  nhau là lý do quy chuẩn đòi một nguồn dời [40].
- Annand: game làm cycle đi tại chỗ như trên máy chạy bộ; chân trụ phẳng, trượt về sau đều bằng 2 key dời; kiểm bằng cách cho
  master control tiến trên một layer riêng, xem gót và mũi có đứng yên [45] tr. 128, 133, 137–139. Williams khuyên cho đi vài
  bước thật trước khi dựng cycle [44] tr. 111. Sprite on twos được dời đều mỗi frame thì giật, như nhân vật on twos trên nền
  pan on ones [44] tr. 79.

### 4.8 Chuyển động phụ bằng thủ tục

- Physics constraint của Spine 4.2 điều khiển dời, xoay, scaleX, shearX với quán tính, độ cứng, damping, khối lượng, gió,
  trọng lực [30]. Damped Transform của Unity Animation Rigging làm trễ vị trí và góc so với xương nguồn [28]. Cooper: cho vải,
  tóc chạy physics là cách rẻ để silhouette đẹp hơn [2].
- Thử chuyển động mô phỏng ở cả 30 và 60 fps.

### 4.9 Motion matching

- Simon Clavet, GDC 2016 (For Honor): liên tục chọn pose tốt nhất trong kho mocap sao cho khớp quỹ đạo sắp tới, có trọng số
  cho độ nhạy; cần chống trượt chân [33]. Đòi hỏi kho mocap lớn nên ngoài tầm phần lớn game mobile.

---

## 5. 2D và 3D: cùng nguyên lý, khác công cụ

| | 2D frame-by-frame | 2D skeletal | 3D |
|---|---|---|---|
| Nén giãn | vẽ, smear | scale xương, deform mesh | Generic: scale, blendshape; Humanoid: chỉ bằng pose |
| Lấy đà, timing | số hình, thời gian giữ hình | key, curve | key, curve, thời gian blend |
| Spacing | vẽ theo chart, chia đôi về phía pose | key, favor, tiếp tuyến | curve, tiếp tuyến; nội suy mặc định chia đều nên phải chỉnh |
| Theo đà, chồng chéo | vẽ trễ | lệch key, physics (Spine) | lệch key, spring, Damped Transform, vải |
| Arcs | onion skin | xoay xương ra cung | motion trail |
| Hành động phụ | hình phụ | sprite swap | layer additive, mặt |
| Khối vững | thể tích từng hình | bộ phận phẳng: swap khi đổi góc | xem từ mọi góc |
| Đổi hướng nhìn | vẽ lại từng hướng | swap bộ phận hoặc rig riêng từng hướng | xoay tự do |
| Sửa timing | vẽ lại, đổi thời gian giữ | dời key | dời key |
| Chi phí khi chạy | bộ nhớ texture | deform CPU hoặc GPU | skinning, Animator |

---

## 6. Nguồn cho các luật kỹ thuật của quy chuẩn

Manual Unity bản 6000.3 trừ khi ghi khác. "Cộng đồng" là nguồn không chính thức, đã đối chiếu nhưng nên thử lại ở pilot.

| Mục quy chuẩn | Điều | Nguồn |
|---|---|---|
| 3.1 | Unity mobile mặc định 30 fps | https://docs.unity3d.com/ScriptReference/Application-targetFrameRate.html |
| 5.1 | 1 unit = 1 m; Y lên, +Z tới trước | https://docs.unity3d.com/6000.3/Documentation/Manual/models-preparing.html · https://docs.unity3d.com/6000.3/Documentation/ScriptReference/ModelImporter-bakeAxisConversion.html |
| 5.1 | Humanoid lấy T-pose làm pose tham chiếu, Enforce T-Pose | https://docs.unity3d.com/6000.3/Documentation/Manual/ConfiguringtheAvatar.html |
| 5.3, 5.6 | Tối đa 4 influence; một SkinnedMeshRenderer; thêm 15 xương vào rig 30 xương tốn thêm 50% | https://docs.unity3d.com/6000.3/Documentation/Manual/ModelingOptimizedCharacters.html |
| 5.6 | Manual Unity 5: dưới 30 xương cho mobile | https://docs.unity3d.com/560/Documentation/Manual/ModelingOptimizedCharacters.html |
| 5.4 | Humanoid tốn thêm 30–50% CPU; dùng Generic khi có thể | e-book *Optimize your game performance for mobile, XR, and the web in Unity 6*: https://unity.com/resources/mobile-xr-web-game-performance-optimization-unity-6 |
| 5.4 | Humanoid chỉ xoay (trừ root, hips); twist mặc định 50% | https://docs.unity3d.com/6000.3/Documentation/Manual/MuscleDefinitions.html · https://unity.com/blog/engine-platform/mecanim-humanoids |
| 5.4 | Xương thêm trên Humanoid phải tick trong Mask > Transform | nhân viên Unity: https://discussions.unity.com/t/humanoid-rigs-and-extra-bones/601395 |
| 5.7 | Tuỳ chọn FBX của Blender (Apply Transform thử nghiệm, Apply Scalings…) | https://docs.blender.org/api/current/bpy.ops.export_scene.html · https://docs.blender.org/manual/en/latest/files/import_export/fbx_legacy.html |
| 5.7 | Tắt Add Leaf Bones, bật Only Deform Bones | e-book *The definitive guide to animation in Unity*: https://unity.com/resources/definitive-guide-animation-unity-2022-lts-ebook |
| 5.7 | Maya: đơn vị, tuỳ chọn FBX; Unity không hỗ trợ Rotate Axis | https://help.autodesk.com/cloudhelp/2026/ENU/Maya-Interoperability/files/GUID-FE8DBEAA-C2DD-43B3-9933-4BA4CDDEAA89.htm · https://docs.unity3d.com/6000.3/Documentation/Manual/HOWTO-ImportObjectsFrom3DApps.html |
| 6.1, 6.2 | Thiết lập PSD Importer, Layer ID, đổi kích thước layer không cập nhật sprite rect | https://docs.unity3d.com/Packages/com.unity.2d.psdimporter@12.0/manual/PSD-importer-properties.html · https://docs.unity3d.com/Packages/com.unity.2d.psdimporter@12.0/manual/PSD-importer-SpriteRect.html |
| 6.4 | Skinning Editor cắt còn 4 influence khi lưu; thông số Auto Geometry lưu theo máy | mã nguồn package: https://github.com/needle-mirror/com.unity.2d.animation/tree/13.0.5/Editor/SkinningModule |
| 6.5 | Key Sprite Resolver là bậc thang; không trộn với key sprite trực tiếp | https://docs.unity3d.com/Packages/com.unity.2d.animation@13.0/manual/SpriteSwapIntro.html |
| 6.6 | IK Manager 2D, các solver | https://docs.unity3d.com/Packages/com.unity.2d.animation@13.0/manual/2DIK.html |
| 6.7 | Điều kiện GPU deform, các trường hợp tự về CPU | https://docs.unity3d.com/Packages/com.unity.2d.animation@13.0/manual/SpriteSkin.html |
| 6.7 | 13.0.6 sửa lỗi nháy khi cull và Auto Rebind | https://docs.unity3d.com/Packages/com.unity.2d.animation@13.0/changelog/CHANGELOG.html |
| 6.8 | Aseprite Importer: tag thành clip, thời lượng từng frame, event | https://docs.unity3d.com/Packages/com.unity.2d.aseprite@3.0/manual/ImporterFeatures.html |
| 6.8 | Unity cộng thêm một frame sau key sprite cuối | mã nguồn Aseprite Importer: https://github.com/needle-mirror/com.unity.2d.aseprite/tree/3.0.2/Editor · cộng đồng: https://discussions.unity.com/t/looping-animation-last-keyframe-adds-extra-frame/232158 |
| 6.8 | Pixel Perfect Camera | https://docs.unity3d.com/6000.3/Documentation/Manual/urp/2d-pixelperfect-ref.html |
| 6.9 | Giá và cách render của Spine | https://esotericsoftware.com/spine-purchase · https://esotericsoftware.com/spine-unity-rendering |
| 7.1 | Đèn loop match | https://docs.unity3d.com/6000.3/Documentation/Manual/LoopingAnimationClips.html |
| 7.2 | Root Transform: Bake Into Pose, Based Upon | https://docs.unity3d.com/6000.3/Documentation/Manual/RootMotion.html |
| 7.4 | Event gọi như SendMessage; lỗi "has no receiver" | https://docs.unity3d.com/6000.3/Documentation/Manual/script-AnimationWindowEvent.html |
| 7.4 | Mọi clip trong blend tree đều bắn event, lọc bằng weight | nhân viên Unity: https://discussions.unity.com/t/mecanim-5-blend-tree-animation-event-called-twice/576456 |
| 8.1 | Các kiểu nén clip | https://docs.unity3d.com/6000.3/Documentation/Manual/class-AnimationClip.html |
| 8.1 | Sai số nén mặc định 0,5 | cộng đồng: https://techarthub.com/animation-compression-unity/ |
| 8.1 | Optimize Game Objects; không chạy chung với Animation Rigging | https://docs.unity3d.com/6000.3/Documentation/Manual/FBXImporter-Rig.html · nhân viên Unity: https://discussions.unity.com/threads/optimize-game-object-being-enabled-seems-to-break-animation-rigging.1072826/ |
| 8.3 | Mặc định của transition mới | mã nguồn Unity: https://github.com/Unity-Technologies/UnityCsReference/blob/master/Editor/Mono/Animation/StateMachine.cs · https://docs.unity3d.com/6000.3/Documentation/Manual/class-Transition.html |
| 8.3 | Layer weight 0 được bỏ qua; mẹo hiệu năng Animator | https://docs.unity3d.com/6000.3/Documentation/Manual/MecanimPeformanceandOptimization.html |
| 8.3 | Animator Override Controller | https://docs.unity3d.com/6000.3/Documentation/Manual/AnimatorOverrideController.html |
| 8.4 | Culling Mode | https://docs.unity3d.com/6000.3/Documentation/Manual/class-Animator.html |
| 8.5 | GPU Skinning; Quality > Skin Weights | https://docs.unity3d.com/6000.3/Documentation/ScriptReference/PlayerSettings-meshDeformation.html · https://docs.unity3d.com/6000.3/Documentation/ScriptReference/QualitySettings-skinWeights.html |
| 8.6 | Constraint của Animation Rigging 1.4 | https://docs.unity3d.com/Packages/com.unity.animation.rigging@1.4/manual/ConstraintComponents.html |
| 3.5, 3.7, 7.1, 7.2, 8.3, 8.8 | Số đo trên pack ExplosiveLLC: Exit Time, khoá theo giây, pha chân, root motion qua `OnAnimatorMove`, `averageSpeed`, state trống, state chờ | đo trên Unity 6000.3.16f1 [40] |
| 8.3 | Any State xét trước, theo thứ tự trong danh sách | https://docs.unity3d.com/6000.3/Documentation/Manual/class-Transition.html |
| 8.3 | Trigger tự xoá khi có transition dùng | https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AnimatorControllerParameterType.Trigger.html |
| 8.3 | Freeform Directional / Cartesian; Compute Positions > Velocity XZ | https://docs.unity3d.com/6000.3/Documentation/Manual/BlendTree-2DBlending.html |
| 8.6 | Curve trên clip điều khiển tham số cùng tên | https://docs.unity3d.com/6000.3/Documentation/Manual/AnimationCurvesOnImportedClips.html |
| 5.3 | Automatic Weights (bone heat), Normalize, Clean, Limit Total, Preserve Volume, Envelope; xương tắt Deform không nhận weight | [42] tr. 46–66, 175, 257–261 |
| 5.10 | Roll, IK và pole, Limit Rotation; keyframe, interpolation, handle; va chạm dừng gắt, rơi nhanh hơn lên | [42] tr. 119, 167, 229, 236–240, 287–300 · [43] |
| 5.3, 5.10 | Số đo khi làm lại một chibi cầm rìu và khiên bằng rigkit; biên độ và pose trung tâm của pack ExplosiveLLC đo cùng thước với gate (`tests/measure_clips.py`) | `Skill~/blender-rig-animate/reference/failure-catalog.md`, `rules.md` · [40] |
| 2 (dòng 13), 7.8 | Nguyên lý 13; trọng tâm, chân trụ; nặng, nhẹ; đặt chân; idle có thế chân trung tính khi blend | [45] tr. 3–4, 93–118 · [44] tr. 256–272 |
| 2 (các dòng khác), 7.6 | Timing, spacing; key, extreme, breakdown; chia đôi, favor; pop, cushion; accent; moving hold; overlap, khớp gãy lần lượt; take; stagger; whip | [44] tr. 35–101, 217–255, 285–303 · [45] tr. 145–228 |
| 3.1 | Ones, twos; strobing; không trộn hình giữ 2, 3, 4 frame; đổi số 24 fps ra 30 fps | [44] tr. 71–79, 346–347 · [45] tr. 155–158 |
| 3.2, 3.3 | Lấy đà vô hình; lấy đà ngược chiều, không lớn hơn hành động; lấy đà của lấy đà | [44] tr. 273–284 · [45] tr. 189–197 |
| 3.4, 3.7, 7.5 | Va chạm đọc ở frame đã lệch khỏi điểm chạm; accent cứng, mềm; pose trúng là key | [44] tr. 280, 285–296 · [45] tr. 165–166, 213–219 |
| 3.6, 7.7 | Nhịp đi, chạy (frame mỗi bước); pose của bước; chạy, nhảy; thú bốn chân, chim | [44] tr. 102–216, 327–332, 352–360 · [45] tr. 127–143 |
| 7.1, 7.2 | Soi chỗ nối loop; đường về khác đường đi; chân trụ trượt đều; kiểm cycle tại chỗ; cycle bớt máy móc | [45] tr. 121, 128–139 · [44] tr. 111, 123, 181, 192 |
| 7.4 | Hình không đi sau tiếng, được sớm hơn vài frame | [44] tr. 268, 272, 310–311 |
| 7.9 | Mặt ba vùng; bảy biểu cảm; giữ biểu cảm; chớp mắt; lip sync; cử chỉ đi trước tiếng | [45] tr. 229–258 · [44] tr. 304–326 |
| 7.10 | Tay FK, chân IK; trục xoay bàn chân | [45] tr. 69, 112–113 |
| 7.10 | Mirror, Foot IK của state chỉ chạy với Humanoid | https://docs.unity3d.com/6000.3/Documentation/Manual/class-State.html |
| 9.1–9.4 | Cách làm kết hợp; blocking 3–4 pose mỗi giây; chạy thử mọi bước; góp ý khách quan; checklist cảnh | [44] tr. 61–69, 334–339 · [45] tr. 275–296 |

---

## 7. Nguồn

1. Lasseter, J. "Principles of Traditional Animation Applied to 3D Computer Animation", *Computer Graphics* 21(4), 1987.
   http://graphics.cs.cmu.edu/nsp/course/15-464/Fall05/papers/lasseter.pdf · https://dl.acm.org/doi/10.1145/37402.37407
2. Cooper, J. "The 12 Principles of Animation (In Video Games)", trích sách, 2019.
   https://www.gameanim.com/2019/05/15/the-12-principles-of-animation-in-video-games/
3. Cooper, J. "The Five Fundamentals of Video Game Animation", 2020.
   https://www.gameanim.com/2020/04/04/the-five-fundamentals-of-video-game-animation/
4. *Game Anim: Video Game Animation Explained*, bản 2 (CRC / Routledge, 2021), mục lục.
   https://www.routledge.com/Game-Anim-Video-Game-Animation-Explained/Cooper/p/book/9780367707651
5. Cooper, J. "Uncharted Mocap" (ghi chép bài GDC 2008 của Yates và Simantov). https://www.gameanim.com/2008/04/19/uncharted-mocap/
6. Cooper, J. "Basics: Animation Blending", 2005. https://www.gameanim.com/2005/06/19/blending-the-future-of-non-linear-animation/
7. Sakurai, M. *Masahiro Sakurai on Creating Games* (YouTube; tên video và mô tả):
   (b) Making Lead-ins Instant and Impactful https://www.youtube.com/watch?v=E8DKndKkHw8 ·
   (c) Follow-Throughs Make the Impact https://www.youtube.com/watch?v=cIB0BUe6Ihk ·
   (d) Squashing and Scaling https://www.youtube.com/watch?v=Sm0LAm4sJKc ·
   (e) The Perils of Interpolation https://www.youtube.com/watch?v=oFwamE6Hy04 ·
   (f) Exaggerate to Make Up for Information Loss https://www.youtube.com/watch?v=Ivwt37x-2EU ·
   (g) Too Much is Just Right, qua tóm tắt của GoNintendo https://gonintendo.com/contents/11218-masahiro-sakurai-s-latest-video-details-over-exaggeration-in-animation ·
   (h) Stop for Big Moments! https://www.youtube.com/watch?v=OdVkEOzdCPw ·
   (i) Eight Hit Stop Techniques https://www.youtube.com/watch?v=tycbMSjDDLg ·
   (j) học đếm frame, qua tóm tắt của GoNintendo https://gonintendo.com/contents/16485-sakurai-s-latest-game-dev-video-discusses-on-having-the-skill-to-count-frames ·
   danh mục đủ: https://archive.org/details/masahiro-sakurai-creating-games
8. Nintendo Wire, "This Week In Sakurai (12/5–12/11)" (tóm tắt). https://nintendowire.com/news/2022/12/12/this-week-in-sakurai-12-5-12-11-fine-tuning-hit-stop-and-cheating-the-system/
9. SmashWiki, "Hitlag" (cộng đồng, số lấy từ dữ liệu game). https://www.ssbwiki.com/Hitlag
10. Sicienski, S. "Hitstop in Capcom Beat 'Em Ups" (blog, có đo). https://shane-sicienski.com/blog/blog-post-title-one-55pmn
11. Cột Famitsu số 490 của Sakurai về hitstop (bản dịch, tóm tắt). https://sourcegaming.info/2015/11/11/thoughts-on-hitstop-sakurais-famitsu-column-vol-490-1/
12. Infil, *The Fighting Game Glossary* (cộng đồng). https://glossary.infil.net/
13. Capcom, frame data chính thức của Street Fighter 6 (Ryu). https://www.streetfighter.com/6/character/ryu/frame
14. West, M. "Measuring Responsiveness in Video Games", 2008. https://cowboyprogramming.com/2008/05/30/measuring-responsiveness-in-video-games/
15. West, M. "Programming Responsiveness", 2008. https://cowboyprogramming.com/2008/05/27/programming-responsiveness/
16. Swink, S. *Game Feel* (Morgan Kaufmann, 2008), chương 2. https://www.taylorfrancis.com/books/mono/10.1201/9781482267334/game-feel-steve-swink
17. Nielsen, J. "Response Times: The 3 Important Limits". https://www.nngroup.com/articles/response-times-3-important-limits/
18. Deber, Jota, Forlines, Wigdor, "How Much Faster is Fast Enough?", CHI 2015. https://www.tactuallabs.com/papers/howMuchFasterIsFastEnoughCHI15.pdf
19. Woods và cộng sự, "Factors influencing the latency of simple reaction time", *Frontiers in Human Neuroscience*, 2015. https://doi.org/10.3389/fnhum.2015.00131
20. Kraj, N. "Keys to Combat Design: Anatomy of an Attack", 2020 (blog). https://gdkeys.com/keys-to-combat-design-1-anatomy-of-an-attack/
21. Stout, M. "Enemy Attacks and Telegraphing", 2015. https://www.gamedeveloper.com/design/enemy-attacks-and-telegraphing
22. Mã nguồn C# của Unity, StateMachine.cs. https://github.com/Unity-Technologies/UnityCsReference/blob/master/Editor/Mono/Animation/StateMachine.cs
23. Unity Manual, Animation transitions. https://docs.unity3d.com/Manual/class-Transition.html
24. Unity Manual, Blend Trees. https://docs.unity3d.com/Manual/class-BlendTree.html
25. Unity Manual: https://docs.unity3d.com/Manual/RootMotion.html · https://docs.unity3d.com/Manual/LoopingAnimationClips.html · https://docs.unity3d.com/Manual/class-AnimationClip.html
26. Unity blog "Mecanim Humanoids" (2014) https://unity.com/blog/engine-platform/mecanim-humanoids · Muscle & Settings https://docs.unity3d.com/Manual/MuscleDefinitions.html · HumanPose https://docs.unity3d.com/ScriptReference/HumanPose.html
27. Unity 2D Animation: https://docs.unity3d.com/Packages/com.unity.2d.animation@6.0/manual/SpriteSkin.html · https://docs.unity3d.com/Packages/com.unity.2d.animation@6.0/manual/FFanimation.html
28. Unity Animation Rigging, Damped Transform. https://docs.unity3d.com/Packages/com.unity.animation.rigging@1.1/manual/constraints/DampedTransform.html
29. Unity Scripting API, Application.targetFrameRate. https://docs.unity3d.com/ScriptReference/Application-targetFrameRate.html
30. Spine User Guide: https://esotericsoftware.com/spine-bones · https://esotericsoftware.com/spine-physics-constraints
31. Unreal: https://dev.epicgames.com/documentation/unreal-engine/transition-rules-in-unreal-engine · https://dev.epicgames.com/documentation/en-us/unreal-engine/motion-matching-in-unreal-engine
32. Bollo, D. "Inertialization", GDC 2018. https://media.gdcvault.com/gdc2018/presentations/bollo_david_inertialization_high_performance.pdf
33. Clavet, S. "Motion Matching and The Road to Next-Gen Animation", GDC 2016. https://gdcvault.com/play/1023280/Motion-Matching-and-The-Road
34. Whitaker, H., Halas, J. *Timing for Animation* (1981). https://archive.org/details/timingforanimation
35. Monmouth University, "Movement: Walk Cycle" (bảng timing theo *The Animator's Survival Kit* của Richard Williams). https://animation.monmouth.edu/instruct/animation/walk-cycle/ · Nguồn gốc: [44] tr. 109–110.
36. Schlitter, R. (Slynyrd), Pixelblog 8 và 50 (blog). https://www.slynyrd.com/blog/2018/8/19/pixelblog-8-intro-to-animation · https://www.slynyrd.com/blog/2024/5/24/pixelblog-50-human-walk-cycle
37. Mã nguồn Aseprite, sprite.cpp (thời lượng frame mặc định 100 ms). https://github.com/aseprite/aseprite/blob/main/src/doc/sprite.cpp
38. Vasseur, T. "Using a 3D pipeline for 2D animation in Dead Cells", 2018. https://www.gamedeveloper.com/production/art-design-deep-dive-using-a-3d-pipeline-for-2d-animation-in-i-dead-cells-i-
39. Thorson, M. "Celeste & Forgiveness". https://www.maddymakesgames.com/articles/celeste_and_forgiveness/index.html
40. `GameAnimation_PackExplosiveLLC.md`: đọc và đo *RPG Character Mecanim Animation Pack FREE* 2.5.2 (Explosive, Asset
    Store id 65284) trên Unity 6000.3.16f1, 2026-09-28. Tài liệu gốc của pack: http://demo.explosive.ws/RPGCharacterMecanimAnimationPackReadMe.pdf ·
    http://demo.explosive.ws/RPGCharacterMecanimAnimationPackComponentAPIReference.pdf
41. Unity Scripting API, AnimatorControllerParameterType.Trigger. https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AnimatorControllerParameterType.Trigger.html
42. Blender Foundation, *Blender Reference Manual, Volume 3: Painting and Sculpting, Rigging, Animation, and Physics*
    (bản in manual Blender 2.77, 2016). Khái niệm còn đúng ở Blender 4.x, tên menu đã đổi. Manual hiện hành:
    https://docs.blender.org/manual/en/latest/animation/index.html · https://docs.blender.org/manual/en/latest/animation/armatures/index.html
43. Ryan King Art, "Animation for Beginners! (Blender Tutorial)", YouTube, 2022. https://www.youtube.com/watch?v=CBJp82tlR3M
44. Williams, R. *The Animator's Survival Kit*, bản mở rộng (Faber and Faber, 2009). ISBN 0-571-23834-7. Số trang là trang in;
    mọi số frame trong sách tính ở 24 fps.
45. Annand, J. *Animation Craft for 3D and 2D Animators* (CRC Press, 2025). ISBN 978-1-032-42239-8 · DOI 10.1201/9781003361893 ·
    tài liệu kèm: https://www.routledge.com/9781032422398. Số trang là trang in; ví dụ trong sách làm bằng Maya ở 24 fps.
