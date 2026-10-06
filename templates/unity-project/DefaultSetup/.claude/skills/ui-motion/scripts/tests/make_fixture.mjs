// Dựng một project Unity GIẢ (chỉ file text: ProjectSettings, manifest, .cs, .prefab, .meta) để selftest và evals của skill
// chạy mà không cần project thật. Tên class / object tự đặt, không lấy của project nào.
//   node make_fixture.mjs --out <dir> [--module <folder module UIMotion>] [--module-at Assets/Game/Modules/UIMotion]
import fs from "node:fs";
import path from "node:path";

const args = process.argv.slice(2);
const opt = (name, def) => {
  const i = args.indexOf(name);
  return i >= 0 && i + 1 < args.length ? args[i + 1] : def;
};

export const GUID = {
  PopupBase: "a1000000000000000000000000000001",
  ShopPopup: "a1000000000000000000000000000002",
  ClickScale: "a1000000000000000000000000000003",
  TapArea: "a1000000000000000000000000000004",
  NewBadge: "a1000000000000000000000000000005",
  ListStagger: "a1000000000000000000000000000006",
  UIManager: "a1000000000000000000000000000007",
  AudioHub: "a1000000000000000000000000000008",
  SfxId: "a1000000000000000000000000000009",
  Boot: "a100000000000000000000000000000a",
  HomeScreen: "b1000000000000000000000000000001",
  ShopPopupPrefab: "b1000000000000000000000000000002",
  ItemView: "b1000000000000000000000000000003",
  OfferView: "b1000000000000000000000000000004",
  RewardView: "b1000000000000000000000000000005",
  PackButton: "b1000000000000000000000000000006",
  PanelTemplate: "b1000000000000000000000000000007",
  KitButton: "b1000000000000000000000000000008",
};
const BUTTON = "4e29b1a8efbd4b44bb3f3716e73f07ff";
const IMAGE = "fe87c0e1cc204ed48ad3b37840f39efc";
const TEXT = "5f7201a12d95ffc409449d95f23cf332";
const VLG = "59f8146938fff824cb5fd77236b75775";

