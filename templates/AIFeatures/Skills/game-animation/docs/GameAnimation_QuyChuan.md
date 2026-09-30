# Quy chuẩn Rigging và Animation (2D, 3D)

Phiên bản 0.1.9 (nháp) · 2026-09-30 · Module có tool đổi tên xương Mixamo (5.9) và rigkit (5.10); còn lại quy chuẩn đi trước, tool và
gate làm sau (như UI Motion). 0.1.1 thêm những gì học được khi mổ pack ExplosiveLLC (`GameAnimation_PackExplosiveLLC.md`):
bố cục Animator Controller, layer và state chờ, root motion, event, IK tay, dùng pack animation mua sẵn (8.8). 0.1.2: nguồn
2D là file `.psd`, không dùng `.psb`; file `.psd` phải chọn PSD Importer (6.2). 0.1.3: rig, skin, pose, animate bằng Blender
headless có gate đo được (rigkit và skill `blender-rig-animate`, 5.10); luật skin rút từ lần làm lại một nhân vật chibi (5.3).
0.1.4: tay nghề từ hai sách, Richard Williams (*The Animator's Survival Kit*) và Jonathan Annand (*Animation Craft for 3D and
2D Animators*): nguyên lý 13 trọng lượng và thăng bằng (2), nhịp đi, chạy lấy từ nguồn gốc (3.6), pose, timing, spacing, đi,
chạy, nhảy, đặt chân, mặt và lời thoại (7.6–7.10), quy trình và cách duyệt (9), gate A-20 … A-22. 0.1.5: gate pose trung
tâm của rigkit khớp A-22 (Idle theo bộ, nhóm Hit được lệch frame đầu). 0.1.6: dùng chung cho mọi project: quy chuẩn,
module và hai skill (`game-animation`, `blender-rig-animate`, nằm trong `Skill~` của module) không mang thông tin riêng
của game nào; điều riêng của một game nằm trong file của game đó (brief 9.2, spec rigkit của từng nhân vật, 5.10).
0.1.7: rigkit có lò xo theo đà (follow-through, 7.6.9), gate vật cứng xuyên sàn, verify_fbx đo đúng đạo cụ rời tay (5.10).
0.1.8: gate của rigkit lấy chiều cao nhân vật đúng khi da mềm không phủ cả người (5.10). 0.1.9: skill `game-animation`
mang bản chép quy chuẩn này (`docs/`), cài skill một mình vẫn đọc được luật, không cần cài module.
Áp dụng cho animation trong thế giới game (nhân vật, quái, vật thể, xe), cả 2D và 3D, trên Unity 6000.3, URP. UI không
thuộc tài liệu này: UI dùng module UI Motion (`UIMotion_QuyChuan.md`).

Người đọc: rigger, animator, tech art, game design, dev. Mục có **[RIG]** là việc của rigger, **[ANIM]** của animator,
**[TA]** của tech art, **[GD]** của game design, **[DEV]** của dev; không ghi thì là việc chung. Vì sao có các luật này:
`GameAnimation_NguyenLy.md` (12 nguyên lý và nguyên lý 13 áp vào game 2D và 3D, kèm nguồn).

**Số trong tài liệu là mặc định để bắt đầu.** Số có nguồn (manual Unity, sách, thông lệ ngành) ghi nguồn trong tài liệu
nguyên lý. Số ghi *(đề xuất)* là điểm xuất phát chưa có nguồn chính thức. Số ghi *(đo)* đo trên Unity 6000.3.16f1 với pack
ExplosiveLLC, trong editor (`GameAnimation_PackExplosiveLLC.md` mục 5). Số lấy từ sách làm phim (Williams, Annand) tính ở
24 fps; quy chuẩn đã đổi ra 30 fps và ghi số gốc bên cạnh khi cần (3.1). Chưa số nào đo trên project của team: pilot đầu
tiên sẽ hiệu chỉnh (mục 12.3), như UI Motion đã hiệu chỉnh theo game pilot của nó. Quyết định đã chốt với team (Blender, Unity 2D
Animation, clip tại chỗ, T-pose, mood và combat theo loại game, mocap): mục 12.1.

| Bạn là | Đọc trước |
|---|---|
| Rigger | 0, 4, 5 (3D) hoặc 6 (2D), 7.10, 11 |
| Animator | 0, 2, 3, 7 (tay nghề: 7.6–7.9), 9 |
| Tech art | 4, 5.7, 5.8, 5.10, 6, 8, 10 |
| Game design | 0, 2.1, 2.2, 3, 7.2–7.5, 9.2, 12 |
| Dev | 7.2, 7.4, 7.5, 8.3–8.6 |
| Người dùng pack animation mua sẵn | 5.8, 8.8, `GameAnimation_PackExplosiveLLC.md` |

---

## 0. Sáu nguyên tắc gốc

1. **Gameplay quyết timing, animation quyết cảm giác.** Lúc đòn bắt đầu có tác dụng, lúc nhân vật cử động lại được, tốc
   độ di chuyển là số của game design (frame data, mục 3). Animator làm đẹp trong khung đó. Muốn đổi khung thì đổi cùng GD.
2. **Phản hồi trước, đẹp sau.** Nhân vật người chơi đổi pose ngay khi có input. Lấy đà dài là việc của quái (telegraph),
   không phải của người chơi.
3. **Đọc được ở camera thật, trên máy thật.** Duyệt pose ở góc và khoảng cách camera gameplay, trên màn hình nhỏ nhất đang
   hỗ trợ. Hình bóng (silhouette) đọc được thì chi tiết mới có nghĩa.
4. **Mọi clip đều nối được.** Loop khớp đầu cuối, chuyển trạng thái không giật, bị ngắt ở đâu cũng không lộ. Clip đẹp khi
   xem riêng mà nối xấu là chưa xong.
5. **Rig theo chuẩn, không theo người.** Cùng đơn vị, hướng, pose gốc, tên xương. Có chuẩn thì mới dùng chung clip, thay
   model, và cho tool kiểm tự động.
6. **Duyệt trong engine, thiếu thì không qua.** Mỗi mốc duyệt xem trong Unity, không chỉ trong phần mềm dựng. Rig và clip
   chưa đạt Định nghĩa xong (mục 11) thì sửa, hoặc ghi nợ kèm lý do.

---

## 1. Thuật ngữ

| Từ | Nghĩa |
|---|---|
| Rig | Xương + skin + bộ điều khiển của một model. Phần vào Unity chỉ gồm xương deform và socket |
| Xương deform | Xương có weight lên mesh. Xương điều khiển (control, IK helper) ở lại file nguồn |
| Socket | Xương không deform, làm điểm gắn vũ khí, VFX, camera (`Socket_Hand_R`) |
| Archetype | Nhóm nhân vật dùng chung một cây xương (người nam, người nữ, thú 4 chân). Cùng archetype thì dùng chung clip |
| Bind pose | Pose lúc skin: T-pose (3D), pose trung tính (2D) |
| Influence | Số xương cùng kéo một đỉnh mesh |
| Clip | Một đoạn animation (`AnimationClip`) |
| Cycle | Clip lặp, đầu và cuối nối liền (idle, đi, chạy) |
| Root motion / in-place | Clip tự dời nhân vật bằng xương root / clip đứng tại chỗ, code dời nhân vật |
| Blend, transition | Trộn hai clip khi chuyển trạng thái |
| Blend tree | Trộn nhiều clip theo tham số (tốc độ, hướng) |
| Layer, Avatar Mask | Lớp animation chồng nhau; mask chọn phần cơ thể lớp đó điều khiển |
| Additive | Clip cộng thêm lên clip nền (thở, giật mình khi trúng đòn) |
| Sub-state machine | State machine lồng trong state machine khác; mỗi bộ, mỗi nhóm clip một cái (8.3) |
| State chờ | State không làm gì của một layer phụ: layer đứng ở đó khi không có hành động (8.3) |
| Write Defaults | Tuỳ chọn của state: ghi giá trị mặc định cho mọi thuộc tính mà motion của state không key |
| Bộ (stance) | Bộ clip theo vũ khí đang cầm: tay không, kiếm hai tay… Mỗi bộ một moveset, cùng cách điều khiển (4.3, 8.3) |
| Frame data | Startup (lấy đà) / Active (có tác dụng) / Recovery (hồi) của một hành động, đếm bằng frame |
| Telegraph | Phần lấy đà của quái, đủ dài và rõ để người chơi kịp phản ứng |
| Cancel window | Đoạn trong recovery mà input mới được ngắt clip |
| Input buffer | Input bấm sớm được giữ lại, chạy ngay khi cửa sổ mở |
| Hitstop | Dừng hình vài frame lúc đòn trúng để cú đánh có lực |
| Event | `AnimationEvent`: clip gọi hàm của code ở một frame (bước chân, sfx) |
| Key / extreme / breakdown / inbetween | Pose kể chuyện / pose đổi hướng, chạm, lấy đà / pose giữa hai extreme, quyết định cung và cảm giác của đoạn chuyển / khung chèn còn lại (7.6) |
| Blocking / spline / polish | Ba mốc làm clip: key, extreme, breakdown giữ nguyên (stepped) → nội suy, rút gọn key → hoàn thiện (9.1) |
| Timing / spacing | Frame nào các mốc xảy ra (chạm, va chạm, đổi hướng) / các vị trí liền nhau cách nhau bao xa: dày là chậm, thưa là nhanh (7.6) |
| Ease, favor | Ease (slow in / out, cushion): vị trí dày dần khi tới pose hoặc khi rời pose. Favor: breakdown, inbetween nằm gần một pose hơn pose kia, không ở giữa |
| Accent | Điểm nhấn của chuyển động. Cứng: vọt qua pose rồi bật lại; mềm: trôi tiếp rồi lắng (7.6) |
| Moving hold | Giữ pose mà vẫn trôi rất nhẹ tới một pose mạnh hơn, không đứng chết (7.6) |
| Take | Phản ứng giật mình, phát hiện: lấy đà (nén) → accent (giãn) → lắng vào pose để đọc (7.6) |
| Overlap / follow-through | Các bộ phận nhân vật tự điều khiển lệch thời gian nhau / phần bị kéo theo (tóc, vải, đuôi, thịt) chạy tiếp rồi lắng. Khác recovery của frame data (3.7) |
| Line of action / path of action | Đường cong chạy dọc thân trong một pose / đường một bộ phận đi giữa các pose |
| Twinning | Hai tay hoặc hai chân làm y hệt nhau, đối xứng, cùng lúc |
| Contact / down / passing / up | Bốn pose của một bước đi, chạy: gót chạm đất / người thấp nhất, nhận trọng lượng / chân sau lướt qua chân trụ / người cao nhất, đẩy đi (7.7) |
| Bước | Từ contact của chân này tới contact của chân kia. Cycle đi, chạy = 2 bước |
| Trọng tâm, chân trụ | Điểm cân bằng của cả người, gồm vật đang cầm / chân đang đỡ trọng lượng (7.8) |
| Pose trung tâm | Pose idle của một bộ; clip hành động bắt đầu và kết thúc ở pose này để nối mượt (7.8) |
| On ones / on twos | 2D: mỗi hình giữ 1 / 2 frame ở 24 fps, tức 24 / 12 hình mỗi giây (3.1) |
| Humanoid / Generic | Hai kiểu rig của Unity. Humanoid retarget được giữa các cây xương người khác nhau; Generic cho mọi thứ còn lại |
| PSD, Sprite Skin, Sprite Library | File Photoshop nhiều layer làm nguồn 2D, import bằng PSD Importer (6.2) / component deform sprite theo xương / bảng sprite để thay (swap) |
| PPU | Pixels Per Unit: số pixel art ứng với 1 unit Unity |
| ROM | Range of motion: clip xoay mọi khớp tới giới hạn để thử skin |
| Mood | Cảm giác chung của một game: nhanh hay chậm, nảy hay mềm, mạnh hay nhẹ. Dùng chung trục với Profile của UI Motion (2.1) |

---

## 2. Từ 12 nguyên lý ra luật

Giải thích từng nguyên lý, cách áp vào 2D và 3D: `GameAnimation_NguyenLy.md`. Bảng dưới chỉ giữ phần thành luật. Dòng 13 là
nguyên lý Jonathan Annand thêm vào 12 nguyên lý (*Animation Craft for 3D and 2D Animators*, 2025): thiếu trọng lượng và thăng
bằng là lỗi hay gặp nhất của animation 3D kém. Cách làm cụ thể (spacing, đi, chạy, trọng lượng, mặt, rig cần có): 7.6–7.10.

| # | Nguyên lý | Luật | Kiểm bằng |
|---|---|---|---|
| 1 | Nén và giãn (squash & stretch) | Giữ thể tích (tích scale ba trục ≈ 1). Mức dùng theo phong cách (2.1). Mỗi nhân vật một biên độ cố định (pose nén nhất, giãn nhất, 7.10), luôn so với pose trung tính, không nén chồng lên pose đang nén. Vật cứng (đồng xu, kiếm, giáp) không nén: trọng lượng đọc bằng spacing (7.6). Rig 3D không mang scale (Humanoid) thì nén giãn bằng pose: co lại, duỗi ra; nhân vật cứng (giáp) thì bằng khớp gãy lần lượt | Pose nén, pose giãn trong bản build có cùng "khối lượng" với pose nghỉ; nén ở frame va chạm cảm được mà không "loé" |
| 2 | Chuẩn bị (anticipation) | Ngược chiều hành động, không lớn hơn hành động (trừ khi cố ý gây cười). Người chơi: pose lấy đà hiện ngay frame đầu sau input, vào thẳng (không ease), ngắn, trong ngân sách 3.2; lấy đà "vô hình" 1–2 frame ngược chiều là đủ cho độ bật. Quái: lấy đà dài và rõ (telegraph, 3.3), các đòn khác nhau ngay từ đầu | Đếm frame từ input (người chơi) hoặc từ tín hiệu đầu tiên (quái) tới frame active |
| 3 | Dàn dựng (staging) | Pose chính đọc được chỉ bằng silhouette, ở camera gameplay, trên màn hình nhỏ nhất. Nhiều nhân vật cùng lúc: không gì được át telegraph và phản hồi. Đổi biểu cảm trước hoặc sau một động tác lớn, không đổi giữa chừng; tay, đạo cụ không che mặt | Chụp màn hình trong game, tô đen, xem ở kích thước thật; camera tự do thì xem từ nhiều góc |
| 4 | Làm thẳng và pose-to-pose | Mặc định pose-to-pose: blocking là key, extreme, breakdown (≥ 3–4 pose mỗi giây), duyệt trong engine rồi mới làm mượt; key thưa để đổi timing chỉ cần dời key. Trong khung pose đã duyệt thì làm từng phần: thân trước, rồi tay, mặt, tóc và vải sau cùng (9.1). Làm thẳng cả clip cho lửa, khói. Mocap phải làm sạch | Có mốc duyệt blocking (9.1); blocking ở stepped đã đọc được |
| 5 | Theo đà, chồng chéo (follow through & overlap) | Overlap: bộ phận dẫn đi trước, khớp sau trễ lần lượt (vai → khuỷu → cổ tay → ngón). Theo đà: tóc, vải, đuôi trễ ≥ 2 frame, mỗi đốt xa hơn trễ thêm ≥ 1 frame, chạy tiếp khi thân dừng rồi lắng (7.6); cần phản ứng với di chuyển thì dùng spring / physics. Về gameplay, theo đà của cả người nằm trong recovery: đánh dấu frame trả điều khiển (cancel) trước khi hết clip, cắt ở đó không lộ. Đòn mạnh trả giá bằng recovery dài | Không frame nào mọi bộ phận cùng dừng (trừ accent cố ý cùng chạm); không hai bộ phận cùng chiều cùng lúc; cancel ở frame sớm nhất blend không pop |
| 6 | Chậm vào, chậm ra (slow in / out) | Chỉ ba kiểu inbetween: ease ra, ease vào, đều (7.6); ease làm bằng chia đôi (1/2, 1/4, 1/8). Đầu hành động của người chơi rời pose ngay, không ease ra; cảm giác nặng nằm ở phần lắng (settle). Va chạm (đánh trúng, chạm đất) vào nhanh dần, dừng gắt: khoảng cuối trước lúc chạm rộng nhất, rộng quá thì pop. Điểm dừng tự nhiên có ease | Graph editor: tiếp tuyến ở đầu hành động và lúc va chạm sắc; không đoạn thẳng đều (trừ vật máy móc); không vọt quá xuyên sàn; chơi ngược clip, chỗ dừng và chỗ bắt đầu vẫn gọn |
| 7 | Đường cong (arcs) | Tay, chân, đầu, vũ khí đi theo cung, kể cả qua chỗ nối loop; breakdown nằm trên cung. Đường về khác đường đi (vòng lặp, vung tay, bật lại sau accent). Đòn thẳng (đấm, chỉ) bắt đầu và kết thúc bằng cung. Nhảy: code tính cung, pose lên / đỉnh / rơi khớp thời điểm của code. Đòn quá ngắn thì vệt vũ khí (VFX) gánh cung | Motion trail / onion skin không gãy khúc, không giật, không đi rồi về trên cùng một đường |
| 8 | Hành động phụ (secondary) | Phần phụ không che hành động chính, telegraph, thông tin gameplay; mỗi nhịp một cử chỉ phụ, làm sau khi thân chính đã duyệt. Thở, chớp mắt, idle break làm bằng layer additive hoặc clip riêng, không bake vào clip nền | Tắt phần phụ, hành động chính vẫn đọc y như cũ |
| 9 | Nhịp (timing) | Theo frame data của GD (mục 3), đo ở đúng fps game chạy. Timing khác spacing: frame data chốt timing, animator lo spacing (7.6). Blend không ăn vào startup. Tốc độ clip khớp tốc độ di chuyển thật. Pose cần đọc giữ ≥ 8 frame (250 ms); nặng, nhẹ làm bằng spacing, không bằng tốc độ phát (7.8) | Đếm frame trong engine; chạy ở tốc độ gameplay nhìn chân có trượt không |
| 10 | Cường điệu (exaggeration) | Đẩy pose theo khoảng cách camera và kích thước màn hình. Mocap, clip mua phải có lượt cường điệu: đẩy cao thấp của hông và đầu, cung, độ nghiêng; cắt đoạn thừa; vẫn trong giới hạn giải phẫu và phong cách của nhân vật. Bám mocap từng frame thì chuyển động trôi, mất trọng lượng. Một mức cho cả game, animation lead giữ | Xếp các clip cùng loại cạnh nhau ở camera gameplay, trên máy; so với mocap thô, cao thấp và pose cực đã lớn hơn |
| 11 | Khối vững (solid drawing / posing) | Có trọng lượng (dòng 13), không xuyên, skin không vỡ. Pose đứng mặc định contrapposto: dồn lên một chân, hông và vai nghiêng ngược nhau. Tránh twinning; đối xứng có chủ ý (ra lệnh, ăn mừng, đòn hai tay) thì lệch một bên 5–8 frame hoặc nghiêng, vào và ra bằng pose lệch. Camera tự do nên không ăn gian góc | Xoay camera một vòng quanh pose trong engine; clip ROM |
| 12 | Cuốn hút (appeal) | Tính cách hiện ở idle và di chuyển: mỗi nhân vật một kiểu đi, một nhịp riêng; hai nhân vật khác cỡ không đi giống nhau. Hình khối rõ; điều khiển thấy "đã". Chịu được lặp: không chi tiết nào gây khó chịu sau nhiều vòng | Duyệt cùng art lead và GD; cho clip lặp lâu |
| 13 | Trọng lượng và thăng bằng (weight & balance) | Mọi frame biết trọng tâm ở đâu: trên chỗ đỡ (bàn chân, tay, gối), không thì đang ngã, và ngã có chủ đích (chạy, lao). Trọng lượng sang chân trụ rồi chân kia mới nhấc. Nặng: trọng tâm thấp, biên độ nhỏ, đổi hướng lâu, nhiều frame chân chạm đất. Chân chạm đất không trượt (7.8) | Dừng ở từng key: đường dọc qua trọng tâm rơi vào vùng chân đỡ; che nửa trên màn hình, hông vẫn nhún, trọng lượng vẫn dời |

