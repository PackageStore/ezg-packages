# Nguyên lý Game IP: vì sao có các luật trong quy chuẩn

Phiên bản 0.0.1 (nháp, chờ công ty duyệt) · 2026-10-23 · Lý do, luật theo thị trường, án lệ và nguồn cho `GameIP_QuyChuan.md`
(QC). Nền là hai bộ slide của IPLNN [1][2], kiểm chứng và mở rộng bằng nguồn công khai.

**Không phải tư vấn pháp lý.** Đây là kiến thức để thiết kế quy trình kiểm; áp vào một vụ cụ thể cần người duyệt của công ty.

## 0. Cách đọc

- Mỗi luật ghi lớp áp dụng: **L1** nơi công ty (Việt Nam), **L2** nền tảng (store, mạng quảng cáo), **L3** thị trường phát hành,
  **L4** nơi bị kiện (mục 1).
- `[n]` là nguồn ở mục 9. Ngày tra là 2026-10-23 trừ khi ghi khác.
- *(chưa đối chiếu gốc)*: chỉ có nguồn thứ cấp (báo, blog luật, trang tổng hợp) hoặc trang gốc không tải được; phải đọc văn bản
  gốc trước khi dùng làm căn cứ cứng.
- Luật, điều khoản dịch vụ, guideline thay đổi thường xuyên; rà lại mỗi sáu tháng (QC 10.3).

---

## 1. Luật nào áp

### 1.1 Bốn lớp

Công ty làm game ở Việt Nam, phát hành toàn cầu. Một game chịu bốn lớp cùng lúc:

| Lớp | Là gì | Áp cho | Tốc độ |
|---|---|---|---|
| L1 Nơi công ty | Việt Nam | Ai sở hữu thứ nhân viên, freelancer, outsource làm ra; hợp đồng; bí mật kinh doanh với nhân viên; xử phạt, hình sự trong nước | chậm |
| L2 Nền tảng | Apple, Google Play, Steam; mạng quảng cáo | Gỡ app, gỡ creative, khoá tài khoản theo chính sách riêng, áp cho mọi nước; khung DMCA của Mỹ, phần EU theo DSA | nhanh nhất (ngày) |
| L3 Thị trường | Mỹ, EU, Anh, Nhật, Hàn, Trung Quốc, Đông Nam Á… | Nhãn hiệu (đăng ký ở đâu có quyền ở đó), quảng cáo sai sự thật, quy định AI | trung bình |
| L4 Nơi bị kiện | Toà bên kia chọn, thường là Mỹ | Bồi thường, lệnh cấm; gửi counter-notice DMCA là đồng ý cho toà Mỹ xét xử (5.5) | chậm nhất (năm) |

DMCA là luật Mỹ, không có hiệu lực ở Việt Nam [2]. Studio Việt Nam vẫn chịu nó qua L2 (Apple, Google áp chính sách gỡ toàn cầu,
bất kể developer ở đâu) và L4 (có doanh thu, hạ tầng ở Mỹ thì có thể bị kiện ở toà Mỹ, như Tencent, NetEase đã bị) [11][12].

### 1.2 Bản quyền tự có ở mọi nước, nhãn hiệu thì không

- Việt Nam là thành viên Công ước Berne từ 26/10/2004 và Hiệp ước WIPO về quyền tác giả (WCT) từ 17/2/2022 [16]. Theo nguyên tắc
  đối xử quốc gia, game làm ở Việt Nam tự được bảo hộ ở mọi nước Berne như tác phẩm của công dân nước đó, không cần thủ tục; quy
  định riêng về đăng ký để được bồi thường của từng nước vẫn áp dụng (2.6).
- Nhãn hiệu chỉ có ở nước đã đăng ký (hoặc đã dùng, riêng ở Mỹ) (3.1).

### 1.3 Bảng nhanh theo thị trường

| | Mỹ | EU | Anh | Nhật | Hàn | Trung Quốc | Việt Nam |
|---|---|---|---|---|---|---|---|
| Nhãn hiệu sinh ra từ | Sử dụng (đăng ký liên bang cho thêm quyền; nộp trước được với ý định sử dụng) | Nộp đơn trước | Nộp đơn trước | Nộp đơn trước | Nộp đơn trước | Nộp đơn trước | Nộp đơn trước |
| Bản quyền tác phẩm của công ty | 95 năm từ công bố hoặc 120 năm từ tạo, mốc nào hết trước | 70 năm từ công bố (pháp nhân, khuyết danh) | như EU; tác phẩm do máy tạo không có tác giả người: 50 năm | 70 năm từ công bố (tác phẩm đứng tên pháp nhân); cá nhân: đời tác giả + 70; tăng từ 50 từ 30/12/2018 | 70 năm từ công bố (tác phẩm của nhân viên) | 50 năm (tác phẩm của tổ chức) | Đời tác giả + 50; điện ảnh, mỹ thuật ứng dụng, khuyết danh 75 năm từ công bố |
| Đầu ra thuần AI | Không bảo hộ | Không (cần sáng tạo của người) | Có điều riêng cho tác phẩm do máy tạo, đang được đề xuất bỏ | Không; phần người đóng góp sáng tạo thì có | Không đăng ký được; phải khai phần AI | Có án sơ thẩm công nhận khi người điều khiển sáng tạo thật | Chưa có quy định |
| Đăng ký bản quyền cho lợi gì | Bồi thường luật định, phí luật sư (2.6) | Không có hệ thống chung | Không có hệ thống | Hệ thống tuỳ chọn | Hệ thống tuỳ chọn, chứng cứ | Tuỳ chọn, hay dùng làm chứng cứ | Tuỳ chọn, chứng cứ |

Nguồn: [15][16][17][18][19][20][21][22][32]. Chi tiết ở các mục dưới.

### 1.4 Ngoài phạm vi: sáng chế

Sáng chế (patent) bảo vệ giải pháp kỹ thuật, kể cả cơ chế game ở một số nước, và phải đăng ký. Ví dụ: vụ Nintendo và The
Pokémon Company kiện Pocketpair (Palworld) ở Tokyo từ 9/2024 là kiện sáng chế, không phải bản quyền hay DMCA; yêu cầu bồi thường
đã giảm còn 5 triệu yên (8/2026), dự kiến có phán quyết khoảng 11/2026 *(chưa đối chiếu gốc)*. Quy chuẩn này chưa kiểm sáng chế;
cơ chế giống một game của hãng lớn có danh mục sáng chế (Nhật, Mỹ) thì hỏi [DUYỆT].

---

## 2. Bản quyền

### 2.1 Bản build chứa nhiều tác phẩm của nhiều chủ

- Không có "bản quyền của cả game" như một khối: bản build là một bản sao, trong đó các tác phẩm (art, code, nhạc, sfx, chữ,
  video) được định hình; mỗi tác phẩm có chủ riêng [1].
- Làm ra bản build không có nghĩa là sở hữu mọi thứ trong đó (pack mua, font, nhạc license, SDK) [1]. → QC 0 (nguyên tắc 1), gate IP-1.
- Phần nhìn-nghe của game được bảo hộ như tác phẩm nghe nhìn, độc lập với code: viết lại code khác mà ra đúng màn hình vẫn là
  chép; người bị chép không cần chứng minh code bị chép (Stern Electronics v. Kaufman, 2d Cir. 1982) [1].

### 2.2 Ý tưởng và cách thể hiện

- Bản quyền bảo vệ cách thể hiện, không bảo vệ ý tưởng, quy trình, hệ thống, phương pháp vận hành (Mỹ: 17 U.S.C. §102(b); Việt
  Nam: Luật SHTT liệt kê tương tự các đối tượng không được bảo hộ quyền tác giả *(chưa đối chiếu gốc)*) [1][2].
- Không được bảo vệ: ý tưởng, cơ chế, luật thắng thua, thể loại, công thức, độ nhận diện trên thị trường [1].
- Năm nguyên tắc phân biệt game tương tự [1]:
  1. Chỉ bảo vệ sự thể hiện gốc: hỏi "đây là cách thể hiện hay ý tưởng?" và "mình tự làm hay đi mượn?".
  2. Chỉ so phần độc quyền: loại yếu tố thể loại (scènes à faire) và yếu tố chỉ có một cách làm (merger) trước khi so.
  3. Luật chơi không được bảo vệ: thứ viết được thành câu "nếu… thì…" (ba ô cùng màu cạnh nhau thì nổ; 5 mạng, hết mạng 30 phút
     hồi 1). Câu mô tả hiệu ứng cụ thể ("ba viên kẹo… tạo hiệu ứng trái tim") là cách thể hiện.
  4. Cách thể hiện rộng hơn mình nghĩ: bàn chơi, khung lá bài, chữ trên nút, cách bày HUD, bố cục tổng thể.
  5. Tổ hợp các yếu tố không được bảo vệ có thể được bảo vệ: từng yếu tố là thể loại, nhưng cách chọn, thiết kế và ghép có thể
     thành IP. Quan trọng nhất với hyper casual.
