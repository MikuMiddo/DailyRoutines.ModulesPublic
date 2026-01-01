using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DailyRoutines.Abstracts;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Nodes;

namespace DailyRoutines.ModulesPublic;

public unsafe partial class MacroOptimization
{
    internal sealed class DRMacroExtendDisplay(DailyModuleBase instance, TaskHelper taskHelper, DRMacroSettings macroSettings) : NativeAddon
    {
    private readonly DailyModuleBase Instance           = instance;
    private readonly TaskHelper      TaskHelper        = taskHelper;
    private readonly DRMacroSettings MacroSettingsAddon = macroSettings;

    private List<uint>   ParsedMacroActionIDs  = [];
    private List<string> ParsedCommandTypes    = [];
    private List<bool>   ParsedHasTargetFlags  = [];
    private List<string> ExecutingMacroLines   = [];
    private List<string> RecordedMacroLines    = [];
    private Dictionary<int, int> MacroLineToProgressIndexMap = [];

    private ResNode?       MainContainerNode;
    private ListPanel?     ListPanel;
    private InfoPanel?     InfoPanel;
    private ProgressPanel? ProgressPanel;

    private string MacroContentBuffer = "";
    private string LastMiconContent   = "";

    private int SelectedMacroIndex = 0;
    private int TotalLoopCount     = 1;

    private uint LastClassJobID = 0;

    private MacroExecutor? MacroExecutorInstance = null;
    private TaskHelper?    TaskHelperInstance    = null;

    public bool IsRecording { get; private set; } = false;
    protected override void OnSetup(AtkUnitBase* addon)
    {
        MainContainerNode = new ResNode
        {
            Position = ContentStartPosition,
            Size = ContentSize,
            IsVisible = true,
        };
        MainContainerNode.AttachNode(this);

        ListPanel = new ListPanel(ModuleConfig, Instance);
        InfoPanel = new InfoPanel(ModuleConfig, Instance);
        ProgressPanel = new ProgressPanel(ModuleConfig);

        ListPanel.AttachNode(MainContainerNode);
        InfoPanel.AttachNode(MainContainerNode);
        ProgressPanel.AttachNode(MainContainerNode);

        ListPanel.Build();
        InfoPanel.Build();
        ProgressPanel.Build();

        ListPanel.OnMacroSelected = HandleMacroSelected;
        ListPanel.OnAddNewMacro = HandleAddNewMacro;

        InfoPanel.OnExecuteMacro = HandleExecuteMacro;
        InfoPanel.OnStopMacro = HandleStopMacro;
        InfoPanel.OnPauseMacro = HandlePauseMacro;
        InfoPanel.OnDeleteMacro = HandleDeleteMacro;
        InfoPanel.OnMacroContentChanged = HandleMacroContentChanged;
        InfoPanel.OnOpenMacroSettings = MacroSettingsAddon.OpenWithMacroIndex;
        InfoPanel.OnToggleRecording = HandleToggleRecording;
        InfoPanel.OnMacroNameChanged = new Action<int, string>((index, name) =>
        {
            ListPanel.UpdateMacroDisplay(index, name: name);
            ListPanel.SetHasUnmodifiedNewMacro(false);
        });
        InfoPanel.OnMacroDescriptionChanged = new Action<int, string>((index, desc) =>
        {
            ListPanel.UpdateMacroDisplay(index, description: desc);
            ListPanel.SetHasUnmodifiedNewMacro(false);
        });
        InfoPanel.OnMacroIconChanged = new Action<int, uint>((index, iconID) =>
        {
            ListPanel.UpdateMacroDisplay(index, iconID: iconID);
            ListPanel.SetHasUnmodifiedNewMacro(false);
        });

        DService.Framework.Update += ProgressBarUpdate;

        if (ModuleConfig.ExtendMacroLists.Count > 0) // 初始化选择第一个宏
        {
            ListPanel.SelectMacro(0);
            HandleMacroSelected(0);
        }
    }

    private void HandleMacroSelected(int index)
    {
        SelectedMacroIndex = index;
        MacroContentBuffer = ModuleConfig.ExtendMacroLists[index].MacroLines;

        UpdateMacroLines();

        InfoPanel.SetMacroIndex(index);
        InfoPanel.SetTextBuffer(MacroContentBuffer);
        InfoPanel.SetMacroData(ParsedMacroActionIDs, ParsedCommandTypes);
        InfoPanel.RefreshDetailEditNode();

        ProgressPanel.SetMacroLines(ParsedMacroActionIDs, ParsedCommandTypes, ParsedHasTargetFlags);

        if (MacroSettingsAddon.IsOpen) // 如果设置窗口打开，同步更新当前宏索引和显示
        {
            MacroSettingsAddon.CurrentMacroIndex = index;
            MacroSettingsAddon.UpdateDisplay();
        }
    }

