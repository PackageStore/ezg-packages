#!/bin/bash
# Registers the ezg-ui-approve:// URL scheme on this Mac, pointed at
# ui-review-approve-handler.py. Run once per machine. Per-user (no sudo needed).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
HANDLER="$ROOT/.claude/scripts/ui-review-approve-handler.py"
APP_DIR="$HOME/Applications/EzgUiApprove.app"
LSREGISTER="/System/Library/Frameworks/CoreServices.framework/Versions/A/Frameworks/LaunchServices.framework/Versions/A/Support/lsregister"

if [ ! -f "$HANDLER" ]; then
  echo "error: handler not found at $HANDLER" >&2
  exit 1
fi

mkdir -p "$HOME/Applications"

SCPT_SRC="$(mktemp /tmp/ezg-ui-approve-XXXXXX).applescript"
cat > "$SCPT_SRC" <<EOF
on open location this_URL
	do shell script "/usr/bin/python3 " & quoted form of "$HANDLER" & " " & quoted form of this_URL
end open location
EOF

rm -rf "$APP_DIR"
osacompile -o "$APP_DIR" "$SCPT_SRC"
rm -f "$SCPT_SRC"

PLIST="$APP_DIR/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Add :CFBundleURLTypes array" "$PLIST" 2>/dev/null || true
/usr/libexec/PlistBuddy -c "Add :CFBundleURLTypes:0 dict" "$PLIST"
/usr/libexec/PlistBuddy -c "Add :CFBundleURLTypes:0:CFBundleURLName string EZG UI Approve" "$PLIST"
/usr/libexec/PlistBuddy -c "Add :CFBundleURLTypes:0:CFBundleURLSchemes array" "$PLIST"
/usr/libexec/PlistBuddy -c "Add :CFBundleURLTypes:0:CFBundleURLSchemes:0 string ezg-ui-approve" "$PLIST"
/usr/libexec/PlistBuddy -c "Add :LSBackgroundOnly bool true" "$PLIST"
/usr/libexec/PlistBuddy -c "Add :LSUIElement bool true" "$PLIST"

"$LSREGISTER" -f "$APP_DIR"

echo "OK: ezg-ui-approve:// -> $APP_DIR (handler: $HANDLER)"
echo "Lan dau macOS se hoi xac nhan mo bang EzgUiApprove.app - dong y 1 lan."
