using ProjectCircle.Player;
using UnityEngine;

namespace ProjectCircle.Systems
{
    /// <summary>
    /// A barrier that stays shut until the run has made enough progress (<see cref="GameProgress"/>):
    /// - the Seal Gate before the Equilateral Spire (<see cref="minSeals"/> = 2),
    /// - Mould gates and the Rank Gate (<see cref="requiredFlags"/> = Mould.Triangle),
    /// - the way on from the Base Warden's arena (requiredFlags = Seal.Warden)...
    /// It opens by sinking into the floor (or vanishing). If the condition is already met when the scene loads,
    /// it starts open.
    ///
    /// Setup: put this on the barrier object itself (a solid block on the Ground layer with its collider).
    /// Its children are looks (symbols, glow) and sink with it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProgressGate : MonoBehaviour
    {
        [Tooltip("Every one of these story flags must be set (e.g. Mould.Triangle, Seal.Warden).")]
        [SerializeField] private string[] requiredFlags = new string[0];

        [Tooltip("At least this many Vertex Seals.")]
        [SerializeField, Min(0)] private int minSeals;

        [Tooltip("And this ability (None = no ability needed).")]
        [SerializeField] private PlayerAbility requiredAbility = PlayerAbility.None;

        [SerializeField, Min(0.05f)] private float openTime = 0.8f;

        [Tooltip("Sink into the floor when opening (off = just disappear).")]
        [SerializeField] private bool sink = true;

        private bool open;

        private void Start()
        {
            if (Satisfied()) SetOpen();
            else GameProgress.Changed += HandleChanged;
        }

        private void OnDestroy() => GameProgress.Changed -= HandleChanged;

        private void HandleChanged()
        {
            if (open || !Satisfied()) return;
            GameProgress.Changed -= HandleChanged;
            StartCoroutine(Open());
        }

        private bool Satisfied() =>
            GameProgress.HasAll(requiredFlags) &&
            GameProgress.SealCount >= minSeals &&
            (requiredAbility == PlayerAbility.None || (GameProgress.Abilities & requiredAbility) == requiredAbility);

        private System.Collections.IEnumerator Open()
        {
            open = true;
            Vector3 from = transform.position;
            Vector3 to = from + Vector3.down * transform.lossyScale.y;
            var col = GetComponent<Collider2D>();
            if (col != null) Fx.Dust(new Vector2(col.bounds.center.x, col.bounds.min.y), col.bounds.size.x + 1f, 1.2f);
            for (float t = 0f; t < openTime && sink; t += Time.deltaTime)
            {
                transform.position = Vector3.Lerp(from, to, t / openTime) + (Vector3)(Random.insideUnitCircle * 0.04f);
                yield return null;
            }
            SetOpen();
        }

        private void SetOpen()
        {
            open = true;
            gameObject.SetActive(false);
        }
    }
}
