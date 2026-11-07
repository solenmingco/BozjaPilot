using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace BozjaPilot;

/// <summary>
///     Thin service locator. Keeps constructor plumbing out of the small pieces that need
///     one or two Dalamud services, without dragging in a full DI container.
/// </summary>
public class Services
{
    [PluginService] public static IPluginLog Log { get; private set; } = null!;
    [PluginService] public static IClientState ClientState { get; private set; } = null!;
    [PluginService] public static IObjectTable Objects { get; private set; } = null!;
    [PluginService] public static ITargetManager Targets { get; private set; } = null!;
    [PluginService] public static ICondition Condition { get; private set; } = null!;
    [PluginService] public static IChatGui Chat { get; private set; } = null!;
    [PluginService] public static ICommandManager Commands { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; private set; } = null!;
    [PluginService] public static IDataManager Data { get; private set; } = null!;
    [PluginService] public static IGameInteropProvider Interop { get; private set; } = null!;
    [PluginService] public static IPartyList Party { get; private set; } = null!;
    [PluginService] public static IKeyState KeyState { get; private set; } = null!;

    public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    public static void Init(IDalamudPluginInterface pluginInterface)
    {
        PluginInterface = pluginInterface;
    }

    public static void Print(string message)
    {
        Chat.Print($"[BozjaPilot] {message}");
    }
}
