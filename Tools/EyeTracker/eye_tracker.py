#!/usr/bin/env python3
"""
0v3r 7h3 R41nb0w — 遮眼子トラッカー

黒い遮眼子に貼った蛍光ピンクの丸マーカーを追い、
「マーカーが目の位置にあるか」を判定して Unity へ UDP で送る。

役割分担:
  MediaPipe Face Landmarker … 左右の目の座標を取るだけ
  OpenCV (HSV)       … マーカーそのものの検出
  この2つの距離      … 遮眼子を目に当てているかの判定

顔のランドマークから遮蔽を推定するより、
「ピンクの丸が目の近くにあるか」の方が眼鏡・照明・顔の向きに強い。

使い方:
    python eye_tracker.py                 # プレビュー付きで起動
    python eye_tracker.py --no-preview    # 体験会本番（ウィンドウなし）
    python eye_tracker.py --list-cameras  # カメラ一覧
プレビュー中のキー:
    c … 今写っている一番鮮やかな色にマーカー色を合わせる（キャリブレーション）
    s … 現在の設定を calib.json に保存
    q / ESC … 終了
"""

import argparse
import ctypes
import json
import os
import socket
import sys
import threading
import time

import cv2
import numpy as np

try:
    import mediapipe as mp
    from mediapipe.tasks import python as mp_tasks
    from mediapipe.tasks.python import vision as mp_vision
except ImportError:
    sys.exit("mediapipe が入っていません。 pip install -r requirements.txt を実行してください。")

HERE = os.path.dirname(os.path.abspath(__file__))
CALIB_PATH = os.path.join(HERE, "calib.json")
MODEL_DIR = os.path.join(HERE, "models")
MODEL_PATH = os.path.join(MODEL_DIR, "face_landmarker.task")
MODEL_URL = ("https://storage.googleapis.com/mediapipe-models/face_landmarker/"
             "face_landmarker/float16/1/face_landmarker.task")

# Face Landmarker のランドマーク番号（解剖学的な左右。画面の左右ではない）
LEFT_EYE = [362, 385, 387, 263, 373, 380]
RIGHT_EYE = [33, 160, 158, 133, 153, 144]

DEFAULT_CALIB = {
    # OpenCV の HSV は H が 0〜179。蛍光ピンクは 165 前後
    "hue_center": 165,
    "hue_tolerance": 12,
    "sat_min": 110,
    "val_min": 90,
    # マーカーが画面に占める面積の下限・上限（比率）。上限は服などの誤検出よけ
    "area_min": 0.0006,
    "area_max": 0.20,
    # 円形度の下限。丸いマーカーだけを通す（4πA/P²、完全な円で 1.0）
    "circularity_min": 0.45,
    # 目までの許容距離。両目の間隔に対する倍率
    "eye_radius": 0.60,
}


def parent_alive(pid):
    """親プロセス（Unity）が生きているか。Windows は os.kill が実際に殺してしまうので使わない。"""
    if sys.platform == "win32":
        SYNCHRONIZE = 0x00100000
        handle = ctypes.windll.kernel32.OpenProcess(SYNCHRONIZE, False, pid)
        if not handle:
            return False
        ctypes.windll.kernel32.CloseHandle(handle)
        return True
    try:
        os.kill(pid, 0)
        return True
    except OSError:
        return False


def watch_parent(pid):
    """Unity が落ちてもカメラを掴んだまま residual しないよう、親が消えたら自分も終わる。"""
    def loop():
        while True:
            time.sleep(1.0)
            if not parent_alive(pid):
                print("[watchdog] 親プロセスが終了したので停止します")
                os._exit(0)

    threading.Thread(target=loop, daemon=True).start()


def load_calib():
    calib = dict(DEFAULT_CALIB)
    if os.path.exists(CALIB_PATH):
        try:
            with open(CALIB_PATH, "r", encoding="utf-8") as f:
                calib.update(json.load(f))
            print(f"[calib] {CALIB_PATH} を読み込みました")
        except (OSError, ValueError) as e:
            print(f"[calib] 読み込みに失敗（既定値を使います）: {e}")
    return calib


def save_calib(calib):
    with open(CALIB_PATH, "w", encoding="utf-8") as f:
        json.dump(calib, f, indent=2)
    print(f"[calib] {CALIB_PATH} に保存しました")


def ensure_model(path):
    """Face Landmarker のモデルを用意する。無ければ初回だけダウンロードする。"""
    if os.path.exists(path) and os.path.getsize(path) > 1_000_000:
        return path

    os.makedirs(os.path.dirname(path), exist_ok=True)
    print(f"[model] モデルをダウンロードします（初回のみ、約3MB）\n        {MODEL_URL}")
    try:
        import urllib.request
        tmp = path + ".part"
        urllib.request.urlretrieve(MODEL_URL, tmp)
        os.replace(tmp, path)
    except Exception as e:
        sys.exit(f"モデルのダウンロードに失敗しました: {e}\n"
                 f"手動で {MODEL_URL} を {path} に保存してください。")
    print(f"[model] {path} に保存しました")
    return path