### 2.1 Mood theo loại game **[GD] [ANIM]**

Diễn theo mood của từng game. Mood dùng chung 5 trục và preset với Profile của UI Motion (`UIMotion_QuyChuan.md` mục 2.1),
để nhân vật và UI của một game cùng một cảm giác. Mỗi game chọn một preset (hoặc chỉnh trục) và ghi vào brief của game;
mọi clip của game theo đó. Game đã chỉnh Profile riêng cho UI thì lấy đúng các trục đó.

| Trục | 0 | 1 | Với animation nhân vật |
|---|---|---|---|
| Tempo | chậm | nhanh | nhịp chung, thời gian blend (3.5), tốc độ idle. Frame data vẫn theo GD |
| Bounce | mềm | nảy | overshoot khi tới pose, nén giãn, nảy khi tiếp đất |
| Impact | nhẹ | mạnh | độ cường điệu của pose va chạm, hitstop và rung (3.4) |
| Liveliness | tĩnh | sống | biên độ thở, số idle break, hành động phụ |
| Softness | sắc | mềm | ease, thời gian lắng (settle) |

| Mood | Tempo | Bounce | Impact | Liveliness | Softness | Combat | Diễn thế nào |
|---|---|---|---|---|---|---|---|
| Action | 0.90 | 0.85 | 0.90 | 0.50 | 0.10 | có | nhanh, gắt; pose va chạm đẩy mạnh; lắng ngắn; hitstop đủ mức |
| Midcore | 0.55 | 0.60 | 0.55 | 0.50 | 0.40 | có (RPG, chiến thuật) | vừa phải; nén giãn chủ yếu lúc va chạm, tiếp đất |
| Puzzle | 0.60 | 0.50 | 0.35 | 0.40 | 0.50 | không | vui, gọn; tương tác rõ, nảy nhẹ |
| Cozy | 0.20 | 0.10 | 0.10 | 0.80 | 0.90 | không | chậm, mềm; idle sống động, nhiều idle break |

Game không combat chỉ có animation hoạt động thường: bỏ telegraph (3.3), hitstop (3.4) và phần đánh nhau của 7.5; mọi
phần còn lại vẫn áp.

Phong cách hình quyết kỹ thuật, mood quyết mức độ. Pixel art nén giãn bằng vẽ vài pixel (6.8). 3D tả thực gần như không nén
giãn (chỉ cơ, mặt), cường điệu chủ yếu bằng timing.

### 2.2 Năm nền tảng của game animation

12 nguyên lý đến từ phim. Game cần thêm 5 nền tảng (Jonathan Cooper, *Game Anim*):

| Nền tảng | Gồm (theo Cooper) | Luật liên quan |
|---|---|---|
| Feel (cảm giác điều khiển) | phản hồi ngay; quán tính và đà; phản hồi hình ảnh | 0.2, 3.2, 7.5 |
| Fluidity (mượt) | blend và chuyển tiếp; cycle liền; lắng | 0.4, 3.5, 7.1 |
| Readability (đọc được) | pose cho camera game; silhouette; va chạm, trọng tâm, thăng bằng | 0.3, 3.3 |
| Context (ngữ cảnh) | khác biệt khi cần; chịu được lặp; hợp kích thước trên màn hình | 7.3, 9.2 |
| Elegance (gọn) | thiết kế đơn giản; đáng công; dùng chung và chuẩn hoá | 5.2, 5.4, 8.3 |

---

## 3. Timing và frame data **[GD] [ANIM]**

Game có combat (Action, RPG, Midcore) dùng cả mục này. Game không combat (Cozy, Puzzle) bỏ 3.3 và 3.4; 3.2 vẫn áp cho mọi
input của người chơi (chạm để đi, chạm để tương tác), và bảng frame data (3.7) chỉ ghi hành động có mốc gameplay.

### 3.1 fps

| Loại | fps khi làm | Ghi chú |
|---|---|---|
| 3D, 2D skeletal | 30 | Unity nội suy giữa các key nên vẫn mượt khi game chạy 60 fps |
| Game đối kháng, nhịp điệu | 60 | frame data tính bằng 1/60 s |
| 2D frame-by-frame HD | 12, tức on twos (mỗi hình khoảng 83 ms) | giữ lâu hơn khi cần (hold); chạy, lướt, đoạn nhanh hoặc vị trí cách xa nhau: on ones (24 hình mỗi giây, khoảng 42 ms) hoặc thêm hình chèn, smear |
| Pixel art | thời lượng riêng từng hình, gốc 100 ms (mặc định của Aseprite) | dưới khoảng 10 hình mỗi giây thì mắt không còn thấy chuyển động liền |

Unity trên iOS và Android mặc định chạy **30 fps** khi game không đặt `targetFrameRate`. Frame data trong tài liệu tính ở
30 fps (1 frame = 33 ms). Duyệt timing ở đúng fps game chạy, trên máy thật.

Số trong sách làm phim (Williams, Annand) tính ở 24 fps: nhân 1,25 ra frame ở 30 fps, chia 2 ra số hình on twos. Tỉ lệ spacing
(chia đôi, một phần ba, favor) giữ nguyên ở mọi fps. Ra số lẻ thì làm tròn và thử cả hai (7 hay 8 frame), hoặc xen kẽ hai giá
trị (15, 16, 15, 16) để bám nhịp.

2D frame-by-frame:

- Trong một chuyển động, không trộn lẫn hình giữ 2, 3, 4 frame: nhìn giật cục (Williams). Giữ khác nhau giữa các đoạn thì
  được.
- Sprite on twos được code dời đều dưới camera bám là đúng trường hợp "nhân vật on twos, nền pan on ones" mà Williams gọi là
  strobing. Giữa hai lần đổi hình, bàn chân đang chạm đất trượt theo nhân vật rồi giật về. Clip di chuyển (Loco, lướt) phải
  kiểm trên máy; giật thì clip đó lên on ones (12.2).

### 3.2 Phản hồi của nhân vật người chơi

Từ lúc chạm tới lúc màn hình đổi, game 30 fps nên nhắm 100 ms, không tệ hơn 133 ms. Màn hình cảm ứng tự thêm 50–200 ms.
Trên khoảng 100 ms người chơi bắt đầu thấy trễ, 200 ms thấy lì. Máy và engine đã ăn phần lớn ngân sách đó, nên animation
**không được thêm frame chờ**: pose đổi ở frame đầu sau input; lực di chuyển và lực nhảy áp ở frame đầu (code), không đợi
tới giữa clip.

| Hành động | Ngân sách *(đề xuất)* | Ghi chú |
|---|---|---|
| Mọi input | pose đổi ở frame kế tiếp | vào thẳng pose lấy đà, không ease ra khỏi pose cũ |
| Bắt đầu đi / chạy | blend ≤ 0,15 s, không lấy đà | |
| Nhảy | rời đất ở frame đầu hoặc thứ hai | pose ngồi lấy đà cực ngắn, hoặc bỏ |
| Đòn nhẹ | startup ≤ 100 ms (≤ 3 frame) | tham chiếu: đấm nhẹ của Street Fighter 6 là 4 frame ở 60 fps |
| Đòn nặng, kỹ năng | startup ≤ 300 ms | chậm hơn phải là chủ ý của GD, và có tín hiệu trong lúc lấy đà |
| Né, lướt | bất tử bắt đầu ≤ 100 ms | |
| Cancel | recovery của đòn nhẹ cancel được sang né | đòn mạnh trả giá bằng recovery dài |

Lấy đà mà không trễ (Williams, Annand):

- Lấy đà "vô hình": 1–2 frame ngược chiều, nhanh tới mức không thấy mà chỉ cảm được độ bật. Frame đó chính là pose đổi ở frame
  kế tiếp, nên không tốn thêm startup.
- Spacing hợp phản hồi: rời pose đầu ngay, lắng vào pose sau (bật ra rồi ease vào). Động tác bật (đá, lướt): từ pose lấy đà nhảy
  sang pose khác hẳn trong 1 frame rồi cushion (7.6).
- Cảm giác nặng đặt ở lúc tiếp đất, nén, recovery; không đặt ở lấy đà.
- Bắt đầu đi, chạy: dồn trọng lượng trùng với bước đầu, không dồn trước rồi mới bước.
- Nhịp nghĩ trước khi phản ứng (Annand gọi là response time) chỉ dành cho NPC, quái (7.6), không dành cho input của người chơi.

### 3.3 Telegraph của quái

- Công thức: telegraph ≥ phản xạ (khoảng 250 ms) + trễ cảm ứng và màn hình (50–200 ms) + startup đòn né của người chơi +
  đệm theo độ khó.
- Mặc định *(đề xuất, chỉnh bằng playtest)*: đòn quái thường ≥ 500 ms; đòn mạnh, boss 600–1000 ms; đòn nhanh (< 400 ms) chỉ
  khi đã có dấu hiệu từ trước (mẫu đòn, combo quen). Game đối kháng cho đòn "phản ứng được" 333–417 ms, nhưng đó là người
  chơi giỏi dùng tay cầm, không phải người chơi phổ thông trên màn hình cảm ứng.
- Telegraph = pose + tín hiệu thêm (loé, âm thanh, giọng, vệt, rung máy). Các đòn khác nhau phải khác nhau ngay từ đầu lấy đà.
- Game dễ, game cho trẻ: dài hơn. Telegraph ghi vào bảng frame data như startup.
- Lấy đà của telegraph theo luật của phim (Williams, Annand):
  - ngược chiều đòn, chậm và nhẹ hơn đòn;
  - không lớn hơn đòn, trừ đòn giả hoặc đòn gây cười mà GD chủ ý;
  - ease vào pose lấy đà (khởi đầu khoảng 4 frame) rồi giữ để đọc;
  - đòn lớn có thể có lấy đà của lấy đà: nhún nhẹ ngược chiều trước pose lấy đà chính;
  - pose lấy đà là key, có ngay từ blocking.
- Lấy đà quá quen thì nhàm. Đòn lừa, nhịp chờ khác đi là việc của GD, ghi vào bảng frame data.

### 3.4 Hitstop và rung

| Lực | Hitstop *(đề xuất)* | Rung camera |
|---|---|---|
| Nhẹ | 67–100 ms (4–6 frame ở 60 fps) | không, hoặc rất nhẹ |
| Vừa | 117–133 ms (7–8) | nhẹ |
| Mạnh, chí mạng | 150–200 ms (9–12) | vừa |
| Kết liễu, boss | 217–333 ms (13–20) | mạnh |

Tham chiếu: beat 'em up của Capcom 4–10 frame; Smash Ultimate tăng theo sát thương (khoảng 6 frame ở 1%, 12 ở 10%, 19 ở
20%, tối đa 30). Hitstop dừng cả hai bên và chỉ bật khi trúng thật (7.5). Nhiều quái trúng cùng lúc thì giảm: hitstop dồn
lại làm game chậm. Game không combat (Puzzle, Cozy) không có hitstop; khoảnh khắc lớn như thắng màn, combo có thể dừng hình
rất ngắn rồi chậm dần về tốc độ thường (7.5).

Trong clip, frame va chạm là một accent cứng (7.6):

- Pose ở frame active đầu đã lệch khỏi điểm chạm: nắm đấm đã qua cằm, đầu bên bị đánh đã bật khỏi chỗ. Không vẽ, không giữ pose
  vừa chạm bề mặt; Williams: studio thời đầu giữ pose chạm 4 frame và cú đánh trông mềm nhũn.
- Hitstop giữ ở frame đó. Phần bật lại (khoảng 5 frame) chạy sau hitstop.
- Rung trong lúc hitstop: lệch qua lại quanh pose mỗi frame, biên độ tắt dần (công thức stagger, 7.6), không dùng nhiễu ngẫu
  nhiên.

### 3.5 Blend

| Chuyển | Thời lượng *(đề xuất)* |
|---|---|
| Locomotion ↔ locomotion | 0,15–0,25 s |
| Idle → đi / chạy | 0,1–0,15 s |
| Vào đòn đánh, né | 0–0,1 s |
| Trúng đòn | 0–0,1 s |
| Hết đòn về idle / chạy | 0,15–0,25 s |
| Chết → ragdoll | 0,1–0,2 s |

- Blend vào đòn nằm trong startup: blend dài ăn mất frame lấy đà, người chơi thấy trễ.
- Mood có Tempo cao (Action) dùng cận dưới của mỗi dải, Tempo thấp (Cozy) dùng cận trên (2.1).
- Unity tạo transition mới dài 0,25 s và bật Has Exit Time: chỉ hợp locomotion, phải chỉnh mọi transition khác (8.3).
- Ra khỏi hành động (về idle, chạy): Has Exit Time bật, Exit Time = 1 − thời gian blend ÷ độ dài clip, để blend xong đúng
  frame cuối recovery. Đuôi clip thừa thì cắt ở import (Last Frame), không giấu bằng Exit Time sớm: pack ExplosiveLLC cho đòn
  đánh ra ở Exit Time 0,8, trúng đòn ở 0,75–0,9, tức 10–25% cuối clip không bao giờ chạy, và độ dài clip không còn khớp bảng
  frame data.
- Tham chiếu từ pack ExplosiveLLC: vào đòn 0 s; trúng đòn, lăn, tiếp đất 0,05 s; hết đòn về idle / chạy 0,1 s; idle → chạy
  0,05–0,1 s; chạy → idle 0,1 s; rơi 0,2 s; đòn thân trên khi chạy về state chờ 0,25 s.

### 3.6 Độ dài cycle, số hình 2D

