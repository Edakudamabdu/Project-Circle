using System;
using UnityEngine;

namespace ProjectCircle.Systems.Story
{
    /// <summary>
    /// An invisible box that tells a cutscene script when the circle walks into it.
    /// Uses a trigger BoxCollider2D (set up automatically). Fires once unless <see cref="fireOnce"/> is off.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class StoryTrigger : MonoBehaviour
    {
        [SerializeField] private LayerMask playerLayers = 1 << 8;
        [SerializeField] private bool fireOnce = true;

        /// <summary>Raised when the circle enters the box.</summary>
        public event Action Entered;

        private bool fired;

        private void Awake() => GetComponent<BoxCollider2D>().isTrigger = true;

        private void OnTriggerEnter2D(Collider2D other)
        {
            if ((playerLayers.value & (1 << other.gameObject.layer)) == 0) return;
            if (fireOnce && fired) return;
            fired = true;
            Entered?.Invoke();
        }

        /// <summary>Stops it firing (e.g. the scene it starts has already been played).</summary>
        public void Disarm() => fired = true;

        /// <summary>Lets it fire again (e.g. a boss fight that restarts after the player died).</summary>
        public void Rearm() => fired = false;

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider2D>();
            if (box == null) return;
            Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.35f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(box.offset, box.size);
        }
#endif
    }
}
