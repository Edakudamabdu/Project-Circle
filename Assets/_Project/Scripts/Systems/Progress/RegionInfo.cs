using ProjectCircle.Player;
using ProjectCircle.Systems.Story;
using ProjectCircle.Systems.Audio;
using UnityEngine;

namespace ProjectCircle.Systems
{
    /// <summary>
    /// One per region scene. Names the region (shown on entering it), and says what the circle has when the
    /// region is played straight from the editor (so a designer can press Play in any region and test it
    /// without walking there). When the circle arrives through a door, the run's real progress
    /// (<see cref="GameProgress"/>) is used instead and the dev settings below are ignored.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-200)] // before PlayerAbilities reads it
    public sealed class RegionInfo : MonoBehaviour
    {
        [Tooltip("Shown on screen when the circle enters (leave empty for none).")]
        [SerializeField] private string regionName = "Region";

        [SerializeField] private bool showNameOnEnter = true;

        [Header("Ambience")]
        [Tooltip("Background loop for this region (Audio/Ambience). Regions sharing a loop keep it playing through doors.")]
        [SerializeField] private AudioClip ambience;

        [SerializeField, Range(0f, 1f)] private float ambienceVolume = 0.35f;

        [Header("Dev start (only when this scene is the first one loaded)")]
        [Tooltip("Wear the cape when playing this region directly.")]
        [SerializeField] private bool devCape = true;

        [Tooltip("Abilities when playing this region directly: what the circle would have by the time it gets here.")]
        [SerializeField] private PlayerAbility devAbilities = PlayerAbility.Attack;

        [Tooltip("Story flags when playing this region directly (e.g. Seal.Warden).")]
        [SerializeField] private string[] devFlags = new string[0];

        [Header("Notes for the level designer")]
        [TextArea(3, 8)]
        [SerializeField] private string notes = "";

        public string RegionName => regionName;

        /// <summary>The designer notes (for tools; not shown in game).</summary>
        public string Notes => notes;

        private void Awake()
        {
            // First scene of the run: start it with this region's dev settings.
            GameProgress.Begin(devCape, devAbilities, devFlags);
        }

        private void Start()
        {
            Sfx.SetAmbience(ambience, ambienceVolume);
            if (showNameOnEnter && !string.IsNullOrEmpty(regionName) && CinematicOverlay.Instance != null)
                CinematicOverlay.Instance.ShowAreaName(regionName.ToUpperInvariant(), 2f);
        }
    }
}
