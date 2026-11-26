using System.Collections;
using UnityEngine;

namespace ProjectCircle.Enemies.Boss
{
    /// <summary>
    /// Vertex Barrage. The boss leaps to the middle of the arena, jumps straight up and hangs at the top,
    /// firing lasers from all three corners at once. It turns a little and fires again, three volleys in all,
    /// then drops back down.
    ///
    /// Before each volley, thin trace lines show where the beams will go (<see cref="traceTime"/>). At the top it
    /// turns so one corner points straight at the circle, so the first volley always has a beam on it (move!);
    /// the turns between volleys are fixed, so the rest of the pattern can be learned. Beams pass through ledges.
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

        [Tooltip("At the top it turns this long to point a corner at the circle before the first volley.")]
        [SerializeField, Min(0.05f)] private float aimTime = 0.35f;

        [Tooltip("Turn between volleys, in degrees (negative = clockwise). Fixed so the pattern can be learned.")]
        [SerializeField] private float turnPerVolley = -40f;

        [Tooltip("Before each volley, thin trace lines show where the beams will go for this long (they don't hurt).")]
        [SerializeField, Min(0f)] private float traceTime = 0.45f;

        [SerializeField] private Color traceColor = new Color(1f, 0.82f, 0.55f, 0.65f);

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
        private LineRenderer[] traces;
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

            // Turn so the nearest corner points straight at the circle: the first volley always has a beam on it.
            // (Following it during the turn, then locking on as it fires: move to dodge.)
            float angle = 0f;
            for (float t = 0f; t < aimTime; t += Time.deltaTime)
            {
                float target = AimAngle();
                angle = Mathf.LerpAngle(angle, target, Mathf.Clamp01(t / aimTime * 1.5f));
                Boss.SetPose(Vector2.zero, angle);
                yield return null;
            }
            angle = AimAngle();
            Boss.SetPose(Vector2.zero, angle);

            for (int v = 0; v < volleys; v++)
            {
                // Trace: thin lines show where this volley will go, blinking faster just before it fires.
                if (traceTime > 0f)
                {
                    SetTraces(true);
                    for (float t = 0f; t < traceTime; t += Time.deltaTime)
                    {
                        float p = t / traceTime;
                        bool on = Mathf.Repeat(t * Mathf.Lerp(6f, 16f, p), 1f) < 0.6f;
                        UpdateTraces(on ? Mathf.Lerp(0.6f, 1f, p) : 0.25f);
                        Boss.Shape.SetCornerGlow(RoundedTriangleMesh.AllCorners, Mathf.Lerp(0.4f, 1f, p), beamColor); // all three vertices charge
                        yield return null;
                    }
                    SetTraces(false);
                }

                // Fire.
                for (int i = 0; i < 3; i++) parried[i] = false;
                SetBeams(true);
                for (int i = 0; i < 3; i++) Systems.Fx.Ring(Boss.Corner(i), Color.white, 1.1f, 0.2f, 0.12f); // muzzle flash
                if (Boss.CameraFollow != null) Boss.CameraFollow.Shake(0.1f, 0.15f);
                for (float t = 0f; t < fireTime; t += Time.deltaTime)
                {
                    UpdateBeams();
                    Boss.Shape.SetCornerGlow(RoundedTriangleMesh.AllCorners, 1f, Color.white); // white-hot while firing
                    yield return null;
                }
                SetBeams(false);
                Boss.Shape.SetCornerGlow(RoundedTriangleMesh.AllCorners, 0.35f, beamColor); // cooling between volleys

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
            Boss.Shape.SetCornerGlow(-1, 0f, beamColor);
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
            SetTraces(false);
            if (Boss != null && Boss.Shape != null) Boss.Shape.SetCornerGlow(-1, 0f, beamColor);
            RestoreGravity();
            if (Boss != null) Boss.ResetPose();
        }

        /// <summary>
        /// The smallest turn (degrees, from upright) that points one of the three corners straight at the circle.
        /// At rest the corners point at 90°, 210° and 330°.
        /// </summary>
        private float AimAngle()
        {
            if (Boss.Target == null) return 0f;
            Vector2 to = (Vector2)Boss.Target.position - (Vector2)Boss.Shape.transform.position;
            float aim = Mathf.Atan2(to.y, to.x) * Mathf.Rad2Deg;
            float best = 0f, bestAbs = float.PositiveInfinity;
            for (int k = 0; k < 3; k++)
            {
                float turn = Mathf.DeltaAngle(90f + 120f * k, aim);
                if (Mathf.Abs(turn) < bestAbs) { bestAbs = Mathf.Abs(turn); best = turn; }
            }
            return best;
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

        private void SetTraces(bool on)
        {
            if (traces == null) return;
            foreach (LineRenderer t in traces) t.enabled = on;
        }

        /// <summary>Lays each trace along the path its beam will take (same clipping as the beams).</summary>
        private void UpdateTraces(float alpha)
        {
            Vector2 centre = Boss.Shape.transform.position;
            Color c = new Color(traceColor.r, traceColor.g, traceColor.b, traceColor.a * alpha);
            for (int i = 0; i < 3; i++)
            {
                Vector2 origin = Boss.Corner(i);
                Vector2 dir = (origin - centre).normalized;
                Vector2 end = Boss.ClipToArena(origin, dir);
                if (Vector2.Distance(origin, end) > beamLength) end = origin + dir * beamLength;
                traces[i].SetPosition(0, origin);
                traces[i].SetPosition(1, end);
                traces[i].startColor = c;
                traces[i].endColor = new Color(c.r, c.g, c.b, c.a * 0.5f);
            }
        }

        private void EnsureBeams()
        {
            if (traces == null)
            {
                traces = new LineRenderer[3];
                for (int i = 0; i < 3; i++)
                {
                    var go = new GameObject("Barrage Trace " + i);
                    go.transform.SetParent(transform, false);
                    var line = go.AddComponent<LineRenderer>();
                    line.sharedMaterial = beamMaterial != null ? beamMaterial : Systems.Fx.Material;
                    line.useWorldSpace = true;
                    line.positionCount = 2;
                    line.widthMultiplier = 0.08f;
                    line.sortingOrder = 8;
                    line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    line.receiveShadows = false;
                    line.enabled = false;
                    traces[i] = line;
                }
            }
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
