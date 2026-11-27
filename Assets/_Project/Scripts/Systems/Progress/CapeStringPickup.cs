using ProjectCircle.Player;
using ProjectCircle.Systems.Story;
using ProjectCircle.Systems.Audio;
using UnityEngine;

namespace ProjectCircle.Systems
{
    /// <summary>
    /// A cape string waiting in an alcove: the resistance of another cast-out shape. When the circle touches it,
    /// it joins the cape and unlocks <see cref="ability"/> (dash, parry, double jump...). Once collected it stays
    /// collected (the ability is in <see cref="GameProgress"/>), so it is gone on every later visit.
    ///
    /// Art: everything under <see cref="visual"/> is just looks (swap it freely). The pickup works by distance
    /// from this object's position (<see cref="pickupRadius"/>), so no collider is needed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CapeStringPickup : MonoBehaviour
    {
        [SerializeField] private PlayerAbility ability = PlayerAbility.Dash;

        [Tooltip("Shown when collected, e.g. \"The Runner's String\".")]
        [SerializeField] private string stringName = "A String";

        [Tooltip("Control hint shown after collecting, e.g. \"Shift / RB to DASH\".")]
        [SerializeField] private string hint = "";

        [SerializeField, Min(0.2f)] private float pickupRadius = 1.2f;
        [SerializeField] private Color glowColor = new Color(0.86f, 0.25f, 0.22f, 1f);

        [Tooltip("The looks (string, glow). Hidden once collected.")]
        [SerializeField] private GameObject visual;

        private Transform player;
        private PlayerAbilities abilities;
        private bool collected;

        /// <summary>Raised once when the circle collects this string.</summary>
        public event System.Action Collected;

        /// <summary>Sets a string up from code (e.g. one a boss drops). Call right after Instantiate.</summary>
        public void Configure(PlayerAbility newAbility, string newName, string newHint)
        {
            ability = newAbility;
            stringName = newName;
            hint = newHint;
        }

        private void Start()
        {
            var movement = FindAnyObjectByType<PlayerMovement>();
            if (movement != null)
            {
                player = movement.transform;
                abilities = movement.GetComponent<PlayerAbilities>();
            }
            if (abilities != null && abilities.Has(ability)) Hide();
        }

        private void Update()
        {
            if (collected || player == null || abilities == null) return;
            if (((Vector2)player.position - (Vector2)transform.position).sqrMagnitude > pickupRadius * pickupRadius) return;
            Collect();
        }

        private void Collect()
        {
            collected = true;
            abilities.Grant(ability);
            Vector2 at = transform.position;
            Fx.LightColumn(at + Vector2.down * 1.2f, 7f, glowColor, 1.1f, 1.6f);
            Sfx.Play(SoundId.StringPickup);
            Fx.Ring(at, glowColor, 2.4f, 0.45f, 0.12f);
            Fx.Shards(at, glowColor, 16, 5f, 0.14f);
            var cam = FindAnyObjectByType<CameraFollow2D>();
            if (cam != null) cam.Shake(0.12f, 0.25f);
            CinematicOverlay.Instance.ShowAreaName(stringName.ToUpperInvariant(), 2.5f);
            if (!string.IsNullOrEmpty(hint)) CinematicOverlay.Instance.ShowHint(hint, 4f);
            Hide();
            Collected?.Invoke();
        }

        private void Hide()
        {
            collected = true;
            if (visual != null) visual.SetActive(false);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = glowColor;
            Gizmos.DrawWireSphere(transform.position, pickupRadius);
        }
    }
}
