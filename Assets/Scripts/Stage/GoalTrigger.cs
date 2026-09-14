using UnityEngine;
using OverTheRainbow.Core;

namespace OverTheRainbow.Stage
{
    /// <summary>赤い気球（ゴール）に触れたらクリア。</summary>
    [RequireComponent(typeof(Collider))]
    public class GoalTrigger : MonoBehaviour
    {
        [SerializeField] private string playerTag = "Player";

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag(playerTag)) return;
            if (GameManager.Instance != null) GameManager.Instance.TriggerClear();
        }
    }
}
