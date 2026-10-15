"""Tests for the backlog loop's Discord notifications.

Covers notify.sh / notify.ps1 (embed layout, parity between the twins), discord-send.sh /
discord-send.ps1 (.env parsing, DM + channel routing, "no ID = no send"), the
`displayName` profile key and the loop's TASK_COMPLETED details line.

Mọi lệnh chạy trên một cây repo tạm (script được copy vào `<tmp>/repo/.claude/scripts/`), không
bao giờ gọi Discord thật: bản bash dùng một `curl` giả đặt đầu PATH, bản PowerShell định nghĩa hàm
`Invoke-RestMethod` trước khi gọi script (hàm thắng cmdlet cùng tên). Không có PowerShell → các test
PowerShell skip (đặt `PWSH=<đường dẫn pwsh>` để chạy).

Run from the repo root:
    python3 -m unittest discover -s .claude/scripts/tests -v
"""

import json
import os
import re
import shutil
import subprocess
import tempfile
import textwrap
import unittest
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parents[1]
PWSH = os.environ.get("PWSH") or shutil.which("pwsh") or shutil.which("powershell")
BASH = shutil.which("bash") or "/bin/bash"
COPIED = ("notify.sh", "notify.ps1", "discord-send.sh", "discord-send.ps1", "project_profile.py")
TOKEN = "test-token-123"

# curl giả: ghi lại URL / body (stdin) / header của từng lần gọi vào $FAKE_CURL_DIR, rồi trả lời như
# Discord. Chỉ dùng builtin + cat để còn chạy được trong PATH tối giản (test không có jq/python3).
FAKE_CURL = r"""#!/bin/bash
dir="$FAKE_CURL_DIR"
n=$(cat "$dir/counter" 2>/dev/null || echo 0); n=$((n + 1)); echo "$n" > "$dir/counter"
url=""; ua=""; auth=""; data_stdin=0; writeout=""; prev=""
for a in "$@"; do
  case "$prev" in
    -A) ua="$a" ;;
    -H) case "$a" in Authorization:*) auth="$a" ;; esac ;;
    --data-binary) [ "$a" = "@-" ] && data_stdin=1 ;;
    -w) writeout="$a" ;;
  esac
  case "$a" in https://*) url="$a" ;; esac
  prev="$a"
done
body=""
[ "$data_stdin" -eq 1 ] && body="$(cat)"
printf '%s' "$url" > "$dir/call-$n.url"
printf '%s' "$body" > "$dir/call-$n.body"
printf '%s\n%s' "$ua" "$auth" > "$dir/call-$n.meta"
code=200
case "$url" in
  */users/@me/channels)
    rid="${body#*\"recipient_id\": \"}"; rid="${rid%%\"*}"
    resp="{\"id\": \"9$rid\", \"type\": 1, \"last_message_id\": null, \"recipients\": [{\"id\": \"$rid\", \"username\": \"dev\"}]}" ;;
  *) resp='{"id": "1111", "channel_id": "x"}' ;;
esac
if [ -n "$FAKE_CURL_FAIL" ]; then
  case "$url" in *"$FAKE_CURL_FAIL"*) code=403; resp='{"message": "Missing Access", "code": 50001}' ;; esac
fi
if [ -n "$FAKE_CURL_DOWN" ]; then
  [ -n "$writeout" ] && printf '\n000'
  exit 7
fi
printf '%s' "$resp"
[ -n "$writeout" ] && printf '\n%s' "$code"
exit 0
"""

# Invoke-RestMethod giả cho PowerShell: ghi mỗi lần gọi thành một dòng JSON vào $env:FAKE_IRM_LOG.
PS_MOCK = r"""
function Invoke-RestMethod {
    param($Uri, $Method, $Headers, $UserAgent, $ContentType, $Body, $TimeoutSec)
    $text = if ($Body -is [byte[]]) { [System.Text.Encoding]::UTF8.GetString($Body) } else { [string]$Body }
    $entry = [ordered]@{ uri = [string]$Uri; body = $text; ua = [string]$UserAgent; auth = [string]$Headers.Authorization; ctype = [string]$ContentType }
    Add-Content -LiteralPath $env:FAKE_IRM_LOG -Value ($entry | ConvertTo-Json -Compress) -Encoding utf8
    if ($env:FAKE_IRM_FAIL -and ([string]$Uri).Contains($env:FAKE_IRM_FAIL)) { throw "Response status code does not indicate success: 403 (Forbidden)." }
    if (([string]$Uri).EndsWith('/users/@me/channels')) {
        $rid = ($text | ConvertFrom-Json).recipient_id
        return [pscustomobject]@{ id = "9$rid"; type = 1; recipients = @([pscustomobject]@{ id = $rid }) }
    }
    return [pscustomobject]@{ id = '1111' }
}
"""

TASK_ARGS = {
    "event": "TASK_COMPLETED",
    "task": 'Fix *bold* _x_ "quoted" `tick` | pipe',
    "url": "file:///Users/dev/game/.git/backlog/in-progress/042-fix-thing.md",
    "details": "Committed `abc1234` to `main`, pushed - next: manual verify steps (DONE summary).",
    "tokens": "12.3M tok | in 1.2K | out 45K\ncache 11M | 37 turns",
    "per-model": "opus: 10M | ~$5.10\nhaiku: 2.3M | ~$0.40",
    "cumulative": "3 iters | 41.2M tok | ~$28.90",
    "breakdown": "Tool           Time    Tokens\nexec           12.3m   1.2M\nRead           3.1m    400K\n+3 more        0.5m    20K",
    "progress": "5/12",
    "duration": "00:04:12",
    "project": "My Game",
}


def ps_quote(value):
    return "'" + str(value).replace("'", "''") + "'"


