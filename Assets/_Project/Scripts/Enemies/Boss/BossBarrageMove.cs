using System.Collections;
using UnityEngine;

namespace ProjectCircle.Enemies.Boss
{
    /// <summary>
    /// Vertex Barrage. The boss leaps to the middle of the arena, jumps straight up and hangs at the top,
    /// firing lasers from all three corners at once. It turns a little and fires again, three volleys in all,
    /// then drops back down.
    ///
    /// There is deliberately no warning glow: the cue is the boss going to the centre and jumping. The beam
    /// angles are the same every time, so players learn where to stand. The beams pass through ledges.
    /// A parried beam hurts the boss instead.
    /// </summary>
    public sealed class BossBarrageMove : BossMove
    {
        [Header("Getting there")]
        [SerializeField, Min(0.5f)] private float leapHeight = 2.5f;

        [Tooltip("How high it jumps from the centre before firing (centre of the triangle, above where it stood).")]
        [SerializeField, Min(1f)] private float hangHeight = 4.5f;

        [Header("Volleys")]
        [SerializeField, Range(1, 6)] private int volleys = 3;

        [Tooltip("Turn between volleys, in degrees (negative = clockwise). Fixed so the pattern can be learned.")]
        [SerializeField] private float turnPerVolley = -40f;

        [Tooltip("How long each volley's beams stay on (they hurt the whole time).")]
        [SerializeField, Min(0.05f)] private float fireTime = 0.4f;

        [SerializeField, Min(0.05f)] private float turnTime = 0.35f;
        [SerializeField, Min(0)] private int damage = 1;

        [Header("Look")]
        [Tooltip("Vertex-colour material (e.g. ShapeUnlit).")]
        [SerializeField] private Material beamMaterial;

        [SerializeField] private Color beamColor = new Color(1f, 0.82f, 0.55f, 1f);
        [SerializeField, Min(0.02f)] private float beamWidth = 0.32f;
        [SerializeField, Min(1f)] private float beamLength = 45f;

        private LineRenderer[] beams;
        private readonly bool[] parried = new bool[3];
        private bool hurtThisBarrage;
        private readonly float[] nextSparkTime = new float[3];
        private float savedGravity = -1f;

        public override IEnumerator Run()
        {
            EnsureBeams();
            hurtThisBarrage = false;

            // Into the middle (exactly: the pattern is only learnable if it always fires from the same spot).
            for (int tries = 0; tries < 3 && Mathf.Abs(Boss.Body.position.x - Boss.ArenaCentreX) > 0.3f; tries++)
                yield return Boss.LeapTo(Boss.ArenaCentreX, leapHeight);
            yield return new WaitForSeconds(0.12f);

            // Straight up, and hang at the top.
            Boss.Body.linearVelocity = new Vector2(0f, Mathf.Sqrt(2f * Boss.Gravity * hangHeight));
            while (Boss.Body.linearVelocity.y > 0f) yield return null;
            savedGravity = Boss.Body.gravityScale;
            Boss.Body.gravityScale = 0f;
            Boss.Body.linearVelocity = Vector2.zero;

            float angle = 0f;
            for (int v = 0; v < volleys; v++)
            {
                // Fire.
                for (int i = 0; i < 3; i++) parried[i] = false;
                SetBeams(true);
                for (int i = 0; i < 3; i++) Systems.Fx.Ring(Boss.Corner(i), Color.white, 1.1f, 0.2f, 0.12f); // muzzle flash
                if (Boss.CameraFollow != null) Boss.CameraFollow.Shake(0.1f, 0.15f);
                for (float t = 0f; t < fireTime; t += Time.deltaTime)
                {
                    UpdateBeams();
                    yield return null;
                }
                SetBeams(false);

                // Turn for the next volley.
                if (v < volleys - 1)
                {
                    float from = angle;
                    for (float t = 0f; t < turnTime; t += Time.deltaTime)
                    {
                        angle = Mathf.Lerp(from, from + turnPerVolley, Mathf.SmoothStep(0f, 1f, t / turnTime));
                        Boss.SetPose(Vector2.zero, angle);
                        yield return null;
                    }
                    angle = from + turnPerVolley;
                    Boss.SetPose(Vector2.zero, angle);
                }
            }

            // Drop, turning back to the nearest upright pose on the way down.
            RestoreGravity();
            float settleFrom = angle;
            float settleTo = Mathf.Round(angle / 120f) * 120f;
            float start = Time.time;
            while (Time.time - start < 0.1f || !Boss.IsGrounded)
            {
                float p = Mathf.Clamp01((Time.time - start) / 0.3f);
                Boss.SetPose(Vector2.zero, Mathf.Lerp(settleFrom, settleTo, p));
                if (Time.time - start > 3f) break;
                yield return null;
            }
            Boss.ResetPose();
            Boss.Body.linearVelocity = Vector2.zero;
            if (Boss.CameraFollow != null) Boss.CameraFollow.Shake(0.22f, 0.25f);
        }

