using System;
using System.Collections;
using System.Collections.Generic;
using ProjectCircle.Systems;
using ProjectCircle.Systems.Story;
using ProjectCircle.UI;
using ProjectCircle.Systems.Audio;
using UnityEngine;

namespace ProjectCircle.Enemies.Boss
{
    /// <summary>
    /// BOSS 2, the Twin Isosceles: two triangles, Iso and Skelos ("equal" and "leg"), the two equal sides of an
    /// isosceles. The middle ranks' enforcers of symmetry and measure, so nearly everything they do is mirrored.
    ///
    /// Each twin is a <see cref="BossBrain"/> used only as a body (its motions, health, flash and shatter); this
    /// component runs their moves together, so it owns the fight. They wait out of sight above the arena; when the
    /// circle walks in they drop in on either side, and the arena locks once both have landed. Phase 1 (both alive):
    /// - <b>Pincer Charge</b>: both rush in from opposite walls and cross, fast (later one bounces high).
    /// - <b>Mirror Slam</b>: both leap and slam at mirrored points; the shockwaves run the length of the arena.
    /// - <b>Crosshair</b>: one rises up a wall firing a horizontal laser, the other rises to the ceiling firing a
    ///   vertical one; both chase the circle so the two beams cross on it, then lock and fire (move off both lines).
    /// - <b>Plunge</b> (the Mantis Lords' dive): one leaps up over the circle tip-down and hangs still for a beat, then
    ///   plunges tip-first; the moment it lands the other swoops over the circle and does the same. All in view.
    /// - <b>The Compass</b>: one rises as the pivot, the other swings round it like a compass, scraping the floor,
    ///   while the swinging one flings orbs off its tip along the swing and outward (parry them back into it).
    /// - <b>Rebalance</b> (once): if one is hurt far more than the other, they join vertices and the healthier pours
    ///   life into the weaker (motes run along the link) until they are even. Links between them are vertex power.
    /// Phase 2 (one falls): the survivor loses what made it what it was, warps uneven (scalene), turns crimson,
    /// comes back to full health and fights alone and faster: wall-bouncing charges, slams, dives and a sweeping beam.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TwinIsosceles : MonoBehaviour, IArenaBoss
    {
        [Header("The Twins")]
        [SerializeField] private string bossName = "The Twin Isosceles";
        [Tooltip("The twin that starts on the left (Iso).")]
        [SerializeField] private BossBrain left;
        [Tooltip("The twin that starts on the right (Skelos).")]
        [SerializeField] private BossBrain right;

        [Tooltip("Middle of the arena (its x is the mirror line).")]
        [SerializeField] private Transform arenaCentre;

        [Header("Shape")]
        [Tooltip("Width of the isosceles shape (1 = equilateral, smaller = taller and narrower).")]
        [SerializeField, Range(0.4f, 1f)] private float isoscelesWidth = 0.72f;
        [Tooltip("The survivor's warped shape in phase 2: width and lean of the apex.")]
        [SerializeField] private float warpedWidth = 1.25f;
        [SerializeField] private float warpedLean = 0.45f;

        [Header("Pacing")]
        [SerializeField, Min(0f)] private float openingDelay = 1f;
        [SerializeField, Min(0f)] private float betweenMoves = 0.7f;
        [Tooltip("Pauses are multiplied by this once only one twin is left.")]
        [SerializeField, Range(0.2f, 1f)] private float phase2PauseScale = 0.5f;

        [Header("Pincer Charge")]
        [SerializeField, Min(1f)] private float chargeSpeed = 32f;
        [SerializeField, Min(0.1f)] private float chargeTell = 0.6f;

        [Header("Mirror Slam")]
        [SerializeField, Min(1f)] private float slamHeight = 7f;
        [Tooltip("How far the slam shockwaves travel (the arena is about 31 wide).")]
        [SerializeField, Min(1f)] private float shockwaveDistance = 32f;

        [Header("Crosshair")]
        [Tooltip("How fast both shooters chase the circle (units per second).")]
        [SerializeField, Min(1f)] private float crossChaseSpeed = 10f;
        [SerializeField, Min(1)] private int crossShots = 3;
        [Tooltip("Chasing time before the first shot (later shots chase for 60% of it).")]
        [SerializeField, Min(0.1f)] private float crossTrackTime = 0.9f;
        [Tooltip("The beams stop and flash this long before firing: the time to get off both lines.")]
        [SerializeField, Min(0.05f)] private float crossLockTime = 0.35f;
        [SerializeField, Min(0.05f)] private float crossFireTime = 0.3f;

        [Header("Plunge (the Mantis Lords' dive)")]
        [Tooltip("Height above the floor where a diver appears and hangs (centre of the shape).")]
        [SerializeField, Min(3f)] private float plungeHeight = 10f;
        [SerializeField, Min(1f)] private float plungeSpeed = 78f;
        [Tooltip("How long it hangs still over the circle before plunging (the second diver hangs a little less).")]
        [SerializeField, Min(0.1f)] private float plungeHang = 0.3f;
        [Tooltip("How long it stays stuck in the floor after landing: the window to hit it.")]
        [SerializeField, Min(0f)] private float plungeStuck = 0.28f;

        [Header("Entrance")]
        [Tooltip("How high above the floor they wait, out of sight, until the circle walks in.")]
        [SerializeField, Min(5f)] private float dropHeight = 20f;
        [Tooltip("How far from the middle each one lands.")]
        [SerializeField, Min(1f)] private float dropSpread = 10f;

        [Header("The Compass")]
        [SerializeField, Min(2f)] private float compassRadius = 6.2f;
        [SerializeField, Min(1f)] private float compassPivotHeight = 6.5f;
        [SerializeField, Min(0.25f)] private float compassTurns = 1.25f;
        [SerializeField, Min(0.5f)] private float compassTime = 3.2f;
        [Tooltip("The swinging twin flings an orb off its tip this often while it is in the air (parry it back into it).")]
        [SerializeField, Min(0.1f)] private float compassShotInterval = 0.3f;
        [Tooltip("Base speed of a flung orb (it goes faster the faster the swing).")]
        [SerializeField, Min(1f)] private float compassOrbSpeed = 10f;
        [Tooltip("How much a flung orb is pushed outward from the turn, on top of following the swing.")]
        [SerializeField, Min(0f)] private float compassOrbOutward = 0.6f;
        [Tooltip("How hard a flung orb bends towards the circle (degrees per second, fading out over its first 0.7 s).")]
        [SerializeField, Min(0f)] private float compassOrbTracking = 90f;

        [Header("Rebalance")]
        [Tooltip("Health difference (as a fraction of max) that makes them rebalance. Once per fight.")]
        [SerializeField, Range(0.05f, 1f)] private float rebalanceGap = 0.3f;

        [Header("Look")]
        [SerializeField] private Color glowColor = new Color(1f, 0.86f, 0.5f, 1f);
        [SerializeField] private Color lineColor = new Color(1f, 0.85f, 0.6f, 1f);
        [SerializeField] private Color orbColor = new Color(0.75f, 0.9f, 1f, 1f);
        [Tooltip("Colour of the life they pour into each other when they rebalance.")]
        [SerializeField] private Color healthColor = new Color(1f, 0.45f, 0.5f, 1f);
        [SerializeField] private Color warpedTop = new Color(0.78f, 0.32f, 0.38f, 1f);
        [SerializeField] private Color warpedBottom = new Color(0.46f, 0.12f, 0.2f, 1f);

        [TextArea] [SerializeField] private string lastWords = "\"...we were one shape, once.\"";

        public string BossName => bossName;
        public event Action Defeated;

        private readonly List<GameObject> temporary = new List<GameObject>();
        private readonly Dictionary<BossBrain, Vector2[]> hurtboxRest = new Dictionary<BossBrain, Vector2[]>();
        private Vector2 leftHome, rightHome;
        private Coroutine loop;
        private bool fighting;
        private bool rebalanced;
        private BossBrain survivor;

        private float CentreX => arenaCentre != null ? arenaCentre.position.x : (left.Body.position.x + right.Body.position.x) * 0.5f;
        private float Floor => left.ArenaFloorY;
        private float StandY => Floor + left.Inradius;
        private Vector2 PlayerPos => left.Target != null ? (Vector2)left.Target.position : new Vector2(CentreX, StandY);
        private float Pause(float seconds) => survivor != null ? seconds * phase2PauseScale : seconds;

        #region Setup

        private void Awake()
        {
            leftHome = left.transform.position;
            rightHome = right.transform.position;
            foreach (BossBrain twin in new[] { left, right })
            {
                Transform hurt = twin.Shape.transform.Find("Hurtbox");
                var poly = hurt != null ? hurt.GetComponent<PolygonCollider2D>() : null;
                if (poly != null) hurtboxRest[twin] = poly.GetPath(0);
                SetShape(twin, isoscelesWidth, 0f);
            }
        }

        private void Start()
        {
            // The two bodies pass through each other (they cross in the Pincer Charge and the Compass).
            var a = left.GetComponents<Collider2D>();
            var b = right.GetComponents<Collider2D>();
            foreach (Collider2D ca in a) foreach (Collider2D cb in b) Physics2D.IgnoreCollision(ca, cb, true);

            left.Defeated += () => HandleTwinFell(left);
            right.Defeated += () => HandleTwinFell(right);

            // Out of sight until the fight: once every Start has run (each twin has measured the floor it stands
            // on), they wait high above the arena.
            StartCoroutine(ParkNextFrame());
        }

        private IEnumerator ParkNextFrame()
        {
            yield return null;
            Park();
        }

        /// <summary>Hangs both twins high above their landing spots, out of sight, still.</summary>
        private void Park()
        {
            foreach (BossBrain twin in new[] { left, right })
            {
                if (twin == null || !twin.isActiveAndEnabled) continue;
                twin.Body.gravityScale = 0f;
                twin.MoveBody(new Vector2(LandingX(twin), Floor + dropHeight));
            }
        }

        private float LandingX(BossBrain twin) => CentreX + (twin == left ? -dropSpread : dropSpread);

        /// <summary>Reshapes a twin's drawn triangle and its hurtbox (width &lt; 1 = isosceles, lean = scalene).</summary>
        private void SetShape(BossBrain twin, float width, float lean)
        {
            twin.Shape.SetShapeMap(width, lean);
            if (!hurtboxRest.TryGetValue(twin, out Vector2[] rest)) return;
            var poly = twin.Shape.transform.Find("Hurtbox").GetComponent<PolygonCollider2D>();
            var mapped = new Vector2[rest.Length];
            for (int i = 0; i < rest.Length; i++) mapped[i] = twin.Shape.MapPoint(rest[i]);
            poly.SetPath(0, mapped);
        }

        #endregion

        #region IArenaBoss

        public void BeginFight()
        {
            if (fighting) return;
            fighting = true;
            loop = StartCoroutine(Drive(PhaseOne()));
        }

        public void ResetFight(Vector2 home)
        {
            StopLoop();
            fighting = false;
            rebalanced = false;
            survivor = null;
            left.ResetFight(leftHome);
            right.ResetFight(rightHome);
            SetShape(left, isoscelesWidth, 0f);
            SetShape(right, isoscelesWidth, 0f);
            Park(); // back up out of sight, to drop in again
        }

        /// <summary>They drop in from either side, one a beat after the other; the arena locks once both land.</summary>
        public IEnumerator Entrance()
        {
            var stepper = new Stepper(Both(Drop(left, 0f), Drop(right, 0.25f)));
            while (stepper.Step()) yield return null;
        }

        private IEnumerator Drop(BossBrain twin, float delay)
        {
            yield return Wait(delay);
            twin.ResetPose(); // upright again (it may have come from a tip-down dive), still out of sight
            twin.Shape.SetCornerGlow(-1, 0f, glowColor);
            float x = LandingX(twin);
            float y = Floor + dropHeight;
            float speed = 0f;
            twin.Body.gravityScale = 0f;
            while (y > StandY)
            {
                speed += 70f * Time.deltaTime;
                y = Mathf.Max(StandY, y - speed * Time.deltaTime);
                twin.MoveBody(new Vector2(x, y));
                yield return null;
            }
            twin.Settle();
            Fx.Dust(new Vector2(x, Floor), twin.SideLength * 1.2f, 1.4f);
            Fx.Ring(new Vector2(x, Floor + 0.3f), glowColor, 3f, 0.35f, 0.12f);
            Sfx.Play(SoundId.BossLand, new Vector2(x, Floor));
            if (twin.CameraFollow != null) twin.CameraFollow.Shake(0.25f, 0.3f);
        }

        public void ShowHealthBar()
        {
            BossHealthBar.Left.Show(left.BossName, left.Health);
            BossHealthBar.Right.Show(right.BossName, right.Health);
        }

        public void HideHealthBar()
        {
            BossHealthBar.Left.Hide();
            BossHealthBar.Right.Hide();
        }

        #endregion

        #region Flow

        private static bool Alive(BossBrain b) => b != null && b.isActiveAndEnabled && !b.Health.IsDead;

        private void StopLoop()
        {
            if (loop != null) StopCoroutine(loop);
            loop = null;
            foreach (GameObject go in temporary) if (go != null) Destroy(go);
            temporary.Clear();
            if (Alive(left)) { left.Settle(); Harmless(left, false); }
            if (Alive(right)) { right.Settle(); Harmless(right, false); }
        }

        private void HandleTwinFell(BossBrain fallen)
        {
            if (!fighting) return;
            BossBrain other = fallen == left ? right : left;
            StopLoop();
            (fallen == left ? BossHealthBar.Left : BossHealthBar.Right).Hide();

            if (Alive(other))
            {
                survivor = other;
                loop = StartCoroutine(Drive(PhaseTwo(other)));
                return;
            }

            // Both gone.
            fighting = false;
            if (!string.IsNullOrEmpty(lastWords)) CinematicOverlay.Instance.ShowHint(lastWords, 4f);
            Defeated?.Invoke();
        }

        private IEnumerator PhaseOne()
        {
            yield return Wait(openingDelay);
            var deck = new List<Func<IEnumerator>> { PincerCharge, MirrorSlam, Crosshair, Plunge };
            while (fighting)
            {
                // Every move once in a shuffled order, then the Compass as the round's big finish.
                Shuffle(deck);
                foreach (Func<IEnumerator> move in deck)
                {
                    yield return move();
                    yield return Wait(Pause(betweenMoves));
                    if (ShouldRebalance()) yield return Rebalance();
                }
                yield return Compass();
                yield return Wait(Pause(betweenMoves));
            }
        }

        private IEnumerator PhaseTwo(BossBrain s)
        {
            // Without its equal, its shape comes apart: it warps uneven and turns crimson.
            s.Settle();
            CinematicOverlay.Instance.EdgePulse(warpedBottom, 0.8f, 1.6f);
            Sfx.Play(SoundId.PhaseWarp);
            if (s.CameraFollow != null) s.CameraFollow.Shake(0.3f, 1.2f);
            float lean = s == left ? warpedLean : -warpedLean;
            for (float t = 0f; t < 1.4f; t += Time.deltaTime)
            {
                float e = Mathf.SmoothStep(0f, 1f, t / 1.4f);
                SetShape(s, Mathf.Lerp(isoscelesWidth, warpedWidth, e), lean * e);
                s.SetPose(UnityEngine.Random.insideUnitCircle * 0.12f, UnityEngine.Random.Range(-3f, 3f));
                yield return null;
            }
            SetShape(s, warpedWidth, lean);
            s.ResetPose();
            s.SetColours(warpedTop, warpedBottom);
            s.Health.ResetHealth(); // alone, it fights on at full strength
            Fx.Ring(s.Shape.transform.position, warpedTop, 7f, 0.6f, 0.3f);
            Fx.Shards(s.Shape.transform.position, warpedTop, 24, 10f, 0.22f);
            yield return Wait(0.6f);

            var moves = new List<Func<BossBrain, IEnumerator>> { SoloCharge, SoloSlam, SoloLine, SoloPlunge };
            int last = -1;
            while (fighting && Alive(s))
            {
                int pick;
                do pick = UnityEngine.Random.Range(0, moves.Count); while (pick == last && moves.Count > 1);
                last = pick;
                yield return moves[pick](s);
                yield return Wait(Pause(betweenMoves));
            }
        }

        #endregion

        #region Phase 1 moves

        private IEnumerator PincerCharge()
        {
            yield return Both(left.LeapTo(left.ArenaMinX, 3f), right.LeapTo(right.ArenaMaxX, 3f));
            yield return Both(AimLance(left, 1, chargeTell), AimLance(right, -1, chargeTell));
            // Later on, one of them bounces high while the other runs low: read which gap to take.
            bool bounce = left.Health.CurrentHealth + right.Health.CurrentHealth < (left.Health.MaxHealth + right.Health.MaxHealth) * 0.7f;
            bool leftBounces = UnityEngine.Random.value < 0.5f;
            yield return Both(Charge(left, 1, chargeSpeed, bounce && leftBounces), Charge(right, -1, chargeSpeed, bounce && !leftBounces));
        }

        private IEnumerator MirrorSlam()
        {
            for (int round = 0; round < 2; round++)
            {
                float margin = left.WallMargin;
                float px = Mathf.Clamp(PlayerPos.x, left.ArenaMinX + margin, left.ArenaMaxX - margin);
                float mx = 2f * CentreX - px;
                if (Mathf.Abs(px - mx) < 5f) { px = CentreX - 3.5f; mx = CentreX + 3.5f; }
                float xa = Mathf.Min(px, mx), xb = Mathf.Max(px, mx);

                yield return Both(Tell(left, 0, round == 0 ? 0.5f : 0.3f), Tell(right, 0, round == 0 ? 0.5f : 0.3f));
                yield return Both(left.LeapTo(xa, slamHeight, 1.1f, 3.2f), right.LeapTo(xb, slamHeight, 1.1f, 3.2f));
                Slam(left);
                Slam(right);
                yield return Wait(0.45f);
            }
        }

        /// <summary>
        /// One shooter rises up a wall and fires across, the other rises to the ceiling and fires down. Both chase the
        /// circle so their beams cross on it, then stop and flash (get off both lines: jump AND move), then fire.
        /// </summary>
        private IEnumerator Crosshair()
        {
            // The horizontal shooter takes the wall farther from the circle.
            BossBrain across = PlayerPos.x < CentreX ? right : left;
            BossBrain down = across == left ? right : left;
            int side = across == right ? 1 : -1;
            float wallX = side > 0 ? across.ArenaMaxX - across.WallMargin : across.ArenaMinX + across.WallMargin;
            float farWallX = side > 0 ? across.ArenaMinX : across.ArenaMaxX;
            float topY = Floor + plungeHeight;
            float minY = StandY, maxY = Floor + plungeHeight - 1.5f;

            // Up into position: the apexes turn to aim (across: sideways; down: pointing at the floor). They don't
            // hurt on the way (one may fly straight through the circle getting to its wall).
            Harmless(across, true);
            Harmless(down, true);
            across.Body.gravityScale = 0f;
            down.Body.gravityScale = 0f;
            Vector2 a0 = across.Body.position, d0 = down.Body.position;
            for (float t = 0f; t < 0.45f; t += Time.deltaTime)
            {
                float e = Mathf.SmoothStep(0f, 1f, t / 0.45f);
                across.MoveBody(Vector2.Lerp(a0, new Vector2(wallX, Mathf.Clamp(PlayerPos.y, minY, maxY)), e));
                down.MoveBody(Vector2.Lerp(d0, new Vector2(Mathf.Clamp(PlayerPos.x, down.ArenaMinX, down.ArenaMaxX), topY), e));
                across.SetPose(Vector2.zero, 90f * side * e);
                down.SetPose(Vector2.zero, 180f * e);
                yield return null;
            }
            Harmless(across, false);
            Harmless(down, false);

            for (int shot = 0; shot < crossShots; shot++)
            {
                LineRenderer warnA = MakeLine("Crosshair (across)", 0.05f, new Color(lineColor.r, lineColor.g, lineColor.b, 0.45f));
                LineRenderer warnD = MakeLine("Crosshair (down)", 0.05f, new Color(lineColor.r, lineColor.g, lineColor.b, 0.45f));
                float track = shot == 0 ? crossTrackTime : crossTrackTime * 0.6f;

                // Chase: the beams slide to cross on the circle.
                for (float t = 0f; t < track; t += Time.deltaTime)
                {
                    float y = Mathf.MoveTowards(across.Body.position.y, Mathf.Clamp(PlayerPos.y, minY, maxY), crossChaseSpeed * Time.deltaTime);
                    float x = Mathf.MoveTowards(down.Body.position.x, Mathf.Clamp(PlayerPos.x, down.ArenaMinX + 0.5f, down.ArenaMaxX - 0.5f), crossChaseSpeed * Time.deltaTime);
                    across.MoveBody(new Vector2(wallX, y));
                    down.MoveBody(new Vector2(x, topY));
                    across.Shape.SetCornerGlow(0, t / track, glowColor);
                    down.Shape.SetCornerGlow(0, t / track, glowColor);
                    AimCrosshair(across, down, farWallX, warnA, warnD);
                    yield return null;
                }

                // Lock: the lines stop and flash.
                Sfx.Play(SoundId.LaserCharge);
                for (float t = 0f; t < crossLockTime; t += Time.deltaTime)
                {
                    float flash = Mathf.PingPong(t * 16f, 1f);
                    Color c = Color.Lerp(new Color(lineColor.r, lineColor.g, lineColor.b, 0.4f), Color.white, flash);
                    warnA.startColor = warnA.endColor = warnD.startColor = warnD.endColor = c;
                    warnA.startWidth = warnA.endWidth = warnD.startWidth = warnD.endWidth = 0.08f;
                    AimCrosshair(across, down, farWallX, warnA, warnD);
                    yield return null;
                }
                Destroy(warnA.gameObject);
                Destroy(warnD.gameObject);

                // Fire.
                LineRenderer beamA = MakeLine("Crosshair Beam (across)", 0.4f, lineColor);
                LineRenderer beamD = MakeLine("Crosshair Beam (down)", 0.4f, lineColor);
                if (across.CameraFollow != null) across.CameraFollow.Shake(0.12f, 0.2f);
                Sfx.Play(SoundId.LaserFire);
                for (float t = 0f; t < crossFireTime; t += Time.deltaTime)
                {
                    AimCrosshair(across, down, farWallX, beamA, beamD);
                    HurtAlongSegment(across, beamA.GetPosition(0), beamA.GetPosition(1));
                    HurtAlongSegment(down, beamD.GetPosition(0), beamD.GetPosition(1));
                    yield return null;
                }
                Destroy(beamA.gameObject);
                Destroy(beamD.gameObject);
                across.Shape.SetCornerGlow(-1, 0f, glowColor);
                down.Shape.SetCornerGlow(-1, 0f, glowColor);
            }

            across.ResetPose();
            down.ResetPose();
            Harmless(across, true);
            Harmless(down, true);
            yield return Both(across.LeapTo(across.Body.position.x - side * 2f, 0.5f), down.LeapTo(down.Body.position.x, 0.5f));
            Harmless(across, false);
            Harmless(down, false);
            left.Settle();
            right.Settle();
        }

        private void AimCrosshair(BossBrain across, BossBrain down, float farWallX, LineRenderer a, LineRenderer d)
        {
            Vector2 tipA = across.Corner(0);
            Vector2 tipD = down.Corner(0);
            SetLine(a, tipA, new Vector2(farWallX, tipA.y));
            SetLine(d, tipD, new Vector2(tipD.x, Floor));
        }

        /// <summary>
        /// The Mantis Lords' dive, as a pair, all inside the arena: one leaps up over the circle (turning tip-down) while
        /// the other leaps up to wait high on its side; the first hangs still for a beat (the tell) and plunges tip-first;
        /// the moment it lands the other swoops over the circle and does the same. Then both flip back upright.
        /// </summary>
        private IEnumerator Plunge()
        {
            BossBrain first = UnityEngine.Random.value < 0.5f ? left : right;
            BossBrain second = first == left ? right : left;
            // The second waits high on the far side from the first, so they never overlap up there.
            float firstX = PlayerX(first);
            float waitX = firstX < CentreX ? CentreX + 11f : CentreX - 11f;
            yield return Both(RiseTo(first, firstX, 0.28f), RiseTo(second, waitX, 0.28f));
            yield return Dive(first, plungeHang);
            yield return RiseTo(second, PlayerX(second), 0.16f);
            yield return Dive(second, plungeHang * 0.65f);
            yield return Both(Recover(first), Recover(second));
        }

        private float PlayerX(BossBrain b) => Mathf.Clamp(PlayerPos.x, b.ArenaMinX + b.WallMargin, b.ArenaMaxX - b.WallMargin);

        /// <summary>
        /// Leaps (or glides, if already up) to hang tip-down at <paramref name="x"/>, high in the arena, turning over on
        /// the way; then it is perfectly still.
        /// </summary>
        private IEnumerator RiseTo(BossBrain b, float x, float time)
        {
            b.Body.gravityScale = 0f;
            Vector2 from = b.Body.position;
            Vector2 to = new Vector2(x, Floor + plungeHeight);
            bool fromFloor = from.y < to.y - 1f;
            if (fromFloor) Fx.Dust(new Vector2(from.x, Floor), b.SideLength * 0.6f, 0.7f);
            float fromAngle = fromFloor ? 0f : 180f;
            for (float t = 0f; t < time; t += Time.deltaTime)
            {
                float p = t / time;
                float e = 1f - (1f - p) * (1f - p); // fast out, easing in to the hang
                Vector2 pos = Vector2.Lerp(from, to, e);
                if (fromFloor) pos.y += Mathf.Sin(p * Mathf.PI) * 1.2f; // a little arc over the top
                b.MoveBody(pos);
                b.SetPose(Vector2.zero, Mathf.Lerp(fromAngle, 180f, e));
                yield return null;
            }
            b.MoveBody(to);
            b.SetPose(Vector2.zero, 180f);
        }

        /// <summary>
        /// From its hang: holds perfectly still with its tip glowing for <paramref name="hang"/> seconds, then plunges
        /// straight down, tip-first into the floor, and sticks there for a moment (the opening to hit it).
        /// </summary>
        private IEnumerator Dive(BossBrain b, float hang)
        {
            float x = b.Body.position.x;
            // Tip-down, its tip is a full circumradius below its centre: land with the tip just in the floor.
            float landY = Floor + b.Circumradius - 0.3f;

            for (float t = 0f; t < hang; t += Time.deltaTime)
            {
                b.Shape.SetCornerGlow(0, t / hang, glowColor);
                b.SetPose(UnityEngine.Random.insideUnitCircle * 0.03f, 180f);
                yield return null;
            }
            b.SetPose(Vector2.zero, 180f);

            float y = b.Body.position.y;
            while (y > landY)
            {
                y = Mathf.Max(landY, y - plungeSpeed * Time.deltaTime);
                b.MoveBody(new Vector2(x, y));
                b.HurtPlayerAt(b.Corner(0), 0.4f);
                b.HurtPlayerAt(b.Shape.transform.position, b.Inradius);
                yield return null;
            }
            b.HurtPlayerAt(new Vector2(x, Floor + 0.4f), b.SideLength * 0.35f);
            Fx.Dust(new Vector2(x, Floor), b.SideLength, 1.1f);
            Fx.Crack(new Vector2(x, Floor), b.SideLength * 0.6f, glowColor, 0.5f);
            Sfx.Play(SoundId.Slam, new Vector2(x, Floor), 0.9f, 1.15f);
            if (b.CameraFollow != null) b.CameraFollow.Shake(0.2f, 0.2f);
            b.Shape.SetCornerGlow(-1, 0f, glowColor);

            yield return Wait(plungeStuck);
        }

        /// <summary>Pulls its tip out of the floor with a small hop, flipping back upright, and lands standing.</summary>
        private IEnumerator Recover(BossBrain b)
        {
            Vector2 from = b.Body.position;
            const float time = 0.4f;
            for (float t = 0f; t < time; t += Time.deltaTime)
            {
                float p = t / time;
                float y = Mathf.Lerp(from.y, StandY, p) + Mathf.Sin(p * Mathf.PI) * 1.6f;
                b.MoveBody(new Vector2(from.x, y));
                b.SetPose(Vector2.zero, Mathf.Lerp(180f, 360f, Mathf.SmoothStep(0f, 1f, p)));
                yield return null;
            }
            b.MoveBody(new Vector2(from.x, StandY));
            b.Settle();
            Fx.Dust(new Vector2(from.x, Floor), b.SideLength * 0.5f, 0.5f);
        }

        private IEnumerator Compass()
        {
            BossBrain pivot = UnityEngine.Random.value < 0.5f ? left : right;
            BossBrain arm = pivot == left ? right : left;
            bool armStartsLeft = UnityEngine.Random.value < 0.5f;
            float cx = CentreX;
            float pivotY = Floor + compassPivotHeight;
            Vector2 centre = new Vector2(cx, pivotY);
            // Start where the swing's circle meets the floor, so the swinging twin is already standing at the
            // first point of its path (no jump when the spin begins).
            float onFloor0 = Mathf.Asin(Mathf.Clamp((StandY - pivotY) / compassRadius, -1f, 1f)) * Mathf.Rad2Deg;
            float startAngle = armStartsLeft ? 180f - onFloor0 : 360f + onFloor0;
            Vector2 armStart = centre + Direction(startAngle) * compassRadius;
            armStart.y = Mathf.Max(armStart.y, StandY);

            yield return Both(pivot.LeapTo(cx, 3f), arm.LeapTo(armStart.x, 3f));
            yield return pivot.ArcTo(centre, 1f);

            LineRenderer link = MakeVertexLink("Compass Link");
            Sfx.Play(SoundId.VertexLink, centre);
            LineRenderer trail = MakeLine("Compass Trail", 0.18f, new Color(lineColor.r, lineColor.g, lineColor.b, 0.85f));
            trail.positionCount = 0;
            var trailPoints = new List<Vector3>();
            for (float t = 0f; t < 0.8f; t += Time.deltaTime)
            {
                LinkVertices(link, pivot, arm, t / 0.8f);
                yield return null;
            }

            float sign = armStartsLeft ? 1f : -1f; // so the swing goes down through the floor first
            if (pivot.CameraFollow != null) pivot.CameraFollow.Shake(0.1f, 0.4f);
            float nextShot = compassShotInterval;
            Vector2 lastPos = arm.Body.position;
            for (float t = 0f; t < compassTime; t += Time.deltaTime)
            {
                float e = Mathf.SmoothStep(0f, 1f, t / compassTime);
                float angle = startAngle + sign * compassTurns * 360f * e;
                Vector2 pos = centre + Direction(angle) * compassRadius;
                bool onFloor = pos.y <= StandY + 0.05f;
                pos.y = Mathf.Max(pos.y, StandY);

                // The swinging twin flings orbs off its tip as it goes round: each one leaves along the swing
                // (tangential) and outward (the pull of the turn), so where it flies depends on where the arm was
                // and how fast it was turning — hard to predict, but always read from the arm's motion.
                if (t >= nextShot && !onFloor && Time.deltaTime > 0f)
                {
                    nextShot += compassShotInterval;
                    Vector2 swing = (pos - lastPos) / Time.deltaTime;
                    Vector2 outward = (pos - centre).normalized;
                    Fling(arm, swing, outward);
                }
                lastPos = pos;

                arm.MoveBody(pos);
                arm.SetPose(Vector2.zero, -sign * angle * 1.5f);
                pivot.SetPose(Vector2.zero, sign * angle * 0.5f);
                LinkVertices(link, pivot, arm, 1f);
                arm.HurtPlayerAt(pos, arm.Inradius + 0.15f);

                // It scrapes an arc of light into the floor.
                if (onFloor && (trailPoints.Count == 0 || Mathf.Abs(trailPoints[trailPoints.Count - 1].x - pos.x) > 0.4f))
                {
                    trailPoints.Add(new Vector3(pos.x, Floor + 0.05f, 0f));
                    trail.positionCount = trailPoints.Count;
                    trail.SetPositions(trailPoints.ToArray());
                    Fx.Puff(new Vector2(pos.x, Floor), 0.5f);
                }
                yield return null;
            }
            Destroy(link.gameObject);
            Destroy(trail.gameObject, 0.8f);
            arm.ResetPose();
            pivot.ResetPose();
            yield return Both(pivot.LeapTo(cx - 2f, 0.5f), arm.LeapTo(arm.Body.position.x, 0.5f));
            left.Settle();
            right.Settle();
        }

        /// <summary>
        /// Flings an orb off the swinging twin: its direction is the swing (tangential velocity) plus an outward push
        /// (the turn's pull), its speed follows the swing's speed; then it bends gently towards the circle. Parried, it homes back into the
        /// twin that threw it.
        /// </summary>
        private void Fling(BossBrain from, Vector2 swing, Vector2 outward)
        {
            if (from.Target == null || swing.sqrMagnitude < 0.01f) return;
            Vector2 dir = (swing.normalized + outward * compassOrbOutward).normalized;
            float speed = Mathf.Clamp(swing.magnitude * 0.6f, compassOrbSpeed * 0.7f, compassOrbSpeed * 1.6f);
            Vector2 origin = from.Shape.transform.position;

            HomingOrb.Settings s = HomingOrb.Settings.Default;
            s.color = orbColor;
            s.damage = 1;
            s.driftSpeed = speed;
            s.driftTime = 0f;
            s.maxSpeed = speed;
            s.acceleration = 100f;
            s.turnRate = compassOrbTracking; // a gentle pull towards the circle: thrown by the swing, nudged at you
            s.homingTime = 0.7f;
            s.fullTurnFraction = 0.4f;     // the pull fades, so a late dodge still shakes it
            s.lifetime = 3f;
            s.playerLayers = from.PlayerLayers;
            s.groundLayers = from.GroundLayers;
            HomingOrb orb = HomingOrb.Spawn(origin + dir * (from.Inradius + 0.3f), s, Fx.Material, from.Target);
            orb.ReturnTo = from.transform;
            orb.Release(dir);
            Fx.Ring(origin + dir * from.Inradius, orbColor, 0.9f, 0.2f, 0.08f);
        }

        private bool ShouldRebalance()
        {
            if (rebalanced || !Alive(left) || !Alive(right)) return false;
            float a = left.Health.CurrentHealth / (float)left.Health.MaxHealth;
            float b = right.Health.CurrentHealth / (float)right.Health.MaxHealth;
            return Mathf.Abs(a - b) >= rebalanceGap;
        }

        /// <summary>They can't bear to be unequal: they meet in the middle and share their health back to even.</summary>
        private IEnumerator Rebalance()
        {
            rebalanced = true;
            yield return Both(left.LeapTo(CentreX - 5.5f, 4f), right.LeapTo(CentreX + 5.5f, 4f));
            CinematicOverlay.Instance.ShowHint("They cannot bear to be unequal.", 2.5f);

            // The healthier one pours life into the weaker one: motes of it run along their vertex link.
            int a0 = left.Health.CurrentHealth, b0 = right.Health.CurrentHealth;
            int even = (a0 + b0) / 2;
            BossBrain donor = a0 >= b0 ? left : right;
            BossBrain receiver = donor == left ? right : left;
            LineRenderer link = MakeVertexLink("Rebalance Link");
            Sfx.Play(SoundId.VertexLink);
            Sfx.Play(SoundId.Rebalance);
            var motes = new List<(VertexGlow glow, float born)>();
            float nextMote = 0f;
            const float duration = 1.6f, moteTime = 0.45f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float e = Mathf.SmoothStep(0f, 1f, t / duration);
                left.Health.SetHealth(Mathf.RoundToInt(Mathf.Lerp(a0, even, e)));
                right.Health.SetHealth(Mathf.RoundToInt(Mathf.Lerp(b0, even, e)));
                donor.SetPose(UnityEngine.Random.insideUnitCircle * 0.06f, 0f);
                receiver.SetPose(Vector2.zero, 0f);
                LinkVertices(link, donor, receiver, 1f, 0, 0); // tip to tip
                Vector2 from = link.GetPosition(0), to = link.GetPosition(link.positionCount - 1);

                if (t >= nextMote && t < duration - moteTime)
                {
                    nextMote = t + 0.06f;
                    motes.Add((MakeMote(from), t));
                }
                for (int i = motes.Count - 1; i >= 0; i--)
                {
                    float p = (t - motes[i].born) / moteTime;
                    if (p >= 1f)
                    {
                        if (motes[i].glow != null) Destroy(motes[i].glow.gameObject);
                        motes.RemoveAt(i);
                        receiver.Shape.SetCornerGlow(-1, 1f, healthColor); // it brightens as it fills
                        continue;
                    }
                    motes[i].glow.transform.position = Vector2.Lerp(from, to, p) + Vector2.up * Mathf.Sin(p * Mathf.PI) * 1.4f;
                }
                yield return null;
            }
            foreach (var m in motes) if (m.glow != null) Destroy(m.glow.gameObject);
            left.Health.SetHealth(even);
            right.Health.SetHealth(even);
            Fx.Ring(receiver.Shape.transform.position, healthColor, 3.5f, 0.4f, 0.14f);
            Fx.Ring(donor.Shape.transform.position, healthColor, 3.5f, 0.4f, 0.14f);
            Destroy(link.gameObject);
            left.Settle();
            right.Settle();
        }