class _Tree:
    """Một repo tạm có `.claude/scripts/` (bản copy) + `.env` + profile tuỳ chọn."""

    def __init__(self, test, folder="repo"):
        self.tmp = Path(tempfile.mkdtemp(prefix="discord-notify-"))
        test.addCleanup(shutil.rmtree, self.tmp, ignore_errors=True)
        self.root = self.tmp / folder
        self.scripts = self.root / ".claude" / "scripts"
        self.scripts.mkdir(parents=True)
        for name in COPIED:
            shutil.copy2(SCRIPTS / name, self.scripts / name)
        self.calls_dir = self.tmp / "calls"
        self.calls_dir.mkdir()
        self.bin = self.tmp / "bin"
        self.bin.mkdir()
        curl = self.bin / "curl"
        curl.write_text(FAKE_CURL, encoding="utf-8")
        curl.chmod(0o755)
        self.irm_log = self.tmp / "irm.log"

    def write_env(self, text, encoding="utf-8", newline="\n"):
        (self.root / ".env").write_bytes(text.replace("\n", newline).encode(encoding))

    def write_profile(self, data):
        (self.root / ".claude" / "project-profile.json").write_text(json.dumps(data), encoding="utf-8")

    def env(self, path=None, **extra):
        env = {k: v for k, v in os.environ.items()
               if not k.startswith("DISCORD_") and not k.startswith("FAKE_") and k != "NOTIFY_DRY_RUN"}
        env["PATH"] = path or (str(self.bin) + os.pathsep + os.environ.get("PATH", ""))
        env["FAKE_CURL_DIR"] = str(self.calls_dir)
        env["FAKE_IRM_LOG"] = str(self.irm_log)
        env.update(extra)
        return env

    # -- bash ---------------------------------------------------------------
    def run_bash(self, script, *args, path=None, **extra):
        return subprocess.run([BASH, str(self.scripts / script), *args], capture_output=True,
                              text=True, encoding="utf-8", env=self.env(path, **extra), timeout=60)

    def curl_calls(self):
        calls = []
        n = int((self.calls_dir / "counter").read_text()) if (self.calls_dir / "counter").exists() else 0
        for i in range(1, n + 1):
            meta = (self.calls_dir / f"call-{i}.meta").read_text(encoding="utf-8").split("\n")
            calls.append({
                "url": (self.calls_dir / f"call-{i}.url").read_text(encoding="utf-8"),
                "body": (self.calls_dir / f"call-{i}.body").read_text(encoding="utf-8"),
                "ua": meta[0], "auth": meta[1] if len(meta) > 1 else "",
            })
        return calls

    # -- PowerShell -----------------------------------------------------------
    def run_ps(self, script, params, mock=True, **extra):
        call = "& " + ps_quote(self.scripts / script) + "".join(
            f" -{k} {ps_quote(v)}" for k, v in params.items())
        wrapper = self.tmp / "wrapper.ps1"
        wrapper.write_text((PS_MOCK if mock else "") + "\n" + call + "\nexit 0\n", encoding="utf-8-sig")
        return subprocess.run([PWSH, "-NoProfile", "-NonInteractive", "-File", str(wrapper)],
                              capture_output=True, text=True, encoding="utf-8",
                              env=self.env(**extra), timeout=120)

    def irm_calls(self):
        if not self.irm_log.exists():
            return []
        return [json.loads(line) for line in self.irm_log.read_text(encoding="utf-8-sig").splitlines() if line.strip()]


def notify_sh_args(args):
    out = []
    for k, v in args.items():
        out += [f"--{k}", v]
    return out


def notify_ps_params(args):
    names = {"event": "Event", "task": "Task", "url": "Url", "details": "Details", "tokens": "Tokens",
             "per-model": "PerModel", "cumulative": "Cumulative", "breakdown": "Breakdown",
             "progress": "Progress", "duration": "Duration", "project": "Project"}
    return {names[k]: v for k, v in args.items()}


# ===========================================================================
# notify.sh — embed layout
# ===========================================================================
class NotifyShLayoutTests(unittest.TestCase):
    def dry(self, tree, args):
        r = tree.run_bash("notify.sh", *notify_sh_args(args), NOTIFY_DRY_RUN="1")
        self.assertEqual(r.returncode, 0, r.stderr + r.stdout)
        return json.loads(r.stdout)

    def test_task_completed_layout(self):
        tree = _Tree(self)
        embed = self.dry(tree, TASK_ARGS)
        self.assertEqual(embed["title"], "My Game: ✅ Task Completed (Task 5/12, 00:04:12)")
        self.assertEqual(embed["color"], 3066993)
        self.assertNotIn("fields", embed)
        self.assertRegex(embed["timestamp"], r"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$")
        lines = embed["description"].split("\n")
        self.assertEqual(lines[0], r'**Fix \*bold\* \_x\_ "quoted" \`tick\` \| pipe** · `042-fix-thing.md`')
        self.assertEqual(lines[1], TASK_ARGS["details"])  # success details stay plain
        self.assertEqual(lines[2], "**Tokens** 12.3M tok · in 1.2K · out 45K · cache 11M · 37 turns")
        self.assertEqual(lines[3], "**By model** opus 10M ~$5.10 · haiku 2.3M ~$0.40")
        self.assertEqual(lines[4], "**Loop total** 3 iters · 41.2M tok · ~$28.90")
        self.assertEqual(lines[5], "**Per tool** (time/tokens, approx.) exec 12.3m/1.2M · Read 3.1m/400K · +3 more 0.5m/20K")
        self.assertEqual(len(lines), 6)

    def test_error_details_go_in_a_code_block(self):
        tree = _Tree(self)
        embed = self.dry(tree, {"event": "COMPILE_BLOCKED", "task": "T", "details": 'error CS1002: ``` "x" \\ y',
                                "project": "P"})
        self.assertEqual(embed["title"], "P: 🔴 Compilation Blocked")
        self.assertEqual(embed["color"], 15158332)
        desc = embed["description"]
        self.assertIn("Unity compilation failed", desc)
        self.assertIn("```\nerror CS1002: ''' \"x\" \\ y\n```", desc)

    def test_http_url_becomes_a_link_and_na_task_is_dropped(self):
        tree = _Tree(self)
        embed = self.dry(tree, {"event": "TASK_RECOVERING", "task": "T1", "url": "https://example.com/t", "project": "P"})
        self.assertTrue(embed["description"].startswith("**[T1](https://example.com/t)**\n"))
        embed = self.dry(tree, {"event": "BACKLOG_EMPTY", "task": "N/A", "project": "P"})
        self.assertEqual(embed["description"], "The backlog TODO queue is empty. The automation loop has paused safely.")

    def test_unknown_event_and_no_description(self):
        tree = _Tree(self)
        embed = self.dry(tree, {"event": "SOMETHING_NEW", "project": "P"})
        self.assertEqual(embed["title"], "P: ⚠️ Automation Event: SOMETHING_NEW")
        embed = self.dry(tree, {"event": "TASK_COMPLETED", "project": "P"})
        self.assertNotIn("description", embed)

    def test_hostile_text_stays_valid_json(self):
        tree = _Tree(self)
        nasty = 'a "b" \\c\td\r\ne $(rm -rf x) `y` 🎉 · ü'
        embed = self.dry(tree, {"event": "CLI_ERROR", "task": nasty, "details": nasty * 50,
                                "tokens": nasty, "project": 'Q"uote'})
        self.assertTrue(embed["title"].startswith('Q"uote: 🔴'))
        self.assertIn("🎉", embed["description"])
        self.assertLessEqual(len(embed["description"]), 4003)

    def test_long_details_truncated(self):
        tree = _Tree(self)
        embed = self.dry(tree, {"event": "VERIFY_BLOCKED", "details": "x" * 2000, "project": "P"})
        self.assertIn("x" * 900 + "...", embed["description"])
        self.assertNotIn("x" * 901, embed["description"])

    def test_project_from_profile_then_folder(self):
        tree = _Tree(self, folder="SuperGameRepo")
        args = {"event": "BACKLOG_EMPTY"}
        tree.write_profile({"projectName": "EpicThing", "displayName": "  Shiny Name  "})
        self.assertEqual(self.dry(tree, args)["title"], "Shiny Name: ✅ All Backlog Tasks Completed")
        tree.write_profile({"projectName": "EpicThing"})
        self.assertEqual(self.dry(tree, args)["title"], "Epic Thing: ✅ All Backlog Tasks Completed")
        (tree.root / ".claude" / "project-profile.json").unlink()
        # Không có profile → tên thư mục repo (giống notify.ps1), không phải default "UnityProject".
        self.assertEqual(self.dry(tree, args)["title"], "Super Game Repo: ✅ All Backlog Tasks Completed")
        tree.write_profile({"projectName": "EpicThing"})
        (tree.scripts / "project_profile.py").unlink()
        self.assertEqual(self.dry(tree, args)["title"], "Super Game Repo: ✅ All Backlog Tasks Completed")

    def test_missing_event_fails(self):
        tree = _Tree(self)
        r = tree.run_bash("notify.sh", "--task", "x", NOTIFY_DRY_RUN="1")
        self.assertNotEqual(r.returncode, 0)