    private void HandleAddNewMacro()
    {
        var newMacro = new ExtendMacro
        {
            Name = "新宏",
            Description = "新建的宏",
            IconID = 66001,
            MacroLines = ""
        };
        ModuleConfig.ExtendMacroLists.Add(newMacro);
        ListPanel.RefreshMacroList();
        ListPanel.SelectMacro(ModuleConfig.ExtendMacroLists.Count - 1);
        HandleMacroSelected(ModuleConfig.ExtendMacroLists.Count - 1);
        ListPanel.SetHasUnmodifiedNewMacro(true);
    }

    private void HandleExecuteMacro(int macroIndex)
    {
        var currentMacro = ModuleConfig.ExtendMacroLists[macroIndex];
        var loopCount = InfoPanel.ForceInfiniteLoop ? 0 : (currentMacro.IsLoopEnabled ? currentMacro.LoopCount : 1);
        TotalLoopCount = loopCount;

        ExecutingMacroLines = Regex.Split(MacroContentBuffer, "\r\n|\r|\n")
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        MacroExecutorInstance = new MacroExecutor();
        MacroExecutorInstance.Initialize(
            ExecutingMacroLines,
            currentMacro.DefaultInterval,
            TotalLoopCount,
            currentMacro.CompletionDelay
        );

        TaskHelperInstance = new TaskHelper();

        MacroExecutorInstance.SetDisplayWindow(null); // null 表示主窗口
        MacroExecutorInstance.OnUpdateProgress = (progressIndex, actionID) =>
        {
            ProgressPanel.SetCurrentProgress(progressIndex);

            var completedCount = Math.Max(0, progressIndex);
            var totalCount = ParsedMacroActionIDs.Count;
            var overallProgress = (float)completedCount / totalCount;
            ProgressPanel.UpdateOverallProgress(overallProgress, completedCount);
        };
        MacroExecutorInstance.OnSkipLine = (progressIndex) =>
        {
            ProgressPanel.UpdateProgress(progressIndex, 1f);
            ProgressPanel.MarkProgressAsSkipped(progressIndex);

            var completedCount = Math.Max(0, progressIndex + 1); // +1 因为这一行已经完成(虽然被跳过)
            var totalCount = ParsedMacroActionIDs.Count;
            var overallProgress = (float)completedCount / totalCount;
            ProgressPanel.UpdateOverallProgress(overallProgress, completedCount);
        };
        MacroExecutorInstance.OnUpdateConditionStatus = ProgressPanel.UpdateConditionStatus;
        MacroExecutorInstance.OnUpdateTargetStatus = ProgressPanel.UpdateTargetStatus;
        MacroExecutorInstance.OnLoopComplete = () =>
        {
            MacroExecutorInstance.GetCurrentProgressInfo(out var progressIndex, out _, out _); // 先将最后一个进度条设置为绿色（如果未被跳过）
            if (progressIndex >= 0 && progressIndex < ParsedMacroActionIDs.Count)
            {
                if (!ProgressPanel.IsProgressSkipped(progressIndex)) // 只有未被跳过的进度条才设置为绿色
                {
                    ProgressPanel.UpdateProgress(progressIndex, 1f);
                    ProgressPanel.SetProgressColor(progressIndex, KnownColor.Green);
                }
            }

            ProgressPanel.UpdateOverallProgress(1f, ParsedMacroActionIDs.Count);
            ProgressPanel.SetOverallProgressColor(KnownColor.Green);

            var currentMacro = ModuleConfig.ExtendMacroLists[SelectedMacroIndex]; // 延迟重置进度条，保持完成状态显示
            var visualDelay = currentMacro.CompletionDelay > 0 ? currentMacro.CompletionDelay : currentMacro.DefaultInterval;

            TaskHelperInstance?.DelayNext(visualDelay);
            TaskHelperInstance?.Enqueue(() =>
            {
                ProgressPanel.ResetProgress();
                ProgressPanel.SetOverallProgressColor(KnownColor.Green);
            });
        };

        MacroExecutorInstance.OnAllComplete = () =>
        {
            MacroExecutorInstance.GetCurrentProgressInfo(out var progressIndex, out _, out _); // 先将最后一个进度条设置为绿色（如果未被跳过）
            if (progressIndex >= 0 && progressIndex < ParsedMacroActionIDs.Count)
            {
                if (!ProgressPanel.IsProgressSkipped(progressIndex)) // 只有未被跳过的进度条才设置为绿色
                {
                    ProgressPanel.UpdateProgress(progressIndex, 1f);
                    ProgressPanel.SetProgressColor(progressIndex, KnownColor.Green);
                }
            }

            ProgressPanel.UpdateOverallProgress(1f, ParsedMacroActionIDs.Count);
            ProgressPanel.SetOverallProgressColor(KnownColor.Green);
            MacroExecutorInstance?.Stop();
            InfoPanel.ShowExecuteButton();

            ProgressPanel.ResetProgress();
        };

        InfoPanel.ShowStopPauseButtons();

        UpdateMacroLines();
        ProgressPanel.SetMacroLines(ParsedMacroActionIDs, ParsedCommandTypes, ParsedHasTargetFlags);
        ProgressPanel.ClearSkippedStates();
        ProgressPanel.SetOverallProgressColor(KnownColor.Green);

        MacroExecutorInstance.Start();
    }

