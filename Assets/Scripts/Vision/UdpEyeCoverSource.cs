using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OverTheRainbow.Vision
{
    /// <summary>
    /// Tools/EyeTracker/eye_tracker.py が UDP で送ってくる判定を受け取る入力元。
    /// 企画書 6.2 の方式 B（別プロセスの Python + MediaPipe）に対応する。
    ///
    /// Python 側は「蛍光ピンクのマーカーが、目のランドマークから一定距離以内にあるか」を
    /// 判定して送ってくる。こちらは受け取るだけで、画像処理は一切持たない。
    ///
    /// IEyeCoverSource の約束どおり時間方向の平滑化はしない。
    /// チャタリング防止は EyeCoverInput の連続フレーム判定に任せる。
    /// </summary>
    public class UdpEyeCoverSource : MonoBehaviour, IEyeCoverSource, IEyeCoverDiagnostics
    {
        /// <summary>eye_tracker.py が送る JSON。JsonUtility で読むため bool は int で受ける。</summary>
        [Serializable]
        private class EyePacket
        {
            public int seq;
            public int face;     // 顔を検出できているか
            public int covered;  // マーカーが目の位置にあるか
            public string eye;   // "L" / "R" / ""
            public float ratio;  // マーカーが画面に占める面積比
            public float dist;   // 目までの距離（両目間隔に対する比）。未検出なら -1
        }

        [Header("受信")]
        [Tooltip("eye_tracker.py の --port と合わせる")]
        [SerializeField] private int port = 51234;
        [Tooltip("この秒数だけ受信が途切れたら Lost（＝遮眼子なし扱い）")]
        [SerializeField, Min(0.05f)] private float timeout = 0.4f;
        [Tooltip("顔を見失っている間は判定しない。切るとマーカーだけで判定してしまうので通常は ON")]
        [SerializeField] private bool requireFace = true;

        private UdpClient client;
        private Thread receiveThread;
        private volatile bool running;

        private readonly Stopwatch clock = new Stopwatch();
        private readonly object gate = new object();
        private EyePacket latest;
        private double latestAt = double.NegativeInfinity;
        private int packetsSinceLastSecond;
        private double lastRateAt;

        /// <summary>直近パケットの面積比。キャリブレーション表示用。</summary>
        public float MarkerRatio { get; private set; }

        /// <summary>直近パケットの、目までの正規化距離。未検出なら -1。</summary>
        public float EyeDistance { get; private set; } = -1f;

        /// <summary>どちらの目を覆っているか（"L" / "R" / ""）。</summary>
        public string CoveredEye { get; private set; } = string.Empty;

        /// <summary>Python 側が顔を検出できているか。</summary>
        public bool FaceFound { get; private set; }

        /// <summary>受信レート。0 なら Python が動いていない。</summary>
        public float PacketsPerSecond { get; private set; }

        public int Port => port;

        /// <summary>画像処理は Python 側なので、Unity にはプレビューも調整手段もない。</summary>
        public Texture PreviewTexture => null;

        public bool TryCalibrateColor() => false;

        public string StatusLine => IsAvailable
            ? "受信中 " + PacketsPerSecond.ToString("F0") + " packet/s（port " + port + "）"
            : "受信なし（port " + port + "）";

        public string SourceName => "UDP:" + port;

        public bool IsAvailable
        {
            get
            {
                lock (gate)
                {
                    return running && latest != null && clock.Elapsed.TotalSeconds - latestAt <= timeout;
                }
            }
        }

        public EyeState ReadRaw()
        {
            EyePacket packet;
            bool fresh;
            lock (gate)
            {
                packet = latest;
                fresh = packet != null && clock.Elapsed.TotalSeconds - latestAt <= timeout;
            }

            if (!fresh) return EyeState.Lost;
            // 顔が映っていなければ「目に当てているか」を判断できない。Lost は安全側（Open）に倒される
            if (requireFace && packet.face == 0) return EyeState.Lost;
            return packet.covered != 0 ? EyeState.Covered : EyeState.Open;
        }

        private void OnEnable()
        {
            clock.Restart();
            try
            {
                client = new UdpClient(port);
            }
            catch (SocketException e)
            {
                // 同じポートを掴んだ Unity がもう一つ動いている、等
                Debug.LogError("[UdpEyeCoverSource] ポート " + port + " を開けません: " + e.Message, this);
                return;
            }

            running = true;
            receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "EyeCoverUdp" };
            receiveThread.Start();
        }

        private void OnDisable()
        {
            running = false;
            // Receive() のブロックは Close() で例外を投げて抜ける
            if (client != null)
            {
                client.Close();
                client = null;
            }
            if (receiveThread != null)
            {
                receiveThread.Join(200);
                receiveThread = null;
            }
            lock (gate)
            {
                latest = null;
                latestAt = double.NegativeInfinity;
            }
            clock.Stop();
        }

        /// <summary>デバッグ表示用の値をメインスレッド側に写す。</summary>
        private void Update()
        {
            lock (gate)
            {
                if (latest != null)
                {
                    MarkerRatio = latest.ratio;
                    EyeDistance = latest.dist;
                    CoveredEye = latest.eye ?? string.Empty;
                    FaceFound = latest.face != 0;
                }

                double now = clock.Elapsed.TotalSeconds;
                if (now - lastRateAt >= 1.0)
                {
                    PacketsPerSecond = (float)(packetsSinceLastSecond / (now - lastRateAt));
                    packetsSinceLastSecond = 0;
                    lastRateAt = now;
                }
            }

            if (!IsAvailable)
            {
                FaceFound = false;
                PacketsPerSecond = 0f;
            }
        }

        private void ReceiveLoop()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (running)
            {
                try
                {
                    byte[] data = client.Receive(ref any);
                    string json = System.Text.Encoding.UTF8.GetString(data);
                    var packet = JsonUtility.FromJson<EyePacket>(json);
                    if (packet == null) continue;

                    lock (gate)
                    {
                        latest = packet;
                        latestAt = clock.Elapsed.TotalSeconds;
                        packetsSinceLastSecond++;
                    }
                }
                catch (ObjectDisposedException) { return; }  // OnDisable の Close()
                catch (SocketException) { if (!running) return; }
                catch (Exception e)
                {
                    // 壊れたパケット1つでスレッドを落とさない
                    Debug.LogWarning("[UdpEyeCoverSource] 受信に失敗: " + e.Message);
                }
            }
        }
    }
}
