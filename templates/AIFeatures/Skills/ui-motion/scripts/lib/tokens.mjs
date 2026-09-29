// Tách tên object thành token và so keyword — cùng luật với UIMotionNameTokens của module (tách theo ký tự không phải chữ /
// số, CamelCase, ranh giới chữ / số; keyword khớp một token hoặc hai token liền nhau ghép lại).

const isLetterOrDigit = (c) => /[\p{L}\p{Nd}]/u.test(c);
const isDigit = (c) => /\p{Nd}/u.test(c);
const isUpper = (c) => /\p{Lu}/u.test(c);
const isLower = (c) => /\p{Ll}/u.test(c);

export function tokenize(name) {
  const tokens = [];
  if (!name) return tokens;
  let cur = "";
  const flush = () => {
    if (cur.length) tokens.push(cur);
    cur = "";
  };
  for (let i = 0; i < name.length; i++) {
    const c = name[i];
    if (!isLetterOrDigit(c)) {
      flush();
      continue;
    }
    if (cur.length && boundary(name, i)) flush();
    cur += c.toLowerCase();
  }
  flush();
  return tokens;
}

function boundary(name, i) {
  const prev = name[i - 1];
  const c = name[i];
  if (isDigit(c) !== isDigit(prev)) return true;
  if (isUpper(c) && isLower(prev)) return true;
  return isUpper(c) && isUpper(prev) && i + 1 < name.length && isLower(name[i + 1]);
}

export function matches(tokens, keyword) {
  if (!keyword || !tokens.length) return false;
  for (let i = 0; i < tokens.length; i++) {
    if (tokens[i] === keyword) return true;
    if (i + 1 < tokens.length && tokens[i] + tokens[i + 1] === keyword) return true;
  }
  return false;
}

/** Keyword đầu tiên (theo thứ tự bảng) khớp tên; null nếu không có. `table` = [{ word, role }]. */
export function matchKeyword(name, table) {
  const tokens = tokenize(name);
  for (const k of table) if (k.word && matches(tokens, k.word)) return k;
  return null;
}

/** Bỏ số đuôi kiểu item (3), item_3 → item (như resolver của module khi so tên item). */
export function baseName(name) {
  return (name || "").replace(/\s*\(\d+\)\s*$/, "").replace(/[_\s-]*\d+$/, "");
}
