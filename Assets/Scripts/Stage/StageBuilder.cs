using System.Collections.Generic;
using UnityEngine;

namespace OverTheRainbow.Stage
{
    /// <summary>
    /// 前方へ無限に続く一本道を、チャンク単位で生成・破棄する（企画書 4.2 ステージ構造 / 7. パラメータ表）。
    ///
    /// 穴の配置は「先に必ず通れるレーン列（解）を決め、それ以外のセルに穴を開ける」方式。
    /// 解はチャンクをまたいで続くので、穴を増やしても道が断絶しない。
    /// </summary>
    public class StageBuilder : MonoBehaviour
    {
        [Header("コースの形")]
        [Tooltip("横方向のレーン数。奇数にすると中央スタートになる")]
        [SerializeField, Min(2)] private int laneCount = 3;
        [Tooltip("1 セルの一辺 m")]
        [SerializeField, Min(0.5f)] private float cellSize = 2f;
        [Tooltip("1 チャンクの進行方向のセル数。チャンク長 = cellsPerChunk * cellSize")]
        [SerializeField, Min(1)] private int cellsPerChunk = 5;

        [Header("生成・破棄")]
        [Tooltip("未設定なら Player タグのオブジェクト")]
        [SerializeField] private Transform player;
        [Tooltip("プレイヤーの何 m 先まで生成しておくか")]
        [SerializeField, Min(0f)] private float generateAheadDistance = 50f;
        [Tooltip("開始時にスタート地点より後ろへ生成しておく距離")]
        [SerializeField, Min(0f)] private float initialBehindDistance = 20f;
        [Tooltip("闇の前面からさらに何 m 後ろのチャンクを破棄するか")]
        [SerializeField, Min(0f)] private float destroyBehindDarkness = 5f;

        [Header("穴（企画書 7. パラメータ表）")]
        [Tooltip("スタート地点からこの距離までは穴を開けない")]
        [SerializeField, Min(0f)] private float safeDistance = 10f;
        [SerializeField, Min(0)] private int minHolesAtStart = 1;
        [SerializeField, Min(0)] private int maxHolesAtStart = 2;
        [Tooltip("この距離ごとに、1 チャンクの穴の数を 1 つ増やす")]
        [SerializeField, Min(1f)] private float difficultyStepDistance = 50f;
        [SerializeField, Min(0)] private int maxHolesPerChunk = 4;
        [Tooltip("0 なら毎回ランダム。固定値にすると同じコースを再現できる")]
        [SerializeField] private int seed = 0;

        [Header("壁")]
        [Tooltip("コース脇に落下防止の壁を置く")]
        [SerializeField] private bool buildSideWalls = true;
        [SerializeField] private float wallHeight = 3f;

        [Header("見た目（Phase 1 は白ボックス）")]
        [Tooltip("URP/Lit のマテリアル。これを複製して色を変える（ビルドでシェーダが削除されないようアセットで持つ）")]
        [SerializeField] private Material baseMaterial;
        [SerializeField] private Color floorColor = new Color(0.78f, 0.76f, 0.72f);
        [SerializeField] private Color wallColor = new Color(0.55f, 0.54f, 0.52f);

        /// <summary>プレイヤーの初期位置（先頭セルの中央レーン、床上）。</summary>
        public Vector3 PlayerSpawn => CellCenter(0, laneCount / 2) + Vector3.up * 1.0f;

        private struct Chunk
        {
            public int Index;
            public GameObject Root;
        }

        private readonly Queue<Chunk> chunks = new Queue<Chunk>();
        private DarknessChaser darkness;
        private Transform root;
        private System.Random rng;
        private Material floorMat;
        private Material wallMat;
        private int nextChunkIndex;
        private int solutionLane; // 直前のセルで「必ず通れる」レーン

        private void Awake()
        {
            if (player == null)
            {
                var go = GameObject.FindWithTag("Player");
                if (go != null) player = go.transform;
            }
            // 古いシーンで付け忘れていても闇が出るようにする
            darkness = GetComponent<DarknessChaser>();
            if (darkness == null) darkness = gameObject.AddComponent<DarknessChaser>();

            rng = seed == 0 ? new System.Random() : new System.Random(seed);
            floorMat = CreateMaterial("Floor", floorColor);
            wallMat = CreateMaterial("Wall", wallColor);

            root = new GameObject("StageRoot").transform;
            root.SetParent(transform, false);

            nextChunkIndex = -Mathf.CeilToInt(initialBehindDistance / ChunkLength);
            solutionLane = laneCount / 2;
            GenerateAhead();
        }

        private void Update()
        {
            GenerateAhead();
            DestroyBehind();
        }

        private float ChunkLength => cellsPerChunk * cellSize;

        private void GenerateAhead()
        {
            float playerZ = player != null ? player.position.z : 0f;
            while (ChunkStartZ(nextChunkIndex) < playerZ + generateAheadDistance)
            {
                chunks.Enqueue(BuildChunk(nextChunkIndex));
                nextChunkIndex++;
            }
        }

