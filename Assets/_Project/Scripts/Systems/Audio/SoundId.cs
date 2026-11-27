namespace ProjectCircle.Systems.Audio
{
    /// <summary>
    /// Every sound effect the game asks for by name. Each one is set up (clips, volume, pitch) in the
    /// <see cref="SoundLibrary"/> asset (Assets/_Project/Resources/SoundLibrary).
    /// The numbers are fixed so the asset keeps its entries if this list is reordered: add new ones at the end.
    /// </summary>
    public enum SoundId
    {
        // Circle
        Swing = 0,
        Jump = 1,
        AirJump = 2,
        Dash = 3,
        Land = 4,
        ParrySnap = 5,
        ParryRing = 6,
        PlayerHurt = 7,
        PlayerDeath = 8,
        Mend = 9,

        // Enemies
        EnemyHit = 20,
        EnemyShatter = 21,
        LaserCharge = 22,
        LaserFire = 23,
        OrbCast = 24,
        Shot = 25,
        Slam = 26,
        Tell = 27,

        // Bosses
        BossLand = 40,
        BossDefeat = 41,
        VertexLink = 42,
        Rebalance = 43,
        PhaseWarp = 44,
        ChargeWhoosh = 45,

        // World
        Gate = 60,
        StringPickup = 61,
        MirrorRest = 62,
        WallCrumble = 63,

        // UI
        DialogueOpen = 80,
        DialogueTick = 81,
        MenuMove = 82,
        MenuConfirm = 83,

        // Opening cutscenes
        Shove = 100,
        PovHit = 101,
        VertexCrackle = 102,
        VertexZap = 103,
        Rumble = 104,
        LightPour = 105,
        StringsGather = 106,
        CapeWrap = 107,
    }
}
