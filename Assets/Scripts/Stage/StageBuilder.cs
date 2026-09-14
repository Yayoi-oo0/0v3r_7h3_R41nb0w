using System.Collections.Generic;
using UnityEngine;

namespace OverTheRainbow.Stage
{
    /// <summary>
    /// 一本道ステージを手続き的に生成する（企画書 4.2 ステージ構造 / Phase 1 白ボックス）。
    ///
    /// 穴の配置は「先に必ず通れるレーン列（解）を決め、それ以外のセルに穴を開ける」方式。
    /// 穴の数を増やしても道が断絶しないことを保証する。
    /// </summary>
    public class StageBuilder : MonoBehaviour
    {
        [Header("コースの形")]
        [Tooltip("横方向のレーン数。奇数にすると中央スタートになる")]
        [SerializeField, Min(2)] private int laneCount = 3;
        [Tooltip("1 セルの一辺 m")]
        [SerializeField, Min(0.5f)] private float cellSize = 2f;
        [Tooltip("進行方向のセル数。全長 = cellCount * cellSize")]
        [SerializeField, Min(4)] private int cellCount = 45;

        [Header("穴（企画書 7. 穴の数 8〜12）")]
        [SerializeField, Min(0)] private int holeCount = 10;
        [Tooltip("スタート直後の安全地帯（セル数）")]
        [SerializeField, Min(0)] private int safeCellsAtStart = 3;
        [Tooltip("ゴール手前の安全地帯（セル数）")]
        [SerializeField, Min(0)] private int safeCellsAtEnd = 2;
        [Tooltip("0 なら毎回ランダム。固定値にすると同じコースを再現できる")]
        [SerializeField] private int seed = 0;

        [Header("壁")]
        [Tooltip("コース脇に落下防止の壁を置く")]
        [SerializeField] private bool buildSideWalls = true;
        [SerializeField] private float wallHeight = 3f;

        [Header("見た目（Phase 1 は白ボックス）")]
        [SerializeField] private Color floorColor = new Color(0.78f, 0.76f, 0.72f);
        [SerializeField] private Color wallColor = new Color(0.55f, 0.54f, 0.52f);
        [SerializeField] private Color goalColor = new Color(0.85f, 0.12f, 0.16f);

        [Header("霧（企画書 6.5 通常時：フォグ濃い）")]
        [SerializeField] private bool applyFogSettings = true;
        [SerializeField] private Color fogColor = new Color(0.80f, 0.79f, 0.78f);
        [SerializeField] private float fogDensity = 0.035f;

        /// <summary>プレイヤーの初期位置。MainSceneBuilder から参照する。</summary>
        public Vector3 PlayerSpawn { get; private set; }
        /// <summary>ゴール（赤い気球）の位置。</summary>
        public Vector3 GoalPosition { get; private set; }

        private Transform root;

        private void Awake()
        {
            Build();
        }

        [ContextMenu("ステージを生成し直す")]
        public void Build()
        {
            ClearBuilt();

            var rootGo = new GameObject("StageRoot");
            root = rootGo.transform;
            root.SetParent(transform, false);

            System.Random rng = seed == 0 ? new System.Random() : new System.Random(seed);

            int[] solutionLane = BuildSolutionLanes(rng);
            HashSet<int> holes = CarveHoles(rng, solutionLane);

            Material floorMat = CreateMaterial("Floor", floorColor);
            Material wallMat = CreateMaterial("Wall", wallColor);

            for (int cell = 0; cell < cellCount; cell++)
            {
                for (int lane = 0; lane < laneCount; lane++)
                {
                    if (holes.Contains(CellKey(cell, lane))) continue; // 穴＝床を置かない
                    CreateBox(
                        "Floor_" + cell + "_" + lane,
                        CellCenter(cell, lane) + Vector3.down * (cellSize * 0.25f),
                        new Vector3(cellSize, cellSize * 0.5f, cellSize),
                        floorMat);
                }
            }

            if (buildSideWalls)
            {
                float halfWidth = laneCount * cellSize * 0.5f;
                float length = cellCount * cellSize;
                float centerZ = length * 0.5f - cellSize * 0.5f;
                for (int side = -1; side <= 1; side += 2)
                {
                    CreateBox(
                        side < 0 ? "Wall_Left" : "Wall_Right",
                        new Vector3(side * (halfWidth + 0.25f), wallHeight * 0.5f - cellSize * 0.5f, centerZ),
                        new Vector3(0.5f, wallHeight, length),
                        wallMat);
                }
            }

            PlayerSpawn = CellCenter(0, laneCount / 2) + Vector3.up * 1.0f;
            GoalPosition = CellCenter(cellCount - 1, laneCount / 2) + Vector3.up * 1.4f;
            CreateGoal(GoalPosition);

            if (applyFogSettings)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogDensity = fogDensity;
            }
        }

