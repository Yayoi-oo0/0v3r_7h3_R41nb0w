@echo off
rem Windows 用ランチャ。初回は仮想環境を作って依存を入れる。
cd /d "%~dp0"

if not exist .venv (
  echo 仮想環境を作成しています（初回のみ、数分かかります）...
  python -m venv .venv || goto :error
  .venv\Scripts\python -m pip install --upgrade pip
  .venv\Scripts\python -m pip install -r requirements.txt || goto :error
)

.venv\Scripts\python eye_tracker.py %*
goto :eof

:error
echo セットアップに失敗しました。Python 3.9 以上（3.10〜3.12 推奨）が入っているか確認してください。
pause
