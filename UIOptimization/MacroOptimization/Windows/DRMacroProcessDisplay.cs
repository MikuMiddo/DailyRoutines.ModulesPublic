using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Nodes;
using System.Text.RegularExpressions;
using System.Linq;

namespace DailyRoutines.ModulesPublic;

public unsafe partial class MacroOptimization
{
    internal sealed class DRMacroProcessDisplay : NativeAddon // 独立窗口显示宏执行进度
    {
    private ProgressPanel? ProgressPanel = null;

    private List<uint>           ParsedMacroActionIDs        = [];
    private List<string>         ParsedCommandTypes          = [];
    private List<bool>           ParsedHasTargetFlags        = [];
    private List<string>         ExecutingMacroLines         = [];
    private Dictionary<int, int> MacroLineToProgressIndexMap = [];

    private int DefaultInterval = 2500;
    private int TotalLoopCount  = 1;
    private int CompletionDelay = 1000;

    private MacroExecutor? MacroExecutorInstance = null;
    private TaskHelper?    TaskHelperInstance    = null;

    private TextButtonNode?    ExecuteButton                 = null;
    private TextButtonNode?    StopButton                    = null;
    private TextButtonNode?    PauseButton                   = null;
    private TextureButtonNode? InfiniteLoopButton            = null;
    private SimpleImageNode?   InfiniteLoopButtonBackground  = null;
    private NumericInputNode?  LoopCountInput                = null;
    private TextNode?          LoopCountLabel                = null;

    private bool ForceInfiniteLoop = false;

