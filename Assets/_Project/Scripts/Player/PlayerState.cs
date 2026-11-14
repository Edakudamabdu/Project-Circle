namespace ProjectCircle.Player
{
    /// <summary>
    /// High-level movement state of the player. Exactly one is active at a time.
    /// Owned by <see cref="PlayerMovement"/>; other systems (visuals, audio, UI, combat) read
    /// <see cref="PlayerMovement.State"/> or subscribe to <see cref="PlayerMovement.StateChanged"/>.
    /// </summary>
    public enum PlayerState
    {
        /// <summary>On the ground, not moving.</summary>
        Idle,

        /// <summary>On the ground, moving or trying to move.</summary>
        Run,

        /// <summary>In the air, moving upward.</summary>
        Jump,

        /// <summary>In the air, moving downward (after a jump or after leaving a ledge).</summary>
        Fall,

        /// <summary>Quickstep dash in progress.</summary>
        Dash,

        /// <summary>Knocked back after taking a hit; controls are briefly ignored.</summary>
        Hurt
    }
}
