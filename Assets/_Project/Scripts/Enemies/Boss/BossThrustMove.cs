using System.Collections;
using UnityEngine;

namespace ProjectCircle.Enemies.Boss
{
    /// <summary>
    /// Vertex Thrust — the boss's basic attack. It tumbles in towards the circle (corner over corner, each
    /// landing a thud), leans back, then tips forward over its front corner and drives its top vertex down
    /// onto the circle. It is stuck tipped over for a moment afterwards: the main opening.
    ///
    /// Enraged (phase 2): it jabs twice, turning to face the circle again in between.
    /// Dodge by jumping over the point or dashing through.
    /// </summary>
    public sealed class BossThrustMove : BossMove
    {
        [Header("Approach")]
        [Tooltip("Thrusts once the circle is within this horizontal distance of its centre.")]
        [SerializeField, Min(1f)] private float strikeRange = 6f;

        [Tooltip("Tumble steps it takes to close in before giving up on this move.")]
        [SerializeField, Min(0)] private int maxApproachSteps = 3;

        [SerializeField, Min(0.05f)] private float stepTime = 0.34f;

        [Header("Thrust")]
        [Tooltip("Lean back before the jab while the top vertex glows: the cue to back off or jump.")]
        [SerializeField, Min(0.05f)] private float windupTime = 0.55f;

        [SerializeField, Range(0f, 30f)] private float leanBack = 16f;
        [SerializeField] private Color windupGlow = new Color(1f, 0.85f, 0.6f, 1f);

        [SerializeField, Min(0.02f)] private float strikeTime = 0.2f;

        [Tooltip("How far it tips forward. ~105 brings the top vertex down to the circle's height.")]
        [SerializeField, Range(60f, 118f)] private float tipAngle = 106f;

        [Tooltip("Stuck tipped over after the jab (halved when enraged). The opening.")]
        [SerializeField, Min(0f)] private float holdTime = 0.55f;

        [SerializeField, Min(0.05f)] private float riseTime = 0.25f;

        public override IEnumerator Run()
        {
            int jabs = Boss.Enraged ? 2 : 1;
            float speed = Boss.Enraged ? 0.8f : 1f; // everything a bit quicker when enraged

            for (int j = 0; j < jabs; j++)
            {
                // Close in, corner over corner.
                int steps = 0;
                while (Boss.DistanceToTarget > strikeRange && steps < maxApproachSteps && Boss.CanStepTo(Boss.DirectionToTarget))
                {
                    // Walking in doesn't hurt (only touching its body does): the jab is the attack.
                    yield return Boss.TumbleStep(Boss.DirectionToTarget, stepTime * speed, hurts: false);
                    if (Boss.CameraFollow != null) Boss.CameraFollow.Shake(0.08f, 0.12f);
                    steps++;
                }
                if (Boss.DistanceToTarget > strikeRange) yield break; // couldn't catch the circle this time

                int dir = Boss.DirectionToTarget;

                // Lean back while the top vertex glows... (the second jab of a double gets the full, unhurried
                // warning: it has turned to face the circle again, and the player needs to see it coming)
                float windup = j == 0 ? windupTime * speed : windupTime;
                Systems.Fx.Converge(Boss.Corner(0), windupGlow, windup, 1.8f); // light drawn into the point
                for (float t = 0f, d = windup; t < d; t += Time.deltaTime)
                {
                    float p = t / d;
                    Boss.SetPivotPose(-dir, leanBack * Mathf.SmoothStep(0f, 1f, p));
                    Boss.Shape.SetCornerGlow(0, p, windupGlow);
                    yield return null;
                }

                // ...and drive the top vertex down at the circle.
                // (The first quarter rocks back down onto both corners, the rest tips over the front corner.)
                for (float t = 0f; t < strikeTime; t += Time.deltaTime)
                {
                    float p = t / strikeTime;
                    if (p < 0.25f)
                    {
                        Boss.SetPivotPose(-dir, leanBack * (1f - p / 0.25f));
                    }
                    else
                    {
                        float q = (p - 0.25f) / 0.75f;
                        Boss.SetPivotPose(dir, tipAngle * q * q);
                        if (q > 0.35f) Boss.HurtAlongLeadingEdge(dir);
                    }
                    yield return null;
                }
                Boss.SetPivotPose(dir, tipAngle);
                Boss.HurtAlongLeadingEdge(dir);
                Boss.Shape.SetCornerGlow(-1, 0f, windupGlow);
                if (Boss.CameraFollow != null) Boss.CameraFollow.Shake(0.16f, 0.18f);

                // Stuck tipped over: the opening.
                yield return new WaitForSeconds(holdTime * Boss.WindowScale);

                // Push back upright.
                for (float t = 0f, d = riseTime * speed; t < d; t += Time.deltaTime)
                {
                    Boss.SetPivotPose(dir, Mathf.Lerp(tipAngle, 0f, Mathf.SmoothStep(0f, 1f, t / d)));
                    yield return null;
                }
                Boss.ResetPose();
            }
        }

        public override void Cancel()
        {
            if (Boss == null) return;
            Boss.ResetPose();
            if (Boss.Shape != null) Boss.Shape.SetCornerGlow(-1, 0f, windupGlow);
        }
    }
}
