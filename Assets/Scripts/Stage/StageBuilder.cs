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

        [Header("壁・柵")]
        [Tooltip("コース脇に落下防止の当たり判定を置く")]
        [SerializeField] private bool buildSideWalls = true;
        [SerializeField] private float wallHeight = 3f;
        [Tooltip("柵のモデル（fence_single）。設定すると、白ボックスの壁の代わりにこれを並べる")]
        [SerializeField] private GameObject sideFencePrefab;
        [Tooltip("柵に貼るマテリアル。未設定ならモデル付属のものを使う（Built-in 用だと URP ではピンクになる）")]
        [SerializeField] private Material fenceMaterial;
        [Tooltip("柵の大きさ倍率")]
        [SerializeField, Min(0.01f)] private float fenceScale = 1f;
        [Tooltip("コース中心からの距離 m。既定の壁と同じ位置")]
        [SerializeField] private float fenceOffsetFromCenter = 3.25f;
        [Tooltip("柵どうしの重なり m。少し重ねると繋ぎ目が目立たない")]
        [SerializeField] private float fenceOverlap = 0.02f;

        [Header("見た目（Phase 1 は白ボックス）")]
        [Tooltip("URP/Lit のマテリアル。これを複製して色を変える（ビルドでシェーダが削除されないようアセットで持つ）")]
        [SerializeField] private Material baseMaterial;
        [SerializeField] private Color floorColor = new Color(0.78f, 0.76f, 0.72f);
        [SerializeField] private Color wallColor = new Color(0.55f, 0.54f, 0.52f);

        [Header("道路")]
        [Tooltip("真上から見た道路テクスチャ。横幅いっぱいでコース 1 本ぶん。未設定なら白ボックスのまま")]
        [SerializeField] private Texture roadTexture;
        [SerializeField] private Texture roadNormalMap;
        [Tooltip("テクスチャ 1 枚が覆う進行方向の距離 m。道幅（レーン数 × セル）と揃えると絵の比率が崩れない")]
        [SerializeField, Min(0.5f)] private float roadTextureLength = 6f;

        [Header("奈落（穴の下）")]
        [Tooltip("コースの下に暗い床を敷く。無いと穴から空が透けて、ぼやけた視界でも穴の位置が分かってしまう")]
        [SerializeField] private bool buildAbyss = true;
        [Tooltip("床面からの深さ m。PlayerController の fallThresholdY より深くする。浅いと、落下が止まる前に奈落を突き抜けて裏から空が見える")]
        [SerializeField, Min(1f)] private float abyssDepth = 6f;
        [Tooltip("コースの左右にはみ出させる幅 m。穴を斜めに覗いたとき、奈落の端から空が見えないように")]
        [SerializeField, Min(0f)] private float abyssSideMargin = 30f;
        [Tooltip("真っ黒だと逆に穴が目立つ。アスファルトより一段暗いくらいにすると、ぼやけた視界で溶け込む")]
        [SerializeField] private Color abyssColor = new Color(0.15f, 0.15f, 0.14f);

        [Header("平原（道の脇）")]
        [Tooltip("道の両脇に芝生を敷く。見た目だけで当たり判定は持たせない（乗れると穴を迂回できてしまう）")]
        [SerializeField] private bool buildGrass = true;
        [SerializeField] private Color grassColor = new Color(0.45f, 0.58f, 0.28f);
        [Tooltip("繰り返して貼る芝のテクスチャ。未設定なら単色")]
        [SerializeField] private Texture grassTexture;
        [Tooltip("テクスチャ 1 枚が覆う距離 m")]
        [SerializeField, Min(0.5f)] private float grassTextureLength = 8f;
        [Tooltip("片側の奥行き m。地平線まで届く幅にする")]
        [SerializeField, Min(1f)] private float grassSideWidth = 500f;
        [Tooltip("進行方向の長さ m。カメラの Far Clip より長くしておく")]
        [SerializeField, Min(1f)] private float grassLength = 1200f;
        [Tooltip("路面よりどれだけ低くするか m。少し下げると道が一段高く見え、縁もはっきりする")]
        [SerializeField, Min(0f)] private float grassDropBelowRoad = 0.15f;
        [Tooltip("脇の壁を見せる。平原を敷くなら切る（当たり判定は残るので落ちない）")]
        [SerializeField] private bool showSideWalls = true;

        [Header("遠景の道（見た目だけ）")]
        [Tooltip("生成済みの道が途切れた先に、穴のない道を描き足して地平線まで繋げる")]
        [SerializeField] private bool buildFarRoad = true;
        [SerializeField, Min(1f)] private float farRoadLength = 1000f;
        [Tooltip("本物の道と重なる部分で z ファイティングしないよう、わずかに沈める m")]
        [SerializeField, Min(0f)] private float farRoadSink = 0.03f;

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
        private Material[,] roadMats; // [レーン, 進行方向の位置] ごとに切り出す位置が違う
        private int roadTilesAlong;
        private Transform abyss;
        private float fenceStep;        // 柵 1 枚が進行方向に占める長さ
        private float fenceBottomLift;  // 足元を路面に合わせるための持ち上げ
        private bool fenceAlongX;       // モデルが X 方向に伸びているか（Z 向きに回す必要があるか）
        private bool fenceMeasured;
        private Transform[] grassSides;
        private Material grassMat;
        private Transform farRoad;
        private Material farRoadMat;
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
            CreateRoadMaterials();

            root = new GameObject("StageRoot").transform;
            root.SetParent(transform, false);

            if (buildAbyss) CreateAbyss();
            if (buildGrass) CreateGrass();
            if (buildFarRoad) CreateFarRoad();

            nextChunkIndex = -Mathf.CeilToInt(initialBehindDistance / ChunkLength);
            solutionLane = laneCount / 2;
            GenerateAhead();
        }

        private void Update()
        {
            GenerateAhead();
            DestroyBehind();
            FollowAbyss();
            FollowGrass();
            FollowFarRoad();
        }

        /// <summary>
        /// コースの下に、プレイヤーについて動く暗い床を敷く。見た目だけで当たり判定は持たせない。
        /// 持たせると落下の挙動に割り込み、穴に落ちてもゲームオーバーにならないことがあった。
        /// 突き抜けないよう、ゲームオーバー後の落下は PlayerController 側で止めている。
        /// </summary>
        private void CreateAbyss()
        {
            float width = laneCount * cellSize + abyssSideMargin * 2f;
            // 生成済みの範囲（前方・後方）を、プレイヤーがどこにいても覆える長さ
            float length = 2f * (Mathf.Max(generateAheadDistance, initialBehindDistance) + ChunkLength);
            const float thickness = 1f;

            var go = CreateBox(transform, "Abyss",
                new Vector3(0f, -abyssDepth - thickness * 0.5f, 0f),
                new Vector3(width, thickness, length),
                CreateMaterial("Abyss", abyssColor));
            Destroy(go.GetComponent<Collider>());

            // 床の影が落ちると、穴の真下だけ日が差して明るくなり、かえって穴の位置が浮く
            var abyssRenderer = go.GetComponent<MeshRenderer>();
            abyssRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            abyssRenderer.receiveShadows = false;

            abyss = go.transform;
            FollowAbyss();
        }

        private void FollowAbyss()
        {
            if (abyss == null || player == null) return;

            Vector3 p = abyss.localPosition;
            p.z = player.position.z;
            abyss.localPosition = p;
        }

        /// <summary>チャンク 1 つぶんの柵を並べる。</summary>
        private void BuildFenceRow(Transform parent, int side, int chunkIndex)
        {
            MeasureFence();
            if (fenceStep <= 0f) return;

            float startZ = ChunkStartZ(chunkIndex);
            // チャンクの長さにぴったり収まる枚数で割り、端が半端に飛び出さないようにする
            int count = Mathf.Max(1, Mathf.RoundToInt(ChunkLength / fenceStep));
            float step = ChunkLength / count;

            // モデルが X 方向に伸びているなら Z 向きへ回す。裏表があるので、右側は反転させる
            float yaw = (fenceAlongX ? 90f : 0f) + (side < 0 ? 0f : 180f);

            for (int i = 0; i < count; i++)
            {
                var go = Instantiate(sideFencePrefab, parent);
                go.name = "Fence_" + (side < 0 ? "L" : "R") + i;
                go.transform.localPosition = new Vector3(
                    side * fenceOffsetFromCenter,
                    fenceBottomLift,
                    startZ + step * (i + 0.5f));
                go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                go.transform.localScale = Vector3.one * fenceScale;

                if (fenceMaterial != null) ApplyMaterial(go, fenceMaterial);
            }
        }

        /// <summary>モデルに含まれる全スロットを同じマテリアルで塗り替える。</summary>
        private static void ApplyMaterial(GameObject go, Material material)
        {
            foreach (var renderer in go.GetComponentsInChildren<MeshRenderer>())
            {
                var materials = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
            }
        }

        /// <summary>
        /// 柵モデルの寸法を 1 度だけ測る。
        /// 長辺の向き・並べる間隔・足元の高さを、モデル任せで決められるようにするため
        /// （アセットごとに原点や向きが違うので、数値で決め打ちしない）。
        /// </summary>
        private void MeasureFence()
        {
            if (fenceMeasured) return;
            fenceMeasured = true;

            var filters = sideFencePrefab.GetComponentsInChildren<MeshFilter>();
            if (filters.Length == 0)
            {
                Debug.LogWarning("[StageBuilder] 柵モデルにメッシュがありません。", this);
                return;
            }

            Bounds bounds = default;
            bool first = true;
            Matrix4x4 rootInverse = sideFencePrefab.transform.worldToLocalMatrix;
            foreach (var filter in filters)
            {
                if (filter.sharedMesh == null) continue;

                Bounds local = filter.sharedMesh.bounds;
                Matrix4x4 toRoot = rootInverse * filter.transform.localToWorldMatrix;
                var transformed = new Bounds(toRoot.MultiplyPoint3x4(local.center), Vector3.zero);
                // 8 頂点を回してから包み直さないと、回転の入ったモデルで寸法がずれる
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = local.center + Vector3.Scale(local.extents, new Vector3(
                        (corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f,
                        (corner & 4) == 0 ? -1f : 1f));
                    transformed.Encapsulate(toRoot.MultiplyPoint3x4(point));
                }

                if (first) { bounds = transformed; first = false; }
                else bounds.Encapsulate(transformed);
            }

            Vector3 size = bounds.size * fenceScale;
            fenceAlongX = size.x >= size.z;
            fenceStep = Mathf.Max(0.1f, (fenceAlongX ? size.x : size.z) - fenceOverlap);
            // 原点が足元とは限らないので、下端が路面（y = 0）に来るよう持ち上げる
            fenceBottomLift = -bounds.min.y * fenceScale;
        }

        /// <summary>
        /// 道の両脇に芝生を敷く。奈落と同じくプレイヤーについて動く一枚板で、当たり判定は持たせない。
        /// 乗れてしまうと穴を全部迂回できてしまうため、脇の壁（当たり判定）はそのまま残す。
        /// </summary>
        private void CreateGrass()
        {
            float halfWidth = laneCount * cellSize * 0.5f;
            float inner = halfWidth + 0.5f; // 壁の外側から始める
            const float thickness = 1f;

            grassMat = CreateMaterial("Grass", grassColor);
            if (grassTexture != null)
            {
                grassMat.mainTexture = grassTexture;
                grassMat.mainTextureScale =
                    new Vector2(grassSideWidth / grassTextureLength, grassLength / grassTextureLength);
            }
            grassMat.SetFloat("_Smoothness", 0f); // 草がてかると一気に嘘っぽくなる

            grassSides = new Transform[2];
            int index = 0;
            for (int side = -1; side <= 1; side += 2)
            {
                var go = CreateBox(transform, side < 0 ? "Grass_Left" : "Grass_Right",
                    new Vector3(side * (inner + grassSideWidth * 0.5f), -grassDropBelowRoad - thickness * 0.5f, 0f),
                    new Vector3(grassSideWidth, thickness, grassLength),
                    grassMat);
                Destroy(go.GetComponent<Collider>());

                var renderer = go.GetComponent<MeshRenderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                grassSides[index++] = go.transform;
            }

            FollowGrass();
        }

        /// <summary>
        /// 生成済みの道が途切れる先に、穴のない道を描き足す。見た目だけで当たり判定はない。
        /// 前方 50m までしか実物を作らないので、これが無いと道が野原の真ん中で切れて見える。
        /// </summary>
        private void CreateFarRoad()
        {
            const float thickness = 1f;

            farRoadMat = CreateMaterial("FarRoad", Color.white);
            if (roadTexture != null)
            {
                farRoadMat.mainTexture = roadTexture;
                farRoadMat.mainTextureScale = new Vector2(1f, farRoadLength / roadTextureLength);
            }

            var go = CreateBox(transform, "FarRoad",
                new Vector3(0f, -farRoadSink - thickness * 0.5f, 0f),
                new Vector3(laneCount * cellSize, thickness, farRoadLength),
                farRoadMat);
            Destroy(go.GetComponent<Collider>());

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            farRoad = go.transform;
            FollowFarRoad();
        }

        /// <summary>
        /// 板はプレイヤーについて動くが、テクスチャは動かさない。
        /// そのまま動かすと模様が張り付いて、進んでいる感じが消える。
        /// </summary>
        private void FollowGrass()
        {
            if (grassSides == null || player == null) return;

            float z = player.position.z;
            foreach (var side in grassSides)
            {
                if (side == null) continue;
                Vector3 p = side.localPosition;
                p.z = z;
                side.localPosition = p;
            }

            if (grassMat != null && grassTexture != null)
            {
                grassMat.mainTextureOffset = new Vector2(0f, z / grassTextureLength);
            }
        }

        private void FollowFarRoad()
        {
            if (farRoad == null || player == null) return;

            // 実物の道が終わるあたりから先に描く。手前は本物が上に乗る（farRoadSink ぶん沈めてある）
            float start = player.position.z + generateAheadDistance;
            Vector3 p = farRoad.localPosition;
            p.z = start + farRoadLength * 0.5f;
            farRoad.localPosition = p;

            if (farRoadMat != null && roadTexture != null)
            {
                farRoadMat.mainTextureOffset = new Vector2(0f, start / roadTextureLength);
            }
        }

        private float ChunkLength => cellsPerChunk * cellSize;

        private void GenerateAhead()
        {
            float playerZ = player != null ? player.position.z : 0f;
            bool built = false;
            while (ChunkStartZ(nextChunkIndex) < playerZ + generateAheadDistance)
            {
                chunks.Enqueue(BuildChunk(nextChunkIndex));
                nextChunkIndex++;
                built = true;
            }

            // Auto Sync Transforms が無効なので、作った床の当たり判定は次の物理ステップまで原点に残る。
            // WebGL ビルドでは最初のフレームに物理ステップが走らないことがあり、
            // 正しい床が無いままプレイヤーの落下処理が進んで、開始直後にすり抜けていた
            if (built) Physics.SyncTransforms();
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
                        FloorMaterial(cell, lane));
                }
            }

            if (buildSideWalls)
            {
                float halfWidth = laneCount * cellSize * 0.5f;
                float centerZ = ChunkStartZ(chunkIndex) + ChunkLength * 0.5f;
                for (int side = -1; side <= 1; side += 2)
                {
                    var wall = CreateBox(
                        chunkRoot.transform,
                        side < 0 ? "Wall_Left" : "Wall_Right",
                        new Vector3(side * (halfWidth + 0.25f), wallHeight * 0.5f - cellSize * 0.5f, centerZ),
                        new Vector3(0.5f, wallHeight, ChunkLength),
                        wallMat);

                    // 柵を立てるなら、箱は当たり判定だけの見えない壁にする。
                    // 柵のモデルに当たり判定を持たせるより、一枚板の方が引っ掛からない
                    bool hideWall = !showSideWalls || sideFencePrefab != null;
                    if (hideWall) wall.GetComponent<MeshRenderer>().enabled = false;

                    if (sideFencePrefab != null) BuildFenceRow(chunkRoot.transform, side, chunkIndex);
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

        private static GameObject CreateBox(Transform parent, string name, Vector3 center, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>
        /// 道路テクスチャを、レーンと進行方向の位置で切り分けたマテリアルを用意する。
        ///
        /// 床はセルごとに独立したキューブで、UV は面ごとに 0〜1 しかない。
        /// そのまま貼るとレーンの数だけ道路が横に並んでしまうので、
        /// Tiling / Offset で「1 枚の道路のどの部分か」をセルごとに指定する。
        /// </summary>
        private void CreateRoadMaterials()
        {
            if (roadTexture == null) return;

            // 継ぎ目を出さないため、テクスチャ 1 枚をセルの整数個で割り切る
            roadTilesAlong = Mathf.Max(1, Mathf.RoundToInt(roadTextureLength / cellSize));

            var scale = new Vector2(1f / laneCount, 1f / roadTilesAlong);
            roadMats = new Material[laneCount, roadTilesAlong];

            for (int lane = 0; lane < laneCount; lane++)
            {
                for (int step = 0; step < roadTilesAlong; step++)
                {
                    // 色を乗せるとアスファルトが染まるので、道路のときだけ白にする
                    var mat = CreateMaterial("Road_" + lane + "_" + step, Color.white);
                    mat.mainTexture = roadTexture;
                    mat.mainTextureScale = scale;
                    mat.mainTextureOffset = new Vector2((float)lane / laneCount, (float)step / roadTilesAlong);

                    if (roadNormalMap != null)
                    {
                        mat.SetTexture("_BumpMap", roadNormalMap);
                        mat.SetTextureScale("_BumpMap", scale);
                        mat.SetTextureOffset("_BumpMap", mat.mainTextureOffset);
                        // URP/Lit は法線マップをキーワードで切り替える。立てないと貼っても効かない
                        mat.EnableKeyword("_NORMALMAP");
                    }

                    roadMats[lane, step] = mat;
                }
            }
        }

        /// <summary>そのセルが道路のどの部分かを返す。道路テクスチャが無ければ従来の白い床。</summary>
        private Material FloorMaterial(int cell, int lane)
        {
            if (roadMats == null) return floorMat;

            // スタート地点より後ろのセルは cell が負になるので、剰余を 0 以上に寄せる
            int step = ((cell % roadTilesAlong) + roadTilesAlong) % roadTilesAlong;
            return roadMats[lane, step];
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
