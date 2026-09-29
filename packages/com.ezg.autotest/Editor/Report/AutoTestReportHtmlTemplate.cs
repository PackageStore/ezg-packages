namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Template tĩnh của report.html: một file tự chứa (CSS + JS vanilla inline, không CDN, không font ngoài) để QA
    ///     mở thẳng bằng double-click khi offline. Dữ liệu được nhúng dạng JSON và JS render phía trình duyệt.
    ///     <para>
    ///         Quy ước để chuỗi verbatim C# dễ đọc/sửa: CSS/JS/HTML trong template chỉ dùng nháy đơn (JS dựng HTML bằng
    ///         template literal với thuộc tính nháy đơn); dấu nháy kép (nếu buộc phải có) phải viết đôi "" theo cú pháp @"...".
    ///         Chuỗi KHÔNG dùng $@ (nội suy) nên dấu ngoặc nhọn và ${...} của JS giữ nguyên.
    ///     </para>
    ///     <para>
    ///         Thứ tự ghép (xem <see cref="AutoTestReportWriter.WriteHtml" />): HEAD_START + title + HEAD_STYLE_OPEN + CSS +
    ///         HEAD_END + DATA_META_OPEN + meta + DATA_REPORT_OPEN + report + SCRIPT_OPEN + JS_CORE + JS_OVERVIEW +
    ///         JS_SUITES + JS_ISSUES + JS_APP + DOC_END. Các phần JS nối liền thành một IIFE (mở ở JS_CORE, đóng ở JS_APP).
    ///     </para>
    /// </summary>
    internal static class AutoTestReportHtmlTemplate
    {
        #region Khung tài liệu

        /// <summary>Mở tài liệu tới ngay trước nội dung thẻ &lt;title&gt;.</summary>
        internal const string HEAD_START = @"<!DOCTYPE html>
<html lang='vi'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width, initial-scale=1'>
<meta name='generator' content='EZG Auto Test'>
<meta name='color-scheme' content='light dark'>
<title>";

        /// <summary>Đóng &lt;title&gt;, script chọn theme sớm (tránh nháy màu), mở &lt;style&gt;.</summary>
        internal const string HEAD_STYLE_OPEN = @"</title>
<script>try { var t = localStorage.getItem('ezg-autotest-theme'); if (t === 'light' || t === 'dark') document.documentElement.setAttribute('data-theme', t); } catch (e) { }</script>
<style>
";

        /// <summary>Đóng &lt;head&gt;, mở &lt;body&gt; + khung #app (JS dựng toàn bộ giao diện vào đây).</summary>
        internal const string HEAD_END = @"</style>
</head>
<body>
<div id='app'><noscript><p style='padding:24px'>Cần bật JavaScript để xem báo cáo. Dữ liệu gốc nằm trong report.json cùng thư mục.</p></noscript></div>
";

        /// <summary>Mở khối JSON meta (nhãn trạng thái/severity, thời điểm tạo, version package).</summary>
        internal const string DATA_META_OPEN = @"<script type=""application/json"" id=""meta"">";

        /// <summary>Đóng meta, mở khối JSON chứa toàn bộ TestRunReport.</summary>
        internal const string DATA_REPORT_OPEN = @"</script>
<script type=""application/json"" id=""data"">";

        /// <summary>Đóng khối dữ liệu, mở script ứng dụng.</summary>
        internal const string SCRIPT_OPEN = @"</script>
<script>
";

        /// <summary>Đóng script + tài liệu.</summary>
        internal const string DOC_END = @"</script>
</body>
</html>
";

        #endregion

        #region CSS

        /// <summary>Toàn bộ CSS: token màu sáng/tối (prefers-color-scheme + data-theme), layout dashboard, responsive, print.</summary>
        internal const string CSS = @":root {
  --bg: #f3f5f9; --surface: #ffffff; --surface-2: #f7f9fc; --surface-3: #edf1f6;
  --border: #e0e5ed; --border-strong: #cbd3df;
  --text: #17202e; --text-2: #3a4658; --muted: #6a778c;
  --accent: #3461e6; --on-accent: #ffffff;
  --pass: #17864a; --warn: #b87412; --fail: #d63434; --crash: #a4278f;
  --skip: #778399; --cancel: #56667e; --run: #3461e6;
  --sev-4: #8e1b1b; --sev-3: #dc3a3a; --sev-2: #e8740f; --sev-1: #b89400; --sev-0: #3479bd;
  --good: #17864a; --ok: #6a9a12; --meh: #d9860b; --bad: #d63434;
  --shadow: 0 1px 2px rgba(15, 23, 42, .05), 0 2px 6px rgba(15, 23, 42, .04);
  --shadow-lg: 0 10px 30px rgba(15, 23, 42, .18);
  --radius: 12px; --radius-sm: 8px;
  --font: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, 'Noto Sans', sans-serif;
  --mono: ui-monospace, SFMono-Regular, Menlo, Consolas, 'Liberation Mono', monospace;
  color-scheme: light;
}
@media (prefers-color-scheme: dark) {
  :root:not([data-theme='light']) {
    --bg: #0c1016; --surface: #141a23; --surface-2: #19202b; --surface-3: #202936;
    --border: #262f3e; --border-strong: #354053;
    --text: #e5eaf1; --text-2: #c1cad7; --muted: #8995a8;
    --accent: #6e94ff; --on-accent: #0c1016;
    --pass: #3ccb7c; --warn: #efb03a; --fail: #ff6b6b; --crash: #e271cf;
    --skip: #8c97a9; --cancel: #a1adc0; --run: #6e94ff;
    --sev-4: #ff4d6a; --sev-3: #ff7a6b; --sev-2: #ffa24f; --sev-1: #e3c348; --sev-0: #5ea8ef;
    --good: #3ccb7c; --ok: #a6d24a; --meh: #efb03a; --bad: #ff6b6b;
    --shadow: 0 1px 2px rgba(0, 0, 0, .35);
    --shadow-lg: 0 12px 36px rgba(0, 0, 0, .55);
    color-scheme: dark;
  }
}
:root[data-theme='dark'] {
  --bg: #0c1016; --surface: #141a23; --surface-2: #19202b; --surface-3: #202936;
  --border: #262f3e; --border-strong: #354053;
  --text: #e5eaf1; --text-2: #c1cad7; --muted: #8995a8;
  --accent: #6e94ff; --on-accent: #0c1016;
  --pass: #3ccb7c; --warn: #efb03a; --fail: #ff6b6b; --crash: #e271cf;
  --skip: #8c97a9; --cancel: #a1adc0; --run: #6e94ff;
  --sev-4: #ff4d6a; --sev-3: #ff7a6b; --sev-2: #ffa24f; --sev-1: #e3c348; --sev-0: #5ea8ef;
  --good: #3ccb7c; --ok: #a6d24a; --meh: #efb03a; --bad: #ff6b6b;
  --shadow: 0 1px 2px rgba(0, 0, 0, .35);
  --shadow-lg: 0 12px 36px rgba(0, 0, 0, .55);
  color-scheme: dark;
}

* { box-sizing: border-box; }
html { -webkit-text-size-adjust: 100%; }
body { margin: 0; background: var(--bg); color: var(--text); font: 14px/1.5 var(--font); }
body.noscroll { overflow: hidden; }
a { color: var(--accent); }
code, pre { font-family: var(--mono); font-size: 12.5px; }
code { background: var(--surface-3); padding: 1px 5px; border-radius: 5px; word-break: break-all; }
pre { margin: 0; white-space: pre-wrap; word-break: break-word; }
h1, h2, h3, h4 { margin: 0; font-weight: 650; }
button { font: inherit; color: inherit; }
[hidden] { display: none !important; }
.muted { color: var(--muted); }
.r { text-align: right; }
.wrap { max-width: 1320px; margin: 0 auto; padding: 0 16px; }
.ico { width: 16px; height: 16px; flex: none; vertical-align: -3px; }

/* ---------- Topbar ---------- */
.topbar { background: var(--surface); border-bottom: 1px solid var(--border); }
.topbar-in { display: flex; align-items: center; justify-content: space-between; gap: 12px; min-height: 56px; }
.brand { display: flex; align-items: center; gap: 10px; min-width: 0; }
.logo { width: 32px; height: 32px; border-radius: 9px; display: grid; place-items: center; background: var(--accent); color: var(--on-accent); flex: none; }
.logo .ico { width: 18px; height: 18px; }
.brand-txt { min-width: 0; }
.brand-name { font-weight: 700; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.brand-sub { font-size: 12px; color: var(--muted); }
.icon-btn { width: 36px; height: 36px; border-radius: 9px; border: 1px solid var(--border); background: var(--surface-2); display: grid; place-items: center; cursor: pointer; flex: none; }
.icon-btn:hover { border-color: var(--border-strong); }
.i-moon { display: none; }
:root[data-theme='dark'] .i-sun { display: none; }
:root[data-theme='dark'] .i-moon { display: block; }

main.wrap { padding-top: 20px; padding-bottom: 48px; }
.card { background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius); box-shadow: var(--shadow); padding: 16px 18px; min-width: 0; }
.card.flush { padding: 0; overflow: hidden; }
.card-h { display: flex; align-items: baseline; justify-content: space-between; gap: 10px; margin-bottom: 12px; flex-wrap: wrap; }
.card-h h2 { font-size: 15px; }
.grid2 { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1.35fr); gap: 16px; margin-top: 16px; }

/* ---------- Banner ---------- */
.banner { display: flex; gap: 12px; align-items: flex-start; padding: 12px 16px; border-radius: var(--radius); margin-bottom: 16px;
  border: 1px solid var(--warn); background: var(--surface-3); background: color-mix(in srgb, var(--warn) 12%, var(--surface)); }
.banner .ico { width: 20px; height: 20px; color: var(--warn); margin-top: 1px; }
.banner.err { border-color: var(--fail); background: color-mix(in srgb, var(--fail) 10%, var(--surface)); }
.banner.err .ico { color: var(--fail); }

/* ---------- Hero ---------- */
.hero { display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 20px; align-items: center; padding: 20px 22px; }
.hero-kicker { font-size: 12px; font-weight: 600; letter-spacing: .04em; text-transform: uppercase; color: var(--muted); }
.hero-title { font-size: 22px; line-height: 1.3; margin: 2px 0 8px; word-break: break-word; }
.hero-pills { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; margin-bottom: 14px; }
.meta-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(190px, 1fr)); gap: 10px 18px; }
.mi-l { font-size: 11.5px; color: var(--muted); text-transform: uppercase; letter-spacing: .03em; }
.mi-v { font-weight: 550; word-break: break-word; }
.hero-score { text-align: center; }
.ring { position: relative; width: 132px; height: 132px; }
.ring svg { width: 132px; height: 132px; transform: rotate(-90deg); }
.ring .trk { stroke: var(--surface-3); }
.ring .val { transition: stroke-dasharray .6s ease; }
.ring-num { position: absolute; inset: 0; display: grid; place-items: center; align-content: center; }
.ring-num b { font-size: 34px; line-height: 1; font-weight: 750; }
.ring-num span { font-size: 11px; color: var(--muted); margin-top: 4px; }
.score-lbl { margin-top: 6px; font-weight: 650; font-size: 13px; }
.h-good { color: var(--good); stroke: var(--good); }
.h-ok { color: var(--ok); stroke: var(--ok); }
.h-meh { color: var(--meh); stroke: var(--meh); }
.h-bad { color: var(--bad); stroke: var(--bad); }

/* ---------- Count cards ---------- */
.counts { display: grid; grid-template-columns: repeat(7, minmax(0, 1fr)); gap: 12px; margin-top: 16px; }
.count { text-align: left; background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius); box-shadow: var(--shadow); padding: 12px 14px;
  cursor: pointer; position: relative; overflow: hidden; display: flex; flex-direction: column; gap: 2px; }
.count::before { content: ''; position: absolute; left: 0; top: 0; bottom: 0; width: 4px; background: var(--c, var(--border-strong)); }
.count:hover { border-color: var(--border-strong); }
.count.on { outline: 2px solid var(--c, var(--accent)); outline-offset: -1px; }
.count-v { font-size: 26px; font-weight: 750; line-height: 1.1; color: var(--c, var(--text)); }
.count.c-total .count-v { color: var(--text); }
.count-l { font-size: 12.5px; color: var(--text-2); font-weight: 600; }
.count-p { font-size: 11.5px; color: var(--muted); }
.count.zero .count-v { color: var(--muted); }

/* ---------- Status & severity colors ---------- */
.st-passed { --c: var(--pass); } .st-warning { --c: var(--warn); } .st-failed { --c: var(--fail); }
.st-error { --c: var(--crash); } .st-skipped { --c: var(--skip); } .st-cancelled { --c: var(--cancel); }
.st-pending { --c: var(--cancel); } .st-running { --c: var(--run); }
.sev-c-4 { --c: var(--sev-4); } .sev-c-3 { --c: var(--sev-3); } .sev-c-2 { --c: var(--sev-2); } .sev-c-1 { --c: var(--sev-1); } .sev-c-0 { --c: var(--sev-0); }
.sev-bg-4 { background: var(--sev-4); } .sev-bg-3 { background: var(--sev-3); } .sev-bg-2 { background: var(--sev-2); } .sev-bg-1 { background: var(--sev-1); } .sev-bg-0 { background: var(--sev-0); }

.pill { display: inline-flex; align-items: center; gap: 5px; padding: 2px 10px; border-radius: 999px; font-size: 12px; font-weight: 650; white-space: nowrap;
  color: var(--c); background: var(--surface-3); background: color-mix(in srgb, var(--c) 14%, transparent); }
