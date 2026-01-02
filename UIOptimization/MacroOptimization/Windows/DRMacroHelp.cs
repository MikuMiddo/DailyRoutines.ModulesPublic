using System;
using System.Collections.Generic;
using System.Windows.Forms;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Nodes;

namespace DailyRoutines.ModulesPublic;

public unsafe partial class MacroOptimization
{
    internal sealed class DRMacroHelp : NativeAddon
    {
        private readonly record struct HelpRow(string Syntax, string Description, string Example);

        private sealed class HelpTableHeaderNode : ResNode
        {
            private readonly TextNode SyntaxNode;
            private readonly TextNode DescriptionNode;
            private readonly TextNode ExampleNode;

            public HelpTableHeaderNode()
            {
                IsVisible = true;

                SyntaxNode = new TextNode
                {
                    IsVisible = true,
                    Position = new(0, 0),
                    String = "语法",
                    FontSize = 13,
                    AlignmentType = AlignmentType.Left,
                    TextColor = KnownColor.Orange.Vector(),
                };
                SyntaxNode.AttachNode(this);

                DescriptionNode = new TextNode
                {
                    IsVisible = true,
                    String = "说明",
                    FontSize = 13,
                    AlignmentType = AlignmentType.Left,
                    TextColor = KnownColor.Orange.Vector(),
                };
                DescriptionNode.AttachNode(this);

                ExampleNode = new TextNode
                {
                    IsVisible = true,
                    String = "示例",
                    FontSize = 13,
                    AlignmentType = AlignmentType.Left,
                    TextColor = KnownColor.Orange.Vector(),
                };
                ExampleNode.AttachNode(this);

                Size = new(1, 24);
            }

            protected override void OnSizeChanged()
            {
                base.OnSizeChanged();

                var width = Width;
                var available = MathF.Max(1, width - 12);
                var col1 = MathF.Round(available * 0.25f);
                var col2 = MathF.Round(available * 0.45f);
                var col3 = MathF.Max(1, available - col1 - col2);

                SyntaxNode.Size = new(col1, Height);
                DescriptionNode.Position = new(col1 + 6, 0);
                DescriptionNode.Size = new(col2, Height);
                ExampleNode.Position = new(col1 + col2 + 12, 0);
                ExampleNode.Size = new(col3, Height);
            }
        }

        private sealed class HelpTableRowNode : ResNode
        {
            private readonly string ExampleText;
            private readonly TextNode SyntaxNode;
            private readonly TextNode DescriptionNode;
            private readonly TextNode ExampleNode;

            public HelpTableRowNode(HelpRow rowData)
            {
                IsVisible = true;
                ExampleText = rowData.Example;

                SyntaxNode = new TextNode
                {
                    IsVisible = true,
                    Position = new(0, 0),
                    String = rowData.Syntax,
                    FontSize = 13,
                    AlignmentType = AlignmentType.Left,
                    TextFlags = TextFlags.MultiLine,
                    LineSpacing = 14,
                    TextColor = KnownColor.LightBlue.Vector(),
                };
                SyntaxNode.AttachNode(this);

                DescriptionNode = new TextNode
                {
                    IsVisible = true,
                    String = rowData.Description,
                    FontSize = 12,
                    AlignmentType = AlignmentType.Left,
                    TextFlags = TextFlags.MultiLine,
                    LineSpacing = 14,
                    TextColor = KnownColor.Gray.Vector(),
                };
                DescriptionNode.AttachNode(this);

                ExampleNode = new TextNode
                {
                    IsVisible = true,
                    String = rowData.Example,
                    FontSize = 12,
                    AlignmentType = AlignmentType.Left,
                    TextFlags = TextFlags.MultiLine,
                    LineSpacing = 14,
                    TextColor = KnownColor.Yellow.Vector(),
                };
                ExampleNode.AttachNode(this);

                ExampleNode.AddEvent(AtkEventType.MouseOver, () =>
                {
                    ExampleNode.TextColor = KnownColor.Orange.Vector();
                });

                ExampleNode.AddEvent(AtkEventType.MouseOut, () =>
                {
                    ExampleNode.TextColor = KnownColor.Yellow.Vector();
                });

                ExampleNode.ShowClickableCursor = true;
                ExampleNode.AddEvent(AtkEventType.MouseClick, () =>
                {
                    if (!string.IsNullOrWhiteSpace(ExampleText))
                    {
                        try
                        {
                            Clipboard.SetText(ExampleText);
                            NotificationSuccess(Lang.Get("CopiedToClipboard"));
                        }
                        catch
                        {
                            // ignored
                        }
                    }
                });

                Size = new(1, 58);
            }