def create_landmarker(model_path):
    """Tasks API の FaceLandmarker を作る（mediapipe 0.10 以降。solutions は廃止）。"""
    options = mp_vision.FaceLandmarkerOptions(
        base_options=mp_tasks.BaseOptions(model_asset_path=model_path),
        running_mode=mp_vision.RunningMode.VIDEO,
        num_faces=1,
        output_face_blendshapes=False,
        output_facial_transformation_matrixes=False,
    )
    return mp_vision.FaceLandmarker.create_from_options(options)


def open_camera(index, width, height, wait_seconds):
    """
    カメラを開く。macOS は初回にカメラ使用許可を聞くが、その要求は非同期で、
    OpenCV は答えを待たずに失敗する。許可を押してもらえるよう、開けるまで数えながら待つ。
    """
    deadline = time.time() + wait_seconds
    notified = False

    while True:
        cap = cv2.VideoCapture(index)
        if cap.isOpened():
            cap.set(cv2.CAP_PROP_FRAME_WIDTH, width)
            cap.set(cv2.CAP_PROP_FRAME_HEIGHT, height)
            ok, _ = cap.read()  # 開けても許可待ちだと読めないので、1枚読めるまで確認する
            if ok:
                return cap
        cap.release()

        if not notified:
            print("[camera] カメラを開けません。macOS ならカメラ使用許可のダイアログが出ているはずです。")
            print("[camera] 「許可」を押すとそのまま続行します（最大 "
                  + str(int(wait_seconds)) + " 秒待ちます）。")
            sys.stdout.flush()
            notified = True

        if time.time() >= deadline:
            return None
        time.sleep(1.0)


def list_cameras(limit=8):
    print("接続されているカメラ:")
    found = False
    for i in range(limit):
        cap = cv2.VideoCapture(i)
        if cap.isOpened():
            ok, frame = cap.read()
            if ok:
                h, w = frame.shape[:2]
                print(f"  index {i}: {w}x{h}")
                found = True
        cap.release()
    if not found:
        print("  見つかりません（OS のカメラ使用許可を確認してください）")


def build_mask(hsv, calib):
    """マーカー色の二値マスク。色相は 0 と 179 が地続きなので折り返しを扱う。"""
    lo = (calib["hue_center"] - calib["hue_tolerance"]) % 180
    hi = (calib["hue_center"] + calib["hue_tolerance"]) % 180
    s_min, v_min = calib["sat_min"], calib["val_min"]

    if lo <= hi:
        mask = cv2.inRange(hsv, (lo, s_min, v_min), (hi, 255, 255))
    else:
        mask = cv2.inRange(hsv, (lo, s_min, v_min), (179, 255, 255))
        mask |= cv2.inRange(hsv, (0, s_min, v_min), (hi, 255, 255))

    # 細かいノイズを消してから、欠けた丸を埋める
    kernel = np.ones((5, 5), np.uint8)
    mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, kernel)
    mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, kernel)
    return mask


def find_marker(mask, calib, frame_area):
    """最大の丸い塊を探す。戻り値は (中心(x,y), 面積比) か None。"""
    contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    best = None
    best_area = 0.0

    for c in contours:
        area = cv2.contourArea(c)
        if area <= 0:
            continue
        ratio = area / frame_area
        if ratio < calib["area_min"] or ratio > calib["area_max"]:
            continue

        perimeter = cv2.arcLength(c, True)
        if perimeter <= 0:
            continue
        circularity = 4.0 * np.pi * area / (perimeter * perimeter)
        if circularity < calib["circularity_min"]:
            continue  # 丸くないものは、ピンクでもマーカーではない

        if area > best_area:
            m = cv2.moments(c)
            if m["m00"] == 0:
                continue
            best = ((m["m10"] / m["m00"], m["m01"] / m["m00"]), ratio)
            best_area = area

    return best


def eye_centers(landmarks, w, h):
    """ランドマークから左右の目の中心（ピクセル座標）を出す。"""
    def center(indices):
        xs = [landmarks[i].x * w for i in indices]
        ys = [landmarks[i].y * h for i in indices]
        return (sum(xs) / len(xs), sum(ys) / len(ys))

    return center(LEFT_EYE), center(RIGHT_EYE)


