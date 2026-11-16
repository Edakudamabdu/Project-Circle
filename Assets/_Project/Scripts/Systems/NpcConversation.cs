using ProjectCircle.Player;
using ProjectCircle.UI;
using TMPro;
using UnityEngine;

namespace ProjectCircle.Systems
{
    /// <summary>
    /// Lets the circle talk to (well, listen to) a character. When the circle is close, a "Listen" prompt
    /// fades in above the character; pressing Interact plays a conversation in the <see cref="DialogueBox"/>.
    ///
    /// The first time, <see cref="firstMeeting"/> plays. After that the <see cref="repeatDialogues"/> play in
    /// turn (the last one repeats), so the character has something new to say for a while.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NpcConversation : MonoBehaviour
    {
        [Header("Dialogue")]
        [SerializeField] private Dialogue firstMeeting;

        [Tooltip("Played in order on later visits; the last one repeats.")]
        [SerializeField] private Dialogue[] repeatDialogues;

        [Header("Prompt")]
        [Tooltip("How close (from this object's position) the circle must be to talk.")]
        [SerializeField, Min(0.1f)] private float talkRadius = 2.2f;

        [Tooltip("Text shown above the character while the circle is in range.")]
        [SerializeField] private TMP_Text prompt;

        [SerializeField, Min(0.01f)] private float promptFadeTime = 0.15f;

        /// <summary>Raised when a conversation finishes. Argument: how many conversations have finished so far.</summary>
        public event System.Action<int> ConversationFinished;

        private Transform player;
        private PlayerInputReader input;
        private DialogueBox box;
        private int timesTalked;
        private float promptAlpha;

        private void Start()
        {
            var movement = FindAnyObjectByType<PlayerMovement>();
            if (movement != null)
            {
                player = movement.transform;
                input = movement.GetComponent<PlayerInputReader>();
                input.InteractPressed += HandleInteract;
            }

            box = FindAnyObjectByType<DialogueBox>();
            if (box == null) Debug.LogWarning($"{name}: no {nameof(DialogueBox)} in the scene, so it can't talk.", this);

            SetPromptAlpha(0f);
        }

        private void OnDestroy()
        {
            if (input != null) input.InteractPressed -= HandleInteract;
        }

        // Disabled (e.g. the character has fallen unconscious): hide the prompt. Interact is ignored too.
        private void OnDisable()
        {
            promptAlpha = 0f;
            SetPromptAlpha(0f);
        }

        private void Update()
        {
            // Fade the prompt in while the circle is close and not already listening.
            float target = InRange() && box != null && !box.IsOpen ? 1f : 0f;
            promptAlpha = Mathf.MoveTowards(promptAlpha, target, Time.unscaledDeltaTime / promptFadeTime);
            SetPromptAlpha(promptAlpha);
        }

        private void HandleInteract()
        {
            if (!isActiveAndEnabled || box == null || box.IsOpen || !InRange()) return;

            Dialogue next = NextDialogue();
            if (next == null) return;

            box.Play(next, () =>
            {
                timesTalked++;
                ConversationFinished?.Invoke(timesTalked);
            });
        }

        private Dialogue NextDialogue()
        {
            if (timesTalked == 0 && firstMeeting != null) return firstMeeting;
            if (repeatDialogues == null || repeatDialogues.Length == 0) return firstMeeting;

            int index = Mathf.Min(timesTalked - (firstMeeting != null ? 1 : 0), repeatDialogues.Length - 1);
            return repeatDialogues[Mathf.Max(0, index)];
        }

        private bool InRange() =>
            player != null && Vector2.Distance(player.position, transform.position) <= talkRadius;

        private void SetPromptAlpha(float alpha)
        {
            if (prompt == null) return;
            prompt.alpha = alpha;
            prompt.gameObject.SetActive(alpha > 0.001f);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.9f, 0.5f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, talkRadius);
        }
#endif
    }
}