            protected override void OnSizeChanged()
            {
                base.OnSizeChanged();

                var width = Width;
                var available = MathF.Max(1, width - 12);
                var col1 = MathF.Round(available * 0.25f);
                var col2 = MathF.Round(available * 0.45f);
                var col3 = MathF.Max(1, available - col1 - col2);

                SyntaxNode.Size = new(col1, Height);
                DescriptionNode.Position = new(col1 + 6, 0);
                DescriptionNode.Size = new(col2, Height);
                ExampleNode.Position = new(col1 + col2 + 12, 0);
                ExampleNode.Size = new(col3, Height);
            }
        }

        private string HelpFilter = string.Empty;
        private bool   ApplyFilterScheduled;

        private TextInputNode? FilterInputNode;
        private ScrollingAreaNode<TreeListNode>? ScrollingArea;

        private TreeListCategoryNode? TargetsCategory;
        private TreeListCategoryNode? IfCategory;
        private TreeListCategoryNode? ExtraCategory;

        private readonly List<(HelpRow Row, ResNode Node)> TargetRowNodes = [];
        private readonly List<(HelpRow Row, ResNode Node)> IfRowNodes = [];
        private readonly List<(HelpRow Row, ResNode Node)> ExtraRowNodes = [];

    protected override void OnSetup(AtkUnitBase* addon)
    {
        var mainContainer = new ResNode
        {
            IsVisible = true,
            Position = ContentStartPosition,
            Size = ContentSize,
        };
        mainContainer.AttachNode(this);

        FilterInputNode = new TextInputNode
        {
            IsVisible = true,
            Position = new(10, 8),
            Size = new(ContentSize.X - 20, 28),
            PlaceholderString = "输入关键字过滤（例如 party / enemy / buff / hp / toTop）",
            OnInputReceived = input =>
            {
                HelpFilter = input.ExtractText();
                ScheduleApplyFilter();
            }
        };
        FilterInputNode.AttachNode(mainContainer);

        ScrollingArea = new ScrollingAreaNode<TreeListNode>
        {
            IsVisible = true,
            Position = new(10, 34),
            Size = new(ContentSize.X - 20, ContentSize.Y - 44),
            ContentHeight = 10f,
            ScrollSpeed = 20,
            AutoHideScrollBar = false,
        };
        ScrollingArea.AttachNode(mainContainer);

        ScrollingArea.ContentNode.OnLayoutUpdate = newHeight =>
            ScrollingArea.ContentHeight = Math.Max(10f, newHeight + 10f);

        BuildTree();
        ApplyFilter();
    }

