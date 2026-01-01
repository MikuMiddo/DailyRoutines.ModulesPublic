using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DailyRoutines.Abstracts;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Nodes;
using Lumina.Excel.Sheets;
using ActionKind = FFXIVClientStructs.FFXIV.Client.UI.Agent.ActionKind;
using FFAction = Lumina.Excel.Sheets.Action;

namespace DailyRoutines.ModulesPublic;

public unsafe partial class MacroOptimization
{
    internal sealed class DRMacroCooldownViewer(DailyModuleBase instance) : NativeAddon
    {
    private readonly DailyModuleBase Instance = instance;

    private ScrollingAreaNode<ResNode>?         ScrollingArea;
    private TextInputNode?                       SearchInputNode;

    private List<(ActionType ActionType, uint ActionID, int CooldownMs, string ActionName, string JobName)>? LastDisplayedEntries;
    private List<(ActionType ActionType, uint ActionID, int CooldownMs, string ActionName, string JobName)>? CachedActionData;
    private List<(ActionType ActionType, uint ActionID, int CooldownMs, string ActionName, string JobName)>? CurrentFilteredEntries;

    private string SearchText              = string.Empty;

    private const int RowHeight = 60;
    private const int RowSpacing = 5;
    private const int RowPitch = RowHeight + RowSpacing;

    private readonly List<CooldownRow> RowPool = [];
    private int CurrentScrollPosition;

    protected override void OnSetup(AtkUnitBase* addon)
    {
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
            Size = new(380, 20),
            Position = new(10f, 0f),
            SeString = "已录制的技能冷却时间",
            FontSize = 16,
            AlignmentType = AlignmentType.Left,
            TextColor = KnownColor.White.Vector(),
        };
        TitleNode.AttachNode(MainContainerNode);

        var HintNode = new TextNode
        {
            IsVisible = true,
            Size = new(460, 20),
            Position = new(10f, 25f),
            SeString = "提示: 手动执行生产或战斗技能会自动录制冷却时间",
            FontSize = 12,
            AlignmentType = AlignmentType.Left,
            TextColor = KnownColor.Gray.Vector(),
        };
        HintNode.AttachNode(MainContainerNode);

        var SearchLabel = new TextNode
        {
            IsVisible = true,
            Size = new(50, 24),
            Position = new(10f, 52f),
            SeString = "搜索:",
            FontSize = 14,
            AlignmentType = AlignmentType.Left,
        };
        SearchLabel.AttachNode(MainContainerNode);

        SearchInputNode = new TextInputNode
        {
            IsVisible = true,
            Size = new(390f, 32f),
            Position = new(60f, 48f),
            PlaceholderString = "输入技能名称、职业或ID...",
            OnInputReceived = (input) =>
            {
                SearchText = input.ExtractText();
                RefreshCooldownList();
            }
        };
        SearchInputNode.AttachNode(MainContainerNode);

        ScrollingArea = new ScrollingAreaNode<ResNode>
        {
            IsVisible = true,
            Position = new(10f, 85f),
            Size = new(445f, 360f),
            ContentHeight = 0,
            ScrollSpeed = 24,
        };
        ScrollingArea.AttachNode(MainContainerNode);
        ScrollingArea.ScrollBarNode.OnValueChanged = scrollPos =>
        {
            // 注意：这里传入的是 PendingScrollPosition（ScrollBarNode.UpdateHandler），
            // 在某些情况下 ScrollPosition 还没同步更新，因此必须用参数驱动刷新，否则会出现“滚动条动了但内容不动”。
            CurrentScrollPosition = scrollPos;
            UpdateVisibleRows(scrollPos);
        };

