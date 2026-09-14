namespace OverTheRainbow.Core
{
    /// <summary>ゲーム全体の状態。企画書 5. ゲームフローに対応。</summary>
    public enum GameState
    {
        Title,
        Countdown,
        Playing,
        Clear,
        GameOver,
    }

    /// <summary>ゲームオーバーの理由。企画書 4.4 勝敗条件。</summary>
    public enum GameOverReason
    {
        None,
        TimeUp,      // 制限時間切れ
        Fell,        // 穴に落下
        Corruption,  // 視界の侵食が最大（Phase 2 で使用）
    }
}
