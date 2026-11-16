using UnityEngine;

namespace ProjectCircle.Systems
{
    /// <summary>
    /// Endless, gentle ambient particles inside a box: dust motes drifting in a light shaft, sparkles in front
    /// of a mirror, motes in the Hall of Strings, a glow seeping out of a doorway. Little squares, no textures.
    ///
    /// Add to an object, set the box (centred on the object), colour and drift in the Inspector.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AmbientParticles : MonoBehaviour
    {
        [Tooltip("Size of the box particles appear in (centred on this object).")]
        [SerializeField] private Vector2 area = new Vector2(2f, 3f);

        [SerializeField] private Color color = new Color(1f, 0.95f, 0.8f, 0.5f);

        [Tooltip("Particles per second.")]
        [SerializeField, Min(0f)] private float rate = 6f;

        [SerializeField] private Vector2 sizeRange = new Vector2(0.04f, 0.1f);

        [Tooltip("Seconds each particle lives.")]
        [SerializeField] private Vector2 lifeRange = new Vector2(2f, 4f);

        [Tooltip("Average drift velocity (e.g. slightly down for falling dust, up for rising motes).")]
        [SerializeField] private Vector2 drift = new Vector2(0f, 0.3f);

        [Tooltip("Random wobble added to the drift.")]
        [SerializeField, Min(0f)] private float wobble = 0.2f;

        [SerializeField] private int sortingOrder = -1;

        private void Awake()
        {
            var ps = gameObject.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeRange.x, lifeRange.y);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeRange.x, sizeRange.y);
            main.startSpeed = 0f;
            main.startColor = color;
            main.maxParticles = 300;

            var emission = ps.emission;
            emission.rateOverTime = rate;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Rectangle;
            shape.scale = new Vector3(area.x, area.y, 1f);

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(drift.x - wobble, drift.x + wobble);
            velocity.y = new ParticleSystem.MinMaxCurve(drift.y - wobble, drift.y + wobble);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            // Fade in and out so nothing pops.
            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                             new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            colour.color = gradient;

            var renderer = GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = Fx.Material;
            renderer.sortingOrder = sortingOrder;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            ps.Play();
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(color.r, color.g, color.b, 0.6f);
            Gizmos.DrawWireCube(transform.position, new Vector3(area.x, area.y, 0f));
        }
#endif
    }
}
