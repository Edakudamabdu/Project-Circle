using System.Collections;
using UnityEngine;

namespace ProjectCircle.Systems.Story
{
    /// <summary>
    /// A triangle that only acts in cutscenes (no physics, no AI): it hops along the ground, glares at a
    /// target, laughs, shoves and rears up. Cutscene scripts drive it with the coroutines below.
    ///
    /// Setup: the root sits at the triangle's centre (its base resting on the floor). Children: a
    /// <see cref="visual"/> (the RoundedTriangleMesh), and optionally an <see cref="eye"/> with a pupil child.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StoryActor : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform visual;
        [SerializeField] private Transform eye;

        [Tooltip("Optional. Defaults to the eye's first child.")]
        [SerializeField] private Transform pupil;

        [Header("Feel")]
        [Tooltip("Height of each little hop while walking.")]
        [SerializeField, Min(0f)] private float hopHeight = 0.14f;

        [Tooltip("Hops per world unit walked.")]
        [SerializeField, Min(0.1f)] private float hopsPerUnit = 1.3f;

        [Tooltip("How far the pupil moves inside the eye (eye-local units).")]
        [SerializeField, Min(0f)] private float pupilTravel = 0.22f;

        private Transform lookTarget;
        private Vector3 visualRest;
        private Vector3 eyeRest;
        private float bob;          // vertical offset from hopping/laughing
        private float lean;         // degrees, + leans back (counter-clockwise)
        private float leanTarget;

        private void Awake()
        {
            if (eye != null && pupil == null && eye.childCount > 0) pupil = eye.GetChild(0);
            if (visual != null) visualRest = visual.localPosition;
            if (eye != null) eyeRest = eye.localPosition;
        }

        private void LateUpdate()
        {
            lean = Mathf.MoveTowards(lean, leanTarget, 160f * Time.deltaTime);

            if (visual != null)
            {
                visual.localPosition = visualRest + new Vector3(0f, bob, 0f);
                visual.localRotation = Quaternion.Euler(0f, 0f, lean);
            }
            if (eye != null) eye.localPosition = eyeRest + new Vector3(0f, bob, 0f);

            if (pupil != null && lookTarget != null)
            {
                Vector2 dir = ((Vector2)(lookTarget.position - pupil.parent.position)).normalized;
                pupil.localPosition = new Vector3(dir.x, dir.y, 0f) * pupilTravel;
            }
        }

        /// <summary>Keeps the eye on <paramref name="target"/> (null to stop).</summary>
        public void LookAt(Transform target) => lookTarget = target;

        /// <summary>Hops along the ground to <paramref name="x"/> at <paramref name="speed"/> units/second.</summary>
        public IEnumerator WalkTo(float x, float speed)
        {
            Vector3 start = transform.position;
            float distance = Mathf.Abs(x - start.x);
            if (distance < 0.01f) yield break;

            float duration = distance / Mathf.Max(0.1f, speed);
            float hops = Mathf.Max(1f, Mathf.Round(distance * hopsPerUnit));
            float dir = Mathf.Sign(x - start.x);
            leanTarget = -6f * dir; // lean into the walk

            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float p = t / duration;
                transform.position = new Vector3(Mathf.Lerp(start.x, x, p), start.y, start.z);
                bob = Mathf.Abs(Mathf.Sin(p * hops * Mathf.PI)) * hopHeight;
                yield return null;
            }

            transform.position = new Vector3(x, start.y, start.z);
            bob = 0f;
            leanTarget = 0f;
        }

        /// <summary>Quick, mocking little bounces.</summary>
        public IEnumerator Laugh(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                bob = Mathf.Abs(Mathf.Sin(t * 22f)) * hopHeight * 0.6f;
                leanTarget = 6f * Mathf.Sin(t * 11f);
                yield return null;
            }
            bob = 0f;
            leanTarget = 0f;
        }

        /// <summary>
        /// Lunges towards <paramref name="direction"/> (-1 left, +1 right) and back. <paramref name="onContact"/>
        /// runs at the furthest point (e.g. to push the circle).
        /// </summary>
        public IEnumerator Shove(float direction, float distance = 0.7f, System.Action onContact = null)
        {
            Vector3 start = transform.position;
            Vector3 end = start + new Vector3(direction * distance, 0f, 0f);
            leanTarget = -14f * direction;

            for (float t = 0f; t < 0.1f; t += Time.deltaTime)
            {
                transform.position = Vector3.Lerp(start, end, t / 0.1f);
                yield return null;
            }
            transform.position = end;
            onContact?.Invoke();

            for (float t = 0f; t < 0.35f; t += Time.deltaTime)
            {
                transform.position = Vector3.Lerp(end, start, Mathf.SmoothStep(0f, 1f, t / 0.35f));
                yield return null;
            }
            transform.position = start;
            leanTarget = 0f;
        }

        /// <summary>Hides or shows the whole actor.</summary>
        public void SetVisible(bool visible) => gameObject.SetActive(visible);
    }
}
