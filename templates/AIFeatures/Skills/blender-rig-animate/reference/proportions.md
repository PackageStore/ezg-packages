# Đo tỉ lệ trước khi pose

Pose và nhịp lấy từ nhân vật người (hạ tay 60°, bước 30 cm, ngồi thấp) áp lên chibi hay quái ngắn chân là nguồn chính
của "pose kỳ quặc": tay chìm vào thân, chân lún, cổ chân gãy. Đo 6 số dưới đây ngay sau bước rig, ghi vào đầu file
pose, rồi mới chọn góc.

| Số | Cách đo | Ví dụ: chibi 1,46 m + mũ, cầm rìu và khiên |
|---|---|---|
| L chân | UpperLeg + LowerLeg (hông → cổ chân) | 0,21 m |
| h hông | độ cao khớp hông trên cổ chân ở pose nền | 0,195 m (hông hạ 1,5 cm) |
| L tay | UpperArm + LowerArm (vai → cổ tay) | 0,255 m |
| Góc tay chạm thân | hạ dần `UpperArm side` tới khi gate báo LIMB_PENETRATION hoặc FLIP | ≈ -35° |
| Vật chắn | vành mũ, vai giáp, bụng: toạ độ biên (bbox của phụ kiện, `measure`) | vành mũ x ±0,44 m ở z 0,95 m |
| Đạo cụ | chiều dài vũ khí so với tay, đường kính khiên | rìu 1,05 m (4× tay), khiên 1,0 m |

## Công thức

**Tầm với của chân trụ** (bàn chân cách điểm dưới hông một khoảng x khi còn chạm đất):

    x_max = sqrt((0,98 L)^2 - h^2)

Mọi thứ dời khớp hông theo chiều ngang cộng vào x: nửa bước (`stride / 2`), độ xoắn hông
(`0,214 × sin(hips_twist)` với 0,214 m là nửa khoảng cách hai hông của chibi ví dụ), lao hông tới. Với chibi đó:
`x_max = sqrt(0,206² - 0,195²) ≈ 0,066 m`, nên `stride / 2 + dịch do xoắn ≤ 6 cm` → stride 10 cm, xoắn hông 3°.
Vượt là IK_REACH, gối duỗi quá (LIMIT LowerLeg < -3), chân lơ lửng.

**Độ hạ hông tối đa** khi bàn chân phẳng: cổ chân gập ≈ góc nghiêng của ống chân. Với chân hai đốt L1, L2 và khoảng
hông–cổ chân d:

    cos(g) = (L1² + L2² - d²) / (2 L1 L2)      (g: góc trong ở gối)
    gập cổ chân ≈ asin(L1 sin(g) / d)

Chibi ví dụ (L1 0,125, L2 0,085): d = 0,190 cho 31°, d = 0,195 cho 27°. Giới hạn cổ chân 30–40° nên hông chỉ hạ được
2–2,5 cm, và chân đang nhấc cần `h - lift ≥ 0,193 m` → nhấc 1 cm.

**Bàn chân dài so với chân** (chibi: bàn chân 0,2 m, chân 0,21 m): chân nhấc thấp thì không buông mũi xuống được (mũi
cắm đất), mà giữ phẳng thì cổ chân gập. Chạy kiểu lê bước: `lift` 1 cm, `follow_max` 0,3, nảy kiểu pogo (thân lên
cao nhất trên chân trụ, `bob_phase = giữa pha chạm + 1/4`) thay vì kiểu người (thấp nhất trên chân trụ). Tốc độ bù
bằng nghiêng người và thân trên. Ở chibi ví dụ chân đã chạm giới hạn: `bob` 1,2 cm, `sway` 2,5°, `hips_twist` 4° đều làm
gối duỗi quá (LIMIT LowerLeg -6) hoặc cổ chân gập 42–44°. Lực của bước chạy đặt vào thân trên: tay đánh 15°, ngực
xoắn ngược 7°, lưng rồi ngực rồi đầu nén giãn 3–4° hai lần mỗi chu kỳ, lệch nhau một frame
(`wave(..., period = nửa chu kỳ)`).

**Tay ngắn, thân tròn**: tay không buông thõng được. Pose nền để tay dang 30–35°; giơ tay lên phải ra ngang
(azimuth 90–110) chứ không thẳng đứng (đụng vành mũ); vũ khí dài vung trong mặt phẳng ngoài vật chắn.

**Vũ khí một tay**: cán rời nắm tay ở 60–110° so với cẳng tay (phía ngón cái). Ở frame va chạm cẳng tay chúc xuống
trước, cổ tay bẻ về phía ngón út, lưỡi gần nằm ngang lao tới trước. Khai `aim` trái điều này thì cổ tay phải gập 80–90°
(LIMIT Hand).

**Nhân vật người thường** (tỉ lệ 7–8 đầu): các giới hạn `humanoid` và tầm với rộng hơn nhiều; vẫn đo 6 số, vì vũ khí,
áo giáp, váy cũng chặn góc.