- Ví dụ của slide (puzzle có ông vua dẫn chuyện) [1]: được bảo vệ là hình dáng ông vua cụ thể (râu, vương miện, áo choàng),
  bảng màu, tỷ lệ, animation, biểu cảm, file .psd / .spine gốc; không được bảo vệ là ý tưởng "puzzle có ông vua", chủ đề hoàng
  gia, lâu đài, gia huy, khái niệm ông vua, con vật, hình khối.
- Bố cục chức năng (cây menu, sơ đồ điều khiển, vị trí nút, HUD) khả năng bảo hộ rất thấp, phải giống rất nhiều; nhưng cảm nhận
  tổng thể (bảng màu, hình dạng và tỷ lệ đối tượng chính, kiểu chuyển động, hiệu ứng phản hồi, bố cục màn chơi) là biểu đạt [2].

### 2.3 Bốn loại hay bị quên

| Loại | Điểm chính | Nguồn |
|---|---|---|
| Overall look & feel | Game là tác phẩm nghe nhìn, bảo hộ riêng, tách khỏi code | Stern v. Kaufman 1982 [1] |
| Creative UA | Mỗi bản là một tác phẩm nghe nhìn riêng: 30 bản test là 30 tác phẩm có quyền; mục đích thương mại không làm mất bản quyền | Bleistein v. Donaldson 1903 [1] |
| Trailer | Nhiều lớp quyền chồng nhau (gameplay quay, nhạc license, voice-over thuê); sở hữu video không đương nhiên sở hữu cả ba lớp; một lớp hết hạn có thể làm dừng phân phối (Alan Wake bị gỡ khỏi store 5/2017 vì hết license nhạc, lên lại 10/2018) | [1] |
| Nhạc | Một bản nhạc mang hai quyền, thường hai chủ: bản nhạc (sáng tác) và bản thu (master); thiếu một trong hai là xâm phạm lớp đó | Bridgeport Music v. Dimension Films 2005 [1] |

### 2.4 Thời hạn

Bảng 1.3. Ghi chú: thời hạn chỉ quan trọng khi dùng tác phẩm cũ "hết hạn bảo hộ"; một tác phẩm có thể hết hạn ở nước này mà còn
ở nước khác, nên với game toàn cầu, coi là còn hạn nếu còn ở bất kỳ thị trường nào.

### 2.5 Ai là chủ

- **Việt Nam (L1)**: tổ chức giao nhiệm vụ cho nhân viên, hoặc bên thuê làm theo hợp đồng, là chủ quyền tài sản, **trừ khi thoả
  thuận khác** (Luật SHTT Điều 39) [32]. Tác giả luôn giữ quyền nhân thân, gồm quyền bảo vệ sự toàn vẹn của tác phẩm (không cho
  người khác sửa, cắt xén, xuyên tạc), không chuyển nhượng được *(chưa đối chiếu gốc)*: hợp đồng nên có sự đồng ý trước cho việc
  sửa, phóng tác.
- **Mỹ (L3, L4)**: tác phẩm của nhân viên trong phạm vi công việc là "work made for hire" của công ty. Tác phẩm đặt hàng người
  ngoài chỉ là "work made for hire" khi thuộc một số loại luật liệt kê và có văn bản ký; ngoài ra phải có văn bản chuyển nhượng
  (Community for Creative Non-Violence v. Reid, 1989 [1]) [15]. → Hợp đồng freelancer, outsource luôn có điều khoản chuyển nhượng
  rõ ràng (QC 4.9), không dựa vào "mặc định" của nước nào.

### 2.6 Đăng ký bản quyền

- **Mỹ**: game làm và công bố lần đầu ngoài Mỹ không phải "United States work", nên được kiện ở Mỹ mà không cần đăng ký trước
  (§411(a)). Nhưng bồi thường luật định (statutory damages) và phí luật sư chỉ có khi đăng ký trước khi bị xâm phạm hoặc trong 3
  tháng sau khi công bố (§412), và điều này **áp cả cho tác phẩm nước ngoài** [15]. Không có bồi thường luật định thì phải chứng
  minh thiệt hại thực tế, rất khó với một game nhái. Lệ phí: 45 USD một tác phẩm; từ 17/2/2026 có đăng ký theo nhóm cho tới 20
  tác phẩm mỹ thuật 2D đã công bố, 85 USD *(chưa đối chiếu gốc; có đề xuất tăng)* [15].
- **Copyright Claims Board (Mỹ)**: toà nhỏ, bồi thường tối đa 30.000 USD; người nước ngoài được đi kiện ở đây, nhưng thường không
  bị kiện ở đây khi ở ngoài Mỹ; bị đơn có 60 ngày để từ chối [33].
- **Việt Nam**: đăng ký ở Cục Bản quyền tác giả là tự nguyện, giá trị chứng cứ khi tranh chấp [32].
- → QC 9.2: đăng ký key art, nhân vật chính, game ở Mỹ trong 3 tháng sau phát hành là cách rẻ để có công cụ mạnh khi bị nhái
  *(chờ [DUYỆT] quyết)*.

---

## 3. Nhãn hiệu

### 3.1 Sinh ra từ đâu

- Mỹ: quyền sinh ra từ sử dụng thực tế; được nộp đơn trước dựa trên ý định sử dụng, nhưng phải chứng minh đã dùng mới được cấp
  [22]. Bỏ không dùng 3 năm liên tục là suy đoán từ bỏ [1].
- EU, Anh, Nhật, Hàn, Trung Quốc, Việt Nam: nộp đơn trước được trước [22][32]. Nhãn hiệu không dùng một thời gian (Hàn, Trung Quốc:
  3 năm; Việt Nam: 5 năm *(chưa đối chiếu gốc)*) có thể bị huỷ.
- Hệ quả: tên game chỉ được bảo vệ ở nước đã đăng ký; game thành công sẽ bị người khác nộp tên trước ở nước mình chưa nộp. Nộp ở
  mọi thị trường chính **trước khi công bố, trước soft launch** [22].

### 3.2 Độ mạnh của tên

Từ mạnh tới yếu [1]: tự bịa (mạnh nhất) → từ có sẵn không liên quan tới game → gợi ý xa xa → mô tả thẳng game làm gì (không
đăng ký được) → tên chung của thể loại (không bao giờ được bảo vệ). Thứ tồn tại vì công dụng (nút Play to ở giữa dưới) không ai
độc quyền được [1].

### 3.3 Nhầm lẫn, và vì sao game mobile ở ngưỡng cao nhất

- Câu hỏi quyết định là người chơi bình thường có bị nhầm hai sản phẩm là của cùng một nhà, hoặc có liên quan, hay không; không
  cần nhầm thật, chỉ cần có thể gây nhầm; xét tổng thể (cách đọc, cách viết, cảm giác chung) [1].
- Bốn yếu tố: tên mạnh hay yếu; độ gần của sản phẩm (cùng store, cùng mục, cùng nền tảng quảng cáo: game mobile luôn ở mức tối
  đa); trạng thái người mua (cài nhanh, miễn phí, không ai đọc tên nhà phát hành); ý đồ (cố tình nhái là tăng nặng) [1].
- Hệ quả hai chiều: bị nhái thì dễ khiếu nại hơn ngành khác; đặt tên hơi giống ai thì cũng dễ bị khiếu nại hơn và khó cãi hơn [1].

### 3.4 Đăng ký: nhóm, Madrid, Trung Quốc, Mỹ

- Nhóm hàng (bảng phân loại Nice) thường dùng cho game mobile: 9 (phần mềm tải về), 41 (giải trí, game trực tuyến); thêm 28
  (đồ chơi, merch), 35 (quảng cáo, bán lẻ), 42 (phần mềm dịch vụ) tuỳ kế hoạch. Bản Nice 13 có hiệu lực 1/1/2026 dời một số hàng
  của nhóm 9; kiểm phạm vi hiện hành khi nộp [25].
- **Madrid**: nộp một đơn quốc tế qua Cục Sở hữu trí tuệ Việt Nam, chỉ định nhiều nước; Việt Nam, Mỹ, EU, Anh, Nhật, Hàn, Trung
  Quốc đều là thành viên (Việt Nam từ 11/7/2006). Đăng ký quốc tế **phụ thuộc đơn gốc ở Việt Nam trong 5 năm**: đơn gốc bị huỷ thì
  đăng ký quốc tế rơi theo ("central attack"), còn 3 tháng để chuyển thành đơn quốc gia giữ ngày ưu tiên. Mỗi nước có 12 tháng (18
  tháng với nước chọn) từ khi WIPO thông báo để cấp hay từ chối [25].