        /// <summary>闇に飲まれたチャンクを消す。闇がなければプレイヤーから十分離れたものを消す。</summary>
        private void DestroyBehind()
        {
            float limitZ = darkness != null
                ? darkness.FrontZ - destroyBehindDarkness
                : (player != null ? player.position.z : 0f) - initialBehindDistance;

            // チャンクの奥側の端（= 次のチャンクの手前端）が limitZ より後ろなら丸ごと消す
            while (chunks.Count > 0 && ChunkStartZ(chunks.Peek().Index + 1) < limitZ)
            {
                Destroy(chunks.Dequeue().Root);
            }
        }

        private Chunk BuildChunk(int chunkIndex)
        {
            var chunkRoot = new GameObject("Chunk_" + chunkIndex);
            chunkRoot.transform.SetParent(root, false);

            int firstCell = chunkIndex * cellsPerChunk;
            HashSet<int> holes = CarveHoles(chunkIndex, firstCell);

            for (int cell = firstCell; cell < firstCell + cellsPerChunk; cell++)
            {
                for (int lane = 0; lane < laneCount; lane++)
                {
                    if (holes.Contains(CellKey(cell - firstCell, lane))) continue; // 穴＝床を置かない
                    CreateBox(
                        chunkRoot.transform,
                        "Floor_" + cell + "_" + lane,
                        CellCenter(cell, lane) + Vector3.down * (cellSize * 0.25f),
                        new Vector3(cellSize, cellSize * 0.5f, cellSize),
                        floorMat);
                }
            }

            if (buildSideWalls)
            {
                float halfWidth = laneCount * cellSize * 0.5f;
                float centerZ = ChunkStartZ(chunkIndex) + ChunkLength * 0.5f;
                for (int side = -1; side <= 1; side += 2)
                {
                    CreateBox(
                        chunkRoot.transform,
                        side < 0 ? "Wall_Left" : "Wall_Right",
                        new Vector3(side * (halfWidth + 0.25f), wallHeight * 0.5f - cellSize * 0.5f, centerZ),
                        new Vector3(0.5f, wallHeight, ChunkLength),
                        wallMat);
                }
            }

            return new Chunk { Index = chunkIndex, Root = chunkRoot };
        }

        /// <summary>
        /// 1 セルごとに ±1 レーンまで動く「必ず通れる道」を決め、それ以外のセルから穴を選ぶ。
        /// レーンを移るセルでは移る前のレーンも残し、斜めの角だけでつながる道にならないようにする。
        /// </summary>
        private HashSet<int> CarveHoles(int chunkIndex, int firstCell)
        {
            var candidates = new List<int>();
            for (int cell = firstCell; cell < firstCell + cellsPerChunk; cell++)
            {
                int previousLane = solutionLane;
                solutionLane = Mathf.Clamp(solutionLane + rng.Next(-1, 2), 0, laneCount - 1);

                if (CellCenter(cell, 0).z < safeDistance) continue;

                for (int lane = 0; lane < laneCount; lane++)
                {
                    if (lane == solutionLane || lane == previousLane) continue;
                    candidates.Add(CellKey(cell - firstCell, lane));
                }
            }

            // 距離に応じて穴を増やす（企画書 4.2：距離に応じて難易度を段階的に上げる）
            int level = Mathf.Max(0, Mathf.FloorToInt(ChunkStartZ(chunkIndex) / difficultyStepDistance));
            int min = Mathf.Min(minHolesAtStart + level, maxHolesPerChunk);
            int max = Mathf.Min(maxHolesAtStart + level, maxHolesPerChunk);
            int take = Mathf.Min(rng.Next(min, max + 1), candidates.Count);

            // Fisher-Yates で先頭 take 個だけ確定させる
            var holes = new HashSet<int>();
            for (int i = 0; i < take; i++)
            {
                int j = rng.Next(i, candidates.Count);
                int tmp = candidates[i];
                candidates[i] = candidates[j];
                candidates[j] = tmp;
                holes.Add(candidates[i]);
            }
            return holes;
        }

        /// <summary>チャンク内でのセルの通し番号。</summary>
        private int CellKey(int cellInChunk, int lane)
        {
            return cellInChunk * laneCount + lane;
        }

        private Vector3 CellCenter(int cell, int lane)
        {
            float x = (lane - (laneCount - 1) * 0.5f) * cellSize;
            float z = cell * cellSize;
            return new Vector3(x, 0f, z);
        }

        /// <summary>チャンクの手前端の Z。セルの中心が cell * cellSize なので半セルずれる。</summary>
        private float ChunkStartZ(int chunkIndex)
        {
            return chunkIndex * ChunkLength - cellSize * 0.5f;
        }

        private static void CreateBox(Transform parent, string name, Vector3 center, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        /// <summary>baseMaterial を複製して色を変える。DarknessChaser からも使う。</summary>
        public Material CreateMaterial(string name, Color color)
        {
            // 未設定のときはエディタ上でだけ動く保険（ビルドではシェーダが見つからないことがある）
            var mat = baseMaterial != null
                ? new Material(baseMaterial)
                : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.name = "Mat_" + name;
            mat.color = color;
            return mat;
        }
    }
}
