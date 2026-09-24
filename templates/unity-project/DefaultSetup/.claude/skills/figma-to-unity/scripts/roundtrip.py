#!/usr/bin/env python3
"""One round of the Figma-to-Unity round trip: (re)import a screen, then score it against Figma.

Talks to the Unity MCP server over HTTP (JSON-RPC, no MCP client needed), so it also works when
the session's MCP connector failed. Standard library only.

    python3 roundtrip.py Assets/UI/Screens/Shop.prefab --import offline
    python3 roundtrip.py Assets/UI/Screens/Shop.prefab --import online --clean

--import none      score the prefab on disk (default)
--import offline   rebuild from the cached document and the files on disk (no Figma API call)
--import online    download the document and renders again; also refreshes the Figma reference
--clean            delete the screen prefab and its sidecar first (last round of a fix)

Exit code: 0 every container passes, 1 a container is below its pass score, 2 the round could not run.
Needs com.ezg.figma-bridge 0.5.1 or later in the open Unity project.
"""
import argparse
import json
import os
import sys
import time
import urllib.error
import urllib.request

DEFAULT_MCP_URL = "http://127.0.0.1:8080/mcp"
BRIDGE = "UnityFigmaBridge.Editor.UnityFigmaBridgeImporter"
CHECK = "UnityFigmaBridge.Editor.Verify.FigmaVisualCheck"


class RoundError(Exception):
    pass


class UnityMcp:
    def __init__(self, url):
        self.url = url
        self.session = None
        self.request_id = 0

    def _post(self, payload, timeout=600):
        headers = {"Content-Type": "application/json", "Accept": "application/json, text/event-stream"}
        if self.session:
            headers["mcp-session-id"] = self.session
        request = urllib.request.Request(self.url, data=json.dumps(payload).encode(), headers=headers)
        with urllib.request.urlopen(request, timeout=timeout) as response:
            self.session = response.headers.get("mcp-session-id") or self.session
            body = response.read().decode()
        messages = [json.loads(line[5:]) for line in body.splitlines() if line.startswith("data:")]
        if not messages and body.strip():
            messages = [json.loads(body)]
        return messages

    def _rpc(self, method, params):
        self.request_id += 1
        for message in self._post({"jsonrpc": "2.0", "id": self.request_id, "method": method, "params": params}):
            if message.get("id") == self.request_id:
                if "error" in message:
                    raise RoundError(f"MCP {method} failed: {message['error']}")
                return message.get("result", {})
        raise RoundError(f"MCP {method}: no answer")

    def connect(self):
        try:
            self._rpc("initialize", {"protocolVersion": "2025-03-26", "capabilities": {},
                                     "clientInfo": {"name": "figma-roundtrip", "version": "1"}})
            self._post({"jsonrpc": "2.0", "method": "notifications/initialized"})
        except (urllib.error.URLError, ConnectionError) as error:
            raise RoundError(f"Unity MCP server not reachable at {self.url} ({error}). Is the editor open?")

    def tool(self, name, arguments):
        result = self._rpc("tools/call", {"name": name, "arguments": arguments})
        text = "".join(part.get("text", "") for part in result.get("content", []))
        try:
            return json.loads(text)
        except ValueError:
            return {"success": not result.get("isError"), "message": text}

    def resource(self, uri):
        result = self._rpc("resources/read", {"uri": uri})
        text = "".join(part.get("text", "") for part in result.get("contents", []))
        return json.loads(text) if text else {}

    def csharp(self, code, retries=5):
        """Run a C# 6 method body in the editor and return its string result."""
        last = None
        for _ in range(retries):
            try:
                answer = self.tool("execute_code", {"action": "execute", "code": code, "safety_checks": False})
            except (urllib.error.URLError, ConnectionError, TimeoutError) as error:
                last = str(error)
                time.sleep(3)
                continue
            if answer.get("success"):
                return (answer.get("data") or {}).get("result")
            last = answer.get("message") or json.dumps(answer)[:500]
            # A domain reload or a busy editor answers without success; compile errors do not recover
            if "Compilation failed" in (last or ""):
                break
            time.sleep(3)
        raise RoundError(f"execute_code failed: {last}")


def project_root(start):
    path = os.path.abspath(start)
    while True:
        if os.path.isdir(os.path.join(path, "Assets")) and os.path.isdir(os.path.join(path, "ProjectSettings")):
            return path
        parent = os.path.dirname(path)
        if parent == path:
            raise RoundError("Not inside a Unity project (no Assets/ and ProjectSettings/ above the current folder)")
        path = parent


def select_instance(unity, root, wanted):
    listing = unity.resource("mcpforunity://instances")
    instances = (listing.get("data") or listing).get("instances", [])
    if len(instances) <= 1 and not wanted:
        return
    name = wanted or os.path.basename(root)
    match = next((i for i in instances if i.get("id") == name or i.get("name") == name), None)
    if match is None:
        raise RoundError(f"No Unity instance '{name}' among: {', '.join(i.get('id', '?') for i in instances)}")
    unity.tool("set_active_instance", {"instance": match["id"]})


def cs_string(value):
    return '"' + value.replace("\\", "\\\\").replace('"', '\\"') + '"'