- **Trung Quốc**: chiếm tên trước (squatting) là rủi ro thật với game; kẻ chiếm tên nhắm vào nhóm hàng mình chưa đăng ký và nộp
  thêm nhóm lân cận khi bị phản đối. Tên chữ Hán phiên âm có thể bị người khác giữ riêng với tên Latin (vụ "Qiaodan"). Thực tế nên:
  nộp sớm, trước khi công bố, ít nhất nhóm 9, 41, 28, cả tên Latin và tên chữ Hán; huỷ vì không sử dụng 3 năm là đường rẻ hơn huỷ
  vì gian dối [23].
- **Mỹ**: từ 3/8/2019 người nộp đơn có trụ sở ngoài Mỹ (studio Việt Nam) **bắt buộc** có luật sư được cấp phép ở Mỹ đại diện [24].

### 3.5 Thương hiệu thật trong game

- Mỹ: xe, vũ khí có thương hiệu trong game có thể được bảo vệ như biểu đạt nghệ thuật (Rogers test): AM General v. Activision
  (S.D.N.Y. 2020) bác đơn của hãng Humvee vì xe Humvee trong Call of Duty có giá trị nghệ thuật (hiện thực) và không gây hiểu lầm
  rõ ràng [30]. Nhưng Jack Daniel's v. VIP Products (Toà tối cao Mỹ, 2023) thu hẹp: không áp Rogers khi dấu hiệu được dùng như
  nhãn hiệu cho sản phẩm của chính mình (vd tên game, icon) [30]. Thắng kiện vẫn tốn tiền kiện.
- Model 3D có thương hiệu thật trên marketplace thường chỉ có license Editorial, cấm dùng thương mại (4.2).
- Hình ảnh người thật: Việt Nam: sử dụng hình ảnh cá nhân phải được người đó đồng ý (Bộ luật Dân sự 2015, Điều 32) *(chưa đối
  chiếu gốc)* [32]; Mỹ: quyền hình ảnh (right of publicity) theo luật từng bang.
- → QC 4.15: mặc định tên, hình hư cấu.

---

## 4. License tài sản và công cụ

### 4.1 Unity Asset Store

Theo Standard Unity Asset Store EULA [6]:

- Chỉ "Extension Asset" (công cụ Editor, scripting, services) tính theo seat, tối đa 2 máy một lúc mỗi seat; asset khác (art,
  audio, template) dùng trên mọi máy của bên được cấp phép.
- EULA không có điều khoản cho phép rõ việc dùng asset trong quảng cáo số, trailer, playable (chỉ nói tới tài liệu quảng cáo vật
  lý). → QC 3.4: `scope.ads` của pack Asset Store mặc định là `"unknown"`.
- Chỉ được sao chép asset khi đã nhúng trong sản phẩm build; phân phối lại file gốc (repo công khai, registry, gateway tải không
  cần token) gần như chắc chắn vi phạm. → gate IP-2.

### 4.2 Font, nhạc, thư viện asset

**Font**

- SIL OFL: được nhúng font chưa sửa vào app; bản sửa phải giữ OFL; không bán riêng font [8]. Google Fonts phần lớn là OFL, một số
  Apache 2.0, vài font UFL: đều cho nhúng thương mại [10].
- Font thương mại: license "Desktop" không đủ; cần license "App / Embedding" riêng, thường giới hạn số người dùng, theo năm [10].
- Adobe Fonts: license đồng bộ qua Creative Cloud cho dùng trên máy và web, **không** cho đóng gói file font vào app hay game; một
  số foundry trên Adobe Fonts ghi rõ loại trừ game, app mobile *(chưa đối chiếu gốc)* [10].
- Nhập font vào font asset TextMesh Pro không thay đổi license của font gốc [10].
- `OS/2.fsType` trong file font: 0 Installable; 2 Restricted (không nhúng khi chưa được phép); 4 Preview & Print; 8 Editable;
  cờ 0x0100 No Subsetting, 0x0200 Bitmap only [7].

**Nhạc và âm thanh**

- Hai lớp quyền (sync cho bản nhạc, master cho bản thu); dùng trong game và dùng trong trailer, quảng cáo thường là phạm vi khác
  nhau; nhạc CC "NC" không dùng được trong game có kiếm tiền; Content ID khớp tự động, có license vẫn có thể bị claim [5].
- Gói thư viện nhạc phổ biến thường **không** phủ việc dùng trong game ở gói cá nhân [10]:

| Thư viện | Dùng trong game | Quảng cáo trả tiền |
|---|---|---|
| Epidemic Sound | Chỉ gói Business Plus (giới hạn số app, game) hoặc Enterprise | Từ gói Pro (cá nhân) / Business trở lên |
| Artlist | Không gói thuê bao nào; app, game cần hợp đồng Enterprise riêng | Gói Pro / Business |
| Musicbed | Chỉ Enterprise | Web, social ở gói thường; broadcast cần Enterprise |
| PremiumBeat | Premium (ngân sách game ≤ 2 triệu USD), Business (≤ 5 triệu), Enterprise | Standard: web, social; Premium trở lên thêm TV |
| AudioJungle (Envato Market) | "Music Standard" giới hạn 10.000 bản sao, không đủ cho game mobile; cần Mass Reproduction trở lên | Standard: quảng cáo số không phát sóng |
| Envato Elements | Được, theo dự án | Được, trừ phát sóng (cần Enterprise) |

**Thư viện asset**

| Nguồn | Thương mại trong game | Quảng cáo | Ghi công | Bẫy |
|---|---|---|---|---|
| Kenney | Được (CC0) | Được | Không bắt buộc | – |
| OpenGameArt | Theo từng asset | Theo từng asset | CC-BY, CC-BY-SA, GPL: có | CC-BY-SA, GPL là copyleft; kiểm từng asset *(chưa đối chiếu gốc)* |
| itch.io | Theo từng người bán | Theo từng người bán | Theo người bán | Không có điều khoản chung của nền tảng |
| Sketchfab | CC0, CC-BY, CC-BY-SA, Standard: được; CC-BY-NC: không | Standard: được; **Editorial: cấm** | CC-BY, CC-BY-SA | Editorial chỉ cho tin tức |
| CGTrader, TurboSquid | Royalty-free / Standard: được, phải là một phần của sản phẩm lớn hơn | **Editorial: cấm** | Không | Model có thương hiệu thật (xe, súng, máy bay) thường chỉ có Editorial; dùng thương hiệu thật cần quyền riêng từ chủ thương hiệu |
| Mixamo | Được, kể cả game *(chưa đối chiếu gốc)* | Chưa rõ ngoài game | Không | Không phân phối lại file gốc |
| Freesound | CC0, CC-BY: được; CC-BY-NC: không | Theo license | CC-BY | License đặt theo từng âm thanh |
| Zapsplat | Được | Được | Gói miễn phí: bắt buộc | *(chưa đối chiếu gốc)* |
| Pixabay | Được | Được | Không | Không dùng ảnh có thương hiệu, gợi ý bảo trợ; không phân phối lại nguyên bản |
| Envato Elements (asset) | Một license cho một sản phẩm cuối mỗi lần tải | Được | Không | Không phân phối lại dạng gốc |
| Freepik, Flaticon | Được | Được | Gói miễn phí: bắt buộc | Không phân phối file nguồn |
| Shutterstock | Standard: được | Được | Không | Không làm logo, nhãn hiệu; không hàng bán lại |

Nguồn bảng: [10], phần Envato, Freepik, Shutterstock: [5].

### 4.3 Asset do AI tạo

**Có bản quyền không** (thị trường, L3):

| Nơi | Quy định | Nguồn |
|---|---|---|
| Mỹ | Đầu ra thuần AI không được bảo hộ; phần người đóng góp sáng tạo (chọn, sắp xếp, sửa) thì được; chỉ viết prompt chưa đủ (Copyright Office, Part 2, 29/1/2025). Thaler v. Perlmutter: phúc thẩm 3/2025 giữ yêu cầu tác giả là người, Toà tối cao từ chối thụ lý 2/3/2026 | [9] |
| EU | Chưa có luật riêng; tiêu chuẩn "sáng tạo trí tuệ của chính tác giả" cần lựa chọn tự do của người; bình luận chung: thuần AI khó được bảo hộ | [18] |
| Anh | Điều 9(3) CDPA: tác phẩm do máy tạo không có tác giả người được bảo hộ 50 năm, tác giả là người "sắp đặt"; báo cáo của chính phủ (3/2026) đề xuất bỏ, chưa có luật | [17] |
| Nhật | Hướng dẫn của Cục Văn hoá (3/2024): đầu ra thuần máy không được bảo hộ, đóng góp sáng tạo thật của người thì có; Điều 30-4 là ngoại lệ cho giai đoạn huấn luyện | [19] |
| Hàn | Hướng dẫn đăng ký (2025): không đăng ký đầu ra không có can thiệp sáng tạo của người; chỉ bảo hộ phần người làm; phải khai phần nào do AI | [20] |
| Trung Quốc | Li v. Liu (Toà Internet Bắc Kinh, 27/11/2023, sơ thẩm): ảnh Stable Diffusion được bảo hộ vì người dùng điều chỉnh prompt, tham số nhiều lần; xét từng vụ | [21] |
| Việt Nam | Luật SHTT chưa có điều khoản về tác phẩm do AI tạo; tác giả phải là cá nhân nên tác phẩm thuần AI khó được coi là tác phẩm *(bình luận, chưa có văn bản)* | [32] |