        /// <summary>A small glowing mote of shared life.</summary>
        private VertexGlow MakeMote(Vector2 at)
        {
            var go = new GameObject("Rebalance Mote");
            temporary.Add(go);
            go.transform.position = at;
            var glow = go.AddComponent<VertexGlow>();
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = Fx.Material;
            mr.sortingOrder = 12;
            glow.Color = healthColor;
            glow.Radius = 0.45f;
            glow.Intensity = 1.5f;
            return glow;
        }

        /// <summary>A crackling line of vertex power (instead of a plain rope).</summary>
        private LineRenderer MakeVertexLink(string objectName)
        {
            LineRenderer line = MakeLine(objectName, 0.1f, glowColor);
            line.positionCount = 14;
            line.numCapVertices = 2;
            return line;
        }

        /// <summary>
        /// Joins the nearest corners of two twins with vertex power: the two vertices glow and a jittering bolt runs
        /// between them. <paramref name="strength"/> 0..1 fades it in. Pass corner indices to join particular corners.
        /// </summary>
        private void LinkVertices(LineRenderer line, BossBrain a, BossBrain b, float strength, int cornerA = -1, int cornerB = -1)
        {
            int ca = cornerA >= 0 ? cornerA : ClosestCorner(a, b.Shape.transform.position);
            int cb = cornerB >= 0 ? cornerB : ClosestCorner(b, a.Shape.transform.position);
            a.Shape.SetCornerGlow(ca, strength, glowColor);
            b.Shape.SetCornerGlow(cb, strength, glowColor);
            Vector2 from = a.Corner(ca), to = b.Corner(cb);
            Vector2 along = to - from;
            Vector2 side = new Vector2(-along.y, along.x).normalized;
            int n = line.positionCount;
            for (int i = 0; i < n; i++)
            {
                float p = i / (n - 1f);
                float jitter = (i == 0 || i == n - 1) ? 0f : UnityEngine.Random.Range(-1f, 1f) * 0.22f * Mathf.Sin(p * Mathf.PI);
                line.SetPosition(i, from + along * p + side * jitter * strength);
            }
            float width = (0.07f + 0.05f * Mathf.PingPong(Time.time * 10f, 1f)) * strength;
            line.startWidth = line.endWidth = width;
            Color c = glowColor;
            c.a = 0.95f * strength;
            line.startColor = line.endColor = c;
        }