const SOURCES = {
  "Assets/Game/Scripts/UI/PopupBase.cs": `using DG.Tweening;
using UnityEngine;

namespace Fixture.UI
{
    // Popup của game tự có jump in / out bằng DOTween.
    public class PopupBase : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private RectTransform _window;

        public virtual void Show()
        {
            gameObject.SetActive(true);
            _window.localScale = Vector3.one * 0.85f;
            _window.DOScale(1f, 0.25f).SetEase(Ease.OutBack);
            _group.DOFade(1f, 0.2f);
        }

        public virtual void Hide()
        {
            _window.DOScale(0.85f, 0.15f).SetEase(Ease.InQuad);
            _group.DOFade(0f, 0.15f).OnComplete(() => gameObject.SetActive(false));
        }
    }
}
`,
  "Assets/Game/Scripts/UI/ShopPopup.cs": `namespace Fixture.UI
{
    public class ShopPopup : PopupBase
    {
    }
}
`,
  "Assets/Game/Scripts/UI/ClickScale.cs": `using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using Fixture.Audio;

namespace Fixture.UI
{
    // Nút của game tự nhún và tự phát tiếng bấm.
    public class ClickScale : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public void OnPointerDown(PointerEventData e)
        {
            transform.DOScale(0.92f, 0.08f);
            AudioHub.Instance.PlaySfx(SfxId.Tap);
        }

        public void OnPointerUp(PointerEventData e)
        {
            transform.DOScale(1f, 0.12f).SetEase(Ease.OutBack);
        }
    }
}
`,
  "Assets/Game/Scripts/UI/TapArea.cs": `using UnityEngine;
using UnityEngine.EventSystems;

namespace Fixture.UI
{
    // Vùng chạm tự viết: bấm được nhưng không có Button, không có motion.
    public class TapArea : MonoBehaviour, IPointerClickHandler
    {
        public System.Action Tapped;
        public void OnPointerClick(PointerEventData e) => Tapped?.Invoke();
    }
}
`,
  "Assets/Game/Scripts/UI/NewBadge.cs": `using UnityEngine;

namespace Fixture.UI
{
    // Chấm "mới" của game: code bật / tắt, không có motion.
    public class NewBadge : MonoBehaviour
    {
        public void Refresh(bool on) => gameObject.SetActive(on);
    }
}
`,
  "Assets/Game/Scripts/UI/ListStagger.cs": `using DG.Tweening;
using UnityEngine;

namespace Fixture.UI
{
    // List của game tự cho item hiện lần lượt.
    public class ListStagger : MonoBehaviour
    {
        [SerializeField] private GameObject itemPrefab;
        [SerializeField] private float delay = 0.05f;

        private void OnEnable()
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = (RectTransform)transform.GetChild(i);
                child.localScale = Vector3.zero;
                child.DOScale(1f, 0.2f).SetDelay(i * delay).SetEase(Ease.OutBack);
            }
        }
    }
}
`,
  "Assets/Game/Scripts/UI/UIManager.cs": `using System.Collections.Generic;
using UnityEngine;

namespace Fixture.UI
{
    // Quản lý popup: mở bằng Instantiate, đóng bằng SetActive(false).
    public class UIManager : MonoBehaviour
    {
        private readonly Stack<PopupBase> _stack = new Stack<PopupBase>();
        [SerializeField] private PopupBase[] _prefabs;

        public PopupBase OpenPopup(int index)
        {
            PopupBase popup = Instantiate(_prefabs[index], transform);
            _stack.Push(popup);
            popup.Show();
            return popup;
        }

        public void ClosePopup()
        {
            if (_stack.Count == 0) return;
            _stack.Pop().gameObject.SetActive(false);
        }

        public void CloseAll()
        {
            while (_stack.Count > 0) _stack.Pop().gameObject.SetActive(false);
        }
    }
}
`,
  "Assets/Game/Scripts/Audio/AudioHub.cs": `using UnityEngine;

namespace Fixture.Audio
{
    public class AudioHub : MonoBehaviour
    {
        public static AudioHub Instance { get; private set; }
        [SerializeField] private AudioSource _source;
        [SerializeField] private AudioClip[] _clips;

        private void Awake() => Instance = this;

        public void PlaySfx(SfxId id) => _source.PlayOneShot(_clips[(int)id]);
    }
}
`,
  "Assets/Game/Scripts/Audio/SfxId.cs": `namespace Fixture.Audio
{
    public enum SfxId
    {
        Tap,
        Open,
        Close,
        Coin,
        Error
    }
}
`,
  "Assets/Game/Scripts/Boot.cs": `using UnityEngine;
using Fixture.Audio;

namespace Fixture
{
    public class Boot : MonoBehaviour
    {
        private void Awake()
        {
            Application.targetFrameRate = 60;
            AudioHub.Instance.PlaySfx(SfxId.Open);
        }
    }
}
`,
};

// ---- prefab ----
let nextId = 1000;
const id = () => String(nextId++);