    private void HandleStopMacro()
    {
        MacroExecutorInstance?.Stop();
 
        TaskHelperInstance?.Abort(); // 清理 TaskHelper
        TaskHelperInstance = null;

        ProgressPanel.ResetProgress(); // 重置进度面板并恢复执行按钮显示
        InfoPanel.ShowExecuteButton();
    }

    public void RecordAction(uint actionID)
    {
        if (!IsRecording) return;
        if (SelectedMacroIndex < 0 || SelectedMacroIndex >= ModuleConfig.ExtendMacroLists.Count) return;

        var actionName = LuminaWrapper.GetActionName(actionID);
        if (string.IsNullOrEmpty(actionName)) return;

        var macroLine = $"/ac {actionName}";

        var currentContent = InfoPanel.GetTextBuffer();
        var newContent = string.IsNullOrEmpty(currentContent)
            ? macroLine
            : currentContent + "\r" + macroLine;

        InfoPanel.SetTextBuffer(newContent);
        MacroContentBuffer = newContent;

        ModuleConfig.ExtendMacroLists[SelectedMacroIndex].MacroLines = newContent;
        ModuleConfig.Save(Instance);
    }

    private void HandlePauseMacro(bool isPaused)
    {
        if (isPaused)
        {
            MacroExecutorInstance?.Pause();
            ProgressPanel.SetOverallProgressColor(KnownColor.Yellow);
        }
        else
        {
            MacroExecutorInstance?.Resume();
            ProgressPanel.SetOverallProgressColor(KnownColor.Green);
        }
    }

    private void HandleToggleRecording()
    {
        if (IsRecording)
        {
            IsRecording = false;
            InfoPanel.SetRecordingState(false);
            UpdateMacroLines();
            InfoPanel.SetMacroData(ParsedMacroActionIDs, ParsedCommandTypes);
            InfoPanel.RefreshDetailEditNode();
            ProgressPanel.SetMacroLines(ParsedMacroActionIDs, ParsedCommandTypes, ParsedHasTargetFlags);
        }
        else
        {
            IsRecording = true;
            InfoPanel.SetRecordingState(true);
        }
    }

    private void HandleDeleteMacro(int macroIndex)
    {
        if (macroIndex < 0 || macroIndex >= ModuleConfig.ExtendMacroLists.Count) return;

        ModuleConfig.ExtendMacroLists.RemoveAt(macroIndex);

        if (ModuleConfig.ExtendMacroLists.Count == 0) // 如果删除后列表为空，创建一个新宏
        {
            var newMacro = new ExtendMacro
            {
                Name = "新宏",
                Description = "新建的宏",
                IconID = 66001,
                MacroLines = ""
            };
            ModuleConfig.ExtendMacroLists.Add(newMacro);
        }

        if (SelectedMacroIndex >= ModuleConfig.ExtendMacroLists.Count) // 调整索引到有效范围
            SelectedMacroIndex = ModuleConfig.ExtendMacroLists.Count - 1;

        ListPanel.RefreshMacroList();
        ListPanel.SelectMacro(SelectedMacroIndex);
        HandleMacroSelected(SelectedMacroIndex);
    }

