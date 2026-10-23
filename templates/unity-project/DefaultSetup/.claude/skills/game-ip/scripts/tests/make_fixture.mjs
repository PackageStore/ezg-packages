// Dựng project Unity giả cho selftest của game-ip: đủ các đường vào build (scene, Resources, Addressables, AssetBundle,
// StreamingAssets, code, plugin, tham chiếu bắc cầu, font nguồn của font asset TMP) và các ca gate phải bắt / không được bắt.
// File nhị phân (font, mp3, wav, png, jpeg, psd, unitypackage) dựng tối thiểu, đủ cho bộ đọc của skill.
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import zlib from "node:zlib";
import { fileURLToPath } from "node:url";

export const guidOf = (p) => crypto.createHash("sha1").update(p).digest("hex").slice(0, 32);

// ---------- dựng file nhị phân ----------
const CRC_TABLE = Array.from({ length: 256 }, (_, n) => {
  let c = n;
  for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  return c >>> 0;
});
function crc32(buf) {
  let c = 0xffffffff;
  for (const b of buf) c = CRC_TABLE[(c ^ b) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}
function pngChunk(type, data) {
  const len = Buffer.alloc(4);
  len.writeUInt32BE(data.length);
  const td = Buffer.concat([Buffer.from(type, "latin1"), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(td));
  return Buffer.concat([len, td, crc]);
}
/** PNG 1×1; text: [[keyword, text, "tEXt" | "iTXt"]]. */
export function png(text = []) {
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(1, 0);
  ihdr.writeUInt32BE(1, 4);
  ihdr[8] = 8;
  ihdr[9] = 6;
  const chunks = [pngChunk("IHDR", ihdr)];
  for (const [k, v, type = "tEXt"] of text) {
    if (type === "iTXt") chunks.push(pngChunk("iTXt", Buffer.concat([Buffer.from(`${k}\0\0\0\0\0`, "latin1"), Buffer.from(v, "utf8")])));
    else chunks.push(pngChunk("tEXt", Buffer.from(`${k}\0${v}`, "latin1")));
  }
  chunks.push(pngChunk("IDAT", zlib.deflateSync(Buffer.from([0, 0, 0, 0, 0]))));
  chunks.push(pngChunk("IEND", Buffer.alloc(0)));
  return Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), ...chunks]);
}
/** JPEG có EXIF IFD0 Copyright. */
export function jpeg(copyright) {
  const s = Buffer.from(`${copyright}\0`, "latin1");
  const tiff = Buffer.alloc(26 + s.length);
  tiff.write("II*\0", 0, "latin1");
  tiff.writeUInt32LE(8, 4);
  tiff.writeUInt16LE(1, 8);
  tiff.writeUInt16LE(0x8298, 10);
  tiff.writeUInt16LE(2, 12);
  tiff.writeUInt32LE(s.length, 14);
  tiff.writeUInt32LE(26, 18);
  tiff.writeUInt32LE(0, 22);
  s.copy(tiff, 26);
  const app1 = Buffer.concat([Buffer.from("Exif\0\0", "latin1"), tiff]);
  const len = Buffer.alloc(2);
  len.writeUInt16BE(app1.length + 2);
  return Buffer.concat([Buffer.from([0xff, 0xd8, 0xff, 0xe1]), len, app1, Buffer.from([0xff, 0xd9])]);
}
/** PSD rỗng có IPTC Copyright Notice. */
export function psd(copyright) {
  const h = Buffer.alloc(26);
  h.write("8BPS", 0, "latin1");
  h.writeUInt16BE(1, 4);
  h.writeUInt16BE(3, 12);
  h.writeUInt32BE(1, 14);
  h.writeUInt32BE(1, 18);
  h.writeUInt16BE(8, 22);
  h.writeUInt16BE(3, 24);
  const text = Buffer.from(copyright, "utf8");
  const iptc = Buffer.concat([Buffer.from([0x1c, 2, 116, text.length >> 8, text.length & 0xff]), text]);
  const pad = iptc.length % 2 ? Buffer.alloc(1) : Buffer.alloc(0);
  const block = Buffer.concat([Buffer.from("8BIM", "latin1"), Buffer.from([0x04, 0x04, 0, 0]), u32(iptc.length), iptc, pad]);
  return Buffer.concat([h, u32(0), u32(block.length), block, u32(0), u32(0)]);
}
const u32 = (n) => {
  const b = Buffer.alloc(4);
  b.writeUInt32BE(n);
  return b;
};
/** MP3 có ID3v2.3: frames [[id, text]]; WXXX nhận URL. */
export function mp3(frames) {
  const body = Buffer.concat(
    frames.map(([id, v]) => {
      const data = id === "WXXX" ? Buffer.concat([Buffer.from([0, 0]), Buffer.from(v, "latin1")]) : Buffer.concat([Buffer.from([0]), Buffer.from(v, "latin1")]);
      const hdr = Buffer.alloc(10);
      hdr.write(id, 0, "latin1");
      hdr.writeUInt32BE(data.length, 4);
      return Buffer.concat([hdr, data]);
    }),
  );
  const n = body.length;
  const size = Buffer.from([(n >> 21) & 0x7f, (n >> 14) & 0x7f, (n >> 7) & 0x7f, n & 0x7f]);
  return Buffer.concat([Buffer.from("ID3\x03\x00\x00", "latin1"), size, body, Buffer.from([0xff, 0xfb, 0x90, 0x00]), Buffer.alloc(64)]);
}
/** WAV có LIST INFO. */
export function wav(info) {
  const le32 = (n) => {
    const b = Buffer.alloc(4);
    b.writeUInt32LE(n);
    return b;
  };
  const subs = Object.entries(info).map(([k, v]) => {
    let d = Buffer.from(`${v}\0`, "latin1");
    if (d.length % 2) d = Buffer.concat([d, Buffer.alloc(1)]);
    return Buffer.concat([Buffer.from(k, "latin1"), le32(d.length), d]);
  });
  const list = Buffer.concat([Buffer.from("INFO", "latin1"), ...subs]);
  const fmt = Buffer.alloc(16);
  fmt.writeUInt16LE(1, 0);
  fmt.writeUInt16LE(1, 2);
  fmt.writeUInt32LE(8000, 4);
  fmt.writeUInt32LE(16000, 8);
  fmt.writeUInt16LE(2, 12);
  fmt.writeUInt16LE(16, 14);
  const body = Buffer.concat([Buffer.from("WAVE", "latin1"), Buffer.from("fmt ", "latin1"), le32(16), fmt, Buffer.from("LIST", "latin1"), le32(list.length), list, Buffer.from("data", "latin1"), le32(0)]);
  return Buffer.concat([Buffer.from("RIFF", "latin1"), le32(body.length), body]);
}
/** TTF tối thiểu: bảng OS/2 (fsType) và name (Windows, UTF-16BE). names: { id: text }. */
export function ttf(names, fsType) {
  const os2 = Buffer.alloc(10);
  os2.writeUInt16BE(4, 0);
  os2.writeUInt16BE(fsType, 8);
  const recs = [];
  const strs = [];
  let off = 0;
  for (const [id, text] of Object.entries(names)) {
    const s = Buffer.alloc(text.length * 2);
    for (let i = 0; i < text.length; i++) s.writeUInt16BE(text.charCodeAt(i), i * 2);
    const r = Buffer.alloc(12);
    r.writeUInt16BE(3, 0);
    r.writeUInt16BE(1, 2);
    r.writeUInt16BE(0x409, 4);
    r.writeUInt16BE(Number(id), 6);
    r.writeUInt16BE(s.length, 8);
    r.writeUInt16BE(off, 10);
    recs.push(r);
    strs.push(s);
    off += s.length;
  }
  const nh = Buffer.alloc(6);
  nh.writeUInt16BE(0, 0);
  nh.writeUInt16BE(recs.length, 2);
  nh.writeUInt16BE(6 + recs.length * 12, 4);
  const name = Buffer.concat([nh, ...recs, ...strs]);
  const head = Buffer.alloc(12);
  head.writeUInt32BE(0x00010000, 0);
  head.writeUInt16BE(2, 4);
  const dir = Buffer.alloc(32);
  const o1 = 12 + 32;
  const o2 = o1 + os2.length + 2;
  dir.write("OS/2", 0, "latin1");
  dir.writeUInt32BE(o1, 8);
  dir.writeUInt32BE(os2.length, 12);
  dir.write("name", 16, "latin1");
  dir.writeUInt32BE(o2, 24);
  dir.writeUInt32BE(name.length, 28);
  return Buffer.concat([head, dir, os2, Buffer.alloc(2), name]);
}
/** .unitypackage (tar.gz): entries [{ guid, pathname }]. */
export function unitypackage(entries) {
  const blocks = [];
  const file = (name, content) => {
    const data = Buffer.from(content, "utf8");
    const h = Buffer.alloc(512);
    h.write(name, 0, "utf8");
    h.write("0000644\0", 100, "latin1");
    h.write(`${data.length.toString(8).padStart(11, "0")}\0`, 124, "latin1");
    h.write("        ", 148, "latin1");
    h[156] = 48;
    blocks.push(h, data, Buffer.alloc((512 - (data.length % 512)) % 512));
  };
  for (const e of entries) {
    file(`${e.guid}/asset`, "dummy");
    file(`${e.guid}/pathname`, `${e.pathname}\n00`);
  }
  blocks.push(Buffer.alloc(1024));
  return zlib.gzipSync(Buffer.concat(blocks));
}

