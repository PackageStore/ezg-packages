// Quét nhanh file C# bằng regex (không biên dịch): namespace, class + lớp cha, enum, tên hàm, và các dấu hiệu motion / âm
// thanh / đóng mở screen. Đủ để đề xuất, không đủ để khẳng định — mọi kết luận ghi "ứng viên" kèm bằng chứng.

/** Bỏ comment và nội dung chuỗi (giữ số dòng) để regex không dính code nằm trong comment / chuỗi. */
export function stripCode(src) {
  let out = "";
  let i = 0;
  const n = src.length;
  while (i < n) {
    const c = src[i];
    const d = src[i + 1];
    if (c === "/" && d === "/") {
      while (i < n && src[i] !== "\n") i++;
      continue;
    }
    if (c === "/" && d === "*") {
      i += 2;
      while (i < n && !(src[i] === "*" && src[i + 1] === "/")) {
        if (src[i] === "\n") out += "\n";
        i++;
      }
      i += 2;
      continue;
    }
    if (c === '"' || (c === "@" && d === '"') || (c === "$" && d === '"') || (c === "$" && d === "@") || (c === "@" && d === "$")) {
      const verbatim = src.slice(i, i + 3).includes("@");
      while (i < n && src[i] !== '"') i++;
      i++;
      out += '""';
      while (i < n) {
        if (verbatim) {
          if (src[i] === '"' && src[i + 1] === '"') {
            i += 2;
            continue;
          }
          if (src[i] === '"') break;
        } else {
          if (src[i] === "\\") {
            i += 2;
            continue;
          }
          if (src[i] === '"' || src[i] === "\n") break;
        }
        if (src[i] === "\n") out += "\n";
        i++;
      }
      i++;
      continue;
    }
    if (c === "'") {
      let j = i + 1;
      if (src[j] === "\\") j += 2;
      else j += 1;
      while (j < n && src[j] !== "'" && j - i < 12) j++;
      out += "' '";
      i = j + 1;
      continue;
    }
    out += c;
    i++;
  }
  return out;
}