# ===========================================================================
# discord-send.sh — routing
# ===========================================================================
EMBED = '{"title": "T \\"q\\" 🎉", "description": "line1\\nline2 @everyone", "color": 1}'


class DiscordSendShTests(unittest.TestCase):
    def send(self, tree, env_text, *args, **extra):
        if env_text is not None:
            tree.write_env(env_text)
        r = tree.run_bash("discord-send.sh", *(args or ("", EMBED)), **extra)
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        return r, tree.curl_calls()

    def test_no_env_file_sends_nothing(self):
        tree = _Tree(self)
        r, calls = self.send(tree, None)
        self.assertEqual(calls, [])
        self.assertIn("DISCORD_BOT_TOKEN not configured", r.stdout)

    def test_token_without_ids_sends_nothing(self):
        for text in (f"DISCORD_BOT_TOKEN={TOKEN}\n",
                     f"DISCORD_BOT_TOKEN={TOKEN}\nDISCORD_DEVELOPERS=\nDISCORD_CHANNELS=\n",
                     f"DISCORD_BOT_TOKEN={TOKEN}\nDISCORD_DEVELOPERS=DEVELOPER_USER_ID_1,DEVELOPER_USER_ID_2\n"):
            with self.subTest(env=text):
                tree = _Tree(self)
                r, calls = self.send(tree, text)
                self.assertEqual(calls, [])
                self.assertIn("Skipping notification", r.stdout)

    def test_placeholder_token_sends_nothing(self):
        tree = _Tree(self)
        r, calls = self.send(tree, "DISCORD_BOT_TOKEN=YOUR_DISCORD_BOT_TOKEN_HERE\nDISCORD_CHANNELS=123\n")
        self.assertEqual(calls, [])

    def test_dm_only(self):
        tree = _Tree(self)
        r, calls = self.send(tree, f"DISCORD_BOT_TOKEN={TOKEN}\nDISCORD_DEVELOPERS=111,222\n")
        self.assertEqual([c["url"] for c in calls], [
            "https://discord.com/api/v10/users/@me/channels",
            "https://discord.com/api/v10/channels/9111/messages",
            "https://discord.com/api/v10/users/@me/channels",
            "https://discord.com/api/v10/channels/9222/messages",
        ])
        self.assertEqual(json.loads(calls[0]["body"]), {"recipient_id": "111"})
        self.assertEqual(r.stdout.count("Notification sent to developer"), 2)

    def test_channel_only(self):
        tree = _Tree(self)
        r, calls = self.send(tree, f"DISCORD_BOT_TOKEN={TOKEN}\nDISCORD_CHANNELS=333, 444\n")
        self.assertEqual([c["url"] for c in calls], [
            "https://discord.com/api/v10/channels/333/messages",
            "https://discord.com/api/v10/channels/444/messages",
        ])
        body = json.loads(calls[0]["body"])
        self.assertEqual(body["embeds"][0]["title"], 'T "q" 🎉')
        self.assertEqual(body["allowed_mentions"], {"parse": []})
        self.assertNotIn("content", body)
        self.assertEqual(calls[0]["auth"], f"Authorization: Bot {TOKEN}")
        self.assertTrue(calls[0]["ua"].startswith("DiscordBot ("))
        self.assertEqual(r.stdout.count("Notification sent to channel"), 2)

    def test_dm_and_channel(self):
        tree = _Tree(self)
        r, calls = self.send(tree, f"DISCORD_BOT_TOKEN={TOKEN}\nDISCORD_DEVELOPERS=111\nDISCORD_CHANNELS=333\n")
        self.assertEqual([c["url"].rsplit("/v10", 1)[1] for c in calls],
                         ["/users/@me/channels", "/channels/9111/messages", "/channels/333/messages"])
        self.assertEqual(calls[1]["body"], calls[2]["body"])

    def test_id_list_parsing(self):
        tree = _Tree(self)
        r, calls = self.send(tree, f"DISCORD_BOT_TOKEN={TOKEN}\nDISCORD_CHANNELS=333,abc, 444 ;333,,*,12x\n")
        self.assertEqual([c["url"].rsplit("/", 2)[1] for c in calls], ["333", "444"])
        self.assertIn("Ignoring invalid channel ID 'abc'", r.stderr)
        self.assertIn("Ignoring invalid channel ID '*'", r.stderr)
        self.assertIn("Ignoring invalid channel ID '12x'", r.stderr)

    def test_env_file_formats(self):
        cases = {
            "crlf": (f"DISCORD_BOT_TOKEN={TOKEN}\nDISCORD_CHANNELS=333\n", "utf-8", "\r\n"),
            "bom": (f"DISCORD_BOT_TOKEN={TOKEN}\nDISCORD_CHANNELS=333\n", "utf-8-sig", "\n"),
            "quotes+export+comments": (
                f'# comment\n  export DISCORD_BOT_TOKEN="{TOKEN}"\nOTHER=x y "z"\n'
                "DISCORD_CHANNELS='333' \nDISCORD_DEVELOPERS=111 # team lead\n", "utf-8", "\n"),
            "no trailing newline": (f"DISCORD_BOT_TOKEN={TOKEN}\nDISCORD_CHANNELS=333", "utf-8", "\n"),
            "quoted + inline comment": (
                f'DISCORD_BOT_TOKEN="{TOKEN}"   # bot token\nDISCORD_CHANNELS="333,444"  # channels\n'
                "DISCORD_DEVELOPERS='111' # me\n", "utf-8", "\n"),
        }
        for name, (text, enc, nl) in cases.items():
            with self.subTest(name):
                tree = _Tree(self)
                tree.write_env(text, enc, nl)
                r = tree.run_bash("discord-send.sh", "", EMBED)
                calls = tree.curl_calls()
                urls = [c["url"] for c in calls]
                self.assertIn("https://discord.com/api/v10/channels/333/messages", urls, r.stdout + r.stderr)
                self.assertTrue(all(c["auth"] == f"Authorization: Bot {TOKEN}" for c in calls))
                if name in ("quotes+export+comments", "quoted + inline comment"):
                    self.assertIn("https://discord.com/api/v10/channels/9111/messages", urls)
                if name == "quoted + inline comment":
                    self.assertIn("https://discord.com/api/v10/channels/444/messages", urls)
                    self.assertNotIn("Ignoring invalid", r.stderr)

    def test_env_file_wins_and_shell_env_fills_gaps(self):
        tree = _Tree(self)
        tree.write_env(f"DISCORD_BOT_TOKEN={TOKEN}\n")
        tree.run_bash("discord-send.sh", "", EMBED, DISCORD_BOT_TOKEN="shell-token", DISCORD_CHANNELS="555")
        calls = tree.curl_calls()
        self.assertEqual([c["url"] for c in calls], ["https://discord.com/api/v10/channels/555/messages"])
        self.assertEqual(calls[0]["auth"], f"Authorization: Bot {TOKEN}")

    def test_empty_env_value_disables_a_kind(self):
        tree = _Tree(self)
        tree.write_env(f"DISCORD_BOT_TOKEN={TOKEN}\nDISCORD_CHANNELS=\nDISCORD_DEVELOPERS=111\n")
        tree.run_bash("discord-send.sh", "", EMBED, DISCORD_CHANNELS="555")
        self.assertEqual([c["url"].rsplit("/v10", 1)[1] for c in tree.curl_calls()],
                         ["/users/@me/channels", "/channels/9111/messages"])

    def test_plain_message_is_escaped(self):
        tree = _Tree(self)
        msg = 'hi "there" \\ back\tslash\nline2 🎉'
        r, calls = self.send(tree, f"DISCORD_BOT_TOKEN={TOKEN}\nDISCORD_CHANNELS=333\n", msg)
        body = json.loads(calls[0]["body"])
        self.assertEqual(body["content"], msg.replace("\t", " "))
        self.assertNotIn("embeds", body)
        r, calls = self.send(tree, None, msg, EMBED)
        body = json.loads(tree.curl_calls()[-1]["body"])
        self.assertIn("content", body)
        self.assertIn("embeds", body)

    def test_nothing_to_send(self):
        tree = _Tree(self)
        r, calls = self.send(tree, f"DISCORD_BOT_TOKEN={TOKEN}\nDISCORD_CHANNELS=333\n", "", "")
        self.assertEqual(calls, [])

    def test_failures_never_fail_the_script(self):
        tree = _Tree(self)
        tree.write_env(f"DISCORD_BOT_TOKEN={TOKEN}\nDISCORD_DEVELOPERS=111\nDISCORD_CHANNELS=333,444\n")
        r = tree.run_bash("discord-send.sh", "", EMBED, FAKE_CURL_FAIL="/channels/333/")
        self.assertEqual(r.returncode, 0)
        self.assertIn("Failed to send message to channel 333 (HTTP 403)", r.stdout)
        self.assertIn("Notification sent to channel 444", r.stdout)
        self.assertIn("Notification sent to developer 111 (DM)", r.stdout)
        tree2 = _Tree(self)
        tree2.write_env(f"DISCORD_BOT_TOKEN={TOKEN}\nDISCORD_DEVELOPERS=111\nDISCORD_CHANNELS=333\n")
        r = tree2.run_bash("discord-send.sh", "", EMBED, FAKE_CURL_FAIL="/users/@me/channels")
        self.assertEqual(r.returncode, 0)
        self.assertIn("Failed to create DM channel for user 111 (HTTP 403)", r.stdout)
        self.assertEqual([c["url"].rsplit("/v10", 1)[1] for c in tree2.curl_calls()],
                         ["/users/@me/channels", "/channels/333/messages"])
        tree3 = _Tree(self)
        tree3.write_env(f"DISCORD_BOT_TOKEN={TOKEN}\nDISCORD_CHANNELS=333\n")
        r = tree3.run_bash("discord-send.sh", "", EMBED, FAKE_CURL_DOWN="1")
        self.assertEqual(r.returncode, 0)
        self.assertIn("(HTTP 000)", r.stdout)

    def test_dm_channel_id_without_jq_or_python(self):
        """Không jq/python3: phải lấy id ĐẦU TIÊN (id kênh DM), không phải id người nhận ở cuối."""
        tree = _Tree(self)
        tree.write_env(f"DISCORD_BOT_TOKEN={TOKEN}\nDISCORD_DEVELOPERS=111\n")
        minimal = tree.tmp / "minbin"
        minimal.mkdir()
        for tool in ("bash", "cat", "tr", "sed", "awk", "grep", "head", "tail", "dirname", "basename"):
            found = shutil.which(tool)
            self.assertTrue(found, tool)
            os.symlink(found, minimal / tool)
        os.symlink(tree.bin / "curl", minimal / "curl")
        r = tree.run_bash("discord-send.sh", "", EMBED, path=str(minimal))
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertEqual([c["url"].rsplit("/v10", 1)[1] for c in tree.curl_calls()],
                         ["/users/@me/channels", "/channels/9111/messages"])

    def test_dm_channel_id_falls_through_broken_tools(self):
        """jq trả rác / python3 là stub của Windows Store / output có \\r → vẫn ra đúng id kênh DM."""
        cases = {
            "garbage jq + stub python": ("#!/bin/sh\necho 'jq: error'\n", "#!/bin/sh\nexit 9009\n"),
            "CRLF jq": ("#!/bin/sh\nprintf '9111\\r\\n'\n", None),
            "no jq, CRLF python": (None, "#!/bin/sh\nprintf '9111\\r\\n'\n"),
        }
        for name, (jq, py) in cases.items():
            with self.subTest(name):
                tree = _Tree(self)
                tree.write_env(f"DISCORD_BOT_TOKEN={TOKEN}\nDISCORD_DEVELOPERS=111\n")
                minimal = tree.tmp / "minbin"
                minimal.mkdir()
                for tool in ("bash", "sh", "cat", "tr", "sed", "awk", "grep", "head", "tail", "dirname", "basename"):
                    os.symlink(shutil.which(tool), minimal / tool)
                os.symlink(tree.bin / "curl", minimal / "curl")
                for tool, body in (("jq", jq), ("python3", py)):
                    if body is not None:
                        (minimal / tool).write_text(body, encoding="utf-8")
                        (minimal / tool).chmod(0o755)
                r = tree.run_bash("discord-send.sh", "", EMBED, path=str(minimal))
                self.assertEqual([c["url"].rsplit("/v10", 1)[1] for c in tree.curl_calls()],
                                 ["/users/@me/channels", "/channels/9111/messages"], r.stdout + r.stderr)

    def test_notify_end_to_end(self):
        tree = _Tree(self)
        tree.write_env(f"DISCORD_BOT_TOKEN={TOKEN}\nDISCORD_DEVELOPERS=111\nDISCORD_CHANNELS=333\n")
        r = tree.run_bash("notify.sh", *notify_sh_args(TASK_ARGS))
        self.assertEqual(r.returncode, 0, r.stderr)
        calls = tree.curl_calls()
        self.assertEqual([c["url"].rsplit("/v10", 1)[1] for c in calls],
                         ["/users/@me/channels", "/channels/9111/messages", "/channels/333/messages"])
        embed = json.loads(calls[2]["body"])["embeds"][0]
        self.assertEqual(embed["title"], "My Game: ✅ Task Completed (Task 5/12, 00:04:12)")


