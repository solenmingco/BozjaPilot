using Dalamud.Configuration;

namespace BozjaPilot;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // ---- master switch -----------------------------------------------------
    /// <summary>Automation master switch. Survives reloads on purpose.</summary>
    public bool Enabled;

    // ---- what to run -------------------------------------------------------
    public bool JoinSkirmishes = true;
    public bool JoinCriticalEngagements = true;

    /// <summary>Abandon a skirmish (even mid-fight) when a CE opens registration.</summary>
    public bool LeaveSkirmishForCe = true;

    /// <summary>Only run CEs whose registration window we can still physically reach, plus margin.</summary>
    public float RegisterSafetyMargin = 8f;

    /// <summary>Don't bother starting travel to an activity that will end this soon after we arrive.</summary>
    public float MinTimeOnArrival = 30f;

    /// <summary>Hard cap so a far-away low value target never wins the score.</summary>
    public float MaxTravelDistance = 700f;

    // ---- movement ----------------------------------------------------------
    public bool AutoMount = true;
    public bool AutoSprint = true;
    public float MountDistance = 40f;
    public bool StopMovingInCombat = true;
    public float CombatStopDistance = 12f;

    /// <summary>Seconds of no progress before we try to unstick ourselves.</summary>
    public float StuckTimeout = 3.0f;

    // ---- combat ------------------------------------------------------------
    public bool AutoTarget = true;
    public float TargetSearchRadius = 40f;

    /// <summary>Optional chat command fired once when combat starts, e.g. "/rotation auto".</summary>
    public string CombatStartCommand = string.Empty;

    /// <summary>Optional chat command fired when we leave combat.</summary>
    public string CombatStopCommand = string.Empty;

    // ---- recovery ----------------------------------------------------------
    public bool AutoRelease = true;
    public float WaitForRaiseSeconds = 20f;

    /// <summary>Return to camp when nothing is available for this long (0 = never).</summary>
    public float ReturnWhenIdleSeconds;

    // ---- ui ----------------------------------------------------------------
    public bool ShowOverlay = true;
    public bool ShowActivityList = true;
    public bool DebugLogging;

    /// <summary>DynamicEvent ids that were observed in a registration phase, i.e. real CEs.</summary>
    public List<ushort> KnownCriticalEngagementIds = [];

    /// <summary>Territories where the Bozja public content director was seen, learned at runtime.</summary>
    public List<uint> KnownBozjaTerritories = [];

    public void Save() => Services.PluginInterface.SavePluginConfig(this);
}
