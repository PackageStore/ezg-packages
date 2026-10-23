// Tự kiểm skill game-ip trên project giả (make_fixture.mjs): survey phải tìm ra đúng các ca đã cài, không báo ca không được báo;
// init phải sinh sổ draft phủ hết asset vào build, không ghi vào project khi có --out, không ghi đè sổ có sẵn.
// Chạy: node gameip.mjs selftest [--keep]
import fs from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { makeFixture } from "./make_fixture.mjs";
import { survey } from "../lib/survey.mjs";
import { buildScope, indexProject } from "../lib/unity.mjs";
import { candidatesFromSurvey, coverage, loadLedger, mergeLedger } from "../lib/ledger.mjs";
import { fontVerdict, surveyReport } from "../lib/report.mjs";
import { runGates } from "../lib/gates.mjs";

const CLI = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "gameip.mjs");

export async function runSelftest(dir) {
  const results = [];
  const check = (name, cond, detail = "") => results.push({ name, ok: !!cond, detail });
  const { root, cacheDir } = makeFixture(path.join(dir, "proj"), path.join(dir, "cache"));
  const s = await survey({ root, assetStoreCaches: [cacheDir], useDefaultCache: false });
  const why = Object.fromEntries(s.inBuildList.map((x) => [x.path, x.why]));
  const inB = (p) => p in why;

  // 1. Vào build
  check("scene đang bật vào build, scene tắt thì không", inB("Assets/Scenes/Main.unity") && !inB("Assets/Scenes/Old.unity"));
  check("tham chiếu bắc cầu scene → prefab → ảnh", inB("Assets/Game/Art/hero.png") && why["Assets/Game/Art/hero.png"] === "ref:Assets/Game/UI/Shop.prefab", why["Assets/Game/Art/hero.png"]);
  check("asset chỉ scene tắt dùng: không vào build", !inB("Assets/Game/Unused/old_only_in_disabled_scene.png"));
  check("danh sách build thật (Library/GameIP/build_assets.json) thêm asset ước lượng bỏ sót", why["Assets/Game/Unused/old.png"] === "buildreport" && s.counts.buildReport && s.counts.buildReport.added === 1 && s.counts.buildReport.target === "Android", JSON.stringify(s.counts.buildReport));
  check("Resources vào build", why["Assets/Game/Audio/Resources/bgm.mp3"] === "resources");
  check("StreamingAssets vào build", why["Assets/StreamingAssets/config.json"] === "streaming");
  check("Addressables + tham chiếu của nó", why["Assets/Game/Addr/level1.asset"] === "addressables" && inB("Assets/Game/Addr/tile.png"));
  check("AssetBundle name vào build", why["Assets/Game/Bundled/boss.png"] === "assetbundle");
  check("code runtime vào build; folder Editor, asmdef chỉ Editor thì không", why["Assets/Game/Scripts/Player.cs"] === "code" && !inB("Assets/Game/Scripts/Editor/Tool.cs") && !inB("Assets/Game/Scripts/EditorOnly/Helper.cs"));
  check("plugin vào build", why["Assets/Plugins/Demigiant/DOTween/DOTween.dll"] === "plugin");
  check("font nguồn của font asset TMP vào build (m_SourceFontFileGUID)", inB("Assets/Game/Fonts/GameFont.ttf"), why["Assets/Game/Fonts/GameFont.ttf"]);
  check("code của package vào build", inB("Packages/com.acme.tools/Runtime/Acme.cs"));

  // 2. Nguồn theo GUID
  const as = s.assetStore.find((a) => a.name === "Explosive Pack");
  check("gói Asset Store khớp GUID 3/4, 1 file đã dời", as && as.matched === 3 && as.packageEntries === 4 && as.moved === 1, JSON.stringify(as && { m: as.matched, e: as.packageEntries, mv: as.moved }));

  // 3. Folder bên thứ ba, package
  const vf = Object.fromEntries(s.vendorFolders.map((v) => [v.path, v]));
  check("nhận DOTween theo tên nhà cung cấp", vf["Assets/Plugins/Demigiant"] && vf["Assets/Plugins/Demigiant"].known && vf["Assets/Plugins/Demigiant"].known.title === "DOTween", Object.keys(vf).join(", "));
  check("nhận pack qua readme Asset Store, lấy URL", vf["Assets/ThirdParty/CoolPack"] && /cool-pack-12345/.test(vf["Assets/ThirdParty/CoolPack"].storeUrl || ""));
  check("nhận license GPL của thư viện", vf["Assets/ThirdParty/GplLib"] && vf["Assets/ThirdParty/GplLib"].licenseFiles.some((l) => l.license === "GPL-3.0"));
  check("folder gốc kiểu ThirdParty / Plugins không tự thành nhà cung cấp", !vf["Assets/ThirdParty"] && !vf["Assets/Plugins"]);
  check("Plugins/iOS, Plugins/tvOS là folder nền tảng: nhà cung cấp là Firebase bên trong", !vf["Assets/Plugins/iOS"] && !vf["Assets/Plugins/tvOS"] && vf["Assets/Plugins/iOS/Firebase"] && vf["Assets/Plugins/tvOS/Firebase"], Object.keys(vf).join(", "));
  check("folder chung (ThirdParty/UI) vẫn là bên thứ ba, đánh dấu chưa rõ pack", vf["Assets/ThirdParty/UI"] && vf["Assets/ThirdParty/UI"].generic);
  check("pack đặt thẳng dưới Assets: tên 'Pack' + readme pdf, folder _Demo; mẫu ở Assets/Samples lấy license package", vf["Assets/Jurassic Pack"] && vf["Assets/TerrainPainter"] && vf["Assets/Samples/Acme Tools"] && vf["Assets/Samples/Acme Tools"].sampleOf && vf["Assets/Samples/Acme Tools"].sampleOf.license === "MIT", Object.keys(vf).join(", "));
  check("folder code của team (FeatureGateKit), folder Demo trơn: không thành bên thứ ba", !vf["Assets/FeatureGateKit"] && !vf["Assets/Game"] && !vf["Assets/Samples"]);
  check("tài liệu của team chỉ nhắc chữ 'Unity Asset Store': không thành folder bên thứ ba",!Object.keys(vf).some((d) => d.startsWith("Assets/Game")), Object.keys(vf).join(", "));
  {
    const own = Object.fromEntries(s.packages.map((p) => [p.name, p.own]));
    check("gói công ty: 3 gói cùng tác giả từ registry mang tên công ty; gói mirror cùng registry thì không", own["com.stt.a"] && own["com.stt.c"] && !own["com.mirror.lib"] && !own["com.unity.fakedebug"], JSON.stringify(own));
  }
  const pk = Object.fromEntries(s.packages.map((p) => [p.name, p]));
  check("package nhúng: license MIT", pk["com.acme.tools"] && pk["com.acme.tools"].license === "MIT", JSON.stringify(pk["com.acme.tools"]));
  check("package com.unity.*: Unity Companion License", pk["com.unity.ugui"] && pk["com.unity.ugui"].license === "unity-companion");

  // 4. Font
  const ft = Object.fromEntries(s.fonts.map((f) => [f.path, f]));
  const ofl = ft["Assets/Game/Fonts/GameFont.ttf"];
  const locked = ft["Assets/Game/Fonts/Locked.ttf"];
  check("font OFL: đọc license, họ, ✓", ofl && ofl.license === "OFL-1.1" && ofl.family === "Foo Sans" && fontVerdict(ofl)[0] === "✓", JSON.stringify(ofl));
  check("font fsType Restricted: ✗", locked && locked.embedding === "restricted" && fontVerdict(locked)[0] === "✗", JSON.stringify(locked));

  // 5. Âm thanh, ảnh
  const bgm = s.audio.tagged.find((a) => a.path.endsWith("bgm.mp3"));
  check("mp3 có tag bản quyền + URL nguồn khác", bgm && bgm.sourceLike && !bgm.own && /Famous Records/.test(bgm.tags.copyright) && /freesound/.test(bgm.tags.url), JSON.stringify(bgm));
  const click = s.audio.tagged.find((a) => a.path.endsWith("click.wav"));
  check("wav có tag của chính công ty: không báo", click && click.own, JSON.stringify(click));
  const foreign = Object.fromEntries(s.images.foreign.map((i) => [i.path, i]));
  check("PNG XMP dc:rights bên khác", foreign["Assets/Game/Art/hero.png"] && /Someone Else/.test(foreign["Assets/Game/Art/hero.png"].rights));
  check("PNG ghi bản quyền của công ty: không báo", !foreign["Assets/Game/Art/coin.png"]);
  check("JPEG EXIF Copyright bên khác", foreign["Assets/Game/Art/photo.jpg"] && /Getty/.test(foreign["Assets/Game/Art/photo.jpg"].rights) && !foreign["Assets/Game/Art/photo.jpg"].inBuild);
  check("PSD IPTC bản quyền bên khác, vào build qua Resources", foreign["Assets/Game/Resources/key.psd"] && foreign["Assets/Game/Resources/key.psd"].inBuild);
  check("ảnh AI qua IPTC DigitalSourceType", s.images.ai.some((i) => i.path === "Assets/Game/Art/hero.png" && /trainedAlgorithmicMedia/.test(i.signal)));

  // 6. Tên đáng ngờ, code dịch ngược, file cài đặt, tên game
  check("tác giả PNG là phần mềm (PDF Tools AG): không tính là bên giữ quyền", !foreign["Assets/ThirdParty/CoolPack/Sprites/noise.png"]);
  check("image.png (tên Figma / clipboard) không đáng ngờ; image (2).png thì có", !s.suspicious.some((x) => x.path === "Assets/Game/Art/image.png") && s.suspicious.some((x) => x.path === "Assets/Game/Art/image (2).png"));
  check("ảnh chụp màn hình trong build",s.suspicious.some((x) => /Screenshot-2021/.test(x.path) && x.inBuild));
  check("code dịch ngược", s.decompiled.some((d) => d.path.endsWith("Dump.cs") && /Token: 0x/.test(d.marker)));
  const apk = Object.fromEntries(s.installers.map((i) => [i.path, i]));
  check("file .apk lạ trong Assets; build của mình nhận ra theo tên", apk["Assets/Tools/competitor_v1.apk"] && !apk["Assets/Tools/competitor_v1.apk"].mayBeOwnBuild && apk["Builds/FixtureGame.apk"] && apk["Builds/FixtureGame.apk"].mayBeOwnBuild, JSON.stringify(s.installers));
  check("tên game khác: CandyCrushShop khớp Candy Crush", s.gameNames.some((g) => g.name === "Candy Crush" && g.segments.some((x) => x.segment === "CandyCrushShop")));
  check("SupersonicBoom không khớp Sonic", !s.gameNames.some((g) => g.name === "Sonic"), JSON.stringify(s.gameNames.map((g) => g.name)));

  // 7. Ghi công, màn Credits
  check("file yêu cầu ghi công", s.creditDemands.some((d) => d.path === "Assets/Game/Maps/Island/CREDIT.txt" && /mapmaker42/.test(d.snippet)));
  check("chưa có màn Credits", !s.credits.found);
  const s2 = await survey({
    root: makeFixture(path.join(dir, "proj2"), path.join(dir, "cache2"), (put) => put("Assets/Game/Resources/CreditsPanel.prefab", "%YAML 1.1\n  m_text: Open Source Licenses\n")).root,
    assetStoreCaches: [],
    useDefaultCache: false,
  });
  check("có prefab chữ 'Open Source Licenses' vào build → thấy màn Credits", s2.credits.found && s2.credits.texts.some((t) => /Open Source Licenses/.test(t.text)));

  // 7b. check khi chưa có sổ: vẫn bắt GPL và nghĩa vụ ghi công từ khảo sát
  const g0 = runGates(s);
  const G0 = (id) => g0.gates.find((x) => x.gate === id);
  check("chưa có sổ: IP-1 ✗, IP-5 bắt GPL từ file license, IP-6 bắt nghĩa vụ ghi công", G0("IP-1").mark === "✗" && G0("IP-5").lines.some((l) => l.mark === "✗" && /GplLib/.test(l.text)) && G0("IP-6").mark === "✗", JSON.stringify([G0("IP-5").lines, G0("IP-6").lines]));

  // 8. init: mục draft phủ hết asset vào build
  const cands = candidatesFromSurvey(s);
  const byId = Object.fromEntries(cands.map((c) => [c.id, c]));
  check("mục pack Asset Store theo folder chung", byId["as-explosive-explosive-pack"] && byId["as-explosive-explosive-pack"].paths[0] === "Assets/ExplosivePack/**", Object.keys(byId).join(", "));
  check("mục CoolPack: asset-store, có URL", byId["vendor-coolpack"] && byId["vendor-coolpack"].kind === "asset-store" && /12345/.test(byId["vendor-coolpack"].source));
  check("mục GplLib: oss, GPL-3.0", byId["vendor-gpllib"] && byId["vendor-gpllib"].kind === "oss" && byId["vendor-gpllib"].license === "GPL-3.0");
  check("mục font OFL có câu ghi công", byId["font-foo-sans"] && /Foo Project Authors/.test(byId["font-foo-sans"].attribution));
  check("mục font Restricted: commercial-font", byId["font-big-casual"] && byId["font-big-casual"].license === "commercial-font");
  check("mục ghi công mapmaker42", cands.some((c) => /mapmaker42/.test(c.attribution) && c.paths.includes("Assets/Game/Maps/Island/**")));
  check("mọi mục là draft", cands.every((c) => c.status === "draft"));
  check("gói công ty gộp một mục inhouse; gói mirror vẫn là package chưa rõ license", byId["inhouse-packages-stt"] && byId["inhouse-packages-stt"].kind === "inhouse" && byId["inhouse-packages-stt"].paths.length === 3 && byId["pkg-com-mirror-lib"] && byId["pkg-com-mirror-lib"].license === "unknown" && !byId["pkg-com-stt-a"], Object.keys(byId).join(", "));
  check("package không ghi license, tên khớp nhà cung cấp quen (UniTask): lấy MIT", byId["pkg-com-cysharp-unitask"] && byId["pkg-com-cysharp-unitask"].license === "MIT" && byId["pkg-com-cysharp-unitask"].kind === "oss");
  check("Jurassic Pack: bên thứ ba chưa rõ license, không bị đoán là tự làm", byId["vendor-jurassic-pack"] && byId["vendor-jurassic-pack"].license === "unknown" && !cands.some((c) => c.kind === "inhouse" && c.paths.some((p) => p.startsWith("Assets/Jurassic Pack"))), Object.keys(byId).join(", "));
  check("font trong package Unity: không thành mục font riêng (mục unity-packages phủ)", !cands.some((c) => c.kind === "font" && c.paths.some((p) => p.startsWith("Packages/com.unity."))));
  check("folder chung ThirdParty/UI: id, tên ghi kèm folder cha", byId["vendor-thirdparty-ui"] && /ThirdParty\/UI/.test(byId["vendor-thirdparty-ui"].title), Object.keys(byId).filter((k) => k.startsWith("vendor")).join(", "));
  const index = indexProject(root);
  const inBuild = buildScope(index);
  const fresh = mergeLedger(null, cands, s.inBuildList, index.byPath).ledger;
  const cov = coverage(fresh, index, inBuild);
  check("sổ draft phủ 100% asset vào build", cov.uncovered === 0 && cov.draft === cov.total, JSON.stringify(cov.uncoveredGroups.slice(0, 3)));
  check("ảnh của pack CoolPack thuộc mục pack, không thuộc mục tự làm (mục cụ thể nhất thắng)", cov.byEntry["vendor-coolpack"] >= 1);

  // 9. CLI init với --out: không ghi vào project; lần hai không ghi đè; nguồn mới → GameIPLedger.new.json
  const out = path.join(dir, "out");
  const run = () => spawnSync(process.execPath, [CLI, "init", "--project", root, "--out", out, "--asset-store-cache", cacheDir, "--no-default-cache", "--quiet"], { encoding: "utf8" });
  const r1 = run();
  check("init chạy được", r1.status === 0, r1.stderr.slice(-500));
  const ledgerPath = path.join(out, "ProjectSettings", "GameIPLedger.json");
  check("init --out ghi sổ, file luật, ghi chú, báo cáo vào folder out", ["ProjectSettings/GameIPLedger.json", "ProjectSettings/GameIPProject.json", "ProjectSettings/GameIPProject.md", "Logs/GameIPSurvey.md"].every((f) => fs.existsSync(path.join(out, f))));
  check("init --out không ghi gì vào project", !fs.existsSync(path.join(root, "ProjectSettings", "GameIPLedger.json")) && !fs.existsSync(path.join(root, "Logs")));
  const l1 = loadLedger(out);
  l1.entries[0].status = "confirmed";
  l1.entries[0].proof = "đơn #1";
  fs.writeFileSync(ledgerPath, JSON.stringify(l1, null, 2));
  const before = fs.readFileSync(ledgerPath, "utf8");
  const r2 = run();
  check("init lần hai, không có nguồn mới: không ghi .new", r2.status === 0 && !fs.existsSync(ledgerPath.replace(/\.json$/, ".new.json")), r2.stdout.slice(-300));
  fs.mkdirSync(path.join(root, "Assets", "ThirdParty", "NewKit"), { recursive: true });
  fs.writeFileSync(path.join(root, "Assets", "ThirdParty", "NewKit", "LICENSE.txt"), "Creative Commons Attribution-NonCommercial 4.0");
  fs.writeFileSync(path.join(root, "Assets", "ThirdParty", "NewKit", "icon.png"), Buffer.from([0x89, 0x50, 0x4e, 0x47]));
  fs.writeFileSync(path.join(root, "Assets", "ThirdParty", "NewKit", "icon.png.meta"), "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n");
  fs.mkdirSync(path.join(root, "Assets", "Game", "Resources"), { recursive: true });
  fs.writeFileSync(path.join(root, "Assets", "Game", "Resources", "uses_newkit.asset"), "%YAML 1.1\n  icon: {fileID: 1, guid: 0123456789abcdef0123456789abcdef, type: 3}\n");
  const r3 = run();
  const newFile = ledgerPath.replace(/\.json$/, ".new.json");
  const l3 = loadLedger(null, newFile);
  check("nguồn mới → GameIPLedger.new.json có mục NewKit CC-BY-NC, sổ gốc không đổi", r3.status === 0 && l3 && l3.entries.some((e) => e.id === "vendor-newkit" && e.license === "CC-BY-NC-4.0") && fs.readFileSync(ledgerPath, "utf8") === before, r3.stdout.slice(-300));
  check("mục đã xác nhận giữ nguyên trong bản gộp", l3 && l3.entries[0].status === "confirmed" && l3.entries[0].proof === "đơn #1");

  // 10. check, notices, nợ
  const settings = path.join(out, "ProjectSettings");
  const cli = (...a) => spawnSync(process.execPath, [CLI, ...a, "--project", root, "--settings", settings, "--out", path.join(dir, "out-check"), "--asset-store-cache", cacheDir, "--no-default-cache", "--quiet"], { encoding: "utf8" });
  const c1 = cli("check");
  const res1 = JSON.parse(fs.readFileSync(path.join(dir, "out-check", "Logs", "GameIPCheck.json"), "utf8"));
  const G = (r, id) => r.gates.find((g) => g.gate === id);
  const has = (r, id, mark, re) => G(r, id).lines.some((l) => l.mark === mark && re.test(l.text));
  check("check có ✗ → mã thoát 1", c1.status === 1, c1.stderr.slice(-300));
  const ip1Fail = G(res1, "IP-1").lines.filter((l) => l.mark === "✗");
  check("IP-1: mục draft là cảnh báo; chỉ NewKit (thêm sau init, chưa gộp) thiếu nguồn", G(res1, "IP-1").lines.some((l) => l.mark === "!") && ip1Fail.length === 1 && /ThirdParty\/NewKit/.test(ip1Fail[0].text), JSON.stringify(ip1Fail));
  check("IP-3: font Restricted ✗, font OFL không báo", has(res1, "IP-3", "✗", /Locked\.ttf/) && !has(res1, "IP-3", "✗", /GameFont/));
  check("IP-4: mp3 tag nguồn khác ✗", has(res1, "IP-4", "✗", /bgm\.mp3/));
  check("IP-5: thư viện GPL ✗", has(res1, "IP-5", "✗", /gpllib/i));
  check("IP-6: đòi ghi công mà chưa có màn Credits ✗", has(res1, "IP-6", "✗", /chưa có màn Credits/));
  check("IP-7: ảnh AI chưa có hồ sơ !", has(res1, "IP-7", "!", /hero\.png/));
  check("IP-8: ảnh chụp màn hình, PSD bản quyền bên khác ✗", has(res1, "IP-8", "✗", /Screenshot-2021/) && has(res1, "IP-8", "✗", /key\.psd/));
  check("IP-8: metadata bên khác trong pack có mục sổ draft là !, không ✗", has(res1, "IP-8", "!", /gem\.png.*vendor-coolpack/) && !has(res1, "IP-8", "✗", /gem\.png/), JSON.stringify(G(res1, "IP-8").lines));
  check("IP-13: Candy Crush !", has(res1, "IP-13", "!", /Candy Crush/));
  check("IP-18: code dịch ngược, apk lạ ✗; build của mình không báo", has(res1, "IP-18", "✗", /Dump\.cs/) && has(res1, "IP-18", "✗", /competitor_v1\.apk/) && !has(res1, "IP-18", "✗", /FixtureGame\.apk/));
  check("gate người kiểm hiện dòng –", G(res1, "IP-10").mark === "–" && G(res1, "IP-17").mark === "–");
  check("dòng GPL ghi rõ không được ghi nợ", has(res1, "IP-5", "✗", /không được ghi nợ/));
  const n0 = spawnSync(process.execPath, [CLI, "notices", "--project", s2.project.root, "--quiet", "--no-default-cache"], { encoding: "utf8" });
  check("notices khi chưa có sổ: dừng, không ghi gì vào project", n0.status !== 0 && !fs.existsSync(path.join(s2.project.root, "Logs")), n0.stdout + n0.stderr);
  const noticesFile = path.join(dir, "notices", "ThirdPartyNotices.txt");
  const n1 = cli("notices", "--to", noticesFile);
  const nt = fs.existsSync(noticesFile) ? fs.readFileSync(noticesFile, "utf8") : "";
  check("notices ghi font OFL, mapmaker42, package MIT kèm nội dung license", n1.status === 0 && /Foo Sans/.test(nt) && /mapmaker42/.test(nt) && /Permission is hereby granted/.test(nt), n1.stdout + n1.stderr);
  check("notices không ghi phần tự làm", !/Tự làm\?/.test(nt));
  const pf = JSON.parse(fs.readFileSync(path.join(settings, "GameIPProject.json"), "utf8"));
  pf.credits.noticesFile = noticesFile;
  fs.writeFileSync(path.join(settings, "GameIPProject.json"), JSON.stringify(pf));
  fs.writeFileSync(path.join(settings, "GameIPDebt.json"), JSON.stringify([
    { gate: "IP-18", path: "Assets/Game/Scripts/Hacked/Dump.cs", reason: "đang gỡ", approvedBy: "Lead", until: "2999-01-01" },
    { gate: "IP-5", entry: "vendor-gpllib", reason: "thử nợ không được phép", approvedBy: "Lead", until: "2999-01-01" },
    { gate: "IP-3", path: "Assets/Game/Fonts/Locked.ttf", reason: "hết hạn", approvedBy: "Lead", until: "2000-01-01" },
  ]));
  cli("check");
  const res2 = JSON.parse(fs.readFileSync(path.join(dir, "out-check", "Logs", "GameIPCheck.json"), "utf8"));
  check("IP-6 ✓ khi file notice có đủ nguồn", G(res2, "IP-6").mark === "✓", JSON.stringify(G(res2, "IP-6").lines));
  check("nợ còn hạn hạ ✗ thành ·", !has(res2, "IP-18", "✗", /Dump\.cs/) && has(res2, "IP-18", "·", /Dump\.cs.*nợ/));
  check("không có nợ cho copyleft mạnh", has(res2, "IP-5", "✗", /gpllib/i));
  check("nợ hết hạn không còn tác dụng", has(res2, "IP-3", "✗", /Locked\.ttf/));

  // 11. IP-15: thương hiệu thật trong nội dung
  const br = Object.fromEntries((s.brands || []).map((b) => [b.brand, b]));
  check("IP-15: Lamborghini trong CSV localize vào build", br.Lamborghini && br.Lamborghini.hits.some((h) => h.inBuild && /items\.csv/.test(h.path)), JSON.stringify(Object.keys(br)));
  check("IP-15: tên nền tảng (Facebook) không báo; file không vào build không tính là vào build", !br.Facebook && br.Glock && !br.Glock.hits.some((h) => h.inBuild));
  check("IP-15 trong check: Lamborghini !, Glock không", has(res1, "IP-15", "!", /Lamborghini/) && !has(res1, "IP-15", "!", /Glock/));

  // 12. compare: án lệ, pHash
  const { matchCases, compareImages, compareCard, casesByGenre } = await import("../lib/compare.mjs");
  check("compare: án lệ cùng thể loại match3 có King v. Avocado", casesByGenre("match-3").some((c) => /King v\. Avocado/.test(c.case)));
  const hint = compareCard({ part: "Icon", ref: "X", date: "2000-01-01", cases: [], closest: { distance: 0, flipped: true, mine: "a.png", theirs: "b.png" } });
  check("thẻ: cặp ảnh ≤ 5 → gợi ý Đỏ trong kết luận", /Gợi ý của máy: cặp ảnh 0 \/ 64 bit \(lật ngang\)/.test(hint));
  const dataDir = path.join(path.dirname(fileURLToPath(import.meta.url)), "data");
  check("compare: Triple Town → Spry Fox v. LOLApps; game lạ → không án lệ", matchCases("Triple Town").some((c) => /Spry Fox/.test(c.case)) && matchCases("Zorbifly Quest").length === 0);
  const cmp = compareImages({ mine: [path.join(dataDir, "a.png"), path.join(dataDir, "b.png")], theirs: [path.join(dataDir, "a.jpg"), path.join(dataDir, "a_flip.png")], threshold: 10 });
  const pairOf = (m, t) => cmp.pairs.find((p) => p.mine.endsWith(m) && p.theirs.endsWith(t));
  check("pHash: PNG ↔ JPEG cùng ảnh khoảng cách ≤ 2", pairOf("a.png", "a.jpg") && pairOf("a.png", "a.jpg").distance <= 2, JSON.stringify(cmp.pairs));
  check("pHash: bắt ảnh lật ngang", pairOf("a.png", "a_flip.png") && pairOf("a.png", "a_flip.png").flipped);
  check("pHash: ảnh khác không thành cặp", !cmp.pairs.some((p) => p.mine.endsWith("b.png")));
  const card = compareCard({ part: "Merge", ref: "Triple Town", date: "2000-01-01", cases: matchCases("Triple Town"), pairsFile: "merge.pairs.md", brief: "GD nói học Triple Town" });
  check("thẻ so sánh điền án lệ, lời brief, file cặp ảnh từ mẫu", /Spry Fox/.test(card) && /GD nói học Triple Town/.test(card) && /merge\.pairs\.md/.test(card) && /## 2\. Trừu tượng hoá và lọc/.test(card));

  // 13. name
  const { assessName } = await import("../lib/name.mjs");
  const nm = (n, o) => assessName(n, { markets: ["VN", "US"], ...(o || {}) });
  const nmCandy = nm("Candy Match Saga");
  check("name: tên toàn từ mô tả là yếu, cảnh báo gần Candy Crush Saga", /^Yếu/.test(nmCandy.strength) && nmCandy.findings.some((f) => f.mark === "!" && /Candy Crush Saga/.test(f.text)));
  check("name: chứa thương hiệu Ferrari → ✗", nm("Ferrari Drift Racer").findings.some((f) => f.mark === "✗" && /Ferrari/.test(f.text)));
  check("name: đọc gần giống (Kandy Krush ~ Candy Crush) → ✗", nm("Kandy Krush").findings.some((f) => f.mark === "✗" && /đọc gần giống/.test(f.text)));
  check("name: tên bịa không gần ai → ✓, có link VN, US, WIPO", (() => { const r = nm("Zorbifly"); return /^Có thể mạnh/.test(r.strength) && r.findings.some((f) => f.mark === "✓") && r.links.map((l) => l.market).join() === "VN,US,WIPO"; })());
  check("name: dài quá 30 ký tự → ✗ (Apple 2.3.7)", nm("Zorbifly the Legendary Quest of Wonders").findings.some((f) => f.mark === "✗" && /30/.test(f.text)));
  check("name: gần đối thủ của project", nm("Pixel Bistro", { others: [{ name: "Pixel Bistro Tycoon", from: "đối thủ" }] }).findings.some((f) => /đối thủ/.test(f.text)));
  check("name: chung từ chủ đề với game khác (Dino Grill ~ Dino Hunter) → !, không ✗", nm("Dino Grill: Cook & Sell").findings.some((f) => f.mark === "!" && /Dino Hunter/.test(f.text)) && !nm("Dino Grill: Cook & Sell").findings.some((f) => f.mark === "✗"));
  check("name: có đủ mọi từ của tên game khác (Royal Gem Kingdom ⊃ Royal Kingdom) → ✗",nm("Royal Gem Kingdom").findings.some((f) => f.mark === "✗" && /Royal Kingdom/.test(f.text)));

  // 14. creative
  const crDir = path.join(root, "Creatives", "ad01");
  const cr1 = spawnSync(process.execPath, [CLI, "creative", crDir, "--project", root, "--out", path.join(dir, "out-cr")], { encoding: "utf8" });
  const crMd = fs.existsSync(path.join(dir, "out-cr", "GameIPCreative.md")) ? fs.readFileSync(path.join(dir, "out-cr", "GameIPCreative.md"), "utf8") : "";
  const crHas = (mark, re) => crMd.split("\n").some((l) => l.startsWith(`- ${mark}`) && re.test(l));
  check("creative: có ✗ → mã thoát 1, không ghi vào folder creative khi có --out", cr1.status === 1 && !fs.existsSync(path.join(crDir, "GameIPCreative.md")), cr1.stderr.slice(-300));
  check("creative: file chưa có mục sổ ✗", crHas("✗", /unlisted\.png: chưa có mục/));
  check("creative: nhạc TikTok chạy cả Meta ✗", crHas("✗", /trend.*chỉ dùng trên.*meta/));
  check("creative: nhạc stock ads unknown ✗, tag nguồn lạ ✗", crHas("✗", /stock.*chưa rõ được dùng cho quảng cáo/) && crHas("✗", /stock_drop\.mp3: tag ghi nguồn khác/));
  check("creative: người thật không hợp đồng ✗", crHas("✗", /actor.*hợp đồng/));
  check("creative: cảnh không có trong game !, ảnh AI chưa khai !", crHas("!", /endcard.*không có trong game/) && crHas("!", /endcard_ai\.png.*AI/));
  check("creative: playable gọi tài nguyên ngoài !; chữ quảng cáo nhắc game khác (Candy Crush) ✗, không lặp thành ! IP-15", crHas("!", /playable\.html: playable gọi tài nguyên ngoài/) && crHas("✗", /playable\.html: chữ quảng cáo nhắc game khác "Candy Crush"/) && !crHas("!", /nhắc "Candy Crush"/));
  check("creative: mục ads unknown + draft gộp một dòng ✗", crMd.split("\n").filter((l) => /`stock`/.test(l)).length === 1);
  const ad02 = path.join(root, "Creatives", "ad02");
  const ci = spawnSync(process.execPath, [CLI, "creative", ad02, "--init"], { encoding: "utf8" });
  const sheet = fs.existsSync(path.join(ad02, "creative.json")) ? JSON.parse(fs.readFileSync(path.join(ad02, "creative.json"), "utf8")) : null;
  check("creative --init: sổ nháp, mục theo loại file, ads unknown", ci.status === 0 && sheet && sheet.entries.some((e) => e.kind === "footage") && sheet.entries.some((e) => e.kind === "music") && sheet.entries.every((e) => e.scope.ads === "unknown" && e.status === "draft"));
  const before2 = fs.readFileSync(path.join(ad02, "creative.json"), "utf8");
  spawnSync(process.execPath, [CLI, "creative", ad02, "--init"], { encoding: "utf8" });
  check("creative --init lần hai không ghi đè", fs.readFileSync(path.join(ad02, "creative.json"), "utf8") === before2);

  // 15. Báo cáo
  const md = surveyReport(s);
  check("báo cáo có đủ 13 mục", (md.match(/^## \d+\./gm) || []).length === 13);

  let failed = 0;
  for (const r of results) {
    if (!r.ok) failed++;
    console.log(`${r.ok ? "✓" : "✗"} ${r.name}${r.ok || !r.detail ? "" : `\n    ${r.detail}`}`);
  }
  console.log(`\n${results.length - failed}/${results.length} đạt.`);
  return failed === 0;
}
