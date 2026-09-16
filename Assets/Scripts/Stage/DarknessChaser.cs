using UnityEngine;
using UnityEngine.Rendering;
using OverTheRainbow.Core;

namespace OverTheRainbow.Stage
{
    /// <summary>
    /// 後ろから迫ってくる闇。遮眼子で立ち止まっている間も止まらない。
    /// 前面がプレイヤーに追いついたらゲームオーバー。
    /// </summary>
    [RequireComponent(typeof(StageBuilder))]
    public class DarknessChaser : MonoBehaviour
    {
        [Tooltip("未設定なら Player タグのオブジェクト")]
        [SerializeField] private Transform player;

        [Header("迫る速さ")]
        [Tooltip("開始時、プレイヤーの何 m 後ろにいるか")]
        [SerializeField, Min(0f)] private float startDistanceBehind = 10f;
        [Tooltip("開始時の速さ m/s。プレイヤーの移動速度は 3.0 m/s")]
        [SerializeField, Min(0f)] private float startSpeed = 1.5f;
        [Tooltip("闇が 100 m 進むごとに増える速さ m/s")]
        [SerializeField, Min(0f)] private float speedGainPer100m = 0.3f;
        [SerializeField, Min(0f)] private float maxSpeed = 3.5f;

        [Header("見た目")]
        [SerializeField] private Color color = Color.black;
        [Tooltip("闇の箱の大きさ（幅, 高さ, 奥行き）。前面から後ろへ奥行きぶん伸びる")]
        [SerializeField] private Vector3 visualSize = new Vector3(40f, 40f, 80f);

        private Transform visual;
        private float startZ;

        /// <summary>闇の前面の Z。これより後ろは飲まれている。</summary>
        public float FrontZ { get; private set; }

        /// <summary>プレイヤーまでの距離 m。プレイヤーがいなければ無限大。</summary>
        public float DistanceToPlayer => player != null ? player.position.z - FrontZ : float.PositiveInfinity;

        private void Awake()
        {
            if (player == null)
            {
                var go = GameObject.FindWithTag("Player");
                if (go != null) player = go.transform;
            }

            startZ = (player != null ? player.position.z : 0f) - startDistanceBehind;
            FrontZ = startZ;
            BuildVisual();
        }

        private void Update()
        {
            GameManager gm = GameManager.Instance;
            if (gm != null && gm.State != GameState.Playing) return;

            float speed = Mathf.Min(maxSpeed, startSpeed + speedGainPer100m * (FrontZ - startZ) / 100f);
            FrontZ += speed * Time.deltaTime;
            UpdateVisualPosition();

            if (player != null && player.position.z <= FrontZ && gm != null)
            {
                gm.TriggerGameOver(GameOverReason.Darkness);
            }
        }

        /// <summary>光を反射しない真っ黒な箱。影を落とすと床が暗くなるので切る。</summary>
        private void BuildVisual()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Darkness";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.localScale = visualSize;

            var renderer = go.GetComponent<MeshRenderer>();
            Material mat = GetComponent<StageBuilder>().CreateMaterial("Darkness", color);
            mat.SetFloat("_Smoothness", 0f);
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            visual = go.transform;
            UpdateVisualPosition();
        }

        private void UpdateVisualPosition()
        {
            visual.position = new Vector3(0f, 0f, FrontZ - visualSize.z * 0.5f);
        }
    }
}