    private void HandleMacroContentChanged(string newContent)
    {
        MacroContentBuffer = newContent;

        var oldActionIDs = new List<uint>(ParsedMacroActionIDs);
        var oldCommandTypes = new List<string>(ParsedCommandTypes);
        var oldHasTargetFlags = new List<bool>(ParsedHasTargetFlags);

        UpdateMacroLines();

        if (SelectedMacroIndex >= 0 && SelectedMacroIndex < ModuleConfig.ExtendMacroLists.Count)
        {
            ModuleConfig.ExtendMacroLists[SelectedMacroIndex].MacroLines = MacroContentBuffer;
            ListPanel.SetHasUnmodifiedNewMacro(false);
            ModuleConfig.Save(Instance);

            if (!ParsedMacroActionIDs.SequenceEqual(oldActionIDs) ||
                !ParsedCommandTypes.SequenceEqual(oldCommandTypes) ||
                !ParsedHasTargetFlags.SequenceEqual(oldHasTargetFlags)) // 只有当解析结果真的改变时才更新UI
            {
                ProgressPanel.SetMacroLines(ParsedMacroActionIDs, ParsedCommandTypes, ParsedHasTargetFlags);
                InfoPanel.SetMacroData(ParsedMacroActionIDs, ParsedCommandTypes);
                InfoPanel.UpdateExecuteTime();
            }
        }
    }


    public void ProgressBarUpdate(IFramework framework)
    {
        var currentClassJobID = DService.ObjectTable.LocalPlayer?.ClassJob.RowId ?? 0; // 检测职业切换
        if (currentClassJobID != 0 && LastClassJobID != 0 && currentClassJobID != LastClassJobID)
        {
            ProgressPanel.ClearAllDragDropPayloads(); // 先清除旧职业的payload，防止原生Dragdropnode内容导致崩溃
            MacroCacheHelper.RebuildAdjustedActionCache();
            UpdateMacroLines();
            InfoPanel.SetMacroData(ParsedMacroActionIDs, ParsedCommandTypes);
            InfoPanel.UpdateExecuteTime();
            ProgressPanel.SetMacroLines(ParsedMacroActionIDs, ParsedCommandTypes, ParsedHasTargetFlags);
        }
        LastClassJobID = currentClassJobID;

        if (MacroExecutorInstance == null) return;

        MacroExecutorInstance.GetCurrentProgressInfo(out var progressIndex, out var actionID, out var progress);

        if (actionID == 0 || progressIndex < 0 || progressIndex >= ParsedMacroActionIDs.Count) return;

        ProgressPanel.UpdateProgress(progressIndex, progress);
        //if (progress > 0.9) // 因为游戏本身时延与滑步原因 这个值可能在0.9x左右就可以滑步 
        //    ProgressPanel.SetProgressColor(progressIndex, KnownColor.Green);
    }

    public void UpdateMacroLines()
    {
        var lines = Regex.Split(MacroContentBuffer, "\r\n|\r|\n")
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        MacroExecutor.ParseMacroLines(lines, out var SkillStrings, out var CommandTypes, out var HasTargetFlags, out var macroLineToProgressBarMap, out var MacroIconName);

        if (!ParsedMacroActionIDs.SequenceEqual(SkillStrings) ||
            !ParsedCommandTypes.SequenceEqual(CommandTypes) ||
            !ParsedHasTargetFlags.SequenceEqual(HasTargetFlags))
        {
            ParsedMacroActionIDs = SkillStrings;
            ParsedCommandTypes = CommandTypes;
            ParsedHasTargetFlags = HasTargetFlags;
            MacroLineToProgressIndexMap = macroLineToProgressBarMap;
        }

        if (MacroIconName != LastMiconContent)
        {
            LastMiconContent = MacroIconName;
            UpdateMacroIcon(MacroIconName);
        }
    }

    private void UpdateMacroIcon(string macroIconName)
    {
        uint iconID = 66001; // 默认图标

        if (!string.IsNullOrEmpty(macroIconName))
        {
            var foundIconID = MacroCacheHelper.FindMacroIconID(macroIconName, LocalPlayerState.ClassJobData);
            if (foundIconID != null)
                iconID = foundIconID.Value;
        }

        if (SelectedMacroIndex < 0 || SelectedMacroIndex >= ModuleConfig.ExtendMacroLists.Count)
            return;

        ModuleConfig.ExtendMacroLists[SelectedMacroIndex].IconID = iconID;

        ListPanel.UpdateMacroDisplay(SelectedMacroIndex, iconID: iconID);

        InfoPanel.UpdateIcon(iconID);
    }

    protected override void OnFinalize(AtkUnitBase* addon)
    {
        DService.Framework.Update -= ProgressBarUpdate;

        ListPanel?.DetachNode();
        ListPanel = null;

        InfoPanel?.DetachNode();
        InfoPanel = null;

        ProgressPanel?.DisposePanel();
        ProgressPanel?.DetachNode();
        ProgressPanel = null;

        MainContainerNode?.DetachNode();
        MainContainerNode = null;

        base.OnFinalize(addon);
    }
    }
}
