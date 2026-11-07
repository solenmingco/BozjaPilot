using Dalamud.Bindings.ImGui;

namespace BozjaPilot.UI;

public abstract class WindowBase
{
    private readonly string windowName;

    protected WindowBase(string windowName)
    {
        this.windowName = windowName;
    }

    public bool IsOpen { get; set; }

    public void Draw()
    {
        if (!IsOpen) return;

        var open = IsOpen;
        ImGui.SetNextWindowSizeConstraints(new System.Numerics.Vector2(360, 220),
            new System.Numerics.Vector2(1600, 1400));

        if (ImGui.Begin(windowName, ref open))
            DrawContents();

        ImGui.End();
        IsOpen = open;
    }

    protected abstract void DrawContents();
}