Nhịp theo Richard Williams (*The Animator's Survival Kit*, chương Walks và Runs), tính bằng frame **mỗi bước** ở 24 fps; một
cycle đi, chạy là 2 bước. Người đi đường bước 12 frame (Milt Kahl bấm giờ người thật, lần nào cũng ra 12): mọi nhịp khác tính là
nhanh hay chậm hơn 12. Nhân vật đi bước 12 thì chạy bước 6.

| Kiểu | Mỗi bước ở 24 fps | Bước mỗi giây | Mỗi bước ở 30 fps | Cycle ở 30 fps |
|---|---|---|---|---|
| Rất chậm: lê bước, rón rén chậm | 32 | 0,75 | 40 | 80 |
| Bước chậm | 24 | 1 | 30 | 60 |
| Người già, người mệt | 20 | 1,2 | 25 | 50 |
| Đi thong thả | 16 | 1,5 | 20 | 40 |
| Đi thường | 12 | 2 | 15 | 30 |
| Chạy chậm, đi kiểu cartoon, chạy giận dữ | 8 | 3 | 10 | 20 |
| Chạy thường, hoặc đi rất nhanh | 6 | 4 | 7,5 | 15 |
| Chạy rất nhanh (chạy 4 hình) | 4 | 6 | 5 | 10 |
| Chạy cực nhanh (chạy 3 hình) | 3 | 8 | 3,75 | 7–8 |

- Đi luôn có một chân chạm đất; chạy có 1–3 frame cả hai chân rời đất: chạy thường và chạy bộ 1 frame, chạy kiểu cartoon và chạy
  tả thực 2, người béo và trẻ con gần như không rời (1 hình). Bước 6 frame mà luôn có chân chạm đất là đi rất nhanh.
- Bước 2 frame (12 bước mỗi giây) là giới hạn, chỉ là mẹo: chân ngắn, nhìn trước, sau hoặc 3/4.
- Ra số lẻ ở 30 fps thì làm tròn và thử cả hai (3.1): cycle 7,5 frame thì chọn 7 hoặc 8.
- Nhịp của nhân vật ghi vào brief (9.2); vị trí các pose trong bước: 7.7.

Idle thở: 2–4 s *(đề xuất)*.

2D frame-by-frame:

| Hành động | Số hình | Mỗi hình | Ghi chú |
|---|---|---|---|
| Đi | 8 mỗi cycle (6 cũng được; 4 thì thiếu) | 80–125 ms | 8 × 125 ms = cycle 1 s như đi thường. Ở 12 fps (83 ms), 8 hình là cycle 0,67 s, nhịp đi kiểu cartoon; đi thường ở 12 fps cần 12 hình |
| Chạy | 6 mỗi cycle (4–8) | 60–80 ms | 6 × 80 ms ≈ cycle 0,5 s. Williams cho chạy luôn on ones (42 ms); giữ 60–80 ms thì mỗi bước phải có ≥ 3 hình khác nhau. 2 hình mỗi bước chỉ được khi chân ngắn và nhìn trước, sau hoặc 3/4: nhìn ngang thì nháy thành hai hình chồng |
| Idle | 2–8 | 120–250 ms *(đề xuất)* | |
| Nhảy | lấy đà 1–2, lên 1–2 (loop), đỉnh 1, rơi 1–2 (loop), tiếp đất 1–3 *(đề xuất)* | | loop lên, rơi phải có gì đó chuyển động (tay, chân, tóc), không thì trôi |
| Đánh | 4–8 *(đề xuất)*: lấy đà 1–3, va chạm 1–2, hồi 2–4 | | hình va chạm có thể là một hình smear |
| Trúng đòn | 1–3 *(đề xuất)* | | |
| Chết | 6–12 *(đề xuất)* | | |

Đi ra, vào hoặc băng qua màn hình cần ít nhất 5 hình; một chuyển động xoay cần ít nhất 3 vị trí, 2 thì nháy (Williams).

### 3.7 Bảng frame data

GD giữ một bảng cho mỗi nhân vật (khi có tool: asset `<Subject>_FrameData`). Đây là nguồn duy nhất cho cả code lẫn
animator.

| Hành động | Clip | Startup | Active | Recovery | Cancel từ | Dời (code) | Ghi chú |
|---|---|---|---|---|---|---|---|
| Đòn nhẹ 1 | `Hero_Atk_Light_01` | 4 | 2 | 10 | 11 | 0,3 m, frame 3–6 | bước nhỏ tới trước |
| Lăn né | `Hero_Def_Roll_F` | 1 | 8 (bất tử) | 6 | 12 | 3 m, frame 2–12 | |
| Nhặt đồ (game không combat) | `Farmer_Interact_Pickup` | 6 | 1 (lúc cầm) | 8 | 9 | 0 | vật bay vào túi từ frame 7 |

- Đơn vị: frame ở 30 fps. Startup = số frame **trước** frame active đầu tiên; Active bắt đầu ở frame Startup + 1. Cancel
  từ = số thứ tự frame tính từ đầu clip. (Street Fighter tính frame active đầu vào startup; quy chuẩn này không tính, để
  tổng clip = startup + active + recovery.)
- Frame active đầu tiên là frame va chạm, và là nơi hitstop, vfx, sfx trúng đòn bám vào. Pose ở đó là key (game cần trúng
  đúng một điểm của cú vung, Annand), và là pose đã qua điểm chạm, không phải pose vừa chạm (3.4).
- Dời (code): quãng code dời nhân vật và khoảng frame dời (7.2). Clip vẫn tại chỗ.
- Không chỉ đòn đánh: hành động tương tác có mốc gameplay cũng ghi theo cùng cách (nhặt đồ: với tay là startup, lúc cầm là
  active, đứng dậy là recovery). Game không combat chỉ cần các dòng này.
- Input bấm trong vài frame trước cancel window được giữ lại và chạy ở frame đầu của cửa sổ (input buffer). GD đặt số frame
  buffer.
- Đổi số trong bảng là đổi clip theo, và ngược lại.
- Code không tự gõ thời gian khoá, thời gian chờ: lấy từ bảng này. Pack ExplosiveLLC khoá đòn tay không 0,75 s, trúng đòn
  0,4 s, knockdown 5,25 s bằng số gõ tay cho gần độ dài clip: sửa clip là code lệch.
- Clip mua sẵn: frame va chạm lấy từ event có sẵn trong clip (pack ExplosiveLLC gọi là `Hit`), quãng dời đo bằng tool (7.2,
  8.8); điền bảng trước khi dùng clip.

---

## 4. Đặt tên và thư mục

### 4.1 Luật chung

- ASCII, tiếng Anh, PascalCase từng đoạn, nối bằng `_`. Không dấu cách, không dấu tiếng Việt, không `.` `:` `@` `-`.
- **Subject**: mã cố định của đối tượng, trùng tên prefab (`Hero`, `GoblinArcher`, `Car01`). **Archetype**: mã của cây
  xương dùng chung (`HumanM`, `HumanF`, `Quad`).
- Tên đã vào Animator, event, code thì không đổi. Đổi tên clip là phải sửa override controller và mọi chỗ gọi tới nó.

### 4.2 Tên asset

| Loại | Mẫu | Ví dụ |
|---|---|---|
| Model + rig 3D | `<Subject>_Rig.fbx` | `Hero_Rig.fbx` |
| Clip 3D, một clip mỗi file | `<Subject>[_<Bộ>]_<Nhóm>_<Hành động>[_<Biến thể>][_<Hướng>].fbx` | `Hero_Atk_Light_01.fbx`, `Hero_Sword2H_Atk_01.fbx` |
| Clip dùng chung một archetype | `<Archetype>[_<Bộ>]_<Nhóm>_<Hành động>…` | `HumanM_Loco_Run_F.fbx`, `HumanM_Sword2H_Loco_Run_F.fbx` |
| Clip ROM | `<Subject>_ROM` | `Hero_ROM.fbx` |
| Nguồn 2D | `<Subject>.psd`, `<Subject>.aseprite` | `Goblin.psd` |
| Clip 2D | như clip 3D, đuôi `.anim` | `Goblin_Atk_Slash.anim` |
| Controller gốc của archetype | `<Archetype>_Base.controller` | `HumanM_Base.controller` |
| Override của nhân vật | `<Subject>.overrideController` | `Hero.overrideController` |
| Avatar Mask | `<Archetype>_Mask_<Phần>.mask` | `HumanM_Mask_UpperBody.mask` |
| Sprite Library | `<Subject>_SpriteLib.spriteLib` | `Goblin_SpriteLib.spriteLib` |

Một clip mỗi file FBX: sửa một clip không phải xuất lại cả bộ, ít xung đột khi nhiều người cùng làm.

### 4.3 Nhóm clip

Danh sách cố định; cần nhóm mới thì sửa bảng này trước.

| Nhóm | Gồm |
|---|---|
| `Idle` | đứng, idle break (`Idle_Break_01`) |
| `Loco` | đi, chạy, lướt, xoay, bắt đầu / dừng |
| `Jump` | nhảy, rơi, tiếp đất |
| `Atk` | đòn thường, combo |
| `Skill` | kỹ năng |
| `Def` | đỡ, né, lăn |
| `Hit` | trúng đòn, ngã, đứng dậy |
| `Death` | chết |
| `Spawn` | xuất hiện |
| `Emote` | biểu cảm, thắng, thua |
| `Interact` | nhặt, mở, dùng vật |
| `Equip` | rút, cất, đổi vũ khí |
| `Add` | clip additive (thở, giật) |
| `Face` | biểu cảm mặt |
| `Cine` | cinematic |
| `Pose` | pose tĩnh cho UI, ảnh |

Hậu tố:

- Hướng 3D (theo nhân vật): `F`, `B`, `L`, `R`, `FL`, `FR`, `BL`, `BR`.
- Hướng 2D top-down (theo màn hình): `N`, `NE`, `E`, `SE`, `S`, `SW`, `W`, `NW`. Nhân vật đối xứng chỉ làm một bên, bên
  kia lật.
- Hành động nhiều đoạn: `_Start`, `_Loop`, `_End` (`Hero_Jump_Start`, `Hero_Jump_Loop`, `Hero_Jump_End`).
- Biến thể: `_01`, `_02`, hoặc chữ (`_Light`, `_Heavy`).

Bộ (tuỳ chọn, đứng ngay sau Subject hoặc Archetype): bộ clip theo vũ khí đang cầm, như pack ExplosiveLLC có bộ tay không và
bộ kiếm hai tay. Mỗi bộ một sub-state machine trong controller (8.3). Danh sách gợi ý: `Unarmed`, `Sword1H`, `Sword2H`,
`SwordShield`, `Spear`, `Bow`, `Staff`, `Gun`. Game chỉ có một bộ thì bỏ đoạn này; game nhiều bộ ghi danh sách vào brief.

### 4.4 Tên trong rig và Animator

| Thứ | Mẫu | Ví dụ |
|---|---|---|
| Xương | mục 5.2 | `UpperArm_L` |
| Socket | `Socket_<Chỗ>[_<Bên>]` | `Socket_Hand_R`, `Socket_Head`, `Socket_Back`, `Socket_Hip_L` |
| Điểm cầm trên vũ khí | `Grip` (tay chính), `Grip_L` (tay trái của vũ khí hai tay) | `Grip`, `Grip_L` |
| IK target | `IK_<Chỗ>_<Bên>` | `IK_Foot_L` |
| Tham số Animator | danh từ / tính từ PascalCase; trigger là động từ | `Speed`, `MoveX`, `MoveZ`, `IsGrounded`, `IsMoving`, `CanCancel`, `Stance`, `Variant`, trigger `Attack` |
| Curve trên clip điều khiển tham số (8.6) | trùng tên tham số float | `HandIK_L` |
| Sprite Library | Category = tên bộ phận, Label = biến thể | `Hand_L` / `Fist` |
| Event | danh sách cố định mục 7.4 | `AE_Footstep` |

### 4.5 Thư mục

Đề xuất. Project đã có cấu trúc riêng thì giữ, chỉ cần tên đúng 4.2.

```
Assets/_Project/Art/Characters/<Subject>/      vật thể: Art/Props/<Subject>/, xe: Art/Vehicles/<Subject>/
  Model/       Hero_Rig.fbx | Goblin.psd | Slime.aseprite
  Anim/        Hero_Atk_Light_01.fbx | Goblin_Atk_Slash.anim
  Animator/    Hero.overrideController
  Textures/  Materials/
Assets/_Project/Art/Animation/<Archetype>/
  Anim/        clip dùng chung của archetype
  HumanM_Base.controller   HumanM_Mask_UpperBody.mask
ArtSource/                                     ngang hàng Assets, Unity không import (12.2)
  Characters/<Subject>/   Hero.blend | Hero_Body.psd | Mixamo/ (clip thô)
```

**File nguồn** là file làm việc mà Unity không dùng trực tiếp nhưng phải giữ để sửa về sau: `.blend` (còn control rig,
constraint, các action mà FBX không mang theo), file vẽ texture gốc nhiều layer, clip mocap hoặc Mixamo thô trước khi
retarget. Mất file nguồn là chỉ còn FBX: sửa rig hay sửa clip phải làm lại từ đầu. File `.blend` không để trong Assets:
Unity phải có Blender cài trên mọi máy (kể cả máy build) mới import được. File `.psd` của nhân vật 2D và `.aseprite` vừa là file nguồn vừa
được Unity import thẳng, nên nằm trong Assets.

**Pack mua sẵn** (Asset Store) để nguyên trong thư mục pack tự đặt (ví dụ `Assets/ExplosiveLLC/`), không sửa file trong đó,
để còn cập nhật được pack. Pack coi như file nguồn: clip dùng thật được làm lại theo chuẩn vào `Art/Animation/<Archetype>/`
(8.8), game không tham chiếu thẳng asset của pack.

---

## 5. Rig 3D **[RIG]**

### 5.1 Đơn vị, hướng, pose gốc

| Mục | Chuẩn | Vì sao |
|---|---|---|
| Đơn vị | 1 unit = 1 m. Mọi nhân vật trong một game cùng thước (người lớn khoảng 1,7–1,8 m nếu tả thực) | Physics, ánh sáng, NavMesh của Unity tính theo mét |
| Hướng | Trong Unity nhân vật nhìn về +Z, trục Y hướng lên | Quy ước của Unity; code di chuyển, prop, camera đều giả định vậy |
| Gốc | Xương `Root` ở (0, 0, 0), giữa hai bàn chân; chân chạm mặt Y = 0 | Nhân vật đứng đúng trên mặt đất ở chỗ code đặt |
| Transform | Mesh và armature đã apply (vị trí 0, xoay 0, scale 1) trước khi skin. Không apply scale cho armature đã có animation | Scale, xoay dư làm lệch prop, lệch chỗ code đặt, hỏng retarget |
| Bind pose | T-pose, cố định cho cả project (đã chốt, 12.1) | Unity lấy T-pose làm pose tham chiếu của Humanoid, Mixamo cũng dựng rig từ T-pose, nên retarget (5.8) gọn. Đổi lại vai dễ bẹp khi tay buông xuôi: xem 5.3 |
| fps | 30 (3.1), thống nhất cả studio | Unity giữ fps của file nguồn |

### 5.2 Cây xương và tên

Cây chuẩn cho archetype người. Tên gần với tên Humanoid của Unity nên map tự động được nếu sau này cần Humanoid.

```
Root                                   không deform; đứng yên ở gốc trong mọi clip
└─ Hips
   ├─ Spine ─ Chest ─ [UpperChest] ─ Neck ─ Head ─ [Jaw] [Eye_L] [Eye_R] [Socket_Head]
   │                  ├─ Shoulder_L ─ UpperArm_L ─ LowerArm_L ─ Hand_L ─ ngón, Socket_Hand_L
   │                  └─ Shoulder_R …  (đối xứng)
   ├─ UpperLeg_L ─ LowerLeg_L ─ Foot_L ─ Toes_L
   └─ UpperLeg_R …  (đối xứng)

Ngón:   Thumb1_L…Thumb3_L, Index1_L…3, Middle1_L…3, Ring1_L…3, Little1_L…3
Twist:  UpperArmTwist_L (con của UpperArm_L), LowerArmTwist_L (con của LowerArm_L); nhân vật chính
[ ] = tuỳ nhân vật
```

1. Tên tiếng Anh, PascalCase. Trái / phải bằng hậu tố `_L` / `_R` theo bên của nhân vật, không theo người nhìn. Đốt đánh
   số từ gốc ra ngọn (`Index1_L` → `Index3_L`).
2. Tên duy nhất trong cả cây. Không namespace (`mixamorig:` phải đổi; tool tự đổi, 5.9), không dấu cách, không dấu chấm.
3. `Root` tách khỏi `Hips` và đứng yên ở gốc trong mọi clip (7.2). Giữ `Root` để cây thống nhất, và để game nào thật cần
   root motion sau này vẫn bật được.
4. Chỉ xuất xương deform và socket. Control rig, IK helper ở lại file nguồn. Không xương lá `_end`.
5. Cùng archetype thì cùng cây xương, cùng tên, không thêm bớt giữa cây. Clip bám xương theo đường dẫn trong cây: lệch
   một tên là clip không chạy trên nhân vật đó. Nhân vật cần thêm xương (tóc, áo choàng, vũ khí) thì thêm ở nhánh lá.
6. Không scale xương trong animation của rig Humanoid. Rig Generic cần nén giãn thì scale đều (uniform), chỉ ở xương lá
   hoặc xương helper. Scale không đều trên xương có con làm con bị méo: Unity không bù scale theo đốt như Segment Scale
   Compensate của Maya.

### 5.3 Skin

1. Tối đa **4 influence** mỗi đỉnh (mức Standard của Unity; hơn 4 thì tốn hơn hẳn). Máy yếu có thể chạy 2 (Quality >
   Skin Weights, 8.5): thử skin ở 2 influence, không được vỡ nặng.
2. Weight chuẩn hoá (tổng bằng 1), bỏ weight rất nhỏ (< 0,01; rigkit bỏ < 0,02), mirror trái ↔ phải.
3. Không weight lên socket và xương không deform.
4. Nhân vật chính có twist bone ở cánh tay trên và cẳng tay, chống "xoắn kẹo" khi xoay cổ tay. Rig Humanoid không map
   twist bone: phải tick trong Mask > Transform của từng clip, hoặc để Unity tự chia twist (mặc định 50%).
5. Mỗi rig bàn giao kèm **clip ROM**: lần lượt xoay mọi khớp chính tới giới hạn (cổ, sống lưng, vai, khuỷu, cổ tay, hông,
   gối, cổ chân), có một đoạn tay buông xuôi. Bind pose là T-pose nên vai và nách dễ bẹp ở pose tay buông, mà đó là pose
   hay gặp nhất trong game: cần thì thêm xương phụ ở vai hoặc sửa weight. Dùng clip ROM để duyệt skin, và chạy lại mỗi lần
   sửa rig. Giới hạn khớp của từng nhân vật chốt bằng ROM: ROM ở 90% giới hạn mà còn rách, lật da hay lún vào thân thì hạ
   giới hạn đó; pose và clip không được vượt (5.10).
6. Mỗi nhân vật một SkinnedMeshRenderer (gộp mesh), ít material nhất có thể.
7. **Không tính weight theo ngưỡng toạ độ** (z, x ≥ 0): thân thành sọc ngang, gập là gãy (lỗi của một lần rig chibi bằng script tay, 5.10).
   Dùng Automatic Weights (bone heat) rồi làm sạch; rigkit làm tự động (5.10).
8. Mỗi chi chỉ có weight trong vùng của nó cộng một dải blend đo dọc bề mặt: bone heat để đuôi dài, cánh tay kéo méo miệng
   khi vung. Da dưới phụ kiện cứng (mũ, giáp) 100% theo xương mang phụ kiện, không thì hai lớp trượt lên nhau và xuyên.
   Mảnh rời nhỏ (mắt, khoá thắt lưng) lấy weight của bề mặt bên dưới.
9. Cắt còn 4 influence sao cho các đỉnh kề nhau giữ cùng bộ xương (chọn trên weight đã làm mượt): cắt riêng từng đỉnh làm da
   gai ở chỗ chuyển.
10. Chibi thân tròn: xương vai (`Shoulder`) không deform, tay mọc thẳng từ thân; bone heat của vai trải cả mảng ngực.
11. Preserve Volume (dual quaternion) trong Blender tắt: Unity trộn tuyến tính, bật thì Blender hiện khác game.

### 5.4 Generic hay Humanoid

| | **Generic (mặc định)** | Humanoid |
|---|---|---|
| Dùng khi | mọi rig lúc chạy; nhân vật cùng archetype dùng chung clip | lúc làm: retarget clip mocap, Mixamo, clip mua sang cây chuẩn (5.8). Lúc chạy: chỉ khi game thật cần retarget ngay lúc chạy |
| CPU | thấp hơn | cao hơn 30–50%: chạy IK và retarget mỗi frame, kể cả khi không dùng |
| Xương thêm (tóc, twist, vũ khí) | chạy bình thường | phải tick trong Mask > Transform của từng clip, chỉ chạy khi cây giống hệt |
| Scale, dời xương | có | chỉ xoay (trừ root, hips) |
| Root motion | theo xương gốc, đúng đơn vị của file | nhân theo `humanScale` (cỡ avatar so với avatar chuẩn của Unity): cùng clip, nhân vật to hơn đi xa hơn *(đo: `humanScale` 1,47 của pack ExplosiveLLC cho Run 6,2 m/s)* |
| Có sẵn | — | mirror, foot IK, look-at, IK tay (`OnAnimatorIK`), retarget |

Cây xương chuẩn (5.2) cho phép dùng chung clip mà vẫn ở Generic. Clip từ nguồn khác cây thì retarget lúc làm rồi lưu thành
clip Generic (5.8). Pack mua sẵn gần như luôn là Humanoid và dựa vào retarget lúc chạy (pack ExplosiveLLC dạy thay model
bằng cách gán controller của pack cho model Humanoid khác): muốn dùng theo quy chuẩn thì đi 8.8.

### 5.5 Socket, prop, xe

- Vũ khí, đồ cầm tay có xương `Grip` ở chỗ nắm, gắn vào `Socket_Hand_R` / `Socket_Hand_L`. Vũ khí hai tay có thêm điểm
  `Grip_L` (con của vũ khí) cho tay trái: kiểm tay trái bám đúng `Grip_L` ở mọi clip của bộ đó (8.6).
- Vũ khí cất trên người: `Socket_Back`, `Socket_Hip_L`, `Socket_Hip_R`. Rút / cất là đổi socket (hoặc bật / tắt model) ở
  đúng frame tay chạm vũ khí, bằng event `AE_Prop` (7.4), không bằng thời gian chờ gõ tay trong code như pack ExplosiveLLC.
- Prop có animation (rương, cửa, cần gạt): rig Generic, `Root` ở tâm đáy, xương đặt tên theo chức năng (`Lid`, `Door_L`).
- Xe: `Root` ở tâm đáy trên mặt đất, `Body`, `Wheel_FL` / `Wheel_FR` / `Wheel_RL` / `Wheel_RR` (pivot ở tâm bánh, quay
  quanh trục X), `Door_L`, `Door_R`, `Hood`, `Trunk`, `SteeringWheel`. Bánh quay và thân nhún do code / physics theo tốc
  độ gameplay; clip chỉ dành cho hành động có kịch bản (mở cửa, nâng cấp).

### 5.6 Ngân sách mobile *(đề xuất, đo lại trên máy thấp nhất ở pilot)*

| Hạng | Xương deform | Tam giác | Influence | Blendshape |
|---|---|---|---|---|
| Nhân vật chính | 45–75 (gồm ngón; mặt ≤ 15) | 8k–20k | 4 (máy yếu 2) | chỉ cận cảnh (khẩu hình, sửa deform) |
| NPC, quái | 25–45 | 3k–8k | 2–4 | không |
| Đám đông | ≤ 25 | ≤ 2k | 1–2 | không |

Mốc tham chiếu từ Unity: thêm 15 xương vào rig 30 xương làm rig Generic tốn thêm 50% phép tính; manual cũ (Unity 5) khuyên
dưới 30 xương cho mobile. Mặt nhân vật trên máy thấp và trung dùng xương (10–20) thay cho blendshape.

### 5.7 Xuất FBX **[RIG] [TA]**

**Blender** (bản 5.x vẫn xuất bằng add-on "FBX (Legacy)"):

| Mục | Đặt | Vì sao |
|---|---|---|
| Scene | Metric, Unit Scale 1.0 | |
| Limit to / Object Types | phần chọn hoặc đang hiện; Armature + Mesh | Không kéo camera, đèn, rác |
| Apply Scalings | **FBX Units Scale** | Mặc định All Local làm model to gấp 100 lần trong Unity |
| Forward / Up | -Z / Y (mặc định) | |
| Apply Transform | **tắt** với rig | Blender ghi là thử nghiệm, hỏng với armature và animation |
| Apply Modifiers | tắt với mesh có shape key | Bật thì mất shape key |
| Add Leaf Bones | **tắt** | Thêm xương `_end` thừa |
| Only Deform Bones | bật; socket, IK target cần xuất thì đánh dấu Deform | Không xuất control rig |
| Bake Animation, Key All Bones | bật | |
| NLA Strips / All Actions | chọn một cách cho cả project | Bật cả hai dễ ra clip trùng |
| Force Start/End Keying | bật | Giữ key cuối của cycle |
| Simplify | 0 | Để Unity nén, không nén hai lần |

Xuất theo bảng trên thì object con trong Unity mang xoay -90° ở trục X: vô hại khi chạy. Cách xuất không xoay (Forward -Y,
Up Z, tắt Use Space Transform, bật Bake Axis Conversion trong Unity) chỉ dùng sau khi đã thử trên rig mẫu. Blender 4.4 trở
lên: mỗi action một slot (All Actions có thể bỏ sót slot).

**Maya** (để tham khảo; team đã chốt Blender):

- Giữ đơn vị cm (mặc định). Trong Unity bật Convert Units, dòng bên cạnh phải là "1cm (File) to 0.01m (Unity)". Trục Y lên.
- FBX: Bake Animation (theo timeline, step 1), Deformed Models (Skins, Blend Shapes), Smoothing Groups bật; Constraints,
  Cameras, Lights, Embed Media tắt.
- Bake IK trước khi xuất. Không dùng Rotate Axis (pre-rotation): Unity không hỗ trợ. Xoá history.

### 5.8 Nguồn animation: tay, mocap, thủ tục **[ANIM] [TA]**

| Nguồn | Dùng cho | Luật |
|---|---|---|
| Keyframe tay (Blender) | nhân vật chính, đòn đánh, kỹ năng, mọi thứ cần diễn | toàn bộ quy chuẩn |
| Mocap, Mixamo, clip mua | nền cho locomotion, NPC, emote, hành động đời thường | tải bản In Place nếu có; tên xương Mixamo tự đổi lúc import (5.9); pack mua theo 8.8; retarget sang cây chuẩn, lưu thành clip Generic; luôn qua lượt làm sạch, cường điệu (mục 2, dòng 10) và chỉnh theo mood (2.1); đọc điều khoản dùng của nguồn |
| Thủ tục (code, tool sinh clip) | prop, xe (bánh quay, thân nhún), idle thở, nảy của quái đơn giản, clip tạm (placeholder) | clip do tool sinh cũng theo tên, fps, loop, `Root` đứng yên như clip tay |
| Key pose viết bằng code (rigkit, 5.10) | quái, NPC, nhân vật phụ, bộ clip tạm, blocking cho animator; nhân vật chính vẫn key tay | qua mọi gate của 5.10 rồi duyệt như clip tay (9.3): gate sạch chưa có nghĩa là đẹp |

Retarget lúc làm, không retarget lúc chạy: nhân vật vẫn chạy Generic, khỏi tốn thêm 30–50% CPU của Humanoid. Hai cách, chọn
ở pilot:

1. Trong Unity: import clip nguồn dạng Humanoid, phát lên avatar Humanoid dựng từ cây chuẩn, ghi lại thành clip Generic bằng
   `GameObjectRecorder` *(tool, chưa có)*. Không cần add-on.
2. Trong Blender: dùng add-on retarget, rồi xuất như clip tay (5.7).

Clip mocap thô không vào gameplay.

### 5.9 Tên xương từ Mixamo **[TA]**

File tải từ Mixamo mang tên xương `mixamorig:LeftUpLeg`… và clip tên `mixamo.com`. Tool `GameAnimMixamoImport` đổi
lúc import, không phải sửa tay:

- Model nằm trong thư mục chuẩn (4.5: `Art/Characters`, `Art/Props`, `Art/Vehicles`, `Art/Animation`): tự đổi.
- Model ở chỗ khác: chọn model, menu **Tools/Game Animation/Đổi tên xương Mixamo theo chuẩn**. Muốn giữ tên gốc: **Giữ
  tên Mixamo gốc** (thắng cả thư mục chuẩn).
- Clip `mixamo.com` đổi thành tên file, nên đặt tên file FBX theo 4.2 trước khi kéo vào Unity (ví dụ
  `Mixamo_Loco_Run_F.fbx`).

| Mixamo (sau khi bỏ tiền tố `mixamorig:`) | Tên chuẩn |
|---|---|
| `Hips`, `Spine`, `Neck`, `Head`, `HeadTop_End` | giữ nguyên |
| `Spine1` / `Spine2` | `Chest` / `UpperChest` |
| `LeftShoulder` / `LeftArm` / `LeftForeArm` / `LeftHand` | `Shoulder_L` / `UpperArm_L` / `LowerArm_L` / `Hand_L` |
| `LeftHandThumb1`…`4`, tương tự `Index`, `Middle`, `Ring` | `Thumb1_L`…`Thumb4_L`, `Index1_L`…, `Middle1_L`…, `Ring1_L`… |
| `LeftHandPinky1`…`4` | `Little1_L`…`Little4_L` |
| `LeftUpLeg` / `LeftLeg` / `LeftFoot` / `LeftToeBase` / `LeftToe_End` | `UpperLeg_L` / `LowerLeg_L` / `Foot_L` / `Toes_L` / `Toes_End_L` |
| `LeftEye` | `Eye_L` |
| `Right…` | như bên trái, hậu tố `_R` |

Đọc trước khi bật cho asset cũ:

1. Tool chỉ đổi tên. Rig Mixamo không có `Root` (`Hips` nằm trên cùng) và còn xương lá (`HeadTop_End`, đốt ngón thứ 4,
   `Toes_End_…`), nên coi là một archetype riêng tên `Mixamo`. Nhân vật rig bằng Mixamo và clip tải từ Mixamo cho đúng nhân
   vật đó có cây giống hệt, nên chạy Generic được ngay. Muốn đúng đủ cây chuẩn 5.2 thì sửa rig trong Blender.
2. Đổi tên xương là đổi ID của object con trong model. Prefab đang gắn đồ vào xương cũ, code tìm xương theo tên cũ
   (`"mixamorig:RightHand"`), avatar Humanoid đã map theo tên cũ đều mất tham chiếu. Asset mới thì không sao; asset cũ chỉ
   bật sau khi đã sửa những chỗ đó.
3. Chép tool vào project đang chạy: Unity import lại mọi model một lần (vì có thêm bước xử lý lúc import). Chỉ model trong
   thư mục chuẩn hoặc có cờ đổi tên mới bị đổi.

### 5.10 Rig và animate bằng Blender headless: rigkit **[RIG] [ANIM] [TA]**

Bộ script `rigkit` (skill Claude `blender-rig-animate`) chạy Blender không giao diện: rig, skin, pose, clip, xuất FBX,
mỗi bước một lệnh, kèm gate đo được; gate còn lỗi thì không sang bước sau. Nguồn nằm trong module ở
`Skill~/blender-rig-animate/` (Unity bỏ qua thư mục có `~`), cạnh skill `game-animation` (đọc và áp quy chuẩn này);
cài cho Claude Code: chép thư mục skill vào `~/.claude/skills/` hoặc `.claude/skills/` của project. Hai skill không mang
thông tin của game nào. Spec của từng nhân vật (rig, skin, key pose, clip, `run.sh` chạy cả chuỗi) là file của project
game, để cạnh model trong folder Unity bỏ qua (ví dụ `<folder model>/Blender~/<Subject>_rigkit/`). Tool không sửa file
nguồn: mỗi bước đọc một file, ghi file mới.

| Bước | Lệnh (`blender -b <file> --python scripts/rk.py -- <bước>`) | Gate |
|---|---|---|
| Khảo sát, chuẩn bị | `inspect`, `prepare --scale`, `measure` | mảnh rời, lỗ, scale chưa apply; đưa về cỡ game, chân chạm đất (5.1) |
| Rig | `rig --spec rig.json` | tên và cây 5.2, cặp trái phải, mirror, khớp nằm trong mesh, roll theo hướng gập |
| Skin | `skin --spec skin.json`, `check_weights --stress` | 5.3; gập từng khớp tới 70% giới hạn, đo rách, bẹp, lật tam giác |
| ROM | `rom`, `check_anim` | 5.3.5; mọi khớp tới 90% giới hạn của nó; lỗi thì hạ giới hạn |
| Pose, clip | `check_anim --poses`, `anim --clips`, `check_anim` | giới hạn khớp, lún đất (da mềm), vật cứng xuyên sàn (vũ khí, khiên, mũ, mặt: chạm được, xuyên thì không), trượt chân, đạo cụ xuyên người và xuyên nhau, tay chân xuyên thân, rách da, loop hở, giật, `Root` dời (7.2), scale xiên, đạo cụ rơi quá xa, pose trung tâm, biên độ |
| Duyệt | `review --video` | không phải gate: ảnh và video ở camera game để duyệt bằng mắt (9.3) |
| Xuất | `export`, `verify_fbx` | 5.7; một SkinnedMeshRenderer, không xương `_end`, lệch ≤ 2 mm sau vòng xuất–nhập (đo sau khi gỡ nối xương: Unity không có "nối", đạo cụ rời tay không đọc lệch); event ra `<fbx>.events.json` (7.4) |

Luật rút ra khi làm lại một chibi cầm rìu và khiên (bản rig bằng script tay, không gate: đo lại 55 lỗi; rigkit: 0; chi
tiết trong `reference/failure-catalog.md` của skill):

1. Pose bằng góc giải phẫu (gập, dạng, xoắn) hoặc mục tiêu IK, không bằng Euler thô. Chân luôn IK cắm đất: dời hông
   mà chân FK là chân lún và trượt.
2. Đo tỉ lệ trước khi pose: tầm với của chân, độ hạ hông tối đa, góc tay chạm thân, vật chắn (vành mũ), chiều dài vũ khí.
   Chibi đó: chân 21 cm chỉ bước được ±5 cm, hông hạ tối đa 2,5 cm, tay không hạ quá -35° so với T-pose.
3. Mỗi clip key đủ location, rotation, scale của mọi xương: kênh thiếu giữ giá trị của clip trước (mũ rơi trong Die
   nằm dưới chân trong Idle).
4. Clip hành động bắt đầu và kết thúc ở pose trung tâm (frame 0 của Idle). Đo cùng thước với gate trên pack ExplosiveLLC: đòn lệch
   Idle trung bình 0–9°; một đòn chibi sinh bằng code (team chê) lệch 35°. Gate báo lỗi khi lệch trung bình > 20° hoặc một
   xương > 90°. Như A-22: pose trung tâm là frame 0 của Idle cùng bộ (`Hero_Sword2H_Atk_01` so với `Hero_Sword2H_Idle`;
   idle break không phải pose trung tâm), nhóm Hit được lệch frame đầu (`Clip(..., hub_start=False)`, tên có `_Hit` thì
   mặc định vậy), Die kết thúc chỗ khác (`hub_end=False`).
5. Idle phải sống: xương thân trên xoay trung bình ≥ 8° (pack 15–16°, idle chibi bị chê khoảng 5°); đòn ≥ 20° (pack
   42–86°). Gate sạch chưa có nghĩa là đẹp: xem video.
6. Đạo cụ rơi nằm lại gần thân (dưới 0,6 chiều cao). Thả ở frame va chạm thì tay đang đi rất nhanh (chibi đó: 9 m/s):
   giảm phần vận tốc đạo cụ kế thừa.

Thêm từ một nhân vật tròn không tay chân, kiếm và khiên lơ lửng (2026-09-30; chi tiết `reference/failure-catalog.md` mục E):

7. Theo đà (7.6.9) sau cú dừng gắt bằng lò xo tắt dần (`spring`, `spring_channel`): phần lỏng đi tiếp, vọt quá rồi lắng,
   không chỉ trễ một frame rồi dừng theo. Vật cầm, vật lơ lửng: một lò xo trên hướng của chính vật; lò xo từng khớp của
   tay giải bằng IK phá thế bù của các khớp dư (vũ khí lắc 95° lúc bật nhảy dù gần như không xoay). Cửa sổ lò xo giữ
   pose trung tâm và khung chạm đúng key.
8. Vật cứng (vũ khí, khiên, mũ, mặt) được chạm sàn, không được xuyên sàn: gate riêng, vì gate lún đất chỉ đo da mềm. Gate
   này bắt được rìu cắm sàn 25 cm ở một clip đòn đánh đã "0 lỗi" từ trước.

---

## 6. Rig 2D **[RIG]**

Công cụ đã chốt (12.1): Unity 2D Animation 13 + PSD Importer 12 (đã có trong project); frame-by-frame dùng Aseprite
Importer 3.

### 6.1 Chuẩn bị art (PSD)

1. File **.psd** (team không dùng .psb), mỗi bộ phận một layer, vẽ ở **pose trung tính** (tay hơi rời thân, chân thẳng tự
   nhiên). `.psd` tối đa 30.000 px mỗi chiều, dư cho một nhân vật; `.psb` chỉ cần cho ảnh lớn hơn.
2. Tô lấn phần bị che ở mọi khớp xoay (vai, khuỷu, hông, gối, cổ): xoay tới góc cực không được hở.
3. Không dựa vào blend mode, opacity của layer, layer effect: importer bỏ qua hết, phải bake vào pixel.
4. Vẽ ở độ phân giải cuối theo PPU của project. Không vẽ to rồi thu nhỏ trong Unity.
5. Phần để thay (tay nắm / mở, miệng, mắt) để trong cùng file PSD dưới dạng layer ẩn, gom theo group của bộ phận.
6. Tên layer = tên bộ phận theo 4.4 (`UpperArm_L`, `Hand_L`); tên group = tên bộ phận chứa các biến thể. Sprite Library
   dựng từ PSD lấy group làm Category, layer làm Label.
7. Đã rig rồi thì: không xoá rồi tạo lại layer (layer mới mang ID mới, sprite đó mất xương và weight); đổi kích thước layer
   thì sprite rect không tự cập nhật, phải sửa trong Sprite Editor hoặc chạy Automatic Reslice.

### 6.2 Import PSD **[TA]**

| Mục | Đặt | Vì sao |
|---|---|---|
| Importer | **UnityEditor.U2D.PSD.PSDImporter** (dropdown Importer ở đầu Inspector) | Unity mặc định import `.psd` bằng Texture Importer: ra một ảnh phẳng, không layer, không rig được |
| Import Mode | Individual Sprites (Mosaic) | Use as Rig chỉ chạy ở chế độ này |
| Use as Rig | bật | Tạo prefab, Order in Layer theo thứ tự layer, gắn Sprite Skin cho sprite có xương |
| Main Skeleton | PSD gốc để trống; PSD biến thể gán `.skeleton` của PSD gốc | Biến thể không sửa được xương, sửa ở PSD gốc |
| Use Layer Group | bật khi muốn prefab chia theo group Photoshop | |
| Include Hidden Layers | bật khi có layer ẩn để swap | |
| Layer Mapping | Use Layer ID (mặc định) | Đổi tên, dời layer vẫn giữ rig |
| Pixels Per Unit | một số cho cả project (đề xuất 100 cho HD) | Mật độ pixel đồng đều |
| Mesh Type | Tight | Ít vẽ đè (overdraw) |
| Pivot của document | Bottom Center | Sorting Group sắp theo vị trí gốc |
| Generate Mip Maps | **tắt** (mặc định bật) | Sprite 2D không cần mip map |
| Wrap Mode | **Clamp** (mặc định Repeat) | |
| Nén | ASTC cho Android và iOS | |

### 6.3 Xương

1. Đặt tên theo 5.2 (Unity tự đặt `bone_N`, phải đổi). Cùng archetype thì cùng tên, cùng cây: clip bám xương theo đường
   dẫn, Auto Rebind của Sprite Skin so theo tên và cây.
2. Xương gốc ở hông (hoặc giữa hai chân), con theo đúng thứ tự cơ thể. Xương phụ (tóc, đuôi, vải) ở nhánh lá.
3. Đổi thứ tự vẽ theo pose bằng Depth của xương.
4. Chỉ xương thật sự làm cong sprite mới nằm trong Bone Influence của sprite đó. Danh sách ngắn thì Sprite Skin nhẹ.

### 6.4 Mesh và weight

1. Auto Geometry rồi sửa tay: thêm đỉnh ở chỗ uốn (khớp), bớt ở chỗ cứng. Thông số Auto Geometry lưu theo máy từng người,
   không theo project, nên cả team dùng chung Outline Detail 10, Alpha Tolerance 10, Subdivide 0 *(mặc định của Unity,
   chỉnh ở pilot)*.
2. Auto Weights rồi sửa bằng brush, giữ Normalize bật. Bộ phận cứng (đầu, bàn tay, vũ khí) 100% vào một xương.
3. Tối đa **4 influence** mỗi đỉnh (Skinning Editor tự cắt còn 4 khi lưu).
4. Thử bằng Preview Pose ở các pose cực: không rách, không hở khớp, chỗ cứng không méo.

### 6.5 Sprite swap

1. Sprite Library Asset: Category = bộ phận (`Hand_L`), Label = biến thể (`Open`, `Fist`, `Point`). Category không trùng.
2. Sprite Library component trên gốc nhân vật, Sprite Resolver trên từng Sprite Renderer cần thay.
3. Nhân vật đã rig: **mọi lần thay sprite đều key qua Sprite Resolver**, không key thẳng sprite của Sprite Renderer. Key
   của Resolver tự là bậc thang (không nội suy, không blend giữa clip), đổi cả bộ skin clip vẫn chạy. Không trộn hai kiểu
   key trong một Animator.
4. Skin khác (màu phe, trang phục): Sprite Library Asset Variant, hoặc asset khác có cùng Category / Label rồi đổi tham
   chiếu. Sprite thay trên mesh đã skin phải cùng skeleton.

### 6.6 IK 2D

1. IK Manager 2D đặt trên gốc. Limb cho tay, chân (2 xương); CCD hoặc FABRIK cho chuỗi dài (đuôi, xúc tu). Tên target
   theo 4.4.
2. IK chạy mỗi LateUpdate và đè lên góc xoay đã key của xương trong chuỗi. Game mobile: dùng IK lúc làm; bản ship tắt IK
   Manager khi clip đã key đủ góc xương *(cần xác nhận ở pilot: Unity không có nút bake IK chính thức)*. Giữ IK lúc chạy
   thì tắt Always Update cho nhân vật hay ra khỏi màn hình.

### 6.7 Sorting, deform, hiệu năng **[TA]**

1. **Sorting Group** trên gốc mỗi nhân vật; mọi bộ phận sắp theo vị trí gốc. Game top-down: 2D Renderer > Transparency
   Sort Mode = Custom Axis (0, 1, 0), pivot gốc ở chân.
2. Sprite Skin deform bằng GPU khi đủ điều kiện: URP, SRP Batcher, Player > GPU Skinning = GPU (Batched), shader hỗ trợ
   skinning, máy có compute shader. Mặc định của project Unity 6 đã đủ. Thiếu một điều kiện thì renderer đó tự về CPU,
   không báo:
   - Material Property Block (hay dùng để loé trắng khi trúng đòn) và Sprite Mask đẩy renderer về CPU. Loé trắng làm bằng
     material riêng.
   - Android OpenGL ES 3 và WebGL không có GPU deform. Android dùng Vulkan.

   GPU hợp khi ít nhân vật, mesh dày, game nặng CPU; CPU (dynamic batching) hợp khi nhiều nhân vật mesh thưa. Chọn bằng
   Profiler trên máy thật.
3. Nâng 2D Animation lên 13.0.6: bản này sửa lỗi nháy của Sprite Skin bị cull và lỗi Auto Rebind (project đang 13.0.5).
4. Sprite của một nhân vật chung một Sprite Atlas; soát bằng Sprite Atlas Analyzer (Window > Analysis).
5. Ngân sách *(đề xuất, Unity không công bố)*: nhân vật chính ≤ 40 xương, ≤ 25 Sprite Skin, ≤ 2k đỉnh; quái thường ≤ 20
   xương; atlas mỗi nhân vật ≤ 2048².

### 6.8 Frame-by-frame và pixel art

1. Mọi frame của một nhân vật cùng kích thước ô, cùng pivot (giữa chân), để không rung khi đổi frame.
2. Aseprite: mỗi hành động một tag, tên tag `<Nhóm>_<Hành động>` (clip ra theo tên tag; clip nằm trong file `.aseprite`
   của nhân vật nên không cần Subject). Tag lặp ∞ thì clip bật Loop Time.
   Thời lượng từng frame giữ nguyên. Bật "Create UUID for layers" trong Aseprite. Controller do importer sinh chỉ đọc: cần
   sửa thì dùng Export Animation Assets. Event: ghi `event:<Tên>` vào user data của cel.
3. Clip tạo bằng cách kéo sprite vào Scene có 12 sample/s. Đặt sample rate trước khi key: đổi sau thì mọi key giữ số frame,
   thời lượng clip đổi theo.
4. Loop sprite: key sprite cuối ở frame N−1, **không** chép frame 0 ra cuối. Unity tự cộng thêm một frame sau key sprite
   cuối; chép frame 0 là frame 0 chạy hai lần. Kiểm: Length của clip = số frame ÷ sample rate.
5. Pixel art: cùng PPU mọi nơi; Filter Point, Compression None, không mip map; pivot Custom theo đơn vị pixel; Pixel Perfect
   Camera (Assets PPU = PPU, Reference Resolution = độ phân giải art); di chuyển nguyên pixel (Pixel Snapping). Không xoay
   xương trên pixel art, trừ khi dùng Upscale Render Texture: xoay tạo pixel nghiêng, to nhỏ lẫn lộn ("mixel"). Pixel art
   mặc định là frame-by-frame.

### 6.9 Vì sao không dùng Spine

| | Unity 2D Animation (đã chốt) | Spine |
|---|---|---|
| Giá | miễn phí, có sẵn | license cho từng người dùng editor; doanh thu từ 500k USD/năm phải mua gói Enterprise |
| Công cụ cho animator | đủ dùng: mesh, weight, IK, swap | mạnh hơn: constraint, path, physics, clipping (gói Professional) |
| Render | mỗi bộ phận một renderer; deform GPU, hoặc CPU có batching | một mesh mỗi nhân vật, dựng lại trên CPU mỗi frame |
| Tích hợp | Animator, Timeline của Unity | runtime riêng, phải khớp phiên bản với editor |

Chỉ xem lại khi một game cần constraint, path, physics mà 2D Animation không có. Một game không trộn hai hệ.

---

## 7. Clip **[ANIM]**

### 7.1 Loop

1. Cycle 3D và 2D skeletal: **key cuối giống hệt key đầu** trên mọi kênh (ví dụ clip 0–30, frame 30 cùng pose frame 0).
   Không cắt bỏ frame cuối: mất một khoảng nội suy, cycle bị giật.
2. Cycle sprite frame-by-frame thì ngược lại: 6.8 mục 4.
3. Unity: Loop Time bật. Key đầu và cuối khớp (mục 1) thì đèn loop match xanh. Loop Pose chỉ để chữa lệch nhỏ (mocap),
   không thay cho việc làm khớp.
4. Các clip locomotion trong một blend tree chạm đất cùng pha: chân trái chạm đất ở 0, chân phải ở 0,5 (thời gian chuẩn
   hoá). Lệch pha thì blend bị trượt chân. Clip mua về phải kiểm pha trước khi cho chung blend tree: pack ExplosiveLLC có
   vòng Strafe lệch vòng Run 0,15–0,4 chu kỳ (so cùng một chân), và chép một mốc chân cho cả 8 hướng chạy dù chân thật
   chạm đất lệch nhau *(đo)*. Sửa bằng Cycle Offset của clip (tab Animation lúc import); mốc chân lấy theo từng clip.
   "Chạm đất" ở đây là pose contact: gót vừa chạm, chưa nhận trọng lượng (7.7). Clip dựng từ pose khác (down, up) thì cắt lại
   cho frame 0 là contact của chân trái trước khi cho vào blend tree.
5. Đường về khác đường đi: tay, đầu, đuôi không đi rồi quay về trên đúng một cung (trông máy móc, như tua ngược). Đầu và thân
   đi chủ yếu lên xuống, không vẽ vòng tròn hay số 8 (Williams).
6. Soi chỗ nối (Annand): kéo cycle ra tới pose down kế tiếp (chép key của frame down sang sau frame cuối), xem chỗ nối như một
   đoạn giữa clip; cần thì chỉnh key hông, thân, tay ở chỗ nối, không đụng chân, rồi chép lại sang frame 0.
7. Bớt máy móc: nửa sau của cycle không chép y nửa đầu (silhouette, tay khác một chút), mốc chân vẫn ở 0 và 0,5 (mục 4).
   Idle, loco của nhân vật nền được dùng cycle dài hơn với các bước khác nhau nếu ngân sách cho phép (Williams).

### 7.2 Dời nhân vật: code dời, clip tại chỗ **[ANIM] [GD] [DEV]**

Đã chốt (12.1): **mọi clip tại chỗ (in-place), code dời và xoay nhân vật**, kể cả hành động có quãng dời và độ cao cú nhảy.
GD chỉnh tốc độ, quãng dời mà không phải làm lại clip; phản hồi nhanh; hợp NavMesh và physics.

1. `Root` đứng yên ở gốc, nhìn +Z, không có key dời hay xoay trong mọi clip. Nhún, nghiêng, xoay người nằm ở `Hips` trở
   xuống. Muốn xem trước quãng dời trong Blender thì dời `Root`, xong xoá key của `Root` trước khi xuất: cơ thể là con của
   `Root` nên pose vẫn đúng.
2. Clip locomotion ghi **tốc độ gốc** (m/s) = 2 × độ dài bước ÷ thời lượng cycle; độ dài bước là quãng bàn chân chạm đất
   trượt về sau trong một bước. Dev lấy số này làm ngưỡng blend tree, và đặt tốc độ phát = tốc độ gameplay ÷ tốc độ gốc, để
   chân không trượt. Tốc độ phát chỉ để khớp chân nên chỉ lệch ít quanh 1 *(đề xuất: 0,8–1,25)*; cần chậm, nhanh hơn nữa thì
   thêm clip vào blend tree, không kéo tốc độ phát: kéo dài clip chỉ ra quay chậm, không ra nặng (7.8).
   - **Chân trụ phẳng, trượt về sau đều** suốt đoạn chạm đất: 2 key dời (đầu và cuối đoạn), curve thẳng, vận tốc = tốc độ gốc
     (Annand). Có vậy chân mới đứng yên khi code dời đều (gate A-20). Tốc độ của thân trong một bước thì không đều (nhanh qua
     contact, passing; chậm ở down, up): phần không đều đó nằm ở hông dời tới lui nhẹ quanh `Root`, không nằm ở chân trụ.
   - **Kiểm trong Blender**: key tạm `Root` tiến đều đúng tốc độ gốc (trên một action thử hoặc một track NLA), xem gót và mũi có
     đứng yên trên đất không; chỗ hay trượt là chân sau lúc nhấc lên quanh pose down. Williams còn khuyên cho nhân vật đi thật vài
     bước (có dời) trước khi dựng cycle, để thấy bước chân thật. Xong xoá key tạm (mục 1).
3. Hành động có quãng dời (lao tới, lăn né, bị đánh bật): bảng frame data ghi quãng dời và khoảng frame dời (3.7). Code dời
   đúng quãng đó trong đúng khoảng đó; animator đặt bước chân theo khoảng dời (chân rời đất, hoặc bước đúng lúc code dời)
   để không trượt.
4. Nhảy: code tính quỹ đạo; clip chỉ có pose (bật, lên, rơi, tiếp đất), không đưa người lên cao.
5. Unity: Animator tắt Apply Root Motion. Rig Generic không đặt Root Motion Node: Unity khuyên vậy khi không dùng root
   motion, đỡ tốn. Clip nguồn có sẵn quãng dời (mocap, Mixamo) mà không có bản In Place: Root Transform Rotation và Position
   XZ không bake (Unity tách ra rồi bỏ đi, vì Apply Root Motion tắt); Position Y bake, trừ clip nhảy.
6. **Tại chỗ là không ai nhận root motion**: Apply Root Motion tắt *và* không component nào trên object có Animator viết
   hàm `OnAnimatorMove`. Có hàm đó thì Unity thôi tự dời và đưa root motion cho hàm, cả khi Apply Root Motion tắt *(đo: clip
   Run của pack ExplosiveLLC vẫn trả 4,9 m mỗi cycle cho `OnAnimatorMove`)*. Component đi kèm pack mua hay có hàm này
   (`RPGCharacterAnimatorEvents`): bỏ khỏi prefab dùng theo quy chuẩn (gate A-16).
7. Một nguồn dời cho một nhân vật: code, hoặc root motion (khi game chốt dùng), không cộng cả hai. Pack ExplosiveLLC cộng 1 m/s
   của code với 6,2 m/s root motion của clip chạy; NPC đi NavMesh 7 m/s trên clip 6,2 m/s nên chân trượt khoảng 12%.
8. Tốc độ gốc (mục 2) và quãng dời của clip có root motion (mocap, pack mua) đo trên đúng nhân vật dùng clip, bằng tool phát
   clip rồi cộng root motion (tool "Đo clip", 12.3). Không đọc `AnimationClip.averageSpeed`: clip Humanoid báo theo tỉ lệ,
   chưa nhân `humanScale`; clip Based Upon = Original dời bằng node gốc của file thì vẫn nhả root motion dù đã tick Bake
   Into Pose, trong khi `averageSpeed` báo 0 *(đo: lăn 6,7 m, knockback lùi 2,2 m ở pack ExplosiveLLC)*.

### 7.3 Moveset tối thiểu *(mẫu, GD chỉnh theo game)*

| Loại | Clip |
|---|---|
| Nhân vật người chơi, game combat | Idle, Idle_Break ×2, Loco Walk / Run (+ Start / Stop, Turn nếu cần; nhân vật nặng nên có Run_Stop, 7.7), Jump Start / Loop / End, Atk combo ×3, Skill ×N, Def (né), Hit Light (F, B, L, R), Hit Heavy hoặc Knockback, Knockdown + Getup (nếu game có ngã), Death, Spawn / Victory |
| Game nhiều vũ khí (RPG) | mỗi bộ (4.3) một moveset như trên, thêm Equip rút / cất, đòn khi đang chạy (layer UpperBody, 8.3); game có strafe thì Loco 8 hướng cho cả đi lẫn chạy. Mẫu: pack ExplosiveLLC, bộ tay không và bộ kiếm hai tay |
| Quái thường | Idle, Loco Walk hoặc Run, Atk ×1–2 (có telegraph), Hit, Death, Spawn |
| Boss | như quái, mỗi đòn một telegraph riêng, Hit không ngắt đòn lớn, chuyển phase |
| Nhân vật game không combat (Cozy, Puzzle) | Idle, Idle_Break ×2–3, Loco Walk (Run nếu cần), Interact (nhặt, trồng, tưới, mở…), Emote (vui, buồn, ngạc nhiên, thắng, thua) |
| NPC | Idle, Idle_Break, Talk, Emote ×N, Loco Walk |
| Vật thể, xe | Idle (nhún nhẹ, loop), Interact / Open, Break, Upgrade |

### 7.4 Event và cửa sổ gameplay **[ANIM] [DEV]**

Hai loại, hai cách làm:

| Loại | Ví dụ | Cách làm |
|---|---|---|
| Trang trí | bước chân, sfx vung tay, vfx, rung khi dậm đất | `AnimationEvent` theo danh sách dưới |
| Gameplay | bật / tắt hitbox, cancel window, bất tử khi né, thả đạn | lấy từ bảng frame data (3.7): code mở / đóng cửa sổ theo thời gian của state (StateMachineBehaviour), tự đóng khi rời state. Cancel window bật tham số `CanCancel` (8.3) |

Vì sao tách: mọi clip trong blend tree đều bắn event, và khi đang chuyển state, event có thể bắn từ **cả hai** state.
Hitbox mở bằng event có thể bật từ đòn vừa bị huỷ, hoặc không bao giờ đóng khi đòn bị ngắt giữa chừng. Project nhỏ được
dùng event cho gameplay nếu receiver giữ hai luật: bỏ qua event của state đang bị chuyển đi, và đóng mọi cửa sổ khi rời
state. **[DEV]**

Event chuẩn, do một receiver `AnimEventReceiver` nằm cùng object với Animator nhận:

| Hàm | Tham số | Đặt ở |
|---|---|---|
| `AE_Footstep` | int: 0 trái, 1 phải | frame gót chạm đất (pose contact, 7.7) |
| `AE_Sfx` | string: key `anim.<subject>.<tên>` | frame phát tiếng |
| `AE_Vfx` | string: key vfx | frame bật hiệu ứng (vị trí lấy từ socket) |
| `AE_Shake` | int: 1 nhẹ, 2 vừa, 3 mạnh | frame va chạm luôn xảy ra (dậm đất, tiếp đất nặng) |
| `AE_Prop` | string: `<vật>:<socket>`, ví dụ `Sword:Hand_R`, `Sword:Back` | frame tay nắm hoặc buông vật (rút, cất vũ khí): receiver đổi socket hoặc bật / tắt model |
| `AE_Signal` | string: tên tự do | chỉ khi các hàm trên không đủ; ghi vào brief |

1. Chỉ dùng hàm trong danh sách. Thêm hàm là sửa bảng này và receiver cùng lúc. Event không có receiver thì Unity báo lỗi
   "has no receiver" mỗi lần clip chạy.
2. Tiền tố `AE_` vì Unity gọi event giống SendMessage, tới mọi component trên object: tên chung chung như `Footstep` dễ
   gọi nhầm hàm của component khác.
3. Receiver lọc theo weight (`animatorClipInfo.weight` ≥ 0,5): clip đang mờ dần trong blend không kêu lần hai. **[DEV]**
4. Key sfx cùng kiểu UI Motion (`ui.<role>.<event>`): `anim.<subject>.<tên>`, chữ thường, ví dụ `anim.hero.swing_heavy`.
   Tiếng bước chân do code chọn theo mặt đất: `anim.footstep.<surface>`.
5. Không đổi tên take / clip sau khi đã đặt event trong import settings: event gắn theo clip.
6. `AnimEventReceiver` có sẵn trong prefab, không `AddComponent` lúc chạy (pack ExplosiveLLC gắn receiver lúc Awake nên
   không thấy, không nối được trong Inspector). Prefab nhân vật chia hai tầng như pack: gốc logic (collider, controller di
   chuyển) ở trên, object model có Animator và receiver ở dưới; thay model không đụng logic.
7. Event có thể không bao giờ bắn: clip bị ngắt trước frame đó. Khi rời state, receiver đồng bộ lại theo dữ liệu: vật đang
   cầm đúng socket, weight IK đúng. Pack ExplosiveLLC bật model kiếm bằng event và vá bằng coroutine đợi cờ.
8. Clip mua mang event tên khác: đổi sang bảng trên lúc làm clip theo chuẩn (8.8). Ví dụ pack ExplosiveLLC: `FootL` /
   `FootR` → `AE_Footstep`, `Land` → `AE_Sfx`, `WeaponSwitch` → `AE_Prop`, còn `Hit` là frame active của bảng frame data.
9. Hình không bao giờ đi sau tiếng (Williams): hình khớp tiếng, hoặc đi trước tối đa khoảng 4 frame ở 24 fps (170 ms); tiếng
   phát trước hình là sai. `AE_Sfx` đặt đúng frame hình (frame va chạm là frame đã bật khỏi điểm chạm, 3.4), không đặt sớm hơn:
   máy nào cũng cộng thêm trễ âm thanh, nên trên máy hình tự đi trước tiếng. Đo lại ở mốc duyệt trên máy (9.1); tiếng trễ quá
   mức trên thì báo dev, audio.

### 7.5 Va chạm có lực (game feel) **[ANIM] [DEV]**

Mọi thứ dồn vào frame active đầu tiên của đòn:

| Thành phần | Ai làm | Bắn khi |
|---|---|---|
| Pose va chạm: đã qua điểm chạm, là accent cứng (3.4, 7.6) | animator | luôn có, đọc được bằng silhouette |
| Sfx vung, vệt vũ khí | animator đặt event | luôn có (`AE_Sfx`, `AE_Vfx`) |
| Hitstop (3.4) | dev | chỉ khi **trúng thật**: đòn trượt không có hitstop |
| Hit react của bên bị đánh | animator | khi trúng, hướng theo đòn. Frame đầu đã lệch khỏi pose đứng (đầu, thân bật ra), không khởi đầu từ pose đứng yên; đòn cứng thì bật về như accent cứng |
| Rung camera, vfx và sfx trúng đòn | dev, vfx, audio | khi trúng |
| UI: HP tụt, số damage, HUD rung | UI Motion | khi trúng: `UIMotionBar`, `UIDamageTextSpawner`, `UIMotionScreenFx.Main.Hit()` |

Trong lúc hitstop: bên bị đánh rung (ngang khi đứng đất, dọc khi trên không, lệch qua lại mỗi frame, biên độ tắt dần như
công thức stagger ở 7.6), bên đánh gần như đứng yên, hitbox không dời. Khoảnh khắc lớn không phải cú đánh (kết liễu, lên cấp)
cũng dùng được: dừng ngắn rồi chậm dần về tốc độ thường.

**[DEV]** Hitstop dừng từng nhân vật bằng `Animator.speed = 0` trong N frame rồi trả về 1, không dùng `Time.timeScale`:
timeScale dừng cả physics, particle và mọi nhân vật khác (tween của UI Motion chạy thời gian thật nên vẫn chạy).
`Time.timeScale` chỉ cho làm chậm cả cảnh có chủ đích (kết liễu, thắng màn). Pack ExplosiveLLC có cả hai: tham số `AnimationSpeed` nhân vào từng state, và `SlowTime` đổi `Time.timeScale`.
Bị đẩy lùi: code dời đúng quãng và khoảng frame trong bảng frame data (3.7), không `AddForce` với lực ngẫu nhiên như pack
(8 ± 4 nên mỗi lần bay một khác, lại cộng thêm root motion của clip).

Game không combat: cùng cách nghĩ cho tương tác (nhặt, thu hoạch, ghép đúng). Pose rõ ở frame tương tác; sfx, vfx và UI
(ví dụ `UIMotionFlyTo` cho vật bay về túi) bám cùng frame đó; không hitstop, không rung camera mạnh.

### 7.6 Pose, timing, spacing **[ANIM]**

Tay nghề nền cho mọi clip 2D và 3D, rút từ Richard Williams (*The Animator's Survival Kit*, bản mở rộng) và Jonathan Annand
(*Animation Craft for 3D and 2D Animators*). Sách tính ở 24 fps; số dưới đây đã đổi ra 30 fps (3.1), số gốc để trong ngoặc khi
cần.

1. **Timing và spacing là hai việc.** Timing: frame nào các mốc xảy ra (chạm, va chạm, đổi hướng). Spacing: các vị trí liền nhau
   cách nhau bao xa. Cùng timing mà spacing khác là hai chuyển động khác hẳn. Frame data (3.7) chốt timing; spacing là việc của
   animator, và là chỗ 3D hay hỏng nhất: nội suy mặc định của Blender, Unity chia đều quanh key và làm dẹt cung, nên chuyển động
   trôi, nhũn. Spacing phải được làm, không để tiếp tuyến tự động quyết.
2. **Thứ bậc pose.** Key (kể chuyện) → extreme (đổi hướng, chạm, lấy đà) → breakdown → inbetween. Breakdown quyết cung và cảm
   giác của đoạn chuyển, cũng là chỗ chọn cách diễn: cùng hai pose, breakdown khác cho nghĩa khác. Thường mỗi đoạn chuyển một
   breakdown; đoạn nhanh hoặc cung phức tạp thì thêm. Breakdown sai thì mọi inbetween sai theo, nên breakdown được duyệt trước khi
   nội suy (9.1).
3. **Ba kiểu inbetween**: ease ra (rời pose chậm rồi nhanh dần), ease vào (nhanh rồi chậm dần vào pose), đều (đi xuyên một đoạn
   với tốc độ không đổi, như lúc kiếm lướt qua vùng trúng). Ease làm bằng chia đôi về phía pose: 1/2, 1/4, 1/8 (con lắc của
   Williams: 0, 1/8, 1/4, 1/2, 3/4, 7/8, 1). Breakdown favor về một pose (một phần ba, hoặc sát hẳn) đổi cảm giác mà không thêm
   frame: hợp đổi hướng gắt, động tác lanh lợi. Mỗi bộ phận một spacing riêng (ví dụ của Annand: hông chia đôi, đầu một phần ba,
   tay favor); 3D là mỗi control một curve. Spacing đều từ đầu tới cuối chỉ dành cho vật máy móc.
4. **Đổi hình dáng.** Mỗi key đổi silhouette so với key trước, không chỉ dời chỗ, và có đảo chiều giữa các pose (đầu ngẩng thì
   hông lùi nhẹ). Nhân vật cứng đơ là vì các pose không đổi hình dáng.
5. **Va chạm, pop.** Vào va chạm nhanh dần, không ease vào; khoảng cuối trước lúc chạm là khoảng rộng nhất: xa thì va mạnh, gần
   thì va nhẹ, rộng quá thì pop. Động tác bật (đá, "ta-da", lướt): từ pose lấy đà nhảy sang pose khác hẳn trong 1 frame rồi
   cushion khoảng 9–11 frame favor pose mới (Annand: 7–9 frame ở 24 fps). Nhanh quá không đọc được thì thêm **một** inbetween
   favor một bên; không chèn inbetween chia đôi, vì nó xoá độ bật.
6. **Accent.** Cứng: tăng tốc vào một pose vọt qua (cảm được, không cần thấy) rồi bật lại khoảng 5 frame vào pose để đọc (đấm,
   đá, búa, súng giật). Mềm: tăng tốc vào rồi trôi tiếp, lắng dần (chém lướt, vẫy, ngồi xuống đệm). Trục Bounce của mood (2.1)
   nghiêng về kiểu nào thì dùng kiểu đó nhiều hơn. Đường bật lại theo cung, không quay về trên đúng đường vào. Khi dừng, thân, tay,
   vải mỗi phần một accent riêng, rồi chớp mắt.
7. **Đủ thời gian để đọc.** Accent ngắn hơn 5 frame thì không đọc được (Williams: 4 frame ở 24 fps); pose, accent muốn đọc rõ thì
   giữ ≥ 8 frame (250 ms; Williams: 6 ở 24 fps). Bốn accent trong một giây gần như không đọc nổi. Gameplay không cho đủ frame thì
   đơn giản hoá, không làm vội: kéo pose giữa bớt đi 1/3, chưa đủ thì bớt 1/2 (Annand). Lỗi hay gặp nhất của người mới là quá
   nhiều hành động trong quá ít thời gian (Williams): clip không có frame data ràng buộc (emote, idle break) thì thử chậm gấp đôi.
8. **Không đứng chết.** Pose đứng yên hoàn toàn quá khoảng 1 s thì chết. Giữ lâu thì dùng moving hold: trôi rất nhẹ tới một pose
   mạnh hơn một chút, tay, mắt, vải vẫn sống, dừng thì chớp mắt. Không để mọi bộ phận dừng (hay bắt đầu) cùng một frame. Hitstop
   (3.4) là dừng cố ý, không tính. Idle cũng vậy: biên độ tối thiểu đo được ở 5.10 mục 5.
9. **Overlap, khớp gãy lần lượt.** Bộ phận dẫn đi trước, các khớp sau trễ lần lượt: vai → khuỷu → cổ tay → ngón (Annand: khuỷu 5,
   cổ tay 9, bàn tay 13, ngón 18 ở 24 fps). Các khớp chồng lên nhau, không xong khớp này mới tới khớp kia: làm vậy trông như
   robot. Phần bị kéo (tóc, vải, bụng, đuôi) trễ ≥ 2 frame, mỗi đốt xa hơn trễ thêm ≥ 1 frame; khi thân dừng, chúng chạy tiếp rồi
   lắng. Thiếu frame thì lệch 1 frame cũng đủ. Đầu và thân không đi thành một khối. Ngoại lệ: accent kiểu cartoon cho mọi bộ phận
   cùng chạm một frame, rồi phần bị kéo theo đà sau đó.
10. **Take** (giật mình, phát hiện người chơi, ngạc nhiên): lấy đà nén xuống → accent giãn lên, chỉ hiện 1–2 frame → lắng vào pose
    để đọc. Williams: kiểu Disney 16 frame (20 ở 30 fps); kiểu Warner 14 frame (khoảng 18) bật thẳng từ nén sang giãn, không có
    inbetween. Mood Tempo cao hợp kiểu Warner, Tempo thấp hợp kiểu Disney. NPC, quái có một nhịp nghĩ trước khi phản ứng; nhân vật
    người chơi thì không (3.2).
11. **Rung (stagger).** Rung, run, cười, khóc: làm chuyển động bình thường rồi phát các hình theo thứ tự xáo. Dãy đều 1…9 phát
    1, 3, 2, 4, 3, 5… (Williams); rung tắt dần về giữa: 1, 17, 2, 16, 3, 15… tới 9. Biên độ tăng rồi tắt, không lắc đều như máy.
    2D frame-by-frame dùng lại hình có sẵn; 3D và code lấy làm mẫu cho rung lúc hitstop (7.5).
12. **Whip.** Đầu roi, đuôi, dây, áo choàng bật thẳng trong 1 frame, có 1 frame ngay trước để dẫn mắt, thả lỏng ở frame sau. Sóng
    (wave) là whip không có cú bật: ease ở hai đầu.
13. **Nhịp.** Frame mỗi nhịp = fps × 60 ÷ bpm: 120 bpm là 15 frame ở 30 fps. Emote, dance theo nhạc đặt pose trên nhịp hoặc sớm
    vài frame. Không có nhạc thì chạy metronome lúc blocking để giữ tempo (Annand). Nhịp đều đều là lỗi: xen đoạn hơi nhanh với
    đoạn hơi chậm (Williams).
14. **Kiểm nhanh**: lật qua lại giữa các key (như lật giấy) xem có đổi hình dáng; chơi ngược clip, chỗ dừng và chỗ bắt đầu vẫn
    gọn như phim người thật tua ngược; xoá thử một key, chuyển động không kém đi thì bỏ luôn key đó.

### 7.7 Đi, chạy, nhảy **[ANIM]**

Công thức mặc định từ Williams và Annand; tính cách, mood (2.1) và brief (9.2) thay đổi từ đây. Nhịp: 3.6.

**Đi.** Mỗi bước 4 pose. Cycle 2 bước; contact của chân trái ở frame 0 (thời gian chuẩn hoá 0), của chân phải ở 0,5 (7.1).

| Pose | Bước 12 frame ở 24 fps | Bước 15 frame ở 30 fps | Là gì |
|---|---|---|---|
| Contact | 1 / 13 (25 = 1) | 0 / 15 (30 = 0) | gót chân trước vừa chạm đất, chưa nhận trọng lượng; hai chân dang rộng nhất, duỗi thẳng; thân ở độ cao giữa |
| Down | 4 / 16 | 4 / 19 | người thấp nhất, gối chân trước chùng, nhận trọng lượng; tay vung xa nhất |
| Passing | 7 / 19 | 8 / 23 | chân sau lướt qua chân trụ đã duỗi thẳng; người cao hơn contact một chút; tay ở đáy cung |
| Up | 10 / 22 | 11 / 26 | người cao nhất, chân trụ đẩy, người ngã về trước vào contact kế tiếp |

Ở 30 fps, down và up của bước 15 frame rơi giữa hai frame nên phải làm tròn; bước 16 frame thì key rơi đúng frame (0, 4, 8,
12) nhưng đi chậm hơn 7%. Cả project chọn một kiểu (12.2).

1. **Contact trước** (Milt Kahl): contact quyết độ dài bước và thái độ (bước dài: tự tin; bước ngắn: thư thả), rồi passing, rồi
   down và up. Muốn chế kiểu đi lạ thì dùng cách của Art Babbitt: dựng hai pose down trước.
2. **Passing là nút chỉnh tính cách.** Giữ contact, chỉ đổi passing: nâng passing lên cao nhất thì contact thành điểm thấp; hạ
   passing xuống thì ra kiểu đi cartoon; ngoài ra dời ngang, nghiêng đầu, trễ bàn chân. Down càng tương phản passing thì bước
   càng kịch tính.
3. **Trọng lượng nằm ở nhịp lên xuống.** Đi phẳng, đầu không lên xuống là không có trọng lượng (trượt như đi dây). Mocap, clip mua
   đi phẳng quá thì đẩy thêm lên xuống (mục 2, dòng 10).
4. **Spacing** (Williams): nhanh qua contact và passing, chậm vào và ra ở down và up.
5. **Bàn chân**: gót đi trước; từ contact tới cả bàn chân phẳng trong 2 frame ở 24 fps (2–3 ở 30), không "trôi" xuống; chân trụ
   khoá phẳng (7.2); mũi rời đất sau cùng. Nhấc chân chậm, qua giữa nhanh, đặt xuống nhanh; chân chỉ nhấc vừa đủ qua đất.
6. **Tay** vung ngược chân, xa nhất ở down, thấp nhất ở passing; cổ tay dẫn, bàn tay kéo theo; đổi chiều theo cung tròn, không
   quay về đúng đường đi. Tay chân cùng chiều là lỗi người mới.
7. **Hông, vai, đầu**: nhìn từ trước, hông nằm giữa hai chân, dời ngang về phía chân trụ (nhiều nhất ở passing) và xoay về phía
   chân trước; vai ngược hông, lên ở contact, xuống ở passing. Đầu đi chủ yếu lên xuống; cằm cúi ở điểm cao, ngẩng ở điểm thấp,
   mắt nhìn ổn định.
8. **Double bounce**: nhún hai lần mỗi bước (Williams, bước 16 ở 24 fps: down 1, up 5, down 9, up 13, down 17). Cần bước dài hơn
   để kịp đọc (Annand: 16 thay vì 12 ở 24 fps, tức 20 ở 30).
9. **Đi càng nhanh càng ngả về trước**; đi chậm thì cân bằng hơn.
10. **Mỗi nhân vật một kiểu đi**: nhìn từ sau vẫn đoán được tuổi, sức khoẻ, tâm trạng. Williams: người say đầu đứng yên, các phần
    đi lệch nhau, nhịp thất thường; người giận dậm chân xuống nhanh; trẻ con nhấc chân cao; lên bậc thì chân đặt phẳng, xuống bậc
    thì nén thêm lúc chạm.
11. **Soát lượt polish** (15 mục "recipe" của Williams): người ngả; chân thẳng ở contact và lúc đẩy; thân xoắn, vai và hông ngược
    nhau; gối lật vào hoặc ra; đai lưng nghiêng; bàn chân lật; chân, ngón rời đất trễ; đầu nghiêng hoặc tới lui; các phần lệch
    nhau, không cùng đi; thịt, vải, tóc ngược nhịp lên xuống; khớp gãy; đủ lên xuống; chân, tay, đầu, thân mỗi phần một nhịp;
    bàn chân lệch khỏi song song; ít nhất một chi tiết khác kiểu sáo mòn.

**Chạy.** Như đi, nhưng pose up có cả hai chân rời đất (3.6); contact là một cú tiếp đất, chân sau kéo theo; down của chạy giống
passing của đi.

| Pose | Bước 8 frame ở 24 fps (Annand) | Bước 10 frame ở 30 fps (làm tròn) |
|---|---|---|
| Contact | 1 / 9 | 0 / 10 |
| Down | 3 / 11 | 3 / 13 |
| Passing | 6 / 14 | 6 / 16 |
| Up (bay) | 7 / 15 | 8 / 18 |

1. Người lên ở pose up tối đa nửa đầu (hoặc 1/3), không bao giờ cả một đầu (Williams).
2. Càng nhanh càng ngả; vào cua thì ngả vào tâm cua, hông dẫn, như xe máy.
3. Chạy rất nhanh: chân là chính, tay vung ít lại; tay nhiều quá thì rối mắt.
4. Nặng: hông xuống nhiều giữa down và up, nhiều frame quanh down, up thấp, ít frame bay. Nhẹ: up cao, nhiều frame bay.
5. Nửa sau của cycle không chép y silhouette nửa đầu (cao hơn hoặc thấp hơn một chút) để hai chân đọc được (7.1).
6. Chạy nhanh được trượt chân chút ít, đi thì không (Williams). Gate A-20 để ngưỡng rộng hơn cho clip chạy nhanh.
7. Dừng, đổi hướng mới bán được trọng lượng: Run_Stop có trượt, một lúc mất thăng bằng, lưng đảo chiều, các phần dừng lệch nhau
   (Williams). Nhân vật nặng, game Action và Midcore nên có clip dừng, xoay gắt; chỉ giảm tốc bằng code thì trông không có trọng
   lượng. Clip dừng vẫn bắt đầu ngay khi thả input (3.2).

**Nhảy.** Code tính quỹ đạo (7.2); clip có các pose: nén lấy đà, bật (giãn 1 frame: cảm được, không cần thấy), lên, đỉnh, rơi,
tiếp đất (giãn 1 frame rồi nén), lắng.

1. Người chơi: nén gộp vào frame bật, rời đất ở frame 1–2 (3.2). Nhảy đứng của Williams nén khoảng 6 frame ở 24 fps rồi thêm
   4 frame mới rời đất: chỉ dùng cho NPC, quái, cinematic.
2. Đỉnh có ease, rơi nhanh dần; tiếp đất cushion vào pose nén, từ nén tới đứng yên khoảng 12–18 frame (Williams: 10–14 ở 24 fps).
   Pose trên không phải cân (7.8).
3. Trên không phải có gì đó đang chuyển động (tay, chân, tóc, một chân trễ); nhảy mà đóng băng giữa không trung thì trôi. Loop lên
   và rơi của 2D cũng vậy (3.6).

**Thú bốn chân, chim** (Williams). Thú bốn chân là hai người đi lệch pha: đôi chân trước, đôi chân sau mỗi đôi có nhịp lên xuống
riêng; đầu lên sau ngực 2–4 frame (24 fps); lồng ngực đứng yên nhất. Nhịp chậm (đi) dựng từ contact (ngựa: chân sau); nhịp nhanh
(phi) dựng từ khối thân, hông. Cycle ở 24 fps (trong ngoặc: 30 fps): chó đi 20 (25), chó chạy 14 (khoảng 18), mèo chạy 18 (khoảng
23), ngựa đi 32 (40), ngựa chạy nước kiệu 16 (20), ngựa phi 12 (15). Chim vỗ cánh kiểu cartoon 8 (10), tả thực 18 (khoảng 23);
thân hạ khi cánh lên, nhấc khi cánh đập xuống.

### 7.8 Trọng lượng, thăng bằng, đặt chân **[ANIM]**

Nguyên lý 13 (mục 2). Theo Annand, thiếu trọng lượng là lỗi hay gặp nhất của animation 3D kém: chạy như rối treo dây, xoay nửa
người mà không có bước chân, nhảy lên vật mà không tốn sức.

1. **Trọng tâm.** Mọi frame biết trọng tâm (của cả người lẫn vật đang cầm) ở đâu, đi từ đâu, qua đâu, sang đâu. Nằm trên chỗ đỡ
   (bàn chân, tay, gối), không thì đang ngã, và ngã phải có chủ đích. Mỗi bước đi là một lần ngã về trước được bàn chân đỡ lại;
   chạy là ngả qua trọng tâm rồi đỡ lại. Ngả càng nhiều trông càng nhanh.
2. **Chân trụ trước.** Trọng lượng sang chân trụ rồi chân kia mới được nhấc. Xoay 180° tại chỗ: nhìn hướng mới trước, rồi 3 bước
   (phải khoảng 90°, trái, phải), bước nào cũng dồn trọng lượng xong mới nhấc chân kia; bản nhanh xoay trên mũi một chân.
3. **Nặng, nhẹ.** Nặng: trọng tâm thấp, biên độ nhỏ, đổi hướng lâu, nhiều frame chân chạm đất, ít frame trên không; rơi nhanh,
   nảy thấp và ngắn; nâng tay, cánh nặng thì lên thấp hơn. Trọng lượng chỉ hiện qua hành động: sức phải bỏ ra để bắt đầu, đổi
   hướng hay dừng cho thấy vật nặng bao nhiêu (Williams). Việc nặng bắt đầu từ hông, việc nhẹ từ khuỷu tay, cổ tay.
   - Vật cầm có trọng lượng riêng: cẳng tay trễ xuống, vai bị kéo, người nghiêng ngược lại.
   - Nâng, đẩy vật nặng thì chuẩn bị trước: chân dang, gối chùng, áp sát vật, lấy đà xuống trước mỗi lần gắng; lưng cong ra khi
     với, ngả về sau khi giữ. Nâng mà không chuẩn bị thì tảng đá trông như cục xốp.
   - Mang vật nặng là một bộ loco riêng: gối chùng, chân nhấc thấp, vật giữ ngang, bước chậm lại.
4. **Không làm nặng bằng tốc độ phát.** Kéo dài clip gấp đôi chỉ ra quay chậm, không ra nặng. Clip nặng, nhẹ là clip riêng hoặc
   làm lại spacing, không dùng `Animator.speed` hay Multiplier (7.2).
5. **Mỗi nhân vật một trọng lượng**, ghi trong brief (9.2). Hai nhân vật khác cỡ không đi, không cầm vật giống nhau.
6. **Chân chạm đất không trượt** trong clip. Loco tại chỗ: chân trụ phẳng, trượt về sau đều (7.2, 7.7). Xoay tại chỗ: xoay trên mũi
   bàn chân, ngón cái làm trục, ngón đứng yên suốt các inbetween (kiểm ở nhiều góc). Không có lý do thì nhấc chân bước, không lết.
   Đẩy người lên (nhảy, nhón gót): gót nhấc, mũi và ngón cái đứng yên.
7. **Tiếp đất.** Bàn chân đi theo một đường tới đúng chỗ đặt: tiếp đất bằng gót thì gót thẳng hàng từ pose rơi tới pose chạm, bằng
   mũi thì mũi thẳng hàng. Không để chân lệch đường rồi giật vào chỗ ở frame cuối. Trên không cũng giữ thăng bằng: pose trên không
   cân thì tiếp đất mới tin được.
8. **Pose trung tâm.** Frame 0 của idle mỗi bộ là pose trung tâm: chân trung tính, hai gót chạm đất, trọng lượng trên một chân
   đã ghi. Clip vào từ idle và về idle thì bắt đầu và kết thúc ở pose này (gate A-22 trong Unity, gate của rigkit trong Blender,
   5.10 mục 4); clip nối thẳng sang clip khác (Knockdown → Getup) thì
   pose cuối clip trước = pose đầu clip sau. Hai pose gần nhau thì blend ngắn (3.5) vẫn mượt; blend dài không cứu được hai pose
   lệch xa. Idle không kiễng gót: transition của game ngắn, thêm frame hạ gót là bớt frame chân phẳng, mất trọng lượng. Hai clip
   blend với nhau thì trọng lượng nằm trên cùng một chân ở điểm chuyển (Annand).
9. **Blend vẫn trượt chân chút ít**, không tránh hết được (Annand). Foot IK của Unity chỉ có ở Humanoid (8.6), nên với rig Generic
   phải giảm bằng các luật 6–8.

### 7.9 Mặt, lời thoại **[ANIM]**

Cho layer Face (8.3), clip diễn (emote, NPC nói, idle break, hit react có mặt) và cinematic (8.7). Ở camera gameplay, mặt chỉ cần
biểu cảm lớn, đơn giản và chớp mắt; thân phải tự nói được ý khi không thấy mặt. Chi tiết mặt dành cho camera gần.

1. **Ba vùng**: trán và mày; mắt và gốc mũi; má, miệng, cằm. Vùng nào cũng kéo vùng khác: mày nâng thì mí trên mở theo; nheo mắt
   và nâng mày thì má lên.
2. **Bảy biểu cảm** đọc được ở mọi nơi (Annand, theo Ekman): giận, buồn, sợ, ngạc nhiên, ghê, khinh, vui. Mỗi cái có một dấu hiệu
   chính phải còn thấy ở kích thước màn hình: giận dùng cả ba vùng (mày hạ và chụm, môi mím vuông); vui nằm ở nửa dưới mặt và mí
   dưới (khoé miệng lên, má lên); buồn là mọi thứ rũ xuống, đầu trong của mày nâng; sợ khác ngạc nhiên ở chỗ mày vừa nâng vừa chụm.
   Kiểm: đưa đồng nghiệp xem pose, gọi đúng tên.
3. **Giữ biểu cảm** quá khoảng 4 s mà không có lời, cử chỉ thì trông giả: mặt của idle, NPC đứng phải đổi hoặc chớp trong khoảng
   4 s, trừ biểu cảm kiểu ký hiệu (nháy mắt).
4. **Đổi biểu cảm** có lấy đà và breakdown: mày đi trước, rồi mắt, rồi miệng, mỗi bước cách 3–4 frame. Breakdown quá gắt thì tách:
   nửa trên theo biểu cảm cũ, nửa dưới theo biểu cảm mới. Đổi trước hoặc sau một động tác lớn, không đổi giữa chừng (Williams).
5. **Chớp mắt** khoảng 9 frame (Williams: 7 hình ở 24 fps): rời mắt mở chậm, khép nhanh, nhắm 2–3 frame, rời mắt nhắm chậm, mở
   nhanh. Chớp ở chỗ đổi hướng khi quay đầu, liếc mắt (con ngươi đi trước), và khi dừng. Mí dưới lên gặp mí trên ở frame nhắm. Mắt
   đang nghe chuyện đảo từng nhịp ngắn, không trôi đều.
6. **Lời thoại.** Thứ tự làm: thái độ → nhấn bằng đầu, thân → cả người tiến về đâu đó trong lúc nói → miệng làm sau cùng
   (Williams). NPC nói không lặp tại chỗ với cái miệng mấp máy: thân nghiêng tới, trôi, có 1–3 điểm nhấn mỗi câu.
   - Làm theo âm, không theo chữ viết; dùng ít khẩu hình nhất có thể (Williams: "How are you?" 3 khẩu hình là tốt nhất, 7 là sai).
     Bảng 9 khẩu hình chỉ là điểm xuất phát; mỗi nhân vật một kiểu miệng (lộ răng trên hay răng dưới).
   - Phụ âm khép (M, B, P) giữ ≥ 3 frame (≥ 2 ở 24 fps). Track chỉ cho 1 frame thì mượn frame của âm đứng trước, không mượn của âm
     sau. Phụ âm giữa chuỗi phụ–nguyên–phụ–nguyên được 1 frame.
   - Bật thẳng từ phụ âm khép sang nguyên âm mở, rồi cushion; inbetween của miệng favor một bên, chia đôi thì miệng nhoè.
   - Nuốt âm, không mở khép cho từng chữ. Lưỡi lên hoặc xuống, không lơ lửng ở giữa. Răng trên gắn với sọ, không bao giờ dời.
   - Miệng khớp tiếng, hoặc sớm hơn vài frame (Williams: 1–3 frame ở 24 fps, thử ở khớp tiếng trước rồi đẩy sớm dần), không bao
     giờ trễ. Nhấn bằng đầu, thân, tay đi trước tiếng khoảng 4–5 frame (Williams: 3 frame nếu on ones, 4 nếu on twos; Annand: 3
     frame ở 24 fps).
   - Hết câu thì giữ khẩu hình của chữ cuối hoặc biểu cảm cuối, để layer Face blend ra; không key khép miệng ở cuối clip.
   - Tắt tiếng vẫn hiểu ý câu nói.

### 7.10 Rig cần có cho các luật trên **[RIG]**

Phần điều khiển nằm trong file nguồn (control rig của Blender), không xuất sang Unity; xương deform vẫn theo 5.2.

1. Tay FK mặc định, chuyển IK khi tay chạm hoặc bám vật (FK nội suy ra cung đẹp hơn); chân IK mặc định, FK khi lộn, xoay người
   trên không (Annand).
2. Bàn chân có trục xoay ở gót, mũi bàn chân và đầu ngón cái: lăn bàn chân (gót → phẳng → nhón) và xoay tại chỗ mà ngón không
   trượt (7.8).
3. Biên độ nén, giãn của nhân vật (pose nén nhất, giãn nhất so với pose trung tính) nằm trong character sheet; rig Generic nén giãn
   theo luật scale ở 5.2.
4. Mặt: điều khiển gom theo ba vùng (7.9), có mí dưới, mày kéo mí trên, má phồng / xẹp, hàm quay quanh khớp. 2D: bộ sprite mắt,
   miệng đủ khẩu hình, đổi bằng Sprite Resolver (6.5).
5. Chuỗi tóc, vải, đuôi: mỗi đốt một xương, để trễ được từng đốt (7.6).
6. Clip trái / phải của rig Generic thì lật trong Blender: Mirror của state trong Unity chỉ chạy với Humanoid.

---

## 8. Vào Unity **[TA] [DEV]**

### 8.1 Import model 3D

| Tab | Mục | Chuẩn |
|---|---|---|
| Model | Scale Factor | 1 |
| Model | Convert Units | bật; dòng bên cạnh phải là "1m → 1m" (Blender, FBX Units Scale) hoặc "1cm → 0.01m" (Maya) |
| Model | Bake Axis Conversion | tắt (chỉ bật nếu team chọn cách xuất không xoay, 5.7) |
| Model | Import BlendShapes | chỉ bật cho mesh có blendshape |
| Model | Import Visibility / Cameras / Lights | tắt |
| Model | Read/Write | tắt (bật là giữ thêm một bản mesh trong RAM) |
| Rig | Animation Type | Generic; Humanoid theo 5.4 |
| Rig | Avatar Definition | file rig: Create From This Model; file chỉ có animation: Copy From Other Avatar (avatar của `<Subject>_Rig` hoặc của rig archetype) |
| Rig | Root node (Generic) | None: không dùng root motion (7.2) |
| Rig | Skin Weights | Standard (4 Bones) |
| Rig | Strip Bones | bật |
| Rig | Optimize Game Objects | bật cho nhân vật ship, khai socket ở Extra Transforms to Expose. Tắt khi dùng Animation Rigging hoặc code ghi thẳng vào xương |
| Animation | Anim. Compression | Optimal, sai số 0,5° / 0,5% / 0,5%. Tay, prop của nhân vật chính 0,2–0,3; NPC ở xa tới 1,0 sau khi duyệt. Không tắt nén |
| Animation | Remove Constant Scale Curves | bật |
| Animation | Loop Time, Root Transform | theo 7.1, 7.2 |
| Animation | Events | theo 7.4 |

Avatar Humanoid dùng để retarget (5.8): bind pose đã là T-pose nên Avatar thường khớp ngay; Unity báo lệch thì dùng
Enforce T-Pose. Lưu bảng map thành Human Template (`.ht`) cho archetype để lần sau map tự động.

### 8.2 Import 2D

PSD: 6.2 (chọn PSD Importer). Aseprite: Import Mode Animated Sprite, Pivot Bottom, PPU theo project (importer mặc định 100). Pixel art giữ
Point / None / không mip map (đúng mặc định của importer).

### 8.3 Animator Controller

1. **Mỗi archetype một controller gốc** (`HumanM_Base`); mỗi nhân vật là một **Animator Override Controller** chỉ thay clip.
   State machine giống nhau thì code điều khiển giống nhau.
2. **Bố cục** (học từ pack ExplosiveLLC, chỉnh theo quy chuẩn):

   ```
   Base
   ├─ Any State → Death, Knockdown, Knockback, Hit…     ngắt được mọi lúc, xếp theo ưu tiên (mục 6)
   ├─ <Bộ>                      sub-state machine, một cái mỗi bộ (4.3); game một bộ thì bỏ tầng này
   │  ├─ Idle                   blend tree 1D các idle (thở, tĩnh)
   │  ├─ Loco                   blend tree locomotion (mục 4)
   │  └─ Atk, Skill, Def, Jump, Hit, Equip, Interact…   mỗi nhóm (4.3) một sub-state machine, xong thì ra Exit
   │     Exit của mọi nhóm → state machine cha chọn: !IsMoving → Idle, IsMoving → Loco
   UpperBody   Override, mask thân trên: state chờ + hành động làm được khi đang chạy (đánh, rút / cất vũ khí)
   Additive    thở, giật
   Face        biểu cảm mặt
   ```

   Đi qua Exit của một sub-state machine rồi Entry của cái khác không tốn frame *(đo: Idle → chạy đổi pose ngay frame
   đầu)*. Layer có weight 0 thì Unity bỏ qua cập nhật.
3. **State chờ của layer phụ** (UpperBody, Face…): Motion = None thì Write Defaults **bật**. Controller để Write Defaults tắt
   thì state chờ dùng một clip rỗng (không curve), không để None *(đo, cả Humanoid lẫn Generic: None + Write Defaults tắt
   làm thân trên sai pose; Generic kẹt ở pose cuối của hành động, Humanoid lệch ngay cả khi chưa có hành động nào)*. Hành
   động của layer ra bằng Exit Time về state chờ, blend 0,1–0,25 s. Clip thân trên có thể là clip toàn thân (pack: vừa chạy
   vừa đấm): mask chỉ lấy nửa trên, chân vẫn theo Loco ở Base.
4. Blend tree locomotion:
   - 1D theo `Speed` (m/s), ngưỡng = tốc độ gốc của clip (7.2).
   - Có đi ngang, đi lùi: 2D **Freeform Directional** theo `MoveX`, `MoveZ` (vận tốc cục bộ, m/s), có một clip ở (0, 0).
     Manual Unity dành Freeform Cartesian cho trục không phải hướng (tốc độ thẳng + tốc độ xoay); pack dùng Cartesian cho
     hướng.
   - Vị trí mỗi clip = vận tốc thật của clip: Compute Positions > Velocity XZ với clip còn root motion, hoặc gõ tốc độ gốc
     đã ghi (7.2). Không gõ vị trí theo input chuẩn hoá (chạy 1, đi 0,5) như pack.
   - Các clip trong một vòng (cùng mức đi hoặc chạy) nên cùng tốc độ gốc. Lệch thì code giới hạn tốc độ theo hướng bằng tốc
     độ của clip hướng đó *(đo: pack chạy thẳng 6,2 m/s, chạy chéo 5,3 m/s, ngang và lùi 5,4 m/s)*.
5. **Vào hành động**:
   - Transition gameplay: **tắt Has Exit Time** (Unity bật sẵn khi tạo bằng tay), **Fixed Duration** bật, thời lượng theo
     bảng 3.5 (Unity mặc định 0,25 s). Interruption Source chỉ dùng khi phải ngắt cả một transition đang chạy.
   - Đi thẳng tới state của hành động. **Không đặt state trống ở giữa** để rẽ nhánh theo tham số: mỗi state trung gian tốn một
     frame *(đo: pack đi Any State → `Attacks` trống → đòn, pose đòn trễ 1 frame, 33 ms ở 30 fps)*. Code muốn tự quyết
     (combo, cancel theo frame data) thì gọi `Animator.CrossFadeInFixedTime(hash, blend)` thẳng vào state (12.2).
   - Tham số: một trigger cho mỗi nhóm (`Attack`, `Skill`, `Dodge`, `Hit`, `Jump`…), int `Variant` chọn biến thể 1…N, int
     `Stance` chọn bộ. Không dùng một trigger chung kèm số nhóm (`Trigger` + `TriggerNumber` của pack): số phải khớp tay
     giữa code và controller, sai thì im lặng không chạy, và transition nào cũng phải kiểm 4–6 điều kiện.
   - Trigger chỉ tự xoá khi có transition dùng nó (manual Unity). Hành động bị từ chối (đang khoá, sai trạng thái) thì code
     gọi `ResetTrigger`, không để trigger treo rồi bắn muộn. Input bấm sớm giữ trong input buffer của code (3.7).
   - Cancel window: transition ra khỏi đòn có thêm điều kiện là tham số bool `CanCancel`, do code bật và tắt theo bảng frame
     data (7.4).
6. **Any State** cho hành động ngắt được mọi lúc: Death, Knockdown, Knockback, Hit, và hành động của người chơi nếu không
   vào bằng CrossFade. Unity xét transition của Any State trước transition của state hiện tại, theo thứ tự trong danh sách:
   xếp Death trên cùng. Điều kiện luôn có trigger, và lọc `Stance` để vào đúng bộ. Can Transition To Self bật khi cần chơi
   lại chính state đó (trúng đòn liên tiếp), vì trigger chỉ bắn một lần. Transition không có trigger (chỉ bool, int) thì
   tắt, không thì state bị gọi lại mỗi frame.
7. Tham số đặt tên theo 4.4. Code cache `Animator.StringToHash`, không truyền chuỗi mỗi frame. **[DEV]**
8. Write Defaults thống nhất trong một controller: bật hết hoặc tắt hết (state chờ: mục 3).
9. Tốc độ phát từng nhân vật: `Animator.speed`. Tham số nhân tốc độ (Multiplier) chỉ cho state phải khác, ví dụ Loco theo
   tốc độ gameplay (7.2). Pack gắn tham số `AnimationSpeed` vào 56 / 59 state; ba state sót không chậm theo.
10. **Controller sạch** (gate A-15): không sub-asset mồ côi, không điều kiện trỏ tới tham số không có, không transition Mute /
    Solo sót. Unity không báo mấy lỗi này: controller của pack nặng 4,2 MB, chứa 3715 state machine mà chỉ 18 cái đang dùng,
    và 6 điều kiện trỏ tới `Injured`, `Crouch` không có trong danh sách tham số.
11. Không dùng Animator cho một giá trị đơn lẻ hay cho UI: dùng tween (UI Motion).

### 8.4 Culling và bật tắt **[DEV]**

- Culling Mode: `Cull Update Transforms` là mặc định (ra khỏi màn hình vẫn chạy state machine, chỉ bỏ cập nhật xương);
  `Cull Completely` cho đám đông, trang trí; `Always Animate` phải có lý do.
- SkinnedMeshRenderer: tắt Update When Offscreen; tắt Skinned Motion Vectors trừ khi cần TAA / motion blur.
- Bật tắt object có Animator làm Unity bind lại (tốn). Nhân vật dùng pool thì cân nhắc `keepAnimatorStateOnDisable`.

### 8.5 Thiết lập project **[TA]**

| Mục | Chuẩn |
|---|---|
| Player > GPU Skinning | GPU (Batched), mặc định của Unity 6; cũng là điều kiện để Sprite Skin deform bằng GPU (6.7) |
| Quality > Skin Weights | 4 Bones ở mức trung, cao; 2 Bones ở mức thấp; không dùng Unlimited |
| Package 2D Animation | 13.0.6 trở lên |

### 8.6 IK lúc chạy, Animation Rigging **[DEV]**

- Humanoid có sẵn foot IK và look-at (IK Pass của layer, `OnAnimatorIK`): ưu tiên dùng.
- Animation Rigging 1.4.1 (chạy trên Unity 6.3): mỗi constraint có giá riêng, không chạy chung với Optimize Game Objects.
  Ngân sách *(đề xuất)*: chỉ nhân vật chính, chỉ foot IK và look-at.
- Tay trái trên vũ khí hai tay: ưu tiên key đúng trong clip của bộ đó (5.5). IK chỉ khi nhiều vũ khí khác chỗ cầm dùng
  chung một bộ clip. Humanoid: IK Pass của layer + `OnAnimatorIK` kéo `LeftHand` về `Grip_L` (cách pack ExplosiveLLC làm);
  Generic: Two Bone IK của Animation Rigging, tính vào ngân sách trên.
- Weight IK lấy từ curve trên clip: thêm curve `HandIK_L` (0–1) ở tab Animation lúc import; Animator có tham số float cùng
  tên thì tham số nhận giá trị của curve (manual Unity). Animator đặt đoạn tay rời vũ khí (trúng đòn, lăn, ngã) ngay trong
  từng clip, code chỉ đọc tham số. Không tắt IK theo số giây gõ tay như pack (0,6 s khi trúng đòn, 1,05 s khi lăn, 5,25 s
  khi knockdown).
- Foot IK của state (Humanoid): pack bật ở mọi state chạm đất, tắt ở nhảy, rơi, lăn, ngã. Rig Generic không có: chân trượt
  thì sửa trong clip, hoặc dùng Animation Rigging.

### 8.7 Timeline cho cinematic

- Timeline cho đoạn có kịch bản; Animator cho gameplay.
- Override Track + Avatar Mask để chồng thân trên. Dùng Signal thay cho event.
- Đặt nhân vật dưới một object rỗng để không bị kéo về gốc thế giới. Hết Timeline thì code trả quyền điều khiển cho gameplay.

### 8.8 Dùng pack animation mua sẵn **[TA] [ANIM]**

Pack trên Asset Store thường là một bộ Humanoid kèm controller và code mẫu, dựng cho retarget lúc chạy và root motion. Coi
pack như nguồn mocap (5.8): học cách làm, lấy clip làm nền, không đưa controller, code, prefab của pack vào game. Bản mổ đầy
đủ một pack: `GameAnimation_PackExplosiveLLC.md`.

| Kiểm khi nhận pack | Ví dụ ở pack ExplosiveLLC | Làm gì |
|---|---|---|
| Kiểu rig | Humanoid, clip Copy From Other Avatar | retarget sang cây chuẩn, lưu Generic (5.8); chạy Humanoid thì ghi nợ |
| Kích thước nhân vật | cao 2,6 m, `humanScale` 1,47 | đo tốc độ, quãng dời trên nhân vật của game, không trên nhân vật mẫu |
| Root motion | chạy 6,2 m/s, lăn 6,7 m, knockback 2,2 m; `averageSpeed` báo sai | đo bằng tool, ghi vào tốc độ gốc và bảng frame data, rồi chạy tại chỗ (7.2) |
| Timing | đấm 314–373 ms tới frame va chạm, chém kiếm 461–506 ms | người chơi: cắt lấy đà cho vừa 3.2; quái: kiểm telegraph 3.3 |
| Pha chân | vòng Strafe lệch vòng Run 0,15–0,4 chu kỳ | Cycle Offset (7.1) |
| Event | `Hit`, `FootL`, `FootR`, `Land`, `WeaponSwitch`, không tiền tố | đổi sang bảng 7.4 |
| Tên | `RPG-Character@Unarmed-Attack-L1` | đổi theo 4.2 |
| Import | nén lẫn Off và Keyframe Reduction; Scale Factor 0,01 | theo 8.1 |
| Controller | 4,2 MB, 95% object mồ côi, điều kiện trỏ tham số không có | không dùng; dựng theo 8.3 |
| Code kèm | Input Manager cũ, `OnAnimatorMove`, khoá theo số giây, `Debug.Log` mỗi lần bắn trigger | chỉ đọc để học |
| Editor script, preset | cửa sổ nhắc bật mỗi lần import asset; preset ghi đè Input Manager, Tags & Layers | xoá script; không load preset vào project chung |
| Điều khoản | Asset Store | đọc trước khi đưa vào repo, vào game |

Các bước chuyển clip sang chuẩn, bảng đổi tên clip và event: `GameAnimation_PackExplosiveLLC.md` mục 7.

---

## 9. Quy trình và duyệt

### 9.1 Các mốc

| Mốc | Làm gì | Ai duyệt | Qua khi |
|---|---|---|---|
| 1 Brief | GD và animator điền brief (9.2) | lead | đủ mục, frame data có số |
| 2 Tham chiếu, kế hoạch | video tự quay, ref game khác; chọn pose kể chuyện từ video (không chép từng frame); thumbnail, cả với 3D; diễn thử, bấm giờ, ghi frame của mọi mốc (chạm, va chạm, event) | lead | chốt hướng; kế hoạch được duyệt trước khi dựng pose |
| 3 Blocking | key + extreme (chạm, lấy đà) + breakdown; 3D key stepped, 2D frame chính; ≥ 3–4 pose mỗi giây; clip diễn có mặt thô | lead + GD, **trong Unity** | đọc được ở stepped: pose đọc được ở camera, frame data khớp, điều khiển thử thấy ổn; breakdown đã duyệt |
| 4 Spline | lượt rút gọn: chuyển spline từng đoạn, trên từng kênh bỏ key thừa giữa hai extreme, chỉ thêm breakdown chỗ cung cần; làm spacing (7.6) | lead | không trôi, không pop, arcs mượt, không key thừa |
| 5 Polish | lượt từng phần: thân → tay → chân tiếp đất → mặt → tóc, vải, đuôi sau cùng (chỉ làm kỹ tóc, vải khi thân đã chốt); 2D: clean-up, smear | lead | checklist 9.3 phần Polish |
| 6 Tích hợp | import, event, Animator, transition, sfx / vfx | TA, dev | import không cảnh báo, nối được với mọi clip trong brief |
| 7 Duyệt trên máy | máy thấp nhất, camera thật, có sfx / vfx | lead + GD | checklist 9.3 phần Trên máy |
| 8 Xong | mục 11 | lead | |

**Blocking phải vào game.** Timing sai phát hiện ở blocking tốn một giờ sửa; phát hiện sau polish tốn cả ngày.

- Blocking là key, extreme và breakdown, không chỉ key: khoảng 3–4 pose mỗi giây là đủ để đọc timing (Williams); ở 30 fps, hai
  key blocking cách nhau không quá 8–10 frame. Làm lần lượt key → extreme → breakdown, chạy thử sau mỗi bước. Pose và breakdown
  chưa duyệt thì chưa nội suy.
- Clip lặp (idle, loco): chạy thử vòng thô trong Unity trước khi làm chi tiết.
- Mọi lượt đều xem ở tốc độ thật, cho lặp nhiều lần, và xem liền với clip đứng trước, sau nó. Williams: animator lớn tuổi hay
  làm chậm dần, animator trẻ hay làm nhanh quá; chỉ xem ở tốc độ thật mới bắt được.
- Đổi hướng, đổi kế hoạch thì đổi ở mốc 2 hoặc ở blocking, không để tới polish.

### 9.2 Brief (mẫu)

| Mục | Ví dụ |
|---|---|
| Clip | `Hero_Atk_Light_01` |
| Mục đích gameplay | đòn 1 của combo nhẹ |
| Ý chính (clip diễn) | nhân vật muốn gì, vì sao; một ý mỗi clip |
| Tính cách, trọng lượng | nặng (giáp, cao 2 m): trọng tâm thấp, đổi hướng chậm; tự tin (7.8) |
| Frame data (30 fps) | startup 4 · active 2 · recovery 10 · cancel từ frame 11 |
| Dời (code) | 0,3 m trong frame 3–6 |
| Loop | không |
| Nối từ / tới | Idle, Loco_Run / Atk_Light_02, Def_Roll_F, Idle |
| Pose trung tâm | idle của bộ tay không (7.8) |
| Event | `AE_Sfx` swing ở f3, `AE_Vfx` slash ở f5 |
| Âm thanh | câu thoại (nếu có), nhịp nhạc (bpm), tiếng va chạm |
| Camera, máy | top-down 45°, cách 12 m; điện thoại 6 inch |
| Tham chiếu | link video |
| Ưu tiên, hạn | |

### 9.3 Checklist duyệt

Ngoặc là nguyên lý / nền tảng tương ứng (mục 2).

**Blocking**
- [ ] Có kế hoạch timing trước khi dựng pose; key đủ dày (≥ 3–4 pose mỗi giây); breakdown nằm trên cung (timing, arcs)
- [ ] Silhouette pose chính đọc được ở camera gameplay, trên màn hình nhỏ nhất (staging, exaggeration, readability)
- [ ] Một đường hành động rõ; tay, chân, ngón không phá đường; tay, đạo cụ không che mặt (staging)
- [ ] Frame data đúng brief; người chơi vào thẳng pose lấy đà, trong ngân sách 3.2; game có combat thì quái telegraph đủ
      3.3 và các đòn khác nhau ngay từ đầu (timing, anticipation, feel)
- [ ] Lật qua lại giữa các key: key nào cũng đổi hình dáng, không bộ phận nào đứng im hay động quá; không hành động thừa
      (squash & stretch, staging)
- [ ] Mọi key: trọng tâm trên chỗ đỡ, hoặc đang ngã có chủ đích; chân chỉ nhấc sau khi trọng lượng đã sang chân trụ;
      contrapposto, không twinning (weight & balance, solid posing)
- [ ] Che nửa trên màn hình: hông vẫn nhún, trọng lượng vẫn dời (weight & balance)
- [ ] Thấy tính cách; clip diễn: cảm xúc đọc được (đồng nghiệp gọi đúng tên ở kích thước gameplay), mặt đã có thô (appeal)

**Polish**
- [ ] Arcs mượt khi xem trail, đường về khác đường đi; có ease ở điểm dừng; va chạm vào nhanh dần, dừng gắt, không pop
      (arcs, slow in / out)
- [ ] Spacing đúng ý: dày ở pose muốn người xem thấy, thưa ở giữa; không đều như máy (trừ vật máy móc); chơi ngược clip
      vẫn gọn (slow in / out, timing)
- [ ] Các bộ phận dừng lệch nhau, khớp gãy lần lượt; tóc, vải trễ và lắng sau thân; cancel ở frame sớm nhất vẫn ổn (follow
      through, overlap)
- [ ] Va chạm là accent: cứng thì bật lại, mềm thì trôi rồi lắng; pose giữ lâu là moving hold, không đứng chết (7.6)
- [ ] Chân chạm đất không trượt trong clip; xoay trên mũi bàn chân; tiếp đất đi theo đường tới chỗ đặt (weight & balance,
      7.8)
- [ ] Phần phụ không che hành động chính; mỗi nhịp một cử chỉ phụ (secondary)
- [ ] Nén giãn giữ thể tích, trong biên độ của nhân vật, đúng mức phong cách 2.1, không "loé" ở frame va chạm (squash &
      stretch)
- [ ] Cùng mức cường điệu với các clip cùng loại, xếp cạnh nhau ở camera gameplay (exaggeration)
- [ ] Mặt: mày, mắt, miệng lệch nhau; chớp mắt có ease; lip sync gọn, miệng không trễ hơn tiếng (7.9)
- [ ] Graph editor: không gai, không lật, không key thừa, soi cả khi xem thấy ổn (A-21)
- [ ] Không xuyên, skin không vỡ

**Trên máy**
- [ ] Loop không giật; vào / ra mọi clip trong brief không pop; pose đầu, cuối khớp pose trung tâm (fluidity, 7.8)
- [ ] Blend không ăn vào startup: đếm frame từ input tới frame active
- [ ] Chân không trượt ở tốc độ gameplay; sprite frame-by-frame được code dời không giật (3.1)
- [ ] Event đúng frame; sfx, vfx, hitstop khớp va chạm; tiếng không phát trước hình, không trễ quá khoảng 170 ms (7.4)
- [ ] Hành động thân trên khi đang chạy (đánh, rút vũ khí) xong thì thân trên về lại locomotion, không kẹt pose (8.3)
- [ ] Bị ngắt giữa lúc rút / cất vũ khí: vũ khí vẫn đúng chỗ, tay trái vẫn bám (7.4, 8.6)
- [ ] Xem liền với clip đứng trước, sau (vào từ idle, về idle), với màu, ánh sáng và nền thật: nhân vật tách khỏi nền
- [ ] Clip diễn, NPC nói: tắt tiếng vẫn hiểu ý (7.9)
- [ ] Điều khiển thấy nhạy (feel); đúng trạng thái và môi trường (context)
- [ ] Cho lặp lâu (idle, locomotion): không chi tiết nào gây khó chịu (appeal, context)
- [ ] Trong ngân sách (Profiler)

### 9.4 Góp ý khi duyệt

Theo chương *Critiquing a Scene* của Annand và cách duyệt Williams học từ các thầy.

1. **Góp ý khách quan**: chỉ ra chỗ làm clip khó đọc, khó tin, sai gameplay, gắn với một mục của 9.3 hay một luật có số mục.
   Không góp ý theo sở thích ("tao thích quay chân vào hơn"): clip là của animator, trừ khi nhân vật thuộc người khác (lead, art
   director quyết kiểu của nhân vật đó).
2. **Mẹo soi**:
   - lật qua lại giữa các key như lật giấy: có đổi hình dáng, có bộ phận nào dính;
   - che nửa trên màn hình: hông có nhún, trọng lượng có dời;
   - xem silhouette đen ở kích thước thật; camera tự do thì xoay quanh;
   - tắt tiếng (clip diễn), rồi bật tiếng;
   - xem liền với clip trước và sau, cho lặp lâu;
   - đi từng frame quanh frame va chạm và chỗ nối loop;
   - soi graph editor tìm gai, kể cả khi xem thấy ổn.
3. **Duyệt kế hoạch trước khi làm**: lead xem brief, tham chiếu, thumbnail ở mốc 2, trước khi dựng pose.
4. **Biết dừng**: clip đọc được, tin được, đúng gameplay và qua 9.3 là xong. Polish thêm chỉ khi brief còn hạn cho việc đó, không
   làm lại mãi: Annand kể Williams sửa đi sửa lại bộ phim riêng tới mức không bao giờ xong.

---

## 10. Kiểm tra (gate, đề xuất)

Như gate M-1 … M-12 của UI Motion: luật đo được thì cho tool kiểm, phần còn lại duyệt tay (mục 9). Tool chưa có; cột
"Tool" ghi rule nào tự động hoá được khi làm module.

| # | Rule | Qua khi | Tool |
|---|---|---|---|
| A-1 | Tên đúng mẫu | tên file, clip, xương, socket, tham số theo mục 4 | được |
| A-2 | Model 3D không phải sửa | Scale Factor 1, Convert Units ra đúng dòng, gốc xoay 0 scale 1, không xương `_end` | được |
| A-3 | Trong ngân sách | số xương, influence, tam giác, SkinnedMeshRenderer theo 5.6 / 6.7 | được |
| A-4 | Cây xương đúng archetype | cùng tên, cùng cây với rig archetype; cặp `_L` / `_R` đủ đôi | được |
| A-5 | Cycle khớp | clip nhóm Idle, Loco, hậu tố `_Loop`: Loop Time bật, pose đầu = cuối (sprite: Length = số frame ÷ sample rate) | được |
| A-6 | Import đúng chuẩn | fps theo 3.1, Optimal, sai số theo 8.1, Remove Constant Scale Curves, Avatar Copy From dùng chung; cùng hành động ở mọi bộ cùng thiết lập Root Transform | được |
| A-7 | Không scale sai | Humanoid không có curve scale; Generic không scale không đều trên xương có con | được |
| A-8 | Event hợp lệ | mọi event nằm trong danh sách 7.4 | được |
| A-9 | Frame data khớp | clip nhóm Atk, Skill, Def, và clip Interact có mốc gameplay, có dòng trong bảng frame data; độ dài clip = startup + active + recovery (lệch ≤ 1 frame) | được |
| A-10 | Animator theo archetype | nhân vật dùng Override Controller của controller archetype | được |
| A-11 | 2D đúng chuẩn | `.psd` của nhân vật dùng PSD Importer, PPU của project, Sorting Group trên gốc, không key thẳng sprite trên nhân vật đã rig, mip map tắt | được |
| A-12 | Pixel art | Point, None, không mip map, pivot theo pixel | được |
| A-13 | `Root` đứng yên | `Root` không có curve dời, xoay; rig Generic không đặt Root Motion Node | được |
| A-14 | Đã duyệt | checklist 9.3 ký đủ 3 phần | tay |
| A-15 | Controller sạch | không sub-asset mồ côi, không điều kiện trỏ tới tham số không có, không transition Mute / Solo (8.3) | được |
| A-16 | Không root motion ngầm | Apply Root Motion tắt; không component nào trên object có Animator viết `OnAnimatorMove` (7.2) | được |
| A-17 | State chờ an toàn | state chờ của layer phụ: Motion None thì Write Defaults bật; Write Defaults tắt thì clip rỗng (8.3) | được |
| A-18 | Vào hành động không trễ | không có state trống nằm giữa Any State (hoặc state nguồn) và state của hành động (8.3) | được |
| A-19 | Pha chân khớp | clip trong một blend tree locomotion: mốc chân (event `AE_Footstep`) cùng pha, lệch ≤ 0,05 thời gian chuẩn hoá (7.1) | được |
| A-20 | Chân trụ trượt đều | clip Loco tại chỗ: lúc bàn chân chạm đất (dưới ngưỡng cao), vận tốc ngang của nó không đổi (lệch ≤ 10% *(đề xuất)*) và bằng tốc độ gốc đã ghi (7.2, 7.8) | được |
| A-21 | Curve không gai | không kênh xoay nào có gai một frame (một frame lệch hẳn khỏi hai frame kề bên trong khi hai frame đó gần nhau) hay lật Euler (nhảy gần 180° hoặc 360° giữa hai frame) *(ngưỡng chỉnh ở pilot)* (7.6) | được |
| A-22 | Khớp pose trung tâm | clip vào từ Idle hoặc về Idle (Atk, Skill, Def, Hit, Interact, Emote, Equip): frame cuối so với pose trung tâm (frame 0 của Idle) của bộ mà clip trả về, frame đầu cũng vậy trừ nhóm Hit (frame đầu được lệch, 7.5). Cùng thước với gate của rigkit (5.10 mục 4): lỗi khi lệch trung bình > 20° hoặc một xương > 90°; pack ExplosiveLLC lệch 0–9°. Clip nối thẳng nhau (Knockdown → Getup): pose cuối = pose đầu clip sau (7.8) | được |

Nợ: vi phạm được chấp nhận thì ghi lý do, người, ngày (như sổ nợ của UI Motion).

---

## 11. Định nghĩa "xong"

**Rig 3D**
- [ ] Đơn vị, hướng, gốc, bind pose theo 5.1; vào Unity không phải xoay, không phải scale
- [ ] Cây và tên xương theo 5.2; cùng archetype thì đúng cây archetype; không xương thừa
- [ ] Skin ≤ 4 influence, chuẩn hoá, đối xứng; clip ROM không vỡ ở vai, hông, cổ tay, gối
- [ ] Đủ socket nhân vật cần; trong ngân sách 5.6
- [ ] Humanoid (nếu có): Avatar map đủ, T-pose, đã thử retarget một clip
- [ ] File nguồn có control rig lưu đúng chỗ

**Rig 2D**
- [ ] PSD theo 6.1; import bằng PSD Importer theo 6.2
- [ ] Xương đúng tên, đúng cây; mesh và weight thử ở pose cực không rách, không hở
- [ ] Sprite Library đủ Category / Label cho phần cần thay
- [ ] Có Sorting Group; thứ tự vẽ đúng ở mọi pose
- [ ] Trong ngân sách 6.7

**Clip**
- [ ] Tên, fps, độ dài đúng; frame data đúng brief
- [ ] Cycle khớp; nối được với mọi clip trong brief
- [ ] `Root` đứng yên; ghi đủ tốc độ gốc, quãng dời; chân không trượt ở tốc độ gameplay; chân trụ trượt đều (A-20)
- [ ] Pose đầu, cuối khớp pose trung tâm (A-22); curve không gai (A-21)
- [ ] Event đúng frame, đúng danh sách
- [ ] Qua checklist 9.3 đủ 3 phần

**Một nhân vật**
- [ ] Đủ moveset theo brief; Override Controller nối đủ; blend tree đúng tốc độ gốc
- [ ] Controller qua A-15 … A-19; clip lấy từ pack mua đã qua 8.8
- [ ] Sfx, vfx, hitstop, rung theo 7.5
- [ ] Profiler trên máy thấp nhất: trong ngân sách

---

## 12. Quyết định, câu hỏi mở, pilot

### 12.1 Đã chốt

| Ngày | Quyết định | Ảnh hưởng |
|---|---|---|
| 2026-09-26 | Phần mềm 3D: Blender | 5.7 theo Blender; phần Maya chỉ để tham khảo |
| 2026-09-26 | 2D: Unity 2D Animation, PSD Importer, Aseprite Importer | mục 6; không dùng Spine (6.9) |
| 2026-09-26 | Mọi clip tại chỗ, code dời và xoay nhân vật | 7.2; cột Dời (code) trong bảng frame data (3.7); gate A-13 |
| 2026-09-26 | Chủ yếu mobile; diễn theo mood của từng loại game | 2.1 (mood dùng chung trục với Profile UI Motion); ngân sách 5.6, 6.7 |
| 2026-09-26 | Dùng được mocap, Mixamo; retarget lúc làm, lúc chạy vẫn Generic | 5.4, 5.8 |
| 2026-09-26 | Bind pose 3D: T-pose | 5.1; 5.3 (kiểm vai khi tay buông); 8.1 |
| 2026-09-26 | Combat tuỳ loại game: Action, RPG có; Cozy, Puzzle không, chỉ có animation hoạt động thường | 2.1 (cột Combat); mục 3; 7.3; 7.5 |
| 2026-09-28 | Nguồn 2D là file `.psd`, không dùng `.psb` | 4.2, 4.5, 6.1; 6.2 (chọn PSD Importer cho từng file); A-11 |

### 12.2 Còn mở

1. Máy thấp nhất: chưa có. Ngân sách 5.6, 6.7 tạm giữ mức đề xuất. Khi có game đầu tiên, chọn một máy Android tầm thấp
   trong team làm máy chuẩn để đo ở pilot.
2. Chỗ để file nguồn (file nguồn là gì: 4.5). Đề xuất: thư mục `ArtSource/` trong repo của game, ngang hàng Assets, đi Git
   LFS: file nguồn và FBX xuất ra nằm cùng lịch sử, cùng commit. Không muốn dùng LFS thì dùng ổ chung có cùng cấu trúc thư
   mục, nhưng mất lịch sử phiên bản. Chọn cách nào thì `.gitignore` cũng phải bỏ qua `*.blend1` (file backup của
   Blender), như repo này đã làm.
3. Đường vào hành động (8.3): transition (Any State, trigger nhóm, `Variant`) hay code gọi `CrossFadeInFixedTime`. Đề xuất:
   transition là mặc định; CrossFade cho nhân vật người chơi khi combo và cancel theo frame data phức tạp. Chốt ở pilot.
4. Pack ExplosiveLLC dùng tới đâu. Đề xuất: làm clip tạm (placeholder) cho blocking và làm mẫu học cách dựng controller;
   không ship thẳng: nhân vật cao 2,6 m dáng khối, rig Humanoid từ 3ds Max, đòn đánh chậm hơn ngân sách 3.2 (8.8).
5. 2D frame-by-frame di chuyển bằng code: 12 hình mỗi giây (on twos) có giật, trượt chân dưới camera bám không (3.1). Đề xuất:
   pilot 2D làm một clip Loco ở cả 12 và 24 hình mỗi giây, xem trên máy, chọn theo mắt.
6. Bước đi ở 30 fps: 15 frame (đúng 2 bước mỗi giây, key down và up phải làm tròn) hay 16 frame (key rơi đúng frame, chậm hơn
   7%) (7.7). Đề xuất: 15 frame; chốt ở pilot, cả project một kiểu.
7. Ngưỡng của gate A-20, A-21 *(đề xuất)*: chỉnh theo lượt đo ở pilot. A-22 dùng ngưỡng rigkit đã hiệu chỉnh trên pack
   ExplosiveLLC và một bộ clip chibi team chê (5.10).

### 12.3 Pilot và tool

**Pilot** (như UI Motion đã pilot trên một game đang chạy): một nhân vật 2D và một nhân vật 3D (hoặc một xe) của game
pilot đi hết mục 9,
ghi lại chỗ vướng. Đo số thật trên máy thấp nhất: số xương, thời gian Animator, Sprite Skin chạy CPU hay GPU. Thử IK 2D chỉ
dùng lúc làm (6.6), cách chia event (7.4), retarget Mixamo (5.8), đường vào hành động (12.2). Sửa quy chuẩn thành 0.2.

**Tool** (cùng hướng với UI Motion: tool làm, không phải người nhớ):

| Tool | Làm gì |
|---|---|
| Script xuất Blender | một nút: kiểm tên xương, đơn vị, `Root` đứng yên; xuất mỗi action một FBX đúng 5.7, tên đúng 4.2. Rig do rigkit dựng thì `export --mode split` và `verify_fbx` đã làm; còn thiếu nút cho file `.blend` làm tay |
| Preset import Unity | AssetPostprocessor tự áp 8.1 và 6.2 theo thư mục, theo tên; `.psd` trong thư mục nhân vật tự chuyển sang PSD Importer (`AssetDatabase.SetImporterOverride`) |
| Gate | A-1 … A-13, A-15 … A-22: cửa sổ Kiểm tra, sổ nợ, chạy lúc build như UI Motion |
| Đo clip | phát clip trên nhân vật đích, ghi độ dài, fps, tốc độ gốc, quãng dời, mốc chân, frame event; điền bảng frame data (7.2, 8.8). Lượt mổ pack ExplosiveLLC đã đo bằng script batch, chưa thành tool |
| Sinh Animator | controller gốc cho archetype theo bố cục 8.3 (bộ, nhóm, layer UpperBody), override cho từng nhân vật dựng từ tên clip |
| Đổi tên Mixamo | **đã có** (0.1.0): đổi tên xương `mixamorig:*` và clip `mixamo.com` lúc import (5.9) |
| rigkit | **đã có** (0.1.3): rig, skin, ROM, pose, clip, xuất FBX bằng Blender headless, gate từng bước; skill `blender-rig-animate` (5.10) |
| Retarget mocap, Mixamo | bake clip Humanoid thành clip Generic của cây chuẩn (5.8) |
| Clip thủ tục | sinh clip cho prop, xe, idle thở, quái đơn giản, clip tạm (5.8) |
| Runtime | `AnimEventReceiver` (7.4, cả `AE_Prop` và đồng bộ lại khi rời state), cửa sổ hitbox và cancel theo frame data, hitstop bằng `Animator.speed` và rung đọc trục Impact của mood |
| Scene duyệt | xem clip ở góc và khoảng cách camera gameplay, bật tắt layer phụ, xếp clip cùng loại cạnh nhau |
