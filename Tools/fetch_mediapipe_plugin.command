#!/bin/bash
# MediaPipe Unity Plugin 本体（約277MB）を取得する。
# リポジトリには大きすぎるので含めていない。クローン直後に一度だけ実行する。
#
# curl で落とすので quarantine 属性は付かない。
# ブラウザで手動ダウンロードした場合は、Gatekeeper に止められることがある：
#   xattr -dr com.apple.quarantine ThirdParty/
set -e
cd "$(dirname "$0")/.." || exit 1

VERSION=0.16.3
FILE="ThirdParty/com.github.homuler.mediapipe-${VERSION}.tgz"
URL="https://github.com/homuler/MediaPipeUnityPlugin/releases/download/v${VERSION}/com.github.homuler.mediapipe-${VERSION}.tgz"

if [ -f "$FILE" ]; then
  echo "既にあります: $FILE"
  exit 0
fi

mkdir -p ThirdParty
echo "ダウンロードしています（約277MB）…"
curl -L --fail -o "$FILE.part" "$URL"
mv "$FILE.part" "$FILE"
echo "完了: $FILE"
echo "Unity を開くと自動でインポートされます（初回は数分かかります）。"
