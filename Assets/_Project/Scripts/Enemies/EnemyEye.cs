using UnityEngine;

namespace ProjectCircle.Enemies
{
    /// <summary>
    /// A triangle's eye for enemies that don't run on <see cref="TriangleEnemy"/> (which drives its own eye).
    /// It sits at the centre of the shape, following it as the shape stretches, tilts or turns, but always stays
    /// upright and round. The pupil looks at the circle, and the eye blinks now and then.
    ///
    /// Setup: put this on the "Eye" object (a white circle sprite, a child of the enemy root) with the pupil (a
    /// dark circle sprite) as its first child. Draw both in front of the shape (higher Order in Layer).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyEye : MonoBehaviour
    {
        [Tooltip("The shape the eye rides on. Defaults to the first RoundedTriangleMesh on the parent.")]
        [SerializeField] private RoundedTriangleMesh shape;

        [Tooltip("Optional. Defaults to this object's first child.")]
        [SerializeField] private Transform pupil;

        [Tooltip("0 = the centre of the triangle; positive moves it towards the point (corner 0), negative towards the base.")]
        [SerializeField, Range(-0.6f, 0.6f)] private float towardsPoint = 0.1f;

        [Tooltip("How far the pupil moves inside the eye (eye-local units; the eye is 1 wide).")]
        [SerializeField, Min(0f)] private float pupilTravel = 0.22f;

        [SerializeField, Min(0.5f)] private float blinkEvery = 3.5f;

        [Tooltip("Optional. Blink together with this eye (for a face with two eyes).")]
        [SerializeField] private EnemyEye blinkWith;

        // How open the eye is this frame (1 open, 0.1 shut): read by a partner eye.
        private float openness = 1f;

        private Transform player;
        private Vector3 baseScale;
        private float nextBlink;

        private void Awake()
        {
            if (shape == null && transform.parent != null) shape = transform.parent.GetComponentInChildren<RoundedTriangleMesh>();
            if (pupil == null && transform.childCount > 0) pupil = transform.GetChild(0);
            baseScale = transform.localScale;
            nextBlink = Time.time + Random.Range(1f, blinkEvery);
        }

        private void Start()
        {
            var movement = FindAnyObjectByType<Player.PlayerMovement>();
            if (movement != null) player = movement.transform;
        }

        private void LateUpdate()
        {
            // Ride the shape's centre (after it has moved, stretched or turned this frame), but stay upright.
            if (shape != null)
            {
                Vector2 c0 = shape.GetCornerPoint(0), c1 = shape.GetCornerPoint(1), c2 = shape.GetCornerPoint(2);
                Vector2 centre = (c0 + c1 + c2) / 3f;
                Vector3 world = shape.transform.TransformPoint(centre + (c0 - centre) * towardsPoint);
                transform.position = new Vector3(world.x, world.y, transform.position.z);
            }
            transform.rotation = Quaternion.identity;

            // A quick blink: squash the eye flat for a moment.
            float blink = 1f;
            float sinceBlink = Time.time - nextBlink;
            if (sinceBlink >= 0f)
            {
                blink = Mathf.Abs(1f - sinceBlink / 0.06f); // closes then opens over 0.12 s
                if (sinceBlink >= 0.12f) { blink = 1f; nextBlink = Time.time + blinkEvery * Random.Range(0.6f, 1.4f); }
            }
            openness = Mathf.Max(0.1f, blink);
            if (blinkWith != null) openness = blinkWith.openness;
            transform.localScale = new Vector3(baseScale.x, baseScale.y * openness, baseScale.z);

            // The pupil looks at the circle.
            if (pupil != null && player != null)
            {
                Vector2 dir = ((Vector2)(player.position - transform.position)).normalized;
                pupil.localPosition = new Vector3(dir.x, dir.y, 0f) * pupilTravel;
            }
        }
    }
}
