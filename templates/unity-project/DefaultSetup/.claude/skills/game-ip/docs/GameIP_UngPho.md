# Sổ tay ứng phó: khi bị khiếu nại, khi bị nhái (Game IP)

Phiên bản 0.0.1 (nháp, chờ công ty duyệt) · 2026-10-23 · Đi cùng `GameIP_QuyChuan.md` (QC) và `GameIP_NguyenLy.md` (NL).

**Không phải tư vấn pháp lý.** Sổ tay nói việc cần làm ngay và việc không được làm, để không mất hạn và không tự làm hại mình
trước khi người duyệt của công ty **[DUYỆT]** và luật sư vào cuộc. Mọi văn bản gửi ra ngoài (trả lời, counter-notice, khiếu
nại) đều qua [DUYỆT].

---

## 0. Năm việc không làm

1. **Không trả lời trực tiếp** thư đe doạ, email của bên khiếu nại hay luật sư của họ. Một câu xin lỗi, giải thích, "bọn em sẽ
   gỡ" có thể thành bằng chứng thừa nhận.
2. **Không xoá, không sửa** bất cứ thứ gì liên quan: file, lịch sử git, tài liệu thiết kế, store listing cũ, creative, tin nhắn.
   Khi đã có hoặc thấy trước tranh chấp, xoá sửa bằng chứng làm vụ việc nặng hơn nhiều. Sửa game để hết vi phạm thì làm trên bản
   mới, giữ nguyên bản cũ.
3. **Không gửi counter-notice** khi chưa có [DUYỆT]: counter-notice DMCA là cam đoan dưới hình phạt khai man và đồng ý cho toà
   Mỹ xét xử (mục 3.1).
4. **Không lên tiếng công khai** (mạng xã hội, cộng đồng người chơi, báo chí).
5. **Không tự liên hệ** bên kia qua kênh cá nhân, kể cả khi quen người bên đó.

---

## 1. Giờ đầu tiên: phân loại

| Nhận được | Ai gửi | Hạn kiểu | Đi tới |
|---|---|---|---|
| Google Play báo app bị gỡ / sắp gỡ do thông báo bản quyền (DMCA) | Google, kèm thông báo của bên khiếu nại | Gỡ ngay; counter-notice không có hạn cứng nhưng app bị gỡ tới khi xử xong | 3.1 |
| Google Play báo vi phạm chính sách (IP, Impersonation, Metadata) | Google | Theo email, thường vài ngày để sửa | 3.3 |
| Apple chuyển thư tranh chấp nội dung (App Store content dispute) | Apple, kèm thông tin bên khiếu nại | Apple yêu cầu hai bên làm việc; có thể gỡ app | 3.2 |
| Apple từ chối bản cập nhật / gỡ app theo guideline (4.1, 4.3, 5.2…) | Apple App Review | Theo email | 3.3 |
| Steam báo thông báo DMCA với game hay trang store | Valve | Gỡ trang / build tới khi xử xong | 3.1 (cùng khung DMCA) |
| Creative bị từ chối, gỡ vì IP; claim Content ID trên video quảng cáo | Mạng quảng cáo, YouTube | Ngay | 3.4 |
| Thư luật sư, cease and desist | Luật sư bên kia | Hạn họ tự đặt (thường 7–14 ngày) | 3.5 |
| Đơn kiện, giấy triệu tập của toà | Toà, qua tống đạt | Hạn trả lời ngắn, tính bằng tuần | 3.6 |
| Người trong công ty báo nghi vi phạm của chính mình | Nội bộ | Không có hạn ngoài, nhưng làm ngay | 3.7 |

Ngay sau khi phân loại: báo [DUYỆT], ghi giờ nhận, chuyển tiếp nguyên văn email (không cắt), làm mục 2.

---

## 2. Việc chung cho mọi trường hợp

1. **Giữ nguyên bằng chứng** (mục 0.2): khoá nhánh git liên quan (tag lại commit hiện tại), sao lưu sổ nguồn gốc, thẻ so sánh,
   creative, store listing (ảnh chụp có ngày).
2. **Gói hồ sơ** cho [DUYỆT] và luật sư:
   - Thông báo nguyên văn; tác phẩm họ nói bị xâm phạm; phần của mình họ chỉ ra (URL, màn hình, file).
   - Sổ nguồn gốc của phần bị nêu: ai làm, khi nào, từ nguồn nào, hợp đồng, license (QC 3).
   - Lịch sử tạo: commit, file gốc, ngày (QC 9.1).
   - Thẻ so sánh với tác phẩm của họ (QC 6), làm mới nếu chưa có, ghi rõ làm sau khi nhận khiếu nại.
   - Doanh thu, thị trường của game (để luật sư ước rủi ro).
3. **Đánh giá nhanh** (không kết luận): phần bị nêu thuộc loại nào (asset chép, nhạc, font, tên, icon, look & feel, cơ chế)?
   Theo NL 6.1 họ đánh vào chỗ dễ chứng minh: nếu sổ cho thấy asset không rõ nguồn hay license sai, nói ngay với [DUYỆT].