# ===========================================================================
# project_profile.displayName + the loop's completed_details
# ===========================================================================
NAME_CASES = {
    "EpicIdleJourney": "Epic Idle Journey",
    "ABCTemplate": "ABC Template",
    "my_game-name": "my game name",
    "Epic Idle Journey": "Epic Idle Journey",
    "iOSGame": "i OS Game",
    "AAbBBc": "A Ab B Bc",
    "Game2D": "Game2D",
}


class DisplayNameTests(unittest.TestCase):
    def profile_value(self, tree, key="displayName"):
        r = subprocess.run(["python3", str(tree.scripts / "project_profile.py"), key],
                           capture_output=True, text=True, encoding="utf-8")
        self.assertEqual(r.returncode, 0, r.stderr)
        return r.stdout.strip()

    def test_python_split_matches_bash_fallback(self):
        for name, expected in NAME_CASES.items():
            with self.subTest(name):
                tree = _Tree(self)
                tree.write_profile({"projectName": name})
                self.assertEqual(self.profile_value(tree), expected)
                sed = subprocess.run(
                    [BASH, "-c", "printf '%s' \"$1\" | sed -E 's/[_-]+/ /g; s/([a-z])([A-Z])/\\1 \\2/g; "
                     "s/([A-Z])([A-Z][a-z])/\\1 \\2/g'", "_", name], capture_output=True, text=True)
                self.assertEqual(" ".join(sed.stdout.split()), expected)

    def test_explicit_display_name_and_summary(self):
        tree = _Tree(self)
        tree.write_profile({"projectName": "EpicThing", "displayName": "Shiny Name"})
        self.assertEqual(self.profile_value(tree), "Shiny Name")
        r = subprocess.run(["python3", str(tree.scripts / "project_profile.py")], capture_output=True,
                           text=True, encoding="utf-8")
        self.assertEqual(json.loads(r.stdout)["displayName"], "Shiny Name")

    def test_loop_calls_stay_compatible_with_older_notify(self):
        """Loop không truyền --project/-Project: notify.sh cũ exit 1 với flag lạ, notify.ps1 cũ (advanced
        script) ném lỗi binding → mất hết thông báo khi project chỉ cập nhật loop."""
        loop = (SCRIPTS / "run-backlog-loop.sh").read_text(encoding="utf-8")
        self.assertIn('bash "$NOTIFY" "$@" >/dev/null 2>&1 || true', loop)
        self.assertIsNone(re.search(r"notify[^\n]*--project|NOTIFY\"[^\n]*--project", loop))
        self.assertIn('--details "$(completed_details "$TASK_BASE_NOTIF" "$prompt_file")$healed_note"', loop)
        core = (SCRIPTS / "run-backlog-loop-core.ps1").read_text(encoding="utf-8")
        calls = [l for l in core.splitlines() if l.strip().startswith("& $notifyScript")]
        self.assertEqual(len(calls), 1)
        self.assertNotIn("-Project", calls[0])
        self.assertIn("$completedDetails = Get-CompletedDetails -TaskBase $taskBase -Since $iterStart", core)