        // 兜底（非轮询）：部分输入路径可能不触发 ValueUpdate，这里监听滚轮事件，延迟 1 tick 再刷新。
        ScrollingArea.ScrollingCollisionNode.AddEvent(AtkEventType.MouseWheel, () =>
            DService.Framework.RunOnTick(() =>
            {
                if (ScrollingArea == null)
                    return;

                var pos = ScrollingArea.ScrollBarNode.ScrollPosition;
                if (pos == CurrentScrollPosition)
                    return;

                CurrentScrollPosition = pos;
                UpdateVisibleRows(pos);
            }, delayTicks: 1));

        BuildCache(); // 初始化时构建缓存
        RefreshCooldownList();
    }

    private void BuildCache()
    {
        if (ModuleConfig.ActionCooldowns.Count == 0)
        {
            CachedActionData = [];
            return;
        }

        CachedActionData = ModuleConfig.ActionCooldowns
            .SelectMany(kvp => kvp.Value.Select(inner =>
            {
                var actionType = kvp.Key;
                var actionID = inner.Key;
                var cooldownMs = inner.Value;
                var actionName = LuminaWrapper.GetActionName(actionID);
                var jobName = GetJobNameForAction(actionType, actionID);
                return (actionType, actionID, cooldownMs, actionName, jobName);
            }))
            .OrderBy(x => x.actionID)
            .ToList();
    }

    private void RefreshCooldownList()
    {
        if (ScrollingArea == null || CachedActionData == null) return;

        var filteredEntries = string.IsNullOrWhiteSpace(SearchText) // 应用搜索过滤
            ? CachedActionData
            : [.. CachedActionData.Where(entry =>
                entry.ActionName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                entry.JobName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                entry.ActionID.ToString().Contains(SearchText))];

        if (LastDisplayedEntries != null && // 检查结果是否与上次相同，如果相同则不刷新UI
            filteredEntries.Count == LastDisplayedEntries.Count &&
            filteredEntries.SequenceEqual(LastDisplayedEntries))
            return;

        LastDisplayedEntries = [.. filteredEntries]; // 保存当前显示的结果
        CurrentFilteredEntries = LastDisplayedEntries;
        EnsureRowPool();

        if (filteredEntries.Count == 0)
        {
            ScrollingArea.ContentHeight = 20;
            ScrollingArea.ScrollPosition = 0;
            CurrentScrollPosition = 0;

            if (RowPool.Count > 0)
                RowPool[0].BindEmpty(CachedActionData.Count == 0 ? "暂无录制的冷却数据" : "未找到匹配的技能");

            for (var i = 1; i < RowPool.Count; i++)
                RowPool[i].Hide();
            return;
        }

        ScrollingArea.ContentHeight = filteredEntries.Count * RowPitch;
        ScrollingArea.ScrollPosition = 0;
        CurrentScrollPosition = 0;
        UpdateVisibleRows(0);
    }

    private void EnsureRowPool()
    {
        if (ScrollingArea == null)
            return;

        var visibleCount = (int)MathF.Ceiling(ScrollingArea.Height / RowPitch) + 2;
        visibleCount = Math.Clamp(visibleCount, 3, 40);

        while (RowPool.Count < visibleCount)
        {
            var row = new CooldownRow(Instance, ModuleConfig);
            row.AttachTo(ScrollingArea.ContentNode);
            RowPool.Add(row);
        }
    }

    private void UpdateVisibleRows(int? overrideScrollPos = null)
    {
        if (ScrollingArea == null || CurrentFilteredEntries == null || RowPool.Count == 0)
            return;

        // ScrollBar 组件会移动 ContentNode.Y 来实现原生滚动；虚拟化方案需要把它当作“父节点偏移”并补偿。
        // 否则会出现整体上移/下移，导致底部空白。
        var parentOffsetY = ScrollingArea.ContentNode.Y;

        var entries = CurrentFilteredEntries;
        if (entries.Count == 0)
            return;

        var maxScroll = Math.Max(0, (entries.Count * RowPitch) - (int)ScrollingArea.Height);
        var scrollPos = overrideScrollPos ?? CurrentScrollPosition;
        scrollPos = Math.Clamp(scrollPos, 0, maxScroll);
        CurrentScrollPosition = scrollPos;
        var firstIndex = scrollPos / RowPitch;
        var offsetY = -(scrollPos % RowPitch);

        for (var i = 0; i < RowPool.Count; i++)
        {
            var index = firstIndex + i;
            var y = offsetY + i * RowPitch;

            if (index < 0 || index >= entries.Count)
            {
                RowPool[i].Hide();
                continue;
            }

            RowPool[i].SetY(y - parentOffsetY);
            RowPool[i].Bind(entries[index]);
        }
    }

