using UnityEngine;

namespace OverTheRainbow.Vision
{
    /// <summary>
    /// Phase 1 用の仮実装。スペースキーを遮眼子の代わりに使う。
    /// Phase 3 以降もデバッグ用として残す（企画書 Phase 3 / 9. リスク対策）。
    /// </summary>
    public class KeyboardEyeCoverSource : MonoBehaviour, IEyeCoverSource
    {
        public enum InputStyle
        {
            Hold,   // 押している間だけ遮眼子を当てている（本番の体験に近い）
            Toggle, // 押すたびに切り替え
        }

        [SerializeField] private KeyCode key = KeyCode.Space;
        [SerializeField] private InputStyle style = InputStyle.Hold;

        private bool toggledOn;

        public bool IsAvailable => true;
        public string SourceName => "Keyboard(" + key + ")";

        private void Update()
        {
            if (style == InputStyle.Toggle && Input.GetKeyDown(key)) toggledOn = !toggledOn;
        }

        public EyeState ReadRaw()
        {
            bool covered = style == InputStyle.Hold ? Input.GetKey(key) : toggledOn;
            return covered ? EyeState.Covered : EyeState.Open;
        }
    }
}
