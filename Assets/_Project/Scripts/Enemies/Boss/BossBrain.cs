using System;
using System.Collections;
using System.Collections.Generic;
using ProjectCircle.Combat;
using ProjectCircle.Player;
using ProjectCircle.Systems;
using ProjectCircle.Systems.Audio;
using UnityEngine;

namespace ProjectCircle.Enemies.Boss
{
    /// <summary>
    /// Runs a boss: waits until <see cref="BeginFight"/>, then loops — pick a ready <see cref="BossMove"/>
    /// (weighted at random), run it, pause for its recovery, repeat.
    ///
    /// Phases: as health drops past each value in <see cref="phaseThresholds"/> the boss enters the next phase.
    /// From phase 2 it is enraged: it roars once (crimson, shaking), then barely pauses between moves and every
    /// opening shrinks (<see cref="WindowScale"/>). Hits flash it white. On death it shatters.
    ///
    /// Also provides the motions its moves share, so every move moves the same way:
    /// - <see cref="TumbleStep"/>: tip over the front corner and land on the next face (how a triangle walks).
    /// - <see cref="LeapTo"/>: a ballistic jump to a point on the floor.
    /// - <see cref="SetPivotPose"/>: tip forward over the front corner (the thrust) without moving.
    /// - <see cref="SetPose"/>: free offset/rotation of the shape (spinning in the air, trembling).
    /// The body (Rigidbody2D and collider) stays upright; only the visual tips and turns.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(EnemyHealth))]
    public sealed class BossBrain : MonoBehaviour, IArenaBoss
    {
        [Header("Identity")]
        [SerializeField] private string bossName = "Base Warden";

        [Header("References")]
        [SerializeField] private RoundedTriangleMesh shape;
        [SerializeField] private Transform eye;

        [Tooltip("Optional. Defaults to the eye's first child.")]
        [SerializeField] private Transform pupil;

        [Tooltip("The triangle-shaped collider the circle's attacks hit. Optional: found on the shape ('Hurtbox') if empty. A circle hit while inside it is pushed out.")]
        [SerializeField] private Collider2D hurtbox;

        [Header("Arena")]
        [Tooltip("Middle of the arena floor area (only its x is used).")]
        [SerializeField] private Transform arenaCentre;

        [Tooltip("Half the arena's width, wall to wall.")]
        [SerializeField, Min(1f)] private float arenaHalfWidth = 17f;

        [Tooltip("Floor to ceiling. The floor is wherever the boss stands when the scene starts.")]
        [SerializeField, Min(1f)] private float arenaHeight = 16f;

        [Tooltip("Colliders the boss passes through (the arena's ledges), so its leaps never catch on them.")]
        [SerializeField] private Collider2D[] passThrough;

        [Header("Fight")]
        [Tooltip("Health fractions where the next phase starts, highest first (0.5 = phase 2 at half health).")]
        [SerializeField] private float[] phaseThresholds = { 0.5f };

        [Tooltip("Pause before the very first move.")]
        [SerializeField, Min(0f)] private float openingDelay = 0.8f;

        [Tooltip("Extra pause between moves in phase 1 (on top of each move's own recovery).")]
        [SerializeField, Min(0f)] private float idleBetweenMoves = 0.25f;

        [SerializeField] private LayerMask groundLayers = 1 << 3;
        [SerializeField] private LayerMask playerLayers = 1 << 8;

        [Header("Phase 2 (enraged)")]
        [SerializeField, Min(0f)] private float phase2IdleBetweenMoves = 0.05f;

        [Tooltip("Openings (recoveries) are multiplied by this in phase 2.")]
        [SerializeField, Range(0.1f, 1f)] private float phase2WindowScale = 0.45f;

        [SerializeField] private Color phase2Top = new Color(0.78f, 0.32f, 0.38f, 1f);
        [SerializeField] private Color phase2Bottom = new Color(0.46f, 0.12f, 0.2f, 1f);
        [SerializeField, Min(0f)] private float roarTime = 1f;

        [Header("Look")]
        [SerializeField] private Color flashColor = Color.white;
        [SerializeField, Min(0f)] private float flashTime = 0.08f;
        [SerializeField] private Color shatterGlow = new Color(1f, 0.8f, 0.6f, 1f);
        [SerializeField, Min(0f)] private float pupilTravel = 0.22f;