    protected override void OnSetup(AtkUnitBase* addon)
    {
        var maxNodes = ParsedMacroActionIDs.Count;
        var visibleRowCount = Math.Clamp(ParsedMacroActionIDs.Count, 1, 3);
        var panelHeight = 20f + visibleRowCount * 50f;

        ProgressPanel = new ProgressPanel(ModuleConfig, maxMacroLines: maxNodes)
        {
            Position = new(20f, 50f),
            Size = new(245f, panelHeight)
        };
        ProgressPanel.AttachNode(this);

        ProgressPanel.Build();
        ProgressPanel.SetMacroLines(ParsedMacroActionIDs, ParsedCommandTypes, ParsedHasTargetFlags);
        ProgressPanel.ResetProgress();

        var buttonY = 70f + (visibleRowCount * 50f);
        InfiniteLoopButton = new TextureButtonNode
        {
            Position = new(20f, buttonY),
            Size = new(24f, 24f),
            IsVisible = true,
            TexturePath = "ui/uld/CircleButtons_hr1.tex",
            TextureSize = new(28, 28),
            TextureCoordinates = new(112, 0),
            TextTooltip = "强制无限循环 (无视宏配置)",
            OnClick = () =>
            {
                ForceInfiniteLoop = !ForceInfiniteLoop;
                InfiniteLoopButtonBackground.IsVisible = ForceInfiniteLoop;
                InfiniteLoopButton.TextTooltip = ForceInfiniteLoop
                    ? "已启用强制无限循环 (点击取消)"
                    : "强制无限循环 (无视宏配置)";

                if (LoopCountLabel is { } label)
                    label.SeString = ForceInfiniteLoop ? "无限循环" : "剩余循环:";

                if (LoopCountInput is { } input)
                    input.Alpha = ForceInfiniteLoop ? 0.5f : 1.0f;
            }
        };
        InfiniteLoopButton.AttachNode(this);

        InfiniteLoopButtonBackground = new SimpleImageNode
        {
            Position = new(-2f, 0f),
            Size = new(24f, 24f),
            IsVisible = ForceInfiniteLoop,
            TexturePath = "ui/uld/circlebuttons_hr1.tex",
            TextureSize = new(28, 28),
            TextureCoordinates = new(84, 84),
        };
        InfiniteLoopButtonBackground.AttachNode(InfiniteLoopButton);

        ExecuteButton = new TextButtonNode
        {
            Position = new(160f, buttonY),
            Size = new(100, 24),
            IsVisible = true,
            String = "执行",
            OnClick = () =>
            {
                if (MacroExecutorInstance?.IsRunning != true) // 运行时禁用调整无限循环按钮
                {
                    StartMacro();
                    ExecuteButton.IsVisible = false;
                    StopButton.IsVisible = true;
                    PauseButton.IsVisible = true;

                    if (InfiniteLoopButton != null)
                        InfiniteLoopButton.IsEnabled = false;
                }
            }
        };
        ExecuteButton.AttachNode(this);

        StopButton = new TextButtonNode
        {
            Position = new(160f, buttonY),
            Size = new(50, 24),
            IsVisible = false,
            String = "停止",
            OnClick = () => // 恢复执行按钮
            {
                StopMacro();
                ExecuteButton.IsVisible = true;
                StopButton.IsVisible = false;
                PauseButton.IsVisible = false;
                PauseButton.String = "暂停";

                if (InfiniteLoopButton != null)
                    InfiniteLoopButton.IsEnabled = true;

                if (LoopCountInput is { } input)
                    input.Value = TotalLoopCount;
            }
        };
        StopButton.AttachNode(this);

        PauseButton = new TextButtonNode
        {
            Position = new(210f, buttonY),
            Size = new(50, 24),
            IsVisible = false,
            String = "暂停",
            OnClick = () =>
            {
                var isPaused = PauseButton.String == "继续";
                PauseButton.String = isPaused ? "暂停" : "继续";
                if (isPaused)
                    ResumeMacro();
                else
                    PauseMacro();
            }
        };
        PauseButton.AttachNode(this);

        LoopCountLabel = new TextNode
        {
            Position = new(45f, buttonY + 12f),
            SeString = ForceInfiniteLoop ? "无限循环" : "剩余循环:",
            FontSize = 12,
            AlignmentType = AlignmentType.Left,
            IsVisible = true
        };
        LoopCountLabel.AttachNode(this);

        LoopCountInput = new NumericInputNode
        {
            Position = new(100f, buttonY),
            Size = new(90, 24),
            IsVisible = true,
            Value = TotalLoopCount,
            Min = 0,
            Max = 9999,
            Step = 1,
            Alpha = ForceInfiniteLoop ? 0.5f : 1.0f,  // 根据状态初始化透明度
            OnValueUpdate = (newValue) =>
            {
                TotalLoopCount = newValue;
            }
        };
        LoopCountInput.AddButton.IsVisible = false;
        LoopCountInput.SubtractButton.IsVisible = false;
        unsafe // 调整输入框的碰撞范围
        {
            AtkResNode* collisionNode = LoopCountInput.CollisionNode;
            collisionNode->Width = (ushort)(LoopCountInput.Size.X - 56);
        }

        LoopCountInput.AttachNode(this); 
    }

    public void OpenWithExtendMacro(ExtendMacro macro, int? loopCount = null)
    {
        DefaultInterval = macro.DefaultInterval;
        TotalLoopCount = loopCount ?? macro.LoopCount; // 使用传入的循环次数，如果没有则使用宏配置的循环次数
        CompletionDelay = macro.CompletionDelay;

        ForceInfiniteLoop = TotalLoopCount == 0; // 如果默认宏设置的循环次数为0，初始化时就启用无限循环
        ExecutingMacroLines = Regex.Split(macro.MacroLines, "\r\n|\r|\n").Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

        MacroExecutor.ParseMacroLines(ExecutingMacroLines,
            out var actionIDs,
            out var commandTypes,
            out var hasTargetFlags,
            out var lineToProgressMap,
            out _);

        ParsedMacroActionIDs = actionIDs;
        ParsedCommandTypes = commandTypes;
        ParsedHasTargetFlags = hasTargetFlags;
        MacroLineToProgressIndexMap = lineToProgressMap;

        var visibleRowCount = Math.Clamp(ParsedMacroActionIDs.Count, 1, 3);
        var windowHeight = 130f + (visibleRowCount * 50f); // 130 = 顶部+按钮+边距, 50 = 每行高度

        Size = new(300, windowHeight);

        Open();
        if (LoopCountInput is { } loopInput)
            loopInput.Value = TotalLoopCount;
    }

