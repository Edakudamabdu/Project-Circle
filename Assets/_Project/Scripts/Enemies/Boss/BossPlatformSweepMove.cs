using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectCircle.Enemies.Boss
{
    /// <summary>
    /// Platform Sweep: the answer to the circle hiding on the ledges. Only used once the circle has stood on a
    /// ledge for a moment. The boss crouches, then slam-jumps onto each ledge in turn (starting with the nearest),
    /// crashing down on it: anything standing on that ledge is hit, and small shockwaves run along its top. After
    /// the last ledge it drops back to the floor.
    ///
    /// Dodge by getting off the ledge it is about to land on (or being in the air when it lands).
    /// </summary>
    public sealed class BossPlatformSweepMove : BossMove
    {
        [Header("Trigger")]
        [Tooltip("Seconds the circle must stay on a ledge before this move becomes available.")]
        [SerializeField, Min(0f)] private float campTime = 1.2f;

        [Header("Slams")]
        [SerializeField, Min(0.05f)] private float crouchTime = 0.3f;
        [SerializeField, Min(0.5f)] private float arcHeight = 3f;
        [SerializeField, Min(0.5f)] private float riseGravity = 1.6f;
        [SerializeField, Min(0.5f)] private float fallGravity = 3.5f;
        [SerializeField, Min(0f)] private float pauseOnLedge = 0.25f;

        [Tooltip("Small shockwaves along the ledge top.")]
        [SerializeField] private GroundShockwave.Settings shockwave = SmallShockwave;

        private float onLedgeSince = -1f;

        private static GroundShockwave.Settings SmallShockwave
        {
            get
            {
                GroundShockwave.Settings s = GroundShockwave.Settings.Default;
                s.height = 1.1f;
                s.distance = 3f;
                s.speed = 12f;
                return s;
            }
        }

        private void Update()
        {
            if (Boss == null) return;
            if (Boss.TargetLedge != null)
            {
                if (onLedgeSince < 0f) onLedgeSince = Time.time;
            }
            else if (Boss.TargetMovement == null || Boss.TargetMovement.IsGrounded)
            {
                onLedgeSince = -1f; // back on the floor (a jump between ledges doesn't reset it)
            }
        }

        public override bool CanStart() =>
            onLedgeSince >= 0f && Time.time - onLedgeSince >= campTime && Boss.IsGrounded && Boss.Ledges.Count > 0;

        public override IEnumerator Run()
        {
            // Visit every ledge, nearest first, then sweeping across.
            var order = new List<Collider2D>();
            foreach (Collider2D ledge in Boss.Ledges) if (ledge != null) order.Add(ledge);
            float x = Boss.Body.position.x;
            order.Sort((a, b) => a.bounds.center.x.CompareTo(b.bounds.center.x));
            if (Mathf.Abs(order[order.Count - 1].bounds.center.x - x) < Mathf.Abs(order[0].bounds.center.x - x)) order.Reverse();

            foreach (Collider2D ledge in order)
            {
                // Squat: the cue.
                for (float t = 0f; t < crouchTime; t += Time.deltaTime)
                {
                    Boss.SetPose(new Vector2(0f, -0.25f * (t / crouchTime)), 0f);
                    yield return null;
                }
                Boss.ResetPose();

                Bounds b = ledge.bounds;
                Vector2 landing = new Vector2(b.center.x, b.max.y + Boss.Inradius);
                yield return Boss.ArcTo(landing, arcHeight, riseGravity, fallGravity);
                Slam(b);
                yield return new WaitForSeconds(pauseOnLedge * Boss.WindowScale);
            }

            // Back down to the floor (gravity returns; it drops through the ledge it stands on).
            float floorX = Mathf.Clamp(Boss.Body.position.x, Boss.ArenaMinX + Boss.WallMargin, Boss.ArenaMaxX - Boss.WallMargin);
            yield return Boss.LeapTo(floorX, 1.5f, riseGravity, fallGravity);
            onLedgeSince = -1f;
        }

        /// <summary>Crashes down on a ledge: hurts whoever stands on it, dust, a crack, shockwaves along its top.</summary>
        private void Slam(Bounds ledge)
        {
            float top = ledge.max.y;
            Vector2 centre = Boss.Body.position;

            // Anyone standing on this ledge (feet on its top) is hit.
            Collider2D player = Physics2D.OverlapBox(new Vector2(ledge.center.x, top + 0.6f), new Vector2(ledge.size.x + 0.4f, 1.2f), 0f, Boss.PlayerLayers);
            if (player != null && Boss.TargetHealth != null) Boss.TargetHealth.TakeDamage(1, centre);

            float half = Boss.SideLength * 0.5f;
            GroundShockwave.Spawn(new Vector2(centre.x - half, top), -1, shockwave, Boss.ShapeMaterial);
            GroundShockwave.Spawn(new Vector2(centre.x + half, top), 1, shockwave, Boss.ShapeMaterial);
            Systems.Fx.Dust(new Vector2(centre.x, top), Boss.SideLength * 0.8f, 1.1f);
            Systems.Fx.Crack(new Vector2(centre.x, top), ledge.size.x * 0.5f, new Color(1f, 0.85f, 0.6f, 1f));
            if (Boss.CameraFollow != null) Boss.CameraFollow.Shake(0.35f, 0.3f);
        }

        public override void Cancel()
        {
            if (Boss != null) Boss.ResetPose();
            onLedgeSince = -1f;
        }
    }
}