4. **Quyết định cùng [DUYỆT]**: sửa (gỡ, thay phần bị nêu), phản đối (counter-notice, trả lời qua luật sư), hay thương lượng.

---

## 3. Theo từng loại

### 3.1 Thông báo DMCA (Google Play, Steam, hosting)

Khung DMCA §512 (NL 5.5):

0. Không trả lời trực tiếp bên khiếu nại (mục 0.1). Với DMCA, mọi phản hồi của mình đi qua nền tảng (counter-notice), hoặc qua luật
   sư khi [DUYỆT] quyết.
1. Nền tảng gỡ nội dung khi nhận thông báo hợp lệ, rồi báo mình.
2. Mình có ba đường:
   - **Sửa**: thay phần bị nêu, gửi bản mới / cập nhật listing. Nhanh nhất khi đúng là có vấn đề (vd asset không rõ nguồn).
   - **Counter-notice**: chỉ khi chắc chắn bị gỡ nhầm hoặc nhận diện sai, và [DUYỆT] đồng ý. Counter-notice phải có: chữ ký;
     nội dung bị gỡ và vị trí trước khi gỡ; cam đoan, dưới hình phạt khai man, rằng tin bị gỡ do nhầm hoặc nhận diện sai; tên,
     địa chỉ, điện thoại; **đồng ý thẩm quyền của toà liên bang Mỹ** (người ở ngoài Mỹ: bất kỳ hạt nào nơi nền tảng có mặt) và
     chấp nhận nhận giấy tờ từ bên khiếu nại. Khai sai có thể phải bồi thường (§512(f)).
   - **Không làm gì**: nội dung vẫn bị gỡ.
3. Sau counter-notice, nền tảng khôi phục sau 10 đến 14 ngày làm việc, trừ khi bên kia báo đã khởi kiện. Đây là "doom loop" (NL
   6.3): bên kia im lặng chưa chắc là an toàn; nếu họ kiện xin lệnh cấm, app có thể bị gỡ lâu.

### 3.2 Apple: tranh chấp nội dung

- Apple không phân xử: chuyển thư của bên khiếu nại cho mình và yêu cầu hai bên tự giải quyết; Apple có quyền gỡ app theo hợp
  đồng developer, có hay không có lý do (NL 5.5).
- Không có counter-notice, không có đồng hồ 10–14 ngày. Kháng nghị sau khi bị gỡ hiếm khi thành công.
- Việc làm: mục 2, rồi trả lời bên khiếu nại qua [DUYỆT] (thường qua luật sư), cc Apple theo hướng dẫn trong email. Nếu phần bị
  nêu có vấn đề thật: sửa và gửi bản cập nhật sớm, trước khi Apple quyết gỡ.

### 3.3 Bị từ chối hoặc gỡ theo chính sách store

- Đọc đúng mục guideline / chính sách được nêu (Apple 4.1, 4.3, 5.2, 2.3.x; Google IP, Impersonation, Metadata).
- Sửa đúng phần đó (thường là tên, icon, keyword, screenshot, mô tả) rồi gửi lại; kháng nghị qua kênh của store nếu chắc chắn bị
  nhầm.
- Vi phạm lặp lại có thể dẫn tới khoá tài khoản developer (hậu quả lớn hơn mất một game): Google tính mỗi lần đình chỉ là một
  strike và có thể chấm dứt cả các tài khoản liên quan; Apple có thể đuổi khỏi Developer Program (NL 5.5). [DUYỆT] xem trước khi
  gửi lại.
- Ở EU, nền tảng phải đưa lý do cụ thể và cách khiếu nại khi hạn chế app (DSA Điều 17): dùng kênh đó nếu bị đối thủ báo sai.

### 3.4 Creative bị gỡ, bị claim

- Dừng creative đó trên **mọi** mạng đang chạy (cùng một file thường chạy nhiều nơi), và mọi biến thể dùng cùng nguồn bị nêu.
- Kiểm sổ creative (QC 4.17): nguồn bị nêu có license quảng cáo trên nền tảng đó không.
- Có license: gửi tranh chấp (dispute) kèm license qua kênh của nền tảng. Không có: thay nguồn, làm lại creative.
- Claim Content ID có thể chặn hoặc kiếm tiền trên video; có license vẫn có thể bị claim (NL 4.2).

### 3.5 Thư luật sư, cease and desist

- Không trả lời (mục 0.1). Ghi ngày nhận, chuyển [DUYỆT] ngay.
- Làm mục 2 trong hạn họ đặt, để luật sư trả lời có căn cứ.

### 3.6 Bị kiện

- Luật sư ở nơi bị kiện, ngay lập tức: hạn trả lời ngắn, bỏ lỡ có thể bị xử vắng mặt.
- Lệnh giữ bằng chứng áp cho toàn công ty, kể cả chat, email cá nhân dùng cho công việc.

### 3.7 Tự phát hiện vấn đề của mình