**Nghĩa vụ công bố**:

- EU AI Act Điều 50, áp dụng từ 2/8/2026: bên cung cấp AI tạo nội dung phải đánh dấu đầu ra máy đọc được; bên dùng AI tạo
  "deepfake" (nội dung giống người, nơi, sự kiện thật, có thể bị tưởng là thật) phải công bố cho người xem thấy. Nội dung rõ ràng
  nghệ thuật, hư cấu được nhẹ hơn; nội dung kỳ ảo rõ ràng (rồng, người bay) theo dự thảo hướng dẫn nằm ngoài định nghĩa. Art game
  hư cấu, cách điệu có lẽ không bị; nội dung giống người thật, nơi thật theo kiểu ảnh chụp, nhất là trong quảng cáo, có thể bị.
  Phạt tới 15 triệu EUR hoặc 3% doanh thu toàn cầu. Chưa có nguồn nói riêng về game *(suy luận, chưa đối chiếu gốc)* [18].
- Steam: từ 1/2024 phải khai báo nội dung AI trên trang store [5].
- Google Play: chính sách cho app có tính năng AI tạo nội dung; không bắt dán nhãn asset AI [4].

→ QC 4.7: ghi phần người làm cho asset quan trọng (nhân vật chính, key art, icon); creative có người, nơi thật làm bằng AI phải
qua [DUYỆT] khi chạy ở EU.

**Điều khoản của công cụ** (quyền với đầu ra, dùng thương mại, bồi thường IP) [10]:

| Công cụ | Đầu ra | Thương mại | Ngưỡng theo gói | Bồi thường IP |
|---|---|---|---|---|
| Google Gemini API (Imagen, Veo qua API) | Google không đòi quyền với đầu ra | Được, API dùng cho mục đích kinh doanh | – | Không trong điều khoản API |
| Google Vertex AI | như trên | Được | – | Có cho danh sách "Generative AI Indemnified Services" (danh sách hiện hành *chưa đối chiếu gốc*) |
| Seedance (BytePlus ModelArk) | Chỉ cấp **license giới hạn, có thể thu hồi** với đầu ra, không chuyển quyền sở hữu | *chưa đối chiếu gốc* | *chưa đối chiếu gốc* | Không thấy |
| OpenAI (ảnh, Sora) | Người dùng sở hữu đầu ra trong phạm vi luật cho phép | Được | – | *chưa đối chiếu gốc* |
| Midjourney | Theo gói trả tiền | Được ở gói trả tiền | Công ty doanh thu > 1 triệu USD/năm phải dùng gói Pro hoặc Mega | Không |
| Stability AI (Community License) | Được | Được | Tổ chức doanh thu ≥ 1 triệu USD/năm cần Enterprise | Không thấy |
| Adobe Firefly | Được | Được, trong phạm vi tính năng Adobe liệt kê | – | Có, chỉ với gói có quyền lợi bồi thường |
| Suno | Gói trả tiền có quyền thương mại; từ 9/2026 phải tải qua hệ thống của Suno, giới hạn theo gói *(chưa đối chiếu gốc)* | Gói Pro trở lên | Gói miễn phí: phi thương mại | *chưa đối chiếu gốc* |
| Udio | Gói trả tiền | *chưa đối chiếu gốc* | – | *chưa đối chiếu gốc* |
| ElevenLabs | Gói trả tiền dùng thương mại | Được, **trừ Eleven Music**: phim, TV, streaming, quảng cáo cần ElevenLabs đồng ý bằng văn bản | Gói miễn phí: không | *chưa đối chiếu gốc* |

- Kiện tụng nhạc AI (10/2026): Warner hoà giải với Udio (10/2025) và Suno (11/2025); Universal hoà giải với Udio (10/2025); Sony
  chưa hoà giải với bên nào và còn kiện *(nguồn báo chí)* [10]. Đầu ra nghe giống nghệ sĩ cụ thể vẫn có rủi ro.
- Hệ quả cho QC 4.7: sổ phải ghi **gói tài khoản lúc tạo**, vì quyền thương mại đi theo gói và theo doanh thu của công ty, không theo
  công cụ.

### 4.4 Middleware có ngưỡng

Một license mua lúc studio còn nhỏ có thể thành vi phạm khi game thành công, mà không ai nhận ra [10]:

| Công cụ | Ngưỡng | Hậu quả hay gặp |
|---|---|---|
| Unity | Personal: doanh thu + vốn gọi ≤ 200.000 USD / 12 tháng; trên đó Pro (tới 25 triệu), trên nữa Enterprise | Vượt ngưỡng mà vẫn Personal |
| Odin Inspector | Doanh thu / vốn > 200.000 USD / 12 tháng: phải Enterprise | Vẫn seat Personal |
| Spine (runtime) | Runtime cần license Spine Editor; Essential / Professional chỉ khi doanh thu / vốn < 500.000 USD / 12 tháng | Vẫn Essential |
| Live2D Cubism SDK | Miễn license phát hành khi doanh thu cả công ty < 10 triệu yên / năm | Vượt mà chưa có license phát hành |
| FMOD | Indie: ngân sách dự án < 600.000 USD *(chưa đối chiếu gốc)* | Ngân sách tính cả live-ops, UA |
| Wwise | Indie: ngân sách < 250.000 USD *(chưa đối chiếu gốc)* | Có kiểm lại qua license key |
| Photon PUN / Fusion | Gói miễn phí 100 CCU cho một app | Không bị tính tiền mà bị chặn kết nối: thành sự cố |
| DOTween Pro, A* Pathfinding Pro, I2 Localization | Theo seat | Dùng chung một seat |

---

## 5. Store và toà xét thế nào

### 5.1 Hai hệ thống phản ứng khác nhau

| | Nền tảng (L2) | Toà (L4) |
|---|---|---|
| Hỏi gì | App có vi phạm chính sách không | Phần biểu đạt sáng tạo có tương tự đáng kể không |
| Cách làm | Nhìn icon, tên, screenshot, 30 giây đầu | Trừu tượng hoá → lọc → so sánh |
| Thời gian | 3–5 ngày | 2–4 năm |
| Hậu quả tối đa | Chấm dứt tài khoản developer | Bồi thường + lệnh cấm với một game |

Nguồn: [2]. → QC 6.1 (lớp store), QC 0 (nguyên tắc 5).

### 5.2 Ba bước của toà

Phép thử trừu tượng hoá – lọc – so sánh (gốc từ Computer Associates v. Altai, 2d Cir. 1992, cho phần mềm) [2]:

1. **Trừu tượng hoá**: tách tác phẩm thành tầng, từ ý tưởng chung nhất tới chi tiết cụ thể nhất. Mục đích, chức năng là ý tưởng;
   thứ không thiết yếu cho mục đích là biểu đạt. Câu hỏi: "còn làm khác được không?" Còn thì đó là lựa chọn của tác giả.
2. **Lọc**: bỏ thứ không được bảo hộ (5.3), lọc **từng yếu tố**, không lọc cả tác phẩm một lần.
3. **So sánh**: chỉ phần còn lại, bằng mắt người chơi bình thường (ordinary observer), trên tổng thể (total concept and feel),
   chỉ cần tương tự đáng kể (substantial similarity). Phải nheo mắt mới thấy khác ở mức chi tiết thì là giống; nếu người chơi
   không được cho biết thì không phân biệt được hai game [2].

### 5.3 Bốn bộ lọc

| Bộ lọc | Loại gì | Câu hỏi |
|---|---|---|
| Ý tưởng / quy trình (idea–expression) | Hệ thống, phương pháp, cách vận hành | Đây là luật chơi hay hình ảnh của luật chơi? |
| Luật chơi thuộc công chúng | Luật ai cũng được dùng lại | (như trên) |
| Yếu tố bắt buộc của thể loại (scènes à faire) | Thứ mọi game cùng chủ đề buộc phải có | Ai làm thể loại này cũng làm thế, hay chỉ game kia làm thế? |
| Hợp nhất (merger) | Ý tưởng chỉ có một hoặc rất ít cách diễn đạt | Có cách làm khác không? |

