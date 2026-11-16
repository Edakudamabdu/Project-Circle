using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectCircle.Systems.Story
{
    /// <summary>
    /// Screen-space effects for cutscenes, built in code (nothing to place in the scene):
    /// - Letterbox bars (drawn below the dialogue box, so lines stay readable).
    /// - Eyelids that close from the top and bottom (passing out), and a full black screen.
    /// - A title card, an area name banner and a control hint line.
    ///
    /// Everything runs on unscaled time, so a hit-stop can't stall it. One per scene: use <see cref="Instance"/>.
    /// </summary>
    public sealed class CinematicOverlay : MonoBehaviour
    {
        private const float ReferenceHeight = 1080f;

        private static CinematicOverlay instance;

        private RectTransform barTop, barBottom;
        private RectTransform lidTop, lidBottom;
        private Image black;
        private TMP_Text title, areaName, hint;
        private float letterbox;   // 0..1 of the bar height
        private float lidsClosed;  // 0 open .. 1 shut
        private Coroutine hintRoutine, areaRoutine;

        /// <summary>The overlay for the current scene (created on first use).</summary>
        public static CinematicOverlay Instance
        {
            get
            {
                if (instance == null) instance = Create();
                return instance;
            }
        }

        // Enter Play Mode Options skip the domain reload, so statics must be cleared by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        #region Public API

        /// <summary>Slides the letterbox bars in or out.</summary>
        public IEnumerator Letterbox(bool on, float time = 0.5f)
        {
            float from = letterbox, to = on ? 1f : 0f;
            for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
            {
                SetLetterbox(Mathf.SmoothStep(from, to, t / time));
                yield return null;
            }
            SetLetterbox(to);
        }

        /// <summary>Fades the whole screen to black (alpha 1) or back (alpha 0).</summary>
        public IEnumerator FadeBlack(float targetAlpha, float time)
        {
            float from = black.color.a;
            for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
            {
                SetBlack(Mathf.Lerp(from, targetAlpha, t / time));
                yield return null;
            }
            SetBlack(targetAlpha);
        }

        /// <summary>Sets the black screen's alpha at once.</summary>
        public void SetBlack(float alpha)
        {
            black.color = new Color(0f, 0f, 0f, alpha);
            black.enabled = alpha > 0.001f;
        }

        /// <summary>Moves the eyelids to <paramref name="closed"/> (0 open, 1 shut) over <paramref name="time"/>.</summary>
        public IEnumerator Eyelids(float closed, float time)
        {
            float from = lidsClosed;
            for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
            {
                SetLids(Mathf.SmoothStep(from, closed, t / time));
                yield return null;
            }
            SetLids(closed);
        }

        /// <summary>Passing out: the eyes half-close twice, then shut. Ends on a black screen with the lids open.</summary>
        public IEnumerator PassOut()
        {
            yield return Eyelids(0.55f, 0.35f);
            yield return Eyelids(0.2f, 0.3f);
            yield return Eyelids(0.75f, 0.45f);
            yield return Eyelids(0.45f, 0.4f);
            yield return Eyelids(1f, 0.6f);
            SetBlack(1f);
            SetLids(0f);
        }

        /// <summary>Waking up: from black, the eyes open slowly with a blink.</summary>
        public IEnumerator WakeUp()
        {
            SetLids(1f);
            SetBlack(0f);
            yield return Eyelids(0.6f, 0.9f);
            yield return Eyelids(0.85f, 0.35f);
            yield return Eyelids(0f, 1.1f);
        }

        /// <summary>Shows a big centred title, holds it, then fades it out.</summary>
        public IEnumerator ShowTitle(string text, float hold = 2.5f)
        {
            title.text = text;
            if (areaRoutine != null) StopCoroutine(areaRoutine); // the title has the screen to itself
            areaName.alpha = 0f;
            yield return FadeText(title, 1f, 1f);
            yield return new WaitForSecondsRealtime(hold);
            yield return FadeText(title, 0f, 1f);
        }

        /// <summary>Shows a small area name near the top of the screen for a few seconds.</summary>
        public void ShowAreaName(string text, float hold = 2.5f)
        {
            if (areaRoutine != null) StopCoroutine(areaRoutine);
            areaRoutine = StartCoroutine(AreaRoutine(text, hold));
        }

        /// <summary>Shows a control hint at the bottom of the screen until <see cref="HideHint"/> (or for <paramref name="seconds"/>).</summary>
        public void ShowHint(string text, float seconds = 0f)
        {
            if (hintRoutine != null) StopCoroutine(hintRoutine);
            hint.text = text;
            hintRoutine = StartCoroutine(HintRoutine(seconds));
        }

        /// <summary>Fades the hint out.</summary>
        public void HideHint()
        {
            if (hintRoutine != null) StopCoroutine(hintRoutine);
            hintRoutine = StartCoroutine(FadeText(hint, 0f, 0.4f));
        }

        /// <summary>A full-screen flash of <paramref name="color"/> that fades out (a parry, a boss dying).</summary>
        public void Flash(Color color, float alpha, float time)
        {
            if (flashRoutine != null) StopCoroutine(flashRoutine);
            flashRoutine = StartCoroutine(FlashRoutine(color, alpha, time));
        }

        /// <summary>The screen edges glow <paramref name="color"/> for a moment (taking damage, a boss's rage).</summary>
        public void EdgePulse(Color color, float alpha, float time)
        {
            if (edgeRoutine != null) StopCoroutine(edgeRoutine);
            edgeRoutine = StartCoroutine(EdgeRoutine(color, alpha, time));
        }

        /// <summary>While on, the screen edges throb faintly red like a heartbeat (last health point).</summary>
        public void SetLowHealth(bool on) => lowHealth = on;

        #endregion

        private Image flash;
        private UI.EdgeVignette edges;
        private Coroutine flashRoutine, edgeRoutine;
        private float edgeAlpha;
        private Color edgeColor = Color.red;
        private bool lowHealth;

        private void Update()
        {
            // Heartbeat: two quick throbs a second, on top of any pulse.
            float beat = 0f;
            if (lowHealth)
            {
                float t = Mathf.Repeat(Time.unscaledTime, 1.1f);
                beat = Mathf.Max(Mathf.Exp(-t * 9f), 0.7f * Mathf.Exp(-Mathf.Abs(t - 0.22f) * 12f)) * 0.35f;
            }
            Color c = edgeAlpha >= beat ? edgeColor : new Color(0.7f, 0.05f, 0.1f, 1f);
            SetEdges(c, Mathf.Max(edgeAlpha, beat));
        }

        private IEnumerator FlashRoutine(Color color, float alpha, float time)
        {
            for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
            {
                float a = alpha * (1f - t / time);
                flash.color = new Color(color.r, color.g, color.b, a);
                flash.enabled = true;
                yield return null;
            }
            flash.enabled = false;
        }

        private IEnumerator EdgeRoutine(Color color, float alpha, float time)
        {
            edgeColor = color;
            for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
            {
                edgeAlpha = alpha * (1f - t / time);
                yield return null;
            }
            edgeAlpha = 0f;
        }

        /// <summary>The edge glow: full colour at the border, fading to nothing towards the middle.</summary>
        private void SetEdges(Color color, float alpha)
        {
            if (edges == null) return;
            edges.color = new Color(color.r, color.g, color.b, alpha * 0.55f);
            edges.enabled = alpha > 0.002f;
        }

        private IEnumerator AreaRoutine(string text, float hold)
        {
            areaName.text = text;
            yield return FadeText(areaName, 1f, 0.8f);
            yield return new WaitForSecondsRealtime(hold);
            yield return FadeText(areaName, 0f, 0.8f);
        }

        private IEnumerator HintRoutine(float seconds)
        {
            yield return FadeText(hint, 1f, 0.4f);
            if (seconds <= 0f) yield break;
            yield return new WaitForSecondsRealtime(seconds);
            yield return FadeText(hint, 0f, 0.4f);
        }

        private static IEnumerator FadeText(TMP_Text text, float target, float time)
        {
            float from = text.alpha;
            for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
            {
                text.alpha = Mathf.Lerp(from, target, t / time);
                yield return null;
            }
            text.alpha = target;
        }

        private void SetLetterbox(float amount)
        {
            letterbox = amount;
            float h = ReferenceHeight * 0.09f * amount;
            barTop.sizeDelta = new Vector2(0f, h);
            barBottom.sizeDelta = new Vector2(0f, h);
        }

        private void SetLids(float closed)
        {
            lidsClosed = closed;
            // Each lid covers half the screen when shut; a soft curve keeps a sliver open until the end.
            float h = ReferenceHeight * 0.5f * closed + 2f;
            lidTop.sizeDelta = new Vector2(0f, h);
            lidBottom.sizeDelta = new Vector2(0f, h);
            lidTop.gameObject.SetActive(closed > 0.001f);
            lidBottom.gameObject.SetActive(closed > 0.001f);
        }

        #region Building

        private static CinematicOverlay Create()
        {
            var root = new GameObject("Cinematic Overlay");
            var overlay = root.AddComponent<CinematicOverlay>();

            // Bars go under the dialogue box (sorting 50); everything else goes over all gameplay UI.
            Transform bars = MakeCanvas("Letterbox Canvas", root.transform, 40);
            Transform top = MakeCanvas("Cinematic Canvas", root.transform, 900);

            overlay.barTop = MakeBar("Bar Top", bars, top: true);
            overlay.barBottom = MakeBar("Bar Bottom", bars, top: false);

            overlay.title = MakeText("Title", top, new Vector2(0.5f, 0.5f), 120f, FontStyles.Bold);
            overlay.title.characterSpacing = 18f;
            overlay.areaName = MakeText("Area Name", top, new Vector2(0.5f, 0.82f), 46f, FontStyles.Normal);
            overlay.areaName.characterSpacing = 8f;
            overlay.hint = MakeText("Hint", top, new Vector2(0.5f, 0.2f), 34f, FontStyles.Normal);
            overlay.hint.color = new Color(0.85f, 0.88f, 0.95f, 1f);

            // Screen-edge glow: one frame that fades smoothly from the border inwards.
            var edgeGo = new GameObject("Edge Glow", typeof(RectTransform));
            edgeGo.transform.SetParent(top, false);
            var edgeRect = (RectTransform)edgeGo.transform;
            edgeRect.anchorMin = Vector2.zero;
            edgeRect.anchorMax = Vector2.one;
            edgeRect.offsetMin = edgeRect.offsetMax = Vector2.zero;
            overlay.edges = edgeGo.AddComponent<UI.EdgeVignette>();
            overlay.edges.raycastTarget = false;
            overlay.edges.Depth = 160f;
            overlay.edges.enabled = false;

            var flashGo = new GameObject("Flash", typeof(RectTransform));
            flashGo.transform.SetParent(top, false);
            var flashRect = (RectTransform)flashGo.transform;
            flashRect.anchorMin = Vector2.zero;
            flashRect.anchorMax = Vector2.one;
            flashRect.offsetMin = flashRect.offsetMax = Vector2.zero;
            overlay.flash = flashGo.AddComponent<Image>();
            overlay.flash.raycastTarget = false;
            overlay.flash.enabled = false;

            overlay.lidTop = MakeBar("Eyelid Top", top, top: true);
            overlay.lidBottom = MakeBar("Eyelid Bottom", top, top: false);

            var blackGo = new GameObject("Black", typeof(RectTransform));
            blackGo.transform.SetParent(top, false);
            var rect = (RectTransform)blackGo.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            overlay.black = blackGo.AddComponent<Image>();
            overlay.black.raycastTarget = false;

            overlay.SetLetterbox(0f);
            overlay.SetLids(0f);
            overlay.SetBlack(0f);
            overlay.title.alpha = overlay.areaName.alpha = overlay.hint.alpha = 0f;
            return overlay;
        }

        private static Transform MakeCanvas(string name, Transform parent, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, ReferenceHeight);
            scaler.matchWidthOrHeight = 1f;
            return go.transform;
        }

        private static RectTransform MakeBar(string name, Transform parent, bool top)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            float y = top ? 1f : 0f;
            rect.anchorMin = new Vector2(0f, y);
            rect.anchorMax = new Vector2(1f, y);
            rect.pivot = new Vector2(0.5f, y);
            rect.anchoredPosition = Vector2.zero;
            var image = go.AddComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = false;
            return rect;
        }


        private static TMP_Text MakeText(string name, Transform parent, Vector2 anchor, float size, FontStyles style)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.sizeDelta = new Vector2(1600f, 200f);
            var text = go.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.color = Color.white;
            return text;
        }

        #endregion
    }
}