        /// <summary>Raised when the fight begins.</summary>
        public event Action FightStarted;

        /// <summary>Raised when a new phase begins. Argument: the phase (2, 3...).</summary>
        public event Action<int> PhaseChanged;

        /// <summary>Raised once when the boss is beaten.</summary>
        public event Action Defeated;

        /// <summary>Raised when the fight is reset (the player died).</summary>
        public event Action FightReset;

        #region Properties

        public string BossName => bossName;
        public EnemyHealth Health { get; private set; }
        public Rigidbody2D Body { get; private set; }
        public RoundedTriangleMesh Shape => shape;
        public Transform Target { get; private set; }
        public PlayerHealth TargetHealth { get; private set; }
        public PlayerParry TargetParry { get; private set; }
        public PlayerMovement TargetMovement { get; private set; }
        public CameraFollow2D CameraFollow { get; private set; }
        public int Phase { get; private set; } = 1;
        public bool IsFighting { get; private set; }
        public BossMove CurrentMove { get; private set; }

        /// <summary>True from phase 2 on.</summary>
        public bool Enraged => Phase >= 2;

        /// <summary>Multiply openings (recoveries, holds) by this: smaller once enraged.</summary>
        public float WindowScale => Enraged ? phase2WindowScale : 1f;

        public LayerMask GroundLayers => groundLayers;
        public LayerMask PlayerLayers => playerLayers;

        public float Circumradius => shape != null ? shape.Circumradius : 1f;
        public float Inradius => Circumradius * 0.5f;
        public float SideLength => Circumradius * 1.7320508f;

        /// <summary>Height of the floor it stands on (valid while grounded).</summary>
        public float FloorY => Body.position.y - Inradius;

        public float ArenaCentreX => arenaCentre != null ? arenaCentre.position.x : Body.position.x;
        public float ArenaMinX => ArenaCentreX - arenaHalfWidth;
        public float ArenaMaxX => ArenaCentreX + arenaHalfWidth;
        public float ArenaFloorY { get; private set; }
        public float ArenaCeilingY => ArenaFloorY + arenaHeight;

        /// <summary>Closest its centre can get to a wall while standing.</summary>
        public float WallMargin => SideLength * 0.5f + 0.15f;

        /// <summary>Gravity acting on the body, in units/s².</summary>
        public float Gravity => Mathf.Abs(Physics2D.gravity.y) * baseGravityScale;

        /// <summary>-1 or +1: which side the circle is on.</summary>
        public int DirectionToTarget => Target != null && Target.position.x < Body.position.x ? -1 : 1;

        /// <summary>Horizontal distance to the circle.</summary>
        public float DistanceToTarget => Target != null ? Mathf.Abs(Target.position.x - Body.position.x) : float.PositiveInfinity;

        /// <summary>True while standing on the ground.</summary>
        public bool IsGrounded
        {
            get
            {
                Bounds b = collider2d.bounds;
                int count = Physics2D.OverlapBox(new Vector2(b.center.x, b.min.y - 0.05f), new Vector2(b.size.x * 0.6f, 0.1f), 0f,
                                                 new ContactFilter2D { useLayerMask = true, layerMask = groundLayers }, groundBuffer);
                for (int i = 0; i < count; i++)
                {
                    if (!IsPassThrough(groundBuffer[i])) return true; // ledges it passes through don't count
                }
                return false;
            }
        }

        /// <summary>Material of the shape (for debris and effects).</summary>
        public Material ShapeMaterial => shape != null ? shape.GetComponent<MeshRenderer>().sharedMaterial : null;

        /// <summary>The arena's ledges (the colliders it normally passes through).</summary>
        public IReadOnlyList<Collider2D> Ledges => passThrough ?? new Collider2D[0];

        /// <summary>The ledge the circle is standing on right now, or null (on the floor, or in the air).</summary>
        public Collider2D TargetLedge
        {
            get
            {
                if (Target == null || passThrough == null) return null;
                Vector2 feet = (Vector2)Target.position + Vector2.down * 0.55f;
                foreach (Collider2D ledge in passThrough)
                {
                    if (ledge == null) continue;
                    Bounds b = ledge.bounds;
                    if (feet.x >= b.min.x - 0.3f && feet.x <= b.max.x + 0.3f && Mathf.Abs(feet.y - b.max.y) < 0.25f) return ledge;
                }
                return null;
            }
        }