        /// <summary>1 セルごとに ±1 レーンまで動く「必ず通れる道」を作る。</summary>
        private int[] BuildSolutionLanes(System.Random rng)
        {
            var lanes = new int[cellCount];
            int current = laneCount / 2;
            for (int cell = 0; cell < cellCount; cell++)
            {
                if (cell > 0)
                {
                    current = Mathf.Clamp(current + rng.Next(-1, 2), 0, laneCount - 1);
                }
                lanes[cell] = current;
            }
            return lanes;
        }

        /// <summary>解の道以外のセルから穴を選ぶ。</summary>
        private HashSet<int> CarveHoles(System.Random rng, int[] solutionLane)
        {
            var candidates = new List<int>();
            int from = Mathf.Clamp(safeCellsAtStart, 0, cellCount);
            int to = Mathf.Clamp(cellCount - safeCellsAtEnd, 0, cellCount);

            for (int cell = from; cell < to; cell++)
            {
                for (int lane = 0; lane < laneCount; lane++)
                {
                    if (lane == solutionLane[cell]) continue;
                    candidates.Add(CellKey(cell, lane));
                }
            }

            // Fisher-Yates で先頭 holeCount 個だけ確定させる
            var holes = new HashSet<int>();
            int take = Mathf.Min(holeCount, candidates.Count);
            for (int i = 0; i < take; i++)
            {
                int j = rng.Next(i, candidates.Count);
                int tmp = candidates[i];
                candidates[i] = candidates[j];
                candidates[j] = tmp;
                holes.Add(candidates[i]);
            }

            if (take < holeCount)
            {
                Debug.LogWarning("[StageBuilder] 穴を " + holeCount + " 個置けません（最大 " + candidates.Count + " 個）。コースを長くするかレーンを増やしてください。", this);
            }
            return holes;
        }

        private int CellKey(int cell, int lane)
        {
            return cell * laneCount + lane;
        }

        private Vector3 CellCenter(int cell, int lane)
        {
            float x = (lane - (laneCount - 1) * 0.5f) * cellSize;
            float z = cell * cellSize;
            return new Vector3(x, 0f, z);
        }

        private void CreateBox(string name, Vector3 center, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(root, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private void CreateGoal(Vector3 position)
        {
            var balloon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            balloon.name = "Goal_RedBalloon";
            balloon.transform.SetParent(root, false);
            balloon.transform.localPosition = position;
            balloon.transform.localScale = Vector3.one * 1.2f;
            balloon.GetComponent<MeshRenderer>().sharedMaterial = CreateMaterial("Goal", goalColor);

            var col = balloon.GetComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 0.9f; // 見た目より少し広く取り、掠めても取れるようにする
            balloon.AddComponent<GoalTrigger>();
        }

        private static Material CreateMaterial(string name, Color color)
        {
            // URP へ移行しても動くよう、シェーダを順に探す
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Diffuse");

            var mat = new Material(shader);
            mat.name = "Mat_" + name;
            mat.color = color;
            return mat;
        }

        private void ClearBuilt()
        {
            var existing = transform.Find("StageRoot");
            if (existing == null) return;
            if (Application.isPlaying) Destroy(existing.gameObject);
            else DestroyImmediate(existing.gameObject);
        }
    }
}
