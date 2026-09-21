using System;
using System.Collections.Generic;
using Mediapipe;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using UnityEngine;
// Mediapipe 名前空間にも同名の型があるので、使う方を明示する
using NormalizedLandmark = Mediapipe.Tasks.Components.Containers.NormalizedLandmark;
using Color = UnityEngine.Color;

namespace OverTheRainbow.Vision
{
    /// <summary>
    /// MediaPipe Unity Plugin を使い、Unity 内だけで遮眼子を検出する入力元（企画書 6.2 方式 A）。
    ///
    ///   MediaPipe FaceLandmarker … 左右の目の座標を取るだけ
    ///   自前の HSV 判定          … 遮眼子に貼った蛍光ピンクの丸マーカーを検出
    ///   その2つの距離            … 目に当てているかの判定
    ///
    /// 顔のランドマークから遮蔽を推定するより、眼鏡・照明・顔の向きに強い。
    /// マーカーが写っていないフレームでは推論を丸ごと省くので、平時の負荷はほぼゼロ。
    ///
    /// IEyeCoverSource の約束どおり時間方向の平滑化はしない（EyeCoverInput の担当）。
    /// </summary>
    public class MediaPipeEyeCoverSource : MonoBehaviour, IEyeCoverSource, IEyeCoverDiagnostics
    {
        // FaceLandmarker のランドマーク番号（解剖学的な左右。画面の左右ではない）
        private static readonly int[] LeftEyeIndices = { 362, 385, 387, 263, 373, 380 };
        private static readonly int[] RightEyeIndices = { 33, 160, 158, 133, 153, 144 };

        [Header("カメラ")]
        [Tooltip("空なら既定のカメラ。複数台ある会場では WebCamTexture.devices の名前を入れる")]
        [SerializeField] private string deviceName = string.Empty;
        [SerializeField] private int requestedWidth = 640;
        [SerializeField] private int requestedHeight = 360;
        [SerializeField] private int requestedFps = 30;

        [Header("モデル")]
        [Tooltip("Resources 下の face_landmarker_v2.bytes")]
        [SerializeField] private string modelResourceName = "face_landmarker_v2";

        [Header("マーカーの色（蛍光ピンク）")]
        [Tooltip("色相の中心。0.92 ≒ 331°（マゼンタ寄りのピンク）")]
        [SerializeField, Range(0f, 1f)] private float hueCenter = 0.92f;
        [Tooltip("色相の許容幅。広げるほど拾いやすいが、赤い服なども拾い始める")]
        [SerializeField, Range(0.005f, 0.25f)] private float hueTolerance = 0.06f;
        [Tooltip("彩度の下限。蛍光色は彩度が高いので、ここで肌色や壁を落とす")]
        [SerializeField, Range(0f, 1f)] private float minSaturation = 0.45f;
        [Tooltip("明度の下限。暗所のノイズを落とす")]
        [SerializeField, Range(0f, 1f)] private float minValue = 0.35f;

        [Header("判定")]
        [Tooltip("マーカーとみなす面積比の下限")]
        [SerializeField, Range(0.0002f, 0.05f)] private float minAreaRatio = 0.0006f;
        [Tooltip("これを超えたら誤検出（ピンクの服が写り込んだ等）とみなす")]
        [SerializeField, Range(0.05f, 1f)] private float maxAreaRatio = 0.20f;
        [Tooltip("マーカーは丸いので、囲む矩形をどれだけ埋めているかで絞る（円なら約 0.79）")]
        [SerializeField, Range(0f, 1f)] private float minFillRatio = 0.45f;
        [Tooltip("矩形の縦横比の許容上限。細長いピンクを弾く")]
        [SerializeField, Range(1f, 6f)] private float maxAspect = 2.5f;
        [Tooltip("目までの許容距離。両目の間隔に対する倍率")]
        [SerializeField, Range(0.1f, 2f)] private float eyeRadius = 0.60f;
        [Tooltip("マーカー探索の縦解像度。小さいほど軽い")]
        [SerializeField, Range(40, 360)] private int analysisHeight = 120;
        [Tooltip("マーカーが写っていないフレームは顔の推論を省く（負荷を大きく下げる）")]
        [SerializeField] private bool detectFaceOnlyWithMarker = true;
        [Tooltip("顔を見失った直後、最後の目の位置を使い続けるフレーム数")]
        [SerializeField, Min(0)] private int holdFramesOnFaceLost = 10;