def _extract(text, start_regex, end_line):
    m = re.search(start_regex, text, re.M)
    assert m, start_regex
    end = text.index(end_line, m.start())
    return text[m.start(): end + len(end_line)]


class CompletedDetailsTests(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="completed-details-"))
        self.addCleanup(shutil.rmtree, self.tmp, ignore_errors=True)
        self.backlog = self.tmp / "backlog"
        self.archive = self.backlog / "runs" / "archive"
        self.archive.mkdir(parents=True)
        self.marker = self.tmp / "iter.prompt"
        self.marker.write_text("p")
        os.utime(self.marker, (1_000_000_000, 1_000_000_000))

    def ship(self, name, shipped, old=False):
        path = self.archive / name
        path.write_text(json.dumps({"shipped": shipped}), encoding="utf-8")
        if old:
            os.utime(path, (999_999_000, 999_999_000))

    def bash_details(self, task_base, mode="current"):
        fn = _extract((SCRIPTS / "run-backlog-loop.sh").read_text(encoding="utf-8"),
                      r"^completed_details\(\) \{", "\n}\n")
        script = f"{fn}\ncompleted_details \"$1\" \"$2\""
        env = dict(os.environ, BACKLOG_ROOT=str(self.backlog), AGENT_BRANCH="main", MODE=mode,
                   LOOP_BASE_BRANCH="develop")
        r = subprocess.run([BASH, "-c", "BACKLOG_ROOT=$BACKLOG_ROOT; " + script, "_", task_base, str(self.marker)],
                           capture_output=True, text=True, env=env)
        self.assertEqual(r.returncode, 0, r.stderr)
        return r.stdout

    def test_bash_shipped_record(self):
        self.ship("042-20261015T010101-shipped.json", {"commit": "abcdef1234567", "push": "pushed"})
        self.ship("042-20261014T010101-shipped.json", {"commit": "0000000", "push": "failed"}, old=True)
        self.assertEqual(self.bash_details("042-fix-thing.md"),
                         "Committed `abcdef1` to `main`, pushed - next: manual verify steps (DONE summary).")

    def test_bash_push_states_and_worktree(self):
        for push, text in (("no-remote", "not pushed (no remote)"), ("failed", "PUSH FAILED - push it by hand"),
                           ("skipped", "not pushed (skipped)")):
            with self.subTest(push):
                for f in self.archive.iterdir():
                    f.unlink()
                self.ship("007-20261015T010101-shipped.json", {"commit": "1234567", "push": push})
                self.assertIn(text, self.bash_details("007-x.md"))
        self.assertTrue(self.bash_details("007-x.md", mode="worktree").endswith(
            "next: manual verify, then merge into `develop`."))

    def test_bash_already_satisfied_is_not_a_commit(self):
        self.ship("042-20261015T010101-shipped.json",
                  {"commit": "9f8e7d6c5b", "push": "skipped", "note": "already-satisfied"})
        self.assertEqual(self.bash_details("042-fix.md"),
                         "Already satisfied, no new commit (checked at `9f8e7d6` on `main`) - "
                         "next: manual verify steps (DONE summary).")
        for f in self.archive.iterdir():
            f.unlink()
        self.ship("042-20261015T010102-shipped.json",
                  {"commit": "1234567", "push": "pushed", "note": "recovered | odd\nnote"})
        self.assertEqual(self.bash_details("042-fix.md"),
                         "Committed `1234567` to `main`, pushed - next: manual verify steps (DONE summary).")
        for f in self.archive.iterdir():
            f.unlink()
        self.ship("042-20261015T010103-shipped.json", {"commit": "", "push": "pushed"})
        self.assertEqual(self.bash_details("042-fix.md"),
                         "Committed to `main`, pushed - next: manual verify steps (DONE summary).")

    def test_bash_no_record_falls_back(self):
        self.ship("042-20261014T010101-shipped.json", {"commit": "abc", "push": "pushed"}, old=True)
        self.assertEqual(self.bash_details("042-fix.md"),
                         "Committed to `main`, pushed if the repo has a remote - next: manual verify steps (DONE summary).")
        self.assertIn("Committed to `main`", self.bash_details(""))
        self.assertIn("Committed to `main`", self.bash_details("README.md"))

    @unittest.skipUnless(PWSH, "cần PowerShell (pwsh)")
    def test_ps_twin_matches_bash(self):
        fn = _extract((SCRIPTS / "run-backlog-loop-core.ps1").read_text(encoding="utf-8"),
                      r"^function Get-CompletedDetails \{", "\n}\n")
        self.ship("042-20261015T010101-shipped.json", {"commit": "abcdef1234567", "push": "no-remote"})
        self.ship("041-20261015T010101-shipped.json", {"commit": "1111111", "push": "pushed"})
        self.ship("044-20261015T010101-shipped.json",
                  {"commit": "9f8e7d6c5b", "push": "skipped", "note": "already-satisfied"})
        os.utime(self.marker, None)  # bản PS so mtime với $Since = "bây giờ" lúc bắt đầu iteration
        since = "(Get-Date).AddSeconds(-30)"
        for f in self.archive.iterdir():
            os.utime(f, None)
        script = self.tmp / "cd.ps1"
        script.write_text(textwrap.dedent(f"""
            $BacklogRoot = {ps_quote(self.backlog)}
            $AgentBranch = 'main'; $LoopBaseBranch = 'develop'; $Mode = 'Current'
            {fn}
            Get-CompletedDetails -TaskBase '042-fix-thing.md' -Since {since}
            Get-CompletedDetails -TaskBase '043-none.md' -Since {since}
            $Mode = 'Worktree'
            Get-CompletedDetails -TaskBase '041-x.md' -Since {since}
            Get-CompletedDetails -TaskBase '042-fix-thing.md' -Since (Get-Date).AddHours(1)
            $Mode = 'Current'
            Get-CompletedDetails -TaskBase '044-done.md' -Since {since}
        """), encoding="utf-8-sig")
        r = subprocess.run([PWSH, "-NoProfile", "-NonInteractive", "-File", str(script)],
                           capture_output=True, text=True, encoding="utf-8", timeout=120)
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertEqual(r.stdout.splitlines(), [
            "Committed `abcdef1` to `main`, not pushed (no remote) - next: manual verify steps (DONE summary).",
            "Committed to `main`, pushed if the repo has a remote - next: manual verify steps (DONE summary).",
            "Committed `1111111` to `main`, pushed - next: manual verify, then merge into `develop`.",
            "Committed to `main`, pushed if the repo has a remote - next: manual verify, then merge into `develop`.",
            "Already satisfied, no new commit (checked at `9f8e7d6` on `main`) - next: manual verify steps (DONE summary).",
        ])