    // 开始执行宏
    public void StartMacro()
    {
        if (MacroExecutorInstance?.IsRunning == true) return;

        TaskHelperInstance = new TaskHelper();

        var actualLoopCount = ForceInfiniteLoop ? 0 : TotalLoopCount;

        MacroExecutorInstance = new MacroExecutor();
        MacroExecutorInstance.Initialize(
            ExecutingMacroLines,
            DefaultInterval,
            actualLoopCount,
            CompletionDelay
        );

        MacroExecutorInstance.SetDisplayWindow(this); // 传递当前独立窗口实例
        MacroExecutorInstance.OnUpdateProgress = (progressIndex, actionID) =>
        {
            ProgressPanel?.SetCurrentProgress(progressIndex);
            ProgressPanel?.UpdateProgress(progressIndex, 0f);

            var completedCount = Math.Max(0, progressIndex);
            var totalCount = ParsedMacroActionIDs.Count;
            var overallProgress = (float)completedCount / totalCount;
            ProgressPanel?.UpdateOverallProgress(overallProgress, completedCount);
        };
        MacroExecutorInstance.OnSkipLine = (progressIndex) =>
        {
            ProgressPanel?.UpdateProgress(progressIndex, 1f);
            ProgressPanel?.MarkProgressAsSkipped(progressIndex);

            var completedCount = Math.Max(0, progressIndex + 1); // +1 因为这一行已经完成(虽然被跳过)
            var totalCount = ParsedMacroActionIDs.Count;
            var overallProgress = (float)completedCount / totalCount;
            ProgressPanel?.UpdateOverallProgress(overallProgress, completedCount);
        };

        MacroExecutorInstance.OnUpdateConditionStatus = (progressIndex, conditionStatus) => ProgressPanel?.UpdateConditionStatus(progressIndex, conditionStatus);

        MacroExecutorInstance.OnUpdateTargetStatus = (progressIndex, targetStatus) => ProgressPanel?.UpdateTargetStatus(progressIndex, targetStatus);

        MacroExecutorInstance.OnLoopComplete = () =>
        {
            MacroExecutorInstance.GetCurrentProgressInfo(out var progressIndex, out _, out _); // 先将最后一个进度条设置为绿色（如果未被跳过）
            if (progressIndex >= 0 && progressIndex < ParsedMacroActionIDs.Count)
            {
                if (ProgressPanel?.IsProgressSkipped(progressIndex) == false) // 只有未被跳过的进度条才设置为绿色
                {
                    ProgressPanel?.UpdateProgress(progressIndex, 1f);
                    ProgressPanel?.SetProgressColor(progressIndex, KnownColor.Green);
                }
            }

            ProgressPanel?.UpdateOverallProgress(1f, ParsedMacroActionIDs.Count);
            ProgressPanel?.SetOverallProgressColor(KnownColor.Green);

            if (LoopCountInput != null && MacroExecutorInstance != null)
                LoopCountInput.Value = MacroExecutorInstance.RemainingLoopCount;

            var visualDelay = CompletionDelay > 0 ? CompletionDelay : DefaultInterval;

            TaskHelperInstance?.DelayNext(visualDelay);
            TaskHelperInstance?.Enqueue(() =>
            {
                ProgressPanel?.ResetProgress();
                ProgressPanel?.SetOverallProgressColor(KnownColor.Green);
            });
        };

        MacroExecutorInstance.OnAllComplete = () =>
        {
            MacroExecutorInstance.GetCurrentProgressInfo(out var progressIndex, out _, out _); // 先将最后一个进度条设置为绿色（如果未被跳过）
            if (progressIndex >= 0 && progressIndex < ParsedMacroActionIDs.Count)
            {
                if (ProgressPanel?.IsProgressSkipped(progressIndex) == false) // 只有未被跳过的进度条才设置为绿色
                {
                    ProgressPanel?.UpdateProgress(progressIndex, 1f);
                    ProgressPanel?.SetProgressColor(progressIndex, KnownColor.Green);
                }
            }

            ProgressPanel?.UpdateOverallProgress(1f, ParsedMacroActionIDs.Count);
            ProgressPanel?.SetOverallProgressColor(KnownColor.Green);

            MacroExecutorInstance?.Stop();
            DService.Framework.Update -= ProgressBarUpdate;

            ExecuteButton.IsVisible = true;
            StopButton.IsVisible = false;
            PauseButton.IsVisible = false;
            PauseButton.String = "暂停";

            if (InfiniteLoopButton != null)
                InfiniteLoopButton.IsEnabled = true;

            if (LoopCountInput is { } input)
                input.Value = TotalLoopCount;

            ProgressPanel?.ResetProgress();
        };

        ProgressPanel?.ResetProgress();
        ProgressPanel?.SetOverallProgressColor(KnownColor.Green);

        MacroExecutorInstance.Start();

        DService.Framework.Update += ProgressBarUpdate;
    }

