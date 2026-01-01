using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;
using FFAction = Lumina.Excel.Sheets.Action;
using ActionKind = FFXIVClientStructs.FFXIV.Client.UI.Agent.ActionKind;
using Lumina.Excel.Sheets;

namespace DailyRoutines.ModulesPublic;

using static DailyRoutines.ModulesPublic.MacroOptimization;

internal sealed class ProgressPanel : ResNode // 宏进度面板
{
    private class ProgressRow
    {
        public SimpleComponentNode?    CompNode;
        public SimpleNineGridNode?     BackgroundNode;
        public IconNode?               SimpleIconNode; // 用于echo/wait/emote
        public DragDropNode?           DragDropIconNode; // 用于action
        public TextNode?               NameNode;
        public TextNode?               ConditionStatusNode; // 用于显示条件判断状态
        public TextNode?               TargetStatusNode; // 用于显示目标判断状态
        public TextNode?               CounterNode;
        public ProgressBarCastNode? ProgressBarNode;
    }

    private readonly MacroConfig ModuleConfig;

    private readonly List<ProgressBarCastNode> ProgressBarNodes        = [];
    private readonly List<SimpleNineGridNode>     ProgressBackgroundNodes = [];
    private readonly List<ProgressRow>            ProgressRows            = [];

    private List<uint>   ParsedMacroActionIDs      = [];
    private List<string> ParsedCommandTypes        = [];
    private List<bool>   ParsedHasTargetFlags      = [];
    private List<int>    SkippedProgressBarIndices = [];

    private ScrollingAreaNode<SimpleComponentNode>? ProgressVerticalListNode;
    private ProgressBarCastNode?                 OverallProgressBarNode;
    private TextNode?                               OverallProgressTextNode;

    private const int RowsPerFrame = 2;
    private bool IsInitializingRows = false;
    private bool IsDisposed = false;
    private int DesiredRowCount = 0;
    private int MaxMacroLines = 255; // 最大宏行数 最大只有255行

    public ProgressPanel(MacroConfig config, int maxMacroLines = 255)
    {
        ModuleConfig = config;
        MaxMacroLines = maxMacroLines;

        Position = new Vector2(657f, 0f);
        Size = new Vector2(245f, 580f);
        IsVisible = true;
    }

    public void Build()
    {
        OverallProgressTextNode = new TextNode
        {
            IsVisible = true,
            SeString = "总体进度",
            FontSize = 16,
            Position = new Vector2(10, -10),
            Size = new Vector2(225, 20),
            AlignmentType = AlignmentType.Center
        };
        OverallProgressTextNode.AttachNode(this);

        OverallProgressBarNode = new ProgressBarCastNode
        {
            Progress = 1f,
            Size = new(225.0f, 18.0f),
            BackgroundColor = KnownColor.Black.Vector(),
            BarColor = KnownColor.White.Vector(),
            IsVisible = true,
            Position = new(10f, 8f)
        };
        OverallProgressBarNode.AttachNode(this);

        
        var scrollAreaY = 26f; // 计算滚动区域大小：使用 ProgressPanel 的 Size 减去顶部占用的空间
        var scrollHeight = Size.Y - scrollAreaY; // 从起始位置到底部的高度
        var scrollWidth = Size.X - 22f;  // 留出右边距

        ProgressVerticalListNode = new ScrollingAreaNode<SimpleComponentNode>
        {
            IsVisible = true,
            Size = new(scrollWidth, scrollHeight),
            Position = new(7f, scrollAreaY),
            ContentHeight = 0f,
            ContentNode = { IsVisible = true }
        };
        ProgressVerticalListNode.AttachNode(this);

        if (MaxMacroLines == 255)
        {
            var initialRows = Math.Min(11, MaxMacroLines);
            EnsureImmediateRows(initialRows);
            ScheduleRowCreation(MaxMacroLines);
        }
        else
            EnsureImmediateRows(MaxMacroLines);
    }

