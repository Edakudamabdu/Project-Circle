using System;
using System.Collections;
using ProjectCircle.Player;
using TMPro;
using UnityEngine;

namespace ProjectCircle.UI
{
    /// <summary>
    /// The small menu shown while the circle stands at a Mirror: the Mirror's name and a vertical list of
    /// options (Rest, Reflect, Leave). Up/Down picks an option, Interact/Jump/Attack chooses it.
    ///
    /// It only shows and reports choices; the <see cref="Systems.MirrorCheckpoint"/> decides what they do.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class MirrorMenu : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TMP_Text titleText;

        [Tooltip("One text per option, top to bottom.")]
        [SerializeField] private TMP_Text[] optionTexts;

        [Tooltip("Marker placed beside the selected option.")]
        [SerializeField] private RectTransform selectionMarker;

        [Tooltip("Short message under the options (e.g. 'Rested').")]
        [SerializeField] private TMP_Text statusText;

        [Header("Look")]
        [SerializeField] private Color normalColor = new Color(0.7f, 0.74f, 0.82f, 1f);
        [SerializeField] private Color selectedColor = new Color(1f, 0.92f, 0.7f, 1f);
        [SerializeField, Min(0.01f)] private float fadeTime = 0.2f;

        [Tooltip("Stick/keys must pass this to move the selection.")]
        [SerializeField, Range(0.1f, 0.9f)] private float navigateThreshold = 0.5f;

        /// <summary>Raised when an option is chosen. Argument: its index (0 = top).</summary>
        public event Action<int> OptionChosen;

        public bool IsOpen { get; private set; }

        /// <summary>While false the menu stays visible but ignores input (e.g. while thoughts are shown).</summary>
        public bool Interactable { get; set; } = true;

        private CanvasGroup group;
        private PlayerInputReader input;
        private int selected;
        private bool navigationHeld;
        private float openedTime;
        private float statusUntil;
        private Coroutine fadeRoutine;

        private void Awake()
        {
            group = GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            if (statusText != null) statusText.text = "";
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

        /// <summary>Shows the menu with <paramref name="title"/>, selection on the first option.</summary>
        public void Open(string title)
        {
            if (titleText != null) titleText.text = title;
            selected = 0;
            navigationHeld = true; // ignore a direction already held when it opens
            openedTime = Time.unscaledTime;
            IsOpen = true;
            Interactable = true;
            RefreshSelection();
            FadeTo(1f);
        }

        public void Close()
        {
            IsOpen = false;
            FadeTo(0f);
        }

        /// <summary>Shows a short message under the options for a few seconds.</summary>
        public void ShowStatus(string message, float seconds = 2f)
        {
            if (statusText == null) return;
            statusText.text = message;
            statusUntil = Time.unscaledTime + seconds;
        }

        private void Update()
        {
            if (statusText != null && statusText.text.Length > 0)
            {
                float left = statusUntil - Time.unscaledTime;
                statusText.alpha = Mathf.Clamp01(left / 0.4f);
                if (left <= 0f) statusText.text = "";
            }

            if (!IsOpen || !Interactable || input == null) return;

            // Move the selection once per push of the stick / key.
            float y = input.RawMove.y;
            if (Mathf.Abs(y) < navigateThreshold * 0.5f)
            {
                navigationHeld = false;
            }
            else if (!navigationHeld && Mathf.Abs(y) >= navigateThreshold)
            {
                navigationHeld = true;
                int count = optionTexts.Length;
                selected = (selected + (y > 0f ? -1 : 1) + count) % count;
                RefreshSelection();
            }

            if (selectionMarker != null && optionTexts.Length > 0)
            {
                // A gentle pulse so the marker reads as "choose".
                float s = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 5f);
                selectionMarker.localScale = new Vector3(s, s, 1f);
            }
        }

        private void HandleConfirm()
        {
            if (!IsOpen || !Interactable || Time.unscaledTime < openedTime + 0.2f) return;
            OptionChosen?.Invoke(selected);
        }

        private void RefreshSelection()
        {
            for (int i = 0; i < optionTexts.Length; i++)
            {
                if (optionTexts[i] != null) optionTexts[i].color = i == selected ? selectedColor : normalColor;
            }

            if (selectionMarker != null && selected < optionTexts.Length && optionTexts[selected] != null)
            {
                RectTransform option = optionTexts[selected].rectTransform;
                selectionMarker.position = new Vector3(selectionMarker.position.x, option.position.y, selectionMarker.position.z);
            }
        }

        private void FadeTo(float target)
        {
            if (fadeRoutine != null) StopCoroutine(fadeRoutine);
            fadeRoutine = StartCoroutine(Fade(target));
        }

        private IEnumerator Fade(float target)
        {
            float start = group.alpha;
            for (float t = 0f; t < fadeTime; t += Time.unscaledDeltaTime)
            {
                group.alpha = Mathf.Lerp(start, target, t / fadeTime);
                yield return null;
            }
            group.alpha = target;
            fadeRoutine = null;
        }
    }
}