        private static int ClosestCorner(BossBrain b, Vector2 to)
        {
            int best = 0;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < 3; i++)
            {
                float d = ((Vector2)b.Corner(i) - to).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            return best;
        }

        #endregion

        #region Phase 2 moves (the survivor alone)

        private IEnumerator SoloCharge(BossBrain s)
        {
            int startSide = s.Body.position.x < CentreX ? -1 : 1;
            yield return s.LeapTo(startSide < 0 ? s.ArenaMinX : s.ArenaMaxX, 3f);
            for (int crossing = 0; crossing < 2; crossing++)
            {
                int dir = crossing == 0 ? -startSide : startSide;
                yield return AimLance(s, dir, crossing == 0 ? 0.45f : 0.25f);
                yield return Charge(s, dir, chargeSpeed * 1.25f, false);
            }
            s.Settle();
        }

        private IEnumerator SoloSlam(BossBrain s)
        {
            for (int i = 0; i < 3; i++)
            {
                yield return Tell(s, 0, i == 0 ? 0.35f : 0.15f);
                yield return s.LeapTo(PlayerPos.x, slamHeight - 1f, 1.3f, 3.6f);
                Slam(s);
                yield return Wait(0.25f);
            }
            s.Settle();
        }

        /// <summary>Alone: the dive twice in a row, then it flips back upright.</summary>
        private IEnumerator SoloPlunge(BossBrain s)
        {
            yield return RiseTo(s, PlayerX(s), 0.28f);
            yield return Dive(s, plungeHang * 0.8f);
            yield return RiseTo(s, PlayerX(s), 0.2f);
            yield return Dive(s, plungeHang * 0.5f);
            yield return Recover(s);
        }

