using System;
using ProjectCircle.Enemies;
using ProjectCircle.Systems;
using UnityEngine;

namespace ProjectCircle.Player
{
    /// <summary>
    /// The parry: the cape wraps tight around the circle for a split second. Projectiles (bolts, orbs) and
    /// laser beams that touch the circle during that window are deflected: projectiles fly back the way they
    /// came and hurt enemies instead; a laser reflects back along its path into the triangle that fired it.
    ///
    /// Only ranged attacks can be parried. Bodies, slams and shockwaves still hurt.
    ///
    /// Projectiles and lasers ask <see cref="TryDeflect"/> before hurting the circle.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInputReader), typeof(PlayerHealth))]
    public sealed class PlayerParry : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerCape cape;

        [Tooltip("Vertex-colour material for the parry flash (e.g. ShapeUnlit).")]
        [SerializeField] private Material flashMaterial;

        [Header("Timing")]
        [Tooltip("How long the parry catches things after the button is pressed.")]
        [SerializeField, Min(0.02f)] private float activeWindow = 0.2f;

        [Tooltip("Minimum time between parries (from press to press). Stops it being held up like a shield.")]
        [SerializeField, Min(0f)] private float cooldown = 0.5f;

        [Tooltip("Protection after a successful parry, so the rest of a beam or a second bolt can't hit in the same breath.")]
        [SerializeField, Min(0f)] private float successGrace = 0.3f;

        [Header("Feel")]
        [SerializeField, Min(0f)] private float hitStopTime = 0.07f;
        [SerializeField, Min(0f)] private float shakeStrength = 0.14f;
        [SerializeField, Min(0f)] private float shakeTime = 0.18f;
        [SerializeField] private Color flashColor = new Color(0.85f, 0.95f, 1f, 1f);

        /// <summary>Raised when a parry is started.</summary>
        public event Action ParryStarted;

        /// <summary>Raised when something is deflected. Argument: where.</summary>
        public event Action<Vector2> Deflected;

        /// <summary>True during the window in which ranged attacks are deflected.</summary>
        public bool IsParrying => Time.time < activeUntil;

        /// <summary>True while the cape is wrapped (parry window or unwrapping): it can't swing or form a platform.</summary>
        public bool IsBusy => IsParrying || (cape != null && cape.IsWrapping);

        private PlayerInputReader input;
        private PlayerHealth health;
        private PlayerAttack attack;
        private CameraFollow2D cameraFollow;
        private VertexGlow flash;
        private float activeUntil = float.NegativeInfinity;
        private float lastParryTime = float.NegativeInfinity;
        private float lastDeflectTime = float.NegativeInfinity;

        #region Unity lifecycle

        private void Awake()
        {
            input = GetComponent<PlayerInputReader>();
            health = GetComponent<PlayerHealth>();
            attack = GetComponent<PlayerAttack>();
            if (cape == null) cape = GetComponentInChildren<PlayerCape>();
            CreateFlash();
        }

        private void Start() => cameraFollow = FindAnyObjectByType<CameraFollow2D>();

        private void OnEnable() => input.ParryPressed += HandleParryPressed;
        private void OnDisable() => input.ParryPressed -= HandleParryPressed;

        private void LateUpdate()
        {
            if (flash == null) return;
            flash.transform.position = transform.position;
            float t = Mathf.Clamp01((Time.unscaledTime - lastDeflectTime) / 0.25f);
            // A faint shimmer while the cape is wrapped; a bright flare on a successful deflect.
            flash.Intensity = Mathf.Max(IsParrying ? 0.35f : 0f, 1.6f * (1f - t) * (1f - t));
        }

        #endregion

        #region Parrying

        private void HandleParryPressed()
        {
            if (Time.time < lastParryTime + cooldown || health.IsDead) return;
            if (cape != null && cape.IsFormingPlatform) return; // the cape is busy being a platform

            // The cape can't be a blade and a wrap at once: the parry wins.
            if (attack != null) attack.InterruptForParry();

            lastParryTime = Time.time;
            activeUntil = Time.time + activeWindow;
            if (cape != null) cape.PlayWrap(activeWindow);
            ParryStarted?.Invoke();
        }

        /// <summary>
        /// Asked by a projectile or laser about to hurt the circle. Returns true if it is deflected (the caller
        /// then reverses it); false if the circle isn't parrying and should take the hit.
        /// </summary>
        public bool TryDeflect(Vector2 point)
        {
            if (!IsParrying) return false;

            // Feedback once per moment, even if several things are deflected together.
            if (Time.unscaledTime > lastDeflectTime + 0.05f)
            {
                lastDeflectTime = Time.unscaledTime;
                if (flash != null) flash.Burst();
                HitStop.Freeze(hitStopTime);
                if (cameraFollow != null) cameraFollow.Shake(shakeStrength, shakeTime);
            }

            health.GrantGrace(successGrace);
            Deflected?.Invoke(point);
            return true;
        }

        #endregion

        private void CreateFlash()
        {
            if (flashMaterial == null) return;
            var go = new GameObject("Parry Flash");
            go.transform.SetParent(transform, false);
            flash = go.AddComponent<VertexGlow>();
            var meshRenderer = go.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = flashMaterial;
            meshRenderer.sortingOrder = 12;
            flash.Color = flashColor;
            flash.Radius = 0.95f;
            flash.BurstRadius = 2.2f;
        }
    }
}