    private void BuildTree()
    {
        if (ScrollingArea == null)
            return;

        var root = ScrollingArea.ContentNode;

        TargetsCategory = new TreeListCategoryNode
        {
            IsVisible = true,
            IsCollapsed = false,
            String = "目标占位符（智能目标）",
        };
        root.AddCategoryNode(TargetsCategory);

        AddTableHeader(TargetsCategory);
        foreach (var row in TargetHelpRows)
            TargetRowNodes.Add((row, AddHelpRow(TargetsCategory, row)));

        IfCategory = new TreeListCategoryNode
        {
            IsVisible = true,
            IsCollapsed = false,
            String = "/if 条件语法",
        };
        root.AddCategoryNode(IfCategory);

        AddTableHeader(IfCategory);
        foreach (var row in IfHelpRows)
            IfRowNodes.Add((row, AddHelpRow(IfCategory, row)));

        ExtraCategory = new TreeListCategoryNode
        {
            IsVisible = true,
            IsCollapsed = false,
            String = "扩展命令",
        };
        root.AddCategoryNode(ExtraCategory);

        AddTableHeader(ExtraCategory);
        foreach (var row in ExtraCommandRows)
            ExtraRowNodes.Add((row, AddHelpRow(ExtraCategory, row)));
    }

    private void ScheduleApplyFilter()
    {
        if (ApplyFilterScheduled)
            return;

        ApplyFilterScheduled = true;
        DService.Framework.RunOnTick(() =>
        {
            ApplyFilterScheduled = false;
            ApplyFilter();
        }, delayTicks: 1);
    }

    private void ApplyFilter()
    {
        UpdateCategoryFilter(TargetsCategory, "目标占位符（智能目标）", TargetRowNodes);
        UpdateCategoryFilter(IfCategory, "/if 条件语法", IfRowNodes);
        UpdateCategoryFilter(ExtraCategory, "扩展命令", ExtraRowNodes);

        ScrollingArea?.ContentNode.RefreshLayout();
    }

    private void UpdateCategoryFilter(TreeListCategoryNode? category, string baseTitle, List<(HelpRow Row, ResNode Node)> rows)
    {
        if (category == null)
            return;

        var matched = 0;
        foreach (var (row, node) in rows)
        {
            var visible = IsMatchFilter(row);
            node.IsVisible = visible;
            if (visible)
                matched++;
        }

        category.String = $"{baseTitle}（匹配 {matched}/{rows.Count}）";
        category.RecalculateLayout();
    }