/** nodes: [{ name, parent (index), stretch, comps: [guid | {guid, refs:[guid]}], active }] → YAML prefab. */
function prefab(nodes) {
  const docs = [];
  const ids = nodes.map(() => ({ go: id(), rt: id() }));
  nodes.forEach((n, i) => {
    const comps = (n.comps || []).map((c) => (typeof c === "string" ? { guid: c, refs: [] } : c)).map((c) => ({ ...c, id: id() }));
    n._comps = comps;
    const children = nodes.map((m, j) => (m.parent === i ? j : -1)).filter((j) => j >= 0);
    docs.push(`--- !u!1 &${ids[i].go}
GameObject:
  m_ObjectHideFlags: 0
  m_Component:
  - component: {fileID: ${ids[i].rt}}
${comps.map((c) => `  - component: {fileID: ${c.id}}`).join("\n")}
  m_Layer: 5
  m_Name: ${n.name}
  m_IsActive: ${n.active === false ? 0 : 1}`);
    const a = n.stretch ? ["{x: 0, y: 0}", "{x: 1, y: 1}"] : ["{x: 0.5, y: 0.5}", "{x: 0.5, y: 0.5}"];
    docs.push(`--- !u!224 &${ids[i].rt}
RectTransform:
  m_GameObject: {fileID: ${ids[i].go}}
  m_Children:${children.length ? "\n" + children.map((j) => `  - {fileID: ${ids[j].rt}}`).join("\n") : " []"}
  m_Father: {fileID: ${n.parent != null ? ids[n.parent].rt : 0}}
  m_AnchorMin: ${a[0]}
  m_AnchorMax: ${a[1]}`);
    for (const c of comps) {
      docs.push(`--- !u!114 &${c.id}
MonoBehaviour:
  m_GameObject: {fileID: ${ids[i].go}}
  m_Enabled: 1
  m_Script: {fileID: 11500000, guid: ${c.guid}, type: 3}${(c.refs || []).map((g, k) => `\n  ref${k}: {fileID: 100100000, guid: ${g}, type: 3}`).join("")}`);
    }
  });
  return "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n" + docs.join("\n") + "\n";
}

const PREFABS = {
  "Assets/Game/UI/Prefabs/HomeScreen.prefab": {
    guid: GUID.HomeScreen,
    nodes: [
      { name: "HomeScreen", stretch: true },
      { name: "pnlTop", parent: 0 },
      { name: "lblGold", parent: 1, comps: [TEXT] },
      { name: "btnPlay", parent: 0, comps: [IMAGE, BUTTON, GUID.ClickScale] },
      { name: "btnShop", parent: 0, comps: [IMAGE, BUTTON, GUID.ClickScale] },
      { name: "icoNew", parent: 4, comps: [IMAGE, GUID.NewBadge] },
      { name: "btnSettings", parent: 0, comps: [IMAGE, BUTTON, GUID.ClickScale] },
      { name: "tapMap", parent: 0, comps: [IMAGE, GUID.TapArea] },
      { name: "pnlBottom", parent: 0 },
      { name: "pnlNews", parent: 0, comps: [IMAGE] },
    ],
  },
  "Assets/Game/UI/Prefabs/ShopPopup.prefab": {
    guid: GUID.ShopPopupPrefab,
    nodes: [
      { name: "ShopPopup", stretch: true, comps: [GUID.ShopPopup] },
      { name: "pnlDim", parent: 0, stretch: true, comps: [IMAGE] },
      { name: "pnlWindow", parent: 0, comps: [IMAGE] },
      { name: "btnClose", parent: 2, comps: [IMAGE, BUTTON, GUID.ClickScale] },
      { name: "lblTitle", parent: 2, comps: [TEXT] },
      { name: "lstOffers", parent: 2, comps: [VLG, { guid: GUID.ListStagger, refs: [GUID.ItemView, GUID.OfferView, GUID.RewardView] }] },
      { name: "lblCoinValue", parent: 2, comps: [TEXT] },
    ],
  },
  "Assets/Game/UI/Prefabs/Items/ItemView.prefab": { guid: GUID.ItemView, nodes: [{ name: "ItemView", comps: [IMAGE] }, { name: "lblName", parent: 0, comps: [TEXT] }, { name: "btnBuy", parent: 0, comps: [IMAGE, BUTTON] }] },
  "Assets/Game/UI/Prefabs/Items/OfferView.prefab": { guid: GUID.OfferView, nodes: [{ name: "OfferView", comps: [IMAGE] }, { name: "lblPrice", parent: 0, comps: [TEXT] }] },
  "Assets/Game/UI/Prefabs/Items/RewardView.prefab": { guid: GUID.RewardView, nodes: [{ name: "RewardView", comps: [IMAGE] }, { name: "lblAmount", parent: 0, comps: [TEXT] }] },
  "Assets/Game/UI/Prefabs/PanelTemplate.prefab": { guid: GUID.PanelTemplate, nodes: [{ name: "PanelTemplate", stretch: true, comps: [IMAGE] }] },
  "Assets/ThirdParty/FancyPack/Prefabs/PackButton.prefab": { guid: GUID.PackButton, nodes: [{ name: "PackButton", comps: [IMAGE, BUTTON] }] },
  // Pack mua về nằm ngoài folder tên ThirdParty: chỉ nhận ra nhờ Readme.
  "Assets/VendorKit/UIKit/Prefabs/KitButton.prefab": { guid: GUID.KitButton, nodes: [{ name: "KitButton", stretch: true, comps: [IMAGE, BUTTON] }] },
};