        #endregion

        private Collider2D collider2d;
        private Collider2D targetCollider;
        private Rigidbody2D targetBody;
        private readonly Collider2D[] groundBuffer = new Collider2D[4];
        private readonly List<BossMove> moves = new List<BossMove>();
        private Coroutine loop;
        private Coroutine moveRoutine;
        private float flashUntil;
        private bool flashing;
        private bool roarPending;
        private Color calmTop, calmBottom;
        private Color baseTop, baseBottom;
        private Vector3 shapeRest, eyeRest;
        private float baseGravityScale;

        #region Unity lifecycle

        private void Awake()
        {
            Body = GetComponent<Rigidbody2D>();
            Health = GetComponent<EnemyHealth>();
            collider2d = GetComponent<Collider2D>();
            if (passThrough != null)
            {
                foreach (Collider2D c in passThrough)
                {
                    if (c != null) Physics2D.IgnoreCollision(collider2d, c, true);
                }
            }
            baseGravityScale = Body.gravityScale;
            Body.freezeRotation = true;
            if (eye != null && pupil == null && eye.childCount > 0) pupil = eye.GetChild(0);
            if (hurtbox == null && shape != null)
            {
                Transform t = shape.transform.Find("Hurtbox");
                if (t != null) hurtbox = t.GetComponent<Collider2D>();
            }

            GetComponents(moves);
            foreach (BossMove m in moves) m.Bind(this);

            if (shape != null)
            {
                calmTop = baseTop = shape.FillColor;
                calmBottom = baseBottom = shape.FillBottomColor;
                shapeRest = shape.transform.localPosition;
            }
            if (eye != null) eyeRest = eye.localPosition;
        }

        private void Start()
        {
            var player = FindAnyObjectByType<PlayerMovement>();
            if (player != null)
            {
                Target = player.transform;
                TargetHealth = player.GetComponent<PlayerHealth>();
                TargetParry = player.GetComponent<PlayerParry>();
                TargetMovement = player;
                targetCollider = player.GetComponent<Collider2D>();
                targetBody = player.GetComponent<Rigidbody2D>();
                if (TargetHealth != null) TargetHealth.Hit += HandleTargetHit;
            }
            CameraFollow = FindAnyObjectByType<CameraFollow2D>();
            ArenaFloorY = FloorY; // it starts the scene standing on the arena floor
        }

        private void OnEnable()
        {
            Health.Hit += HandleHit;
            Health.Died += HandleDied;
        }

        private void OnDisable()
        {
            Health.Hit -= HandleHit;
            Health.Died -= HandleDied;
        }

        private void Update()
        {
            // Hit flash.
            bool shouldFlash = Time.time < flashUntil;
            if (shape != null && shouldFlash != flashing)
            {
                flashing = shouldFlash;
                if (flashing) shape.SetFill(flashColor, Color.Lerp(baseBottom, flashColor, 0.6f));
                else shape.SetFill(baseTop, baseBottom);
            }

            // The eye follows the circle.
            if (pupil != null && Target != null)
            {
                Vector2 dir = ((Vector2)(Target.position - pupil.parent.position)).normalized;
                pupil.localPosition = new Vector3(dir.x, dir.y, 0f) * pupilTravel;
            }
        }

        private void FixedUpdate()
        {
            // Between moves: stand still (gravity still applies).
            if (IsFighting && CurrentMove == null && IsGrounded) Body.linearVelocity = new Vector2(0f, Body.linearVelocity.y);
        }

        #endregion

        #region Fight control

        /// <summary>Starts the fight (called by the arena once the gates close).</summary>
        public void BeginFight()
        {
            if (IsFighting || Health.IsDead) return;
            IsFighting = true;
            FightStarted?.Invoke();
            loop = StartCoroutine(FightLoop());
        }