    private bool IsMatchFilter(HelpRow row)
    {
        var filter = HelpFilter;
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        filter = filter.Trim();
        return row.Syntax.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               row.Description.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               row.Example.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddTableHeader(TreeListCategoryNode category)
    {
        category.AddNode(new HelpTableHeaderNode());
    }

    private static ResNode AddHelpRow(TreeListCategoryNode category, HelpRow rowData)
    {
        var row = new HelpTableRowNode(rowData);
        category.AddNode(row);
        return row;
    }

    private static readonly HelpRow[] TargetHelpRows =
    [
        new("<party.minHp>", "小队中血量百分比最低的目标", "/ac 治疗 <party.minHp>"),
        new("<party.maxHp>", "小队中血量百分比最高的目标", "/ac 神明 <party.maxHp>"),
        new("<party.job=WHM>", "按职业名/简称匹配小队成员", "/ac 安慰之心 <party.job=WHM>"),
        new("<party.role=tank>", "按职责匹配小队成员（tank/healer/dps/ranged）", "/ac 鼓舞激励之策 <party.role=tank>"),
        new("<party.buff=再生>", "匹配拥有指定 Buff（名称或ID）的队友", "/ac 神铸祷 <party.buff=再生>"),
        new("<party.buff!=再生>", "匹配未拥有指定 Buff（名称或ID）的队友", "/ac 再生 <party.buff!=再生>"),
        new("<party.1>", "按小队列表序号（1-8）", "/ac 水流幕 <party.1>"),
        new("<party.lowHp>", "小队中最低血（不含自己）", "/ac 救疗 <party.lowHp>"),
        new("<party.lowHpOrMe>", "小队中最低血（含自己）", "/ac 救疗 <party.lowHpOrMe>"),
        new("<party.dead>", "小队中已阵亡成员（默认按列表顺序）", "/ac 复活 <party.dead>"),
        new("<party.near>", "距离自己最近的小队成员（不含自己）", "/ac 鼓舞激励之策 <party.near>"),
        new("<party.far>", "距离自己最远的小队成员（不含自己）", "/ac 鼓舞激励之策 <party.far>"),
        new("<party.dispellable>", "小队中有可驱散状态的成员（不含自己）", "/ac 康复 <party.dispellable>"),
        new("<party.dispellableOrMe>", "小队中有可驱散状态的成员（含自己）", "/ac 康复 <party.dispellableOrMe>"),
        new("<party.status:123>", "小队中拥有指定状态ID的成员（不含自己）", "/ac 治疗 <party.status:123>"),
        new("<party.statusOrMe:123>", "小队中拥有指定状态ID的成员（含自己）", "/ac 治疗 <party.statusOrMe:123>"),
        new("<enemy.near>", "距离自己最近的敌人", "/ac 连击 <enemy.near>"),
        new("<enemy.far>", "距离自己最远的敌人", "/ac 乾坤斗气弹 <enemy.far>"),
        new("<enemy.lowHp>", "当前范围内最低血的敌人", "/ac 斩铁剑 <enemy.lowHp>"),
        new("<enemy.status:123>", "拥有指定状态ID的敌人", "/ac 斩铁剑 <enemy.status:123>"),
    ];

    private static readonly HelpRow[] IfHelpRows =
    [
        new("/if [combat=true] /echo ...", "战斗状态判断（true/false）", "/if [combat=true] /echo In combat"),
        new("/if [job=白魔法师] /echo ...", "职业判断（按当前职业名称）", "/if [job=白魔法师] /echo WHM"),
        new("/if [target.hp < 30] /ac ...", "目标条件：hp/mp/buff/name/exists/none", "/if [target.hp < 30] /ac 治疗 <t>"),
        new("/if [self.hp < 50] /ac ...", "自身条件：hp/mp/buff/name/exists/none", "/if [self.hp < 50] /ac 医济 <me>"),
        new("/if [focus.exists] /echo ...", "焦点条件：hp/mp/buff/name/exists/none", "/if [focus.exists] /echo has focus"),
        new("/if [party.minHp < 30] /ac ...", "小队条件：hp/minHp/count（整体）", "/if [party.minHp < 30] /ac 医养 <party.minHp>"),
        new("/if [party.2.hp < 30] /ac ...", "小队成员条件：party.N.hp/mp/buff/name/exists/none", "/if [party.2.hp < 30] /ac 治疗 <party.2>"),
        new("/if [target.buff=再生] /ac ...", "Buff 条件：buff=名称或ID（支持空格）", "/if [target.buff=再生] /ac 神铸祷 <t>"),
        new("/if [target.buff!=再生] /ac ...", "Buff 条件：buff!=名称或ID（支持空格）", "/if [target.buff!=再生] /ac 再生 <t>"),
        new("/if [target.name!=BOSS] /echo ...", "名称条件：name= / name!=（支持空格）", "/if [target.name!=BOSS] /echo Not boss"),
        new("/if [party.count >= 4] /echo ...", "队伍人数条件：party.count", "/if [party.count >= 4] /echo Party ready"),
        new("/if [party.hp < 50] /ac ...", "队伍整体血量条件：party.hp", "/if [party.hp < 50] /ac 治疗 <party.lowHp>"),
        new("比较运算符", "支持：<  >  <=  >=  =  !=", "hp != 100"),
        new("逻辑运算符", "支持：&&  ||  !（可用于组合条件）", "/if [self.hp < 50 && combat=true] /ac 神明"),
        new("order 选项", "多目标命中时的列表顺序：toTop/toBottom", "/if [party.dead order=toTop] /ac 复活 <party.dead>"),
    ];

    private static readonly HelpRow[] ExtraCommandRows =
    [
        new("/call 宏名称 [循环次数]", "以独立窗口运行指定宏；循环次数可选", "/call 自动上再生 4"),
        new("/close", "关闭独立窗口（仅独立窗口宏内有效）", "/close"),
    ];
    }
}
