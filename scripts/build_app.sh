#!/usr/bin/env bash
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP_NAME="AntigravityAssistant"
APP_BUNDLE="${PROJECT_DIR}/dist/${APP_NAME}.app"

ARCH="$(uname -m)"
echo "==> Building ${APP_NAME} (Release, ${ARCH})..."
cd "${PROJECT_DIR}"

rm -rf "${APP_BUNDLE}"
mkdir -p "${APP_BUNDLE}/Contents/MacOS"
mkdir -p "${APP_BUNDLE}/Contents/Resources"

BIN_PATH="${APP_BUNDLE}/Contents/MacOS/${APP_NAME}"

swiftc \
    -O \
    -parse-as-library \
    -swift-version 5 \
    -target "${ARCH}-apple-macosx14.0" \
    -framework AppKit \
    -framework ApplicationServices \
    -framework SwiftUI \
    -framework Carbon \
    -framework UserNotifications \
    -lsqlite3 \
    Sources/AntigravityAssistant/*.swift \
    -o "${BIN_PATH}"

chmod +x "${BIN_PATH}"

ICON_TMP_ROOT="$(mktemp -d)"
ICONSET_DIR="${ICON_TMP_ROOT}/AppIcon.iconset"
"${BIN_PATH}" --export-iconset "${ICONSET_DIR}" >/dev/null
iconutil -c icns "${ICONSET_DIR}" -o "${APP_BUNDLE}/Contents/Resources/AppIcon.icns"
rm -rf "${ICON_TMP_ROOT}"

cat > "${APP_BUNDLE}/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key>
    <string>zh_CN</string>
    <key>CFBundleDisplayName</key>
    <string>Antigravity 使用助手</string>
    <key>CFBundleExecutable</key>
    <string>${APP_NAME}</string>
    <key>CFBundleIconFile</key>
    <string>AppIcon</string>
    <key>CFBundleIconName</key>
    <string>AppIcon</string>
    <key>CFBundleIdentifier</key>
    <string>com.local.antigravity.floating-assistant</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>CFBundleName</key>
    <string>Antigravity 使用助手</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0.0</string>
    <key>CFBundleVersion</key>
    <string>1</string>
    <key>LSMinimumSystemVersion</key>
    <string>14.0</string>
    <key>LSUIElement</key>
    <true/>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
EOF

# Ad-hoc sign with stable bundle identifier so macOS TCC / UserNotifications recognize the bundle cleanly
codesign --force --deep --sign - --identifier com.local.antigravity.floating-assistant "${APP_BUNDLE}" >/dev/null 2>&1 || true
touch "${APP_BUNDLE}"
/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "${APP_BUNDLE}" >/dev/null 2>&1 || true

echo "==> Done! App bundle ready at:"
echo "    ${APP_BUNDLE}"
