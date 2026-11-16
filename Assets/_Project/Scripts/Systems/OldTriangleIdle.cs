using ProjectCircle.Enemies;
using UnityEngine;

namespace ProjectCircle.Systems
{
    /// <summary>
    /// Idle life for the Old Triangle: slow, tired breathing, a slight lean, and every so often the
    /// Shapeshifter's influence flickers at its broken vertex (a violet glow and a shiver).
    ///
    /// Purely visual. Put it on the NPC root; it animates the <see cref="visual"/> child.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OldTriangleIdle : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The triangle's visual (the RoundedTriangleMesh object).")]
        [SerializeField] private Transform visual;

        [Tooltip("Glow placed on the broken vertex.")]
        [SerializeField] private VertexGlow corruptionGlow;

        [Header("Breathing")]
        [SerializeField, Min(0f)] private float breathsPerSecond = 0.25f;
        [SerializeField, Range(0f, 0.1f)] private float breathSquash = 0.025f;

        [Tooltip("Resting lean, in degrees (it can't quite stand straight any more).")]
        [SerializeField, Range(-20f, 20f)] private float lean = -7f;

        [Header("Corruption Flicker")]
        [SerializeField] private Color corruptionColor = new Color(0.72f, 0.38f, 1f, 1f);
        [SerializeField, Min(0.1f)] private float minFlickerInterval = 2.5f;
        [SerializeField, Min(0.1f)] private float maxFlickerInterval = 6f;
        [SerializeField, Min(0.05f)] private float flickerTime = 0.45f;

        [Tooltip("How far it shivers during a flicker.")]
        [SerializeField, Min(0f)] private float shiver = 0.03f;

        private Vector3 visualRestPosition;
        private Vector3 visualRestScale;
        private float nextFlickerTime;
        private float flickerStartTime = float.NegativeInfinity;

        private void Awake()
        {
            if (visual == null) return;
            visualRestPosition = visual.localPosition;
            visualRestScale = visual.localScale;
            nextFlickerTime = Time.time + Random.Range(minFlickerInterval, maxFlickerInterval);
            if (corruptionGlow != null) corruptionGlow.Color = corruptionColor;
        }

        /// <summary>
        /// While true, the Shapeshifter is pulling at him: the violet glow and the shiver never stop
        /// (the world-shake in the opening). Set false to calm back down.
        /// </summary>
        public bool Agitated { get; set; }

        /// <summary>True once he has collapsed (unconscious). No breathing, no glow.</summary>
        public bool Collapsed { get; private set; }

        private float collapseStartTime;

        /// <summary>
        /// He spends his last strength and slumps over, unconscious. <paramref name="instant"/> skips the slump
        /// (e.g. loading the scene after the opening has been played).
        /// </summary>
        public void Collapse(bool instant = false)
        {
            if (Collapsed) return;
            Collapsed = true;
            Agitated = false;
            collapseStartTime = instant ? float.NegativeInfinity : Time.time;
        }

        private void Update()
        {
            if (visual == null) return;

            if (Collapsed)
            {
                // Slump over a second and a half, then lie still with the glow out.
                float c = Mathf.SmoothStep(0f, 1f, (Time.time - collapseStartTime) / 1.5f);
                visual.localScale = new Vector3(visualRestScale.x * (1f + 0.06f * c), visualRestScale.y * (1f - 0.12f * c), visualRestScale.z);
                visual.localPosition = visualRestPosition + new Vector3(0f, -0.1f * c, 0f);
                visual.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(lean, lean - 16f, c));
                if (corruptionGlow != null) corruptionGlow.Intensity = Mathf.Lerp(0.6f, 0f, c);
                return;
            }

            // Breathing: a slow squash that keeps the base on the ground.
            float breath = Mathf.Sin(Time.time * breathsPerSecond * Mathf.PI * 2f) * breathSquash;
            visual.localScale = new Vector3(visualRestScale.x * (1f - breath), visualRestScale.y * (1f + breath), visualRestScale.z);

            // Corruption: now and then the broken corner glows violet and the body shivers.
            if (Time.time >= nextFlickerTime)
            {
                flickerStartTime = Time.time;
                nextFlickerTime = Time.time + Random.Range(minFlickerInterval, maxFlickerInterval);
            }

            float f = (Time.time - flickerStartTime) / flickerTime;
            float flicker = f >= 0f && f < 1f ? Mathf.Sin(f * Mathf.PI) * (0.6f + 0.4f * Mathf.Sin(Time.time * 70f)) : 0f;
            if (Agitated) flicker = Mathf.Max(flicker, 0.75f + 0.25f * Mathf.Sin(Time.time * 70f));

            if (corruptionGlow != null) corruptionGlow.Intensity = 0.15f + 0.9f * flicker;
            float shake = Agitated ? shiver * 2.5f : shiver;
            Vector3 jitter = flicker > 0f ? (Vector3)(Random.insideUnitCircle * shake * flicker) : Vector3.zero;
            visual.localPosition = visualRestPosition + jitter;
            visual.localRotation = Quaternion.Euler(0f, 0f, lean + flicker * 2.5f * Mathf.Sin(Time.time * 50f));
        }
    }
}