    private void EnsureImmediateRows(int requiredRows)
    {
        requiredRows = Math.Min(requiredRows, MaxMacroLines);
        while (ProgressRows.Count < requiredRows)
            CreateNewRow();

        DesiredRowCount = Math.Max(DesiredRowCount, requiredRows);
    }

    private void ScheduleRowCreation(int requiredRows)
    {
        if (IsDisposed)
            return;

        requiredRows = Math.Clamp(requiredRows, 0, MaxMacroLines);
        if (requiredRows <= ProgressRows.Count)
            return;

        DesiredRowCount = Math.Max(DesiredRowCount, requiredRows);

        if (IsInitializingRows)
            return;

        IsInitializingRows = true;
        DService.Framework.Update += ProgressiveRowCreation;
    }

    private void StopProgressiveCreation()
    {
        if (!IsInitializingRows) return;

        DService.Framework.Update -= ProgressiveRowCreation;
        IsInitializingRows = false;
    }

    public void DisposePanel()
    {
        if (IsDisposed) return;

        IsDisposed = true;
        StopProgressiveCreation();
    }

    public void SetMacroLines(List<uint> macroLines, List<string> commandTypes = null, List<bool> hasTargetFlags = null)
    {
        ParsedMacroActionIDs = macroLines;
        ParsedCommandTypes = commandTypes ?? [];
        ParsedHasTargetFlags = hasTargetFlags ?? [];

        // 计算需要的行数
        var requiredRows = MaxMacroLines == 255
            ? Math.Max(ParsedMacroActionIDs.Count, 11)  // 默认至少11个占位行
            : ParsedMacroActionIDs.Count;               // 只创建实际需要的行数

        if (requiredRows > ProgressRows.Count)
            ScheduleRowCreation(requiredRows);

        RefreshProgressList();

        if (ParsedMacroActionIDs.Count > 0) // 重置总体进度显示
            OverallProgressTextNode.SeString = $"总体进度: 0 / {ParsedMacroActionIDs.Count} (0%)";
        else
            OverallProgressTextNode.SeString = "总体进度: 0 / 0 (0%)";
        OverallProgressBarNode.Progress = 1f;
    }

    private void ProgressiveRowCreation(IFramework framework)
    {
        if (IsDisposed || ProgressRows.Count >= MaxMacroLines)
        {
            StopProgressiveCreation();
            return;
        }

        var target = Math.Min(DesiredRowCount, MaxMacroLines);
        if (ProgressRows.Count >= target)
        {
            StopProgressiveCreation();
            return;
        }

        var rowsToCreate = Math.Min(RowsPerFrame, target - ProgressRows.Count);
        if (rowsToCreate <= 0)
        {
            StopProgressiveCreation();
            return;
        }

        for (var i = 0; i < rowsToCreate; i++)
            CreateNewRow();

        if (ParsedMacroActionIDs.Count > 0)
            RefreshProgressList();
    }

    public void ClearAllDragDropPayloads()
    {
        foreach (var row in ProgressRows)
        {
            if (row.DragDropIconNode.Payload != null)
                row.DragDropIconNode.Payload = null;
        }
    }

    private int LastVisibleRowCount = 0;
    private int CurrentMacroLineCount = 0;