        /// <summary>One beam from its tip, swinging down across the arena like a clock hand: jump it as it passes.</summary>
        private IEnumerator SoloLine(BossBrain s)
        {
            int side = PlayerPos.x < CentreX ? 1 : -1; // from the wall away from the circle
            yield return s.LeapTo(side < 0 ? s.ArenaMinX : s.ArenaMaxX, 3f);
            int towards = -side;

            LineRenderer warn = MakeLine("Sweep (warning)", 0.06f, new Color(lineColor.r, lineColor.g, lineColor.b, 0.5f));
            for (float t = 0f; t < 0.8f; t += Time.deltaTime)
            {
                s.Shape.SetCornerGlow(0, t / 0.8f, glowColor);
                Vector2 tip = s.Corner(0);
                SetLine(warn, tip, s.ClipToArena(tip, Aim(towards, 35f)));
                yield return null;
            }
            Destroy(warn.gameObject);

            LineRenderer beam = MakeLine("Sweep", 0.32f, lineColor);
            Sfx.Play(SoundId.LaserFire);
            for (float t = 0f; t < 1.7f; t += Time.deltaTime)
            {
                float angle = Mathf.Lerp(35f, -28f, Mathf.SmoothStep(0f, 1f, t / 1.7f));
                Vector2 tip = s.Corner(0);
                Vector2 end = s.ClipToArena(tip, Aim(towards, angle));
                SetLine(beam, tip, end);
                HurtAlongSegment(s, tip, end);
                yield return null;
            }
            Destroy(beam.gameObject);
            s.Settle();
        }

