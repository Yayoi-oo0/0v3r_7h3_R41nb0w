using UnityEngine;
using OverTheRainbow.Core;

namespace OverTheRainbow.Player
{
    /// <summary>
    /// 到達距離の計測（企画書 4.4 終了条件とスコア）。
    /// スタート地点から前方向（Z 軸）に進んだ最大値をスコアにする。
    /// </summary>
    public class DistanceTracker : MonoBehaviour
    {
        private CharacterController controller;
        private float startZ;

        /// <summary>到達距離（m）。後ろに下がっても減らない。</summary>
        public float Distance { get; private set; }

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            startZ = transform.position.z;
        }

        private void Update()
        {
            GameManager gm = GameManager.Instance;
            if (gm != null && gm.State != GameState.Playing) return;

            // 落下中は数えない。穴に落ちたときは「落下地点の手前まで」の距離になる
            if (controller != null && !controller.isGrounded) return;

            Distance = Mathf.Max(Distance, transform.position.z - startZ);
        }
    }
}