    private void RefreshProgressList()
    {
        if (ProgressRows.Count == 0)
            return;

        ProgressVerticalListNode.ContentHeight = 0f;

        CurrentMacroLineCount = ParsedMacroActionIDs.Count;
       
        var visibleRowCount = MaxMacroLines == 255 // 只有 MaxMacroLines == 255 时才显示占位行(至少11个)
            ? Math.Max(ParsedMacroActionIDs.Count, 11)
            : ParsedMacroActionIDs.Count;

        var availableRowCount = Math.Min(visibleRowCount, ProgressRows.Count); // 只更新已经创建的节点，如果节点还在创建中，只更新已有的

        for (var i = 0; i < availableRowCount; i++)
        {
            if (i < ParsedMacroActionIDs.Count)
            {
                var actionID = ParsedMacroActionIDs[i];
                var commandType = i < ParsedCommandTypes.Count ? ParsedCommandTypes[i] : "action";
                var hasTarget = i < ParsedHasTargetFlags.Count && ParsedHasTargetFlags[i];
                UpdateRow(i, actionID, commandType, hasTarget, true);
            }
            else
                UpdateRow(i, 0, "", false, true);
        }

        if (LastVisibleRowCount > availableRowCount)
        {
            for (var i = availableRowCount; i < LastVisibleRowCount && i < ProgressRows.Count; i++)
                ProgressRows[i].CompNode.IsVisible = false;
        }

        LastVisibleRowCount = availableRowCount;
    }

    private void CreateNewRow()
    {
        var row = new ProgressRow();

        row.CompNode = new SimpleComponentNode
        {
            IsVisible = false, // 初始化时隐藏，UpdateRow时才显示并设置位置
            Size = new(200.0f, 50.0f),
            Position = Vector2.Zero
        };
        row.CompNode.AttachNode(ProgressVerticalListNode.ContentNode);

        row.BackgroundNode = new SimpleNineGridNode
        {
            IsVisible = false,
            Size = new(200.0f, 50.0f),
            Position = Vector2.Zero,
            TexturePath = "ui/uld/ListItemA.tex",
            TextureCoordinates = new Vector2(0.0f, 44.0f),
            TextureSize = new Vector2(64.0f, 22.0f),
            Color = KnownColor.Yellow.Vector()
        };
        row.BackgroundNode.AttachNode(row.CompNode);

        row.SimpleIconNode = new IconNode
        {
            IsVisible = false, // 默认隐藏
            IconId = 0,
            Size = new(45),
        };
        row.SimpleIconNode.AttachNode(row.CompNode);

        row.DragDropIconNode = new DragDropNode
        {
            IsVisible = true, // 默认显示(大部分是action)
            IconId = 0,
            Size = new(45),
            AcceptedType = DragDropType.Nothing,
            IsDraggable = true,
            IsClickable = true,
        };
        row.DragDropIconNode.AttachNode(row.CompNode);

        row.NameNode = new TextNode
        {
            IsVisible = true,
            SeString = "",
            FontSize = 16,
            TextFlags = TextFlags.AutoAdjustNodeSize,
            Position = new(50f, 5f)
        };
        row.NameNode.AttachNode(row.CompNode);


        row.ConditionStatusNode = new TextNode
        {
            IsVisible = false,
            SeString = "",
            FontSize = 10,
            TextFlags = TextFlags.AutoAdjustNodeSize,
            AlignmentType = AlignmentType.Center,
            Position = new(50f, 5f)
        };
        row.ConditionStatusNode.AttachNode(row.CompNode);


        row.TargetStatusNode = new TextNode
        {
            IsVisible = false,
            SeString = "",
            FontSize = 10,
            TextFlags = TextFlags.AutoAdjustNodeSize,
            AlignmentType = AlignmentType.Center,
            Position = new(50f, 5f)
        };
        row.TargetStatusNode.AttachNode(row.CompNode);

        row.CounterNode = new TextNode
        {
            IsVisible = true,
            SeString = "",
            FontSize = 10,
            Position = new Vector2(143, 5),
            Size = new Vector2(50, 20),
            AlignmentType = AlignmentType.Right
        };
        row.CounterNode.AttachNode(row.CompNode);

        row.ProgressBarNode = new ProgressBarCastNode
        {
            Progress = 0f,
            Size = new(160.0f, 20.0f),
            BackgroundColor = KnownColor.Black.Vector(),
            BarColor = KnownColor.Gray.Vector(),
            IsVisible = true,
            Position = new(40f, 25f)
        };
        row.ProgressBarNode.AttachNode(row.CompNode);

        ProgressRows.Add(row);
        ProgressBarNodes.Add(row.ProgressBarNode);
        ProgressBackgroundNodes.Add(row.BackgroundNode);
    }