def calibrate_to_vivid(hsv, calib):
    """今写っている中で最も鮮やかな色相にマーカー色を合わせる。"""
    h, s, v = cv2.split(hsv)
    vivid = (s > 120) & (v > 100)
    if np.count_nonzero(vivid) < 50:
        print("[calib] 鮮やかな色が見つかりません。マーカーをカメラに近づけてください")
        return False

    # 彩度で重み付けした色相ヒストグラムの山を採る
    hist = np.bincount(h[vivid].ravel(), weights=s[vivid].ravel().astype(np.float64), minlength=180)
    # 色相の折り返しをまたぐ山も拾えるよう、環状に平滑化する
    smooth = np.convolve(np.r_[hist, hist, hist], np.ones(7) / 7.0, mode="same")[180:360]
    calib["hue_center"] = int(np.argmax(smooth))
    print(f"[calib] hue_center = {calib['hue_center']} に合わせました")
    return True


def make_trackbars(window, calib):
    cv2.createTrackbar("hue", window, calib["hue_center"], 179, lambda v: None)
    cv2.createTrackbar("hue tol", window, calib["hue_tolerance"], 40, lambda v: None)
    cv2.createTrackbar("sat min", window, calib["sat_min"], 255, lambda v: None)
    cv2.createTrackbar("val min", window, calib["val_min"], 255, lambda v: None)
    cv2.createTrackbar("eye r x100", window, int(calib["eye_radius"] * 100), 200, lambda v: None)
    cv2.createTrackbar("circ x100", window, int(calib["circularity_min"] * 100), 100, lambda v: None)


def read_trackbars(window, calib):
    calib["hue_center"] = cv2.getTrackbarPos("hue", window)
    calib["hue_tolerance"] = max(1, cv2.getTrackbarPos("hue tol", window))
    calib["sat_min"] = cv2.getTrackbarPos("sat min", window)
    calib["val_min"] = cv2.getTrackbarPos("val min", window)
    calib["eye_radius"] = max(0.05, cv2.getTrackbarPos("eye r x100", window) / 100.0)
    calib["circularity_min"] = cv2.getTrackbarPos("circ x100", window) / 100.0


def sync_trackbars(window, calib):
    cv2.setTrackbarPos("hue", window, int(calib["hue_center"]))


