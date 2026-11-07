using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace BozjaPilot.Core;

/// <summary>
///     Combat is intentionally thin: we pick targets and let the user's rotation plugin do the
///     actual work. Two things matter here - target selection (so rotation plugins that rely on
///     the current target actually engage) and starting/stopping combat exactly once.
/// </summary>
public sealed class CombatService(Configuration config)
{
    private DateTime lastTargetAttempt = DateTime.MinValue;
    private bool combatStarted;

    public bool Engaged { get; private set; }

    public string? CurrentTargetName { get; private set; }

    public void Engage(Activity activity)
    {
        if (!Engaged)
        {
            Engaged = true;
            if (!combatStarted)
            {
                combatStarted = true;
                GameState.ExecuteCommand(config.CombatStartCommand);
            }
        }

        if (config.AutoTarget) TryTarget(activity);
    }

    public void Disengage()
    {
        if (!Engaged) return;
        Engaged = false;
        CurrentTargetName = null;

        if (combatStarted)
        {
            combatStarted = false;
            GameState.ExecuteCommand(config.CombatStopCommand);
        }
    }

    private void TryTarget(Activity activity)
    {
        var now = DateTime.UtcNow;
        if ((now - lastTargetAttempt).TotalMilliseconds < 250) return;
        lastTargetAttempt = now;

        var current = Services.Targets.Target;
        if (current is { IsDead: false, IsTargetable: true })
        {
            var currentDistance = Vec.Distance2D(current.Position, GameState.PlayerPosition);
            if (currentDistance <= config.TargetSearchRadius)
            {
                CurrentTargetName = current.Name.ToString();
                return;
            }
        }

        var player = GameState.PlayerPosition;
        IBattleNpc? best = null;
        var bestScore = float.MaxValue;

        foreach (var obj in Services.Objects)
        {
            if (obj is not IBattleNpc npc) continue;
            if (npc.IsDead || !npc.IsTargetable) continue;
            if (npc.SubKind != (byte)BattleNpcSubKind.Combatant) continue;

            var distance = Vec.Distance2D(npc.Position, player);
            if (distance > config.TargetSearchRadius) continue;

            if (!IsHostile(npc)) continue;

            // Strongly prefer enemies that belong to the event we are running. In Bozja the
            // event id is carried in the enemy's EventId entry, same as in Occult Crescent.
            var score = distance;
            if (TryGetEventId(npc, out var eventId) && eventId == activity.Id) score -= 100f;

            if (score >= bestScore) continue;
            bestScore = score;
            best = npc;
        }

        if (best == null) return;
        Services.Targets.Target = best;
        CurrentTargetName = best.Name.ToString();
    }

    private static unsafe bool IsHostile(IBattleNpc npc)
    {
        var battleChara = (BattleChara*)npc.Address;
        return battleChara != null && battleChara->IsHostile;
    }

    private static unsafe bool TryGetEventId(IBattleNpc npc, out ushort eventId)
    {
        eventId = 0;
        var battleChara = (BattleChara*)npc.Address;
        if (battleChara == null) return false;
        eventId = battleChara->EventId.EntryId;
        return eventId != 0;
    }
}