    private void UpdateRow(int index, uint actionID, string commandType, bool hasTarget, bool isVisible)
    {
        var row = ProgressRows[index];

        row.CompNode.IsVisible = isVisible;
        if (!isVisible)
            return;

        row.CompNode.Position = new Vector2(10f, ProgressVerticalListNode.ContentHeight);
        ProgressVerticalListNode.ContentHeight += row.CompNode.Height;

        var isEmpty = actionID == 0 && string.IsNullOrEmpty(commandType);

        if (isEmpty)
        {
            row.CompNode.Size = new(200.0f, 50.0f);
            row.SimpleIconNode.IsVisible = true;
            row.SimpleIconNode.IconId = 0;
            row.SimpleIconNode.Size = new(45);
            row.DragDropIconNode.IsVisible = false;
            row.NameNode.SeString = "";
            row.CounterNode.SeString = "";
            row.ConditionStatusNode.IsVisible = false;
            row.TargetStatusNode.IsVisible = false;
            row.ProgressBarNode.BackgroundColor = KnownColor.Transparent.Vector();
            row.ProgressBarNode.BarColor = KnownColor.Transparent.Vector();
            row.ProgressBarNode.Progress = 0f;
            return;
        }

        row.CompNode.Size = new(200.0f, 50.0f); // 以下为非空白行设置过程

        uint iconID;
        string name;
        var isAction = false;
        var isCraftAction = false;

        switch (commandType)
        {
            case "echo":
                iconID = 246387; // 使用"嘘"图标
                name = "私语";
                break;
            case "wait":
                iconID = 246428;// 使用"等待"图标
                name = "等待";
                break;
            case "call":
                iconID = 246261; // 使用"箭头"图标
                name = "呼叫宏";
                break;
            case "close":
                iconID = 246262; // 使用"关闭"图标
                name = "关闭窗口";
                break;
            case "emote":
                var emote = LuminaGetter.GetRow<Emote>(actionID);
                iconID = emote?.Icon ?? 0;
                name = emote?.Name.ToString() ?? "未知表情";
                break;
            case "if-conditional":
                if (MacroCacheHelper.TryGetActionDisplay(actionID, out var conditionalDisplayInfo))
                {
                    iconID = conditionalDisplayInfo.iconId;
                    name = conditionalDisplayInfo.name;
                    isCraftAction = conditionalDisplayInfo.isCraftAction;
                    isAction = !isCraftAction;
                }
                else if (actionID > 0)
                {
                    var action = LuminaGetter.GetRow<FFAction>(actionID);
                    var caction = LuminaGetter.GetRow<CraftAction>(actionID);
                    iconID = action?.Icon ?? caction?.Icon ?? 0;
                    name = action?.Name.ToString() ?? caction?.Name.ToString() ?? "未知技能";
                    isAction = action != null;
                    isCraftAction = caction != null;
                }
                else
                {
                    iconID = 246367; // 使用“疑问”图标作为默认
                    name = "条件命令";
                }
                break;
            case "action":
            default:
                if (MacroCacheHelper.TryGetActionDisplay(actionID, out var displayInfo))
                {
                    iconID = displayInfo.iconId;
                    name = displayInfo.name;
                    isCraftAction = displayInfo.isCraftAction;
                    isAction = !isCraftAction;
                }
                else
                {
                    var action = LuminaGetter.GetRow<FFAction>(actionID);
                    var caction = LuminaGetter.GetRow<CraftAction>(actionID);
                    iconID = action?.Icon ?? caction?.Icon ?? 0;
                    name = action?.Name.ToString() ?? caction?.Name.ToString() ?? "未知技能";
                    isAction = action != null;
                    isCraftAction = caction != null;
                }
                break;
        }

        var needsDragDrop = isAction || isCraftAction; // 根据类型切换显示哪个图标节点

        if (needsDragDrop)
        {
            row.DragDropIconNode.IsVisible = true;
            row.DragDropIconNode.Size = new(45);
            row.DragDropIconNode.IconId = iconID;
            row.DragDropIconNode.Payload = new()
            {
                Type = isCraftAction ? DragDropType.CraftingAction : DragDropType.Action,
                Int2 = (int)actionID    ,
            };
            row.DragDropIconNode.OnRollOver = node => node.ShowTooltip(AtkTooltipManager.AtkTooltipType.Action, isCraftAction ? ActionKind.CraftingAction : ActionKind.Action);
            row.DragDropIconNode.OnRollOut = node => node.HideTooltip();

            row.SimpleIconNode.IsVisible = false;
        }
        else
        {
            row.SimpleIconNode.IsVisible = true;
            row.SimpleIconNode.Size = new(45);
            row.SimpleIconNode.IconId = iconID;

            row.DragDropIconNode.IsVisible = false;
        }

        var hasCondition = commandType == "if-conditional";
        var hasBothLabels = hasCondition && hasTarget;
        if (hasCondition) // 如果是条件命令，显示 [条件] 标签
        {
            row.ConditionStatusNode.IsVisible = true;
            row.ConditionStatusNode.SeString = "[条件]";
            row.ConditionStatusNode.Position = new(60f, hasBothLabels ? 2f : 8f);
        }
        else
            row.ConditionStatusNode.IsVisible = false;

        if (hasTarget) // 如果有目标后缀，显示 [目标] 标签
        {
            row.TargetStatusNode.IsVisible = true;
            row.TargetStatusNode.SeString = "<目标>";
            row.TargetStatusNode.Position = new(60f, hasBothLabels ? 16f : 8f);
        }
        else
            row.TargetStatusNode.IsVisible = false;

        row.NameNode.SeString = name; // 设置技能名位置：如果有标签则在右侧，否则在原位置
        row.NameNode.Position = (hasCondition || hasTarget) ? new(80f, 5f) : new(50f, 5f);

        row.CounterNode.SeString = $"{index + 1} / {ParsedMacroActionIDs.Count}";

        row.ProgressBarNode.Progress = 1f;
        row.ProgressBarNode.BackgroundColor = KnownColor.Black.Vector();
        row.ProgressBarNode.BarColor = KnownColor.Gray.Vector();
    }