        /// <summary>Stops everything and puts the boss back at <paramref name="home"/> at full health.</summary>
        public void ResetFight(Vector2 home)
        {
            StopFight();
            gameObject.SetActive(true);
            Health.ResetHealth();
            Phase = 1;
            roarPending = false;
            Body.gravityScale = baseGravityScale;
            Body.position = home;
            transform.position = home;
            Body.linearVelocity = Vector2.zero;
            ResetPose();
            SetColours(calmTop, calmBottom);
            if (shape != null) shape.SetCornerGlow(-1, 0f, Color.white);
            foreach (BossMove m in moves) m.ResetCooldown();
            FightReset?.Invoke();
        }

        /// <summary>Already standing in the arena: no entrance.</summary>
        public IEnumerator Entrance() { yield break; }

        /// <summary>One health bar along the top of the screen.</summary>
        public void ShowHealthBar() => UI.BossHealthBar.Instance.Show(bossName, Health);

        public void HideHealthBar() => UI.BossHealthBar.Instance.Hide();

        /// <summary>Back to normal gravity, standing upright and still (e.g. after a move was cut short mid-air).</summary>
        public void Settle()
        {
            Body.gravityScale = baseGravityScale;
            Body.linearVelocity = Vector2.zero;
            ResetPose();
            if (shape != null) shape.SetCornerGlow(-1, 0f, Color.white);
        }

        /// <summary>Changes the boss's resting colours.</summary>
        public void SetColours(Color top, Color bottom)
        {
            baseTop = top;
            baseBottom = bottom;
            if (shape != null && !flashing) shape.SetFill(top, bottom);
        }

        private void StopFight()
        {
            IsFighting = false;
            if (loop != null) StopCoroutine(loop);
            if (moveRoutine != null) StopCoroutine(moveRoutine);
            loop = moveRoutine = null;
            if (CurrentMove != null) CurrentMove.Cancel();
            CurrentMove = null;
            Body.gravityScale = baseGravityScale;
            ResetPose();
        }

        private IEnumerator FightLoop()
        {
            yield return new WaitForSeconds(openingDelay);

            while (IsFighting)
            {
                if (roarPending)
                {
                    roarPending = false;
                    yield return Roar();
                }

                BossMove next = PickMove();
                if (next == null)
                {
                    yield return new WaitForSeconds(0.15f); // nothing ready yet
                    continue;
                }

                CurrentMove = next;
                moveRoutine = StartCoroutine(next.Run());
                yield return moveRoutine;
                moveRoutine = null;
                CurrentMove = null;
                ResetPose();
                next.StartCooldown();

                float pause = next.RecoverTime * WindowScale + (Enraged ? phase2IdleBetweenMoves : idleBetweenMoves);
                yield return new WaitForSeconds(pause);
            }
        }

        /// <summary>Entering phase 2: turns crimson and shakes with rage.</summary>
        private IEnumerator Roar()
        {
            SetColours(phase2Top, phase2Bottom);
            if (CameraFollow != null)
            {
                CameraFollow.Shake(0.35f, roarTime);
                CameraFollow.Punch(0.1f, 0.5f);
            }
            Vector2 centre = shape.transform.position;
            Fx.Ring(centre, phase2Top, 7f, 0.6f, 0.3f);
            Fx.Ring(centre, Color.white, 4f, 0.35f, 0.15f);
            Fx.Shards(centre, phase2Top, 24, 10f, 0.22f);
            Systems.Story.CinematicOverlay.Instance.EdgePulse(phase2Bottom, 1f, roarTime + 0.4f);
            Systems.Story.CinematicOverlay.Instance.Flash(phase2Top, 0.12f, 0.35f);
            yield return Tremble(roarTime, 0.14f);
        }

        /// <summary>Weighted random pick among the moves that are ready.</summary>
        private BossMove PickMove()
        {
            float total = 0f;
            foreach (BossMove m in moves) if (m.enabled && m.IsReady(Phase)) total += m.Weight;
            if (total <= 0f) return null;

            float roll = UnityEngine.Random.value * total;
            foreach (BossMove m in moves)
            {
                if (!m.enabled || !m.IsReady(Phase)) continue;
                roll -= m.Weight;
                if (roll <= 0f) return m;
            }
            return null;
        }

        #endregion

        #region Shared motions (used by moves)

        /// <summary>World position of a corner of the (posed) shape: 0 top, 1 bottom-left, 2 bottom-right at rest.</summary>
        public Vector2 Corner(int index) => shape.transform.TransformPoint(shape.GetCornerPoint(index));

