using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;

namespace BozjaPilot.Core;

public static class Vec
{
    public static float Distance2D(Vector3 a, Vector3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    public static bool IsZero(this Vector3 v) => v.X == 0f && v.Y == 0f && v.Z == 0f;
}

/// <summary>
///     A single DynamicEvent as we currently understand it. Snapshot, not a live view: the
///     scanner refreshes it a few times per second and everything else reads the snapshot.
/// </summary>
public sealed class Activity
{
    public ushort Id { get; init; }
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     Bozja CEs always start with a registration window ("报名"), skirmishes never do.
    ///     We therefore treat "was seen while <see cref="DynamicEventState.Register" />" as the
    ///     authoritative CE flag and remember it for the rest of the event's life.
    /// </summary>
    public bool IsCriticalEngagement { get; set; }

    public Vector3 Position { get; set; }
    public float Radius { get; set; }
    public DynamicEventState State { get; set; }
    public float SecondsLeft { get; set; }
    public float Duration { get; set; }
    public int Participants { get; set; }
    public int Progress { get; set; }

    public DateTime FirstSeen { get; init; }
    public DateTime LastSeen { get; set; }

    public bool RegisteredByMe { get; set; }
    public float Distance { get; set; }
    public float Eta { get; set; }
    public float Score { get; set; }
    public bool Blacklisted { get; set; }
    public DateTime BlacklistedUntil { get; set; }

    public bool IsRunning => State is DynamicEventState.Register
        or DynamicEventState.Warmup
        or DynamicEventState.Battle;

    public bool IsRegistrationOpen => State == DynamicEventState.Register;

    public bool IsFighting => State == DynamicEventState.Battle;

    public float RemainingAfterArrival => SecondsLeft - Eta;

    public override string ToString() =>
        $"#{Id} {Name} {(IsCriticalEngagement ? "CE" : "SM")} {State} left={SecondsLeft:F0}s dist={Distance:F0} eta={Eta:F0}s pos=({Position.X:F1},{Position.Y:F1},{Position.Z:F1}) r={Radius:F1}";
}
