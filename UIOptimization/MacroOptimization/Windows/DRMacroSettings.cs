using DailyRoutines.Abstracts;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Nodes;

namespace DailyRoutines.ModulesPublic;

public unsafe partial class MacroOptimization
{
    internal sealed class DRMacroSettings(DailyModuleBase instance) : NativeAddon
    {
    private readonly DailyModuleBase Instance = instance;

    private CheckboxNode?     LoopCheckbox;
    private NumericInputNode? LoopCountInputNode;
    private NumericInputNode? IntervalInputNode;
    private NumericInputNode? CompletionDelayInputNode;
    private TextNode?         LoopHintNode;
    private TextNode?         LoopCountLabel;
    private TextNode?         IntervalLabel;
    private TextNode?         CompletionDelayLabel;

    internal int CurrentMacroIndex = -1;

    protected override void OnSetup(AtkUnitBase* addon)
    {
        var initialIsLoopEnabled = false;
        var initialLoopCount = 1;
        var initialInterval = 2500;
        if (CurrentMacroIndex >= 0 && CurrentMacroIndex < ModuleConfig.ExtendMacroLists.Count)
        {
            var macro = ModuleConfig.ExtendMacroLists[CurrentMacroIndex];
            initialIsLoopEnabled = macro.IsLoopEnabled;
            initialLoopCount = macro.LoopCount;
            initialInterval = macro.DefaultInterval;
        }

        var MainContainerNode = new ResNode
        {
            Position = ContentStartPosition,
            Size = ContentSize,
            IsVisible = true,
        };
        MainContainerNode.AttachNode(this);

        var TitleNode = new TextNode
        {
            IsVisible = true,
            Size = new(280, 20),
            Position = new(10f, 0f),
            SeString = "循环设置",
            FontSize = 16,
            AlignmentType = AlignmentType.Left,
            TextColor = KnownColor.White.Vector(),
        };
        TitleNode.AttachNode(MainContainerNode);

        LoopCheckbox = new CheckboxNode
        {
            IsVisible = true,
            Size = new(20, 20),
            Position = new(10f, 30f),
            SeString = "启用循环执行",
            IsChecked = initialIsLoopEnabled,
            OnClick = (isChecked) =>
            {
                if (CurrentMacroIndex < 0 || CurrentMacroIndex >= ModuleConfig.ExtendMacroLists.Count) return;
                if (LoopCountInputNode == null) return;

                ModuleConfig.ExtendMacroLists[CurrentMacroIndex].IsLoopEnabled = isChecked;
                if (LoopCountInputNode.AddButton != null)
                    LoopCountInputNode.AddButton.IsEnabled = isChecked;
                if (LoopCountInputNode.SubtractButton != null)
                    LoopCountInputNode.SubtractButton.IsEnabled = isChecked;

                var alpha = isChecked ? 1.0f : 0.5f;
                if (LoopCountLabel != null)
                    LoopCountLabel.Alpha = alpha;
                if (LoopCountInputNode != null)
                    LoopCountInputNode.Alpha = alpha;
                if (LoopHintNode != null)
                    LoopHintNode.Alpha = alpha;

                ModuleConfig.Save(Instance);
            }
        };
        LoopCheckbox.AttachNode(MainContainerNode);

        LoopCountLabel = new TextNode
        {
            IsVisible = true,
            Size = new(80, 20),
            Position = new(20f, 50f),
            SeString = "循环次数:",
            FontSize = 14,
            AlignmentType = AlignmentType.Left,
            Alpha = initialIsLoopEnabled ? 1.0f : 0.5f,
        };
        LoopCountLabel.AttachNode(MainContainerNode);

        LoopCountInputNode = new NumericInputNode
        {
            IsVisible = true,
            Size = new(120f, 24f),
            Position = new(160f, 47f),
            Value = initialLoopCount,
            Min = 0,
            Max = 9999,
            Step = 1,
            Alpha = initialIsLoopEnabled ? 1.0f : 0.5f,
            OnValueUpdate = (value) =>
            {
                if (CurrentMacroIndex >= 0 && CurrentMacroIndex < ModuleConfig.ExtendMacroLists.Count)
                {
                    ModuleConfig.ExtendMacroLists[CurrentMacroIndex].LoopCount = value;
                    ModuleConfig.Save(Instance);
                }
            }
        };

        if (LoopCountInputNode.AddButton != null)
            LoopCountInputNode.AddButton.IsEnabled = initialIsLoopEnabled;
        if (LoopCountInputNode.SubtractButton != null)
            LoopCountInputNode.SubtractButton.IsEnabled = initialIsLoopEnabled;
        unsafe
        {
            AtkResNode* collisionNode = LoopCountInputNode.CollisionNode;
            if (initialIsLoopEnabled)
                collisionNode->NodeFlags |= NodeFlags.RespondToMouse | NodeFlags.Focusable;
            else
                collisionNode->NodeFlags &= ~(NodeFlags.RespondToMouse | NodeFlags.Focusable);
        }
        LoopCountInputNode.AttachNode(MainContainerNode);

        LoopHintNode = new TextNode
        {
            IsVisible = true,
            Size = new(200f, 20f),
            Position = new(20f, 70f),
            SeString = "提示: 输入 0 表示无限循环",
            FontSize = 12,
            AlignmentType = AlignmentType.Left,
            TextColor = KnownColor.Gray.Vector(),
            Alpha = initialIsLoopEnabled ? 1.0f : 0.5f,
        };
        LoopHintNode.AttachNode(MainContainerNode);

        var IntervalTitleNode = new TextNode
        {
            IsVisible = true,
            Size = new(280, 20),
            Position = new(10f, 100f),
            SeString = "默认间隔设置",
            FontSize = 16,
            AlignmentType = AlignmentType.Left,
            TextColor = KnownColor.White.Vector(),
        };
        IntervalTitleNode.AttachNode(MainContainerNode);

        var ViewCooldownButton = new TextureButtonNode
        {
            IsVisible = true,
            Size = new(20, 20),
            Position = new(110f, 100f),
            TexturePath = "ui/uld/CircleButtons_hr1.tex",
            TextureCoordinates = new(0, 28),
            TextureSize = new(28, 28),
            TextTooltip = "查看录制的冷却时间",
            OnClick = () => MacroCooldownViewerAddon?.Toggle()

        };
        ViewCooldownButton.AttachNode(MainContainerNode);

        IntervalLabel = new TextNode
        {
            IsVisible = true,
            Size = new(120, 20),
            Position = new(20f, 120f),
            SeString = "间隔(毫秒):",
            FontSize = 14,
            AlignmentType = AlignmentType.Left,
        };
        IntervalLabel.AttachNode(MainContainerNode);

        IntervalInputNode = new NumericInputNode
        {
            IsVisible = true,
            Size = new(120f, 24f),
            Position = new(160f, 117f),
            Value = initialInterval,
            Min = 500,
            Max = 10000,
            Step = 100,
            OnValueUpdate = (value) =>
            {
                if (CurrentMacroIndex >= 0 && CurrentMacroIndex < ModuleConfig.ExtendMacroLists.Count)
                {
                    ModuleConfig.ExtendMacroLists[CurrentMacroIndex].DefaultInterval = value;
                    ModuleConfig.Save(Instance);
                }
            }
        };
        IntervalInputNode.AttachNode(MainContainerNode);

        var IntervalHintNode = new TextNode
        {
            IsVisible = true,
            Size = new(260f, 32f),
            Position = new(20f, 140f),
            SeString = "提示: 只对未设置<wait>后缀\n且未被录制过的动作或情感动作、发言等起效",
            FontSize = 12,
            AlignmentType = AlignmentType.Left,
            TextFlags = TextFlags.MultiLine,
            LineSpacing = 14,
            TextColor = KnownColor.Gray.Vector(),
        };
        IntervalHintNode.AttachNode(MainContainerNode);

        CompletionDelayLabel = new TextNode
        {
            IsVisible = true,
            Size = new(160f, 24f),
            Position = new(20f, 185f),
            SeString = "完成后延迟(毫秒):",
            FontSize = 14,
            AlignmentType = AlignmentType.Left
        };
        CompletionDelayLabel.AttachNode(MainContainerNode);

        var initialCompletionDelay = CurrentMacroIndex >= 0 && CurrentMacroIndex < ModuleConfig.ExtendMacroLists.Count
            ? ModuleConfig.ExtendMacroLists[CurrentMacroIndex].CompletionDelay
            : 1000;

        CompletionDelayInputNode = new NumericInputNode
        {
            IsVisible = true,
            Size = new(120f, 24f),
            Position = new(160f, 182f),
            Value = initialCompletionDelay,
            Min = 0,
            Max = 10000,
            Step = 100,
            OnValueUpdate = (value) =>
            {
                if (CurrentMacroIndex >= 0 && CurrentMacroIndex < ModuleConfig.ExtendMacroLists.Count)
                {
                    ModuleConfig.ExtendMacroLists[CurrentMacroIndex].CompletionDelay = value;
                    ModuleConfig.Save(Instance);
                }
            }
        };
        CompletionDelayInputNode.AttachNode(MainContainerNode);

        var CompletionDelayHintNode = new TextNode
        {
            IsVisible = true,
            Size = new(260f, 32f),
            Position = new(20f, 205f),
            SeString = "提示: 宏执行完成后等待多久再重置进度条\n设置为0则立即重置",
            FontSize = 12,
            AlignmentType = AlignmentType.Left,
            TextFlags = TextFlags.MultiLine,
            LineSpacing = 14,
            TextColor = KnownColor.Gray.Vector(),
        };
        CompletionDelayHintNode.AttachNode(MainContainerNode);
    }