        public override void Cancel()
        {
            SetBeams(false);
            RestoreGravity();
            if (Boss != null) Boss.ResetPose();
        }

        private void RestoreGravity()
        {
            if (savedGravity < 0f || Boss == null) return;
            Boss.Body.gravityScale = savedGravity;
            savedGravity = -1f;
        }

        /// <summary>Points each beam out from the centre through its corner, stops it at walls/ledges, and checks for hits.</summary>
        private void UpdateBeams()
        {
            Vector2 centre = Boss.Shape.transform.position;
            for (int i = 0; i < 3; i++)
            {
                Vector2 origin = Boss.Corner(i);
                Vector2 dir = (origin - centre).normalized;
                // Through the ledges: the beams only stop at the arena's walls, floor and ceiling.
                Vector2 end = Boss.ClipToArena(origin, dir);
                float length = Mathf.Min(Vector2.Distance(origin, end), beamLength);

                float flicker = 1f + 0.15f * Mathf.Sin(Time.time * 60f + i);
                beams[i].widthMultiplier = beamWidth * flicker;
                beams[i].SetPosition(0, origin);
                beams[i].SetPosition(1, end);

                // Sparks where the beam strikes the wall or floor.
                if (Time.time >= nextSparkTime[i])
                {
                    nextSparkTime[i] = Time.time + 0.06f;
                    float back = Mathf.Atan2(-dir.y, -dir.x) * Mathf.Rad2Deg;
                    Systems.Fx.Sparks(end, back, beamColor, 3, 6f, 70f);
                }

                RaycastHit2D player = Physics2D.CircleCast(origin, beamWidth * 0.5f, dir, length, Boss.PlayerLayers);
                if (player.collider == null) continue;

                // Parried: the beam bites back into the boss (once per volley per beam).
                if (Boss.TargetParry != null && Boss.TargetParry.TryDeflect(player.point))
                {
                    if (!parried[i])
                    {
                        parried[i] = true;
                        Boss.Health.TakeHit(1, player.point);
                    }
                    continue;
                }
                // One hit per barrage: a mistake costs one mask, not two.
                if (!hurtThisBarrage && Boss.TargetHealth != null && Boss.TargetHealth.TakeDamage(damage, origin)) hurtThisBarrage = true;
            }
        }

        private void SetBeams(bool on)
        {
            if (beams == null) return;
            foreach (LineRenderer b in beams) b.enabled = on;
        }

        private void EnsureBeams()
        {
            if (beams != null) return;
            beams = new LineRenderer[3];
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("Barrage Beam " + i);
                go.transform.SetParent(transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.sharedMaterial = beamMaterial;
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.numCapVertices = 3;
                line.sortingOrder = 9;
                line.startColor = Color.white;
                line.endColor = beamColor;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.enabled = false;
                beams[i] = line;
            }
        }
    }
}
