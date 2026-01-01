using System;
using System.Collections.Generic;
using System.Numerics;
using DailyRoutines.Abstracts;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Classes;
using KamiToolKit.Nodes;

namespace DailyRoutines.ModulesPublic;

internal sealed class ListPanel : ResNode // 宏列表面板
{
    private readonly MacroConfig          ModuleConfig;
    private readonly DailyModuleBase Instance;

    private readonly List<ExtenMacroButtonNode> MacroButtonList = [];

    private ScrollingAreaNode<SimpleComponentNode>? MacroContainerNode;
    private HorizontalFlexNode?                     SearchContainerNode;
    private TextInputNode?                          SearchBoxNode;

    private string SearchText         = "";
    private bool   HasUnsavedNewMacro = false;

    public Action<int>?   OnMacroSelected;
    public System.Action? OnAddNewMacro;

    public ListPanel(MacroConfig config, DailyModuleBase instance)
    {
        ModuleConfig = config;
        Instance = instance;

        Position = new Vector2(0f, 0f);
        Size = new Vector2(257f, 580f);
        IsVisible = true;
    }

    public void Build()
    {
        SearchContainerNode = new HorizontalFlexNode
        {
            Position = new Vector2(0, -15),
            Size = new Vector2(257f, 32f),
            AlignmentFlags = FlexFlags.FitHeight | FlexFlags.FitWidth,
            IsVisible = true,
        };
        SearchContainerNode.AttachNode(this);

        SearchBoxNode = new TextInputNode
        {
            IsVisible = true,
            OnInputReceived = (input) =>
            {
                SearchText = SearchBoxNode.String.ToLower();
                RefreshMacroList();
            },
            PlaceholderString = "搜索名称/描述/内容",
            AutoSelectAll = true,
        };
        SearchContainerNode.AddNode(SearchBoxNode);

        MacroContainerNode = new ScrollingAreaNode<SimpleComponentNode>
        {
            IsVisible = true,
            ContentHeight = 0.0f,
            Position = new Vector2(0, 23f),
            Size = new Vector2(250f, 557f),
            ScrollSpeed = 24,
        };
        MacroContainerNode.AttachNode(this);

        RefreshMacroList();
    }

    public void RefreshMacroList()
    {
        foreach (var node in MacroButtonList)
        {
            node.DetachNode();
            node.Dispose();
        }
        MacroButtonList.Clear();
        MacroContainerNode.ContentHeight = 0.0f;

        for (var i = 0; i < ModuleConfig.ExtendMacroLists.Count; i++)
        {
            var macroIndex = i;
            var macro = ModuleConfig.ExtendMacroLists[i];

            if (!MatchesSearch(macro)) continue;

            var macroButton = new ExtenMacroButtonNode(macro)
            {
                Y = MacroContainerNode.ContentHeight,
                Width = MacroContainerNode.ContentNode.Width,
                Height = 60f,
                IsVisible = true,
            };
            macroButton.OnClick = () =>
            {
                ClearSelection();
                macroButton.IsSelected = true;
                OnMacroSelected?.Invoke(macroIndex);
            };

            MacroContainerNode.ContentHeight += macroButton.Height;
            macroButton.AttachNode(MacroContainerNode.ContentNode);
            MacroButtonList.Add(macroButton);
        }

        var newMacroPlaceholder = new ExtendMacro
        {
            Name = "点此添加新的宏",
            Description = "",
            IconID = 138,
            MacroLines = ""
        };

        var addButton = new ExtenMacroButtonNode(newMacroPlaceholder)
        {
            Y = MacroContainerNode.ContentHeight,
            Width = MacroContainerNode.ContentNode.Width,
            Height = 60f,
            IsVisible = true,
        };
        addButton.OnClick = () =>
        {
            if (!HasUnsavedNewMacro)
                OnAddNewMacro?.Invoke();
        };
        addButton.AddEvent(AtkEventType.MouseOver, () =>
        {
            if (HasUnsavedNewMacro)
                addButton.UpdateDisplay(102, "存在未修改的新宏", "请修改后再添加");
        });
        addButton.AddEvent(AtkEventType.MouseOut, () =>
        {
            addButton.UpdateDisplay(138, "点此添加新的宏", string.Empty);
        });

        MacroContainerNode.ContentHeight += addButton.Height;
        addButton.AttachNode(MacroContainerNode.ContentNode);
        MacroButtonList.Add(addButton);

        MacroContainerNode.ContentHeight += 15.0f;
    }

    public void SelectMacro(int index)
    {
        ClearSelection();
        if (index >= 0 && index < MacroButtonList.Count - 1)
            MacroButtonList[index].IsSelected = true;
    }

    public void SetHasUnmodifiedNewMacro(bool value)
    {
        HasUnsavedNewMacro = value;
    }

    public void UpdateMacroDisplay(int index, uint? iconID = null, string? name = null, string? description = null)
    {
        if (index >= 0 && index < MacroButtonList.Count - 1)
            MacroButtonList[index].UpdateDisplay(iconID, name, description);
    }

    private void ClearSelection()
    {
        foreach (var node in MacroButtonList)
            node.IsSelected = false;
    }

    private bool MatchesSearch(ExtendMacro macro)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        return macro.Name.ToLower().Contains(SearchText) ||
               macro.Description.ToLower().Contains(SearchText) ||
               macro.MacroLines.ToLower().Contains(SearchText);
    }
}
