using System.Reflection;
using System.Runtime.CompilerServices;
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
        => Assert.Equal(expected, OffsetOf<RaptureAtkModule>(field));

    [Theory]
    [InlineData("ConfigModule", 0xAAE40)]
    [InlineData("UI3DModule", 0xBB150)]
    [InlineData("RaptureAtkModule", 0xD2690)]
    [InlineData("InfoModule", 0xFD000)]
    [InlineData("UIModuleHelpers", 0xFEC78)]
    [InlineData("UIInputData", 0xFF020)]
    [InlineData("UIInputModule", 0xFFA50)]
    public void UIModuleOffsets(string field, int expected)
        => Assert.Equal(expected, OffsetOf<UIModule>(field));

    [Fact]
    public void EmbeddedAtkModuleEndsAtInfoModule()
        => Assert.Equal(
            OffsetOf<UIModule>("InfoModule"),
            OffsetOf<UIModule>("RaptureAtkModule") + Unsafe.SizeOf<RaptureAtkModule>());

    // These types contain unmanaged function pointers and do not support Marshal.OffsetOf.
    private static int OffsetOf<T>(string field)
        => typeof(T).GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetCustomAttribute<FieldOffsetAttribute>()!.Value;
}