        /// <summary>Moves and turns the shape (and the eye) away from its rest pose. Offset in world units, angle in degrees.</summary>
        public void SetPose(Vector2 offset, float angle)
        {
            if (shape != null)
            {
                shape.transform.localPosition = shapeRest + (Vector3)offset;
                shape.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            }
            if (eye != null) eye.localPosition = eyeRest + (Vector3)offset;
        }

        /// <summary>Back to standing upright.</summary>
        public void ResetPose() => SetPose(Vector2.zero, 0f);

        /// <summary>
        /// Tips the shape over its bottom corner on the <paramref name="dir"/> side by <paramref name="degrees"/>
        /// (0 upright, 120 = landed on the next face). Negative <paramref name="dir"/> tips left.
        /// </summary>
        public void SetPivotPose(int dir, float degrees)
        {
            Vector2 centre = PivotCentre(dir, degrees, out float angle);
            SetPose(centre, angle);
        }

        /// <summary>Where the centre ends up (relative to its rest position) when tipped over the front corner.</summary>
        private Vector2 PivotCentre(int dir, float degrees, out float angle)
        {
            // Pivot: the front bottom corner, relative to the centre at rest.
            Vector2 pivot = new Vector2(dir * SideLength * 0.5f, -Inradius);
            angle = -dir * degrees; // tipping right turns clockwise
            return pivot + Rotate(-pivot, angle);
        }

        /// <summary>True if a tumble step in <paramref name="dir"/> stays inside the arena.</summary>
        public bool CanStepTo(int dir)
        {
            float x = Body.position.x + dir * SideLength;
            return x >= ArenaMinX + WallMargin && x <= ArenaMaxX - WallMargin;
        }

        /// <summary>
        /// One step of a triangle's walk: tips over its front corner and lands on the next face, a side length
        /// further on. If <paramref name="hurts"/>, the falling corner and edge hurt the circle.
        /// </summary>
        public IEnumerator TumbleStep(int dir, float time, bool hurts)
        {
            Vector2 start = Body.position;
            for (float t = 0f; t < time; t += Time.deltaTime)
            {
                float p = t / time;
                float e = p * p * (3f - 2f * p);

                // The body (and its touch-damage collider) slides along under the tipping shape, so the
                // hitbox is always where the triangle is drawn; only the lift and the turn are visual.
                Vector2 centre = PivotCentre(dir, 120f * e, out float angle);
                MoveBody(new Vector2(start.x + centre.x, start.y));
                SetPose(new Vector2(0f, centre.y), angle);

                if (hurts && p > 0.3f) HurtAlongLeadingEdge(dir);
                yield return null;
            }

            // A triangle turned by 120° looks the same, so finish upright one side length further on.
            ResetPose();
            MoveBody(start + new Vector2(dir * SideLength, 0f));

            // Thud: dust where the corner came down.
            Fx.Puff(new Vector2(Body.position.x + dir * SideLength * 0.5f, FloorY), 0.9f);
        }

        /// <summary>Puts the body (and the object) at <paramref name="position"/>, standing still.</summary>
        public void MoveBody(Vector2 position)
        {
            Body.position = position;
            transform.position = position;
            Body.linearVelocity = Vector2.zero;
        }

        /// <summary>Jumps on a ballistic arc to <paramref name="x"/> (kept inside the arena) and waits until it lands.</summary>
        /// <param name="riseGravity">Gravity multiplier on the way up (higher = snappier rise).</param>
        /// <param name="fallGravity">Gravity multiplier on the way down (higher = it slams down hard).</param>
        public IEnumerator LeapTo(float x, float height, float riseGravity = 1f, float fallGravity = 1f)
        {
            x = Mathf.Clamp(x, ArenaMinX + WallMargin, ArenaMaxX - WallMargin);
            float gUp = Gravity * riseGravity;
            float gDown = Gravity * fallGravity;
            float vy = Mathf.Sqrt(2f * gUp * height);
            float airTime = vy / gUp + Mathf.Sqrt(2f * height / gDown);
            float vx = (x - Body.position.x) / airTime;
            Body.gravityScale = baseGravityScale * riseGravity;
            Body.linearVelocity = new Vector2(vx, vy);

            float start = Time.time;
            while (Time.time - start < 0.1f || !IsGrounded)
            {
                Body.linearVelocity = new Vector2(vx, Body.linearVelocity.y);
                if (Body.linearVelocity.y <= 0f) Body.gravityScale = baseGravityScale * fallGravity;
                if (Time.time - start > airTime + 1.5f) break; // safety
                yield return null;
            }
            Body.gravityScale = baseGravityScale;
            Body.linearVelocity = Vector2.zero;
            Fx.Dust(new Vector2(Body.position.x, FloorY), SideLength * 0.6f, 0.6f); // a heavy landing
            Sfx.Play(SoundId.BossLand, Body.position, 0.6f, 1.1f);
        }

