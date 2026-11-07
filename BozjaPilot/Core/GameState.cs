using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using System.Numerics;

namespace BozjaPilot.Core;

/// <summary>
///     Everything that talks to the game client. All unsafe pointer access lives here so the
///     rest of the plugin stays boring and testable.
/// </summary>
public static unsafe class GameState
{
    private const byte GeneralActionMount = 9;
    private const byte GeneralActionDismount = 23;
    private const byte GeneralActionSprint = 4;

    public static bool IsLoggedIn => Services.ClientState.IsLoggedIn;

    public static bool IsBusy =>
        Services.Condition[ConditionFlag.BetweenAreas]
        || Services.Condition[ConditionFlag.BetweenAreas51]
        || Services.Condition[ConditionFlag.OccupiedInQuestEvent]
        || Services.Condition[ConditionFlag.OccupiedInCutSceneEvent]
        || Services.Condition[ConditionFlag.WatchingCutscene]
        || Services.Condition[ConditionFlag.WatchingCutscene78]
        || Services.Condition[ConditionFlag.Occupied33]
        || Services.Condition[ConditionFlag.TradeOpen];

    public static bool IsDead => Services.Condition[ConditionFlag.Unconscious];

    public static bool InCombat => Services.Condition[ConditionFlag.InCombat];

    public static bool IsMounted => Services.Condition[ConditionFlag.Mounted];

    public static Vector3 PlayerPosition =>
        Services.Objects.LocalPlayer?.Position ?? Vector3.Zero;

    /// <summary>
    ///     The Bozja public content director. Returns null when we are not inside Bozja content.
    ///     This is the only reliable in-zone check: it does not depend on hard-coded territory ids.
    /// </summary>
    public static PublicContentBozja* Director
    {
        get
        {
            var director = PublicContentBozja.GetInstance();
            return director == null ? null : director;
        }
    }

    public static bool IsInBozja
    {
        get
        {
            if (!Services.ClientState.IsLoggedIn) return false;
            return PublicContentBozja.GetState() != null;
        }
    }

    public static DynamicEventContainer* Events
    {
        get
        {
            var director = Director;
            return director == null ? null : &director->DynamicEventContainer;
        }
    }

    /// <summary>
    ///     Mettle ("战果") for the current Bozja session. 0 when unavailable.
    /// </summary>
    public static uint Mettle
    {
        get
        {
            var state = PublicContentBozja.GetState();
            return state == null ? 0u : state->CurrentExperience;
        }
    }

    /// <summary>
    ///     While we are registered for / taking part in a DynamicEvent, our own EventId entry id
    ///     is set to that event's id. This is *the* signal that registration actually worked -
    ///     walking into the circle and hoping is what makes most simple plugins unreliable.
    /// </summary>
    public static ushort RegisteredEventId
    {
        get
        {
            var player = Services.Objects.LocalPlayer;
            if (player == null) return 0;

            var battleChara = (BattleChara*)player.Address;
            return battleChara == null ? (ushort)0 : battleChara->EventId.EntryId;
        }
    }

    public static void Mount()
    {
        if (IsMounted) return;
        ActionManager.Instance()->UseAction(ActionType.GeneralAction, GeneralActionMount);
    }

    public static void Dismount()
    {
        if (!IsMounted) return;
        ActionManager.Instance()->UseAction(ActionType.GeneralAction, GeneralActionDismount);
    }

    public static void Sprint()
    {
        ActionManager.Instance()->UseAction(ActionType.GeneralAction, GeneralActionSprint);
    }

    public static void Jump()
    {
        ActionManager.Instance()->UseAction(ActionType.GeneralAction, 2);
    }

    public static void ReturnToCamp()
    {
        Services.Commands.ProcessCommand("/return");
    }

    public static void ExecuteCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        if (!command.StartsWith('/')) command = "/" + command;
        Services.Commands.ProcessCommand(command);
    }
}