    private sealed class CooldownRow
    {
        private readonly DailyModuleBase instance;
        private readonly MacroConfig config;

        private readonly ResNode root;
        private readonly DragDropNode dragDrop;
        private readonly TextNode nameNode;
        private readonly TextNode jobNode;
        private readonly TextNode currentCooldownLabel;
        private readonly NumericInputNode cooldownInput;
        private readonly TextNode msLabel;

        private ActionType boundActionType;
        private uint boundActionID;
        private bool isBound;

        public CooldownRow(DailyModuleBase instance, MacroConfig config)
        {
            this.instance = instance;
            this.config = config;

            root = new ResNode
            {
                IsVisible = true,
                Size = new(360f, 60f),
                Position = Vector2.Zero,
            };

            dragDrop = new DragDropNode
            {
                IsVisible = true,
                Size = new(40f),
                Position = new(0, 10f),
                AcceptedType = DragDropType.Nothing,
                IsDraggable = true,
                IsClickable = true,
            };
            dragDrop.AttachNode(root);

            nameNode = new TextNode
            {
                IsVisible = true,
                Size = new(150, 20),
                Position = new(50f, 5f),
                FontSize = 14,
                AlignmentType = AlignmentType.Left,
            };
            nameNode.AttachNode(root);

            jobNode = new TextNode
            {
                IsVisible = true,
                Size = new(190, 40),
                Position = new(50f, 25f),
                FontSize = 11,
                AlignmentType = AlignmentType.Left,
                TextColor = KnownColor.Gray.Vector(),
                TextFlags = TextFlags.WordWrap | TextFlags.MultiLine,
                LineSpacing = 14,
            };
            jobNode.AttachNode(root);

            currentCooldownLabel = new TextNode
            {
                IsVisible = true,
                Size = new(65, 20),
                Position = new(250f, 22f),
                SeString = "当前冷却:",
                FontSize = 12,
                AlignmentType = AlignmentType.Left,
                TextColor = KnownColor.White.Vector(),
            };
            currentCooldownLabel.AttachNode(root);

            cooldownInput = new NumericInputNode
            {
                IsVisible = true,
                Size = new(100f, 24f),
                Position = new(310f, 18f),
                Min = 100,
                Max = 10000,
                Step = 100,
                OnValueUpdate = value =>
                {
                    if (!isBound)
                        return;

                    if (!this.config.ActionCooldowns.ContainsKey(boundActionType))
                        this.config.ActionCooldowns[boundActionType] = [];

                    this.config.ActionCooldowns[boundActionType][boundActionID] = value;
                    this.config.Save(this.instance);
                }
            };
            cooldownInput.AttachNode(root);
            cooldownInput.AddButton.IsVisible = false;
            cooldownInput.SubtractButton.IsVisible = false;

            msLabel = new TextNode
            {
                IsVisible = true,
                Size = new(25, 20),
                Position = new(370f, 22f),
                SeString = "ms",
                FontSize = 12,
                AlignmentType = AlignmentType.Left,
                TextColor = KnownColor.Gray.Vector(),
            };
            msLabel.AttachNode(root);
        }

        public void AttachTo(ResNode parent) => root.AttachNode(parent);

        public void SetY(float y) => root.Y = y;

        public void Hide()
        {
            root.IsVisible = false;
            isBound = false;
        }

