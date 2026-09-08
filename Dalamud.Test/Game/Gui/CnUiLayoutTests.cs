using System.Runtime.InteropServices;

using FFXIVClientStructs.FFXIV.Client.UI;

using Xunit;

namespace Dalamud.Test.Game.Gui;

// CN 2026.09.01: verified against native module getters and constructor/destructor access.
// Signature resolution alone cannot detect these embedded-structure offset regressions.
public sealed class CnUiLayoutTests
{
    [Theory]
    [InlineData("AddonNames", 0x12348)]
    [InlineData("UIModulePtr", 0x12418)]
    [InlineData("AgentModule", 0x12428)]
    [InlineData("RaptureAtkUnitManager", 0x13450)]
    [InlineData("RaptureAtkColorDataManager", 0x1D180)]
    public void RaptureAtkModuleOffsets(string field, int expected)
        => Assert.Equal(expected, Marshal.OffsetOf<RaptureAtkModule>(field).ToInt32());

    [Theory]
    [InlineData("ConfigModule", 0xAAE40)]
    [InlineData("UI3DModule", 0xBB150)]
    [InlineData("RaptureAtkModule", 0xD2690)]
    [InlineData("InfoModule", 0xFD000)]
    [InlineData("UIModuleHelpers", 0xFEC78)]
    [InlineData("UIInputData", 0xFF020)]
    [InlineData("UIInputModule", 0xFFA50)]
    public void UIModuleOffsets(string field, int expected)
        => Assert.Equal(expected, Marshal.OffsetOf<UIModule>(field).ToInt32());

    [Fact]
    public void EmbeddedAtkModuleEndsAtInfoModule()
        => Assert.Equal(
            Marshal.OffsetOf<UIModule>("InfoModule").ToInt32(),
            Marshal.OffsetOf<UIModule>("RaptureAtkModule").ToInt32() + Marshal.SizeOf<RaptureAtkModule>());
}