const CLASS_RE = /\b(?:(?:public|internal|private|protected|sealed|abstract|static|partial|unsafe|new)\s+)*class\s+([A-Za-z_]\w*)\s*(?:<[^>{]*>)?\s*(?::\s*([^{]+?))?\s*(?:where\s[^{]*)?\{/g;
const ENUM_RE = /\benum\s+([A-Za-z_]\w*)\s*(?::\s*\w+)?\s*\{([^}]*)\}/g;
const NS_RE = /\bnamespace\s+([\w.]+)/;
const METHOD_RE = /\b(?:public|private|protected|internal|static|virtual|override|async|\s)+[\w<>\[\],.?]+\s+([A-Za-z_]\w*)\s*\(([^)]*)\)\s*(?:where[^{]*)?(?:\{|=>)/g;

/** Tách tên lớp cha / interface: "Base<T>, IFoo" → ["Base", "IFoo"]. */
function splitBases(s) {
  if (!s) return [];
  const out = [];
  let depth = 0;
  let cur = "";
  for (const ch of s) {
    if (ch === "<") depth++;
    else if (ch === ">") depth--;
    if (ch === "," && depth === 0) {
      out.push(cur.trim());
      cur = "";
    } else cur += ch;
  }
  if (cur.trim()) out.push(cur.trim());
  return out.map((b) => b.replace(/<.*$/, "").replace(/^global::/, "").trim()).filter(Boolean);
}

const isInterfaceName = (n) => /^I[A-Z]/.test(n.split(".").pop());

export const UI_TWEEN = /\.DO(Scale|ScaleX|ScaleY|Fade|AnchorPos\w*|SizeDelta|Color|LocalMove\w*|Move\w*|Punch\w*|Shake\w*|Rotate|LocalRotate|FillAmount|Blendable\w*|Text|Counter|Jump\w*|Pivot\w*)\s*\(/g;

/**
 * Phân tích một file: { namespace, classes: [{name, base, interfaces}], enums: [{name, members}], methods: [names],
 * features: {...}, tweenLines: [dòng gốc có tween + số] }.
 */
export function analyze(src) {
  const code = stripCode(src);
  const ns = NS_RE.exec(code);
  const classes = [];
  let m;
  CLASS_RE.lastIndex = 0;
  while ((m = CLASS_RE.exec(code))) {
    const bases = splitBases(m[2]);
    const base = bases.length && !isInterfaceName(bases[0]) ? bases[0] : null;
    classes.push({ name: m[1], base, interfaces: bases.filter((b) => b !== base) });
  }
  const enums = [];
  ENUM_RE.lastIndex = 0;
  while ((m = ENUM_RE.exec(code))) {
    const members = m[2]
      .split(",")
      .map((x) => x.replace(/\[[^\]]*\]/g, "").replace(/=.*$/, "").trim())
      .filter((x) => /^[A-Za-z_]\w*$/.test(x));
    enums.push({ name: m[1], members });
  }
  const methods = [];
  METHOD_RE.lastIndex = 0;
  while ((m = METHOD_RE.exec(code))) {
    if (!["if", "for", "foreach", "while", "switch", "using", "return", "lock", "catch", "nameof", "typeof"].includes(m[1])) methods.push(m[1]);
  }

  const count = (re) => (code.match(re) || []).length;
  const features = {
    dotween: /\bDG\.Tweening\b/.test(code) || /\bDOTween\./.test(code),
    uiTweens: count(UI_TWEEN),
    otherTween: /\bLeanTween\.|\bPrimeTween\b|\biTween\.|\bTween\.(Scale|Position|LocalPosition|Alpha|UIAnchoredPosition|Custom)\b/.test(code),
    animator: /\bAnimator\b/.test(code) && /\.(SetTrigger|SetBool|SetInteger|Play|CrossFade)\s*\(/.test(code),
    manualFade: /\bCanvasGroup\b/.test(code) && /\balpha\s*[-+*/]?=/.test(code) && /\b(Update|Coroutine|IEnumerator)\b/.test(code),
    uiEvents: /\bI(Pointer(Down|Up|Click|Enter|Exit)|Drag|BeginDrag|EndDrag|Select|Submit)Handler\b/.test(code) || /\bonClick\s*\.\s*AddListener\b/.test(code),
    pointerHandlers: /\bI(Pointer(Down|Up|Click)|Submit)Handler\b/.test(code),
    setActiveFalse: count(/\.SetActive\s*\(\s*false\s*\)/g),
    setActiveTrue: count(/\.SetActive\s*\(\s*true\s*\)/g),
    destroy: count(/\bDestroy\s*\(/g),
    instantiate: count(/\bInstantiate\s*[<(]/g),
    audioSource: /\bAudioSource\b/.test(code),
    playOneShot: /\.PlayOneShot\s*\(/.test(code),
    singleton: /\bstatic\s+[\w<>.]+\s+Instance\b/.test(code) || /:\s*\w*Singleton\w*</.test(code),
    targetFrameRate: /Application\s*\.\s*targetFrameRate\s*=/.test(code),
    initializeOnLoad: /\[\s*InitializeOnLoad\s*\]/.test(code),
    playModeHook: /playModeStateChanged/.test(code) && /\b(OpenScene|LoadScene|EditorSceneManager)\b/.test(code),
    odinStubs: /namespace\s+Sirenix\.OdinInspector\b/.test(code),
    usesUIMotion: /\bUIMotion\.\w+|\bSetActiveAnimated\b|\bIUIMotionSfxSink\b|\bIUIMotionHideable\b|\bUIMotionFeedback\b/.test(code),
  };

  const tweenLines = [];
  const lines = src.split(/\r?\n/);
  for (let i = 0; i < lines.length && tweenLines.length < 8; i++) {
    const line = lines[i];
    if (/\.DO(Scale|Fade|AnchorPos\w*|LocalMove\w*|Punch\w*|Shake\w*|SizeDelta|Color)\s*\(|SetEase\s*\(\s*Ease\.\w+|LeanTween\.\w+\(/.test(line) && /\d/.test(line)) {
      tweenLines.push({ line: i + 1, text: line.trim().slice(0, 160) });
    }
  }
  const constStrings = [...code.matchAll(/\bconst\s+string\s+([A-Za-z_]\w*)\s*=/g)].map((x) => x[1]);
  return { namespace: ns ? ns[1] : null, classes, enums, methods, features, tweenLines, constStrings };
}

const AUDIO_METHOD = /^(Play|Stop|Fire|Emit)\w*(Sfx|SFX|Sound|Audio|Clip|OneShot|Fx|FX)\w*$|^PlaySfx$|^PlaySound$|^PlayOneShot$|^Play$/;

/** Tên hàm trông như phát âm thanh. */
export function isAudioMethod(name) {
  return AUDIO_METHOD.test(name);
}

const SHOW_HIDE = new Set(["Show", "Hide", "Open", "Close", "Appear", "Disappear", "ShowPopup", "HidePopup", "ClosePopup", "OpenPopup", "OpenScreen", "CloseScreen", "ShowScreen", "HideScreen", "Push", "Pop", "Back", "OnBack", "OnClose", "OnOpen", "Enter", "Exit", "ShowPanel", "HidePanel", "Dismiss"]);

/** Tên hàm trông như mở / đóng screen. */
export function isShowHideMethod(name) {
  return SHOW_HIDE.has(name) || /^(Show|Hide|Open|Close)[A-Z]\w*$/.test(name);
}
