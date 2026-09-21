using UnityEngine;

namespace OverTheRainbow.Stage
{
    /// <summary>
    /// 道の先に浮かぶ赤い気球（企画書 4.2）。進行方向の目印であり、決して辿り着けない。
    ///
    /// プレイヤーの子に置くが、位置はワールド基準で決め直す。
    /// PlayerController はマウスでプレイヤー自身を yaw 回転させるので、
    /// 素直に子の localPosition を使うと、視点を振るたびに気球が真横へ流れてしまう。
    /// ここでは「プレイヤーの位置 ＋ ワールドのオフセット」に毎フレーム置き直すことで、
    /// 前に進んでも同じだけ先にいる（＝近づけない）遠景として見せる。
    /// </summary>
    public class BalloonMarker : MonoBehaviour
    {
        [Header("追従")]
        [Tooltip("未設定なら親、それも無ければ Player タグを探す")]
        [SerializeField] private Transform player;
        [Tooltip("プレイヤーからのワールド基準のずれ。Z が道の先、Y が気球の中心の高さ")]
        // 気球は高さ約 21m（モデル 0.695 × スケール 30）で、この点が見た目の中心になる。
        // 足元（籠の底）＝ Y - 10.4 なので、Y=10 だと道すれすれに浮いて見える。
        // 視力検査の「道の先に気球が接している」絵に合わせた値
        [SerializeField] private Vector3 offset = new Vector3(0f, 10f, 180f);
        [Tooltip("プレイヤーの上下動（落下など）には追従しない。地面の高さを基準に浮かべる")]
        [SerializeField] private bool ignorePlayerHeight = true;

        [Header("揺らぎ")]
        [Tooltip("ゆっくりした上下動の幅（m）。0 で止まる。道に接する高さでは小さめに")]
        [SerializeField, Min(0f)] private float bobAmplitude = 0.5f;
        [Tooltip("上下動の周期（秒）")]
        [SerializeField, Min(0.1f)] private float bobPeriod = 9f;
        [Tooltip("左右のゆらぎの幅（m）。道の中心から外れすぎない程度に")]
        [SerializeField, Min(0f)] private float swayAmplitude = 1.5f;
        [Tooltip("左右のゆらぎの周期（秒）")]
        [SerializeField, Min(0.1f)] private float swayPeriod = 17f;

        [Header("向き")]
        [Tooltip("常にプレイヤーの方へ向ける（横向きの見た目を保つ）。傾きは付けず yaw のみ")]
        [SerializeField] private bool faceCamera = true;
        [Tooltip("向きの微調整（度）")]
        [SerializeField] private float yawOffset = 0f;

        private float baseHeight;

        private void Awake()
        {
            if (player == null) player = transform.parent;
            if (player == null)
            {
                var tagged = GameObject.FindGameObjectWithTag("Player");
                if (tagged != null) player = tagged.transform;
            }
            if (player == null)
            {
                Debug.LogWarning("[BalloonMarker] プレイヤーが見つかりません。その場に留まります。", this);
                enabled = false;
                return;
            }

            baseHeight = player.position.y;
        }

        /// <summary>
        /// 位置決めは LateUpdate。PlayerController が Update で動かしたあとに合わせないと、
        /// 1 フレーム遅れて気球だけが揺れて見える。
        /// </summary>
        private void LateUpdate()
        {
            Vector3 anchor = player.position;
            if (ignorePlayerHeight) anchor.y = baseHeight;

            Vector3 position = anchor + offset;
            position.y += Wave(bobPeriod) * bobAmplitude;
            position.x += Wave(swayPeriod) * swayAmplitude;
            transform.position = position;

            if (!faceCamera) return;

            // 見上げる角度で傾くと模型っぽくなるので、yaw だけプレイヤーへ向ける
            Vector3 toPlayer = player.position - transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(toPlayer) * Quaternion.Euler(0f, yawOffset, 0f);
            }
        }

        private static float Wave(float period) => Mathf.Sin(Time.time / period * Mathf.PI * 2f);
    }
}