# ===========================================================================
# PowerShell twins
# ===========================================================================
class PsSenderParityTests(unittest.TestCase):
    def block(self, name):
        text = (SCRIPTS / name).read_bytes().decode("utf-8-sig")
        m = re.search(r"# >>> discord-sender.*?# <<< discord-sender\n", text, re.S)
        self.assertIsNotNone(m, name)
        return m.group(0)

    def test_sender_block_identical(self):
        self.assertEqual(self.block("notify.ps1"), self.block("discord-send.ps1"))

    def test_encodings(self):
        # PS 5.1 đọc file không BOM theo ANSI: file có ký tự ngoài ASCII phải có BOM.
        for name in ("notify.ps1", "discord-send.ps1", "run-backlog-loop-core.ps1"):
            raw = (SCRIPTS / name).read_bytes()
            has_bom = raw.startswith(b"\xef\xbb\xbf")
            ascii_only = all(b < 128 for b in raw[3 if has_bom else 0:])
            self.assertTrue(has_bom or ascii_only, f"{name}: non-ASCII without BOM")
            self.assertNotIn(b"\r\n", raw, name)


@unittest.skipUnless(PWSH, "cần PowerShell (pwsh)")
class NotifyPs1Tests(unittest.TestCase):
    def dry_ps(self, tree, args):
        r = tree.run_ps("notify.ps1", notify_ps_params(args), mock=False, NOTIFY_DRY_RUN="1")
        self.assertEqual(r.returncode, 0, r.stderr + r.stdout)
        return json.loads(r.stdout)["embeds"][0]

    def dry_sh(self, tree, args):
        r = tree.run_bash("notify.sh", *notify_sh_args(args), NOTIFY_DRY_RUN="1")
        return json.loads(r.stdout)

    def test_layout_parity_with_bash(self):
        cases = [
            TASK_ARGS,
            {"event": "COMPILE_BLOCKED", "task": "T", "url": "C:\\repo\\backlog\\todo\\001-a.md",
             "details": 'err ``` "x" ' + "y" * 1000, "project": "P"},
            {"event": "TASK_RECOVERING", "task": "T1", "url": "https://example.com/t", "project": "P"},
            {"event": "BACKLOG_EMPTY", "task": "N/A", "progress": "3/3", "project": "P"},
            {"event": "SOMETHING_NEW", "project": "P"},
            {"event": "TASK_COMPLETED", "project": "P"},
        ]
        tree = _Tree(self)
        for args in cases:
            with self.subTest(args["event"]):
                sh, ps = self.dry_sh(tree, args), self.dry_ps(tree, args)
                for key in ("title", "color", "description"):
                    self.assertEqual(sh.get(key), ps.get(key), key)

    def test_project_from_profile(self):
        tree = _Tree(self, folder="SuperGameRepo")
        tree.write_profile({"projectName": "EpicThing"})
        self.assertEqual(self.dry_ps(tree, {"event": "BACKLOG_EMPTY"})["title"],
                         "Epic Thing: ✅ All Backlog Tasks Completed")
        tree.write_profile({"projectName": "EpicThing", "displayName": "Shiny"})
        self.assertTrue(self.dry_ps(tree, {"event": "BACKLOG_EMPTY"})["title"].startswith("Shiny: "))
        (tree.root / ".claude" / "project-profile.json").unlink()
        self.assertTrue(self.dry_ps(tree, {"event": "BACKLOG_EMPTY"})["title"].startswith("Super Game Repo: "))

    def run_notify(self, tree, env_text, **extra):
        tree.write_env(env_text)
        r = tree.run_ps("notify.ps1", notify_ps_params(TASK_ARGS), **extra)
        self.assertEqual(r.returncode, 0, r.stderr + r.stdout)
        return r, tree.irm_calls()

    def test_routing(self):
        cases = {
            "no ids": (f"DISCORD_BOT_TOKEN={TOKEN}\n", []),
            "placeholders": (f"DISCORD_BOT_TOKEN={TOKEN}\nDISCORD_DEVELOPERS=DEVELOPER_USER_ID_1,DEVELOPER_USER_ID_2\n", []),
            "no token": ("DISCORD_DEVELOPERS=111\nDISCORD_CHANNELS=333\n", []),
            "dm": (f"DISCORD_BOT_TOKEN={TOKEN}\nDISCORD_DEVELOPERS=111\n",
                   ["/users/@me/channels", "/channels/9111/messages"]),
            "channel": (f"DISCORD_BOT_TOKEN={TOKEN}\nDISCORD_CHANNELS=333;444, 333\n",
                        ["/channels/333/messages", "/channels/444/messages"]),
            "both": (f'export DISCORD_BOT_TOKEN="{TOKEN}"\nDISCORD_DEVELOPERS=111 # me\nDISCORD_CHANNELS=333\n',
                     ["/users/@me/channels", "/channels/9111/messages", "/channels/333/messages"]),
            "quoted + inline comment": (
                f'DISCORD_BOT_TOKEN="{TOKEN}"   # bot token\nDISCORD_CHANNELS="333,444"  # channels\n'
                "DISCORD_DEVELOPERS='111' # me\n",
                ["/users/@me/channels", "/channels/9111/messages", "/channels/333/messages", "/channels/444/messages"]),
        }
        for name, (env_text, expected) in cases.items():
            with self.subTest(name):
                tree = _Tree(self)
                r, calls = self.run_notify(tree, env_text)
                self.assertEqual([c["uri"].rsplit("/v10", 1)[1] for c in calls], expected, r.stdout)
                for c in calls:
                    self.assertEqual(c["auth"], f"Bot {TOKEN}")
                    self.assertTrue(c["ua"].startswith("DiscordBot ("))
                    self.assertEqual(c["ctype"], "application/json")
                if expected:
                    body = json.loads(calls[-1]["body"])
                    self.assertEqual(body["allowed_mentions"], {"parse": []})
                    self.assertEqual(body["embeds"][0]["title"], "My Game: ✅ Task Completed (Task 5/12, 00:04:12)")

    def test_failures_are_logged_not_thrown(self):
        tree = _Tree(self)
        r, calls = self.run_notify(tree, f"DISCORD_BOT_TOKEN={TOKEN}\nDISCORD_DEVELOPERS=111\nDISCORD_CHANNELS=333,444\n",
                                   FAKE_IRM_FAIL="/channels/333/")
        self.assertIn("Failed to send message to channel 333", r.stdout)
        self.assertIn("Notification sent to channel 444", r.stdout)
        self.assertIn("Notification sent to developer 111 (DM)", r.stdout)

    def test_env_reread_per_call_and_never_leaks(self):
        """notify.ps1 chạy trong process controller: .env sửa giữa hai lần gọi phải có hiệu lực, và token
        không được ghi vào env của process (sẽ lọt sang mọi process agent loop mở sau đó)."""
        tree = _Tree(self)
        env_path = tree.root / ".env"
        tree.write_env(f"DISCORD_BOT_TOKEN={TOKEN}\nDISCORD_CHANNELS=333\n")
        call = "& " + ps_quote(tree.scripts / "notify.ps1") + " -Event BACKLOG_EMPTY -Project P"
        wrapper = tree.tmp / "twice.ps1"
        wrapper.write_text(PS_MOCK + "\n" + call + "\n"
                           + f"Set-Content -LiteralPath {ps_quote(env_path)} -Value 'DISCORD_BOT_TOKEN={TOKEN}' -Encoding ascii\n"
                           + call + "\n"
                           + "Write-Output ('LEAK=' + [string]$env:DISCORD_BOT_TOKEN + '|' + [string]$env:DISCORD_CHANNELS)\n",
                           encoding="utf-8-sig")
        r = subprocess.run([PWSH, "-NoProfile", "-NonInteractive", "-File", str(wrapper)], capture_output=True,
                           text=True, encoding="utf-8", env=tree.env(), timeout=120)
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertEqual([c["uri"] for c in tree.irm_calls()], ["https://discord.com/api/v10/channels/333/messages"],
                         r.stdout)
        self.assertIn("LEAK=|", r.stdout)

    def test_timestamp_is_iso_under_any_culture(self):
        tree = _Tree(self)
        call = "& " + ps_quote(tree.scripts / "notify.ps1") + " -Event BACKLOG_EMPTY -Project P"
        wrapper = tree.tmp / "culture.ps1"
        wrapper.write_text("[System.Threading.Thread]::CurrentThread.CurrentCulture = 'fi-FI'\n"
                           "[System.Globalization.CultureInfo]::CurrentCulture = 'fi-FI'\n" + call + "\n",
                           encoding="utf-8-sig")
        r = subprocess.run([PWSH, "-NoProfile", "-NonInteractive", "-File", str(wrapper)], capture_output=True,
                           text=True, encoding="utf-8", env=tree.env(NOTIFY_DRY_RUN="1"), timeout=120)
        self.assertEqual(r.returncode, 0, r.stderr)
        stamp = json.loads(r.stdout)["embeds"][0]["timestamp"]
        self.assertRegex(stamp, r"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$")

    def test_crlf_bom_env(self):
        tree = _Tree(self)
        tree.write_env(f"DISCORD_BOT_TOKEN={TOKEN}\nDISCORD_CHANNELS=333\n", "utf-8-sig", "\r\n")
        r = tree.run_ps("notify.ps1", notify_ps_params(TASK_ARGS))
        self.assertEqual([c["uri"] for c in tree.irm_calls()], ["https://discord.com/api/v10/channels/333/messages"],
                         r.stdout + r.stderr)