Nguồn: [2]. → QC 6.2.

### 5.4 Án lệ

| Vụ | Nơi | Kết quả, bài học | Nguồn |
|---|---|---|---|
| Stern Electronics v. Kaufman (1982) | Mỹ | Màn hình game là tác phẩm nghe nhìn, bảo hộ độc lập với code | [1] |
| Atari v. North American Philips (1982), Pac-Man / K.C. Munchkin | Mỹ | "Đuổi bắt trong mê cung" là ý tưởng tự do, nhưng nhân vật, quái, hiệu ứng cụ thể của Pac-Man là biểu đạt; đổi vài chi tiết không thoát vì chiếm đoạt cảm nhận tổng thể | [2] |
| Atari v. Amusement World (1981), Asteroids / Meteors | Mỹ | 22 điểm giống nhưng phần lớn không tránh được để thể hiện ý tưởng bắn đá ngoài không gian với phần cứng 1981 (merger); trải nghiệm thẩm mỹ khác → không vi phạm | [2] |
| Data East v. Epyx (9th Cir. 1988) | Mỹ | Sơ thẩm xử có vi phạm; phúc thẩm lật: điểm giống đến từ môn karate và giới hạn phần cứng; sai lầm là so tổng thể trước khi lọc | [2] |
| Incredible Technologies v. Virtual Technologies (7th Cir. 2005) | Mỹ | Giữ việc từ chối lệnh cấm: yếu tố tất yếu của golf là scènes à faire, hình hướng dẫn trackball là chức năng | [2] |
| Tetris Holding v. Xio (D.N.J. 2012) | Mỹ | Xio thừa nhận cố tình chép; hình khối, màu, sân 10×20, ô khối tiếp theo, bóng khối là biểu đạt vì còn nhiều cách khác mà vẫn giữ luật | [2] |
| Spry Fox v. LOLApps (W.D. Wash. 2012), Triple Town / Yeti Town | Mỹ | Toà không bác đơn: chuỗi tiến hoá cỏ → bụi → cây → nhà kèm con gấu là biểu đạt; thay vỏ đồ hoạ không che được; hai bên hoà giải, bản quyền Yeti Town chuyển cho Spry Fox | [2] |
| King v. Avocado (Toà tối cao Hàn Quốc, 27/6/2019), Farm Heroes Saga / Forest Mania | Hàn | Hai cấp dưới không thấy vi phạm bản quyền; Toà tối cao: cách chọn, sắp xếp, kết hợp các luật tương tự đáng kể là vi phạm; lần đầu toà này công nhận vi phạm với game nhái | [13] |
| Nexon v. Ironmace (2025, Toà tối cao Hàn chốt), Dark and Darker | Hàn | Bản quyền bị bác (không tương tự đáng kể); bí mật kinh doanh thắng, 5,76 tỷ won (mục 8) | [11] |
| NetEase v. Huaduo (Quảng Châu 2017, phúc thẩm 2019) | Trung Quốc | Hình ảnh liên tục trên màn hình game là tác phẩm nghe nhìn của nhà làm game, kể cả khi do người chơi tạo ra khi chơi; bồi thường 20 triệu NDT | [27] |
| GREE v. DeNA (Toà quận Tokyo, 23/2/2012) | Nhật | Game câu cá: vi phạm chỉ ở một màn hình cụ thể (màn kéo cần), không ở ý tưởng game câu cá; 234,6 triệu yên. Có nguồn nói phúc thẩm đã lật *(chưa đối chiếu gốc)* | [28] |
| Nova Productions v. Mazooma (Anh, 2007) | Anh | Chép "look and feel" chung, luật, cơ chế (cách tính điểm, thanh lực) không phải vi phạm; chỉ chép code, art cụ thể mới là | [29] |
| Sony v. Tencent (N.D. Cal. 2025), Light of Motiram / Horizon | Mỹ | Kiện bản quyền và nhãn hiệu lúc game mới công bố; hoà giải kín, bác đơn vĩnh viễn 17/12/2025; game bị gỡ khỏi Steam, Epic | [12] |
| Krafton v. NetEase (2018–2023), PUBG / Knives Out | Mỹ | Hoà giải 2019; kiện lại 2020 vì vi phạm hoà giải; 2023: có vi phạm nhưng không chứng minh được thiệt hại 65 triệu USD *(chưa đối chiếu gốc)* | [12] |

Đọc chung: toà các nước đều không bảo vệ cơ chế, thể loại; đều bảo vệ hình, chữ, âm thanh cụ thể; Mỹ và Hàn (sau 2019) bảo vệ cả
tổ hợp chọn và sắp xếp; Anh hẹp nhất. Với game toàn cầu, lấy mức rộng nhất (Mỹ, Hàn) làm chuẩn kiểm (QC 6).

### 5.5 Cơ chế gỡ của từng nền tảng (L2)

**DMCA §512, khung chung của nền tảng Mỹ** [2][15]:

- Nền tảng được miễn trách nhiệm với nội dung người khác đưa lên nếu: gỡ nhanh khi nhận thông báo hợp lệ; nhận counter-notice và
  khôi phục; có chính sách với người vi phạm lặp lại. Bốn loại: §512(a) truyền dẫn, (b) lưu đệm, (c) lưu trữ theo yêu cầu người
  dùng (YouTube, Drive, chợ ứng dụng), (d) công cụ tìm kiếm.
- Thông báo hợp lệ có sáu ý (§512(c)(3)(A), xem `GameIP_UngPho.md` mục 5).
- Counter-notice (§512(g)(3)): người ở ngoài Mỹ phải **đồng ý thẩm quyền của toà liên bang ở bất kỳ hạt nào nơi nền tảng "có thể
  được tìm thấy"** và nhận giấy tờ từ bên khiếu nại; một toà phúc thẩm (10th Cir. 2024) hiểu "có thể được tìm thấy" theo nghĩa
  thông thường, rộng [34]. Nghĩa là gửi counter-notice để lấy lại game là bỏ lá chắn "công ty Việt Nam, toà Mỹ không có thẩm quyền"
  cho tranh chấp đó.
- Nền tảng khôi phục sau 10 đến 14 ngày làm việc kể từ counter-notice, trừ khi bên khiếu nại báo đã khởi kiện (§512(g)(2)(C)).
- Khai sai trong thông báo hay counter-notice (biết rõ, trọng yếu) phải bồi thường (§512(f)) [15]. Khiếu nại cơ chế, ý tưởng là
  khiếu nại sai.

**Google Play** [2][4]: xử thông báo bản quyền theo §512: gỡ app, công bố thông báo lên cơ sở dữ liệu Lumen; developer có thể
counter-notice. Tranh chấp tên, icon đi kênh khác: form nhãn hiệu hoặc chính sách Impersonation (không cần nhãn hiệu đã đăng ký).
Muốn biết developer ẩn danh là ai: trát §512(h) ở toà Mỹ.

**Apple App Store** [2][3]: xử theo hợp đồng developer (DPLA), không theo §512: form tranh chấp IP chung, Apple chuyển thư cho hai
bên tự làm việc, không phân xử; Apple có quyền gỡ app "có hay không có lý do". Musi v. Apple (N.D. Cal., bác đơn vĩnh viễn
3/2026) xác nhận Apple được làm vậy. Không có counter-notice, không có đồng hồ 10–14 ngày; tỷ lệ khôi phục sau kháng nghị
khoảng 1,6% (421 / 26.224, theo báo cáo DSA) [2]. Apple gỡ nhanh và thường nhanh hơn Google [2].

**Số liệu** (báo cáo DSA của EU, kỳ 01–06/2026, theo slide) [2]: Apple gỡ 0 vụ theo cơ sở pháp luật, 496 vụ theo điều khoản dịch
vụ; Google 576 vụ theo cơ sở pháp luật, 281 vụ theo điều khoản dịch vụ.

**Google Play, chi tiết** [31]:

- Chính sách IP: không cho app hay tài khoản developer xâm phạm IP của người khác; sửa đổi nội dung có bản quyền vẫn có thể là
  vi phạm; nhãn hiệu giống hoặc gần giống gây nhầm lẫn có thể bị đình chỉ.
- Chính sách Impersonation: cấm app mạo danh, có ví dụ đúng trường hợp clone: tên và icon giống sản phẩm có sẵn tới mức người
  dùng có thể bị nhầm.