def main():
    parser = argparse.ArgumentParser(description="遮眼子（蛍光ピンクマーカー）トラッカー")
    parser.add_argument("--camera", type=int, default=0, help="カメラ番号")
    parser.add_argument("--width", type=int, default=640)
    parser.add_argument("--height", type=int, default=480)
    parser.add_argument("--host", default="127.0.0.1", help="送信先（Unity）")
    parser.add_argument("--port", type=int, default=51234)
    parser.add_argument("--no-preview", action="store_true", help="ウィンドウを出さない（本番用）")
    parser.add_argument("--hold-frames", type=int, default=10,
                        help="顔を見失った直後、最後の目の位置を使い続けるフレーム数")
    parser.add_argument("--model", default=MODEL_PATH, help="face_landmarker.task の場所")
    parser.add_argument("--parent-pid", type=int, default=0,
                        help="このプロセス（Unity）が終了したら一緒に終わる")
    parser.add_argument("--camera-wait", type=float, default=30.0,
                        help="カメラが開くまで待つ秒数（macOS の許可ダイアログを待つため）")
    parser.add_argument("--list-cameras", action="store_true")
    args = parser.parse_args()

    if args.list_cameras:
        list_cameras()
        return 0

    print("[起動] 遮眼子トラッカー")
    sys.stdout.flush()

    if args.parent_pid:
        watch_parent(args.parent_pid)

    calib = load_calib()

    # カメラを先に開く。モデルの読み込み（数秒）で許可ダイアログを待たせないため
    cap = open_camera(args.camera, args.width, args.height, args.camera_wait)
    if cap is None:
        print(f"カメラ {args.camera} を開けませんでした。", file=sys.stderr)
        print("・他のアプリがカメラを使っていないか確認してください", file=sys.stderr)
        print("・システム設定 → プライバシーとセキュリティ → カメラ で Unity を許可してください",
              file=sys.stderr)
        print("・カメラ番号が違う場合は --camera N（--list-cameras で確認）", file=sys.stderr)
        return 1
    print("[camera] カメラを開きました")
    sys.stdout.flush()

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    dest = (args.host, args.port)

    window = "0v3r 7h3 R41nb0w - eye tracker"
    if not args.no_preview:
        cv2.namedWindow(window, cv2.WINDOW_NORMAL)
        make_trackbars(window, calib)

    print("[model] 顔ランドマークのモデルを読み込みます")
    sys.stdout.flush()
    landmarker = create_landmarker(ensure_model(args.model))

    print(f"送信先 udp://{args.host}:{args.port}  （Ctrl+C で終了）")
    sys.stdout.flush()

    seq = 0
    last_stamp = -1        # detect_for_video は単調増加のタイムスタンプを要求する
    last_eyes = None       # 顔を見失ったとき用の、最後に見えた目の位置
    hold_left = 0
    fps_t0, fps_n, fps = time.time(), 0, 0.0

    try:
        while True:
            ok, frame = cap.read()
            if not ok:
                time.sleep(0.05)
                continue

            frame = cv2.flip(frame, 1)  # 鏡像。プレイヤーが自分の動きと一致して見えるように
            h, w = frame.shape[:2]
            frame_area = float(w * h)

            if not args.no_preview:
                read_trackbars(window, calib)

            hsv = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)

            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            stamp = max(int(time.monotonic() * 1000), last_stamp + 1)
            last_stamp = stamp
            results = landmarker.detect_for_video(
                mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb), stamp)

            if results.face_landmarks:
                lm = results.face_landmarks[0]
                last_eyes = eye_centers(lm, w, h)
                hold_left = args.hold_frames
                face_found = True
            elif hold_left > 0:
                # 遮眼子を当てた瞬間に顔検出が一瞬落ちても判定を途切れさせない
                hold_left -= 1
                face_found = last_eyes is not None
            else:
                last_eyes = None
                face_found = False

            mask = build_mask(hsv, calib)
            marker = find_marker(mask, calib, frame_area)

            covered = False
            eye = ""
            dist_norm = -1.0
            ratio = marker[1] if marker else 0.0

            if marker and face_found and last_eyes:
                (mx, my), _ = marker
                left, right = last_eyes
                eye_span = float(np.hypot(left[0] - right[0], left[1] - right[1]))
                if eye_span > 1.0:
                    d_left = float(np.hypot(mx - left[0], my - left[1])) / eye_span
                    d_right = float(np.hypot(mx - right[0], my - right[1])) / eye_span
                    dist_norm = min(d_left, d_right)
                    if dist_norm <= calib["eye_radius"]:
                        covered = True
                        eye = "L" if d_left <= d_right else "R"

            seq += 1
            packet = {
                "seq": seq,
                "face": 1 if face_found else 0,
                "covered": 1 if covered else 0,
                "eye": eye,
                "ratio": round(ratio, 5),
                "dist": round(dist_norm, 4),
            }
            sock.sendto(json.dumps(packet).encode("utf-8"), dest)

            fps_n += 1
            if time.time() - fps_t0 >= 1.0:
                fps = fps_n / (time.time() - fps_t0)
                fps_t0, fps_n = time.time(), 0

            if args.no_preview:
                continue

            draw_preview(frame, mask, last_eyes, marker, calib, packet, fps)
            cv2.imshow(window, frame)

            key = cv2.waitKey(1) & 0xFF
            if key in (ord("q"), 27):
                break
            if key == ord("c"):
                if calibrate_to_vivid(hsv, calib):
                    sync_trackbars(window, calib)
            if key == ord("s"):
                save_calib(calib)

    except KeyboardInterrupt:
        pass
    finally:
        # 落ちたときに Unity 側が「当てたまま」で固まらないよう、最後に解除を送る
        sock.sendto(json.dumps({"seq": seq + 1, "face": 0, "covered": 0,
                                "eye": "", "ratio": 0.0, "dist": -1.0}).encode("utf-8"), dest)
        cap.release()
        landmarker.close()
        sock.close()
        cv2.destroyAllWindows()

    return 0


def draw_preview(frame, mask, eyes, marker, calib, packet, fps):
    h, w = frame.shape[:2]

    # 検出マスクを右下に小さく重ねる
    small = cv2.resize(mask, (w // 4, h // 4))
    frame[h - h // 4:, w - w // 4:] = cv2.cvtColor(small, cv2.COLOR_GRAY2BGR)

    if eyes:
        left, right = eyes
        eye_span = float(np.hypot(left[0] - right[0], left[1] - right[1]))
        radius = int(eye_span * calib["eye_radius"])
        for center in (left, right):
            cv2.circle(frame, (int(center[0]), int(center[1])), radius, (90, 200, 90), 1)
            cv2.circle(frame, (int(center[0]), int(center[1])), 3, (90, 200, 90), -1)

    if marker:
        (mx, my), _ = marker
        color = (0, 255, 255) if packet["covered"] else (200, 120, 255)
        cv2.circle(frame, (int(mx), int(my)), 12, color, 2)

    state = "COVERED " + packet["eye"] if packet["covered"] else ("open" if packet["face"] else "NO FACE")
    color = (0, 255, 255) if packet["covered"] else ((220, 220, 220) if packet["face"] else (80, 80, 230))
    cv2.putText(frame, state, (12, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.8, color, 2)
    cv2.putText(frame,
                f"area {packet['ratio']:.4f}  dist {packet['dist']:.2f} / {calib['eye_radius']:.2f}  {fps:.0f}fps",
                (12, 56), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (200, 200, 200), 1)
    cv2.putText(frame, "c: pick color   s: save   q: quit",
                (12, h - 12), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (150, 150, 150), 1)


if __name__ == "__main__":
    sys.exit(main())
