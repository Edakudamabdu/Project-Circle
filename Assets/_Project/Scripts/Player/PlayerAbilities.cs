using System;
using UnityEngine;

namespace ProjectCircle.Player
{
    /// <summary>The circle's unlockable moves. Walking and jumping are always available.</summary>
    [Flags]
    public enum PlayerAbility
    {
        None = 0,
        Dash = 1 << 0,
        Attack = 1 << 1,
        AirJump = 1 << 2,
        Parry = 1 << 3,
        All = Dash | Attack | AirJump | Parry,
    }

    /// <summary>
    /// What the circle can do right now, and whether it wears the cape.
    ///
    /// In the opening the circle has no cape (and so no attack, air jump or parry) until the strings in the
    /// Hall of Strings form it. Everywhere else it starts with everything, so test scenes need no setup.
    /// <see cref="PlayerMovement"/>, <see cref="PlayerAttack"/> and <see cref="PlayerParry"/> ask
    /// <see cref="Has"/> before doing a move.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAbilities : MonoBehaviour
    {
        [Tooltip("Wear the cape from the start. The opening turns this off from code.")]
        [SerializeField] private bool startWithCape = true;

        [Tooltip("Moves available from the start.")]
        [SerializeField] private PlayerAbility startingAbilities = PlayerAbility.All;

        [Tooltip("The cape (child object). Hidden while the circle has no cape.")]
        [SerializeField] private PlayerCape cape;

        /// <summary>True once the circle wears the cape.</summary>
        public bool HasCape { get; private set; }

        /// <summary>Moves currently unlocked.</summary>
        public PlayerAbility Unlocked { get; private set; }

        /// <summary>Raised when the cape is given (the Hall of Strings).</summary>
        public event Action CapeGained;

        private Renderer capeRenderer;

        private void Awake()
        {
            if (cape == null) cape = GetComponentInChildren<PlayerCape>(true);
            if (cape != null) capeRenderer = cape.GetComponent<Renderer>();

            HasCape = startWithCape;
            Unlocked = startingAbilities;
        }

        private void LateUpdate()
        {
            // Other systems (e.g. the hurt flicker) toggle the circle's renderers, so keep the cape hidden here.
            if (!HasCape && capeRenderer != null && capeRenderer.enabled) capeRenderer.enabled = false;
        }

        /// <summary>True if every move in <paramref name="ability"/> is unlocked.</summary>
        public bool Has(PlayerAbility ability) => (Unlocked & ability) == ability;

        /// <summary>Takes the cape and every move away (the start of the opening).</summary>
        public void RemoveCape()
        {
            HasCape = false;
            Unlocked = PlayerAbility.None;
            if (capeRenderer != null) capeRenderer.enabled = false;
        }

        /// <summary>Gives the cape and unlocks <paramref name="abilities"/>.</summary>
        public void GiveCape(PlayerAbility abilities)
        {
            HasCape = true;
            Unlocked |= abilities;
            if (cape != null) cape.ResetCape();
            if (capeRenderer != null) capeRenderer.enabled = true;
            CapeGained?.Invoke();
        }
    }
}
