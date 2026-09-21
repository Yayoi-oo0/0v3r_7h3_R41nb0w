#!/bin/bash
# macOS 用ランチャ。初回は仮想環境を作って依存を入れる。
# Finder からダブルクリックで起動できる（初回だけ chmod +x が必要）。
cd "$(dirname "$0")" || exit 1

PY=$(command -v python3.12 || command -v python3.11 || command -v python3.10 || command -v python3)
if [ -z "$PY" ]; then
  echo "python3 が見つかりません。python.org から 3.10〜3.12 を入れてください。"
  read -r -p "Enter で閉じる"
  exit 1
fi

if [ ! -d .venv ]; then
  echo "仮想環境を作成しています（初回のみ、数分かかります）…"
  "$PY" -m venv .venv || exit 1
  ./.venv/bin/pip install --upgrade pip
  ./.venv/bin/pip install -r requirements.txt || { read -r -p "Enter で閉じる"; exit 1; }
fi

./.venv/bin/python eye_tracker.py "$@"
