namespace OverTheRainbow.Core
{
    /// <summary>ゲーム全体の状態。企画書 5. ゲームフローに対応。</summary>
    public enum GameState
    {
        Title,
        Countdown,
        Playing,
        GameOver, // クリアはない。プレイの終わりは常にゲームオーバー（企画書 4.4）
    }

    /// <summary>ゲームオーバーの理由。企画書 4.4 終了条件。</summary>
    public enum GameOverReason
    {
        None,
        Fell,        // 穴に落下
        Corruption,  // 視界の侵食が最大
        Darkness,    // 後ろから迫る闇に追いつかれた
    }
}