        public void BindEmpty(string message)
        {
            root.IsVisible = true;
            root.Y = 0;

            dragDrop.IsVisible = false;
            cooldownInput.IsVisible = false;
            msLabel.IsVisible = false;
            currentCooldownLabel.IsVisible = false;

            nameNode.IsVisible = true;
            nameNode.Position = new(0, 0);
            nameNode.Size = new(360, 60);
            nameNode.AlignmentType = AlignmentType.Center;
            nameNode.FontSize = 14;
            nameNode.TextColor = KnownColor.Gray.Vector();
            nameNode.SeString = message;

            jobNode.IsVisible = false;

            isBound = false;
        }

        public void Bind((ActionType ActionType, uint ActionID, int CooldownMs, string ActionName, string JobName) entry)
        {
            root.IsVisible = true;

            dragDrop.IsVisible = true;
            cooldownInput.IsVisible = true;
            msLabel.IsVisible = true;
            currentCooldownLabel.IsVisible = true;
            jobNode.IsVisible = true;

            nameNode.AlignmentType = AlignmentType.Left;
            nameNode.Position = new(50f, 5f);
            nameNode.Size = new(150, 20);
            nameNode.FontSize = 14;
            nameNode.TextColor = KnownColor.White.Vector();

            boundActionType = entry.ActionType;
            boundActionID = entry.ActionID;
            isBound = true;

            var iconID = LuminaWrapper.GetActionIconID(entry.ActionID);
            var isCraftAction = entry.ActionType == ActionType.CraftAction;

            dragDrop.IconId = iconID;
            dragDrop.Payload = new()
            {
                Type = isCraftAction ? DragDropType.CraftingAction : DragDropType.Action,
                Int2 = (int)entry.ActionID,
            };
            dragDrop.OnRollOver = node => node.ShowTooltip(AtkTooltipManager.AtkTooltipType.Action, isCraftAction ? ActionKind.CraftingAction : ActionKind.Action);
            dragDrop.OnRollOut = node => node.HideTooltip();

            nameNode.SeString = $"{entry.ActionName} (ID: {entry.ActionID})";
            jobNode.SeString = entry.JobName;
            cooldownInput.Value = entry.CooldownMs;
        }
    }
    
    private static string GetJobNameForAction(ActionType actionType, uint actionID)
    {
        string jobName = "未知职业";

        if (actionType == ActionType.CraftAction)
        {
            var craftAction = LuminaGetter.GetRow<CraftAction>(actionID);
            if (craftAction != null)
            {
                var classJobCategory = craftAction.Value.ClassJobCategory;
                if (classJobCategory.IsValid)
                    jobName = classJobCategory.Value.Name.ExtractText();
            }
        }
        else if (actionType == ActionType.Action)
        {
            var action = LuminaGetter.GetRow<FFAction>(actionID);
            if (action != null)
            {
                var classJobCategory = action.Value.ClassJobCategory;
                if (classJobCategory.IsValid)
                    jobName = classJobCategory.Value.Name.ExtractText();
            }
        }

        if (jobName.Length > 20) // 如果职业名称过长，手动插入换行符 比如醒梦浴血等通用技能
        {
            var words = jobName.Split(' ');
            var lines = new List<string>();
            var currentLine = "";

            foreach (var word in words)
            {
                if (string.IsNullOrEmpty(currentLine))
                    currentLine = word;
                else if ((currentLine + " " + word).Length <= 20)
                    currentLine += " " + word;
                else
                {
                    lines.Add(currentLine);
                    currentLine = word;
                }
            }

            if (!string.IsNullOrEmpty(currentLine))
                lines.Add(currentLine);

            return string.Join("\r", lines);
        }

        return jobName;
    }

    protected override void OnFinalize(AtkUnitBase* addon)
    {
        LastDisplayedEntries = null;
        CachedActionData     = null;
        CurrentFilteredEntries = null;
        ScrollingArea        = null;
        SearchInputNode      = null;
        SearchText           = string.Empty;
        RowPool.Clear();

        base.OnFinalize(addon);
    }
    }
}