- Ghi vào nợ (QC 10.2) hoặc sửa ngay theo gate; không xoá dấu vết của phần cũ trong lịch sử.
- Nếu phần có vấn đề đã ở bản phát hành: [DUYỆT] quyết có cần cập nhật khẩn, dừng creative, liên hệ chủ quyền xin license hay
  không.

---

## 4. Khi mình bị nhái

### 4.1 Thu bằng chứng trước khi báo

Bị gỡ là mất bằng chứng, nên thu trước: URL store, tên developer, ảnh chụp listing, video gameplay của game nhái, creative của
họ (thư viện quảng cáo công khai), ngày thấy, số lượt tải / xếp hạng nếu có. Ghép với gói bằng chứng của mình (QC 9.1): ngày
mình tạo và phát hành phần bị chép.

### 4.2 Chọn đúng kênh theo loại vi phạm

| Họ chép gì | Loại quyền | Kênh |
|---|---|---|
| Art, nhân vật, UI, nhạc, video, code | Bản quyền | Google Play: form DMCA; Apple: form tranh chấp IP; Steam: thông báo DMCA (steamcommunity.com/dmca/create/); Meta: IP Reporting Center; TikTok: form báo quảng cáo (kèm Ad ID); mạng quảng cáo khác: kênh báo IP của mạng đó |
| Tên, logo, icon gần giống | Nhãn hiệu | Cần nhãn hiệu đã đăng ký ở nước đó (QC 7.3). Google: form nhãn hiệu (nên liên hệ developer trước); Apple: form tranh chấp, viện dẫn 4.1(a), 4.1(b), 4.3(b); app khác chiếm tên: form tranh chấp tên app; Google Ads: báo dùng nhãn hiệu trong chữ quảng cáo |
| Giả làm game / studio của mình, tên + icon dễ nhầm | Mạo danh | Google Play: nút "Flag as inappropriate" theo chính sách Impersonation (không cần nhãn hiệu đã đăng ký); Apple: form tranh chấp; Meta: Brand Rights Protection |
| Clone phát hành ở EU | – | Thông tin trader công khai trên trang EU của app (DSA Điều 30(7)); thiếu thông tin thì báo lỗi tuân thủ DSA, tách khỏi khiếu nại IP (NL 5.5) |
| Developer ẩn danh | – | Google: trát §512(h) ở Mỹ để buộc nền tảng tiết lộ danh tính; EU: thông tin trader mà store phải công khai theo DSA (NL 5.5) |
| Bên nhái ở Mỹ, thiệt hại nhỏ | Bản quyền | Copyright Claims Board của Mỹ: bồi thường tối đa 30.000 USD, người nước ngoài được đi kiện, bên kia có quyền từ chối trong 60 ngày (NL 2.6). Muốn bồi thường luật định ở toà thường thì phải đăng ký bản quyền Mỹ sớm (QC 9.2) |

### 4.3 Chỉ khiếu nại phần là IP của mình

- Khiếu nại cách thể hiện (art, chữ, âm thanh, tổ hợp chọn và sắp xếp đặc trưng), không khiếu nại cơ chế, thể loại (NL 2.2). Làm
  thẻ so sánh (QC 6) theo chiều ngược lại trước khi gửi.
- Khiếu nại sai có thể bị phản đòn (bồi thường do khai sai, §512(f)) và làm giảm uy tín với store.
- Nhãn hiệu chưa đăng ký ở nước đó thì kênh nhãn hiệu yếu; dùng kênh bản quyền nếu art, icon bị chép.

### 4.4 Sau khi gửi

- Theo dõi phản hồi: nếu bên kia gửi counter-notice (Google), nền tảng sẽ khôi phục sau 10–14 ngày làm việc trừ khi mình khởi
  kiện. [DUYỆT] quyết trong khoảng đó.
- Ghi lại mọi bước, ngày.

---

## 5. Khi mình đi khiếu nại: nội dung bắt buộc của thông báo DMCA (để [DUYỆT] soạn)

Dùng cho mục 4 (mình bị nhái). Không phải counter-notice: nội dung counter-notice ở mục 3.1. Theo §512(c)(3)(A):

1. Chữ ký (tay hoặc điện tử) của chủ quyền hoặc người được uỷ quyền.
2. Tác phẩm bị xâm phạm (hoặc danh sách, nếu nhiều).
3. Nội dung xâm phạm và thông tin đủ để nền tảng tìm ra (URL, tên app, ảnh).
4. Thông tin liên hệ.
5. Câu cam đoan thiện chí rằng việc sử dụng không được chủ quyền, người đại diện hay luật cho phép.
6. Câu cam đoan thông tin chính xác và, dưới hình phạt khai man, người gửi được uỷ quyền.

Mỗi nền tảng có form riêng hỏi đủ các ý này; dùng form của nền tảng.

---

## 6. Sau sự việc

- Postmortem ngắn: nguyên nhân (gate nào lẽ ra bắt được), sửa sổ, sửa quy trình.
- Cập nhật QC hoặc danh sách của skill nếu thiếu luật.