def editor_idle(unity, timeout):
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            state = unity.resource("mcpforunity://editor/state").get("data", {})
            compiling = state.get("compilation", {}).get("is_compiling")
            updating = state.get("assets", {}).get("is_updating")
            if not compiling and not updating:
                return
        except (RoundError, urllib.error.URLError, ValueError):
            pass
        time.sleep(2)
    raise RoundError("Editor did not become idle")


def run_import(unity, mode, timeout):
    call = "SyncDocument" if mode == "online" else "SyncDocumentOffline"
    # The import holds the main thread, so it starts on the next editor update and this call answers
    # first. A one-shot update handler, not delayCall: delayCall does not run while the editor is
    # unfocused. One attempt: a retry after a lost answer would queue a second import.
    requested = unity.csharp(f"{BRIDGE}.SuppressDialogs = true; "
                             "UnityEditor.EditorApplication.CallbackFunction start = null; "
                             f"start = () => {{ UnityEditor.EditorApplication.update -= start; {BRIDGE}.{call}(); }}; "
                             "UnityEditor.EditorApplication.update += start; "
                             "return System.DateTime.UtcNow.ToString(\"o\");", retries=1)
    print(f"[roundtrip] {mode} import requested", flush=True)
    # Fields joined by U+001F: an error message may hold newlines
    fields = ["ImportInProgress ? \"1\" : \"0\"", "LastImportStartedUtc ?? \"\"", "LastImportError ?? \"\"",
              "LastImportCompletedUtc ?? \"\""]
    poll = "return " + " + \"\\u001f\" + ".join(f"({BRIDGE}.{field})" for field in fields) + ";"
    deadline = time.time() + timeout
    start_deadline = time.time() + 120
    while time.time() < deadline:
        time.sleep(5)
        try:
            running, started, error, completed = (unity.csharp(poll, retries=1) + "\x1f" * 3).split("\x1f")[:4]
        except RoundError as problem:
            if "Compilation failed" in str(problem):
                raise RoundError("The bridge in this project has no import state; update com.ezg.figma-bridge "
                                 f"to 0.5.1 or later ({problem})")
            continue  # the editor is busy importing and does not answer
        if running == "1":
            continue
        if not started or started < requested:
            if time.time() > start_deadline:
                raise RoundError("The import did not start within 120 s")
            continue
        if error:
            raise RoundError(f"Import stopped: {error}")
        if completed and completed >= started:
            editor_idle(unity, 300)
            print("[roundtrip] import finished", flush=True)
            return
        raise RoundError("Import ended without finishing (a domain reload during the import kills it; "
                         "do not edit scripts while it runs)")
    raise RoundError(f"Import still running after {timeout}s")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("screen", help="Screen prefab path, e.g. Assets/UI/Screens/Shop.prefab")
    parser.add_argument("--import", dest="import_mode", choices=["none", "offline", "online"], default="none")
    parser.add_argument("--clean", action="store_true", help="delete the screen prefab and its sidecar first")
    parser.add_argument("--refresh-reference", action="store_true", help="fetch Figma's frame render again")
    parser.add_argument("--pass-score", type=float, default=0.90)
    parser.add_argument("--text-pass-score", type=float, default=0.85)
    parser.add_argument("--timeout", type=int, default=1800, help="seconds to wait for the import")
    parser.add_argument("--mcp-url", default=os.environ.get("UNITY_MCP_URL", DEFAULT_MCP_URL))
    parser.add_argument("--unity-instance", help="Name@hash or project name when several editors are open")
    args = parser.parse_args()

    try:
        root = project_root(os.getcwd())
        screen = args.screen.replace("\\", "/")
        if not screen.startswith("Assets/"):
            raise RoundError("Pass the screen prefab as an Assets/... path")
        unity = UnityMcp(args.mcp_url)
        unity.connect()
        select_instance(unity, root, args.unity_instance)

        if args.clean:
            if args.import_mode == "none":
                raise RoundError("--clean deletes the prefab, so it needs --import offline or online")
            sidecar = os.path.splitext(screen)[0] + ".instances.json"
            unity.csharp(f"UnityEditor.AssetDatabase.DeleteAsset({cs_string(screen)}); "
                         f"UnityEditor.AssetDatabase.DeleteAsset({cs_string(sidecar)}); return \"ok\";", retries=1)
            print(f"[roundtrip] deleted {screen} and its sidecar", flush=True)
        if args.import_mode != "none":
            run_import(unity, args.import_mode, args.timeout)

        refresh = args.refresh_reference or args.import_mode == "online"
        summary = unity.csharp(f"return {CHECK}.Run({cs_string(screen)}, {args.pass_score}f, "
                               f"{args.text_pass_score}f, {'true' if refresh else 'false'}).ToString();", retries=1)
        print(summary, flush=True)

        name = os.path.splitext(os.path.basename(screen))[0]
        report_path = os.path.join(root, "Library", "FigmaVisualCheck", name, "report.json")
        with open(report_path, encoding="utf-8") as handle:
            report = json.load(handle)
        low_folder = os.path.join(os.path.dirname(report_path), "low")
        if not report.get("passed") and os.path.isdir(low_folder):
            print("[roundtrip] crops of the failing containers (Figma | Unity | difference):")
            for crop in sorted(os.listdir(low_folder)):
                print("  " + os.path.join(low_folder, crop))
        return 0 if report.get("passed") else 1
    except RoundError as error:
        print(f"[roundtrip] {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