// ---------- dựng project ----------
const MIT = "MIT License\n\nCopyright (c) 2024 Acme\n\nPermission is hereby granted, free of charge, to any person obtaining a copy of this software…";
const GPL = "                    GNU GENERAL PUBLIC LICENSE\n                       Version 3, 29 June 2007\n";
const OFL = "This Font Software is licensed under the SIL Open Font License, Version 1.1.";

/**
 * Dựng project ở `root` và cache Asset Store giả ở `cacheDir`. Trả về { root, cacheDir }.
 * `extra(write)` thêm file cho biến thể.
 */
export function makeFixture(root, cacheDir, extra = null) {
  const files = new Map(); // unity path → Buffer | string
  const metaExtra = new Map();
  const put = (p, content, metaTail = "") => {
    files.set(p, content);
    if (metaTail) metaExtra.set(p, metaTail);
  };
  const g = (p) => guidOf(p);
  const ref = (p) => `{fileID: 11400000, guid: ${g(p)}, type: 2}`;

  fs.mkdirSync(path.join(root, "ProjectSettings"), { recursive: true });
  fs.writeFileSync(path.join(root, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 6000.3.16f1\n");
  fs.writeFileSync(path.join(root, "ProjectSettings", "ProjectSettings.asset"), "PlayerSettings:\n  companyName: Studio Test\n  productName: Fixture Game\n");
  fs.writeFileSync(
    path.join(root, "ProjectSettings", "EditorBuildSettings.asset"),
    `EditorBuildSettings:\n  m_Scenes:\n  - enabled: 1\n    path: Assets/Scenes/Main.unity\n    guid: ${g("Assets/Scenes/Main.unity")}\n  - enabled: 0\n    path: Assets/Scenes/Old.unity\n    guid: ${g("Assets/Scenes/Old.unity")}\n`,
  );
  fs.mkdirSync(path.join(root, "Packages", "com.acme.tools", "Runtime"), { recursive: true });
  fs.writeFileSync(path.join(root, "Packages", "manifest.json"), JSON.stringify({ dependencies: { "com.unity.ugui": "2.0.0", "com.acme.tools": "file:com.acme.tools" } }));
  fs.writeFileSync(path.join(root, "Packages", "com.acme.tools", "package.json"), JSON.stringify({ name: "com.acme.tools", version: "1.0.0", displayName: "Acme Tools", license: "MIT" }));
  fs.writeFileSync(path.join(root, "Packages", "com.acme.tools", "LICENSE.md"), MIT);
  fs.writeFileSync(path.join(root, "Packages", "com.acme.tools", "Runtime", "Acme.cs"), "public class Acme {}\n");
  fs.writeFileSync(path.join(root, "Packages", "com.acme.tools", "Runtime", "Acme.cs.meta"), `fileFormatVersion: 2\nguid: ${g("Packages/com.acme.tools/Runtime/Acme.cs")}\n`);
  // Gói công ty qua registry mang tên công ty (3 gói cùng tác giả, không license); registry đó mirror cả một gói ngoài.
  const pkgs = { "com.stt.a": "STT", "com.stt.b": "STT", "com.stt.c": "STT", "com.mirror.lib": "Mirror Inc", "com.unity.fakedebug": "Unity Technologies", "com.cysharp.unitask": "Cysharp, Inc." };
  for (const [name, author] of Object.entries(pkgs)) {
    fs.mkdirSync(path.join(root, "Packages", name), { recursive: true });
    fs.writeFileSync(path.join(root, "Packages", name, "package.json"), JSON.stringify({ name, version: "1.0.0", author }));
  }
  fs.mkdirSync(path.join(root, "Packages", "com.unity.fakedebug", "Fonts"), { recursive: true });
  fs.writeFileSync(path.join(root, "Packages", "com.unity.fakedebug", "Fonts", "Dos.ttf"), ttf({ 1: "Dos Font" }, 0));
  fs.writeFileSync(path.join(root, "Packages", "com.unity.fakedebug", "Fonts", "Dos.ttf.meta"), `fileFormatVersion: 2\nguid: ${g("Packages/com.unity.fakedebug/Fonts/Dos.ttf")}\n`);
  const manifest = JSON.parse(fs.readFileSync(path.join(root, "Packages", "manifest.json"), "utf8"));
  manifest.scopedRegistries = [{ name: "Studio Test Registry", url: "https://upm.example.invalid", scopes: ["com.stt", "com.mirror"] }];
  fs.writeFileSync(path.join(root, "Packages", "manifest.json"), JSON.stringify(manifest));

  // Scene build → prefab → ảnh, font asset, pack
  put("Assets/Scenes/Main.unity", `%YAML 1.1\n--- !u!1001 &1\nPrefabInstance:\n  m_SourcePrefab: ${ref("Assets/Game/UI/Shop.prefab")}\n  height: ${ref("Assets/Game/Maps/Island/height.png")}\n`);
  put("Assets/Scenes/Old.unity", `%YAML 1.1\n  tex: ${ref("Assets/Game/Unused/old_only_in_disabled_scene.png")}\n`);
  put(
    "Assets/Game/UI/Shop.prefab",
    [
      "%YAML 1.1",
      `  coin: ${ref("Assets/Game/Art/coin.png")}`,
      `  hero: ${ref("Assets/Game/Art/hero.png")}`,
      `  shot: ${ref("Assets/Game/Art/Screenshot-2021-12-28-1220561.png")}`,
      `  font: ${ref("Assets/Game/Fonts/GameFont SDF.asset")}`,
      `  locked: ${ref("Assets/Game/Fonts/Locked.ttf")}`,
      `  gem: ${ref("Assets/ThirdParty/CoolPack/Sprites/gem.png")}`,
      `  model: ${ref("Assets/ExplosivePack/Models/a.fbx")}`,
      `  anim: ${ref("Assets/ExplosivePack/Moved/c.anim")}`,
      "  m_text: Shop",
    ].join("\n"),
  );
  put("Assets/Game/Art/coin.png", png([["Copyright", "(c) Studio Test 2026"]]));
  put(
    "Assets/Game/Art/hero.png",
    png([["XML:com.adobe.xmp", '<x:xmpmeta><rdf:Description Iptc4xmpExt:DigitalSourceType="http://cv.iptc.org/newscodes/digitalsourcetype/trainedAlgorithmicMedia"><dc:rights><rdf:Alt><rdf:li>Someone Else Inc</rdf:li></rdf:Alt></dc:rights></rdf:Description></x:xmpmeta>', "iTXt"]]),
  );
  put("Assets/Game/Art/Screenshot-2021-12-28-1220561.png", png());
  put("Assets/Game/Art/photo.jpg", jpeg("Getty Images"));
  put("Assets/Game/Resources/key.psd", psd("Other Studio Ltd"));
  put("Assets/Game/Unused/old.png", png());
  put("Assets/Game/Unused/old_only_in_disabled_scene.png", png());
  put("Assets/Game/Fonts/GameFont.ttf", ttf({ 0: "Copyright 2020 The Foo Project Authors", 1: "Foo Sans", 13: OFL, 14: "https://openfontlicense.org" }, 0));
  put("Assets/Game/Fonts/GameFont SDF.asset", `%YAML 1.1\nMonoBehaviour:\n  m_SourceFontFileGUID: ${g("Assets/Game/Fonts/GameFont.ttf")}\n`);
  put("Assets/Game/Fonts/Locked.ttf", ttf({ 0: "Copyright (c) 2021 Big Publisher. All rights reserved.", 1: "Big Casual", 13: "This is a custom font owned by Big Publisher" }, 2));
  put("Assets/Game/Audio/Resources/bgm.mp3", mp3([["TIT2", "Epic Theme"], ["TCOP", "(c) Famous Records"], ["WXXX", "https://freesound.org/s/123"]]));
  put("Assets/Game/Audio/Resources/click.wav", wav({ ICOP: "Studio Test", ISFT: "Audacity" }));
  put("Assets/Game/Scripts/Player.cs", "public class Player {}\n");
  put("Assets/Game/Scripts/Editor/Tool.cs", "public class Tool {}\n");
  put("Assets/Game/Scripts/EditorOnly/EditorOnly.asmdef", JSON.stringify({ name: "EditorOnly", includePlatforms: ["Editor"] }));
  put("Assets/Game/Scripts/EditorOnly/Helper.cs", "class Helper {}\n");
  put("Assets/Game/Scripts/Hacked/Dump.cs", "// Token: 0x06000001 RID: 1 RVA: 0x1234\npublic class Dump {}\n");
  put("Assets/Game/Features/CandyCrushShop/ShopController.cs", "public class ShopController {}\n");
  put("Assets/Game/Features/Boost/SupersonicBoom.cs", "public class SupersonicBoom {}\n");
  put("Assets/Game/Maps/Island/CREDIT.txt", "Heightmap by mapmaker42. Free to use for anything, but please credit me (mapmaker42) in your game.\n");
  put("Assets/Game/Maps/Island/height.png", png());
  put("Assets/Game/Addr/level1.asset", `%YAML 1.1\n  tile: ${ref("Assets/Game/Addr/tile.png")}\n`);
  put("Assets/Game/Addr/tile.png", png());
  put("Assets/Game/Bundled/boss.png", png(), "assetBundleName: boss\n");
  put("Assets/AddressableAssetsData/AssetGroups/Default Local Group.asset", `%YAML 1.1\n  m_SerializeEntries:\n  - m_GUID: ${g("Assets/Game/Addr/level1.asset")}\n    m_Address: level1\n`);
  put("Assets/StreamingAssets/config.json", "{}\n");
  put("Assets/Plugins/Demigiant/DOTween/DOTween.dll", "MZ");
  put("Assets/ThirdParty/CoolPack/Readme.txt", "Thanks for buying Cool Pack! https://assetstore.unity.com/packages/2d/gui/cool-pack-12345\n");
  put("Assets/ThirdParty/CoolPack/Sprites/gem.png", png([["Copyright", "Cool Studio"]]));
  put("Assets/ThirdParty/CoolPack/Sprites/noise.png", png([["Author", "PDF Tools AG"], ["Title", "PDF Creator"]]));
  put("Assets/ThirdParty/UI/frame.png", png());
  put("Assets/Plugins/iOS/Firebase/libFirebaseCore.a", "!<arch>");
  put("Assets/Plugins/tvOS/Firebase/libFirebaseCore.a", "!<arch>");
  put("Assets/Game/Features/Demo/README.md", "Demo của team. Blockout xong thì đắp asset riêng (Unity Asset Store, Poly Haven CC0).\n");
  put("Assets/Game/Features/Demo/terrain.png", png());
  // Pack đặt thẳng dưới Assets; mẫu package ở Assets/Samples; folder code của team và folder Demo trơn thì không phải pack.
  put("Assets/Jurassic Pack/readMe.pdf", "%PDF-1.4");
  put("Assets/Jurassic Pack/Creatures/raptor.fbx", "Kaydara FBX Binary");
  put("Assets/TerrainPainter/_Demo/demo.png", png());
  put("Assets/TerrainPainter/Runtime/Painter.cs", "class Painter {}\n");
  put("Assets/Samples/Acme Tools/1.0.0/sample.png", png());
  put("Assets/FeatureGateKit/Gate.cs", "class Gate {}\n");
  put("Assets/FeatureGateKit/README.md", "# FeatureGateKit\nHệ mở khoá feature tập trung của team.\n");
  put("Assets/Game/Demo/demo_level.png", png());
  put("Assets/Game/Art/image.png", png());
  put("Assets/Game/Art/image (2).png", png());
  put("Assets/ThirdParty/GplLib/LICENSE", GPL);
  put("Assets/ThirdParty/GplLib/GplCode.cs", "class GplCode {}\n");
  put("Assets/ExplosivePack/Models/a.fbx", "Kaydara FBX Binary");
  put("Assets/ExplosivePack/Models/b.fbx", "Kaydara FBX Binary");
  put("Assets/ExplosivePack/Moved/c.anim", "%YAML 1.1\nAnimationClip:\n");
  put("Assets/Tools/competitor_v1.apk", "PK");
  // IP-15: thương hiệu thật trong dữ liệu nội dung (vào build qua Resources); tên nền tảng để đăng nhập thì không báo.
  put("Assets/Game/Resources/Localization/items.csv", "id,en\ncar_01,Lamborghini Aventador\ncar_02,Thunder Bolt\nlogin,Login with Facebook\n");
  put("Assets/Game/Data/Unused/old_items.csv", "id,en\ngun_01,Glock 17\n");
  if (extra) extra(put);

  const dirs = new Set();
  for (const [p, content] of files) {
    const abs = path.join(root, ...p.split("/"));
    fs.mkdirSync(path.dirname(abs), { recursive: true });
    fs.writeFileSync(abs, content);
    fs.writeFileSync(`${abs}.meta`, `fileFormatVersion: 2\nguid: ${g(p)}\n${metaExtra.get(p) || ""}`);
    const parts = p.split("/");
    for (let i = 2; i < parts.length; i++) dirs.add(parts.slice(0, i).join("/"));
  }
  for (const d of dirs) fs.writeFileSync(path.join(root, ...d.split("/")) + ".meta", `fileFormatVersion: 2\nguid: ${g(d)}\nfolderAsset: yes\n`);
  // Danh sách build thật do gói Unity ghi: có một asset mà ước lượng YAML không thấy (nạp bằng code, không tham chiếu).
  fs.mkdirSync(path.join(root, "Library", "GameIP"), { recursive: true });
  fs.writeFileSync(
    path.join(root, "Library", "GameIP", "build_assets.json"),
    JSON.stringify({ generatedAt: "2026-10-23T00:00:00Z", target: "Android", development: false, packedAssetGroups: 3, assets: [{ path: "Assets/Scenes/Main.unity", guid: g("Assets/Scenes/Main.unity") }, { path: "Assets/Game/Unused/old.png", guid: g("Assets/Game/Unused/old.png") }] }),
  );
  fs.mkdirSync(path.join(root, "Builds"), { recursive: true });
  fs.writeFileSync(path.join(root, "Builds", "FixtureGame.apk"), "PK");

  // Creative UA (ngoài Assets): sổ creative.json có lỗi cài sẵn cho lệnh creative.
  const cr = path.join(root, "Creatives", "ad01");
  fs.mkdirSync(path.join(cr, "music"), { recursive: true });
  fs.writeFileSync(path.join(cr, "gameplay_take3.mp4"), "....ftypisom");
  fs.writeFileSync(path.join(cr, "music", "trend_song.mp3"), mp3([["TIT2", "Trend Song"]]));
  fs.writeFileSync(path.join(cr, "music", "stock_drop.mp3"), mp3([["TCOP", "(c) Big Label"], ["WXXX", "https://example-music.com/t/9"]]));
  fs.writeFileSync(path.join(cr, "actor_hand.png"), png());
  fs.writeFileSync(path.join(cr, "endcard_ai.png"), png([["parameters", "chibi knight, masterpiece, Steps: 30"]]));
  fs.writeFileSync(path.join(cr, "playable.html"), '<html><body><img src="https://cdn.example.com/a.png"><p>Better than Candy Crush!</p><script>var x=1;</script></body></html>');
  fs.writeFileSync(path.join(cr, "unlisted.png"), png());
  fs.writeFileSync(
    path.join(cr, "creative.json"),
    JSON.stringify({
      creative: "ad01",
      networks: ["meta", "tiktok"],
      storePreview: false,
      entries: [
        { id: "gameplay", kind: "gameplay", title: "Gameplay quay từ game", license: "inhouse", scope: { ads: true }, paths: ["gameplay_take3.mp4", "playable.html"], status: "confirmed" },
        { id: "trend", kind: "platform-music", title: "Nhạc TikTok CML", license: "custom", scope: { ads: true }, platforms: ["tiktok"], paths: ["music/trend_song.mp3"], status: "confirmed" },
        { id: "stock", kind: "music", title: "Nhạc stock", license: "custom", scope: { ads: "unknown" }, paths: ["music/stock_drop.mp3"], status: "draft" },
        { id: "actor", kind: "actor", title: "Tay người thật", license: "custom", scope: { ads: true }, paths: ["actor_hand.png"], status: "confirmed" },
        { id: "endcard", kind: "asset", title: "End card", license: "inhouse", scope: { ads: true }, paths: ["endcard_ai.png"], status: "confirmed", notInGame: true },
      ],
    }),
  );
  fs.mkdirSync(path.join(root, "Creatives", "ad02"), { recursive: true });
  fs.writeFileSync(path.join(root, "Creatives", "ad02", "clip.mp4"), "....ftypisom");
  fs.writeFileSync(path.join(root, "Creatives", "ad02", "bgm.mp3"), mp3([["TIT2", "x"]]));

  // Cache Asset Store giả: gói "Explosive Pack" có 3 asset; c.anim đã bị dời khỏi chỗ gốc.
  const pkgDir = path.join(cacheDir, "Explosive", "Animation");
  fs.mkdirSync(pkgDir, { recursive: true });
  fs.writeFileSync(
    path.join(pkgDir, "Explosive Pack.unitypackage"),
    unitypackage([
      { guid: g("Assets/ExplosivePack/Models/a.fbx"), pathname: "Assets/ExplosivePack/Models/a.fbx" },
      { guid: g("Assets/ExplosivePack/Models/b.fbx"), pathname: "Assets/ExplosivePack/Models/b.fbx" },
      { guid: g("Assets/ExplosivePack/Moved/c.anim"), pathname: "Assets/ExplosivePack/Anims/c.anim" },
      { guid: "ffffffffffffffffffffffffffffffff", pathname: "Assets/ExplosivePack/NotImported.png" },
    ]),
  );
  return { root, cacheDir };
}

// Chạy thẳng: node make_fixture.mjs <folder project> [folder cache Asset Store giả] (dùng cho scaffold của evals).
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const root = path.resolve(process.argv[2] || ".");
  const cache = path.resolve(process.argv[3] || path.join(root, "..", `${path.basename(root)}-assetstore-cache`));
  makeFixture(root, cache);
  console.log(`Project giả: ${root}\nCache Asset Store giả: ${cache}`);
}