.pill::before { content: ''; width: 6px; height: 6px; border-radius: 50%; background: var(--c); }
.sev { display: inline-block; padding: 1px 8px; border-radius: 6px; font-size: 11.5px; font-weight: 700; letter-spacing: .02em; white-space: nowrap;
  color: var(--c); background: var(--surface-3); background: color-mix(in srgb, var(--c) 15%, transparent); }
.sev.sev-c-4 { color: #fff; background: var(--sev-4); }
.badge-new { display: inline-block; padding: 0 6px; border-radius: 5px; font-size: 10.5px; font-weight: 800; letter-spacing: .05em; color: #fff; background: var(--accent); margin-right: 6px; vertical-align: 1px; }
.tag { display: inline-block; padding: 0 7px; border-radius: 5px; font-size: 11.5px; font-weight: 600; color: var(--text-2); background: var(--surface-3); margin-left: 6px; white-space: nowrap; }
.tag-warn { color: var(--warn); background: color-mix(in srgb, var(--warn) 14%, transparent); }
.tag-bad { color: var(--fail); background: color-mix(in srgb, var(--fail) 12%, transparent); }
.st-ico { width: 22px; height: 22px; border-radius: 50%; display: inline-grid; place-items: center; flex: none;
  color: var(--c); background: var(--surface-3); background: color-mix(in srgb, var(--c) 15%, transparent); }
.st-ico .ico { width: 13px; height: 13px; vertical-align: 0; }
.sev-dot { display: inline-block; width: 8px; height: 8px; border-radius: 50%; margin-right: 6px; vertical-align: 1px; }

/* ---------- Severity card ---------- */
.sevbar { display: flex; height: 14px; border-radius: 7px; overflow: hidden; background: var(--surface-3); gap: 2px; }
.sevbar .seg { display: block; height: 100%; min-width: 4px; }
.sev-legend { display: grid; grid-template-columns: repeat(5, minmax(0, 1fr)); gap: 8px; margin-top: 14px; }
.sev-lg { display: flex; flex-direction: column; align-items: flex-start; gap: 2px; padding: 8px 10px; border-radius: var(--radius-sm); border: 1px solid var(--border);
  background: var(--surface-2); cursor: pointer; text-align: left; }
.sev-lg:hover { border-color: var(--border-strong); }
.sev-lg span { display: flex; align-items: center; gap: 6px; font-size: 12px; font-weight: 600; color: var(--text-2); }
.sev-lg i { width: 9px; height: 9px; border-radius: 3px; display: inline-block; }
.sev-lg b { font-size: 20px; line-height: 1.1; }
.sev-lg.zero b { color: var(--muted); }

/* ---------- Diff ---------- */
.diff-stats { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 10px; }
.dstat { padding: 10px 12px; border-radius: var(--radius-sm); border: 1px solid var(--border); background: var(--surface-2); text-align: left; cursor: default; }
button.dstat { cursor: pointer; }
button.dstat:hover { border-color: var(--border-strong); }
.dstat b { display: block; font-size: 22px; line-height: 1.15; color: var(--c); }
.dstat span { font-size: 12px; color: var(--text-2); font-weight: 600; }
.d-new { --c: var(--fail); } .d-fixed { --c: var(--pass); } .d-persist { --c: var(--warn); }
.diff-lists { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: 12px; margin-top: 12px; }
.diff-lists h3 { font-size: 12.5px; color: var(--text-2); margin-bottom: 6px; }
.chip-list { display: flex; flex-wrap: wrap; gap: 6px; margin: 0; padding: 0; list-style: none; }
.goto { border: 1px solid var(--border); background: var(--surface-2); border-radius: 6px; padding: 2px 8px; font-size: 12px; cursor: pointer; text-align: left; max-width: 100%;
  overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.goto:hover { border-color: var(--accent); color: var(--accent); }
.goto.bad { border-color: color-mix(in srgb, var(--fail) 40%, var(--border)); }
.goto.good { border-color: color-mix(in srgb, var(--pass) 40%, var(--border)); }
details.fold { margin-top: 12px; border-top: 1px dashed var(--border); padding-top: 10px; }
details.fold > summary { cursor: pointer; font-weight: 600; font-size: 13px; color: var(--text-2); }
.fixed-list { margin-top: 8px; display: flex; flex-direction: column; gap: 6px; max-height: 320px; overflow: auto; }
.fx { display: flex; gap: 8px; align-items: baseline; font-size: 12.5px; padding: 4px 0; border-bottom: 1px solid var(--border); }
.fx-t { flex: 1; min-width: 0; }
.fx-t s { color: var(--text-2); }
.fx-t div { color: var(--muted); font-size: 11.5px; word-break: break-all; }

/* ---------- Tabs & toolbar ---------- */
.tabs { display: flex; gap: 4px; margin-top: 22px; border-bottom: 1px solid var(--border); overflow-x: auto; }
.tab { border: 0; background: none; padding: 10px 14px; font-weight: 650; color: var(--muted); cursor: pointer; border-bottom: 2px solid transparent; margin-bottom: -1px; white-space: nowrap; }
.tab:hover { color: var(--text); }
.tab.on { color: var(--accent); border-bottom-color: var(--accent); }
.tab-n { display: inline-block; min-width: 20px; padding: 0 6px; border-radius: 999px; background: var(--surface-3); color: var(--text-2); font-size: 11.5px; margin-left: 4px; }
.toolbar { position: sticky; top: 0; z-index: 20; background: var(--bg); padding: 12px 0 8px; display: flex; flex-direction: column; gap: 8px; }
.tb-row { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 14px; }
.search { flex: 1 1 280px; display: flex; align-items: center; gap: 8px; background: var(--surface); border: 1px solid var(--border); border-radius: 10px; padding: 0 12px; min-width: 0; }
.search:focus-within { border-color: var(--accent); box-shadow: 0 0 0 3px color-mix(in srgb, var(--accent) 20%, transparent); }
.search .ico { color: var(--muted); }
.search input { flex: 1; min-width: 0; border: 0; outline: 0; background: none; color: var(--text); font: inherit; padding: 9px 0; }
.chips { display: flex; flex-wrap: wrap; align-items: center; gap: 6px; }
.chips-l { font-size: 11.5px; font-weight: 700; color: var(--muted); text-transform: uppercase; letter-spacing: .04em; margin-right: 2px; }
.chip { display: inline-flex; align-items: center; gap: 6px; border: 1px solid var(--border); background: var(--surface); border-radius: 999px; padding: 3px 10px; font-size: 12.5px;
  font-weight: 600; cursor: pointer; color: var(--muted); user-select: none; }
.chip .dot { width: 8px; height: 8px; border-radius: 50%; background: var(--c, var(--muted)); opacity: .35; }
.chip b { font-weight: 700; font-size: 11.5px; color: var(--muted); }
.chip.on { color: var(--text); border-color: var(--c, var(--accent)); background: var(--surface-3); background: color-mix(in srgb, var(--c, var(--accent)) 10%, var(--surface)); }
.chip.on .dot { opacity: 1; }
.chip.on b { color: var(--text-2); }
.chip-sm { padding: 1px 8px; font-size: 12px; }
.switch { display: inline-flex; align-items: center; gap: 8px; font-weight: 600; font-size: 13px; cursor: pointer; user-select: none; }
.switch input { position: absolute; opacity: 0; pointer-events: none; }
.switch .sw { width: 34px; height: 20px; border-radius: 999px; background: var(--border-strong); position: relative; transition: background .15s; flex: none; }
.switch .sw::after { content: ''; position: absolute; top: 2px; left: 2px; width: 16px; height: 16px; border-radius: 50%; background: #fff; transition: transform .15s; box-shadow: 0 1px 2px rgba(0,0,0,.25); }
.switch input:checked + .sw { background: var(--accent); }
.switch input:checked + .sw::after { transform: translateX(14px); }
.switch input:focus-visible + .sw { outline: 2px solid var(--accent); outline-offset: 2px; }
.switch.disabled { opacity: .5; cursor: not-allowed; }
.tb-actions { display: flex; gap: 6px; flex-wrap: wrap; }
.btn { border: 1px solid var(--border); background: var(--surface); border-radius: 8px; padding: 6px 12px; font-size: 13px; font-weight: 600; cursor: pointer; white-space: nowrap; }
.btn:hover { border-color: var(--border-strong); }
.btn:disabled { opacity: .5; cursor: default; }
.btn-ghost { background: transparent; }
.btn.more { display: block; margin: 12px auto; }
.link { border: 0; background: none; padding: 0; color: var(--accent); cursor: pointer; font-weight: 600; }
.result-line { font-size: 12.5px; color: var(--muted); padding: 2px 2px 10px; }
.result-line b { color: var(--text); }

/* ---------- Suites ---------- */
.suite { background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius); box-shadow: var(--shadow); margin-bottom: 10px; overflow: hidden; }
.suite-h { width: 100%; display: flex; align-items: center; gap: 10px; padding: 12px 16px; border: 0; background: none; cursor: pointer; text-align: left; flex-wrap: wrap; }
.suite-h:hover { background: var(--surface-2); }
.chev { transition: transform .15s; color: var(--muted); }
.suite.open > .suite-h .chev { transform: rotate(90deg); }
.suite-name { font-weight: 700; font-size: 15px; flex: 1 1 220px; min-width: 0; }
.suite-name small { display: block; font-weight: 500; font-size: 12px; color: var(--muted); }
.suite-mini { display: flex; gap: 4px; }
.mini { font-size: 11.5px; font-weight: 700; color: var(--c); padding: 0 7px; border-radius: 999px; background: var(--surface-3); background: color-mix(in srgb, var(--c) 13%, transparent); }
.suite-meta { font-size: 12.5px; color: var(--muted); white-space: nowrap; }
.suite-b { border-top: 1px solid var(--border); padding: 12px 16px 14px; }
.suite-desc { margin: 0 0 10px; color: var(--text-2); font-size: 13px; }
.note { padding: 8px 12px; border-radius: var(--radius-sm); background: var(--surface-2); border: 1px solid var(--border); margin-bottom: 10px; font-size: 13px; }
.empty-sm { padding: 14px; text-align: center; color: var(--muted); font-size: 13px; }
.empty { padding: 48px 16px; text-align: center; color: var(--muted); }
.empty .ico { width: 36px; height: 36px; color: var(--border-strong); display: block; margin: 0 auto 10px; }
.empty b { display: block; color: var(--text); font-size: 15px; margin-bottom: 4px; }

.cases { border: 1px solid var(--border); border-radius: var(--radius-sm); overflow: hidden; }
.case + .case { border-top: 1px solid var(--border); }
.case-row { display: grid; grid-template-columns: 26px minmax(160px, 2.3fr) minmax(80px, 1fr) 78px 56px minmax(140px, 3fr); gap: 12px; align-items: center; padding: 8px 12px; }
.case-row.head { background: var(--surface-2); font-size: 11.5px; font-weight: 700; color: var(--muted); text-transform: uppercase; letter-spacing: .03em; border-bottom: 1px solid var(--border); }
.case-row[data-ck] { cursor: pointer; }
.case-row[data-ck]:hover { background: var(--surface-2); }
.case-row[data-ck]:focus-visible { outline: 2px solid var(--accent); outline-offset: -2px; }
.case.open > .case-row { background: var(--surface-2); }
.case.flash > .case-row { animation: flash 1.6s ease; }
@keyframes flash { 0%, 40% { background: color-mix(in srgb, var(--accent) 22%, var(--surface)); } 100% { background: var(--surface-2); } }
.c-name { min-width: 0; font-weight: 600; word-break: break-word; }
.c-name small { display: block; font-weight: 400; color: var(--muted); font-size: 11.5px; word-break: break-all; }
.c-cat { font-size: 12.5px; color: var(--text-2); min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.c-dur, .c-iss { font-size: 12.5px; font-variant-numeric: tabular-nums; white-space: nowrap; }
.c-msg { font-size: 12.5px; color: var(--text-2); min-width: 0; overflow: hidden; text-overflow: ellipsis; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; word-break: break-word; }
.case-d { border-top: 1px dashed var(--border); background: var(--surface-2); }

/* ---------- Case detail ---------- */
.detail { padding: 14px 16px 16px 50px; display: flex; flex-direction: column; gap: 14px; }
.desc { margin: 0; color: var(--text-2); }
.d-meta { display: flex; flex-wrap: wrap; gap: 6px 18px; font-size: 12.5px; color: var(--text-2); }
.d-meta b { color: var(--muted); font-weight: 600; margin-right: 4px; }
.d-meta .tag { margin-left: 0; margin-right: 4px; }
.msg { display: flex; gap: 10px; align-items: flex-start; padding: 10px 12px; border-radius: var(--radius-sm); background: var(--surface); border: 1px solid var(--border); border-left: 3px solid var(--c); white-space: pre-wrap; word-break: break-word; }
.d-sec h4 { font-size: 12px; text-transform: uppercase; letter-spacing: .04em; color: var(--muted); margin-bottom: 8px; display: flex; align-items: center; gap: 6px; flex-wrap: wrap; }
.d-sec h4 .n { font-size: 11px; background: var(--surface-3); color: var(--text-2); border-radius: 999px; padding: 0 7px; letter-spacing: 0; }
.d-sec h4 .tag { margin-left: 0; text-transform: none; letter-spacing: 0; }
details.d-sec > summary { cursor: pointer; list-style: none; }
details.d-sec > summary::-webkit-details-marker { display: none; }
details.d-sec > summary h4::before { content: '\25B8'; font-size: 11px; transition: transform .15s; display: inline-block; }
details.d-sec[open] > summary h4::before { transform: rotate(90deg); }

.timeline { list-style: none; margin: 0; padding: 0; position: relative; }
.tl { display: grid; grid-template-columns: 16px minmax(0, 1fr) auto; gap: 10px; align-items: start; padding: 4px 0; position: relative; }
.tl::before { content: ''; position: absolute; left: 7px; top: 0; bottom: 0; width: 2px; background: var(--border); }
.tl:first-child::before { top: 12px; }
.tl:last-child::before { bottom: calc(100% - 12px); }
.tl-dot { width: 12px; height: 12px; border-radius: 50%; background: var(--surface); border: 3px solid var(--c); margin: 4px 0 0 2px; position: relative; z-index: 1; }
.tl-name { font-weight: 600; font-size: 13px; word-break: break-word; }
.tl-detail { font-size: 12px; color: var(--muted); white-space: pre-wrap; word-break: break-word; }
.tl-time { font-size: 12px; color: var(--muted); text-align: right; font-variant-numeric: tabular-nums; white-space: nowrap; }
.tl-time b { display: block; color: var(--text-2); font-weight: 600; }

.issues { display: flex; flex-direction: column; gap: 10px; }
.issue { background: var(--surface); border: 1px solid var(--border); border-left: 4px solid var(--c); border-radius: var(--radius-sm); padding: 10px 12px; display: flex; flex-direction: column; gap: 8px; min-width: 0; }
.issue-h { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
.issue-title { font-weight: 650; flex: 1 1 200px; min-width: 0; word-break: break-word; }
.issue-msg { white-space: pre-wrap; word-break: break-word; color: var(--text-2); }
.btn-copy { display: inline-flex; align-items: center; gap: 5px; border: 1px solid var(--border); background: var(--surface-2); border-radius: 7px; padding: 3px 9px; font-size: 12px; font-weight: 600; cursor: pointer; white-space: nowrap; color: var(--text-2); }
.btn-copy:hover { border-color: var(--accent); color: var(--accent); }
.btn-copy.icon-only { padding: 4px 6px; }
.kv { display: grid; grid-template-columns: max-content minmax(0, 1fr); gap: 4px 12px; margin: 0; font-size: 12.5px; }
.kv dt { color: var(--muted); font-weight: 600; }
.kv dd { margin: 0; min-width: 0; word-break: break-word; }
.lbl { font-size: 11.5px; font-weight: 700; color: var(--muted); text-transform: uppercase; letter-spacing: .03em; margin-bottom: 4px; }
.ea { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: 8px; }
.ea > div { border-radius: 7px; padding: 8px 10px; border: 1px solid var(--border); min-width: 0; }
.ea-e { background: color-mix(in srgb, var(--pass) 7%, var(--surface)); }
.ea-a { background: color-mix(in srgb, var(--fail) 7%, var(--surface)); }
pre.steps { background: var(--surface-2); border: 1px solid var(--border); border-radius: 7px; padding: 8px 10px; }
details.stack > summary, details.log-st > summary { cursor: pointer; font-size: 12px; color: var(--muted); font-weight: 600; }
details.stack pre { margin-top: 6px; background: var(--surface-2); border: 1px solid var(--border); border-radius: 7px; padding: 8px 10px; max-height: 360px; overflow: auto; font-size: 11.5px; }
.issue-id { font-size: 11px; color: var(--muted); }
.issue-id code { font-size: 11px; }

.tbl-wrap { overflow-x: auto; -webkit-overflow-scrolling: touch; }
.tbl { width: 100%; border-collapse: collapse; font-size: 13px; }
.tbl th, .tbl td { padding: 7px 10px; border-bottom: 1px solid var(--border); text-align: left; vertical-align: top; }
.tbl th { font-size: 11.5px; text-transform: uppercase; letter-spacing: .03em; color: var(--muted); background: var(--surface-2); font-weight: 700; white-space: nowrap; }
.tbl td.num, .tbl th.num { text-align: right; font-variant-numeric: tabular-nums; white-space: nowrap; }
.tbl tr.bad td { background: color-mix(in srgb, var(--fail) 7%, transparent); }
.tbl tr.bad td.num:nth-child(2) { color: var(--fail); font-weight: 700; }
.unit { color: var(--muted); font-size: 11.5px; }
.detail .tbl-wrap { border: 1px solid var(--border); border-radius: var(--radius-sm); background: var(--surface); }
.detail .tbl tr:last-child td { border-bottom: 0; }

.thumbs { display: grid; grid-template-columns: repeat(auto-fill, minmax(140px, 1fr)); gap: 10px; }
.thumb { display: block; border: 1px solid var(--border); border-radius: var(--radius-sm); overflow: hidden; background: var(--surface); text-decoration: none; color: var(--text-2); position: relative; max-width: 260px; }
.thumb img { display: block; width: 100%; height: 110px; object-fit: contain; background: repeating-conic-gradient(var(--surface-3) 0% 25%, var(--surface) 0% 50%) 50% / 16px 16px; }
.thumb:hover { border-color: var(--accent); }
.thumb-cap { display: block; font-size: 11.5px; padding: 4px 8px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.thumb.broken img { display: none; }
.thumb.broken::before { content: 'Không tìm thấy ảnh'; display: grid; place-items: center; height: 110px; font-size: 12px; color: var(--muted); background: var(--surface-2); }
.files { list-style: none; margin: 8px 0 0; padding: 0; display: flex; flex-direction: column; gap: 6px; }
.files li { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; font-size: 13px; }
.files li .tag { margin-left: 0; }
.files code { font-size: 11px; }

.log-body { margin-top: 8px; }
.log-f { margin-bottom: 8px; }
.log-list { border: 1px solid var(--border); border-radius: var(--radius-sm); background: var(--surface); max-height: 480px; overflow: auto; font-family: var(--mono); font-size: 12px; }
.log { display: grid; grid-template-columns: 58px 74px minmax(0, 1fr); gap: 8px; padding: 4px 10px; border-bottom: 1px solid var(--border); }
.log:last-child { border-bottom: 0; }
.log-t { color: var(--muted); text-align: right; font-variant-numeric: tabular-nums; }
.log-type { font-weight: 700; }
.log-msg { white-space: pre-wrap; word-break: break-word; }
.l-error { background: color-mix(in srgb, var(--fail) 7%, transparent); }
.l-error .log-type { color: var(--fail); }
.l-warning { background: color-mix(in srgb, var(--warn) 8%, transparent); }
.l-warning .log-type { color: var(--warn); }
.l-log .log-type { color: var(--muted); }
details.log-st pre { font-size: 11px; color: var(--muted); margin-top: 4px; }

/* ---------- Issues tab ---------- */
.issues-tbl th.sortable { cursor: pointer; user-select: none; }
.issues-tbl th.sortable:hover { color: var(--text); }
.issues-tbl th.sorted { color: var(--accent); }
.sort-ind { font-size: 9px; margin-left: 4px; }
.issues-tbl tr.irow { cursor: pointer; }
.issues-tbl tr.irow:hover td { background: var(--surface-2); }
.issues-tbl tr.irow:focus-visible { outline: 2px solid var(--accent); outline-offset: -2px; }
.issues-tbl tr.irow.open td { background: var(--surface-2); border-bottom-color: transparent; }
.issues-tbl tr.idetail > td { background: var(--surface-2); padding: 0 12px 12px; }
.issues-tbl td { min-width: 90px; }
.it-title { font-weight: 600; word-break: break-word; min-width: 180px; }
.it-msg { font-size: 12px; color: var(--muted); word-break: break-word; }
.it-sub { font-size: 12px; color: var(--muted); display: flex; align-items: center; gap: 5px; margin-top: 2px; word-break: break-word; }
.it-sub .st-ico { width: 16px; height: 16px; }
.it-sub .st-ico .ico { width: 10px; height: 10px; }
.it-loc code { font-size: 11.5px; }
.act-col { width: 1%; white-space: nowrap; }

/* ---------- Env / footer ---------- */
.kvt { width: 100%; border-collapse: collapse; font-size: 13px; }
.kvt th, .kvt td { text-align: left; padding: 6px 0; border-bottom: 1px solid var(--border); vertical-align: top; }
.kvt th { width: 42%; color: var(--muted); font-weight: 600; padding-right: 12px; }
.kvt td { word-break: break-word; }
.kvt tr:last-child th, .kvt tr:last-child td { border-bottom: 0; }
.foot { margin-top: 28px; font-size: 12px; color: var(--muted); text-align: center; line-height: 1.8; }
.foot a { margin: 0 4px; }

/* ---------- Lightbox & toast ---------- */
.lb { position: fixed; inset: 0; z-index: 100; background: rgba(5, 8, 12, .86); display: flex; align-items: center; justify-content: center; padding: 48px 56px; }
.lb figure { margin: 0; max-width: 100%; max-height: 100%; display: flex; flex-direction: column; align-items: center; gap: 10px; }
.lb img { max-width: 100%; max-height: calc(100vh - 140px); object-fit: contain; border-radius: 6px; box-shadow: var(--shadow-lg); background: #fff; }
.lb figcaption { color: #e5eaf1; font-size: 13px; text-align: center; word-break: break-word; }
.lb figcaption a { color: #9cb6ff; margin-left: 8px; }
.lb-btn { position: absolute; border: 0; background: rgba(255, 255, 255, .12); color: #fff; width: 44px; height: 44px; border-radius: 50%; cursor: pointer; font-size: 22px; line-height: 1; display: grid; place-items: center; }
.lb-btn:hover { background: rgba(255, 255, 255, .24); }
.lb-x { top: 14px; right: 14px; }
.lb-prev { left: 12px; top: 50%; margin-top: -22px; }
.lb-next { right: 12px; top: 50%; margin-top: -22px; }
.toast { position: fixed; left: 50%; bottom: 24px; transform: translate(-50%, 20px); background: var(--text); color: var(--bg); padding: 10px 16px; border-radius: 10px; font-weight: 600;
  font-size: 13px; opacity: 0; pointer-events: none; transition: opacity .2s, transform .2s; z-index: 120; max-width: calc(100vw - 32px); box-shadow: var(--shadow-lg); }
.toast.show { opacity: 1; transform: translate(-50%, 0); }
.toast.err { background: var(--fail); color: #fff; }

/* ---------- Responsive ---------- */
@media (max-width: 1080px) {
  .counts { grid-template-columns: repeat(4, minmax(0, 1fr)); }
  .grid2 { grid-template-columns: minmax(0, 1fr); }
}
@media (max-width: 760px) {
  .hero { grid-template-columns: minmax(0, 1fr); padding: 16px; }
  .hero-score { order: -1; display: flex; align-items: center; gap: 14px; text-align: left; }
  .ring, .ring svg { width: 96px; height: 96px; }
  .ring-num b { font-size: 26px; }
  .counts { grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 8px; }
  .count { padding: 10px 12px; }
  .count-v { font-size: 22px; }
  .sev-legend { grid-template-columns: repeat(3, minmax(0, 1fr)); }
  .diff-lists, .ea { grid-template-columns: minmax(0, 1fr); }
  .case-row { grid-template-columns: 26px minmax(0, 1fr) auto; gap: 4px 10px; }
  .case-row.head { display: none; }
  .case-row .c-cat { grid-column: 2; grid-row: 2; }
  .case-row .c-dur { grid-column: 3; grid-row: 1; }
  .case-row .c-iss { grid-column: 3; grid-row: 2; }
  .case-row .c-msg { grid-column: 2 / span 2; grid-row: 3; }
  .detail { padding: 12px; }
  .suite-meta { width: 100%; padding-left: 26px; white-space: normal; }
  .log { grid-template-columns: 48px minmax(0, 1fr); }
  .log-type { display: none; }
  .lb { padding: 56px 8px; }
}
@media print {
  .toolbar, .tabs, .icon-btn, .btn-copy, .btn.more { display: none !important; }
  body { background: #fff; }
  .card, .suite, .count { box-shadow: none; }
}
";

        #endregion

        #region JS_CORE

        /// <summary>JS phần 1: hằng số, tiện ích format, đọc dữ liệu, index case/issue, bộ lọc, mảnh HTML dùng chung.</summary>
        internal const string JS_CORE = @"(function () {
  'use strict';
  var D = document;

  // ---------- Hằng số ----------
  const PAGE_ISSUES = 200;
  const PAGE_CASES = 300;
  const PAGE_LOGS = 300;
  const LIST_PREVIEW = 40;
  const MSG_MAX = 180;
  const SEARCH_DEBOUNCE_MS = 160;
  const TOAST_MS = 2200;
  const RING_RADIUS = 52;
  const SCORE_GOOD = 90;
  const SCORE_OK = 70;
  const SCORE_MEH = 40;
  const COMMIT_SHORT = 8;
  const THEME_KEY = 'ezg-autotest-theme';
  const ST = { PENDING: 0, RUNNING: 1, PASSED: 2, WARNING: 3, FAILED: 4, ERROR: 5, SKIPPED: 6, CANCELLED: 7 };
  const STATUS_KEYS = ['pending', 'running', 'passed', 'warning', 'failed', 'error', 'skipped', 'cancelled'];
  const FILTER_STATUSES = [ST.FAILED, ST.ERROR, ST.WARNING, ST.PASSED, ST.SKIPPED, ST.CANCELLED];
  const SEV_ORDER = [4, 3, 2, 1, 0];
  const SEV_MAX = 4;
  const MODE_NAMES = ['Edit', 'Play', 'Device'];
  const ESC_MAP = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '\u0022': '&quot;', '\'': '&#39;' };

  // ---------- Tiện ích ----------
  function $(sel, root) { return (root || D).querySelector(sel); }
  function $$(sel, root) { return Array.prototype.slice.call((root || D).querySelectorAll(sel)); }
  function arr(v) { return Array.isArray(v) ? v : []; }
  function str(v) { return v == null ? '' : String(v); }
  function esc(v) { return str(v).replace(/[&<>\u0022']/g, function (c) { return ESC_MAP[c]; }); }
  function trunc(v, n) { const s = str(v).replace(/\s+/g, ' ').trim(); return s.length > n ? s.slice(0, n - 1) + '…' : s; }
  function pad(n) { return (n < 10 ? '0' : '') + n; }
  // Chuẩn hoá để tìm kiếm không phân biệt hoa/thường và dấu tiếng Việt ('loi' khớp 'Lỗi').
  function fold(v) {
    let s = str(v).toLowerCase();
    if (s.normalize) s = s.normalize('NFD').replace(/[\u0300-\u036f]/g, '');
    return s.replace(/đ/g, 'd');
  }

  function readJson(id) {
    const el = D.getElementById(id);
    if (!el) return null;
    const raw = el.textContent || '';
    try { return JSON.parse(raw); } catch (e) {
      try { return JSON.parse(raw.replace(/:\s*-?(NaN|Infinity)\b/g, ':null')); } catch (e2) { return null; }
    }
  }

  function parseDate(iso) {
    const s = str(iso).trim();
    if (!s) return null;
    const d = new Date(s.replace(/(\.\d{3})\d+/, '$1'));
    return isNaN(d.getTime()) ? null : d;
  }
  function fmtDate(iso) {
    const d = parseDate(iso);
    if (!d) return str(iso);
    return pad(d.getDate()) + '/' + pad(d.getMonth() + 1) + '/' + d.getFullYear() + ' ' +
      pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':' + pad(d.getSeconds());
  }
  function fmtDur(ms) {
    ms = Number(ms) || 0;
    if (ms < 1000) return Math.round(ms) + ' ms';
    const sec = ms / 1000;
    if (sec < 60) return (Math.round(sec * 10) / 10) + 's';
    const whole = Math.floor(sec);
    const h = Math.floor(whole / 3600);
    const m = Math.floor((whole % 3600) / 60);
    const s = whole % 60;
    return h > 0 ? h + 'h ' + m + 'm' : m + 'm ' + s + 's';
  }
  function fmtSecs(t) { const n = Number(t); return isFinite(n) ? n.toFixed(2) + 's' : ''; }
  function fmtNum(v) {
    if (v == null || v === '') return '—';
    const n = Number(v);
    if (!isFinite(n)) return '—';
    if (Math.abs(n) >= 1000) return String(Math.round(n * 10) / 10);
    return String(Number(n.toFixed(3)));
  }
  function fileName(p) { const s = str(p).replace(/\\/g, '/'); return s.slice(s.lastIndexOf('/') + 1); }
  function assetUrl(p) {
    let s = str(p).replace(/\\/g, '/');
    if (!s) return '';
    if (/^(https?|file|data):/i.test(s)) return s;
    let prefix = '';
    if (/^[a-zA-Z]:\//.test(s)) { prefix = 'file:///' + s.slice(0, 2); s = s.slice(2); }
    else if (s.charAt(0) === '/') prefix = 'file://';
    return prefix + s.split('/').map(function (seg) { return encodeURIComponent(seg); }).join('/');
  }

  // ---------- Dữ liệu ----------
  const META = readJson('meta') || {};
  const DATA = readJson('data');
  const REP = DATA || {};
  const STATUS_LABELS = arr(META.statusLabels).length === STATUS_KEYS.length ? META.statusLabels :
    ['Chờ', 'Đang chạy', 'Đạt', 'Cảnh báo', 'Lỗi', 'Crash', 'Bỏ qua', 'Đã dừng'];
  const SEV_NAMES = arr(META.severityNames).length === SEV_MAX + 1 ? META.severityNames :
    ['Info', 'Minor', 'Major', 'Critical', 'Blocker'];

  function clampStatus(s) { s = s | 0; return s >= 0 && s < STATUS_KEYS.length ? s : ST.ERROR; }
  function effStatus(s) { s = clampStatus(s); return s === ST.PENDING ? ST.CANCELLED : s === ST.RUNNING ? ST.ERROR : s; }
  function sevOf(v) { v = v | 0; return v < 0 ? 0 : v > SEV_MAX ? SEV_MAX : v; }
  function label(st) { return str(STATUS_LABELS[st]); }
  function suiteName(s) { return str(s.name) || str(s.suiteId) || 'Suite'; }
  function caseName(c) { return str(c.name) || str(c.caseId) || '(không tên)'; }
  function aggregate(cases) {
    if (!cases.length) return ST.SKIPPED;
    const has = {};
    cases.forEach(function (c) { has[c._eff] = true; });
    if (has[ST.ERROR]) return ST.ERROR;
    if (has[ST.FAILED]) return ST.FAILED;
    if (has[ST.CANCELLED]) return ST.CANCELLED;
    if (has[ST.WARNING]) return ST.WARNING;
    return has[ST.PASSED] ? ST.PASSED : ST.SKIPPED;
  }

  const suites = arr(REP.suites);
  const allCases = [];
  const allIssues = [];
  const caseByKey = new Map();
  const issueByKey = new Map();
  const counts = [0, 0, 0, 0, 0, 0, 0, 0];
  const sevCounts = [0, 0, 0, 0, 0];
  suites.forEach(function (s, si) {
    s._i = si;
    s._cases = arr(s.cases);
    s._issueCount = 0;
    s._counts = [0, 0, 0, 0, 0, 0, 0, 0];
    s._cases.forEach(function (c, ci) {
      c._suite = s; c._i = ci; c._key = si + '-' + ci;
      c._issues = arr(c.issues);
      c._eff = effStatus(c.status);
      c._maxSev = -1; c._hasNew = false;
      s._counts[c._eff]++; counts[c._eff]++;
      c._issues.forEach(function (it, ii) {
        it._case = c; it._i = ii; it._key = c._key + '-' + ii; it._sev = sevOf(it.severity);
        if (it._sev > c._maxSev) c._maxSev = it._sev;
        if (it.isNew) c._hasNew = true;
        sevCounts[it._sev]++;
        issueByKey.set(it._key, it); allIssues.push(it);
      });
      s._issueCount += c._issues.length;
      caseByKey.set(c._key, c); allCases.push(c);
    });
    s._eff = aggregate(s._cases);
    s._vis = s._cases;
  });
  const overall = aggregate(allCases);
  const diff = REP.diff || {};
  const hasPrev = !!str(diff.previousRunId);
  const summary = REP.summary || null;
  const health = summary && typeof summary.healthScore === 'number' ? Math.max(0, Math.min(100, summary.healthScore)) : 100;

  // ---------- Trạng thái UI ----------
  const state = {
    tab: 'suites', q: '', onlyNew: false,
    statuses: new Set(FILTER_STATUSES), sevs: new Set(SEV_ORDER),
    sortKey: 'severity', sortDir: -1, issueLimit: PAGE_ISSUES
  };
  const openSuites = new Set();
  const openCases = new Set();
  const openIssues = new Set();
  const suiteLimit = new Map();
  const logState = new Map();
  const stale = { suites: true, issues: true, env: true };

  // ---------- Bộ lọc ----------
  function caseText(c) {
    if (c._t == null) c._t = fold([c.name, c.caseId, c.category, c.message, c.description, c.device, arr(c.tags).join(' '),
      suiteName(c._suite), c._suite.suiteId, label(c._eff)].join('\n'));
    return c._t;
  }
  function issueText(i) {
    if (i._t == null) i._t = fold([i.title, i.message, i.category, i.location, i.objectPath, i.id, i.expected, i.actual,
      SEV_NAMES[i._sev]].join('\n'));
    return i._t;
  }
  function issueFiltersActive() { return state.sevs.size < SEV_ORDER.length || state.onlyNew; }
  function filtersDefault() { return !state.q && !issueFiltersActive() && state.statuses.size === FILTER_STATUSES.length; }
  function caseHit(c) { return !state.q || caseText(c).indexOf(state.q) >= 0; }
  function issuePass(i, hit) {
    if (!state.sevs.has(i._sev)) return false;
    if (state.onlyNew && !i.isNew) return false;
    if (hit || !state.q) return true;
    return issueText(i).indexOf(state.q) >= 0;
  }
  function caseVisible(c) {
    if (!state.statuses.has(c._eff)) return false;
    const hit = caseHit(c);
    if (!issueFiltersActive()) {
      if (hit) return true;
      return c._issues.some(function (i) { return issuePass(i, false); });
    }
    return c._issues.some(function (i) { return issuePass(i, hit); });
  }
  function visibleIssues() {
    const out = [];
    allIssues.forEach(function (i) {
      const c = i._case;
      if (state.statuses.has(c._eff) && issuePass(i, caseHit(c))) out.push(i);
    });
    return out;
  }

  // ---------- Mảnh HTML dùng chung ----------
  const ICON = {
    passed: `<path d='M3.5 8.5l3 3 6-7'/>`,
    warning: `<path d='M8 3.5v5.5'/><path d='M8 12.3v.2'/>`,
    failed: `<path d='M4.5 4.5l7 7M11.5 4.5l-7 7'/>`,
    error: `<path d='M9.2 1.8L4 9h4l-1.2 5.2L12 7H8z'/>`,
    skipped: `<path d='M4 4l4.5 4L4 12'/><path d='M11.5 4v8'/>`,
    cancelled: `<rect x='4.5' y='4.5' width='7' height='7' rx='1.2'/>`,
    pending: `<circle cx='8' cy='8' r='5'/>`,
    running: `<path d='M8 3a5 5 0 1 1-5 5'/>`,
    chev: `<path d='M6 3.5L10.5 8 6 12.5'/>`,
    search: `<circle cx='7' cy='7' r='4.5'/><path d='M10.5 10.5L14 14'/>`,
    copy: `<rect x='5.5' y='5.5' width='8' height='8' rx='1.5'/><path d='M10.5 5.5V3.8c0-.7-.6-1.3-1.3-1.3H3.8c-.7 0-1.3.6-1.3 1.3v5.4c0 .7.6 1.3 1.3 1.3h1.7'/>`,
    sun: `<circle cx='8' cy='8' r='3'/><path d='M8 1.5v1.5M8 13v1.5M1.5 8H3M13 8h1.5M3.4 3.4l1 1M11.6 11.6l1 1M3.4 12.6l1-1M11.6 4.4l1-1'/>`,
    moon: `<path d='M13.5 9.5A5.5 5.5 0 0 1 6.5 2.5a5.5 5.5 0 1 0 7 7z'/>`,
    flask: `<path d='M6 1.8h4M6.8 1.8v4.1L2.9 12.6c-.5.9.1 1.9 1.1 1.9h8c1 0 1.6-1 1.1-1.9L9.2 5.9V1.8'/><path d='M4.6 9.8h6.8'/>`,
    alert: `<path d='M8 1.8l6.5 11.4H1.5z'/><path d='M8 6.2v3.2M8 11.4v.2'/>`,
    file: `<path d='M9 1.8H4.3c-.7 0-1.3.6-1.3 1.3v9.8c0 .7.6 1.3 1.3 1.3h7.4c.7 0 1.3-.6 1.3-1.3V5.8z'/><path d='M9 1.8v4h4'/>`,
    inbox: `<path d='M1.8 9.5l2-6h8.4l2 6v3.2c0 .7-.6 1.3-1.3 1.3H3.1c-.7 0-1.3-.6-1.3-1.3z'/><path d='M1.8 9.5h3.7l1 1.7h3l1-1.7h3.7'/>`,
    party: `<path d='M2.5 13.5l3-8.5 5.5 5.5z'/><path d='M9.5 2.5v1.5M13.5 6.5H12M11.8 4.2l1-1'/>`
  };
  function svg(inner, cls) {
    return `<svg class='ico${cls ? ' ' + cls : ''}' viewBox='0 0 16 16' fill='none' stroke='currentColor' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round' aria-hidden='true'>${inner}</svg>`;
  }
  function pill(st) { return `<span class='pill st-${STATUS_KEYS[st]}'>${esc(label(st))}</span>`; }
  function stIcon(st) { return `<span class='st-ico st-${STATUS_KEYS[st]}' title='${esc(label(st))}'>${svg(ICON[STATUS_KEYS[st]])}</span>`; }
  function sevChip(s) { return `<span class='sev sev-c-${s}'>${esc(SEV_NAMES[s])}</span>`; }
  function badgeNew() { return `<span class='badge-new' title='Lỗi mới so với lần chạy trước'>MỚI</span>`; }
  function copyBtn(i, iconOnly) {
    return `<button class='btn-copy${iconOnly ? ' icon-only' : ''}' data-copy='${i._key}' title='Sao chép bug dạng text để dán vào tracker'>${svg(ICON.copy)}${iconOnly ? '' : 'Sao chép bug'}</button>`;
  }
  function emptyState(title, withReset, happy) {
    return `<div class='card empty'>${svg(happy ? ICON.party : ICON.inbox)}<b>${esc(title)}</b>` +
      (withReset ? `<button class='link' data-act='reset'>Xoá bộ lọc</button>` : '') + `</div>`;
  }
  function kvTable(rows) {
    return `<table class='kvt'><tbody>${rows.map(function (r) { return `<tr><th>${esc(r[0])}</th><td>${r[2] ? r[1] : esc(r[1])}</td></tr>`; }).join('')}</tbody></table>`;
  }
  function productName() { const e = REP.env || {}; return str(e.productName) || str(e.projectName) || 'Unity project'; }
  function gitHtml(e) {
    if (!e.gitBranch && !e.gitCommit) return '';
    let h = esc(str(e.gitBranch) || '(detached)');
    if (e.gitCommit) h += ` @ <code>${esc(str(e.gitCommit).slice(0, COMMIT_SHORT))}</code>`;
    if (e.gitDirty) h += `<span class='tag tag-warn' title='Có thay đổi chưa commit lúc chạy'>dirty</span>`;
    return h;
  }
  function deviceText(e) { return [e.deviceModel, e.deviceOs].filter(Boolean).join(' · '); }
  function appVersionText(e) {
    if (!e.appVersion && !e.buildNumber) return '';
    return str(e.appVersion) + (e.buildNumber ? ' (build ' + e.buildNumber + ')' : '');
  }

";

        #endregion

        #region JS_OVERVIEW

        /// <summary>JS phần 2: khung trang, header, điểm sức khoẻ, thẻ đếm, thanh severity, so sánh lần trước, tab, toolbar, môi trường.</summary>
        internal const string JS_OVERVIEW = @"  function renderLayout() {
    D.getElementById('app').innerHTML = `
<header class='topbar'><div class='wrap topbar-in'>
  <div class='brand'><span class='logo'>${svg(ICON.flask)}</span>
    <div class='brand-txt'><div class='brand-name'>${esc(productName())}</div><div class='brand-sub'>EZG Auto Test · Báo cáo kiểm thử tự động</div></div></div>
  <button class='icon-btn' data-act='theme' title='Đổi giao diện sáng / tối' aria-label='Đổi giao diện sáng / tối'>${svg(ICON.sun, 'i-sun')}${svg(ICON.moon, 'i-moon')}</button>
</div></header>
<main class='wrap'>
  <div id='banner'></div>
  <section id='hero' class='card hero'></section>
  <section id='counts' class='counts'></section>
  <div class='grid2'><section id='sev' class='card'></section><section id='diff' class='card'></section></div>
  <nav id='tabs' class='tabs' role='tablist'></nav>
  <div id='toolbar' class='toolbar'></div>
  <div id='resultLine' class='result-line'></div>
  <section id='view-suites' class='view'></section>
  <section id='view-issues' class='view' hidden></section>
  <section id='view-env' class='view' hidden></section>
  <footer id='foot' class='foot'></footer>
</main>
<div id='lb' class='lb' hidden role='dialog' aria-modal='true' aria-label='Xem ảnh'>
  <button class='lb-btn lb-x' data-act='lb-close' aria-label='Đóng'>×</button>
  <button class='lb-btn lb-prev' data-act='lb-prev' aria-label='Ảnh trước'>‹</button>
  <figure><img id='lbImg' alt=''><figcaption><span id='lbCap'></span><a id='lbOpen' target='_blank' rel='noopener'>Mở ảnh gốc</a></figcaption></figure>
  <button class='lb-btn lb-next' data-act='lb-next' aria-label='Ảnh sau'>›</button>
</div>
<div id='toast' class='toast' role='status' aria-live='polite'></div>`;
  }

  function renderBanner() {
    if (!REP.cancelled) return;
    $('#banner').innerHTML = `<div class='banner'>${svg(ICON.alert)}<div><strong>Lượt chạy đã bị dừng giữa chừng</strong>` +
      `<div>Lý do: ${esc(str(REP.cancelReason) || 'không rõ')}. Kết quả bên dưới chỉ gồm các case đã chạy tới lúc dừng.</div></div></div>`;
  }

  function scoreClass(v) { return v >= SCORE_GOOD ? 'h-good' : v >= SCORE_OK ? 'h-ok' : v >= SCORE_MEH ? 'h-meh' : 'h-bad'; }
  function scoreLabel(v) { return v >= SCORE_GOOD ? 'Tốt' : v >= SCORE_OK ? 'Khá' : v >= SCORE_MEH ? 'Cần chú ý' : 'Nghiêm trọng'; }

  function renderHero() {
    const e = REP.env || {};
    const items = [];
    function add(lbl, value, isHtml) {
      if (value) items.push(`<div class='mi'><div class='mi-l'>${esc(lbl)}</div><div class='mi-v'>${isHtml ? value : esc(value)}</div></div>`);
    }
    add('Bắt đầu', REP.startedAt ? fmtDate(REP.startedAt) : '');
    add('Thời lượng', fmtDur(REP.durationMs));
    add('Kích hoạt', str(REP.trigger));
    add('Nhánh git', gitHtml(e), true);
    add('Unity', str(e.unityVersion));
    add('Nền tảng', [e.platform, e.buildTarget && e.buildTarget !== e.platform ? '(' + e.buildTarget + ')' : ''].filter(Boolean).join(' '));
    add('Thiết bị', deviceText(e) || 'Unity Editor');
    add('Phiên bản app', appVersionText(e));
    add('Adapter', str(e.adapter));
    add('Run ID', str(REP.runId));
    const kicker = [e.projectName, e.companyName].filter(Boolean).join(' · ');
    const circ = 2 * Math.PI * RING_RADIUS;
    const cls = scoreClass(health);
    $('#hero').innerHTML = `
<div class='hero-main'>
  ${kicker ? `<div class='hero-kicker'>${esc(kicker)}</div>` : ''}
  <h1 class='hero-title'>${esc(str(REP.title) || 'Lượt chạy auto test')}</h1>
  <div class='hero-pills'>${pill(overall)}${REP.cancelled && overall !== ST.CANCELLED ? pill(ST.CANCELLED) : ''}
    <span class='muted'>${suites.length} suite · ${allCases.length} case · ${allIssues.length} lỗi</span></div>
  <div class='meta-grid'>${items.join('')}</div>
</div>
<div class='hero-score' title='Điểm sức khoẻ: 100 trừ điểm phạt theo mức độ lỗi và số case crash'>
  <div class='ring'>
    <svg viewBox='0 0 120 120'><circle class='trk' cx='60' cy='60' r='${RING_RADIUS}' fill='none' stroke-width='11'/>
      <circle class='val ${cls}' cx='60' cy='60' r='${RING_RADIUS}' fill='none' stroke-width='11' stroke-linecap='round' stroke-dasharray='${(circ * health / 100).toFixed(1)} ${circ.toFixed(1)}'/></svg>
    <div class='ring-num'><b class='${cls}'>${health}</b><span>/ 100</span></div>
  </div>
  <div class='score-lbl ${cls}'>Sức khoẻ: ${scoreLabel(health)}</div>
</div>`;
  }

  function renderCounts() {
    const total = allCases.length;
    const cards = [`<button class='count c-total' data-count='-1' title='Hiện tất cả case'><span class='count-v'>${total}</span><span class='count-l'>Tổng</span><span class='count-p'>case</span></button>`];
    [ST.PASSED, ST.WARNING, ST.FAILED, ST.ERROR, ST.SKIPPED, ST.CANCELLED].forEach(function (st) {
      const n = counts[st];
      const pct = total ? Math.round(n * 100 / total) : 0;
      cards.push(`<button class='count st-${STATUS_KEYS[st]}${n ? '' : ' zero'}' data-count='${st}' title='Chỉ xem case ${esc(label(st))}'>` +
        `<span class='count-v'>${n}</span><span class='count-l'>${esc(label(st))}</span><span class='count-p'>${pct}%</span></button>`);
    });
    $('#counts').innerHTML = cards.join('');
  }

  function renderSev() {
    const total = allIssues.length;
    const segs = SEV_ORDER.filter(function (s) { return sevCounts[s]; }).map(function (s) {
      return `<span class='seg sev-bg-${s}' style='width:${(sevCounts[s] * 100 / total).toFixed(2)}%' title='${esc(SEV_NAMES[s])}: ${sevCounts[s]}'></span>`;
    }).join('');
    const legend = SEV_ORDER.map(function (s) {
      return `<button class='sev-lg${sevCounts[s] ? '' : ' zero'}' data-sevonly='${s}' title='Chỉ xem lỗi ${esc(SEV_NAMES[s])}'>` +
        `<span><i class='sev-bg-${s}'></i>${esc(SEV_NAMES[s])}</span><b>${sevCounts[s]}</b></button>`;
    }).join('');
    $('#sev').innerHTML = `<div class='card-h'><h2>Lỗi theo mức độ</h2><span class='muted'>${total} lỗi</span></div>` +
      `<div class='sevbar'>${segs}</div>` + (total ? '' : `<p class='muted'>Không phát hiện lỗi nào.</p>`) +
      `<div class='sev-legend'>${legend}</div>`;
  }

  function findCaseByLabel(text) {
    for (let i = 0; i < allCases.length; i++) {
      const c = allCases[i];
      if (suiteName(c._suite) + ' / ' + caseName(c) === text) return c;
    }
    return null;
  }
  function gotoList(items, cls) {
    if (!items.length) return `<span class='muted'>Không có</span>`;
    const shown = items.slice(0, LIST_PREVIEW).map(function (t) {
      return `<li><button class='goto ${cls}' data-goto='${esc(t)}' title='${esc(t)}'>${esc(t)}</button></li>`;
    }).join('');
    const more = items.length > LIST_PREVIEW ? `<li class='muted'>+${items.length - LIST_PREVIEW} case nữa</li>` : '';
    return `<ul class='chip-list'>${shown}${more}</ul>`;
  }

  function renderDiff() {
    const box = $('#diff');
    if (!hasPrev) {
      box.innerHTML = `<div class='card-h'><h2>So với lần chạy trước</h2></div>` +
        `<p class='muted'>Chưa có lượt chạy trước cùng bộ suite để so sánh. Lần chạy sau sẽ hiện lỗi mới / đã sửa / còn tồn.</p>`;
      return;
    }
    const failing = arr(diff.newlyFailingCases);
    const fixedCases = arr(diff.fixedCases);
    const fixed = arr(diff.fixedIssueList);
    const fixedRows = fixed.map(function (i) {
      const sv = sevOf(i.severity);
      return `<div class='fx'>${sevChip(sv)}<div class='fx-t'><s>${esc(str(i.title) || '(không tiêu đề)')}</s>` +
        `${i.location || i.objectPath ? `<div>${esc([i.location, i.objectPath].filter(Boolean).join(' → '))}</div>` : ''}</div></div>`;
    }).join('');
    const fixedMore = (diff.fixedIssues | 0) > fixed.length ? `<div class='muted'>… và ${(diff.fixedIssues | 0) - fixed.length} lỗi khác (report chỉ lưu tối đa ${fixed.length}).</div>` : '';
    box.innerHTML = `<div class='card-h'><h2>So với lần chạy trước</h2><span class='muted'>${esc(fmtDate(diff.previousStartedAt))}` +
      ` · <code>${esc(str(diff.previousRunId))}</code></span></div>
<div class='diff-stats'>
  <button class='dstat d-new' data-act='only-new' title='Chỉ xem lỗi mới'><b>${diff.newIssues | 0}</b><span>Lỗi mới</span></button>
  <div class='dstat d-fixed'><b>${diff.fixedIssues | 0}</b><span>Đã sửa</span></div>
  <div class='dstat d-persist'><b>${diff.persistingIssues | 0}</b><span>Còn tồn</span></div>
</div>
<div class='diff-lists'>
  <div><h3>Case mới lỗi (${failing.length})</h3>${gotoList(failing, 'bad')}</div>
  <div><h3>Case đã hết lỗi (${fixedCases.length})</h3>${gotoList(fixedCases, 'good')}</div>
</div>
${fixed.length ? `<details class='fold'><summary>Danh sách lỗi đã sửa (${diff.fixedIssues | 0})</summary><div class='fixed-list'>${fixedRows}${fixedMore}</div></details>` : ''}`;
  }

  function renderTabs() {
    $('#tabs').innerHTML =
      `<button class='tab' data-tab='suites' role='tab'>Theo suite <span class='tab-n' id='tnSuites'></span></button>` +
      `<button class='tab' data-tab='issues' role='tab'>Danh sách lỗi <span class='tab-n' id='tnIssues'></span></button>` +
      `<button class='tab' data-tab='env' role='tab'>Môi trường</button>`;
  }

  function renderToolbar() {
    const hint = 'Bấm để bật/tắt · Ctrl/⌘ + bấm (hoặc bấm đúp) để chỉ xem mục này';
    const stChips = FILTER_STATUSES.map(function (st) {
      return `<button class='chip st-${STATUS_KEYS[st]}' data-st='${st}' title='${esc(hint)}'><span class='dot'></span>${esc(label(st))}<b>${counts[st]}</b></button>`;
    }).join('');
    const sevChips = SEV_ORDER.map(function (s) {
      return `<button class='chip sev-c-${s}' data-sev='${s}' title='${esc(hint)}'><span class='dot'></span>${esc(SEV_NAMES[s])}<b>${sevCounts[s]}</b></button>`;
    }).join('');
    $('#toolbar').innerHTML = `
<div class='tb-row'>
  <label class='search'>${svg(ICON.search)}<input id='q' type='search' placeholder='Tìm case, lỗi, vị trí, object, ID… (phím /)' autocomplete='off' spellcheck='false' aria-label='Tìm kiếm'></label>
  <label class='switch${hasPrev ? '' : ' disabled'}' title='${hasPrev ? 'Chỉ hiện lỗi chưa có ở lần chạy trước' : 'Chưa có lượt chạy trước để so sánh'}'>
    <input type='checkbox' id='onlyNew'${hasPrev ? '' : ' disabled'}><span class='sw'></span>Chỉ lỗi mới</label>
  <div class='tb-actions'>
    <button class='btn' data-act='expand'>Mở hết</button>
    <button class='btn' data-act='collapse'>Thu hết</button>
    <button class='btn btn-ghost' data-act='reset' id='resetBtn'>Xoá lọc</button>
  </div>
</div>
<div class='tb-row'>
  <div class='chips'><span class='chips-l'>Trạng thái</span>${stChips}</div>
  <div class='chips'><span class='chips-l'>Mức độ</span>${sevChips}</div>
</div>`;
  }

  function renderFooter() {
    const ver = META.packageVersion ? ' v' + META.packageVersion : '';
    $('#foot').innerHTML = `Tạo bởi EZG Auto Test${esc(ver)}${META.generatedAt ? ' · ' + esc(fmtDate(META.generatedAt)) : ''}<br>` +
      `Dữ liệu gốc: <a href='report.json'>report.json</a> · <a href='junit.xml'>junit.xml</a> · <a href='issues.csv'>issues.csv</a> · <a href='summary.md'>summary.md</a>`;
  }

  function renderEnv() {
    const e = REP.env || {};
    const ENV_FIELDS = [
      ['projectName', 'Project'], ['productName', 'Tên sản phẩm'], ['companyName', 'Công ty'], ['bundleId', 'Bundle ID'],
      ['appVersion', 'Phiên bản app'], ['buildNumber', 'Build number'], ['unityVersion', 'Unity'], ['platform', 'Nền tảng'],
      ['buildTarget', 'Build target'], ['renderPipeline', 'Render pipeline'], ['gitBranch', 'Nhánh git'], ['gitCommit', 'Commit'],
      ['gitDirty', 'Có thay đổi chưa commit'], ['machine', 'Máy chạy'], ['os', 'Hệ điều hành'], ['user', 'Người chạy'],
      ['deviceModel', 'Thiết bị'], ['deviceOs', 'HĐH thiết bị'], ['gpu', 'GPU'], ['systemMemoryMb', 'RAM (MB)'],
      ['resolution', 'Độ phân giải'], ['adapter', 'Adapter game'], ['packageVersion', 'Phiên bản package']
    ];
    const envRows = ENV_FIELDS.map(function (f) {
      let v = e[f[0]];
      if (typeof v === 'boolean') v = v ? 'Có' : 'Không';
      else if (typeof v === 'number') v = v ? String(v) : '';
      return [f[1], str(v)];
    }).filter(function (r) { return r[1]; });
    const runRows = [
      ['Run ID', str(REP.runId)], ['Tiêu đề', str(REP.title)],
      ['Bắt đầu', REP.startedAt ? fmtDate(REP.startedAt) : ''], ['Kết thúc', REP.finishedAt ? fmtDate(REP.finishedAt) : ''],
      ['Thời lượng', fmtDur(REP.durationMs)], ['Kích hoạt', str(REP.trigger)],
      ['Bị dừng', REP.cancelled ? 'Có — ' + (str(REP.cancelReason) || 'không rõ lý do') : ''],
      ['Thư mục report', str(REP.outputDir)], ['Tạo report lúc', META.generatedAt ? fmtDate(META.generatedAt) : '']
    ].filter(function (r) { return r[1]; });
    $('#view-env').innerHTML = `<div class='grid2'><div class='card'><div class='card-h'><h2>Lượt chạy</h2></div>${kvTable(runRows)}</div>` +
      `<div class='card'><div class='card-h'><h2>Môi trường</h2></div>${envRows.length ? kvTable(envRows) : `<p class='muted'>Không có thông tin.</p>`}</div></div>`;
  }

";

        #endregion

        #region JS_SUITES

        /// <summary>JS phần 3: danh sách suite/case (dựng lười khi mở), chi tiết case: bước, lỗi, số đo, đính kèm, log.</summary>
        internal const string JS_SUITES = @"  function renderSuites() {
    const view = $('#view-suites');
    if (!suites.length) { view.innerHTML = emptyState('Lượt chạy này không có suite nào.', false); return; }
    const def = filtersDefault();
    const parts = [];
    suites.forEach(function (s) {
      s._vis = def ? s._cases : s._cases.filter(caseVisible);
      if (!def && !s._vis.length) return;
      parts.push(suiteHtml(s));
    });
    view.innerHTML = parts.length ? parts.join('') : emptyState('Không có case nào khớp bộ lọc.', true);
  }

  function suiteHtml(s) {
    const open = openSuites.has(s._i);
    const total = s._cases.length;
    const countTxt = s._vis.length === total ? total + ' case' : s._vis.length + '/' + total + ' case';
    const mini = FILTER_STATUSES.filter(function (st) { return s._counts[st]; }).map(function (st) {
      return `<span class='mini st-${STATUS_KEYS[st]}' title='${esc(label(st))}'>${s._counts[st]}</span>`;
    }).join('');
    const mode = MODE_NAMES[s.mode | 0] || '';
    return `<div class='suite${open ? ' open' : ''}' data-si='${s._i}'>
<button class='suite-h' aria-expanded='${open}'>${svg(ICON.chev, 'chev')}${pill(s._eff)}
  <span class='suite-name'>${esc(suiteName(s))}<small>${esc(str(s.suiteId))}${mode ? ' · ' + mode : ''}</small></span>
  <span class='suite-mini'>${mini}</span>
  <span class='suite-meta'>${countTxt} · ${s._issueCount} lỗi · ${fmtDur(s.durationMs)}</span>
</button>
<div class='suite-b'${open ? '' : ' hidden'}>${open ? suiteBodyHtml(s) : ''}</div></div>`;
  }

  function suiteBodyHtml(s) {
    const out = [];
    if (s.description) out.push(`<p class='suite-desc'>${esc(s.description)}</p>`);
    if (s.message) out.push(`<div class='note'>${esc(s.message)}</div>`);
    if (!s._cases.length) { out.push(`<div class='empty-sm'>Suite không có case nào.</div>`); return out.join(''); }
    if (!s._vis.length) { out.push(`<div class='empty-sm'>Không có case khớp bộ lọc.</div>`); return out.join(''); }
    const limit = suiteLimit.get(s._i) || PAGE_CASES;
    out.push(`<div class='cases'><div class='case-row head' aria-hidden='true'><span></span><span>Case</span><span>Nhóm</span>` +
      `<span class='r'>Thời gian</span><span class='r'>Lỗi</span><span>Thông điệp</span></div>`);
    s._vis.slice(0, limit).forEach(function (c) { out.push(caseHtml(c)); });
    out.push('</div>');
    if (s._vis.length > limit)
      out.push(`<button class='btn more' data-act='more-cases' data-si='${s._i}'>Hiển thị thêm (còn ${s._vis.length - limit} case)</button>`);
    return out.join('');
  }

  function caseHtml(c) {
    const open = openCases.has(c._key);
    const n = c._issues.length;
    const iss = n ? `<span class='sev-dot sev-bg-${c._maxSev}' title='Nặng nhất: ${esc(SEV_NAMES[c._maxSev])}'></span>${n}` : `<span class='muted'>0</span>`;
    return `<div class='case${open ? ' open' : ''}' data-cw='${c._key}'>
<div class='case-row' data-ck='${c._key}' role='button' tabindex='0' aria-expanded='${open}'>${stIcon(c._eff)}
  <span class='c-name'>${c._hasNew ? badgeNew() : ''}${esc(caseName(c))}${c.device ? `<span class='tag'>${esc(c.device)}</span>` : ''}<small>${esc(str(c.caseId))}</small></span>
  <span class='c-cat' title='${esc(str(c.category))}'>${esc(str(c.category))}</span>
  <span class='c-dur r'>${fmtDur(c.durationMs)}</span>
  <span class='c-iss r'>${iss}</span>
  <span class='c-msg' title='${esc(str(c.message))}'>${esc(trunc(c.message, MSG_MAX))}</span>
</div>
<div class='case-d'${open ? '' : ' hidden'}>${open ? caseDetailHtml(c) : ''}</div></div>`;
  }

  function section(title, n, body) { return `<div class='d-sec'><h4>${esc(title)} <span class='n'>${n}</span></h4>${body}</div>`; }
  function bySevDesc(a, b) { return b._sev - a._sev || a._i - b._i; }

  function caseDetailHtml(c) {
    const out = [`<div class='detail'>`];
    if (c.description) out.push(`<p class='desc'>${esc(c.description)}</p>`);
    const meta = [];
    if (c.caseId) meta.push(`<span><b>ID</b><code>${esc(c.caseId)}</code></span>`);
    meta.push(`<span><b>Suite</b>${esc(suiteName(c._suite))}</span>`);
    if (c.startedAt) meta.push(`<span><b>Bắt đầu</b>${esc(fmtDate(c.startedAt))}</span>`);
    meta.push(`<span><b>Thời gian</b>${fmtDur(c.durationMs)}</span>`);
    meta.push(`<span><b>Thiết bị</b>${esc(str(c.device) || deviceText(REP.env || {}) || 'Unity Editor')}</span>`);
    const tags = arr(c.tags).filter(Boolean);
    if (tags.length) meta.push(`<span><b>Tags</b>${tags.map(function (t) { return `<span class='tag'>${esc(t)}</span>`; }).join('')}</span>`);
    out.push(`<div class='d-meta'>${meta.join('')}</div>`);
    if (c.message) out.push(`<div class='msg st-${STATUS_KEYS[c._eff]}'>${stIcon(c._eff)}<div>${esc(c.message)}</div></div>`);

    const steps = arr(c.steps);
    if (steps.length) out.push(section('Các bước', steps.length, stepsHtml(steps)));
    if (c._issues.length) {
      const hit = caseHit(c);
      const shown = c._issues.filter(function (i) { return issuePass(i, hit); }).sort(bySevDesc);
      let html = shown.map(function (i) { return issueCardHtml(i, false); }).join('');
      if (shown.length < c._issues.length) html += `<div class='note muted'>Đang ẩn ${c._issues.length - shown.length} lỗi do bộ lọc.</div>`;
      const n = shown.length === c._issues.length ? String(shown.length) : shown.length + '/' + c._issues.length;
      out.push(section('Lỗi', n, `<div class='issues'>${html}</div>`));
    }
    const metrics = arr(c.metrics);
    if (metrics.length) out.push(section('Số đo', metrics.length, metricsHtml(metrics)));
    const att = arr(c.attachments);
    if (att.length) out.push(section('Tệp đính kèm', att.length, attachmentsHtml(att)));
    const logs = arr(c.logs);
    if (logs.length) out.push(logsShellHtml(c, logs));
    if (!steps.length && !c._issues.length && !metrics.length && !att.length && !logs.length)
      out.push(`<div class='empty-sm'>Case không ghi thêm bước, lỗi, số đo hay log nào.</div>`);
    out.push('</div>');
    return out.join('');
  }

  function stepsHtml(steps) {
    return `<ol class='timeline'>${steps.map(function (s) {
      const st = clampStatus(s.status);
      return `<li class='tl st-${STATUS_KEYS[st]}' title='${esc(label(st))}'><span class='tl-dot'></span>` +
        `<div><div class='tl-name'>${esc(str(s.name) || '(bước)')}</div>${s.detail ? `<div class='tl-detail'>${esc(s.detail)}</div>` : ''}</div>` +
        `<div class='tl-time'><span>+${fmtSecs((Number(s.startMs) || 0) / 1000)}</span><b>${fmtDur(s.durationMs)}</b></div></li>`;
    }).join('')}</ol>`;
  }

  function thumbHtml(path, cap) {
    const url = esc(assetUrl(path));
    return `<a class='thumb' href='${url}' data-lb='${url}' data-cap='${esc(cap)}'><img loading='lazy' src='${url}' alt='${esc(cap)}'><span class='thumb-cap'>${esc(cap)}</span></a>`;
  }

  function issueCardHtml(i, withCase) {
    const c = i._case;
    const kv = [];
    if (withCase) kv.push(['Suite / Case', esc(suiteName(c._suite)) + ' / ' + esc(caseName(c)) + ' ' + pill(c._eff)]);
    if (i.category) kv.push(['Nhóm', esc(i.category)]);
    if (i.location) kv.push(['Vị trí', `<code>${esc(i.location)}</code>`]);
    if (i.objectPath) kv.push(['Object', `<code>${esc(i.objectPath)}</code>`]);
    let h = `<div class='issue sev-c-${i._sev}' data-ik='${i._key}'>`;
    h += `<div class='issue-h'>${sevChip(i._sev)}${i.isNew ? badgeNew() : ''}<span class='issue-title'>${esc(str(i.title) || '(không tiêu đề)')}</span>${copyBtn(i, false)}</div>`;
    if (i.message) h += `<div class='issue-msg'>${esc(i.message)}</div>`;
    if (kv.length) h += `<dl class='kv'>${kv.map(function (p) { return `<dt>${esc(p[0])}</dt><dd>${p[1]}</dd>`; }).join('')}</dl>`;
    if (i.expected || i.actual) {
      h += `<div class='ea'><div class='ea-e'><div class='lbl'>Kỳ vọng</div><pre>${esc(str(i.expected) || '—')}</pre></div>` +
        `<div class='ea-a'><div class='lbl'>Thực tế</div><pre>${esc(str(i.actual) || '—')}</pre></div></div>`;
    }
    if (i.steps) h += `<div><div class='lbl'>Các bước tái hiện</div><pre class='steps'>${esc(i.steps)}</pre></div>`;
    if (i.screenshot) h += `<div class='thumbs' data-lbg='1'>${thumbHtml(i.screenshot, str(i.title) || 'Ảnh chụp lỗi')}</div>`;
    if (i.stackTrace) h += `<details class='stack'><summary>Stack trace</summary><pre>${esc(i.stackTrace)}</pre></details>`;
    if (i.id) h += `<div class='issue-id'>ID lỗi: <code>${esc(i.id)}</code></div>`;
    return h + '</div>';
  }

  function metricsHtml(ms) {
    const rows = ms.map(function (m) {
      const th = [];
      if (m.hasMin) th.push('≥ ' + fmtNum(m.min));
      if (m.hasMax) th.push('≤ ' + fmtNum(m.max));
      const ok = m.passed !== false;
      const unit = str(m.unit) ? ` <span class='unit'>${esc(m.unit)}</span>` : '';
      return `<tr class='${ok ? '' : 'bad'}'><td>${esc(str(m.name))}</td><td class='num'>${esc(fmtNum(m.value))}${unit}</td>` +
        `<td class='num muted'>${th.length ? esc(th.join(' · ')) + unit : '—'}</td>` +
        `<td>${ok ? `<span class='pill st-passed'>${esc(label(ST.PASSED))}</span>` : `<span class='pill st-failed'>Vượt ngưỡng</span>`}</td></tr>`;
    }).join('');
    return `<div class='tbl-wrap'><table class='tbl'><thead><tr><th>Số đo</th><th class='num'>Giá trị</th><th class='num'>Ngưỡng</th><th>Kết quả</th></tr></thead><tbody>${rows}</tbody></table></div>`;
  }

  function isImage(a) { return str(a.kind).toLowerCase() === 'image' || /\.(png|jpe?g|gif|webp|bmp)$/i.test(str(a.path)); }
  function attachmentsHtml(att) {
    const imgs = att.filter(isImage);
    const files = att.filter(function (a) { return !isImage(a); });
    let h = '';
    if (imgs.length) h += `<div class='thumbs' data-lbg='1'>${imgs.map(function (a) { return thumbHtml(a.path, str(a.label) || fileName(a.path)); }).join('')}</div>`;
    if (files.length) h += `<ul class='files'>${files.map(function (a) {
      return `<li>${svg(ICON.file)}<a href='${esc(assetUrl(a.path))}' target='_blank' rel='noopener'>${esc(str(a.label) || fileName(a.path))}</a>` +
        `<span class='tag'>${esc(str(a.kind) || 'file')}</span><code>${esc(str(a.path))}</code></li>`;
    }).join('')}</ul>`;
    return h;
  }

  function logKind(t) {
    t = str(t).toLowerCase();
    if (t === 'error' || t === 'exception' || t === 'assert') return 'error';
    return t === 'warning' ? 'warning' : 'log';
  }
  function logsShellHtml(c, logs) {
    let e = 0, w = 0;
    logs.forEach(function (l) { const k = logKind(l.type); if (k === 'error') e++; else if (k === 'warning') w++; });
    return `<details class='d-sec logs' data-logs='${c._key}'><summary><h4>Log <span class='n'>${logs.length}</span>` +
      `${e ? `<span class='tag tag-bad'>${e} lỗi</span>` : ''}${w ? `<span class='tag tag-warn'>${w} cảnh báo</span>` : ''}</h4></summary>` +
      `<div class='log-body'></div></details>`;
  }
  function renderLogs(det) {
    const c = caseByKey.get(det.getAttribute('data-logs'));
    if (!c) return;
    let st = logState.get(c._key);
    if (!st) { st = { filter: 'all', limit: PAGE_LOGS }; logState.set(c._key, st); }
    const logs = arr(c.logs);
    const cnt = { all: logs.length, error: 0, warning: 0, log: 0 };
    logs.forEach(function (l) { cnt[logKind(l.type)]++; });
    const list = st.filter === 'all' ? logs : logs.filter(function (l) { return logKind(l.type) === st.filter; });
    const filters = [['all', 'Tất cả'], ['error', 'Lỗi'], ['warning', 'Cảnh báo'], ['log', 'Log']].map(function (f) {
      const on = st.filter === f[0];
      return `<button class='chip chip-sm${on ? ' on' : ''}' data-logf='${f[0]}' aria-pressed='${on}'>${f[1]}<b>${cnt[f[0]]}</b></button>`;
    }).join('');
    const lines = list.slice(0, st.limit).map(function (l) {
      const k = logKind(l.type);
      return `<div class='log l-${k}'><span class='log-t'>${fmtSecs(l.t)}</span><span class='log-type'>${esc(str(l.type) || 'Log')}</span>` +
        `<div><div class='log-msg'>${esc(l.message)}</div>${l.stack ? `<details class='log-st'><summary>stack</summary><pre>${esc(l.stack)}</pre></details>` : ''}</div></div>`;
    }).join('');
    const more = list.length > st.limit ? `<button class='btn more' data-act='more-logs'>Hiển thị thêm (còn ${list.length - st.limit} dòng)</button>` : '';
    det.querySelector('.log-body').innerHTML = `<div class='chips log-f'>${filters}</div>` +
      `<div class='log-list'>${lines || `<div class='empty-sm'>Không có dòng log nào.</div>`}</div>${more}`;
  }

";

        #endregion

        #region JS_ISSUES

        /// <summary>JS phần 4: tab Danh sách lỗi (sắp xếp, phân trang), sao chép bug ra clipboard, toast.</summary>
        internal const string JS_ISSUES = @"  const collator = typeof Intl !== 'undefined' && Intl.Collator ? new Intl.Collator('vi', { sensitivity: 'base', numeric: true }) : null;
  function cmpStr(a, b) { a = str(a); b = str(b); return collator ? collator.compare(a, b) : (a < b ? -1 : a > b ? 1 : 0); }
  const SORTERS = {
    severity: function (a, b) { return a._sev - b._sev; },
    title: function (a, b) { return cmpStr(a.title, b.title); },
    suite: function (a, b) { return cmpStr(suiteName(a._case._suite), suiteName(b._case._suite)) || cmpStr(caseName(a._case), caseName(b._case)); },
    category: function (a, b) { return cmpStr(a.category, b.category); },
    location: function (a, b) { return cmpStr(a.location, b.location) || cmpStr(a.objectPath, b.objectPath); }
  };
  function naturalOrder(a, b) { return a._case._suite._i - b._case._suite._i || a._case._i - b._case._i || a._i - b._i; }
  function sortIssues(list) {
    const f = SORTERS[state.sortKey] || SORTERS.severity;
    const d = state.sortDir;
    const bySev = state.sortKey !== 'severity';
    return list.sort(function (a, b) { return f(a, b) * d || (bySev ? b._sev - a._sev : 0) || naturalOrder(a, b); });
  }

  function renderIssues() {
    const view = $('#view-issues');
    if (!allIssues.length) { view.innerHTML = emptyState('Không phát hiện lỗi nào trong lượt chạy này.', false, true); return; }
    const list = sortIssues(visibleIssues());
    if (!list.length) { view.innerHTML = emptyState('Không có lỗi nào khớp bộ lọc.', true); return; }
    const cols = [['severity', 'Mức độ'], ['title', 'Lỗi'], ['suite', 'Suite / Case'], ['category', 'Nhóm'], ['location', 'Vị trí']];
    const head = cols.map(function (h) {
      const on = state.sortKey === h[0];
      const dirTxt = on ? (state.sortDir < 0 ? 'descending' : 'ascending') : 'none';
      return `<th class='sortable${on ? ' sorted' : ''}' data-sort='${h[0]}' aria-sort='${dirTxt}' title='Sắp xếp'>${h[1]}` +
        `<span class='sort-ind'>${on ? (state.sortDir < 0 ? '▼' : '▲') : ''}</span></th>`;
    }).join('');
    const rows = list.slice(0, state.issueLimit).map(issueRowHtml).join('');
    const rest = list.length - state.issueLimit;
    const more = rest > 0 ? `<button class='btn more' data-act='more-issues'>Hiển thị thêm ${Math.min(PAGE_ISSUES, rest)} (còn ${rest} lỗi)</button>` : '';
    view.innerHTML = `<div class='card flush'><div class='tbl-wrap'><table class='tbl issues-tbl'><thead><tr>${head}<th class='act-col'></th></tr></thead>` +
      `<tbody>${rows}</tbody></table></div>${more}</div>`;
  }

  function issueRowHtml(i) {
    const open = openIssues.has(i._key);
    const c = i._case;
    let h = `<tr class='irow${open ? ' open' : ''}' data-ik='${i._key}' tabindex='0' role='button' aria-expanded='${open}'>` +
      `<td>${sevChip(i._sev)}</td>` +
      `<td><div class='it-title'>${i.isNew ? badgeNew() : ''}${esc(str(i.title) || '(không tiêu đề)')}</div>${i.message ? `<div class='it-msg'>${esc(trunc(i.message, MSG_MAX))}</div>` : ''}</td>` +
      `<td><div>${esc(suiteName(c._suite))}</div><div class='it-sub'>${stIcon(c._eff)}${esc(caseName(c))}</div></td>` +
      `<td>${esc(str(i.category))}</td>` +
      `<td class='it-loc'>${i.location ? `<code>${esc(i.location)}</code>` : ''}${i.objectPath ? `<div class='it-sub'><code>${esc(i.objectPath)}</code></div>` : ''}</td>` +
      `<td class='act-col'>${copyBtn(i, true)}</td></tr>`;
    if (open) h += issueDetailRow(i);
    return h;
  }
  function issueDetailRow(i) { return `<tr class='idetail' data-for='${i._key}'><td colspan='6'>${issueCardHtml(i, true)}</td></tr>`; }

  function toggleIssueRow(tr) {
    const key = tr.getAttribute('data-ik');
    const i = issueByKey.get(key);
    if (!i) return;
    const next = tr.nextElementSibling;
    if (openIssues.has(key)) {
      openIssues.delete(key);
      if (next && next.classList.contains('idetail')) next.parentNode.removeChild(next);
      tr.classList.remove('open'); tr.setAttribute('aria-expanded', 'false');
    } else {
      openIssues.add(key);
      tr.insertAdjacentHTML('afterend', issueDetailRow(i));
      tr.classList.add('open'); tr.setAttribute('aria-expanded', 'true');
    }
  }

  // ---------- Sao chép bug ----------
  function envLine(c) {
    const e = REP.env || {};
    const parts = [];
    const app = [productName(), e.appVersion ? 'v' + e.appVersion : '', e.buildNumber ? '(build ' + e.buildNumber + ')' : ''].filter(Boolean).join(' ');
    if (app) parts.push(app);
    parts.push(str(c && c.device) || deviceText(e) || 'Unity Editor');
    if (e.platform) parts.push(str(e.platform));
    if (e.unityVersion) parts.push('Unity ' + e.unityVersion);
    if (e.gitBranch || e.gitCommit)
      parts.push('git ' + str(e.gitBranch) + (e.gitCommit ? '@' + str(e.gitCommit).slice(0, COMMIT_SHORT) : '') + (e.gitDirty ? ' (dirty)' : ''));
    return parts.join(' · ');
  }
  function bugText(i) {
    const c = i._case;
    const L = [];
    L.push('Tiêu đề: ' + (str(i.title) || '(không tiêu đề)'));
    L.push('Mức độ: ' + SEV_NAMES[i._sev] + (i.isNew ? ' (lỗi mới)' : ''));
    L.push('Suite / Case: ' + suiteName(c._suite) + ' / ' + caseName(c) + ' [' + label(c._eff) + ']');
    if (i.category) L.push('Nhóm: ' + i.category);
    if (i.message) L.push('Mô tả: ' + i.message);
    if (i.steps) { L.push('Các bước tái hiện:'); L.push(str(i.steps)); }
    if (i.expected) L.push('Kỳ vọng: ' + i.expected);
    if (i.actual) L.push('Thực tế: ' + i.actual);
    const loc = [i.location, i.objectPath].filter(Boolean).join(' → ');
    if (loc) L.push('Vị trí: ' + loc);
    if (i.screenshot) L.push('Ảnh: ' + i.screenshot);
    L.push('Môi trường: ' + envLine(c));
    L.push('Lượt chạy: ' + [REP.startedAt ? fmtDate(REP.startedAt) : '', str(REP.runId), i.id ? 'ID lỗi ' + i.id : ''].filter(Boolean).join(' · '));
    return L.join('\n');
  }
  let toastTimer = 0;
  function toast(msg, isErr) {
    const t = $('#toast');
    t.textContent = msg;
    t.className = 'toast show' + (isErr ? ' err' : '');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(function () { t.className = 'toast'; }, TOAST_MS);
  }
  function copyText(text) {
    const done = function () { toast('Đã sao chép bug vào clipboard'); };
    const fallback = function () {
      const ta = D.createElement('textarea');
      ta.value = text; ta.setAttribute('readonly', ''); ta.style.position = 'fixed'; ta.style.top = '-1000px';
      D.body.appendChild(ta); ta.select();
      let ok = false;
      try { ok = D.execCommand('copy'); } catch (e) { ok = false; }
      D.body.removeChild(ta);
      if (ok) done(); else toast('Trình duyệt chặn clipboard — không sao chép được', true);
    };
    if (navigator.clipboard && window.isSecureContext) navigator.clipboard.writeText(text).then(done, fallback);
    else fallback();
  }

";

        #endregion

        #region JS_APP

        /// <summary>JS phần 5: làm mới theo bộ lọc, mở/thu, lightbox, theme, sự kiện, khởi động.</summary>
        internal const string JS_APP = @"  // ---------- Làm mới theo bộ lọc ----------
  function syncToolbar() {
    $$('[data-st]').forEach(function (b) { const on = state.statuses.has(+b.getAttribute('data-st')); b.classList.toggle('on', on); b.setAttribute('aria-pressed', String(on)); });
    $$('[data-sev]').forEach(function (b) { const on = state.sevs.has(+b.getAttribute('data-sev')); b.classList.toggle('on', on); b.setAttribute('aria-pressed', String(on)); });
    const cb = $('#onlyNew');
    if (cb) cb.checked = state.onlyNew;
    const q = $('#q');
    if (q && fold(q.value.trim()) !== state.q) q.value = state.q;
    const rb = $('#resetBtn');
    if (rb) rb.disabled = filtersDefault();
    const only = state.statuses.size === 1 ? state.statuses.values().next().value : null;
    $$('[data-count]').forEach(function (b) {
      const v = +b.getAttribute('data-count');
      b.classList.toggle('on', v === -1 ? false : v === only);
    });
  }

  function renderActive() {
    if (state.tab === 'suites') renderSuites();
    else if (state.tab === 'issues') renderIssues();
    else if (state.tab === 'env' && stale.env) renderEnv();
    stale[state.tab] = false;
  }

  function refresh(resetPaging) {
    if (resetPaging) { state.issueLimit = PAGE_ISSUES; suiteLimit.clear(); }
    syncToolbar();
    const nCases = filtersDefault() ? allCases.length : allCases.filter(caseVisible).length;
    const nIssues = filtersDefault() ? allIssues.length : visibleIssues().length;
    $('#tnSuites').textContent = String(nCases);
    $('#tnIssues').textContent = String(nIssues);
    $('#resultLine').innerHTML = state.tab === 'env' ? '' :
      `Hiển thị <b>${nCases}</b>/${allCases.length} case · <b>${nIssues}</b>/${allIssues.length} lỗi` +
      (filtersDefault() ? '' : ` · <button class='link' data-act='reset'>Xoá bộ lọc</button>`);
    stale.suites = true; stale.issues = true;
    renderActive();
  }

  function setTab(tab) {
    state.tab = tab;
    $$('[data-tab]').forEach(function (b) { const on = b.getAttribute('data-tab') === tab; b.classList.toggle('on', on); b.setAttribute('aria-selected', String(on)); });
    ['suites', 'issues', 'env'].forEach(function (t) { $('#view-' + t).hidden = t !== tab; });
    $('#toolbar').hidden = tab === 'env';
    refresh(false);
  }

  function resetFilters() {
    state.q = ''; state.onlyNew = false;
    state.statuses = new Set(FILTER_STATUSES); state.sevs = new Set(SEV_ORDER);
  }

  function toggleIn(set, v, solo, all) {
    if (solo) {
      if (set.size === 1 && set.has(v)) return new Set(all);
      return new Set([v]);
    }
    const next = new Set(set);
    if (next.has(v)) next.delete(v); else next.add(v);
    return next;
  }

  function scrollToTabs() { const t = $('#tabs'); if (t && t.scrollIntoView) t.scrollIntoView({ behavior: 'smooth', block: 'start' }); }

  function gotoCase(c) {
    resetFilters();
    openSuites.add(c._suite._i);
    openCases.add(c._key);
    const idx = c._suite._cases.indexOf(c);
    if (idx >= PAGE_CASES) suiteLimit.set(c._suite._i, idx + 1);
    state.tab = 'suites';
    setTab('suites');
    const el = D.querySelector(`[data-cw='${c._key}']`);
    if (el) {
      el.scrollIntoView({ behavior: 'smooth', block: 'center' });
      el.classList.add('flash');
      setTimeout(function () { el.classList.remove('flash'); }, 1700);
    }
  }

  function toggleSuite(el) {
    const s = suites[+el.getAttribute('data-si')];
    if (!s) return;
    const body = el.querySelector('.suite-b');
    const head = el.querySelector('.suite-h');
    const open = !openSuites.has(s._i);
    if (open) { openSuites.add(s._i); body.innerHTML = suiteBodyHtml(s); }
    else { openSuites.delete(s._i); body.innerHTML = ''; }
    body.hidden = !open;
    el.classList.toggle('open', open);
    head.setAttribute('aria-expanded', String(open));
  }

  function toggleCase(row) {
    const key = row.getAttribute('data-ck');
    const c = caseByKey.get(key);
    if (!c) return;
    const wrap = row.parentNode;
    const det = wrap.querySelector('.case-d');
    const open = !openCases.has(key);
    if (open) { openCases.add(key); det.innerHTML = caseDetailHtml(c); }
    else { openCases.delete(key); det.innerHTML = ''; }
    det.hidden = !open;
    wrap.classList.toggle('open', open);
    row.setAttribute('aria-expanded', String(open));
  }

  // ---------- Lightbox ----------
  let lbList = [];
  let lbIdx = 0;
  function showLb() {
    const a = lbList[lbIdx];
    if (!a) return;
    const src = a.getAttribute('data-lb');
    $('#lbImg').src = src;
    $('#lbOpen').href = src;
    $('#lbCap').textContent = (a.getAttribute('data-cap') || '') + (lbList.length > 1 ? '  (' + (lbIdx + 1) + '/' + lbList.length + ')' : '');
    $('.lb-prev').hidden = lbList.length < 2;
    $('.lb-next').hidden = lbList.length < 2;
  }
  function openLb(a) {
    const grp = a.closest('[data-lbg]');
    lbList = grp ? $$('[data-lb]', grp) : [a];
    lbIdx = Math.max(0, lbList.indexOf(a));
    showLb();
    $('#lb').hidden = false;
    D.body.classList.add('noscroll');
  }
  function closeLb() { $('#lb').hidden = true; $('#lbImg').removeAttribute('src'); D.body.classList.remove('noscroll'); }
  function stepLb(d) { if (lbList.length < 2) return; lbIdx = (lbIdx + d + lbList.length) % lbList.length; showLb(); }

  // ---------- Theme ----------
  function currentTheme() {
    const a = D.documentElement.getAttribute('data-theme');
    if (a) return a;
    return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }
  function initTheme() {
    try { const t = localStorage.getItem(THEME_KEY); if (t === 'light' || t === 'dark') D.documentElement.setAttribute('data-theme', t); } catch (e) { /* bỏ qua */ }
    if (!D.documentElement.getAttribute('data-theme')) D.documentElement.setAttribute('data-theme', currentTheme());
  }
  function toggleTheme() {
    const t = currentTheme() === 'dark' ? 'light' : 'dark';
    D.documentElement.setAttribute('data-theme', t);
    try { localStorage.setItem(THEME_KEY, t); } catch (e) { /* bỏ qua */ }
  }

  // ---------- Sự kiện ----------
  function onAction(act, el) {
    switch (act) {
      case 'theme': toggleTheme(); break;
      case 'reset': resetFilters(); refresh(true); break;
      case 'expand':
        if (state.tab === 'issues') { $$('tr.irow').forEach(function (r) { openIssues.add(r.getAttribute('data-ik')); }); renderIssues(); }
        else { suites.forEach(function (s) { openSuites.add(s._i); }); renderSuites(); }
        break;
      case 'collapse':
        if (state.tab === 'issues') { openIssues.clear(); renderIssues(); }
        else { openSuites.clear(); openCases.clear(); renderSuites(); }
        break;
      case 'more-issues': state.issueLimit += PAGE_ISSUES; renderIssues(); break;
      case 'more-cases': {
        const si = +el.getAttribute('data-si');
        suiteLimit.set(si, (suiteLimit.get(si) || PAGE_CASES) + PAGE_CASES);
        const suiteEl = el.closest('.suite');
        if (suiteEl) suiteEl.querySelector('.suite-b').innerHTML = suiteBodyHtml(suites[si]);
        break;
      }
      case 'more-logs': {
        const det = el.closest('details.logs');
        const st = det && logState.get(det.getAttribute('data-logs'));
        if (st) { st.limit += PAGE_LOGS; renderLogs(det); }
        break;
      }
      case 'only-new':
        if (!hasPrev) break;
        resetFilters(); state.onlyNew = true; setTab('issues'); scrollToTabs();
        break;
      case 'lb-close': closeLb(); break;
      case 'lb-prev': stepLb(-1); break;
      case 'lb-next': stepLb(1); break;
    }
  }

  function onClick(e) {
    const t = e.target;
    if (!t || !t.closest) return;
    const solo = e.ctrlKey || e.metaKey || e.altKey;
    let el;
    if ((el = t.closest('[data-copy]'))) { const it = issueByKey.get(el.getAttribute('data-copy')); if (it) copyText(bugText(it)); return; }
    if ((el = t.closest('[data-lb]'))) { e.preventDefault(); openLb(el); return; }
    if (t.id === 'lb') { closeLb(); return; }
    if ((el = t.closest('[data-act]'))) { onAction(el.getAttribute('data-act'), el); return; }
    if ((el = t.closest('[data-logf]'))) {
      const det = el.closest('details.logs');
      const st = det && logState.get(det.getAttribute('data-logs'));
      if (st) { st.filter = el.getAttribute('data-logf'); st.limit = PAGE_LOGS; renderLogs(det); }
      return;
    }
    if ((el = t.closest('[data-st]'))) { state.statuses = toggleIn(state.statuses, +el.getAttribute('data-st'), solo, FILTER_STATUSES); refresh(true); return; }
    if ((el = t.closest('[data-sev]'))) { state.sevs = toggleIn(state.sevs, +el.getAttribute('data-sev'), solo, SEV_ORDER); refresh(true); return; }
    if ((el = t.closest('[data-sevonly]'))) {
      state.sevs = toggleIn(state.sevs, +el.getAttribute('data-sevonly'), true, SEV_ORDER);
      if (state.tab !== 'issues') setTab('issues'); else refresh(true);
      scrollToTabs(); return;
    }
    if ((el = t.closest('[data-count]'))) {
      const v = +el.getAttribute('data-count');
      state.statuses = v < 0 ? new Set(FILTER_STATUSES) : toggleIn(state.statuses, v, true, FILTER_STATUSES);
      if (state.tab !== 'suites') setTab('suites'); else refresh(true);
      scrollToTabs(); return;
    }
    if ((el = t.closest('[data-tab]'))) { setTab(el.getAttribute('data-tab')); return; }
    if ((el = t.closest('th[data-sort]'))) {
      const k = el.getAttribute('data-sort');
      if (state.sortKey === k) state.sortDir = -state.sortDir;
      else { state.sortKey = k; state.sortDir = k === 'severity' ? -1 : 1; }
      renderIssues(); return;
    }
    if ((el = t.closest('[data-goto]'))) { const c = findCaseByLabel(el.getAttribute('data-goto')); if (c) gotoCase(c); else toast('Không tìm thấy case này trong lượt chạy hiện tại', true); return; }
    if ((el = t.closest('.suite-h'))) { toggleSuite(el.parentNode); return; }
    if ((el = t.closest('.case-row[data-ck]'))) { toggleCase(el); return; }
    if ((el = t.closest('tr.irow'))) { toggleIssueRow(el); return; }
  }

  function onDblClick(e) {
    const t = e.target;
    if (!t || !t.closest) return;
    let el;
    if ((el = t.closest('[data-st]'))) { state.statuses = new Set([+el.getAttribute('data-st')]); refresh(true); }
    else if ((el = t.closest('[data-sev]'))) { state.sevs = new Set([+el.getAttribute('data-sev')]); refresh(true); }
  }

  function onKey(e) {
    const lb = $('#lb');
    if (lb && !lb.hidden) {
      if (e.key === 'Escape') closeLb();
      else if (e.key === 'ArrowLeft') stepLb(-1);
      else if (e.key === 'ArrowRight') stepLb(1);
      return;
    }
    const t = e.target;
    const tag = t && t.tagName ? t.tagName.toUpperCase() : '';
    if ((e.key === 'Enter' || e.key === ' ') && t.matches && t.matches('.case-row[data-ck], tr.irow')) { e.preventDefault(); t.click(); return; }
    if (e.key === '/' && tag !== 'INPUT' && tag !== 'TEXTAREA') { e.preventDefault(); const q = $('#q'); if (q) q.focus(); }
    if (e.key === 'Escape' && t && t.id === 'q' && t.value) { t.value = ''; state.q = ''; refresh(true); }
  }

  let searchTimer = 0;
  function bindEvents() {
    D.addEventListener('click', onClick);
    D.addEventListener('dblclick', onDblClick);
    D.addEventListener('keydown', onKey);
    D.addEventListener('toggle', function (e) {
      const det = e.target;
      if (det && det.matches && det.matches('details.logs') && det.open && !det.getAttribute('data-ready')) {
        det.setAttribute('data-ready', '1');
        renderLogs(det);
      }
    }, true);
    D.addEventListener('error', function (e) {
      const img = e.target;
      if (img && img.tagName === 'IMG' && img.closest) { const th = img.closest('.thumb'); if (th) th.classList.add('broken'); }
    }, true);
    const q = $('#q');
    q.addEventListener('input', function () {
      clearTimeout(searchTimer);
      searchTimer = setTimeout(function () { state.q = fold(q.value.trim()); refresh(true); }, SEARCH_DEBOUNCE_MS);
    });
    const cb = $('#onlyNew');
    cb.addEventListener('change', function () { state.onlyNew = cb.checked; refresh(true); });
  }

  function renderBroken() {
    D.getElementById('app').innerHTML = `<main class='wrap' style='padding-top:32px'><div class='banner err'>${svg(ICON.alert)}<div>` +
      `<strong>Không đọc được dữ liệu báo cáo.</strong><div>File report.html có thể bị cắt ngang khi ghi. Dữ liệu gốc vẫn nằm trong ` +
      `<a href='report.json'>report.json</a> cùng thư mục.</div></div></div></main>`;
  }

  function boot() {
    initTheme();
    if (!DATA) { renderBroken(); return; }
    suites.forEach(function (s) { if (s._eff === ST.FAILED || s._eff === ST.ERROR) openSuites.add(s._i); });
    if (suites.length === 1) openSuites.add(0);
    renderLayout();
    renderBanner();
    renderHero();
    renderCounts();
    renderSev();
    renderDiff();
    renderTabs();
    renderToolbar();
    renderFooter();
    bindEvents();
    setTab('suites');
  }

  if (D.readyState === 'loading') D.addEventListener('DOMContentLoaded', boot); else boot();
})();
";

        #endregion

        /// <summary>Ghép toàn bộ JS theo đúng thứ tự.</summary>
        internal static readonly string[] JsParts = { JS_CORE, JS_OVERVIEW, JS_SUITES, JS_ISSUES, JS_APP };
    }
}
