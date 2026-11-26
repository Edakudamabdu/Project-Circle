using UnityEngine;

namespace ProjectCircle.Systems
{
    /// <summary>
    /// Shows or hides this object depending on a story flag. For example, the Slums' corrupted residents are a
    /// group with this set to hide once "Freed.Slums" is set, and the freed villagers a group set to show.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ActiveWhenFlag : MonoBehaviour
    {
        [SerializeField] private string flag = "Freed.Slums";

        [Tooltip("On: visible only once the flag is set. Off: visible only until the flag is set.")]
        [SerializeField] private bool showWhenSet = true;

        private void Awake() => gameObject.SetActive(GameProgress.Has(flag) == showWhenSet);
    }
}
