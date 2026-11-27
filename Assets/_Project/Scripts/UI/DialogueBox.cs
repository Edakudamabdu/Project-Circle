using System;
using System.Collections;
using ProjectCircle.Player;
using ProjectCircle.Systems;
using TMPro;
using ProjectCircle.Systems.Audio;
using UnityEngine;

namespace ProjectCircle.UI
{
    /// <summary>
    /// The on-screen dialogue box. <see cref="Play"/> shows a <see cref="Dialogue"/> one page at a time:
    /// text types itself out; pressing Interact, Jump or Attack finishes the page, then turns to the next.
    /// The circle can't move while the box is open.
    ///
    /// Put one in any scene that has conversations (a Canvas child with a CanvasGroup on its root).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class DialogueBox : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TMP_Text speakerText;
        [SerializeField] private TMP_Text bodyText;

        [Tooltip("Shown when a page has finished typing: press to continue.")]
        [SerializeField] private RectTransform continueMarker;

        [Header("Feel")]
        [Tooltip("Letters typed per second.")]
        [SerializeField, Min(1f)] private float lettersPerSecond = 45f;

        [Tooltip("Extra pause after . , ! ? and … so the text breathes.")]
        [SerializeField, Min(0f)] private float punctuationPause = 0.08f;

        [SerializeField, Min(0.01f)] private float fadeTime = 0.15f;

        [Tooltip("Presses are ignored this long after the box opens, so the press that started talking can't skip a page.")]
        [SerializeField, Min(0f)] private float inputGraceTime = 0.15f;

        /// <summary>True from the moment a conversation starts until the box has closed.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>Unscaled time the box last finished closing (so other menus can ignore that same press).</summary>
        public float LastClosedTime { get; private set; } = float.NegativeInfinity;

        private CanvasGroup group;
        private PlayerInputReader input;
        private Dialogue current;
        private Action onFinished;
        private int page;
        private bool typing;
        private bool skipRequested;
        private float openedTime;
        private Coroutine typeRoutine;
        private Vector2 markerRestPosition;

        #region Unity lifecycle

        private void Awake()
        {
            group = GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            if (continueMarker != null) markerRestPosition = continueMarker.anchoredPosition;
        }

        private void Start()
        {
            input = FindAnyObjectByType<PlayerInputReader>();
            if (input != null) input.ConfirmPressed += HandleConfirm;
        }

        private void OnDestroy()
        {
            if (input != null) input.ConfirmPressed -= HandleConfirm;
        }

        private void Update()
        {
            if (continueMarker == null) return;

            // Bob the marker while waiting for a press.
            bool waiting = IsOpen && !typing;
            continueMarker.gameObject.SetActive(waiting);
            if (waiting) continueMarker.anchoredPosition = markerRestPosition + Vector2.up * (Mathf.Sin(Time.unscaledTime * 6f) * 4f);
        }

        #endregion

        /// <summary>
        /// Starts a conversation. <paramref name="finished"/> is called once the last page has been read.
        /// Ignored if a conversation is already open.
        /// </summary>
        public void Play(Dialogue dialogue, Action finished = null)
        {
            if (IsOpen || dialogue == null || dialogue.Lines.Count == 0) return;

            current = dialogue;
            onFinished = finished;
            page = 0;
            IsOpen = true;
            openedTime = Time.unscaledTime;
            if (input != null) input.AddControlLock(this);

            speakerText.text = dialogue.Speaker;
            StopAllCoroutines();
            StartCoroutine(Fade(1f));
            Sfx.Play(SoundId.DialogueOpen);
            ShowPage();
        }

        private void HandleConfirm()
        {
            if (!IsOpen || Time.unscaledTime < openedTime + inputGraceTime) return;

            if (typing)
            {
                skipRequested = true; // show the rest of this page at once
                return;
            }

            page++;
            Sfx.Play(SoundId.MenuMove, 0.6f);
            if (page < current.Lines.Count) ShowPage();
            else Close();
        }

        private void ShowPage()
        {
            if (typeRoutine != null) StopCoroutine(typeRoutine);
            typeRoutine = StartCoroutine(TypePage(current.Lines[page]));
        }

        /// <summary>Reveals the page letter by letter (using TMP's visible-character count, so the layout never jumps).</summary>
        private IEnumerator TypePage(string text)
        {
            typing = true;
            skipRequested = false;
            bodyText.text = text;
            bodyText.maxVisibleCharacters = 0;
            bodyText.ForceMeshUpdate();
            int total = bodyText.textInfo.characterCount;

            for (int shown = 0; shown < total && !skipRequested; shown++)
            {
                bodyText.maxVisibleCharacters = shown + 1;
                char c = bodyText.textInfo.characterInfo[shown].character;
                float wait = 1f / lettersPerSecond;
                if (c == '.' || c == ',' || c == '!' || c == '?' || c == '…') wait += punctuationPause;
                if (shown % 2 == 0 && !char.IsWhiteSpace(c)) Sfx.Play(SoundId.DialogueTick); // a soft tick every other letter
                yield return new WaitForSecondsRealtime(wait);
            }

            bodyText.maxVisibleCharacters = int.MaxValue;
            typing = false;
            typeRoutine = null;
        }

        private void Close()
        {
            StartCoroutine(CloseRoutine());
        }

        private IEnumerator CloseRoutine()
        {
            yield return Fade(0f);

            // Hand control back a frame later, so the press that closed the box doesn't also jump or attack.
            yield return null;
            IsOpen = false;
            LastClosedTime = Time.unscaledTime;
            current = null;
            if (input != null) input.RemoveControlLock(this);

            Action finished = onFinished;
            onFinished = null;
            finished?.Invoke();
        }

        private IEnumerator Fade(float target)
        {
            group.blocksRaycasts = target > 0.5f;
            float start = group.alpha;
            for (float t = 0f; t < fadeTime; t += Time.unscaledDeltaTime)
            {
                group.alpha = Mathf.Lerp(start, target, t / fadeTime);
                yield return null;
            }
            group.alpha = target;
        }
    }
}