    public void UpdateProgress(int progressIndex, float progress)
    {
        if (progressIndex >= 0 && progressIndex < ProgressBarNodes.Count)
            ProgressBarNodes[progressIndex].Progress = Math.Clamp(progress, 0f, 1f);
    }

    public void SetProgressColor(int progressIndex, KnownColor color)
    {
        if (progressIndex >= 0 && progressIndex < ProgressBarNodes.Count)
            ProgressBarNodes[progressIndex].BarColor = color.Vector();
    }

    public void MarkProgressAsSkipped(int progressIndex)
    {
        if (progressIndex >= 0 && progressIndex < ProgressBarNodes.Count)
        {
            SkippedProgressBarIndices.Add(progressIndex);
            ProgressBarNodes[progressIndex].BarColor = KnownColor.Red.Vector();
        }
    }

    public void UpdateConditionStatus(int progressIndex, string conditionStatus)
    {
        if (progressIndex >= 0 && progressIndex < ProgressRows.Count)
        {
            var row = ProgressRows[progressIndex];
            row.ConditionStatusNode.SeString = conditionStatus;
        }
    }

    public void UpdateTargetStatus(int progressIndex, string targetStatus)
    {
        if (progressIndex >= 0 && progressIndex < ProgressRows.Count)
        {
            var row = ProgressRows[progressIndex];
            row.TargetStatusNode.SeString = targetStatus;
        }
    }