    public void ProgressBarUpdate(IFramework framework)
    {
        if (MacroExecutorInstance == null) return;

        MacroExecutorInstance.GetCurrentProgressInfo(out var progressIndex, out var actionID, out var progress);

        if (actionID == 0 || progressIndex < 0) return;

        ProgressPanel?.UpdateProgress(progressIndex, progress);
        //if (progress > 0.9) // 因为游戏本身时延与滑步原因 这个值可能在0.9x左右就可以滑步
        //    ProgressPanel?.SetProgressColor(progressIndex, KnownColor.Green);
    }

    public void PauseMacro()
    {
        MacroExecutorInstance?.Pause();
        ProgressPanel?.SetOverallProgressColor(KnownColor.Yellow);
    }

    public void ResumeMacro()
    {
        MacroExecutorInstance?.Resume();
        ProgressPanel?.SetOverallProgressColor(KnownColor.Green);
    }

    public void StopMacro()
    {
        MacroExecutorInstance?.Stop();
        ProgressPanel?.ResetProgress();
    }

    public void CloseWindow()
    {
        StopMacro();
        lock (StandaloneProcessDisplaysLock)
            StandaloneProcessDisplays.Remove(this);
        Dispose();
    }

    protected override void OnFinalize(AtkUnitBase* addon)
    {
        if (MacroExecutorInstance?.IsRunning == true)
            MacroExecutorInstance.Stop();

        lock (StandaloneProcessDisplaysLock)
            StandaloneProcessDisplays.Remove(this);

        DService.Framework.Update -= ProgressBarUpdate;

        ProgressPanel?.DisposePanel();
        ProgressPanel?.DetachNode();
        ProgressPanel = null;

        ExecuteButton?.DetachNode();
        ExecuteButton = null;

        StopButton?.DetachNode();
        StopButton = null;

        PauseButton?.DetachNode();
        PauseButton = null;

        InfiniteLoopButton?.DetachNode();
        InfiniteLoopButton = null;

        InfiniteLoopButtonBackground?.DetachNode();
        InfiniteLoopButtonBackground = null;

        LoopCountLabel?.DetachNode();
        LoopCountLabel = null;

        LoopCountInput?.DetachNode();
        LoopCountInput = null;

        TaskHelperInstance?.Abort();
        TaskHelperInstance = null;

        MacroExecutorInstance = null;

        base.OnFinalize(addon);
    }
    }
}
