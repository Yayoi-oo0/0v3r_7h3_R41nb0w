using UnityEngine;
using OverTheRainbow.Core;
using OverTheRainbow.Vision;

namespace OverTheRainbow.Player
{
    /// <summary>
    /// WASD 移動（A/D は横移動）、マウス視点、遮眼子使用中の移動ロック、穴への落下判定。
    /// 遮眼子中はキーボードから手が離れるので、マウスでの見回しと両立できる。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("移動（企画書 7. パラメータ表）")]
        [Tooltip("移動速度 m/s")]
        [SerializeField] private float moveSpeed = 3.0f;
        [SerializeField] private float gravity = -20f;

        [Header("マウス視点")]
        [Tooltip("未設定なら子オブジェクトの Camera を使う。上下の首振りはこのカメラだけを回す")]
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private float mouseSensitivity = 2.0f;
        [Tooltip("上下の首振りの限界角（度）")]
        [SerializeField, Range(0f, 89f)] private float pitchLimit = 80f;
        [SerializeField] private bool invertY = false;

        [Header("遮眼子")]
        [SerializeField] private EyeCoverInput eyeCover;
        [Tooltip("遮眼子中もマウスで見回せる。「立ち止まって観察する」ための余地")]
        [SerializeField] private bool allowLookWhileCovered = true;

        [Header("落下判定")]
        [Tooltip("この Y 座標を下回ったら穴に落ちたと見なす")]
        [SerializeField] private float fallThresholdY = -4f;

        private CharacterController controller;
        private float verticalVelocity;
        private float pitch;

        /// <summary>遮眼子以外の理由で移動を止めたいとき（演出など）に外から立てる。</summary>
        public bool ExternalMovementLock { get; set; }

        /// <summary>今フレーム、移動がロックされているか。UI 表示にも使う。</summary>
        public bool IsMovementLocked { get; private set; }

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (eyeCover == null) eyeCover = FindObjectOfType<EyeCoverInput>();

            // 古いシーンで付け忘れていても距離を数えられるようにする
            if (GetComponent<DistanceTracker>() == null) gameObject.AddComponent<DistanceTracker>();
            if (cameraPivot == null)
            {
                var cam = GetComponentInChildren<Camera>();
                if (cam != null) cameraPivot = cam.transform;
            }
        }

        private void OnDisable()
        {
            SetCursorLocked(false);
        }

        private void Update()
        {
            GameManager gm = GameManager.Instance;
            bool paused = Time.timeScale == 0f;
            bool acceptsInput = (gm == null || gm.State == GameState.Playing) && !paused;
            bool covered = eyeCover != null && eyeCover.IsEyeCovered;

            IsMovementLocked = ExternalMovementLock || covered;

            SetCursorLocked(acceptsInput);

            bool canLook = acceptsInput && !ExternalMovementLock && (!covered || allowLookWhileCovered);
            if (canLook) UpdateLook();

            Vector3 horizontalVelocity = Vector3.zero;
            if (acceptsInput && !IsMovementLocked)
            {
                float h = Input.GetAxisRaw("Horizontal");
                float v = Input.GetAxisRaw("Vertical");
                Vector3 dir = transform.forward * v + transform.right * h;
                if (dir.sqrMagnitude > 1f) dir.Normalize(); // 斜め移動が速くならないように
                horizontalVelocity = dir * moveSpeed;
            }

            // 接地中はわずかに押し付けて、段差で浮かないようにする
            if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            verticalVelocity += gravity * Time.deltaTime;

            Vector3 motion = horizontalVelocity + Vector3.up * verticalVelocity;
            controller.Move(motion * Time.deltaTime);

            if (gm != null && gm.State == GameState.Playing && transform.position.y < fallThresholdY)
            {
                gm.TriggerGameOver(GameOverReason.Fell);
            }
        }

        /// <summary>左右はプレイヤー本体、上下はカメラだけを回す（移動方向が上下の向きに引きずられないように）。</summary>
        private void UpdateLook()
        {
            // マウスの Axis はフレーム間の移動量なので deltaTime は掛けない
            float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * (invertY ? 1f : -1f);

            transform.Rotate(0f, mouseX, 0f);

            if (cameraPivot != null)
            {
                pitch = Mathf.Clamp(pitch + mouseY, -pitchLimit, pitchLimit);
                cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }
        }

        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