export function makeFixture(out, modulePath, moduleAt) {
  const w = (rel, text) => {
    const abs = path.join(out, rel);
    fs.mkdirSync(path.dirname(abs), { recursive: true });
    fs.writeFileSync(abs, text, "utf8");
  };
  w("ProjectSettings/ProjectVersion.txt", "m_EditorVersion: 2022.3.20f1\nm_EditorVersionWithRevision: 2022.3.20f1 (0)\n");
  w("ProjectSettings/ProjectSettings.asset", "%YAML 1.1\n--- !u!129 &1\nPlayerSettings:\n  productName: FixtureGame\n  activeInputHandler: 0\n");
  w("Packages/manifest.json", JSON.stringify({ dependencies: { "com.unity.ugui": "1.0.0", "com.unity.textmeshpro": "3.0.6" } }, null, 2) + "\n");
  w("Assets/Plugins/Demigiant/DOTween/DOTween.dll", "");
  w("Assets/Plugins/Demigiant/DOTween/Modules/DOTweenModuleUI.cs", "// stub: DOTween Modules (UI) của project giả\n");
  w("Assets/VendorKit/UIKit/Readme.txt", "UI Kit - thank you for your purchase.\n");
  for (const [rel, src] of Object.entries(SOURCES)) {
    w(rel, src);
    w(rel + ".meta", `fileFormatVersion: 2\nguid: ${GUID[path.basename(rel, ".cs")]}\n`);
  }
  // Prefab dùng chung: PanelTemplate lồng vào HomeScreen và ShopPopup (khuôn)
  for (const [rel, p] of Object.entries(PREFABS)) {
    let text = prefab(p.nodes);
    if (/HomeScreen|ShopPopup/.test(rel)) text += `--- !u!1001 &${id()}\nPrefabInstance:\n  m_SourcePrefab: {fileID: 100100000, guid: ${GUID.PanelTemplate}, type: 3}\n`;
    w(rel, text);
    w(rel + ".meta", `fileFormatVersion: 2\nguid: ${p.guid}\nPrefabImporter:\n  externalObjects: {}\n`);
  }
  if (modulePath) copyDir(modulePath, path.join(out, moduleAt || "Assets/Game/Modules/UIMotion"));
  return out;
}

function copyDir(from, to) {
  fs.mkdirSync(to, { recursive: true });
  for (const e of fs.readdirSync(from, { withFileTypes: true })) {
    if (e.name === "Skill~") continue; // skill không cần trong project giả
    const a = path.join(from, e.name);
    const b = path.join(to, e.name);
    if (e.isDirectory()) copyDir(a, b);
    else fs.copyFileSync(a, b);
  }
}

if (import.meta.url === `file:///${process.argv[1].replace(/\\/g, "/")}` || process.argv[1]?.endsWith("make_fixture.mjs")) {
  const out = opt("--out");
  if (out) {
    makeFixture(path.resolve(out), opt("--module") ? path.resolve(opt("--module")) : null, opt("--module-at"));
    console.log("fixture: " + path.resolve(out));
  } else if (args.length) {
    console.error("usage: node make_fixture.mjs --out <dir> [--module <module folder>] [--module-at <Assets/...>]");
    process.exit(2);
  }
}
