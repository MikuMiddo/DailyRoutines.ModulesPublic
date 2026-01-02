using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;
using DailyRoutines.Abstracts;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using KamiToolKit.Nodes;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace DailyRoutines.ModulesPublic;

public unsafe partial class MacroOptimization : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = GetLoc("扩展宏"),
        Description = GetLoc("添加扩展宏窗口"),
        Category = ModuleCategories.UIOptimization,
        Author = ["Middo"]
    };

    private static readonly CompSig                             ResolvePlaceholderSig = new("E8 ?? ?? ?? ?? 33 ED 4C 8B F8");
    private delegate        GameObject*                         ResolvePlaceholderDelegate(PronounModule* module, byte* str, byte a3, byte a4);
    private static          Hook<ResolvePlaceholderDelegate>?   ResolvePlaceholderHook;

    internal static readonly HashSet<string> SupportedCommands     = new(StringComparer.OrdinalIgnoreCase) { "ac", "action", "micon", "macroicon", "wait", "echo", "e", "if", "call", "close" }; // 命令和后缀配置
    internal static readonly HashSet<string> CommandsRequiringArgs = new(StringComparer.OrdinalIgnoreCase) { "ac", "action", "micon", "macroicon", "if", "call" };
    internal static readonly HashSet<string> NonExecutableCommands = new(StringComparer.OrdinalIgnoreCase) { "micon", "macroicon" };
    internal static readonly HashSet<string> SupportedSuffixes     = new(StringComparer.OrdinalIgnoreCase) { "t", "target", "tt", "me", "f", "focus", "ft", "mo", "mouseover", "lowhpmeandmember", "lowhpmember", "lowhpenemy", "deadmember", "nearmember", "farmember", "nearenemy", "farenemy", "dispellablemeandmember", "dispellablemember" };

    internal static readonly List<DRMacroProcessDisplay> StandaloneProcessDisplays     = [];
    internal static readonly Lock                        StandaloneProcessDisplaysLock = new();

    internal static DRMacroExtendDisplay?    MacroExtendAddon;
    internal static DRMacroSettings?         MacroSettingsAddon;
    internal static DRMacroCooldownViewer?   MacroCooldownViewerAddon;
    internal static DRMacroHelp?             MacroHelpAddon;
    private static TextButtonNode?          OpenMacroExtendButton;

    internal static MacroConfig ModuleConfig = null!;

    protected override void Init()
    {
        TaskHelper ??= new() { TimeLimitMS = 15_000 };

        NativeMacroTakeoverManager.Enable();
        ActionRecordingManager.Enable(this);

        ResolvePlaceholderHook = ResolvePlaceholderSig.GetHook<ResolvePlaceholderDelegate>(SmartTargets.ResolvePlaceholderDetour);
        ResolvePlaceholderHook.Enable();

        MacroCacheHelper.InitializeGlobalActionCache();
        MacroCacheHelper.InitializeIconCache();
        MacroCacheHelper.InitializeEmoteCache();

        MacroSettingsAddon ??= new(this)
        {
            InternalName = "DRMacroSettings",
            Title = GetLoc("宏设置"),
            Size = new Vector2(300.0f, 320.0f),
            RememberClosePosition = true
        };

        MacroHelpAddon ??= new()
        {
            InternalName = "DRMacroHelp",
            Title = GetLoc("扩展宏帮助窗口"),
            Size = new Vector2(780.0f, 520.0f),
            RememberClosePosition = true
        };

        MacroCooldownViewerAddon ??= new(this)
        {
            InternalName = "DRMacroCooldownViewer",
            Title = GetLoc("技能冷却时间"),
            Size = new Vector2(480.0f, 530.0f),
            RememberClosePosition = true
        };

        MacroExtendAddon ??= new(this, TaskHelper, MacroSettingsAddon)
        {
            InternalName = "DRMacroExtendDisplay",
            Title = GetLoc("扩展宏窗口"),
            Size = new Vector2(915.0f, 650.0f),
            RememberClosePosition = true
        };

        DService.AddonLifecycle.RegisterListener(AddonEvent.PostDraw,    "Macro", OnMacroAddon);
        DService.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "Macro", OnMacroAddon);

        ModuleConfig = LoadConfig<MacroConfig>() ?? new();
        if (!File.Exists(ConfigFilePath))
            SaveConfig(ModuleConfig);
    }

    private static void OnMacroAddon(AddonEvent type, AddonArgs? args)
    {
        switch (type)
        {
            case AddonEvent.PostDraw:
                var macroAddon = InfosOm.Macro;
                if (macroAddon == null) return;

                if (OpenMacroExtendButton == null)
                {
                    OpenMacroExtendButton = new TextButtonNode
                    {
                        Size = new(150f, 30f),
                        IsVisible = true,
                        String = GetLoc("打开扩展宏窗口"),
                        OnClick = () => MacroExtendAddon?.Toggle(),
                        Position = new(160, 515)
                    };
                    OpenMacroExtendButton.AttachNode(macroAddon->RootNode);
                }
                break;

            case AddonEvent.PreFinalize:
                OpenMacroExtendButton?.DetachNode();
                OpenMacroExtendButton = null;
                break;
        }
    }

    public static void OpenStandaloneMacroWindowByIndex(int macroIndex, int? loopCount = null)
    {
        if (macroIndex < 0 || macroIndex >= ModuleConfig.ExtendMacroLists.Count)
            return;

        var macro = ModuleConfig.ExtendMacroLists[macroIndex];

        var processDisplay = new DRMacroProcessDisplay
        {
            InternalName = "DRMacroProcessDisplay",
            Title = macro.Name,
            Size = new(300, 430),
            RememberClosePosition = false
        };
        lock (StandaloneProcessDisplaysLock)
            StandaloneProcessDisplays.Add(processDisplay);
        processDisplay.OpenWithExtendMacro(macro, loopCount);
    }

    public static void OpenStandaloneMacroWindowByName(string macroName, int? loopCount = null)
    {
        var macroIndex = ModuleConfig.ExtendMacroLists.FindIndex(m =>
            string.Equals(m.Name, macroName, StringComparison.OrdinalIgnoreCase));

        if (macroIndex < 0)
        {
            ChatManager.SendCommand($"/e 未找到宏: {macroName}");
            return;
        }

        OpenStandaloneMacroWindowByIndex(macroIndex, loopCount);
    }

    protected override void Uninit()
    {
        NativeMacroTakeoverManager.Disable();
        ActionRecordingManager.Disable();

        ResolvePlaceholderHook?.Disable();
        ResolvePlaceholderHook?.Dispose();
        ResolvePlaceholderHook = null;

        DService.AddonLifecycle.UnregisterListener(OnMacroAddon);
        OnMacroAddon(AddonEvent.PreFinalize, null);

        MacroExtendAddon?.Dispose();
        MacroExtendAddon = null;

        MacroSettingsAddon?.Dispose();
        MacroSettingsAddon = null;
    }

}

public sealed class MacroConfig : ModuleConfiguration
{
    public List<ExtendMacro>                             ExtendMacroLists { get; set; } = [];
    public Dictionary<ActionType, Dictionary<uint, int>> ActionCooldowns  { get; set; } = [];
}

public sealed class ExtendMacro
{
    public uint   IconID;
    public string Name = string.Empty;
    public string Description = string.Empty;
    public string MacroLines = string.Empty;

    public bool IsLoopEnabled   = false;
    public int  LoopCount       = 1;
    public int  DefaultInterval = 2500;
    public int  CompletionDelay = 1000;
}
