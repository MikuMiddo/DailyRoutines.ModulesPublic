using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using DailyRoutines.Abstracts;
using Dalamud;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.Text;

namespace DailyRoutines.ModulesPublic;

public unsafe class OptimizedBubbleDisplay : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = GetLoc("OptimizedBubbleDisplayTitle"),
        Description = GetLoc("OptimizedBubbleDisplayDescription"),
        Category = ModuleCategories.UIOptimization,
        Author = ["Middo"]
    };

    private static readonly CompSig ChatBubbleSig = new("E8 ?? ?? ?? ?? 0F B6 E8 48 8D 5F 18 40 0A 6C 24 ?? BE");
    private delegate ulong ChatBubbleDelegate(ChatBubbleStruct* chatBubbleStruct);
    private static Hook<ChatBubbleDelegate> ChatBubbleHook;

    private static readonly CompSig SetupChatBubbleSig = new("E8 ?? ?? ?? ?? 49 FF 46 60");
    private delegate byte SetupChatBubbleDelegate(nint unk, nint newBubble, nint a3);
    private static Hook<SetupChatBubbleDelegate> SetupChatBubbleHook;

    private static readonly CompSig GetStringSizeSig = new("E8 ?? ?? ?? ?? 49 8D 56 40");
    private delegate uint GetStringSize(TextChecker* textChecker, Utf8String* str);
    private static GetStringSize getStringSize;

    private static readonly CompSig ShowMiniTalkPlayerSig = new("0F 84 ?? ?? ?? ?? ?? ?? ?? 48 8B CF 49 89 46");
    private nint ShowMiniTalkPlayerAddress = 0;

    private static Config ModuleConfig = null!;

    private readonly HashSet<nint> newBubbles = [];

    protected override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();

        ChatBubbleHook = ChatBubbleSig.GetHook<ChatBubbleDelegate>(ChatBubbleDetour);
        ChatBubbleHook.Enable();

        SetupChatBubbleHook = SetupChatBubbleSig.GetHook<SetupChatBubbleDelegate>(SetupChatBubbleDetour);
        SetupChatBubbleHook.Enable();

        getStringSize = GetStringSizeSig.GetDelegate<GetStringSize>();
        ShowMiniTalkPlayerAddress = ShowMiniTalkPlayerSig.ScanText();

        if (ModuleConfig.IsShowInCombat)
            SafeMemory.WriteBytes(ShowMiniTalkPlayerAddress, [0x90, 0xE9]);
    }

    protected override void ConfigUI()
    {
        if (ImGui.Checkbox(GetLoc("OptimizedBubbleDisplay-IsShowInCombat"), ref ModuleConfig.IsShowInCombat))
        {
            SaveConfig(ModuleConfig);
            if (ModuleConfig.IsShowInCombat)
                SafeMemory.WriteBytes(ShowMiniTalkPlayerAddress, [0x90, 0xE9]);
            else
                SafeMemory.WriteBytes(ShowMiniTalkPlayerAddress, [0x0F, 0x84]);
        }

        using (ImRaii.ItemWidth(80f * GlobalFontScale))
        using (ImRaii.PushIndent())
        {
            ImGui.InputInt(GetLoc("OptimizedBubbleDisplay-MaxLines"), ref ModuleConfig.MaxLines, 1);
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                ModuleConfig.MaxLines = Math.Clamp(ModuleConfig.MaxLines, 1, 7);
                SaveConfig(ModuleConfig);
            }

            var timeSeconds = ModuleConfig.Duration / 1000f;
            ImGui.InputFloat(GetLoc("OptimizedBubbleDisplay-Duration"), ref timeSeconds, 0.1f, 1f, "%.1f");
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                ModuleConfig.Duration = Math.Clamp((int)MathF.Round(timeSeconds * 10), 10, 600) * 100;
                SaveConfig(ModuleConfig);
            }

            ImGui.InputInt(GetLoc("OptimizedBubbleDisplay-AddDurationPerCharacter"), ref ModuleConfig.AddDurationPerCharacter, 1, 10);
            if (ImGui.IsItemDeactivatedAfterEdit())
                SaveConfig(ModuleConfig);
        }
    }

    private ulong ChatBubbleDetour(ChatBubbleStruct* chatBubbleStruct)
    {
        try
        {
            return ChatBubbleHook.Original(chatBubbleStruct);
        }
        finally
        {
            chatBubbleStruct->LineCount = (byte)Math.Clamp(ModuleConfig.MaxLines, 1, 7);

            newBubbles.RemoveWhere(b =>
            {
                var bubble = (ChatBubbleEntry*)b;
                if (bubble->Timestamp < 200)
                {
                    if (bubble->Timestamp >= 0)
                        bubble->Timestamp++;
                    return false;
                }

                bubble->Timestamp += (ModuleConfig.Duration - 4000);
                if (ModuleConfig.AddDurationPerCharacter > 0)
                {
                    var characterCounts = getStringSize(&RaptureTextModule.Instance()->TextChecker, &bubble->String);
                    var additionalDuration = ModuleConfig.AddDurationPerCharacter * Math.Clamp(characterCounts, 0, 194 * ModuleConfig.MaxLines);
                    bubble->Timestamp += additionalDuration;
                }
                return true;
            });
        }
    }

    private byte SetupChatBubbleDetour(nint unk, nint newBubble, nint a3)
    {
        try
        {
            if (ModuleConfig.Duration != 4000 || ModuleConfig.AddDurationPerCharacter > 0)
                newBubbles.Add(newBubble);
            return SetupChatBubbleHook.Original(unk, newBubble, a3);
        }
        catch
        {
            return 0;
        }
    }

    protected override void Uninit()
    {
        SafeMemory.WriteBytes(ShowMiniTalkPlayerAddress, [0x0F, 0x84]);

        ChatBubbleHook.Disable();
        SetupChatBubbleHook.Disable();
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct ChatBubbleStruct
    {
        [FieldOffset(0x8C)] public byte LineCount;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct ChatBubbleEntry
    {
        [FieldOffset(0x000)] public Utf8String String;
        [FieldOffset(0x1B8)] public long Timestamp;
    }

    private class Config : ModuleConfiguration
    {
        public bool IsShowInCombat          = false;
        public int  MaxLines                = 2;
        public int  Duration                = 4000;
        public int  AddDurationPerCharacter = 0;
    }
}