        #endregion

        #region Shared pieces

        /// <summary>A short wind-up: the corner glows and the body tenses.</summary>
        private IEnumerator Tell(BossBrain b, int corner, float time)
        {
            Sfx.Play(SoundId.Tell, b.Body.position);
            for (float t = 0f; t < time; t += Time.deltaTime)
            {
                b.Shape.SetCornerGlow(corner, t / time, glowColor);
                b.SetPose(UnityEngine.Random.insideUnitCircle * 0.05f, 0f);
                yield return null;
            }
            b.ResetPose();
        }

        /// <summary>
        /// Turns on its side so the sharp tip (where the two long edges meet) points along <paramref name="dir"/>,
        /// lifted just enough that its long edge clears the floor, tip glowing: the wind-up of a charge.
        /// </summary>
        private IEnumerator AimLance(BossBrain b, int dir, float time)
        {
            float lift = LanceLift(b, dir);
            Sfx.Play(SoundId.Tell, b.Body.position);
            for (float t = 0f; t < time; t += Time.deltaTime)
            {
                float e = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, t / (time * 0.6f)));
                b.SetPose(new Vector2(0f, lift * e) + UnityEngine.Random.insideUnitCircle * 0.04f * (t / time), -90f * dir * e);
                b.Shape.SetCornerGlow(0, t / time, glowColor);
                yield return null;
            }
            b.SetPose(new Vector2(0f, lift), -90f * dir);
        }

        /// <summary>How far to raise a twin turned on its side (tip along <paramref name="dir"/>) so it doesn't sink into the floor.</summary>
        private float LanceLift(BossBrain b, int dir)
        {
            b.SetPose(Vector2.zero, -90f * dir);
            float lowest = Mathf.Min(b.Corner(0).y, Mathf.Min(b.Corner(1).y, b.Corner(2).y));
            b.ResetPose();
            return Mathf.Max(0f, Floor + 0.05f - lowest);
        }

        /// <summary>
        /// Rushes tip-first along the floor to the far wall (bouncing in hops if <paramref name="bounce"/>), then
        /// turns back upright. Call <see cref="AimLance"/> first.
        /// </summary>
        private IEnumerator Charge(BossBrain b, int dir, float speed, bool bounce)
        {
            float startX = b.Body.position.x;
            float endX = dir > 0 ? b.ArenaMaxX - b.WallMargin : b.ArenaMinX + b.WallMargin;
            float distance = Mathf.Max(0.1f, Mathf.Abs(endX - startX));
            float x = startX;
            float lift = LanceLift(b, dir);
            b.Body.gravityScale = 0f;
            Fx.SpeedLines(b.Body.position, dir, glowColor);
            Sfx.Play(SoundId.ChargeWhoosh, b.Body.position);
            while ((endX - x) * dir > 0f)
            {
                x += dir * speed * Time.deltaTime;
                if ((endX - x) * dir < 0f) x = endX;
                float y = StandY;
                if (bounce) y += Mathf.Abs(Mathf.Sin(Mathf.Abs(x - startX) / distance * Mathf.PI * 3f)) * 3.2f;
                b.MoveBody(new Vector2(x, y));
                b.SetPose(new Vector2(0f, lift), -90f * dir);

                // The tip and both long edges cut; the body behind them too.
                Vector2 tip = b.Corner(0);
                b.HurtPlayerAt(tip, 0.35f);
                for (int c = 1; c <= 2; c++)
                {
                    b.HurtPlayerAt(Vector2.Lerp(tip, b.Corner(c), 0.35f), 0.25f);
                    b.HurtPlayerAt(Vector2.Lerp(tip, b.Corner(c), 0.7f), 0.25f);
                }
                b.HurtPlayerAt(b.Shape.transform.position, b.Inradius * 0.8f);
                yield return null;
            }
            Fx.Dust(new Vector2(b.Body.position.x, Floor), b.SideLength * 0.6f, 0.8f);

            // Back upright.
            for (float t = 0f; t < 0.18f; t += Time.deltaTime)
            {
                float e = 1f - t / 0.18f;
                b.SetPose(new Vector2(0f, lift * e), -90f * dir * e);
                yield return null;
            }
            b.Settle();
        }

        /// <summary>Landing impact: hurts close by and sends a shockwave both ways along the floor.</summary>
        private void Slam(BossBrain b)
        {
            float x = b.Body.position.x;
            b.HurtPlayerAt(new Vector2(x, Floor + 0.5f), b.SideLength * 0.45f);
            GroundShockwave.Settings wave = GroundShockwave.Settings.Default;
            wave.distance = shockwaveDistance; // they run the length of the arena
            GroundShockwave.Spawn(new Vector2(x, Floor), -1, wave, Fx.Material);
            GroundShockwave.Spawn(new Vector2(x, Floor), 1, wave, Fx.Material);
            if (b.CameraFollow != null) b.CameraFollow.Shake(0.22f, 0.25f);
            Fx.Dust(new Vector2(x, Floor), b.SideLength, 1.2f);
            b.Shape.SetCornerGlow(-1, 0f, glowColor);
        }

        /// <summary>Hurts the circle if it touches the segment (a parry blocks it).</summary>
        private static void HurtAlongSegment(BossBrain owner, Vector2 a, Vector2 b)
        {
            if (owner.Target == null) return;
            Vector2 p = owner.Target.position;
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            Vector2 closest = a + ab * t;
            if (Vector2.Distance(p, closest) > 0.75f) return;
            if (owner.TargetParry != null && owner.TargetParry.TryDeflect(closest)) return;
            owner.HurtPlayerAt(closest, 0.3f);
        }

        private LineRenderer MakeLine(string objectName, float width, Color color)
        {
            var go = new GameObject(objectName);
            temporary.Add(go);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = Fx.Material;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.numCapVertices = 4;
            line.startWidth = line.endWidth = width;
            line.startColor = line.endColor = color;
            line.sortingOrder = 11;
            return line;
        }

        private static void SetLine(LineRenderer line, Vector2 a, Vector2 b)
        {
            line.SetPosition(0, a);
            line.SetPosition(1, b);
        }

        private static Vector2 Direction(float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
        }

        /// <summary>A direction pointing towards <paramref name="towards"/> (±1), tilted up by <paramref name="degrees"/>.</summary>
        private static Vector2 Aim(int towards, float degrees)
        {
            Vector2 d = Direction(degrees);
            return new Vector2(d.x * towards, d.y);
        }

        /// <summary>
        /// Runs a whole phase as one coroutine, stepping nested motions by hand, so stopping it (a twin falls, the fight
        /// resets) stops everything inside it too. (Unity keeps nested coroutines running when their parent stops.)
        /// </summary>
        private static IEnumerator Drive(IEnumerator phase)
        {
            var stepper = new Stepper(phase);
            while (stepper.Step()) yield return null;
        }

        /// <summary>Turns a twin's touch damage off (e.g. while it flies into position) or back on.</summary>
        private static void Harmless(BossBrain b, bool harmless)
        {
            var touch = b.GetComponent<Combat.ContactDamage>();
            if (touch != null) touch.enabled = !harmless;
        }

        private static IEnumerator Wait(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null;
        }

        /// <summary>Runs two motions side by side and waits for both (they must only yield null or nested motions).</summary>
        private static IEnumerator Both(IEnumerator a, IEnumerator b)
        {
            var ra = new Stepper(a);
            var rb = new Stepper(b);
            bool runA = true, runB = true;
            while (runA || runB)
            {
                if (runA) runA = ra.Step();
                if (runB) runB = rb.Step();
                if (runA || runB) yield return null;
            }
        }

        private static void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>Steps a coroutine by hand, following nested IEnumerators.</summary>
        private sealed class Stepper
        {
            private readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
            public Stepper(IEnumerator root) => stack.Push(root);

            public bool Step()
            {
                while (stack.Count > 0)
                {
                    IEnumerator top = stack.Peek();
                    if (!top.MoveNext()) { stack.Pop(); continue; }
                    if (top.Current is IEnumerator inner) { stack.Push(inner); continue; }
                    return true;
                }
                return false;
            }
        }

        #endregion
    }
}
