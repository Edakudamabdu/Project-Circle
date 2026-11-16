using System.Collections;
using UnityEngine;

namespace ProjectCircle.Enemies.Boss
{
    /// <summary>
    /// One move in a boss's moveset. Add one component per move to the boss; the <see cref="BossBrain"/>
    /// picks among the ready ones (weighted at random), runs it to the end, then waits before the next.
    ///
    /// To make a new move: derive from this, write <see cref="Run"/> as a coroutine (wind-up, attack,
    /// recovery), override <see cref="CanStart"/> if it needs a range or position, and clean up in
    /// <see cref="Cancel"/> (called if the fight stops mid-move).
    /// </summary>
    public abstract class BossMove : MonoBehaviour
    {
        [Header("Selection")]
        [Tooltip("Name shown in the Inspector and debug logs.")]
        [SerializeField] private string moveName = "Move";

        [Tooltip("Relative chance of being picked among the moves that are ready.")]
        [SerializeField, Min(0f)] private float weight = 1f;

        [Tooltip("Seconds after this move ends before it can be picked again.")]
        [SerializeField, Min(0f)] private float cooldown = 2f;

        [Tooltip("First phase this move is used in (1 = from the start).")]
        [SerializeField, Min(1)] private int minPhase = 1;

        [Tooltip("Last phase this move is used in.")]
        [SerializeField, Min(1)] private int maxPhase = 99;

        [Tooltip("Pause after this move before the boss picks another (its recovery window).")]
        [SerializeField, Min(0f)] private float recoverTime = 0.6f;

        public string MoveName => moveName;
        public int MaxPhase => maxPhase;
        public float Weight => weight;
        public float RecoverTime => recoverTime;

        /// <summary>The boss using this move (set by <see cref="BossBrain"/>).</summary>
        protected BossBrain Boss { get; private set; }

        private float readyTime;

        internal void Bind(BossBrain boss) => Boss = boss;

        internal void StartCooldown() => readyTime = Time.time + cooldown;

        internal void ResetCooldown() => readyTime = 0f;

        /// <summary>True if the move can be picked now.</summary>
        internal bool IsReady(int phase) => Time.time >= readyTime && phase >= minPhase && phase <= maxPhase && CanStart();

        /// <summary>Extra conditions (range, position, ...). Default: always.</summary>
        public virtual bool CanStart() => true;

        /// <summary>The move itself. The boss does nothing else until it finishes.</summary>
        public abstract IEnumerator Run();

        /// <summary>Called if the fight stops mid-move (boss defeated or reset): undo glows, poses, spawned things.</summary>
        public virtual void Cancel() { }
    }
}