- Ba kênh báo: bản quyền (form DMCA), nhãn hiệu (form nhãn hiệu, nên liên hệ developer trước), mạo danh (nút "Flag as
  inappropriate" trên trang app).
- Đình chỉ tính là "strike"; vi phạm lặp lại hoặc nghiêm trọng dẫn tới **chấm dứt tài khoản developer và các tài khoản liên
  quan**: mất mọi app, không hoàn phí, tài khoản mới cũng bị ảnh hưởng.

**Apple, chi tiết** [3][31]: 4.1(a) không chép app đang hot hay đổi chút tên, UI rồi coi là của mình; 4.1(b) mạo danh có thể bị
đuổi khỏi Developer Program; 4.3(b) không nộp app không phân biệt được với thứ đã có nhiều, nộp lặp lại có thể bị đuổi. Apple không
công bố ngưỡng strike. Khi báo clone: viện dẫn 4.1(a), 4.1(b), 4.3(b) cùng form tranh chấp IP. Form riêng cho tranh chấp tên app
(app khác chiếm tên là nhãn hiệu của mình).

**Steam** [31]: chỉ có kênh thông báo DMCA gửi Copyright Agent của Valve (steamcommunity.com/dmca/create/); không thấy form nhãn
hiệu riêng *(chưa đối chiếu gốc)*; game thường chỉ bị gỡ sau thông báo DMCA chính thức, không phải do người dùng báo. Quy định
trang store: chỉ dùng nội dung mình sở hữu hoặc có quyền; capsule chỉ chứa art, tên của chính game.

**EU, Digital Services Act** [31]:

- Điều 16: kênh thông báo nội dung bất hợp pháp (rộng hơn DMCA, không chỉ bản quyền): lý do, URL chính xác, tên, email người báo,
  cam đoan thiện chí; thông báo đủ chính xác coi như nền tảng "biết thực sự".
- Điều 17: khi nền tảng hạn chế app hay tài khoản, phải đưa lý do rõ ràng, căn cứ, có dùng máy tự động không, và cách khiếu nại:
  công cụ của mình khi bị đối thủ báo sai.
- Điều 30: nền tảng phải thu thông tin của trader (tên, địa chỉ, điện thoại, email, giấy tờ, số đăng ký kinh doanh) trước khi cho
  bán ở EU; **Điều 30(7): phải hiển thị công khai** tên, địa chỉ, điện thoại, email, số đăng ký trên trang sản phẩm. Apple bắt
  developer khai trạng thái trader trong App Store Connect, hiển thị trên trang EU; app thiếu có nguy cơ bị gỡ ở EU từ 17/2/2025.
  Trang tương đương của Google *(chưa đối chiếu gốc)*.
- Dùng khi bị nhái: Điều 30(7) lộ danh tính thật của clone có kiếm tiền ở EU (cơ sở cho thư yêu cầu, kiện); listing thiếu thông
  tin trader báo được như lỗi tuân thủ DSA, tách khỏi khiếu nại IP. Chiều ngược lại: game mình phát hành ở EU cũng phải khai
  đủ thông tin trader.

### 5.6 Quảng cáo và creative

- Creative bị siết chặt hơn game [2]: không có lá chắn "cơ chế, thể loại" (video là tác phẩm nghe nhìn, chép footage là chép
  nguyên cách thể hiện); license tài sản thường chỉ phủ game, không phủ quảng cáo; nhạc trong game ít ai soi nhưng trong quảng cáo
  bị nhận dạng âm thanh tự động gần như ngay; playable mang code, art thật, tải về được.
- Apple xét creative [3]: preview trên App Store chỉ là quay màn hình của chính app (2.3.4; Google không có quy tắc tương đương);
  quảng cáo chạy ngoài App Store mà mô tả sai app vẫn bị xét, hậu quả rơi vào app (2.3.1).

**Nền tảng quảng cáo** [31]:

| Nền tảng | IP | Nhạc | Báo vi phạm |
|---|---|---|---|
| Meta (Facebook, Instagram) | Quảng cáo không được vi phạm IP bên thứ ba; nhạc phải có license cả bản thu lẫn bản nhạc | **Sound Collection**: miễn phí bản quyền, dùng được cho quảng cáo, **chỉ trên Meta**. Thư viện nhạc thường (trending) chỉ cho dùng cá nhân, phi thương mại: dùng trong quảng cáo có thể bị tắt tiếng, gỡ *(trích qua kết quả tìm kiếm, chưa đối chiếu gốc)* | IP Reporting Center (bản quyền, nhãn hiệu, hàng giả, nhắm được một quảng cáo cụ thể); Brand Rights Protection có luồng mạo danh riêng; Rights Manager khớp video |
| TikTok | Cấm dùng thương hiệu bên thứ ba khi chưa được phép, cấm dùng clip từ nguồn không được phép (gồm trailer, gameplay của đối thủ) | **Commercial Music Library**: đã cleared cho dùng thương mại, cả quảng cáo, cần Business Account; dùng ngoài TikTok *(chưa đối chiếu gốc, coi như chỉ trên TikTok)*. Thư viện nhạc chung: không cho doanh nghiệp | Nút báo trên quảng cáo hoặc form ads.tiktok.com/business/en/report (kèm Ad ID) |
| Google Ads | Đặt giá từ khoá là tên nhãn hiệu của đối thủ: **được** ở mọi nơi; dùng nhãn hiệu của đối thủ trong **chữ quảng cáo**: bị hạn chế | – | Chủ nhãn hiệu báo qua "Report Content on Google"; cảnh báo trước ít nhất 7 ngày |
| Apple Ads (Search Ads) | Apple **không xử** khiếu nại về từ khoá là tên thương hiệu; chỉ xử nội dung quảng cáo | – | Form tranh chấp IP của Apple |
| YouTube | Content ID quét cả video unlisted; claim có thể chặn (không xem được ở nước đó) hoặc kiếm tiền (tiền về bên claim) | – | – |
| AppLovin, Unity Ads / LevelPlay, Mintegral, AdMob | Cùng một khung: không IP, thương hiệu, người nổi tiếng của bên khác; quảng cáo phải đúng với app thật (không UI giả, phần thưởng giả, gameplay giả); vi phạm lặp lại có thể mất cả tài khoản. Mintegral ghi rõ nhất: creative phải thể hiện đúng sản phẩm; nêu đích danh IP lớn (Disney, Nintendo, Netflix) bị cấm | – | Theo từng mạng |

Playable trên AppLovin: một file HTML ≤ 5 MB, tài nguyên nhúng base64, không gọi mạng ngoài khi chưa được duyệt: asset nằm trọn
trong file, ai cũng lấy ra được.

**Cơ quan quản lý: gameplay trong quảng cáo** [31]:

- Anh (ASA, CAP Code 3.1): gỡ quảng cáo Homescapes, Gardenscapes của Playrix (2020) vì làm nổi phần "rút chốt" chỉ khoảng 0,03%
  người chơi gặp; Evony (Top Games, 3/4/2024) tương tự. Năm kiểu ASA hay xử: (1) quảng cáo minigame như thể là game chính; (2) hình,
  cơ chế không có trong app; (3) cinematic bị tưởng là gameplay; (4) dòng "không phải mọi hình ảnh là gameplay thật" không cứu được
  ấn tượng sai; (5) footage của game khác.
- EU (mạng CPC): "Key Principles on In-Game Virtual Currencies" (21/3/2025) về minh bạch giá tiền ảo; có tin về đợt hành động
  phối hợp mới cuối 2026 với nhiều studio lớn *(nguồn báo, chưa đối chiếu gốc)*. Anh: ASA đang thực thi yêu cầu ghi rõ có loot box
  trên trang store từ 26/5/2026. Hai việc này thuộc bảo vệ người tiêu dùng, ngoài phạm vi IP của quy chuẩn, ghi lại để [REL] biết.

→ QC 4.17, QC 8.

---

## 6. Vì sao và khi nào game bị nhắm

### 6.1 Công thức

Bên khiếu nại hành động khi **thiệt hại + khả năng chứng minh > chi phí theo đuổi + rủi ro phản đòn**; họ đánh vào chỗ dễ chứng
minh nhất (bằng chứng rõ, ít tốn phí luật sư), không phải chỗ giống nhất. Câu team nên tự hỏi không phải "feature này có giống quá
không?" mà "nếu ai đó muốn dựng hồ sơ chống lại mình, họ tìm thấy gì dễ nhất?" [2]. → QC 0 (nguyên tắc 4), QC 6.4.

### 6.2 Đối thủ thấy mình từ creative đầu tiên

Qua bảng xếp hạng store (quét hằng ngày), thư viện quảng cáo công khai của nền tảng, báo cáo creative intelligence mà studio lớn
đều mua, báo chí và cộng đồng người chơi, nhân sự chuyển việc [2].

Bảng vụ việc trong slide [2] (thời điểm bị nhắm): Dark and Darker (playtest vòng 5, Nexon), Light of Motiram (lúc công bố, Sony),
một clone Palworld (lúc pre-order, Apple gỡ trong ngày), Knives Out (vượt 50 triệu MAU, PUBG), Palworld (sau 8 tháng, Nintendo:
sáng chế), clone Tomodachi Life (khoảng 50.000 lượt tải, Google gỡ trong ngày), clone Sora (khoảng 160.000 USD doanh thu, Apple
gỡ), Smash Fest! (lúc scale 6/2026, ít nhất 10 game bắt chước). Bị nhắm ở mọi quy mô.

### 6.3 Vùng nguy hiểm nhất ở giữa, và doom loop

| Quy mô của mình | Họ dùng gì | Mình có bao lâu |
|---|---|---|
| Rất nhỏ | Form khiếu nại trên store | Vài ngày, thường không kịp biết |
| Đang scale, có doanh thu | Store + report creative trên nền tảng quảng cáo | Vài tuần |
| Rất lớn, có luật sư | Kiện ra toà (store sẽ không tự gỡ) | Nhiều năm |

Doom loop: takedown trên store (2–3 ngày) → counter-notice → bên kia im lặng (10–14 ngày làm việc) → khởi kiện xin lệnh cấm, app
bị gỡ, có thể kéo dài nhiều năm. Im lặng có thể là tín hiệu nguy hiểm, không phải an toàn [2]. → `GameIP_UngPho.md`.

---

## 7. Tra nhãn hiệu

| Thị trường | Công cụ | Ghi chú |
|---|---|---|
| Mỹ | USPTO Trademark Search (tmsearch.uspto.gov) | API duy nhất là TSDR, chỉ tra trạng thái theo số đơn, cần key [14] |
| EU | EUIPO eSearch; TMview (tmdn.org/tmview, gom nhiều cơ quan) | Không có API công khai chính thức [14] |
| Anh | UKIPO trademark search | |
| Nhật | J-PlatPat | |
| Hàn | KIPRIS | |
| Trung Quốc | CNIPA (sbj.cnipa.gov.cn) | Tra cả tên chữ Hán |
| Việt Nam | Thư viện số SHCN của Cục SHTT (iplib.ipvietnam.gov.vn) | Chỉ tra trên web [14] |
| Nhiều nước | WIPO Global Brand Database (branddb.wipo.int) | Điều khoản **cấm** tải hàng loạt, cào, truy vấn tự động [14] |

→ Skill chỉ sinh link để người tra có chủ đích; không tự cào. Tra trùng không thay cho ý kiến của đại diện SHTT khi nộp đơn.

---

## 8. Bí mật kinh doanh

- Điều kiện gần giống nhau ở mọi nơi: không phải hiểu biết chung; có giá trị vì được giữ kín; chủ sở hữu có **biện pháp hợp lý**
  để giữ [1][26].

| Nơi | Luật | Điểm riêng |
|---|---|---|
| Việt Nam | Luật SHTT Điều 84 | Ba điều kiện như trên [32] |
| Mỹ | Defend Trade Secrets Act (18 U.S.C. §1836) | Bồi thường thực tế + làm giàu bất chính, tới 2 lần khi cố ý; thời hiệu 3 năm từ khi phát hiện [26] |
| EU | Chỉ thị 2016/943 | "Biện pháp hợp lý trong hoàn cảnh", nước thành viên tự định [26] |
| Nhật | Luật chống cạnh tranh không lành mạnh | "Quản lý như bí mật": người có quyền truy cập phải nhận ra được ý định giữ bí mật một cách khách quan; có hình sự [26] |
| Hàn | Luật chống cạnh tranh không lành mạnh và bảo vệ bí mật kinh doanh | Dùng trong Nexon v. Ironmace [11] |
| Trung Quốc | Luật chống cạnh tranh không lành mạnh (sửa 2019) | Chủ sở hữu chỉ cần chứng cứ ban đầu về biện pháp bảo mật, rồi nghĩa vụ chứng minh chuyển sang bên bị cáo buộc [26] |

- Biện pháp hợp lý trong thực tế: NDA, điều khoản bảo mật; quyền truy cập theo người, theo nhu cầu (repo, kho tài liệu); đánh dấu
  tài liệu mật; nhắc nghĩa vụ khi nghỉ việc; kiểm soát IT [26]. → QC 4.19.
- Bí mật xét theo từng file: trong Nexon v. Ironmace, sơ thẩm coi một phần tài liệu không còn là bí mật vì hơn 2.600 trong hơn
  11.000 file đã bị công khai [11]. Bí mật kinh doanh là loại "mất là mất vĩnh viễn" [1].
- Án lệ nhân viên mang tài liệu đi: Zynga v. Patmore (2012): bị cáo buộc mang 760 tài liệu (thiết kế hơn 10 game chưa phát hành,
  kế hoạch kiếm tiền, doanh thu, lương) qua Dropbox; toà buộc giám định pháp y máy, tài khoản; công ty mới (Kixeye) bị đưa vào
  làm bị đơn, có giai đoạn phải trả 125.000 USD án phí luật sư [1]. Nexon v. Ironmace: nhân viên cũ mang tài liệu dự án P3 sang
  công ty mới; bản quyền thua, bí mật kinh doanh thắng [11]. → QC 4.18.
- Slide nói bí mật kinh doanh là loại IP duy nhất có thể dẫn tới trách nhiệm hình sự [1]; chưa chính xác: xâm phạm bản quyền, nhãn
  hiệu ở quy mô thương mại cũng có thể bị xử hình sự (Mỹ: 17 U.S.C. §506; Việt Nam: Bộ luật Hình sự 2015 Điều 225, 226 *(chưa đối
  chiếu gốc)*) [15][32].
- Quy tắc gương: được dùng IP, kết quả rút ra từ đọc số liệu, tự nghiên cứu, testing; thứ mình coi là IP của mình cũng là vùng cấm
  không được lấy của đối thủ [1].

---

## 9. Nguồn

Tra ngày 2026-10-23 trừ khi ghi khác.

1. IPLNN, *Video Game IP* (slide, trình bày Jenny Pham), tài liệu nội bộ team gửi, 21 trang.
2. IPLNN, *Tổng quan DMCA* (slide), tài liệu nội bộ team gửi, 31 trang. Án lệ Mỹ ở mục 5.4 lấy theo slide, đối chiếu thêm qua ghi
   chú nghiên cứu; trích dẫn chuẩn: Atari v. North American Philips, 672 F.2d 607 (7th Cir. 1982); Atari v. Amusement World, 547 F.
   Supp. 222 (D. Md. 1981); Data East USA v. Epyx, 862 F.2d 204 (9th Cir. 1988); Incredible Technologies v. Virtual Technologies,
   400 F.3d 1007 (7th Cir. 2005); Tetris Holding v. Xio Interactive, 863 F. Supp. 2d 394 (D.N.J. 2012); Spry Fox v. LOLApps (W.D.
   Wash. 2012). Phép thử trừu tượng hoá – lọc – so sánh: Computer Associates Int'l v. Altai, 982 F.2d 693 (2d Cir. 1992).
3. Apple, App Review Guidelines: https://developer.apple.com/app-store/review/guidelines/ ; form tranh chấp IP:
   https://www.apple.com/legal/intellectual-property/dispute-forms/app-store/
4. Google Play Policy Center: https://support.google.com/googleplay/android-developer/topic/9858052 ; khiếu nại bản quyền:
   https://support.google.com/legal/troubleshooter/1114905 ; chính sách nội dung AI tạo:
   https://support.google.com/googleplay/android-developer/answer/14094294
5. Ghi chú nghiên cứu đợt 1 (2026-10-23), nguồn thứ cấp cho: Envato Elements, Freepik / Flaticon, Shutterstock (trang license của
   từng hãng), nhạc sync / master, Content ID, khai báo AI của Steam (Valve, "AI Content on Steam", 1/2024) *(chưa đối chiếu gốc)*.
6. Unity, Asset Store Terms of Service and EULA: https://unity.com/legal/as-terms
7. Microsoft, OpenType spec, OS/2 `fsType`: https://learn.microsoft.com/en-us/typography/opentype/spec/os2#fstype
8. SIL Open Font License 1.1 và FAQ: https://openfontlicense.org
9. U.S. Copyright Office, *Copyright and Artificial Intelligence, Part 2: Copyrightability* (29/1/2025):
   https://www.copyright.gov/newsnet/2025/1060.html ; Thaler v. Perlmutter, Toà tối cao từ chối thụ lý 2/3/2026:
   https://www.scotusblog.com/cases/case-files/thaler-v-perlmutter/
10. Điều khoản của từng nhà cung cấp (bảng 4.2–4.4): epidemicsound.com/pricing; artlist.io/help-center; musicbed.com/license-terms;
    premiumbeat.com/license-agreement; audiojungle.net/licenses/music; help.elements.envato.com; kenney.nl/support;
    help.sketchfab.com; turbosquid.com/help; freesound.org/help/faq; pixabay.com/service/license-summary;
    ai.google.dev/gemini-api/terms; cloud.google.com/terms/generative-ai-indemnified-services; docs.byteplus.com (ModelArk);
    openai.com/policies/terms-of-use; docs.midjourney.com/docs/terms-of-service; stability.ai/license;
    helpx.adobe.com/legal/product-descriptions/adobe-firefly.html; elevenlabs.io/docs/help-center/legal;
    odininspector.com/pricing; esotericsoftware.com/spine-runtimes-license; live2d.com/en/sdk/license; fmod.com/licensing;
    audiokinetic.com/en/wwise/pricing; photonengine.com/pun/pricing; unity.com/pricing-updates. Dòng ghi *(chưa đối chiếu gốc)* là
    trang chặn tải hoặc chỉ có nguồn thứ cấp.
11. Nexon v. Ironmace: Inven Global,
    https://www.invenglobal.com/articles/21463/nexon-wins-partial-supreme-court-victory-against-ironmace-in-trade-secret-misappropriation-case ;
    Korea Times, https://www.koreatimes.co.kr/business/tech-science/20250309/analysis-nexon-gains-legal-ground-in-dispute-over-dark-and-darker
12. Sony v. Tencent: VGC, https://www.videogameschronicle.com/news/tencent-horizon-clone-pulled-from-stores-as-sony-settles-lawsuit ;
    PocketGamer.biz, https://www.pocketgamer.biz/sony-and-tencent-reach-confidential-settlement-over-light-of-motiram . Krafton v.
    NetEase: ghi chú nghiên cứu đợt 1 *(chưa đối chiếu gốc)*.
13. King v. Avocado: Lee & Ko (luật sư của King) trên Legal500, https://legal500.com/developments/?p=20912
14. Công cụ tra nhãn hiệu: USPTO TSDR API, https://developer.uspto.gov/api-catalog/tsdr-data-api ; điều khoản WIPO Global Brand
    Database, https://www.wipo.int/reference/en/branddb/terms_and_conditions.html
15. Luật bản quyền Mỹ, 17 U.S.C. §§102, 411, 412, 506, 512: https://www.law.cornell.edu/uscode/text/17 ; về §411(a) / §412 với tác
    phẩm nước ngoài: https://www.bipc.com/registration-of-foreign-works-is-required-to-collect-statutory-damages ; lệ phí đăng ký
    2026 (nguồn thứ cấp).
16. Việt Nam gia nhập Công ước Berne (26/10/2004): https://vietnamembassy-usa.org/node/3278 ; WCT (17/2/2022):
    https://www.tilleke.com/insights/vietnam-joins-wipo-copyright-treaty/6/
17. Anh, CDPA s.9(3) và báo cáo 3/2026: https://www.hsfkramer.com/notes/ip/2026-03/uk-government-report-on-copyright-and-ai-concludes-more-evidence-is-needed-although-s9-3-cdpa-could-go
18. EU: chuẩn độc đáo (Infopaq C-5/08, Cofemel C-683/17); AI Act Điều 50: https://gdprlocal.com/eu-ai-act-article-50/ ;
    https://www.mondaq.com/eu-ai-act-guidance-expands-ai-disclosure-rules-for-advertisers-and-pr-teams/1825496
19. Nhật, "General Understanding on AI and Copyright" (3/2024): https://apaaonline.org/article/general-understanding-on-ai-and-copyright-in-japan/
20. Hàn, hướng dẫn đăng ký tác phẩm có AI: https://www.mondaq.com/copyright/1660404/south-korean-copyright-office-issues-ai-guidance
21. Trung Quốc, Li v. Liu (27/11/2023): https://www.chinajusticeobserver.com/a/landmark-ruling-china's-court-upholds-copyright-for-ai-generated-images
22. Nộp đơn trước / sử dụng trước: KIPO, https://www.kipo.go.kr/en/HtmlApp?c=30103 ; tổng hợp:
    https://www.tramatm.com/en/trademark-questions-and-answers/trademark-registration-process/which-countries-are-first-to-use
23. Chiếm tên ở Trung Quốc: https://asiaiplaw.com/sector/trademarks/the-fight-against-trademark-squatting-in-china ;
    https://www.iam-media.com/article/trademark-squatters-get-smarter-in-china-look-the-latest-tactics
24. USPTO, luật sư Mỹ bắt buộc với người nộp đơn nước ngoài (3/8/2019):
    https://www.uspto.gov/subscription-center/2019/new-rule-requires-foreign-domiciled-trademark-applicants-and-registrants
25. Hệ thống Madrid: https://www.wipo.int/treaties/en/notifications/madridp-gp/treaty_madridp_gp_165.html ;
    https://www.wipo.int/en/web/madrid-system/journey-of-an-international-trademark-registration ; hướng dẫn phân loại:
    https://www.wipo.int/documents/d/madrid-system/docs-en-madrid-system-classification-examination-guidelines.pdf
26. Bí mật kinh doanh: DTSA, https://ipwatchdog.com/2016/07/18/available-remedies-dtsa/id=70899/ ; EU 2016/943 Điều 2,
    https://www.legislation.gov.uk/eudr/2016/943/article/2 ; Nhật, https://www.nishimura.com/sites/default/files/images/79357.pdf ;
    Trung Quốc, https://law.asia/trade-secret-protection-under-amended-competition-law/
27. NetEase v. Huaduo: https://asiaiplaw.com/article/china-court-rules-on-copyright-involving-online-games-and-live-streaming
28. GREE v. DeNA: https://hd.gree.net/jp/en/news/press/2012/0223-01.html
29. Nova Productions v. Mazooma Games [2007] EWCA Civ 219; tóm tắt:
    https://openaccess.city.ac.uk/26210/1/Goold%20Curious%20Case_WPS%202021%2003.pdf
30. AM General v. Activision (S.D.N.Y. 2020): https://www.thedrive.com/tech/32872/you-have-a-first-amendment-right-to-humvees-in-call-of-duty-games-judge-rules ;
    Jack Daniel's v. VIP Products (2023): https://conventuslaw.com/report/us-supreme-court-sharply-limits-applicability-of-rogers-v-grimaldi-test-for-trademark-infringement/
31. Nền tảng và quảng cáo: Google Play IP policy, https://support.google.com/googleplay/android-developer/answer/9888072 ;
    Impersonation, https://support.google.com/googleplay/android-developer/answer/9888374 ; strike,
    https://support.google.com/googleplay/android-developer/answer/9899234 ; form nhãn hiệu,
    https://support.google.com/legal/contact/lr_trademark?product=googleplay ; Steam, https://store.steampowered.com/legal/ ,
    https://steamcommunity.com/dmca/create/ ; DSA (Quy định (EU) 2022/2065) Điều 16, 17, 30, 31 (văn bản ở EUR-Lex); Apple DSA: App Store Connect Help, mục quản lý thông tin tuân thủ Digital Services Act (developer.apple.com) *(chưa đối chiếu
    gốc)*; Meta, https://transparency.meta.com/policies/ad-standards/intellectual-property-infringement/third-party-infringement ;
    TikTok, https://ads.tiktok.com/help/article/tiktok-ads-policy-intellectual-property-infringement ,
    https://ads.tiktok.com/business/en-US/blog/audio-library-royalty-free-music ; Google Ads trademark,
    https://support.google.com/adspolicy/answer/6118 ; YouTube Content ID, https://support.google.com/youtube/answer/6013276 ;
    Unity, https://unity.com/legal/content-policy ; Mintegral, https://helpcenter.mintegral.com/en/docs/creative-policies ; Google
    Publisher Policies, https://support.google.com/adsense/answer/10502938 ; ASA,
    https://www.asa.org.uk/news/don-t-hate-the-player-hate-the-game-play-misleading-gameplay-in-videogame-ads.html ,
    https://www.asa.org.uk/rulings/top-games-inc-a23-1213924-top-games-inc.html
32. Việt Nam: Luật Sở hữu trí tuệ 2005, sửa đổi 2009, 2019, 2022 (Luật 07/2022/QH15) (Điều 27, 39, 84, 95); Nghị định 17/2023/NĐ-CP;
    Nghị định 131/2013/NĐ-CP (sửa đổi); Bộ luật Dân sự 2015 Điều 32; Bộ luật Hình sự 2015 Điều 225, 226. Tra văn bản hợp nhất ở
    vbpl.vn hoặc thuvienphapluat.vn *(nội dung tóm tắt từ nguồn thứ cấp, chưa đối chiếu gốc)*.
33. Copyright Claims Board: https://www.fr.com/insights/thought-leadership/blogs/need-to-knows-of-the-new-copyright-claims-board-for-small-value-copyright-claims/
34. §512(g)(3)(D), "may be found": Bountiful Baby v. Amazon (10th Cir. 2024), https://ca10.washburnlaw.edu/cases/2024/10/23-4126.pdf
