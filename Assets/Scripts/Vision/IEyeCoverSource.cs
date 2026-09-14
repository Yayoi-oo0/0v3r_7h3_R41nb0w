namespace OverTheRainbow.Vision
{
    /// <summary>ランドマークを取りこぼした等で、入力元が状態を判断できないことを表す。</summary>
    public enum EyeState
    {
        Open,     // 遮眼子なし
        Covered,  // 片目が覆われている
        Lost,     // 検出不能（安全側＝Open として扱う）
    }

    /// <summary>
    /// 「片目が覆われているか」の供給元。
    /// Phase 1 はキーボード、Phase 3 で MediaPipe 受信に差し替える。
    /// 実装側は平滑化をせず、生の判定だけを返すこと（平滑化は EyeCoverInput が行う）。
    /// </summary>
    public interface IEyeCoverSource
    {
        /// <summary>この入力元が今フレーム使用可能か（カメラ未接続・未受信なら false）。</summary>
        bool IsAvailable { get; }

        /// <summary>今フレームの生の判定。</summary>
        EyeState ReadRaw();

        /// <summary>デバッグ表示用の短い名前。</summary>
        string SourceName { get; }
    }
}
