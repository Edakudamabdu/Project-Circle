using System.Collections;
using UnityEngine;

namespace ProjectCircle.Enemies.Boss
{
    /// <summary>
    /// Wall-to-Wall Roll. The boss leaps to the nearer end of the arena, flings a bolt up out of its top vertex,
    /// and immediately rolls corner over corner to the other wall, where it stops, dazed.
    ///
    /// The bolt hangs glowing above the start, then dives at the circle, fast, timed to arrive just after the
    /// roll has passed it: jump the roll, land, parry the bolt (it flies back into the boss).
    ///
    /// Enraged (phase 2): it shakes with rage first, and a second bolt dives in a moment after the first.
    /// </summary>
    public sealed class BossRollMove : BossMove
    {
        [Header("Getting there")]
        [SerializeField, Min(0.5f)] private float leapHeight = 3f;

        [Header("Bolts")]
        [Tooltip("Vertex-colour material for the bolts (e.g. ShapeUnlit).")]
        [SerializeField] private Material boltMaterial;

        [SerializeField] private HomingOrb.Settings bolt = DefaultBolt;

        [Tooltip("How fast the bolt dives at the circle once it stops hanging.")]
        [SerializeField, Min(1f)] private float boltSpeed = 18f;

        [Tooltip("The bolt arrives this long after the roll has passed the circle: about when it lands from jumping the roll.")]
        [SerializeField, Min(0f)] private float arriveAfterPass = 0.35f;

        [Tooltip("Enraged: the second bolt arrives this long after the first (a separate parry).")]
        [SerializeField, Min(0f)] private float secondBoltGap = 0.6f;

        [Header("Roll")]
        [Tooltip("Rage shake before it fires and rolls (enraged only).")]
        [SerializeField, Min(0f)] private float rageTime = 0.7f;

        [Tooltip("After flinging the bolt it rocks back on its heel this long before the first tumble: the beat to jump on.")]
        [SerializeField, Min(0f)] private float beforeRoll = 0.3f;

        [Tooltip("Seconds per tumble while rolling (each tumble covers ~4.2 units). Lower = faster. 0.32 ≈ 13 units/s, against the circle's 8.5.")]
        [SerializeField, Min(0.04f)] private float rollStepTime = 0.32f;

        [Tooltip("Roll step time when enraged.")]
        [SerializeField, Min(0.04f)] private float enragedRollStepTime = 0.26f;

        private static HomingOrb.Settings DefaultBolt
        {
            get
            {
                HomingOrb.Settings s = HomingOrb.Settings.Default;
                s.color = new Color(1f, 0.72f, 0.4f, 1f);
                s.glowRadius = 0.34f;
                s.hitRadius = 0.16f;
                s.driftSpeed = 4f;      // flung up out of the top vertex first...
                s.driftTime = 0.3f;
                s.maxSpeed = 8f;        // ...then hunts at about the circle's running speed
                s.acceleration = 12f;
                s.turnRate = 360f;      // tight turns that never fade: parry it, don't outrun it
                s.homingTime = 3.5f;
                s.fullTurnFraction = 1f;
                s.lifetime = 4.2f;
                s.groundLayers = 0;     // passes through platforms: it must reach the circle to be parried
                return s;
            }
        }

        public override IEnumerator Run()
        {
            // To the nearer end, facing across the arena.
            int side = Boss.Body.position.x < Boss.ArenaCentreX ? -1 : 1;
            float endX = side < 0 ? Boss.ArenaMinX + Boss.WallMargin : Boss.ArenaMaxX - Boss.WallMargin;
            if (Mathf.Abs(Boss.Body.position.x - endX) > 0.4f) yield return Boss.LeapTo(endX, leapHeight);
            int dir = -side;

            if (Boss.Enraged)
            {
                if (Boss.CameraFollow != null) Boss.CameraFollow.Shake(0.12f, rageTime);
                yield return Boss.Tremble(rageTime, 0.12f);
            }
            else
            {
                yield return new WaitForSeconds(0.15f);
            }

            // Work out when the roll will have passed the circle, so the bolt can arrive just as the circle lands
            // from jumping it. The bolt hangs glowing above the start until then, then dives in fast.
            float step = Boss.Enraged ? enragedRollStepTime : rollStepTime;
            float rollSpeed = Boss.SideLength / step;
            float ahead = Boss.Target != null ? (Boss.Target.position.x - Boss.Body.position.x) * dir : 0f;
            float passTime = beforeRoll + Mathf.Max(0f, ahead + Boss.SideLength * 0.5f) / rollSpeed;
            float arrive = Time.time + passTime + arriveAfterPass;
            FireBolt(dir, arrive);
            if (Boss.Enraged) FireBolt(dir, arrive + secondBoltGap);

            // Rock back on the rear corner, then go: the readable beat before the roll.
            for (float t = 0f; t < beforeRoll; t += Time.deltaTime)
            {
                Boss.SetPivotPose(-dir, 12f * Mathf.Sin(t / beforeRoll * Mathf.PI));
                yield return null;
            }
            Boss.ResetPose();

            while (Boss.CanStepTo(dir))
            {
                yield return Boss.TumbleStep(dir, step, hurts: true);
                if (Boss.CameraFollow != null) Boss.CameraFollow.Shake(0.07f, 0.1f);
            }
            if (Boss.CameraFollow != null) Boss.CameraFollow.Shake(0.3f, 0.3f);
        }

        /// <summary>
        /// Flings a bolt up out of the top vertex. It hangs there (slowing to a glowing stop) until it is time to
        /// dive, then homes in fast enough to reach the circle at <paramref name="arriveAt"/>.
        /// </summary>
        private void FireBolt(int dir, float arriveAt)
        {
            if (Boss.Target == null) return;
            Vector2 origin = Boss.Corner(0) + Vector2.up * 0.3f;
            Vector2 aim = new Vector2(dir * 0.3f, 1f).normalized;

            HomingOrb.Settings s = bolt;
            s.maxSpeed = boltSpeed;
            s.acceleration = boltSpeed * 5f;   // up to speed in a fifth of a second
            s.turnRate = 900f;                  // a fast dive that still follows a jumping circle
            s.fullTurnFraction = 1f;
            float travel = Vector2.Distance(origin + Vector2.up * 1.5f, Boss.Target.position) / boltSpeed + 0.1f;
            s.driftTime = Mathf.Max(0.25f, arriveAt - Time.time - travel); // the hang
            // Steer for most of the dive, then commit to a straight line for the last stretch: at this speed a
            // bolt still turning would circle the circle instead of hitting it (and slip past the parry).
            s.homingTime = travel * 0.7f;
            s.lifetime = s.driftTime + s.homingTime + 0.5f;

            HomingOrb orb = HomingOrb.Spawn(origin, s, boltMaterial, Boss.Target);
            orb.ReturnTo = Boss.Shape.transform; // a parried bolt flies back into the boss
            orb.Release(aim);
            Systems.Fx.Embers(orb.transform, s.color, s.driftTime); // it smoulders while it hangs
        }

        public override void Cancel()
        {
            if (Boss != null) Boss.ResetPose();
        }
    }
}
