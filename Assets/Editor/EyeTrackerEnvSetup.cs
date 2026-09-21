using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OverTheRainbow.EditorTools
{
    /// <summary>
    /// 遮眼子トラッカー（Python）の実行環境を Unity から用意する。
    /// 体験会の PC でターミナルを開かずに済ませるためのもので、
    /// Tools/EyeTracker/.venv を作って依存を入れるだけ。
    /// 一度作れば EyeTrackerLauncher が .venv を優先して使う。
    /// </summary>
    public static class EyeTrackerEnvSetup
    {
        private const string ToolsDir = "Tools/EyeTracker";

        private static Process process;
        private static float startedAt;

        [MenuItem("0v3r 7h3 R41nb0w/遮眼子トラッカーの環境を準備", false, 26)]
        public static void Prepare()
        {
            if (process != null)
            {
                Debug.LogWarning("[EyeTrackerEnvSetup] すでに実行中です。");
                return;
            }

            string root = Path.GetDirectoryName(Application.dataPath);
            string dir = Path.Combine(root, ToolsDir);
            if (!File.Exists(Path.Combine(dir, "requirements.txt")))
            {
                EditorUtility.DisplayDialog("0v3r 7h3 R41nb0w",
                    ToolsDir + " が見つかりません。", "OK");
                return;
            }

            bool windows = Application.platform == RuntimePlatform.WindowsEditor;
            string shell = windows ? "cmd.exe" : "/bin/sh";
            string command = windows
                ? "/c python -m venv .venv && .venv\\Scripts\\python -m pip install --upgrade pip "
                  + "&& .venv\\Scripts\\python -m pip install -r requirements.txt"
                : "-c \"python3 -m venv .venv && ./.venv/bin/python -m pip install --upgrade pip "
                  + "&& ./.venv/bin/python -m pip install -r requirements.txt\"";

            var info = new ProcessStartInfo(shell, command)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = dir,
            };

            process = new Process { StartInfo = info, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.Log("[pip] " + e.Data); };
            process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.Log("[pip] " + e.Data); };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch (Exception e)
            {
                Debug.LogError("[EyeTrackerEnvSetup] 起動に失敗しました: " + e.Message);
                process = null;
                return;
            }

            startedAt = (float)EditorApplication.timeSinceStartup;
            EditorApplication.update += Pump;
            Debug.Log("[EyeTrackerEnvSetup] 依存のインストールを開始しました（数分かかります）。進捗はコンソールに出ます。");
        }

        /// <summary>完了を待ちつつ、キャンセルできるようにする。</summary>
        private static void Pump()
        {
            if (process == null)
            {
                EditorApplication.update -= Pump;
                EditorUtility.ClearProgressBar();
                return;
            }

            float elapsed = (float)EditorApplication.timeSinceStartup - startedAt;

            if (!process.HasExited)
            {
                bool cancel = EditorUtility.DisplayCancelableProgressBar(
                    "遮眼子トラッカーの環境を準備",
                    "依存をインストールしています… " + Mathf.FloorToInt(elapsed) + " 秒",
                    Mathf.Repeat(elapsed / 60f, 1f));
                if (cancel)
                {
                    try { process.Kill(); } catch (InvalidOperationException) { }
                }
                return;
            }

            int code = process.ExitCode;
            process.Dispose();
            process = null;
            EditorApplication.update -= Pump;
            EditorUtility.ClearProgressBar();

            if (code == 0)
            {
                Debug.Log("[EyeTrackerEnvSetup] 完了しました。再生ボタンでトラッカーが自動起動します。");
                EditorUtility.DisplayDialog("0v3r 7h3 R41nb0w",
                    "遮眼子トラッカーの環境ができました。\n\n再生ボタンを押すと自動で起動します。", "OK");
            }
            else
            {
                Debug.LogError("[EyeTrackerEnvSetup] 失敗しました（exit " + code + "）。"
                             + "コンソールのログを確認してください。Python 3.9 以上が必要です。");
            }
        }
    }
}
