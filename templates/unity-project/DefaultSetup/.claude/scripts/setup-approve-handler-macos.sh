#!/bin/bash
# Registers the <gitConfigPrefix>-approve:// URL scheme on this Mac, pointed at
# ui-review-approve-handler.py. Run once per machine. Per-user (no sudo needed).
#
# The scheme is namespaced per project (from .claude/project-profile.json's
# gitConfigPrefix) because URL schemes are a MACHINE-WIDE registry: a dev working
# on two games generated from this template would otherwise have both register the
# same scheme, and whichever ran last would swallow the other's approvals.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SCRIPT_DIR="$ROOT/.claude/scripts"
HANDLER="$SCRIPT_DIR/ui-review-approve-handler.py"
LSREGISTER="/System/Library/Frameworks/CoreServices.framework/Versions/A/Frameworks/LaunchServices.framework/Versions/A/Support/lsregister"

if [ ! -f "$HANDLER" ]; then
  echo "error: handler not found at $HANDLER" >&2
  exit 1
fi

profile_get() {
  python3 "$SCRIPT_DIR/project_profile.py" "$1" 2>/dev/null || printf '%s' "$2"
}
SLUG="$(profile_get gitConfigPrefix agent)"
PROJECT_NAME="$(profile_get projectName UnityProject)"
SCHEME="${SLUG}-approve"
APP_DIR="$HOME/Applications/${PROJECT_NAME}Approve.app"

mkdir -p "$HOME/Applications"

SCPT_SRC="$(mktemp "/tmp/${SCHEME}-XXXXXX").applescript"
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
/usr/libexec/PlistBuddy -c "Add :CFBundleURLTypes:0:CFBundleURLName string $PROJECT_NAME Approve" "$PLIST"
/usr/libexec/PlistBuddy -c "Add :CFBundleURLTypes:0:CFBundleURLSchemes array" "$PLIST"
/usr/libexec/PlistBuddy -c "Add :CFBundleURLTypes:0:CFBundleURLSchemes:0 string $SCHEME" "$PLIST"
/usr/libexec/PlistBuddy -c "Add :LSBackgroundOnly bool true" "$PLIST"
/usr/libexec/PlistBuddy -c "Add :LSUIElement bool true" "$PLIST"

"$LSREGISTER" -f "$APP_DIR"

echo "OK: ${SCHEME}:// -> $APP_DIR (handler: $HANDLER)"
echo "Lan dau macOS se hoi xac nhan mo bang ${PROJECT_NAME}Approve.app - dong y 1 lan."
