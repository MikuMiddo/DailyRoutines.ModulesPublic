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
        private readonly DailyModuleBase ModuleInstance = instance;
        private readonly TaskHelper      TaskHelperTemplate = taskHelper;
        private readonly DRMacroSettings MacroSettingsAddon = macroSettings;

        private List<uint>           ParsedMacroActionIDs = [];
        private List<string>         ParsedCommandTypes = [];
        private List<bool>           ParsedHasTargetFlags = [];
        private List<string>         ExecutingMacroLines = [];
        private Dictionary<int, int> MacroLineToProgressIndexMap = [];

        private ResNode?       MainContainerNode;
        private ListPanel?     MacroListPanel;
        private InfoPanel?     MacroInfoPanel;
        private ProgressPanel? MacroProgressPanel;

        private string MacroContentBuffer = string.Empty;
        private string LastMiconContent = string.Empty;

        private int  SelectedMacroIndex;
        private int  TotalLoopCount = 1;
        private uint LastClassJobID;

        private MacroExecutor? MacroExecutorInstance;
        private TaskHelper?    TaskHelperInstance;

        public bool IsRecording { get; private set; }

        protected override void OnSetup(AtkUnitBase* addon)
        {
            MainContainerNode = new ResNode
            {
                Position = ContentStartPosition,
                Size = ContentSize,
                IsVisible = true,
            };
            MainContainerNode.AttachNode(this);

            MacroListPanel = new ListPanel(ModuleConfig, ModuleInstance);
            MacroInfoPanel = new InfoPanel(ModuleConfig, ModuleInstance);
            MacroProgressPanel = new ProgressPanel(ModuleConfig);

            MacroListPanel.AttachNode(MainContainerNode);
            MacroInfoPanel.AttachNode(MainContainerNode);
            MacroProgressPanel.AttachNode(MainContainerNode);

            MacroListPanel.Build();
            MacroInfoPanel.Build();
            MacroProgressPanel.Build();

            MacroListPanel.OnMacroSelected = HandleMacroSelected;
            MacroListPanel.OnAddNewMacro = HandleAddNewMacro;

            MacroInfoPanel.OnExecuteMacro = HandleExecuteMacro;
            MacroInfoPanel.OnStopMacro = HandleStopMacro;
            MacroInfoPanel.OnPauseMacro = HandlePauseMacro;
            MacroInfoPanel.OnDeleteMacro = HandleDeleteMacro;
            MacroInfoPanel.OnMacroContentChanged = HandleMacroContentChanged;
            MacroInfoPanel.OnOpenMacroSettings = MacroSettingsAddon.OpenWithMacroIndex;
            MacroInfoPanel.OnToggleRecording = HandleToggleRecording;
            MacroInfoPanel.OnMacroNameChanged = (index, name) =>
            {
                MacroListPanel.UpdateMacroDisplay(index, name: name);
                MacroListPanel.SetHasUnmodifiedNewMacro(false);
            };
            MacroInfoPanel.OnMacroDescriptionChanged = (index, desc) =>
            {
                MacroListPanel.UpdateMacroDisplay(index, description: desc);
                MacroListPanel.SetHasUnmodifiedNewMacro(false);
            };
            MacroInfoPanel.OnMacroIconChanged = (index, iconID) =>
            {
                MacroListPanel.UpdateMacroDisplay(index, iconID: iconID);
                MacroListPanel.SetHasUnmodifiedNewMacro(false);
            };

            DService.Framework.Update += ProgressBarUpdate;

            if (ModuleConfig.ExtendMacroLists.Count > 0) // 初始化选择第一个宏
            {
                MacroListPanel.SelectMacro(0);
                HandleMacroSelected(0);
            }
        }

    private void HandleMacroSelected(int index)
    {
        SelectedMacroIndex = index;
        MacroContentBuffer = ModuleConfig.ExtendMacroLists[index].MacroLines;

        UpdateMacroLines();

        MacroInfoPanel.SetMacroIndex(index);
        MacroInfoPanel.SetTextBuffer(MacroContentBuffer);
        MacroInfoPanel.SetMacroData(ParsedMacroActionIDs, ParsedCommandTypes);
        MacroInfoPanel.RefreshDetailEditNode();

        MacroProgressPanel.SetMacroLines(ParsedMacroActionIDs, ParsedCommandTypes, ParsedHasTargetFlags);

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
        MacroListPanel.RefreshMacroList();
        MacroListPanel.SelectMacro(ModuleConfig.ExtendMacroLists.Count - 1);
        HandleMacroSelected(ModuleConfig.ExtendMacroLists.Count - 1);
        MacroListPanel.SetHasUnmodifiedNewMacro(true);
    }

    private void HandleExecuteMacro(int macroIndex)
    {
        var currentMacro = ModuleConfig.ExtendMacroLists[macroIndex];
        var loopCount = MacroInfoPanel.ForceInfiniteLoop ? 0 : (currentMacro.IsLoopEnabled ? currentMacro.LoopCount : 1);
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

        TaskHelperInstance = new() { TimeLimitMS = TaskHelperTemplate.TimeLimitMS };

        MacroExecutorInstance.SetDisplayWindow(null); // null 表示主窗口
        MacroExecutorInstance.OnUpdateProgress = (progressIndex, actionID) =>
        {
            MacroProgressPanel.SetCurrentProgress(progressIndex);

            var completedCount = Math.Max(0, progressIndex);
            var totalCount = ParsedMacroActionIDs.Count;
            var overallProgress = (float)completedCount / totalCount;
            MacroProgressPanel.UpdateOverallProgress(overallProgress, completedCount);
        };
        MacroExecutorInstance.OnSkipLine = (progressIndex) =>
        {
            MacroProgressPanel.UpdateProgress(progressIndex, 1f);
            MacroProgressPanel.MarkProgressAsSkipped(progressIndex);

            var completedCount = Math.Max(0, progressIndex + 1); // +1 因为这一行已经完成(虽然被跳过)
            var totalCount = ParsedMacroActionIDs.Count;
            var overallProgress = (float)completedCount / totalCount;
            MacroProgressPanel.UpdateOverallProgress(overallProgress, completedCount);
        };
        MacroExecutorInstance.OnUpdateConditionStatus = MacroProgressPanel.UpdateConditionStatus;
        MacroExecutorInstance.OnUpdateTargetStatus = MacroProgressPanel.UpdateTargetStatus;
        MacroExecutorInstance.OnLoopComplete = () =>
        {
            MacroExecutorInstance.GetCurrentProgressInfo(out var progressIndex, out _, out _); // 先将最后一个进度条设置为绿色（如果未被跳过）
            if (progressIndex >= 0 && progressIndex < ParsedMacroActionIDs.Count)
            {
                if (!MacroProgressPanel.IsProgressSkipped(progressIndex)) // 只有未被跳过的进度条才设置为绿色
                {
                    MacroProgressPanel.UpdateProgress(progressIndex, 1f);
                    MacroProgressPanel.SetProgressColor(progressIndex, KnownColor.Green);
                }
            }

            MacroProgressPanel.UpdateOverallProgress(1f, ParsedMacroActionIDs.Count);
            MacroProgressPanel.SetOverallProgressColor(KnownColor.Green);

            var currentMacro = ModuleConfig.ExtendMacroLists[SelectedMacroIndex]; // 延迟重置进度条，保持完成状态显示
            var visualDelay = currentMacro.CompletionDelay > 0 ? currentMacro.CompletionDelay : currentMacro.DefaultInterval;

            TaskHelperInstance?.DelayNext(visualDelay);
            TaskHelperInstance?.Enqueue(() =>
            {
                MacroProgressPanel.ResetProgress();
                MacroProgressPanel.SetOverallProgressColor(KnownColor.Green);
            });
        };

        MacroExecutorInstance.OnAllComplete = () =>
        {
            MacroExecutorInstance.GetCurrentProgressInfo(out var progressIndex, out _, out _); // 先将最后一个进度条设置为绿色（如果未被跳过）
            if (progressIndex >= 0 && progressIndex < ParsedMacroActionIDs.Count)
            {
                if (!MacroProgressPanel.IsProgressSkipped(progressIndex)) // 只有未被跳过的进度条才设置为绿色
                {
                    MacroProgressPanel.UpdateProgress(progressIndex, 1f);
                    MacroProgressPanel.SetProgressColor(progressIndex, KnownColor.Green);
                }
            }

            MacroProgressPanel.UpdateOverallProgress(1f, ParsedMacroActionIDs.Count);
            MacroProgressPanel.SetOverallProgressColor(KnownColor.Green);
            MacroExecutorInstance?.Stop();
            MacroInfoPanel.ShowExecuteButton();

            MacroProgressPanel.ResetProgress();
        };

        MacroInfoPanel.ShowStopPauseButtons();

        UpdateMacroLines();
        MacroProgressPanel.SetMacroLines(ParsedMacroActionIDs, ParsedCommandTypes, ParsedHasTargetFlags);
        MacroProgressPanel.ClearSkippedStates();
        MacroProgressPanel.SetOverallProgressColor(KnownColor.Green);

        MacroExecutorInstance.Start();
    }

    private void HandleStopMacro()
    {
        MacroExecutorInstance?.Stop();
 
        TaskHelperInstance?.Abort(); // 清理 TaskHelper
        TaskHelperInstance = null;

        MacroProgressPanel.ResetProgress(); // 重置进度面板并恢复执行按钮显示
        MacroInfoPanel.ShowExecuteButton();
    }

    public void RecordAction(uint actionID)
    {
        if (!IsRecording) return;
        if (SelectedMacroIndex < 0 || SelectedMacroIndex >= ModuleConfig.ExtendMacroLists.Count) return;

        var actionName = LuminaWrapper.GetActionName(actionID);
        if (string.IsNullOrEmpty(actionName)) return;

        var macroLine = $"/ac {actionName}";

        var currentContent = MacroInfoPanel.GetTextBuffer();
        var newContent = string.IsNullOrEmpty(currentContent)
            ? macroLine
            : currentContent + "\r" + macroLine;

        MacroInfoPanel.SetTextBuffer(newContent);
        MacroContentBuffer = newContent;

        ModuleConfig.ExtendMacroLists[SelectedMacroIndex].MacroLines = newContent;
        ModuleConfig.Save(ModuleInstance);
    }

    private void HandlePauseMacro(bool isPaused)
    {
        if (isPaused)
        {
            MacroExecutorInstance?.Pause();
            MacroProgressPanel.SetOverallProgressColor(KnownColor.Yellow);
        }
        else
        {
            MacroExecutorInstance?.Resume();
            MacroProgressPanel.SetOverallProgressColor(KnownColor.Green);
        }
    }

    private void HandleToggleRecording()
    {
        if (IsRecording)
        {
            IsRecording = false;
            MacroInfoPanel.SetRecordingState(false);
            UpdateMacroLines();
            MacroInfoPanel.SetMacroData(ParsedMacroActionIDs, ParsedCommandTypes);
            MacroInfoPanel.RefreshDetailEditNode();
            MacroProgressPanel.SetMacroLines(ParsedMacroActionIDs, ParsedCommandTypes, ParsedHasTargetFlags);
        }
        else
        {
            IsRecording = true;
            MacroInfoPanel.SetRecordingState(true);
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

        MacroListPanel.RefreshMacroList();
        MacroListPanel.SelectMacro(SelectedMacroIndex);
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
            MacroListPanel.SetHasUnmodifiedNewMacro(false);
            ModuleConfig.Save(ModuleInstance);

            if (!ParsedMacroActionIDs.SequenceEqual(oldActionIDs) ||
                !ParsedCommandTypes.SequenceEqual(oldCommandTypes) ||
                !ParsedHasTargetFlags.SequenceEqual(oldHasTargetFlags)) // 只有当解析结果真的改变时才更新UI
            {
                MacroProgressPanel.SetMacroLines(ParsedMacroActionIDs, ParsedCommandTypes, ParsedHasTargetFlags);
                MacroInfoPanel.SetMacroData(ParsedMacroActionIDs, ParsedCommandTypes);
                MacroInfoPanel.UpdateExecuteTime();
            }
        }
    }


    private void ProgressBarUpdate(IFramework framework)
    {
        var currentClassJobID = DService.ObjectTable.LocalPlayer?.ClassJob.RowId ?? 0; // 检测职业切换
        if (currentClassJobID != 0 && LastClassJobID != 0 && currentClassJobID != LastClassJobID)
        {
            MacroProgressPanel.ClearAllDragDropPayloads(); // 先清除旧职业的 payload，防止原生 DragDropNode 内容导致崩溃
            MacroCacheHelper.RebuildAdjustedActionCache();
            UpdateMacroLines();
            MacroInfoPanel.SetMacroData(ParsedMacroActionIDs, ParsedCommandTypes);
            MacroInfoPanel.UpdateExecuteTime();
            MacroProgressPanel.SetMacroLines(ParsedMacroActionIDs, ParsedCommandTypes, ParsedHasTargetFlags);
        }
        LastClassJobID = currentClassJobID;

        if (MacroExecutorInstance == null) return;

        MacroExecutorInstance.GetCurrentProgressInfo(out var progressIndex, out var actionID, out var progress);

        if (actionID == 0 || progressIndex < 0 || progressIndex >= ParsedMacroActionIDs.Count) return;

        MacroProgressPanel.UpdateProgress(progressIndex, progress);
        //if (progress > 0.9) // 因为游戏本身时延与滑步原因 这个值可能在0.9x左右就可以滑步 
        //    MacroProgressPanel.SetProgressColor(progressIndex, KnownColor.Green);
    }

    private void UpdateMacroLines()
    {
        var lines = Regex.Split(MacroContentBuffer, "\r\n|\r|\n")
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        MacroExecutor.ParseMacroLines(lines,
                                     out var skillStrings,
                                     out var commandTypes,
                                     out var hasTargetFlags,
                                     out var macroLineToProgressBarMap,
                                     out var macroIconName);

        if (!ParsedMacroActionIDs.SequenceEqual(skillStrings) ||
            !ParsedCommandTypes.SequenceEqual(commandTypes) ||
            !ParsedHasTargetFlags.SequenceEqual(hasTargetFlags))
        {
            ParsedMacroActionIDs = skillStrings;
            ParsedCommandTypes = commandTypes;
            ParsedHasTargetFlags = hasTargetFlags;
            MacroLineToProgressIndexMap = macroLineToProgressBarMap;
        }

        if (macroIconName != LastMiconContent)
        {
            LastMiconContent = macroIconName;
            UpdateMacroIcon(macroIconName);
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

        MacroListPanel.UpdateMacroDisplay(SelectedMacroIndex, iconID: iconID);

        MacroInfoPanel.UpdateIcon(iconID);
    }

    protected override void OnFinalize(AtkUnitBase* addon)
    {
        DService.Framework.Update -= ProgressBarUpdate;

        MacroListPanel?.DetachNode();
        MacroListPanel = null;

        MacroInfoPanel?.DetachNode();
        MacroInfoPanel = null;

        MacroProgressPanel?.DisposePanel();
        MacroProgressPanel?.DetachNode();
        MacroProgressPanel = null;

        MainContainerNode?.DetachNode();
        MainContainerNode = null;

        base.OnFinalize(addon);
    }
    }
}
