using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Dalamud.Interface.Internal.Windows;

/// <summary>
/// Explains that online branch switching is unavailable in standalone builds.
/// </summary>
public class BranchSwitcherWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BranchSwitcherWindow"/> class.
    /// </summary>
    public BranchSwitcherWindow()
        : base("Branch Switcher", ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.ShowCloseButton = true;
        this.RespectCloseHotkey = true;
    }

    /// <inheritdoc/>
    public override void Draw()
    {
        ImGui.TextWrapped("Standalone builds do not use an online Dalamud release or branch service."u8);
        ImGui.TextWrapped("Select and build the desired local branch outside the game, then inject that local payload."u8);
    }
}