        private WebCamTexture cam;
        private Texture2D frameTexture;     // MediaPipe に渡す（上下を反転して詰める）
        private Color32[] camBuffer;
        private Color32[] frameBuffer;
        private FaceLandmarker landmarker;
        private FaceLandmarkerResult result;
        private long lastTimestamp = -1;
        private float lastFrameTime = -1f;
        private bool markerAtEye;
        private Vector2 lastLeftEye;
        private Vector2 lastRightEye;
        private int holdLeft;
        private string status = "未起動";

        public bool FaceFound { get; private set; }
        public float MarkerRatio { get; private set; }
        public float EyeDistance { get; private set; } = -1f;
        public string CoveredEye { get; private set; } = string.Empty;
        public string StatusLine => status;

        /// <summary>キャリブレーション表示用。</summary>
        public Texture PreviewTexture => cam;

        public bool TryCalibrateColor() => CalibrateToVividColor();

        public string SourceName => cam != null ? "MediaPipe(" + cam.deviceName + ")" : "MediaPipe(no camera)";

        /// <summary>カメラが回っていて、直近にフレームを受け取れていること。</summary>
        public bool IsAvailable =>
            cam != null && cam.isPlaying && landmarker != null
            && lastFrameTime >= 0f && Time.time - lastFrameTime <= 1f;

        public EyeState ReadRaw()
        {
            if (!IsAvailable) return EyeState.Lost;
            // マーカーが見えていないなら、顔が映っていなくても「当てていない」で確定できる
            if (MarkerRatio < minAreaRatio) return EyeState.Open;
            // マーカーは見えるのに顔が分からない＝目の位置が決められない。安全側に倒す
            if (!FaceFound) return EyeState.Lost;
            return markerAtEye ? EyeState.Covered : EyeState.Open;
        }

        private void OnEnable()
        {
            StartCamera();
            StartLandmarker();
        }

        private void OnDisable()
        {
            if (landmarker != null)
            {
                landmarker.Close();
                landmarker = null;
            }
            if (cam != null)
            {
                cam.Stop();
                Destroy(cam);
                cam = null;
            }
            if (frameTexture != null)
            {
                Destroy(frameTexture);
                frameTexture = null;
            }
            camBuffer = null;
            frameBuffer = null;
            lastFrameTime = -1f;
            FaceFound = false;
            markerAtEye = false;
        }

        private void Update()
        {
            if (cam == null || !cam.isPlaying || landmarker == null) return;
            if (cam.width <= 16) return;          // 起動直後はダミー解像度
            if (!cam.didUpdateThisFrame) return;

            lastFrameTime = Time.time;
            PrepareFrame();

            bool markerVisible = FindMarker(out Vector2 markerCenter);
            if (!markerVisible && detectFaceOnlyWithMarker)
            {
                // マーカーが無い＝遮眼子を当てていない。顔の推論は省く
                markerAtEye = false;
                CoveredEye = string.Empty;
                EyeDistance = -1f;
                return;
            }

            DetectFace();
            Judge(markerVisible, markerCenter);
        }

        private void StartCamera()
        {
            WebCamDevice[] devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                status = "Webカメラが見つかりません（OS のカメラ使用許可も確認してください）";
                Debug.LogWarning("[MediaPipeEyeCoverSource] " + status, this);
                return;
            }

            string target = deviceName;
            if (!string.IsNullOrEmpty(target) && Array.FindIndex(devices, d => d.name == target) < 0)
            {
                Debug.LogWarning("[MediaPipeEyeCoverSource] カメラ「" + target + "」が無いので既定のカメラを使います。", this);
                target = string.Empty;
            }
            if (string.IsNullOrEmpty(target)) target = devices[0].name;

