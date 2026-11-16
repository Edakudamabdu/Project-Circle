using System.Collections;
using ProjectCircle.Enemies;
using UnityEngine;

namespace ProjectCircle.Systems.Story
{
    /// <summary>
    /// The circle's point of view while the triangles stand over it: huge triangles lean in from above,
    /// points aimed down at the viewer, their corners glowing a hostile red.
    ///
    /// A separate little set built away from the level. The cutscene cuts the camera to <see cref="CameraPoint"/>,
    /// calls <see cref="Begin"/>, then plays <see cref="CreepIn"/>, <see cref="RearUp"/> and <see cref="Lunge"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PovShot : MonoBehaviour
    {
        [Tooltip("The towering triangles (children). Each is pushed in along the line from the camera point to it.")]
        [SerializeField] private Transform[] towering;

        [Tooltip("The one who strikes (one of the towering triangles).")]
        [SerializeField] private Transform leader;

        [Tooltip("Glows on the triangles' points.")]
        [SerializeField] private VertexGlow[] glows;

        [Tooltip("Where the camera looks (the circle's eye).")]
        [SerializeField] private Transform cameraPoint;

        [SerializeField, Min(1f)] private float cameraSize = 6f;

        /// <summary>Camera centre for the shot.</summary>
        public Vector2 CameraPoint => cameraPoint != null ? (Vector2)cameraPoint.position : (Vector2)transform.position;

        /// <summary>Orthographic size for the shot.</summary>
        public float CameraSize => cameraSize;

        private Vector3[] restPositions;
        private float glowLevel;

        private void Awake()
        {
            restPositions = new Vector3[towering.Length];
            for (int i = 0; i < towering.Length; i++) restPositions[i] = towering[i].position;
        }

        private void Update()
        {
            // Slow, menacing pulse on every point.
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 5f);
            foreach (VertexGlow g in glows)
            {
                if (g != null) g.Intensity = glowLevel * pulse;
            }
        }

        /// <summary>Shows the set with the triangles a little further out.</summary>
        public void Begin()
        {
            gameObject.SetActive(true);
            SetPush(-0.12f);
            glowLevel = 0.4f;
        }

        /// <summary>Hides the set.</summary>
        public void End() => gameObject.SetActive(false);

        /// <summary>The triangles slowly close in and their points glow brighter.</summary>
        public IEnumerator CreepIn(float time)
        {
            for (float t = 0f; t < time; t += Time.deltaTime)
            {
                float p = t / time;
                SetPush(Mathf.Lerp(-0.12f, 0.06f, p));
                glowLevel = Mathf.Lerp(0.4f, 1.1f, p);
                yield return null;
            }
        }

        /// <summary>The leader pulls back, ready to strike.</summary>
        public IEnumerator RearUp(float time)
        {
            Vector3 from = leader.position;
            Vector3 to = from + (from - (Vector3)CameraPoint).normalized * 1.6f;
            for (float t = 0f; t < time; t += Time.deltaTime)
            {
                leader.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / time));
                yield return null;
            }
        }

        /// <summary>The leader drives its point down at the viewer.</summary>
        public IEnumerator Lunge(float time)
        {
            Vector3 from = leader.position;
            Vector3 to = Vector3.Lerp(from, (Vector3)CameraPoint, 0.65f);
            to.z = from.z;
            for (float t = 0f; t < time; t += Time.deltaTime)
            {
                float p = t / time;
                leader.position = Vector3.Lerp(from, to, p * p);
                yield return null;
            }
        }

        /// <summary>Moves every triangle along its line to the camera point: + towards it, - away.</summary>
        private void SetPush(float amount)
        {
            Vector3 centre = CameraPoint;
            for (int i = 0; i < towering.Length; i++)
            {
                Vector3 rest = restPositions[i];
                Vector3 toward = centre - rest;
                toward.z = 0f;
                towering[i].position = rest + toward * amount;
            }
        }
    }
}
