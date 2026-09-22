using UnityEngine;
using unityroom.Api;
using OverTheRainbow.Player;

namespace OverTheRainbow.Core
{
    /// <summary>
    /// 到達距離を unityroom のスコアボードへ送る（企画書 2.5 競争性 / 4.4 スコア）。
    ///
    /// 送信はゲームオーバーの瞬間に 1 回だけ。リトライはシーンの再読み込みなので、
    /// 次のプレイでは新しいインスタンスが改めて送る。
    /// 送信が効くのは unityroom 上の WebGL ビルドのみで、エディタでは何も起きない。
    /// </summary>
    public class ScoreSubmitter : MonoBehaviour
    {
        [Tooltip("未設定ならシーンから探す")]
        [SerializeField] private DistanceTracker distanceTracker;
        [Tooltip("unityroom のスコアボード No")]
        [SerializeField, Min(1)] private int boardNo = 1;
        [Tooltip("unityroom 側のスコアボードと同じ設定にする。距離は長いほど良いので降順")]
        [SerializeField] private ScoreboardWriteMode writeMode = ScoreboardWriteMode.HighScoreDesc;
        [Tooltip("小数を切り捨てて送る。画面表示（245 m）とランキングの数字を揃えるため")]
        [SerializeField] private bool sendWholeMeters = true;

        private GameState lastState = GameState.Title;
        private bool sent;

        private void Start()
        {
            if (distanceTracker == null) distanceTracker = FindObjectOfType<DistanceTracker>();
        }

        private void Update()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || gm.State == lastState) return;

            lastState = gm.State;
            if (lastState == GameState.GameOver) Send();
        }

        private void Send()
        {
            if (sent) return;
            sent = true;

            float distance = distanceTracker != null ? distanceTracker.Distance : 0f;
            if (sendWholeMeters) distance = Mathf.Floor(distance);

            UnityroomApiClient.Instance.SendScore(boardNo, distance, writeMode);
            Debug.Log("[ScoreSubmitter] スコアを送信しました: " + distance + " m（board " + boardNo + "）");
        }
    }
}