            cam = new WebCamTexture(target, requestedWidth, requestedHeight, requestedFps);
            cam.Play();
            status = cam.isPlaying ? "カメラ起動中" : "カメラを開始できませんでした";
        }

        private void StartLandmarker()
        {
            var model = Resources.Load<TextAsset>(modelResourceName);
            if (model == null)
            {
                status = "モデル " + modelResourceName + " が Resources にありません";
                Debug.LogError("[MediaPipeEyeCoverSource] " + status, this);
                return;
            }

            try
            {
                var options = new FaceLandmarkerOptions(
                    new BaseOptions(BaseOptions.Delegate.CPU, modelAssetBuffer: model.bytes),
                    runningMode: RunningMode.VIDEO,
                    numFaces: 1);
                landmarker = FaceLandmarker.CreateFromOptions(options);
                result = FaceLandmarkerResult.Alloc(1);
                status = "動作中";
            }
            catch (Exception e)
            {
                status = "MediaPipe の初期化に失敗: " + e.Message;
                Debug.LogError("[MediaPipeEyeCoverSource] " + status, this);
            }
        }

        /// <summary>
        /// カメラの画を MediaPipe 用のテクスチャに詰め直す。
        /// WebCamTexture は左下原点、MediaPipe が読む生データは左上原点なので、行を逆順に写す。
        /// 以降の座標計算は、すべて「左上原点・y は下向き」で統一する。
        /// </summary>
        private void PrepareFrame()
        {
            int w = cam.width;
            int h = cam.height;

            if (camBuffer == null || camBuffer.Length != w * h) camBuffer = new Color32[w * h];
            if (frameBuffer == null || frameBuffer.Length != w * h) frameBuffer = new Color32[w * h];
            if (frameTexture == null || frameTexture.width != w || frameTexture.height != h)
            {
                if (frameTexture != null) Destroy(frameTexture);
                frameTexture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            }

            cam.GetPixels32(camBuffer);
            for (int y = 0; y < h; y++)
            {
                Array.Copy(camBuffer, (h - 1 - y) * w, frameBuffer, y * w, w);
            }

            frameTexture.SetPixels32(frameBuffer);
            frameTexture.Apply(false);
        }

