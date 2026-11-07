using BozjaPilot.Core;
using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace BozjaPilot.UI;

/// <summary>
///     Compact always-on-top readout. Useful when you actually want to watch the fight instead
///     of the main window.
/// </summary>
public sealed class OverlayWindow(Plugin plugin)
{
    private const ImGuiWindowFlags Flags = ImGuiWindowFlags.NoTitleBar
                                           | ImGuiWindowFlags.NoResize
                                           | ImGuiWindowFlags.NoScrollbar
                                           | ImGuiWindowFlags.AlwaysAutoResize
                                           | ImGuiWindowFlags.NoFocusOnAppearing
                                           | ImGuiWindowFlags.NoNav;

    public bool IsOpen = true;

    public void Draw()
    {
        if (!plugin.Config.ShowOverlay || !plugin.Engine.Enabled) return;

        var engine = plugin.Engine;

        ImGui.SetNextWindowBgAlpha(0.65f);
        if (!ImGui.Begin("###BozjaPilotOverlay", ref IsOpen, Flags)) 
        {
            ImGui.End();
            return;
        }

        ImGui.TextColored(Color(engine.State), $"{engine.State}");
        ImGui.SameLine();
        ImGui.TextUnformatted(engine.StatusMessage);
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f),
            $"战果 +{engine.SessionMettleGain:N0}  ({engine.MettlePerHour:N0}/h)   CE {engine.CriticalEngagementsJoined}");

        ImGui.End();
    }

    private static Vector4 Color(AutomationState state) => state switch
    {
        AutomationState.Fighting => new Vector4(0.42f, 0.85f, 0.45f, 1f),
        AutomationState.Registering => new Vector4(0.42f, 0.85f, 0.45f, 1f),
        AutomationState.Travelling => new Vector4(0.96f, 0.82f, 0.35f, 1f),
        AutomationState.Dead => new Vector4(0.94f, 0.35f, 0.35f, 1f),
        AutomationState.Stuck => new Vector4(0.94f, 0.35f, 0.35f, 1f),
        _ => new Vector4(0.7f, 0.7f, 0.7f, 1f)
    };
}