        /// <summary>
        /// Flies along an exact arc so its centre ends at <paramref name="centre"/> (e.g. standing on top of a ledge
        /// it would otherwise pass through), peaking <paramref name="apexAbove"/> above the higher end. Gravity is
        /// off afterwards, so it stays put until the next leap (<see cref="LeapTo"/> restores it).
        /// </summary>
        public IEnumerator ArcTo(Vector2 centre, float apexAbove, float riseGravity = 1.6f, float fallGravity = 3.5f)
        {
            Vector2 start = Body.position;
            float apexY = Mathf.Min(Mathf.Max(start.y, centre.y) + apexAbove, ArenaCeilingY - Circumradius - 0.2f);
            apexY = Mathf.Max(apexY, Mathf.Max(start.y, centre.y));
            float gUp = Gravity * riseGravity, gDown = Gravity * fallGravity;
            float vy = Mathf.Sqrt(2f * gUp * (apexY - start.y));
            float tUp = vy / gUp;
            float tDown = Mathf.Sqrt(2f * (apexY - centre.y) / gDown);
            float total = Mathf.Max(0.05f, tUp + tDown);

            Body.gravityScale = 0f;
            for (float t = 0f; t < total; t += Time.deltaTime)
            {
                float x = Mathf.Lerp(start.x, centre.x, t / total);
                float y = t < tUp ? start.y + vy * t - 0.5f * gUp * t * t : apexY - 0.5f * gDown * (t - tUp) * (t - tUp);
                MoveBody(new Vector2(x, y));
                yield return null;
            }
            MoveBody(centre);
        }

        /// <summary>Shakes in place for <paramref name="time"/> seconds.</summary>
        public IEnumerator Tremble(float time, float amount)
        {
            for (float t = 0f; t < time; t += Time.deltaTime)
            {
                SetPose(UnityEngine.Random.insideUnitCircle * amount, UnityEngine.Random.Range(-2f, 2f));
                yield return null;
            }
            ResetPose();
        }

        /// <summary>Hurts the circle if it is within <paramref name="radius"/> of <paramref name="point"/>.</summary>
        public void HurtPlayerAt(Vector2 point, float radius, int damage = 1)
        {
            if (TargetHealth == null) return;
            if (Physics2D.OverlapCircle(point, radius, playerLayers) != null) TargetHealth.TakeDamage(damage, Body.position);
        }

        /// <summary>Hurts the circle if it is under the top corner or the edge leading down to the front corner.</summary>
        public void HurtAlongLeadingEdge(int dir)
        {
            // Small radii so the danger matches the drawn shape (the rounded corner tip and the edge itself):
            // a jump that clears the triangle on screen clears it in the game.
            Vector2 top = Corner(0);
            Vector2 front = Corner(dir > 0 ? 2 : 1);
            Vector2 inward = ((Vector2)shape.transform.position - top).normalized * 0.15f; // the tip is rounded
            HurtPlayerAt(top + inward, 0.2f);
            HurtPlayerAt(Vector2.Lerp(top, front, 0.35f), 0.2f);
            HurtPlayerAt(Vector2.Lerp(top, front, 0.7f), 0.2f);
        }

        /// <summary>
        /// Where a ray from <paramref name="origin"/> along <paramref name="dir"/> leaves the arena (walls, floor
        /// or ceiling). Ledges inside the arena are ignored.
        /// </summary>
        public Vector2 ClipToArena(Vector2 origin, Vector2 dir)
        {
            float t = float.PositiveInfinity;
            if (dir.x > 1e-4f) t = Mathf.Min(t, (ArenaMaxX - origin.x) / dir.x);
            if (dir.x < -1e-4f) t = Mathf.Min(t, (ArenaMinX - origin.x) / dir.x);
            if (dir.y > 1e-4f) t = Mathf.Min(t, (ArenaCeilingY - origin.y) / dir.y);
            if (dir.y < -1e-4f) t = Mathf.Min(t, (ArenaFloorY - origin.y) / dir.y);
            return origin + dir * Mathf.Max(0f, t);
        }