    public void ClearSkippedStates() => SkippedProgressBarIndices.Clear();

    public bool IsProgressSkipped(int progressIndex) => SkippedProgressBarIndices.Contains(progressIndex);

    public void SetCurrentProgress(int progressIndex)
    {
        for (var i = 0; i < CurrentMacroLineCount; i++)
        {
            if (i < progressIndex)
            {
                ProgressBarNodes[i].Progress = 1f;
                if (!SkippedProgressBarIndices.Contains(i))
                    ProgressBarNodes[i].BarColor = KnownColor.Green.Vector();

                ProgressBackgroundNodes[i].IsVisible = false;
            }
            else if (i == progressIndex)
            {
                if (!SkippedProgressBarIndices.Contains(i))
                    ProgressBarNodes[i].BarColor = KnownColor.Yellow.Vector();
                ProgressBackgroundNodes[i].IsVisible = true;
            }
            else
            {
                ProgressBarNodes[i].Progress = 0f;

                ProgressBackgroundNodes[i].IsVisible = false;
            }
        }

        ScrollToCurrentProgress(progressIndex);
    }

    private void ScrollToCurrentProgress(int progressIndex)
    {
        if (progressIndex < 0 || progressIndex >= CurrentMacroLineCount) return;

        const float itemHeight = 50f; // 每个步骤的高度

        var targetY = progressIndex * itemHeight;

        var visibleHeight = ProgressVerticalListNode.Size.Y;

        var scrollPosition = targetY - (visibleHeight / 2) + (itemHeight / 2); // 计算需要滚动的位置，让当前项居中显示

        var maxScroll = Math.Max(0, ProgressVerticalListNode.ContentHeight - visibleHeight);
        scrollPosition = Math.Max(0, Math.Min(scrollPosition, maxScroll));

        ProgressVerticalListNode.ScrollPosition = (int)scrollPosition;
    }

    public void UpdateOverallProgress(float progress, int currentStep = -1)
    {
        OverallProgressBarNode.Progress = progress;

        if (ParsedMacroActionIDs.Count == 0)
            OverallProgressTextNode.SeString = "总体进度: 0 / 0 (0%)";
        else
        {
            var current = currentStep >= 0 ? currentStep : (int)(progress * ParsedMacroActionIDs.Count);
            var percentage = (int)(progress * 100);
            OverallProgressTextNode.SeString = $"总体进度: {current} / {ParsedMacroActionIDs.Count} ({percentage}%)";
        }
    }

    public void SetOverallProgressColor(KnownColor color)
    {
        OverallProgressBarNode.BarColor = color.Vector();
    }

    public void ResetProgress()
    {
        for (var i = 0; i < CurrentMacroLineCount; i++)
        {
            ProgressBarNodes[i].Progress = 1f;
            ProgressBarNodes[i].BarColor = KnownColor.White.Vector();
            ProgressBackgroundNodes[i].IsVisible = false;

            if (i < ProgressRows.Count) // 恢复初始状态
            {
                var row = ProgressRows[i];
                if (row.ConditionStatusNode.IsVisible)
                    row.ConditionStatusNode.SeString = "[条件]";

                if (row.TargetStatusNode.IsVisible)
                    row.TargetStatusNode.SeString = "<目标>";
            }
        }

        SkippedProgressBarIndices.Clear(); // 重置时清空跳过状态

        OverallProgressBarNode.Progress = 1f;
        OverallProgressBarNode.BarColor = KnownColor.White.Vector();
        if (ParsedMacroActionIDs.Count > 0)
            OverallProgressTextNode.SeString = $"总体进度: 0 / {ParsedMacroActionIDs.Count} (0%)";
        else
            OverallProgressTextNode.SeString = "总体进度: 0 / 0 (0%)";
    }
}