    public void OpenWithMacroIndex(int macroIndex)
    {
        if (macroIndex < 0 || macroIndex >= ModuleConfig.ExtendMacroLists.Count) return;

        CurrentMacroIndex = macroIndex;

        Toggle();
    }

    internal void UpdateDisplay()
    {
        if (CurrentMacroIndex < 0 || CurrentMacroIndex >= ModuleConfig.ExtendMacroLists.Count) return;
        if (LoopCheckbox == null || LoopCountInputNode == null || IntervalInputNode == null || CompletionDelayInputNode == null) return;

        var currentMacro = ModuleConfig.ExtendMacroLists[CurrentMacroIndex];

        LoopCheckbox.IsChecked = currentMacro.IsLoopEnabled;
        LoopCountInputNode.Value = currentMacro.LoopCount;
        IntervalInputNode.Value = currentMacro.DefaultInterval;
        CompletionDelayInputNode.Value = currentMacro.CompletionDelay;
        if (LoopCountInputNode.AddButton != null)
            LoopCountInputNode.AddButton.IsEnabled = currentMacro.IsLoopEnabled;
        if (LoopCountInputNode.SubtractButton != null)
            LoopCountInputNode.SubtractButton.IsEnabled = currentMacro.IsLoopEnabled;

        unsafe
        {
            AtkResNode* collisionNode = LoopCountInputNode.CollisionNode;
            if (currentMacro.IsLoopEnabled)
                collisionNode->NodeFlags |= NodeFlags.RespondToMouse | NodeFlags.Focusable;
            else
                collisionNode->NodeFlags &= ~(NodeFlags.RespondToMouse | NodeFlags.Focusable);
        }
    }


    protected override void OnFinalize(AtkUnitBase* addon)
    {
        LoopCheckbox = null;
        LoopCountInputNode = null;
        LoopHintNode = null;
        base.OnFinalize(addon);
    }
    }
}
