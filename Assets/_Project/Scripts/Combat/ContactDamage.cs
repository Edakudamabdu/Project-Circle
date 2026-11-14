using System.Collections.Generic;
using ProjectCircle.Player;
using UnityEngine;

namespace ProjectCircle.Combat
{
    /// <summary>
    /// Hurts the player when they touch this object (e.g. bumping into an enemy). The player is knocked
    /// back away from it. Put it on any enemy or hazard with a Collider2D.
    ///
    /// Works with an overlap check rather than physics contacts, because the Player and Enemy layers are set
    /// not to collide (so they never shove each other around). The player's normal invulnerability rules
    /// apply: no damage while dashing or just after being hit.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ContactDamage : MonoBehaviour
    {
        [Tooltip("The area that hurts. Uses this object's own collider if left empty.")]
        [SerializeField] private Collider2D hitbox;

        [SerializeField, Min(1)] private int damage = 1;

        [Tooltip("Layers that take contact damage.")]
        [SerializeField] private LayerMask targetLayers = 1 << 8;

        private ContactFilter2D filter;
        private readonly List<Collider2D> overlaps = new List<Collider2D>(4);

        private void Awake()
        {
            if (hitbox == null) hitbox = GetComponent<Collider2D>();
            filter = new ContactFilter2D { useLayerMask = true, layerMask = targetLayers, useTriggers = true };
        }

        private void FixedUpdate()
        {
            if (hitbox == null || !hitbox.enabled) return;

            int count = hitbox.Overlap(filter, overlaps);
            for (int i = 0; i < count; i++)
            {
                var health = overlaps[i].GetComponentInParent<PlayerHealth>();
                if (health != null) health.TakeDamage(damage, hitbox.bounds.center);
            }
        }
    }
}
