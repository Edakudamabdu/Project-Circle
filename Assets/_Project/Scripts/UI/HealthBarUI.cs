using System.Collections.Generic;
using ProjectCircle.Player;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectCircle.UI
{
    /// <summary>
    /// Simple health bar for the circle, built from plain uGUI Images (no sprites needed).
    ///
    /// - The fill shrinks instantly on damage; a red "trail" lingers behind it for a moment and then drains,
    ///   so the player can see how much was lost.
    /// - Thin dividers split the bar into one segment per point of health.
    /// - The bar shakes and the fill flashes white on a hit; on the last point of health the fill pulses.
    ///
    /// Bars are sized by their RectTransform anchors, so Fill and Trail should be stretched children of
    /// the bar area with no sprite.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HealthBarUI : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Whose health to show. Found automatically if left empty.")]
        [SerializeField] private PlayerHealth health;

        [Tooltip("The current-health bar.")]
        [SerializeField] private Image fill;

        [Tooltip("The bar that lingers behind the fill after damage.")]
        [SerializeField] private Image trail;

        [Tooltip("Dividers between health segments are created inside this.")]
        [SerializeField] private RectTransform segmentContainer;

        [Tooltip("This is shaken when the circle is hit. Usually the whole bar.")]
        [SerializeField] private RectTransform shakeTarget;

        [Header("Colours")]
        [SerializeField] private Color fillColor = new Color(0.95f, 0.93f, 0.85f, 1f);
        [SerializeField] private Color lowHealthColor = new Color(1f, 0.55f, 0.45f, 1f);
        [SerializeField] private Color trailColor = new Color(0.86f, 0.3f, 0.28f, 1f);
        [SerializeField] private Color dividerColor = new Color(0.05f, 0.06f, 0.08f, 1f);

        [Header("Animation")]
        [Tooltip("How long the trail waits before draining.")]
        [SerializeField, Min(0f)] private float trailDelay = 0.45f;

        [Tooltip("How fast the trail drains (fraction of the bar per second).")]
        [SerializeField, Min(0.01f)] private float trailDrainSpeed = 1.2f;

        [Tooltip("How fast the fill grows when healing (fraction of the bar per second).")]
        [SerializeField, Min(0.01f)] private float healSpeed = 2f;

        [SerializeField, Min(0f)] private float shakeStrength = 8f;
        [SerializeField, Min(0f)] private float shakeTime = 0.25f;
        [SerializeField, Min(0f)] private float flashTime = 0.12f;

        [Tooltip("Pulses per second on the last point of health.")]
        [SerializeField, Min(0f)] private float lowHealthPulseRate = 2.2f;

        [Tooltip("Width of the dividers between segments, in pixels.")]
        [SerializeField, Min(0f)] private float dividerWidth = 3f;

        private float shownFill = 1f;     // fill fraction currently drawn
        private float targetFill = 1f;    // fill fraction it should reach
        private float shownTrail = 1f;
        private float trailHoldUntil;
        private float hitTime = float.NegativeInfinity;
        private Vector2 shakeBasePosition;
        private int builtSegments = -1;
        private bool lowHealth;
        private readonly List<GameObject> dividers = new List<GameObject>();

        private void Awake()
        {
            if (health == null) health = FindAnyObjectByType<PlayerHealth>();
            if (shakeTarget == null) shakeTarget = (RectTransform)transform;
            shakeBasePosition = shakeTarget.anchoredPosition;

            if (trail != null) trail.color = trailColor;
        }

        private void OnEnable()
        {
            if (health == null) return;
            health.HealthChanged += HandleHealthChanged;
            health.Damaged += HandleDamaged;
            HandleHealthChanged(health.CurrentHealth, health.MaxHealth);
            SnapToTarget();
        }

        private void OnDisable()
        {
            if (health == null) return;
            health.HealthChanged -= HandleHealthChanged;
            health.Damaged -= HandleDamaged;
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            // Fill: drops instantly (handled in HandleHealthChanged), grows smoothly when healing.
            shownFill = shownFill < targetFill ? Mathf.MoveTowards(shownFill, targetFill, healSpeed * dt) : targetFill;

            // Trail: waits, then drains down to the fill. It never sits below the fill.
            if (Time.time >= trailHoldUntil)
            {
                shownTrail = Mathf.MoveTowards(shownTrail, shownFill, trailDrainSpeed * dt);
            }
            shownTrail = Mathf.Max(shownTrail, shownFill);

            SetWidth(fill, shownFill);
            SetWidth(trail, shownTrail);

            UpdateFillColour();
            UpdateShake();
        }

        private void HandleHealthChanged(int current, int max)
        {
            if (max != builtSegments) BuildDividers(max);

            targetFill = max > 0 ? (float)current / max : 0f;
            lowHealth = current == 1;

            // A full refill (respawn) snaps the bar instead of animating.
            if (current == max && shownFill <= 0f) SnapToTarget();
        }

        private void HandleDamaged(int amount)
        {
            hitTime = Time.time;
            trailHoldUntil = Time.time + trailDelay;
        }

        private void SnapToTarget()
        {
            shownFill = targetFill;
            shownTrail = targetFill;
        }

        private void UpdateFillColour()
        {
            if (fill == null) return;

            Color colour = fillColor;
            if (lowHealth && lowHealthPulseRate > 0f)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * lowHealthPulseRate * Mathf.PI * 2f);
                colour = Color.Lerp(fillColor, lowHealthColor, pulse);
            }

            // Flash white for a moment when hit.
            float sinceHit = Time.time - hitTime;
            if (sinceHit < flashTime) colour = Color.Lerp(Color.white, colour, sinceHit / flashTime);

            fill.color = colour;
        }

        private void UpdateShake()
        {
            float sinceHit = Time.time - hitTime;
            if (sinceHit < shakeTime)
            {
                float strength = shakeStrength * (1f - sinceHit / shakeTime);
                shakeTarget.anchoredPosition = shakeBasePosition + Random.insideUnitCircle * strength;
            }
            else
            {
                shakeTarget.anchoredPosition = shakeBasePosition;
            }
        }

        /// <summary>Sizes a stretched bar by moving its right anchor (0 = empty, 1 = full).</summary>
        private static void SetWidth(Image bar, float fraction)
        {
            if (bar == null) return;
            RectTransform rect = bar.rectTransform;
            rect.anchorMin = new Vector2(0f, rect.anchorMin.y);
            rect.anchorMax = new Vector2(Mathf.Clamp01(fraction), rect.anchorMax.y);
            rect.offsetMin = new Vector2(0f, rect.offsetMin.y);
            rect.offsetMax = new Vector2(0f, rect.offsetMax.y);
        }

        /// <summary>Creates one thin divider between each pair of health segments.</summary>
        private void BuildDividers(int segments)
        {
            builtSegments = segments;
            foreach (GameObject divider in dividers) Destroy(divider);
            dividers.Clear();
            if (segmentContainer == null) return;

            for (int i = 1; i < segments; i++)
            {
                var divider = new GameObject("Divider " + i, typeof(RectTransform), typeof(Image));
                var rect = (RectTransform)divider.transform;
                rect.SetParent(segmentContainer, false);

                float x = (float)i / segments;
                rect.anchorMin = new Vector2(x, 0f);
                rect.anchorMax = new Vector2(x, 1f);
                rect.sizeDelta = new Vector2(dividerWidth, 0f);
                rect.anchoredPosition = Vector2.zero;

                var image = divider.GetComponent<Image>();
                image.color = dividerColor;
                image.raycastTarget = false;
                dividers.Add(divider);
            }
        }
    }
}
