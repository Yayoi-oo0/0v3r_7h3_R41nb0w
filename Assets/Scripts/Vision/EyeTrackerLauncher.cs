using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OverTheRainbow.Vision
{
    /// <summary>
    /// 再生と同時に Python のトラッカー（Tools/EyeTracker/eye_tracker.py）を起動し、
    /// 再生終了・アプリ終了で確実に止める。体験会でターミナルを触らせないためのもの。
    ///
    /// 起動前に少しだけ待って、既にトラッカーが動いていれば二重起動しない
    /// （設営中に手で起動している場合を尊重する）。
    /// Unity が異常終了した場合は、Python 側の --parent-pid 監視が後始末をする。
    /// </summary>
    public class EyeTrackerLauncher : MonoBehaviour
    {
        [Header("起動")]
        [Tooltip("再生と同時にトラッカーを起動する")]
        [SerializeField] private bool launchOnPlay = true;
        [Tooltip("OpenCV のプレビューウィンドウを出す。キャリブレーション時だけ ON")]
        [SerializeField] private bool showPreview;
        [Tooltip("この秒数だけ既存のトラッカーからの受信を待つ。来れば起動しない")]
        [SerializeField, Min(0f)] private float probeSeconds = 0.6f;
        [Tooltip("追加の引数（例：--camera 1）")]
        [SerializeField] private string extraArguments = string.Empty;

        [Header("場所")]
        [Tooltip("プロジェクトルート（ビルドでは実行ファイル）からの相対パス")]
        [SerializeField] private string toolsRelativePath = "Tools/EyeTracker";
        [Tooltip("空なら .venv → システムの python3 の順に探す")]
        [SerializeField] private string pythonOverride = string.Empty;

        private const string PidPrefKey = "OverTheRainbow.EyeTracker.Pid";

        private Process process;
        private UdpEyeCoverSource receiver;
        private readonly Queue<string> pendingLogs = new Queue<string>();

        private void Awake()
        {
            receiver = FindObjectOfType<UdpEyeCoverSource>();
        }

        private void Start()
        {
            if (!launchOnPlay) return;
            KillOrphanFromPreviousSession();
            StartCoroutine(LaunchWhenNeeded());
        }

        private void OnApplicationQuit()
        {
            StopTracker();
        }

        private void OnDestroy()
        {
            StopTracker();
        }

        /// <summary>Python の標準出力はメインスレッド以外から来るので、ここで流し込む。</summary>
        private void Update()
        {
            lock (pendingLogs)
            {
                while (pendingLogs.Count > 0) Debug.Log("[EyeTracker] " + pendingLogs.Dequeue());
            }

            if (process != null && process.HasExited)
            {
                Debug.LogWarning("[EyeTrackerLauncher] トラッカーが終了しました（exit " + process.ExitCode
                               + "）。SPACE キーで代用できます。");
                ClearProcess();
            }
        }

        private IEnumerator LaunchWhenNeeded()
        {
            // 既に誰かが動かしていれば、そのまま使う（手動起動・前のプロセスの生き残り）
            float deadline = Time.realtimeSinceStartup + probeSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (receiver != null && receiver.IsAvailable)
                {
                    Debug.Log("[EyeTrackerLauncher] 既に動いているトラッカーを使います。");
                    yield break;
                }
                yield return null;
            }

            Launch();
        }

        private void Launch()
        {
            string toolsDir = ResolveToolsDirectory();
            string script = Path.Combine(toolsDir, "eye_tracker.py");
            if (!File.Exists(script))
            {
                Debug.LogWarning("[EyeTrackerLauncher] " + script + " が見つかりません。"
                               + "SPACE キーで代用できます。");
                return;
            }

            string python = ResolvePython(toolsDir);
            if (python == null)
            {
                Debug.LogWarning("[EyeTrackerLauncher] python が見つかりません。"
                               + "メニュー「0v3r 7h3 R41nb0w/遮眼子トラッカーの環境を準備」を実行してください。");
                return;
            }

            int port = receiver != null ? receiver.Port : 51234;
            // -u：標準出力をバッファさせない（Unity のコンソールに即出す）
            string args = "-u \"" + script + "\""
                        + " --port " + port
                        + " --parent-pid " + Process.GetCurrentProcess().Id
                        + (showPreview ? string.Empty : " --no-preview")
                        + (string.IsNullOrEmpty(extraArguments) ? string.Empty : " " + extraArguments);

            var info = new ProcessStartInfo(python, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = toolsDir,
            };

            try
            {
                process = new Process { StartInfo = info, EnableRaisingEvents = true };
                process.OutputDataReceived += (_, e) => EnqueueLog(e.Data);
                process.ErrorDataReceived += (_, e) => EnqueueLog(e.Data);
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[EyeTrackerLauncher] 起動に失敗しました: " + e.Message
                               + "（SPACE キーで代用できます）");
                ClearProcess();
                return;
            }

            RememberPid(process.Id);
            Debug.Log("[EyeTrackerLauncher] トラッカーを起動しました: " + python + " " + args);
        }

        private void EnqueueLog(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            lock (pendingLogs) pendingLogs.Enqueue(line);
        }

        private void StopTracker()
        {
            if (process == null) return;
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(1000);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[EyeTrackerLauncher] 停止に失敗: " + e.Message);
            }
            ClearProcess();
            ForgetPid();
        }

        private void ClearProcess()
        {
            if (process == null) return;
            process.Dispose();
            process = null;
        }

        /// <summary>
        /// エディタの再生中にスクリプトが再コンパイルされると、この参照だけが失われて
        /// プロセスが残る。次の再生の頭で、自分が起動した分だけを掃除する。
        /// </summary>
        private void KillOrphanFromPreviousSession()
        {
#if UNITY_EDITOR
            int pid = UnityEditor.EditorPrefs.GetInt(PidPrefKey, 0);
            if (pid == 0) return;
            UnityEditor.EditorPrefs.DeleteKey(PidPrefKey);
            try
            {
                var orphan = Process.GetProcessById(pid);
                if (!orphan.HasExited)
                {
                    orphan.Kill();
                    Debug.Log("[EyeTrackerLauncher] 前回の残りプロセス（pid " + pid + "）を停止しました。");
                }
            }
            catch (ArgumentException) { /* もう居ない */ }
            catch (InvalidOperationException) { /* もう居ない */ }
#endif
        }

        private static void RememberPid(int pid)
        {
#if UNITY_EDITOR
            UnityEditor.EditorPrefs.SetInt(PidPrefKey, pid);
#endif
        }

        private static void ForgetPid()
        {
#if UNITY_EDITOR
            UnityEditor.EditorPrefs.DeleteKey(PidPrefKey);
#endif
        }

        /// <summary>エディタならプロジェクトルート、ビルドなら実行ファイルの隣を見る。</summary>
        private string ResolveToolsDirectory()
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(root, toolsRelativePath);
        }

        /// <summary>
        /// .venv → よくある絶対パス → PATH の順。
        /// Finder から起動した Unity は PATH が細いので、絶対パスも当たる。
        /// </summary>
        private string ResolvePython(string toolsDir)
        {
            if (!string.IsNullOrEmpty(pythonOverride))
            {
                return File.Exists(pythonOverride) ? pythonOverride : null;
            }

            bool windows = Application.platform == RuntimePlatform.WindowsPlayer
                        || Application.platform == RuntimePlatform.WindowsEditor;

            string venv = windows
                ? Path.Combine(toolsDir, ".venv", "Scripts", "python.exe")
                : Path.Combine(toolsDir, ".venv", "bin", "python");
            if (File.Exists(venv)) return venv;

            if (windows) return "python";

            foreach (string candidate in new[]
                     {
                         "/opt/homebrew/bin/python3",
                         "/usr/local/bin/python3",
                         "/usr/bin/python3",
                     })
            {
                if (File.Exists(candidate)) return candidate;
            }
            return "python3";
        }
    }
}
