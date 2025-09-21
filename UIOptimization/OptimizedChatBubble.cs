using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using DailyRoutines.Abstracts;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.Text;

namespace DailyRoutines.ModulesPublic;

public unsafe class OptimizedChatBubble : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = GetLoc("OptimizedChatBubbleTitle"),
        Description = GetLoc("OptimizedChatBubbleDescription"),
        Category = ModuleCategories.UIOptimization,
        Author = ["Middo","Xww"]
    };

    private static readonly CompSig ChatBubbleSig = new("E8 ?? ?? ?? ?? 0F B6 E8 48 8D 5F 18 40 0A 6C 24 ?? BE");
    private delegate ulong ChatBubbleDelegate(ChatBubbleStruct* chatBubbleStruct);
    private static Hook<ChatBubbleDelegate> ChatBubbleHook;

    private static readonly CompSig SetupChatBubbleSig = new("E8 ?? ?? ?? ?? 49 FF 46 60");
    private delegate byte SetupChatBubbleDelegate(nint unk, nint newBubble, nint a3);
    private static Hook<SetupChatBubbleDelegate> SetupChatBubbleHook;

    private static readonly CompSig GetStringSizeSig = new("E8 ?? ?? ?? ?? 49 8D 56 40");
    private delegate uint GetStringSizeDelegate(TextChecker* textChecker, Utf8String* str);
    private static readonly GetStringSizeDelegate GetStringSize = GetStringSizeSig.GetDelegate<GetStringSizeDelegate>();

    private static readonly MemoryPatch ShowMiniTalkPlayerPatch = new("0F 84 ?? ?? ?? ?? ?? ?? ?? 48 8B CF 49 89 46", [0x90, 0xE9]);

    private static Config ModuleConfig = null!;

    private static readonly HashSet<nint> newBubbles = [];

    protected override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();

        ChatBubbleHook = ChatBubbleSig.GetHook<ChatBubbleDelegate>(ChatBubbleDetour);
        ChatBubbleHook.Enable();

        SetupChatBubbleHook = SetupChatBubbleSig.GetHook<SetupChatBubbleDelegate>(SetupChatBubbleDetour);
        SetupChatBubbleHook.Enable();

        ShowMiniTalkPlayerPatch.Set(ModuleConfig.IsShowInCombat);
    }

    protected override void ConfigUI()
    {
        if (ImGui.Checkbox(GetLoc("OptimizedChatBubble-IsShowInCombat"), ref ModuleConfig.IsShowInCombat))
        {
            SaveConfig(ModuleConfig);
            ShowMiniTalkPlayerPatch.Set(ModuleConfig.IsShowInCombat);
        }

        using (ImRaii.ItemWidth(80f * GlobalFontScale))
        using (ImRaii.PushIndent())
        {
            ImGui.InputInt(GetLoc("OptimizedChatBubble-MaxLines"), ref ModuleConfig.MaxLines, 1);
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                ModuleConfig.MaxLines = Math.Clamp(ModuleConfig.MaxLines, 1, 7);
                SaveConfig(ModuleConfig);
            }

            var timeSeconds = ModuleConfig.Duration / 1000f;
            ImGui.InputFloat(GetLoc("OptimizedChatBubble-Duration"), ref timeSeconds, 0.1f, 1f, "%.1f");
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                ModuleConfig.Duration = Math.Clamp((int)MathF.Round(timeSeconds * 10), 10, 600) * 100;
                SaveConfig(ModuleConfig);
            }

            ImGui.InputInt(GetLoc("OptimizedChatBubble-AddDurationPerCharacter"), ref ModuleConfig.AddDurationPerCharacter, 1, 10);
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
                    var characterCounts = GetStringSize(&RaptureTextModule.Instance()->TextChecker, &bubble->String);
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

    protected override void Uninit() => ShowMiniTalkPlayerPatch.Dispose();

    [StructLayout(LayoutKind.Explicit)]
    private struct ChatBubbleStruct
    {
        [FieldOffset(0x8C)] public byte LineCount;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct ChatBubbleEntry
    {
        [FieldOffset(0x000)] public Utf8String String;
        [FieldOffset(0x1B8)] public long Timestamp;
    }

    private class Config : ModuleConfiguration
    {
        public bool IsShowInCombat          = false;
        public int  MaxLines                = 2;
        public int  Duration                = 4000;
        public int  AddDurationPerCharacter;
    }
}
