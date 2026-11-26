using ProjectCircle.Player;
using ProjectCircle.Systems.Story;
using UnityEngine;

namespace ProjectCircle.Systems
{
    /// <summary>
    /// Something the circle picks up that sets a story flag in <see cref="GameProgress"/>: a Vertex Seal
    /// ("Seal.Twins"), the Triangle Mould ("Mould.Triangle")... Touch it to take it. Gone on later visits.
    ///
    /// Also used as a stand-in in boss rooms whose boss isn't built yet: the seal sits in the empty room so the
    /// route can still be walked.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FlagPickup : MonoBehaviour
    {
        [SerializeField] private string flag = "Seal.Twins";

        [Tooltip("Shown when taken, e.g. \"Vertex Seal II\".")]
        [SerializeField] private string displayName = "Vertex Seal";

        [SerializeField, Min(0.2f)] private float pickupRadius = 1.2f;
        [SerializeField] private Color glowColor = new Color(1f, 0.82f, 0.4f, 1f);

        [Tooltip("The looks. Hidden once taken.")]
        [SerializeField] private GameObject visual;

        private Transform player;
        private bool taken;

        private void Start()
        {
            var movement = FindAnyObjectByType<PlayerMovement>();
            if (movement != null) player = movement.transform;
            if (GameProgress.Has(flag)) Hide();
        }

        private void Update()
        {
            if (taken || player == null) return;
            if (((Vector2)player.position - (Vector2)transform.position).sqrMagnitude > pickupRadius * pickupRadius) return;
            taken = true;
            GameProgress.Set(flag);
            Vector2 at = transform.position;
            Fx.Ring(at, glowColor, 2.6f, 0.5f, 0.14f);
            Fx.LightColumn(at + Vector2.down * 1.2f, 6f, glowColor, 1f, 1.4f);
            CinematicOverlay.Instance.ShowAreaName(displayName.ToUpperInvariant(), 2.5f);
            Hide();
        }

        private void Hide()
        {
            taken = true;
            if (visual != null) visual.SetActive(false);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = glowColor;
            Gizmos.DrawWireSphere(transform.position, pickupRadius);
        }
    }
}