@unittest.skipUnless(PWSH, "cần PowerShell (pwsh)")
class DiscordSendPs1Tests(unittest.TestCase):
    def test_embed_message_and_routing(self):
        tree = _Tree(self)
        tree.write_env(f"DISCORD_BOT_TOKEN={TOKEN}\nDISCORD_DEVELOPERS=111\nDISCORD_CHANNELS=333\n")
        r = tree.run_ps("discord-send.ps1", {"Message": "hi \"x\" 🎉", "EmbedJson": EMBED})
        self.assertEqual(r.returncode, 0, r.stderr)
        calls = tree.irm_calls()
        self.assertEqual([c["uri"].rsplit("/v10", 1)[1] for c in calls],
                         ["/users/@me/channels", "/channels/9111/messages", "/channels/333/messages"])
        body = json.loads(calls[-1]["body"])
        self.assertEqual(body["content"], 'hi "x" 🎉')
        self.assertEqual(body["embeds"][0]["title"], 'T "q" 🎉')
        self.assertEqual(body["embeds"][0]["description"], "line1\nline2 @everyone")

    def test_bad_json_and_no_ids(self):
        tree = _Tree(self)
        tree.write_env(f"DISCORD_BOT_TOKEN={TOKEN}\nDISCORD_CHANNELS=333\n")
        r = tree.run_ps("discord-send.ps1", {"EmbedJson": "{not json"})
        self.assertEqual(r.returncode, 0)
        self.assertIn("not valid JSON", r.stdout)
        self.assertEqual(tree.irm_calls(), [])
        tree.write_env(f"DISCORD_BOT_TOKEN={TOKEN}\nDISCORD_CHANNELS=abc\n")
        r = tree.run_ps("discord-send.ps1", {"Message": "x"})
        self.assertIn("Ignoring invalid channel ID 'abc'", r.stdout)
        self.assertEqual(tree.irm_calls(), [])


if __name__ == "__main__":
    unittest.main()
