using System.Collections;
using UnityEngine;

namespace ProjectCircle.Enemies.Boss
{
    /// <summary>
    /// Slam Chain (phase 2). The boss leaps at the circle and crashes down, sending shockwaves racing along the
    /// floor both ways, and does it again — three slams back to back, re-aiming each time. Only after the last
    /// one does it stagger for a moment.
    ///
    /// Jump the shockwaves; don't be under it when it lands.
    /// </summary>
    public sealed class BossSlamChainMove : BossMove
    {
        [SerializeField, Range(1, 6)] private int slams = 3;
        [SerializeField, Min(0.05f)] private float crouchTime = 0.22f;
        [SerializeField, Min(0.5f)] private float leapHeight = 4.2f;
        [SerializeField, Min(0f)] private float betweenSlams = 0.12f;

        [Tooltip("Gravity multiplier on the way up (higher = a quicker hop).")]
        [SerializeField, Min(0.5f)] private float riseGravity = 1.8f;

        [Tooltip("Gravity multiplier on the way down (higher = it slams down hard and fast).")]
        [SerializeField, Min(0.5f)] private float fallGravity = 4f;
        [SerializeField] private GroundShockwave.Settings shockwave = GroundShockwave.Settings.Default;
        [SerializeField, Min(0f)] private float landingRadius = 1.6f;

        public override IEnumerator Run()
        {
            for (int i = 0; i < slams; i++)
            {
                // Quick squat.
                for (float t = 0f; t < crouchTime; t += Time.deltaTime)
                {
                    float p = t / crouchTime;
                    Boss.SetPose(new Vector2(0f, -0.25f * p), 0f);
                    yield return null;
                }
                Boss.ResetPose();

                float targetX = Boss.Target != null ? Boss.Target.position.x : Boss.Body.position.x;
                yield return Boss.LeapTo(targetX, leapHeight, riseGravity, fallGravity);
                Impact();

                if (i < slams - 1) yield return new WaitForSeconds(betweenSlams);
            }
        }

        private void Impact()
        {
            float floorY = Boss.FloorY;
            Vector2 centre = Boss.Body.position;
            Boss.HurtPlayerAt(new Vector2(centre.x, floorY + 0.5f), landingRadius);

            float half = Boss.SideLength * 0.5f;
            GroundShockwave.Spawn(new Vector2(centre.x - half, floorY), -1, shockwave, Boss.ShapeMaterial);
            GroundShockwave.Spawn(new Vector2(centre.x + half, floorY), 1, shockwave, Boss.ShapeMaterial);
            if (Boss.CameraFollow != null) Boss.CameraFollow.Shake(0.4f, 0.35f);
            Systems.Fx.Dust(new Vector2(centre.x, floorY), Boss.SideLength, 1.4f);
            Systems.Fx.Crack(new Vector2(centre.x, floorY), Boss.SideLength * 1.2f, new Color(1f, 0.85f, 0.6f, 1f));
        }

        public override void Cancel()
        {
            if (Boss != null) Boss.ResetPose();
        }
    }
}