        /// <summary>
        /// 間引いた画素からマーカー色の重心・面積・囲む矩形を求める。
        /// 輪郭を取らずに、矩形の占有率と縦横比で「丸いひと塊か」を近似的に判定する。
        /// </summary>
        private bool FindMarker(out Vector2 center)
        {
            center = Vector2.zero;

            int w = cam.width;
            int h = cam.height;
            int step = Mathf.Max(1, h / Mathf.Max(1, analysisHeight));

            int matched = 0;
            int sampled = 0;
            long sumX = 0;
            long sumY = 0;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;

            for (int y = 0; y < h; y += step)
            {
                int row = y * w;
                for (int x = 0; x < w; x += step)
                {
                    sampled++;
                    if (!IsMarkerColor(frameBuffer[row + x])) continue;

                    matched++;
                    sumX += x;
                    sumY += y;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            MarkerRatio = sampled > 0 ? (float)matched / sampled : 0f;
            if (matched == 0 || MarkerRatio < minAreaRatio || MarkerRatio > maxAreaRatio) return false;

            center = new Vector2((float)sumX / matched, (float)sumY / matched);

            // 囲む矩形が「ほぼ正方形で、よく埋まっている」ならひと塊の丸とみなす
            float boxW = (maxX - minX) + step;
            float boxH = (maxY - minY) + step;
            float aspect = Mathf.Max(boxW, boxH) / Mathf.Max(1f, Mathf.Min(boxW, boxH));
            if (aspect > maxAspect) return false;

            float boxSamples = (boxW / step) * (boxH / step);
            if (boxSamples > 0f && matched / boxSamples < minFillRatio) return false;

            return true;
        }

        private bool IsMarkerColor(Color32 c)
        {
            Color.RGBToHSV(new Color(c.r / 255f, c.g / 255f, c.b / 255f), out float hue, out float sat, out float val);
            if (sat < minSaturation || val < minValue) return false;

            float d = Mathf.Abs(hue - hueCenter);
            if (d > 0.5f) d = 1f - d; // 色相は 0 と 1 が地続き。赤〜ピンクは境目をまたぐ
            return d <= hueTolerance;
        }

        private void DetectFace()
        {
            long stamp = Math.Max((long)(Time.realtimeSinceStartupAsDouble * 1000.0), lastTimestamp + 1);
            lastTimestamp = stamp;

            bool found;
            using (var image = new Image(ImageFormat.Types.Format.Srgba, frameTexture))
            {
                found = landmarker.TryDetectForVideo(image, stamp, null, ref result)
                        && result.faceLandmarks != null && result.faceLandmarks.Count > 0;
            }

            if (found)
            {
                var landmarks = result.faceLandmarks[0].landmarks;
                lastLeftEye = EyeCenter(landmarks, LeftEyeIndices);
                lastRightEye = EyeCenter(landmarks, RightEyeIndices);
                holdLeft = holdFramesOnFaceLost;
                FaceFound = true;
            }
            else if (holdLeft > 0)
            {
                // 遮眼子を当てた瞬間に顔検出が一瞬落ちても、判定を途切れさせない
                holdLeft--;
                FaceFound = true;
            }
            else
            {
                FaceFound = false;
            }
        }

        /// <summary>ランドマークの平均。戻り値はピクセル座標（左上原点）。</summary>
        private Vector2 EyeCenter(List<NormalizedLandmark> landmarks, int[] indices)
        {
            float x = 0f;
            float y = 0f;
            foreach (int i in indices)
            {
                x += landmarks[i].x;
                y += landmarks[i].y;
            }
            // 正規化座標は縦横で尺度が違うので、距離を比べる前にピクセルへ戻す
            return new Vector2(x / indices.Length * cam.width, y / indices.Length * cam.height);
        }

        private void Judge(bool markerVisible, Vector2 markerCenter)
        {
            markerAtEye = false;
            CoveredEye = string.Empty;
            EyeDistance = -1f;

            if (!markerVisible || !FaceFound) return;

            float span = Vector2.Distance(lastLeftEye, lastRightEye);
            if (span <= 1f) return;

            float toLeft = Vector2.Distance(markerCenter, lastLeftEye) / span;
            float toRight = Vector2.Distance(markerCenter, lastRightEye) / span;
            EyeDistance = Mathf.Min(toLeft, toRight);

            if (EyeDistance <= eyeRadius)
            {
                markerAtEye = true;
                CoveredEye = toLeft <= toRight ? "L" : "R";
            }
        }

        /// <summary>今カメラに写っている中で最も鮮やかな色相にマーカー色を合わせる。</summary>
        public bool CalibrateToVividColor()
        {
            if (frameBuffer == null || cam == null || cam.width <= 16) return false;

            const int bins = 36;
            var weight = new float[bins];
            int w = cam.width;
            int h = cam.height;
            int step = Mathf.Max(1, h / Mathf.Max(1, analysisHeight));

            for (int y = 0; y < h; y += step)
            {
                int row = y * w;
                for (int x = 0; x < w; x += step)
                {
                    Color32 c = frameBuffer[row + x];
                    Color.RGBToHSV(new Color(c.r / 255f, c.g / 255f, c.b / 255f),
                        out float hue, out float sat, out float val);
                    if (sat < 0.5f || val < 0.4f) continue;
                    weight[Mathf.Clamp((int)(hue * bins), 0, bins - 1)] += sat * val;
                }
            }

            int peak = 0;
            for (int i = 1; i < bins; i++) if (weight[i] > weight[peak]) peak = i;
            if (weight[peak] <= 0f) return false;

            hueCenter = (peak + 0.5f) / bins;
            return true;
        }
    }
}
