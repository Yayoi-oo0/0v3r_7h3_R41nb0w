using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OverTheRainbow.Core
{
    /// <summary>
    /// 状態遷移（Title / Countdown / Play / GameOver）を管理する。
    /// 制限時間もクリアもなし。落下・侵食 MAX・闇に追いつかれるまで続き、到達距離がスコアになる（企画書 4.4）。
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("ルール（企画書 7. パラメータ表）")]
        [Tooltip("開始カウントダウン（秒）")]
        [SerializeField] private float countdownSeconds = 3f;
        [Tooltip("シーン再生と同時にカウントダウンを始める（Phase 5 でタイトル画面に差し替え）")]
        [SerializeField] private bool startImmediately = true;

        [Header("デバッグ")]
        [Tooltip("R キーでシーンをリロードする")]
        [SerializeField] private bool allowQuickRestart = true;

        public GameState State { get; private set; } = GameState.Title;
        public GameOverReason LastGameOverReason { get; private set; } = GameOverReason.None;

        /// <summary>カウントダウンの残り秒。</summary>
        public float CountdownRemaining { get; private set; }

        /// <summary>状態が変わったときに発火。引数は新しい状態。</summary>
        public event Action<GameState> StateChanged;

        private bool isPaused;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            if (startImmediately) BeginCountdown();
        }

        private void Update()
        {
            HandleDebugKeys();

            switch (State)
            {
                case GameState.Countdown:
                    CountdownRemaining -= Time.deltaTime;
                    if (CountdownRemaining <= 0f) SetState(GameState.Playing);
                    break;
            }
        }

        private void HandleDebugKeys()
        {
            if (allowQuickRestart && Input.GetKeyDown(KeyCode.R)) Restart();

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (State == GameState.Playing || isPaused) TogglePause();
            }
        }

        public void BeginCountdown()
        {
            LastGameOverReason = GameOverReason.None;
            CountdownRemaining = countdownSeconds;
            SetState(countdownSeconds > 0f ? GameState.Countdown : GameState.Playing);
        }

        public void TriggerGameOver(GameOverReason reason)
        {
            if (State != GameState.Playing && State != GameState.Countdown) return;
            LastGameOverReason = reason;
            SetState(GameState.GameOver);
        }

        public void TogglePause()
        {
            isPaused = !isPaused;
            Time.timeScale = isPaused ? 0f : 1f;
        }

        public void Restart()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private void SetState(GameState next)
        {
            if (State == next) return;
            State = next;
            StateChanged?.Invoke(next);
        }
    }
}
