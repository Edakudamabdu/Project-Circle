using ProjectCircle.Player;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectCircle.UI
{
    /// <summary>
    /// The Threads meter under the health bar: one slot per storable heal (<see cref="PlayerMend"/>). Each slot
    /// fills as the circle lands hits and parries; a full slot glows. While mending, the slot being spent pulses.
    /// Built in code (nothing to place in the scene).
    /// </summary>
    public sealed class ThreadMeter : MonoBehaviour
    {
        private const float SlotWidth = 70f;
        private const float SlotHeight = 10f;
        private const float Gap = 10f;

        private static ThreadMeter instance;

        private PlayerMend mend;
        private RectTransform root;
        private Image[] frames = new Image[0];
        private RectTransform[] fills = new RectTransform[0];
        private Image[] fillImages = new Image[0];

        private static readonly Color FrameColor = new Color(0.3f, 0.29f, 0.34f, 0.9f); // visible even when empty
        private static readonly Color FillColor = new Color(0.85f, 0.78f, 0.6f, 1f);
        private static readonly Color FullColor = new Color(1f, 0.93f, 0.72f, 1f);

        /// <summary>The meter for the current scene (created on first use).</summary>
        public static ThreadMeter Instance
        {
            get
            {
                if (instance == null) instance = Create();
                return instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        /// <summary>Shows <paramref name="playerMend"/>'s Threads.</summary>
        public void Bind(PlayerMend playerMend)
        {
            mend = playerMend;
            BuildSlots(mend.MaxThreads);
        }

        private void Update()
        {
            if (mend == null) return;
            float threads = mend.Threads;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 18f);
            for (int i = 0; i < fills.Length; i++)
            {
                float fill = Mathf.Clamp01(threads - i);
                fills[i].sizeDelta = new Vector2(SlotWidth * fill, SlotHeight);
                bool full = fill >= 1f;
                Color c = full ? FullColor : FillColor * new Color(1f, 1f, 1f, 0.8f);

                // The slot being spent pulses while mending.
                bool spending = mend.IsMending && i == Mathf.FloorToInt(threads - 0.001f);
                if (spending) c = Color.Lerp(c, Color.white, pulse);
                fillImages[i].color = c;
            }
        }

        private void BuildSlots(int count)
        {
            foreach (Transform child in root) Destroy(child.gameObject);
            frames = new Image[count];
            fills = new RectTransform[count];
            fillImages = new Image[count];
            for (int i = 0; i < count; i++)
            {
                float x = i * (SlotWidth + Gap);
                frames[i] = MakeImage("Slot " + i, root, new Vector2(x - 3f, -3f), new Vector2(SlotWidth + 6f, SlotHeight + 6f), FrameColor);
                fillImages[i] = MakeImage("Fill " + i, root, new Vector2(x, 0f), new Vector2(0f, SlotHeight), FillColor);
                fills[i] = fillImages[i].rectTransform;
            }
        }

        private static ThreadMeter Create()
        {
            var go = new GameObject("Thread Meter");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            var meter = go.AddComponent<ThreadMeter>();
            var rootGo = new GameObject("Slots", typeof(RectTransform));
            rootGo.transform.SetParent(go.transform, false);
            meter.root = (RectTransform)rootGo.transform;
            meter.root.anchorMin = meter.root.anchorMax = new Vector2(0f, 1f); // top-left, under the health bar
            meter.root.pivot = new Vector2(0f, 1f);
            meter.root.anchoredPosition = new Vector2(100f, -100f);
            return meter;
        }

        private static Image MakeImage(string name, Transform parent, Vector2 topLeft, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            rect.sizeDelta = size;
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }
    }
}