        private void OnDestroy()
        {
            if (TargetHealth != null) TargetHealth.Hit -= HandleTargetHit;
        }

        /// <summary>
        /// The circle was hit. If it is inside the boss, put it just outside (on the side it was nearer, or the
        /// other side if a wall is in the way) so its knockback carries it clear, instead of leaving it inside to
        /// be hit again the moment its invulnerability ends.
        /// </summary>
        private void HandleTargetHit(int damage, Vector2 source)
        {
            if (!isActiveAndEnabled || hurtbox == null || targetCollider == null || targetBody == null) return;
            if (!hurtbox.Distance(targetCollider).isOverlapped) return;

            Bounds b = hurtbox.bounds;
            float r = TargetMovement != null ? TargetMovement.Radius : 0.5f;
            Vector2 p = targetBody.position;
            int preferred = p.x >= b.center.x ? 1 : -1;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                int side = attempt == 0 ? preferred : -preferred;
                float x = side > 0 ? b.max.x + r + 0.15f : b.min.x - r - 0.15f;
                if (x - r < ArenaMinX || x + r > ArenaMaxX) continue;
                var spot = new Vector2(x, Mathf.Max(p.y, ArenaFloorY + r + 0.02f));
                if (Physics2D.OverlapCircle(spot, r * 0.9f, groundLayers) != null) continue;

                targetBody.position = spot;
                Target.position = spot;
                // Make sure the knockback points away from the boss.
                Vector2 v = targetBody.linearVelocity;
                if (Mathf.Sign(v.x) != side) targetBody.linearVelocity = new Vector2(Mathf.Abs(v.x) * side, v.y);
                return;
            }
        }

        private bool IsPassThrough(Collider2D c)
        {
            if (passThrough == null) return false;
            foreach (Collider2D p in passThrough) if (p == c) return true;
            return false;
        }

        private static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        #endregion

        #region Hits & death

        private void HandleHit(int damage, Vector2 source)
        {
            flashUntil = Time.time + flashTime;
            if (Health.IsDead) return;

            // Next phase once health drops past the next threshold.
            float fraction = Health.CurrentHealth / (float)Health.MaxHealth;
            while (Phase - 1 < phaseThresholds.Length && fraction <= phaseThresholds[Phase - 1])
            {
                Phase++;
                roarPending = true;
                PhaseChanged?.Invoke(Phase);
            }
        }

        private void HandleDied()
        {
            StopFight();

            // A big ending: slow motion, a white flash, a ring and a storm of shards.
            HitStop.Freeze(0.9f, 0.25f);
            if (CameraFollow != null)
            {
                CameraFollow.Shake(0.5f, 0.8f);
                CameraFollow.Punch(0.12f, 0.9f);
            }
            Systems.Story.CinematicOverlay.Instance.Flash(Color.white, 0.45f, 0.7f);
            if (shape != null)
            {
                Vector2 centre = shape.transform.position;
                Fx.Ring(centre, Color.white, 9f, 0.8f, 0.35f);
                Fx.Ring(centre, baseTop, 5f, 0.6f, 0.25f);
                Fx.Shards(centre, baseTop, 40, 14f, 0.3f);
                Fx.Shards(centre, Color.white, 20, 9f, 0.15f);
                Fx.Dust(new Vector2(centre.x, ArenaFloorY), SideLength, 1.6f);
                Sfx.Play(SoundId.BossDefeat, centre);
            }
            if (shape != null)
            {
                Vector2 source = Target != null ? (Vector2)Target.position : Body.position + Vector2.down;
                TriangleShatter.Spawn(shape.transform.position, Circumradius, baseTop, baseBottom, new Color(0.16f, 0.08f, 0.1f, 1f), shatterGlow, ShapeMaterial, source);
            }
            Defeated?.Invoke();
            gameObject.SetActive(false);
        }

        #endregion
    }
}
