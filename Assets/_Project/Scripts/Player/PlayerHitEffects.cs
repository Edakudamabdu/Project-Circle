using System.Collections;
using ProjectCircle.Enemies;
using ProjectCircle.Systems;
using UnityEngine;

namespace ProjectCircle.Player
{
    /// <summary>
    /// Makes getting hit feel like a hit:
    /// - Hit-stop: time almost freezes for a split second, so the impact registers.
    /// - Camera shake.
    /// - The circle flashes red.
    /// - A burst of light with an expanding ring at the circle.
    /// A death gets a stronger version of the same.
    ///
    /// Purely presentational: listens to <see cref="PlayerHealth"/> events and changes nothing about gameplay
    /// (knockback is handled by <see cref="PlayerMovement"/>).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerHealth))]
    public sealed class PlayerHitEffects : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The circle's sprite, which flashes when hit.")]
        [SerializeField] private SpriteRenderer bodyRenderer;

        [Tooltip("Glow and ring burst shown on the circle when hit. Child of the Player.")]
        [SerializeField] private VertexGlow hitGlow;

        [Tooltip("Camera to shake. Found automatically if left empty.")]
        [SerializeField] private CameraFollow2D cameraFollow;

        [Header("Hit-Stop")]
        [Tooltip("Real-time seconds the game nearly freezes on a hit. 0 = off.")]
        [SerializeField, Min(0f)] private float hitStopTime = 0.08f;

        [Tooltip("Game speed during the freeze (0 = full stop).")]
        [SerializeField, Range(0f, 1f)] private float hitStopTimeScale = 0.02f;

        [Header("Camera Shake")]
        [SerializeField, Min(0f)] private float shakeStrength = 0.3f;
        [SerializeField, Min(0f)] private float shakeTime = 0.3f;

        [Tooltip("Shake and hit-stop multiplier when the hit is fatal.")]
        [SerializeField, Min(1f)] private float deathMultiplier = 1.8f;

        [Header("Flash & Glow")]
        [SerializeField] private Color flashColor = new Color(1f, 0.32f, 0.28f, 1f);
        [SerializeField, Min(0.01f)] private float flashTime = 0.2f;
        [SerializeField] private Color glowColor = new Color(1f, 0.45f, 0.38f, 1f);
        [SerializeField, Min(0.01f)] private float glowTime = 0.35f;

        private PlayerHealth health;
        private Color bodyColor = Color.white;
        private float hitTime = float.NegativeInfinity; // real time of the last hit
        private Coroutine hitStopRoutine;
        private float timeScaleBeforeStop = 1f;

        private void Awake()
        {
            health = GetComponent<PlayerHealth>();
            if (cameraFollow == null) cameraFollow = FindAnyObjectByType<CameraFollow2D>();
            if (bodyRenderer != null) bodyColor = bodyRenderer.color;
        }

        private void OnEnable() => health.Hit += HandleHit;

        private void OnDisable()
        {
            health.Hit -= HandleHit;

            // Never leave the game frozen.
            if (hitStopRoutine != null)
            {
                StopCoroutine(hitStopRoutine);
                Time.timeScale = timeScaleBeforeStop;
                hitStopRoutine = null;
            }
        }

        private void LateUpdate()
        {
            // Effects run on real time so they keep animating through the hit-stop.
            float since = Time.unscaledTime - hitTime;

            if (bodyRenderer != null)
            {
                float t = Mathf.Clamp01(since / flashTime);
                bodyRenderer.color = Color.Lerp(flashColor, bodyColor, t * t);
            }

            if (hitGlow != null)
            {
                float t = Mathf.Clamp01(since / glowTime);
                hitGlow.Intensity = 1.3f * (1f - t) * (1f - t);
            }
        }

        private void HandleHit(int amount, Vector2 source)
        {
            bool fatal = health.CurrentHealth <= 0;
            float scale = fatal ? deathMultiplier : 1f;

            hitTime = Time.unscaledTime;

            if (hitGlow != null)
            {
                hitGlow.Color = glowColor;
                hitGlow.Burst();
            }

            if (cameraFollow != null) cameraFollow.Shake(shakeStrength * scale, shakeTime * scale);

            if (hitStopTime > 0f)
            {
                if (hitStopRoutine != null) StopCoroutine(hitStopRoutine);
                else timeScaleBeforeStop = Time.timeScale;
                hitStopRoutine = StartCoroutine(HitStop(hitStopTime * scale));
            }
        }

        private IEnumerator HitStop(float duration)
        {
            Time.timeScale = hitStopTimeScale;
            yield return new WaitForSecondsRealtime(duration);
            Time.timeScale = timeScaleBeforeStop;
            hitStopRoutine = null;
        }
    }
}
