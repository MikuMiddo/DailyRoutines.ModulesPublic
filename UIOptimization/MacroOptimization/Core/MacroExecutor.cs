using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFAction = Lumina.Excel.Sheets.Action;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using Lumina.Excel.Sheets;

namespace DailyRoutines.ModulesPublic;

public unsafe partial class MacroOptimization
{
    internal sealed class MacroExecutor // 宏执行器 - 包含所有执行逻辑和状态
    {
    #region 字段

    private List<string>         MacroLines        = []; // 宏数据
    private Dictionary<int, int> LineToProgressMap = [];
    private List<uint>           ParsedActionIDs   = [];

    private int      CurrentLineIndex         = 0; // 执行状态
    private DateTime LastExecuteTime          = DateTime.MinValue;
    private bool     IsPaused                 = false;
    private bool     IsWaitingDelay           = false;
    private bool     IsWaitingActionExecution = false;  // 等待技能执行确认
    private DateTime LastCommandSendTime      = DateTime.MinValue;
    private uint     PendingActionID          = 0;

    private uint     CurrentActionID      = 0;  // 进度追踪
    private DateTime GCDStartTime         = DateTime.MinValue;
    private int      CurrentProgressIndex = -1;

    private int DefaultInterval = 2500; // 配置
    private int TotalLoopCount  = 1;
    private int CompletionDelay = 0;
    private int CurrentLoopIteration = 0;

    private TaskHelper?                  TaskHelperInstance     = null; // 依赖项
    private IFramework.OnUpdateDelegate? FrameworkUpdateHandler = null;
    private DRMacroProcessDisplay?       CurrentDisplayWindow   = null;

    #endregion

    #region 属性和事件

    public bool IsRunning { get; private set; } = false;
    public int  RemainingLoopCount => TotalLoopCount == 0 ? 0 : Math.Max(0, TotalLoopCount - CurrentLoopIteration);

    public Action<int, uint>?   OnUpdateProgress        { get; set; }
    public Action<int>?         OnSkipLine              { get; set; }
    public Action<int, string>? OnUpdateConditionStatus { get; set; }
    public Action<int, string>? OnUpdateTargetStatus    { get; set; }
    public System.Action?       OnLoopComplete          { get; set; }
    public System.Action?       OnAllComplete           { get; set; }

    #endregion

    #region 生命周期管理

    public void SetDisplayWindow(DRMacroProcessDisplay? displayWindow) => CurrentDisplayWindow = displayWindow;

    public void Pause() => IsPaused = true;

    public void Resume() => IsPaused = false;

    public void Start()
    {
        if (IsRunning) return; // 避免重复注册

        TaskHelperInstance = new TaskHelper();

        IsRunning = true;
        IsPaused = false;
        CurrentLineIndex = 0;
        LastExecuteTime = DateTime.MinValue;

        FrameworkUpdateHandler = (framework) =>
        {
            if (!IsRunning || IsPaused) return;
            ExecuteNextLine();
        };
        DService.Framework.Update += FrameworkUpdateHandler;
    }

    public void Stop()
    {
        IsRunning = false;
        IsPaused = false;
        IsWaitingActionExecution = false;

        CurrentActionID = 0;
        PendingActionID = 0;
        CurrentProgressIndex = -1;

        GCDStartTime = DateTime.MinValue;
        LastCommandSendTime = DateTime.MinValue;

        TaskHelperInstance?.Abort();
        TaskHelperInstance = null;

        if (FrameworkUpdateHandler != null)
        {
            DService.Framework.Update -= FrameworkUpdateHandler;
            FrameworkUpdateHandler = null;
        }
    }

    public void Initialize(List<string> macroLines, int defaultInterval, int loopCount, int completionDelay)
    {
        MacroLines = macroLines;
        DefaultInterval = defaultInterval;
        TotalLoopCount = loopCount;
        CompletionDelay = completionDelay;

        ParseMacroLines(macroLines, out var actionIDs, out var commandTypes,
                        out _, out var lineToProgressMap, out _);

        LineToProgressMap = lineToProgressMap;
        ParsedActionIDs = actionIDs;

        CurrentLineIndex = 0;
        LastExecuteTime = DateTime.MinValue;
        CurrentLoopIteration = 0;
        IsWaitingDelay = false;
    }

    #endregion

    #region 执行与进度

    public float CalculateCurrentProgress()
    {
        if (CurrentActionID == 0 || GCDStartTime == DateTime.MinValue)
            return 0f;

        var actionType = CurrentActionID < 100000 ? ActionType.Action : ActionType.CraftAction;
        var elapsedMs = (DateTime.UtcNow - GCDStartTime).TotalMilliseconds;

        if (ModuleConfig.ActionCooldowns.TryGetValue(actionType, out var cooldowns) && // 优先使用录制的冷却时间
            cooldowns.TryGetValue(CurrentActionID, out var recordedCooldownMs))
            return (float)(elapsedMs / recordedCooldownMs);

        if (IsBattleSkill(CurrentActionID)) // 没有录制数据，使用默认值
        {
            var recastTime = ActionManager.Instance()->GetRecastTime(ActionType.Action, CurrentActionID);
            if (recastTime == 0) 
                recastTime = 2.5f; // 战斗技能使用固定2.5秒
            return (float)(elapsedMs / (recastTime * 1000));
        }
        else
            return (float)(elapsedMs / 3000); // 非战斗技能使用固定3秒
    }

    public void GetCurrentProgressInfo(out int progressIndex, out uint actionID, out float progress)
    {
        progressIndex = CurrentProgressIndex;
        actionID = CurrentActionID;
        progress = CalculateCurrentProgress();
    }

    public bool ExecuteNextLine()
    {
        if (IsPaused)
            return true;

        if (IsWaitingDelay)
            return true;

        if (IsWaitingActionExecution)
        {
            if (ActionRecordingManager.ExecutionDetector != null && ActionRecordingManager.ExecutionDetector.TryClaimExecution(PendingActionID, this)) // 尝试认领执行结果 (如果两个宏尝试执行相同技能会导致错误的竞争 使用所有权管理解决)
            {
                IsWaitingActionExecution = false;
                if (LineToProgressMap.TryGetValue(CurrentLineIndex, out var progressIndex))
                {
                    CurrentProgressIndex = progressIndex;
                    CurrentActionID = PendingActionID;
                    GCDStartTime = DateTime.UtcNow;
                    OnUpdateProgress?.Invoke(progressIndex, PendingActionID);
                }
                LastExecuteTime = DateTime.UtcNow;
                CurrentLineIndex++; // 成功认领，前进到下一行
                PendingActionID = 0;
            }
            else
            {
                var elapsed = (DateTime.UtcNow - LastCommandSendTime).TotalMilliseconds;
                if (elapsed > 2000)
                {
                    IsWaitingActionExecution = false;
                    PendingActionID = 0; // 超时后会自动重新执行当前行
                }
            }
            return true;
        }

        if (CurrentLineIndex >= MacroLines.Count)
            return HandleCompletion(); // 所有行执行完毕,检查是否需要循环

        if (CurrentLineIndex > 0 && LastExecuteTime != DateTime.MinValue) // 检查上一行的等待时间
        {
            var prevLine = MacroLines[CurrentLineIndex - 1];
            var prevActionID = ParseActionID(prevLine);

           
            var waitMatch = Regex.Match(prevLine, @"<[Ww]ait\.(\d+(?:\.\d+)?)>", RegexOptions.IgnoreCase); // 1. 优先检查显式 wait

            if (waitMatch.Success && float.TryParse(waitMatch.Groups[1].Value, out var waitTime))
            {
                var waitTimeMs = (int)(waitTime * 1000);
                var elapsed = (DateTime.UtcNow - LastExecuteTime).TotalMilliseconds;
                if (elapsed < waitTimeMs)
                    return true;
            }
            else if (prevActionID == 0) //非技能命令且没有显式 wait 使用默认间隔
            {
                var elapsed = (DateTime.UtcNow - LastExecuteTime).TotalMilliseconds;
                if (elapsed < DefaultInterval)
                {
                    if (LineToProgressMap.TryGetValue(CurrentLineIndex, out var progressIndex)) // 在等待期间，更新当前行的进度条为黄色（准备执行）
                    {
                        var currentActionID = ParseActionID(MacroLines[CurrentLineIndex]);
                        OnUpdateProgress?.Invoke(progressIndex, currentActionID);
                    }
                    return true;
                }
            }
        }

        var currentLine = MacroLines[CurrentLineIndex];

        if (IsNonExecutableCommand(currentLine))
        {
            CurrentLineIndex++;
            return true;
        }

        if (IsConditionalCommand(currentLine, out var conditionalCommand)) // 处理条件命令 /if
        {
            if (EvaluateCondition(currentLine))
            {
                if (LineToProgressMap.TryGetValue(CurrentLineIndex, out var successProgressBarIndex))
                    OnUpdateConditionStatus?.Invoke(successProgressBarIndex, "[成功]");
                currentLine = conditionalCommand;
            }
            else
            {
                if (LineToProgressMap.TryGetValue(CurrentLineIndex, out var progressBarIndex))
                {
                    OnUpdateConditionStatus?.Invoke(progressBarIndex, "[失败]");
                    OnSkipLine?.Invoke(progressBarIndex);
                }

                CurrentLineIndex++;
                return true;
            }
        }

        var actionID = ParseActionID(currentLine);
        var hasExplicitWait = Regex.IsMatch(currentLine, @"<[Ww]ait\.\d+(?:\.\d+)?>", RegexOptions.IgnoreCase);
        var isBattleSkill = IsBattleSkill(actionID);
        var isCraftingSkill = IsCraftingSkill(actionID);

        if (isCraftingSkill && DService.Condition[ConditionFlag.ExecutingCraftingAction])
            return true;

        if (!CanExecuteAction(currentLine, actionID, hasExplicitWait, isBattleSkill, isCraftingSkill))
            return true;

        var targetMatch = Regex.Match(currentLine, @"<(?![Ww]ait\.)([^>]+)>", RegexOptions.IgnoreCase); // 检查智能目标是否能找到有效目标（排除wait）
        if (targetMatch.Success)
        {
            if (!CheckSmartTargetAvailability(currentLine, out var targetObj))
            {
                //Error("未找到有效目标，跳过当前行");
                if (LineToProgressMap.TryGetValue(CurrentLineIndex, out var progressBarIndex))
                {
                    OnUpdateTargetStatus?.Invoke(progressBarIndex, "<失败>");
                    OnSkipLine?.Invoke(progressBarIndex);
                }
                CurrentLineIndex++;
                return true;
            }
            else if (LineToProgressMap.TryGetValue(CurrentLineIndex, out var progressBarIndex))
                OnUpdateTargetStatus?.Invoke(progressBarIndex, "<成功>");
        }

        ExecuteCurrentLine(currentLine, actionID);
        return true;
    }

    private void ExecuteCurrentLine(string line, uint actionID)
    {
        var cleanLine = Regex.Replace(line, @"<[Ww]ait\.\d+(?:\.\d+)?>", string.Empty);

        if (actionID > 0)
        {
            var actionMatch = Regex.Match(cleanLine, @"^(/(?:action|ac))\s+(.+?)(\s+<.+>)?$", RegexOptions.IgnoreCase);
            if (actionMatch.Success)
            {
                var command = actionMatch.Groups[1].Value;
                var originalName = actionMatch.Groups[2].Value.Trim();
                var suffix = actionMatch.Groups[3].Value;

                var executableName = GetExecutableActionName(actionID, originalName);
                if (!string.IsNullOrEmpty(executableName))
                    cleanLine = $"{command} {executableName}{suffix}";
            }
        }

        cleanLine = Regex.Replace(cleanLine, @"<(?![Ww]ait\.)([^>]+)>", m => "<" + NormalizePlaceholderForExecution(m.Groups[1].Value) + ">"); // 规范化目标后缀（排除wait）

        var isBattleSkill = IsBattleSkill(actionID); // 如果是战斗技能，先记录等待执行确认的状态，再发送命令
        if (isBattleSkill && actionID > 0)
        {
            IsWaitingActionExecution = true;
            LastCommandSendTime = DateTime.UtcNow;
            PendingActionID = actionID;
        }

        if (!TryExecuteCustomCommand(cleanLine.Trim())) // 检查是否是自定义命令，如果不是才发送到聊天
            ChatManager.SendCommand(cleanLine.Trim());

        if (!isBattleSkill || actionID == 0) // 非战斗技能直接认为执行成功
        {
            if (LineToProgressMap.TryGetValue(CurrentLineIndex, out var progressIndex))
            {
                CurrentProgressIndex = progressIndex;
                CurrentActionID = actionID;
                GCDStartTime = DateTime.UtcNow;
                OnUpdateProgress?.Invoke(progressIndex, actionID);
            }

            LastExecuteTime = DateTime.UtcNow;
            CurrentLineIndex++;
        }
    }

    private bool HandleCompletion()
    {
        if (!CheckLastSkillCompletion())
            return true;

        CurrentLoopIteration++; // 最后一个技能已完成

        if (TotalLoopCount == 0 || CurrentLoopIteration < TotalLoopCount)
        {
            OnLoopComplete?.Invoke(); // 还需要继续循环 回调通知外部(UI重置)

            if (CompletionDelay > 0 && TaskHelperInstance != null) // 如果有完成延迟,使用TaskHelper处理
            {
                IsWaitingDelay = true;
                TaskHelperInstance.DelayNext(CompletionDelay);
                TaskHelperInstance.Enqueue(() =>
                {
                    IsWaitingDelay = false;
                    CurrentLineIndex = 0;
                    LastExecuteTime = DateTime.MinValue;
                });
                return true; 
            }
            CurrentLineIndex = 0;
            LastExecuteTime = DateTime.MinValue;
            return true;
        }

        OnAllComplete?.Invoke(); // 所有循环完成 通知外部停止执行

        if (CompletionDelay > 0 && TaskHelperInstance != null)
        {
            TaskHelperInstance.DelayNext(CompletionDelay);
            TaskHelperInstance.Enqueue(() =>
            {
                // 占位 保持延迟能正常进行
                return true;
            });
        }

        return false; // 完全结束
    }

    private bool CheckLastSkillCompletion() // 检查最后一个技能是否完成
    {
        if (LastExecuteTime == DateTime.MinValue)
            return true; // 没有执行任何命令,直接返回完成

        var lastLineIndex = MacroLines.Count - 1;
        if (lastLineIndex < 0)
            return true;

        var lastLine = MacroLines[lastLineIndex];

        if (!LineToProgressMap.TryGetValue(lastLineIndex, out var lastProgressIndex)) // 通过映射表获取进度条索引，再获取 actionID
            return true; // 最后一行不在映射表中，说明不是可执行宏，直接返回完成

        var lastActionID = lastProgressIndex < ParsedActionIDs.Count ? ParsedActionIDs[lastProgressIndex] : 0u;
        var isLastBattleSkill = IsBattleSkill(lastActionID);
        var hasLastExplicitWait = Regex.IsMatch(lastLine, @"<[Ww]ait\.\d+(?:\.\d+)?>", RegexOptions.IgnoreCase);

        if (hasLastExplicitWait || !isLastBattleSkill)
        {
            var lastWaitTimeMs = ExtractWaitTime(lastLine, lastActionID, DefaultInterval);
            var elapsed = (DateTime.Now - LastExecuteTime).TotalMilliseconds;
            if (elapsed < lastWaitTimeMs)
                return false;
        }
        else if (isLastBattleSkill)
        {
            if (IsPlayerCasting() || IsAnimationLocked() || IsGCDActive())
                return false;
        }

        return true;
    }

    #endregion

    #region 私有执行辅助方法

    private static uint ParseActionID(string line)
    {
        var actionMatch = Regex.Match(line, @"^/(?:action|ac)\s+(.+?)(?:\s+<.+>)?$", RegexOptions.IgnoreCase);
        if (!actionMatch.Success) return 0;

        var skillName = actionMatch.Groups[1].Value.Trim();
        var actionID = MacroCacheHelper.FindActionID(skillName, LocalPlayerState.ClassJobData);
        return actionID ?? 0;
    }

    private bool IsNonExecutableCommand(string line)
    {
        var commandMatch = Regex.Match(line, @"^/(\w+)");
        return commandMatch.Success && NonExecutableCommands.Contains(commandMatch.Groups[1].Value);
    }

    #endregion

    #region 条件判断方法

    private static bool IsConditionalCommand(string line, out string conditionalCommand)
    {
        conditionalCommand = string.Empty;
        var match = Regex.Match(line, @"^/if\s+\[(.+?)\]\s+(/.+)$", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            conditionalCommand = match.Groups[2].Value;
            return true;
        }
        return false;
    }

    internal static bool EvaluateCondition(string line)
    {
        var match = Regex.Match(line, @"^/if\s+\[(.+?)\]\s+/", RegexOptions.IgnoreCase);
        if (!match.Success) return false;

        var condition = match.Groups[1].Value.Trim();

        var targetPrefixMatch = Regex.Match(condition, @"^(self|target|focus|party)\.(.+)$", RegexOptions.IgnoreCase);
        if (targetPrefixMatch.Success)
        {
            var targetType = targetPrefixMatch.Groups[1].Value.ToLower();
            var actualCondition = targetPrefixMatch.Groups[2].Value.Trim();

            if (targetType == "party")
            {
                var partyIndexMatch = Regex.Match(actualCondition, @"^(\d+)\.(.+)$");
                if (partyIndexMatch.Success)
                {
                    var userIndex = int.Parse(partyIndexMatch.Groups[1].Value);
                    var memberCondition = partyIndexMatch.Groups[2].Value.Trim();

                    var agent = AgentHUD.Instance();
                    if (agent == null) return false;

                    var partyMembers = agent->PartyMembers.ToArray();
                    var member = partyMembers.FirstOrDefault(m => m.Index == userIndex);
                    if (member.Object == null) return false;

                    var memberObject = DService.ObjectTable.CreateObjectReference((nint)member.Object) as IBattleChara;
                    if (memberObject == null) return false;

                    return EvaluateConditionOnTarget(memberCondition, memberObject);
                }

                return EvaluatePartyCondition(actualCondition);
            }

            IBattleChara targetObject = targetType switch
            {
                "self"   => LocalPlayerState.Object,
                "target" => TargetManager.Target      as IBattleChara,
                "focus"  => TargetManager.FocusTarget as IBattleChara,
                _        => null
            };

            if (targetObject == null) return false;
            return EvaluateConditionOnTarget(actualCondition, targetObject);
        }

        if (Regex.Match(condition, @"^combat\s*=\s*(true|false)$", RegexOptions.IgnoreCase) is { Success: true } combatMatch)
        {
            var wantCombat = combatMatch.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
            return DService.Condition[ConditionFlag.InCombat] == wantCombat;
        }

        if (Regex.Match(condition, @"^job\s*=\s*(.+)$", RegexOptions.IgnoreCase) is { Success: true } jobMatch)
        {
            var jobName = jobMatch.Groups[1].Value.Trim();
            return LocalPlayerState.ClassJobData.Name.ToString().Equals(jobName, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool EvaluateConditionOnTarget(string condition, IBattleChara target) // 对指定目标对象评估条件
    {
        if (condition.Equals("exists", StringComparison.OrdinalIgnoreCase))
            return true;

        if (condition.Equals("none", StringComparison.OrdinalIgnoreCase))
            return false;

        if (Regex.Match(condition, @"^hp\s*(<=?|>=?|!=|=)\s*(\d+)$", RegexOptions.IgnoreCase) is { Success: true } hpMatch)
        {
            var op = hpMatch.Groups[1].Value;
            var threshold = float.Parse(hpMatch.Groups[2].Value);
            var currentHpPercent = target.MaxHp == 0 ? 0 : (float)target.CurrentHp / target.MaxHp * 100;
            return op switch
            {
                "<"  => currentHpPercent <  threshold,
                ">"  => currentHpPercent >  threshold,
                "<=" => currentHpPercent <= threshold,
                ">=" => currentHpPercent >= threshold,
                "="  => Math.Abs(currentHpPercent - threshold) <  0.01f,
                "!=" => Math.Abs(currentHpPercent - threshold) >= 0.01f,
                _    => false
            };
        }

        if (Regex.Match(condition, @"^mp\s*(<=?|>=?|!=|=)\s*(\d+)$", RegexOptions.IgnoreCase) is { Success: true } mpMatch)
        {
            var op = mpMatch.Groups[1].Value;
            var threshold = float.Parse(mpMatch.Groups[2].Value);
            var currentMpPercent = target.MaxMp == 0 ? 0 : (float)target.CurrentMp / target.MaxMp * 100;
            return op switch
            {
                "<"  => currentMpPercent <  threshold,
                ">"  => currentMpPercent >  threshold,
                "<=" => currentMpPercent <= threshold,
                ">=" => currentMpPercent >= threshold,
                "="  => Math.Abs(currentMpPercent - threshold) <  0.01f,
                "!=" => Math.Abs(currentMpPercent - threshold) >= 0.01f,
                _    => false
            };
        }

        if (Regex.Match(condition, @"^buff\s*(=|!=)\s*(.+)$", RegexOptions.IgnoreCase) is { Success: true } buffMatch)
        {
            var op = buffMatch.Groups[1].Value;
            var buffStr = buffMatch.Groups[2].Value.Trim();

            bool hasBuff;
            if (uint.TryParse(buffStr, out var buffID))
                hasBuff = target.StatusList.Any(s => s.StatusID == buffID);
            else
            {
                var statusSheet = DService.Data.GetExcelSheet<Lumina.Excel.Sheets.Status>();
                var status = statusSheet.FirstOrDefault(s => s.Name.ToString().Equals(buffStr, StringComparison.OrdinalIgnoreCase));
                hasBuff = status.RowId != 0 && target.StatusList.Any(s => s.StatusID == status.RowId);
            }

            return op == "=" ? hasBuff : !hasBuff;
        }

        if (Regex.Match(condition, @"^name\s*(=|!=)\s*(.+)$", RegexOptions.IgnoreCase) is { Success: true } nameMatch)
        {
            var op = nameMatch.Groups[1].Value;
            var targetName = nameMatch.Groups[2].Value.Trim();
            var contains = target.Name.ToString().Contains(targetName, StringComparison.OrdinalIgnoreCase);
            return op == "=" ? contains : !contains;
        }

        return false;
    }

    private static bool EvaluatePartyCondition(string condition) // 对队伍评估条件
    {
        var agent = AgentHUD.Instance();
        if (agent == null) return false;

        var partyMembers = agent->PartyMembers.ToArray().Where(m => m.Object != null).ToArray();

        if (Regex.Match(condition, @"^hp\s*(<=?|>=?|!=|=)\s*(\d+)$", RegexOptions.IgnoreCase) is { Success: true } hpMatch)
        {
            var op = hpMatch.Groups[1].Value;
            var threshold = float.Parse(hpMatch.Groups[2].Value);

            foreach (var member in partyMembers)
            {
                if (DService.ObjectTable.CreateObjectReference((nint)member.Object) is IBattleChara chara)
                {
                    var hpPercent = chara.MaxHp == 0 ? 0 : (float)chara.CurrentHp / chara.MaxHp * 100;
                    var matchCondition = op switch
                    {
                        "<"  => hpPercent <  threshold,
                        ">"  => hpPercent >  threshold,
                        "<=" => hpPercent <= threshold,
                        ">=" => hpPercent >= threshold,
                        "="  => Math.Abs(hpPercent - threshold) <  0.01f,
                        "!=" => Math.Abs(hpPercent - threshold) >= 0.01f,
                        _    => false
                    };
                    if (matchCondition)
                        return true;
                }
            }
            return false;
        }

        if (Regex.Match(condition, @"^minHp\s*(<=?|>=?|!=|=)\s*(\d+)$", RegexOptions.IgnoreCase) is { Success: true } minHpMatch)
        {
            var op = minHpMatch.Groups[1].Value;
            var threshold = float.Parse(minHpMatch.Groups[2].Value);

            float minHpPercent = 100f;
            foreach (var member in partyMembers)
            {
                if (DService.ObjectTable.CreateObjectReference((nint)member.Object) is IBattleChara chara)
                {
                    var hpPercent = chara.MaxHp == 0 ? 0 : (float)chara.CurrentHp / chara.MaxHp * 100;
                    if (hpPercent < minHpPercent)
                        minHpPercent = hpPercent;
                }
            }

            return op switch
            {
                "<"  => minHpPercent <  threshold,
                ">"  => minHpPercent >  threshold,
                "<=" => minHpPercent <= threshold,
                ">=" => minHpPercent >= threshold,
                "="  => Math.Abs(minHpPercent - threshold) <  0.01f,
                "!=" => Math.Abs(minHpPercent - threshold) >= 0.01f,
                _    => false
            };
        }

        if (Regex.Match(condition, @"^count\s*(<=?|>=?|!=|=)\s*(\d+)$", RegexOptions.IgnoreCase) is { Success: true } countMatch)
        {
            var op = countMatch.Groups[1].Value;
            var threshold = int.Parse(countMatch.Groups[2].Value);
            var partyCount = partyMembers.Length;

            return op switch
            {
                "<"  => partyCount <  threshold,
                ">"  => partyCount >  threshold,
                "<=" => partyCount <= threshold,
                ">=" => partyCount >= threshold,
                "="  => partyCount == threshold,
                "!=" => partyCount != threshold,
                _    => false
            };
        }

        return false;
    }

    #endregion

    #region 技能检查方法

    public static bool IsBattleSkill(uint actionID) => actionID > 0 && actionID < 100000;
    public static bool IsCraftingSkill(uint actionID) => actionID >= 100000;

    public static bool IsAbilitySkill(uint actionID)
    {
        var action = LuminaGetter.GetRow<FFAction>(actionID);
        return action != null && action.Value.Cast100ms == 0 && action.Value.CooldownGroup != 58;
    }

    public static bool IsPlayerCasting() => DService.ObjectTable.LocalPlayer?.CurrentCastTime > 0;

    public static bool IsAnimationLocked()
    {
        var manager = ActionManager.Instance();
        return manager != null && manager->AnimationLock > 0;
    }

    public static bool IsGCDActive()
    {
        var manager = ActionManager.Instance();
        if (manager == null) return false;

        var gcdRecast = manager->GetRecastGroupDetail(58);
        return gcdRecast != null && gcdRecast->IsActive;
    }

        private bool CanExecuteAction(string line, uint actionID, bool hasExplicitWait, bool isBattleSkill, bool isCraftingSkill) // 检查是否可以执行动作
    {
        if (hasExplicitWait || actionID == 0 || isCraftingSkill)
        {
            if (!CheckTimingCondition(line, actionID))
                return false;
        }
        else if (isBattleSkill)
        {
            if (!CheckBattleSkillCondition(actionID))
                return false;
        }

        if (isBattleSkill && !hasExplicitWait)
        {
            if (!CheckActionStatus(actionID))
                return false;
        }

        return true;
    }

    private bool CheckTimingCondition(string line, uint actionID)
    {
        var waitTimeMs = ExtractWaitTime(line, actionID, DefaultInterval);

        if (LastExecuteTime != DateTime.MinValue)
        {
            var elapsed = (DateTime.Now - LastExecuteTime).TotalMilliseconds;
            if (elapsed < waitTimeMs)
                return false;
        }
        return true;
    }

    private static bool CheckBattleSkillCondition(uint actionID)
    {
        if (ActionManager.Instance() == null)
            return false;

        if (IsPlayerCasting() || IsAnimationLocked())
            return false;

        if (!IsAbilitySkill(actionID) && IsGCDActive())
            return false;

        return true;
    }

    private static bool CheckActionStatus(uint actionID)
    {
        var manager = ActionManager.Instance();
        if (manager == null)
            return true;

        var status = manager->GetActionStatus(ActionType.Action, actionID);
        return status == 0;
    }

    #endregion

    #region 智能目标方法

    private static string NormalizePlaceholderForExecution(string placeholderInnerText)
    {
        var normalized = Regex.Replace(placeholderInnerText, @"\s+", " ").Trim();
        if (string.IsNullOrEmpty(normalized))
            return placeholderInnerText.Replace(" ", string.Empty);

        if (Regex.IsMatch(normalized, @"^(party|enemy)\s*\.", RegexOptions.IgnoreCase))
        {
            normalized = Regex.Replace(normalized, @"\s*\.\s*", ".", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\s*!=\s*", "!=", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\s*=\s*", "=", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\s*:\s*", ":", RegexOptions.IgnoreCase);
            return normalized;
        }

        return normalized.Replace(" ", string.Empty);
    }

    private static bool CheckSmartTargetAvailability(string line, out GameObject* target)
    {
        target = null;

        var targetMatch = Regex.Match(line, @"<(?![Ww]ait\.)[^>]+>", RegexOptions.IgnoreCase);
        if (!targetMatch.Success)
            return true;

        var placeholder = targetMatch.Value;
        if (!SmartTargets.IsSmartTargetPlaceholder(placeholder))
            return true;

        return SmartTargets.TryResolve(placeholder, out target);
    }

    #endregion

    #region 宏解析方法

    public static void ParseMacroLines(List<string> lines,
        out List<uint> actionIDs,
        out List<string> commandTypes,
        out List<bool> hasTargetFlags,
        out Dictionary<int, int> lineToProgressMap,
        out string macroIconName)
    {
        actionIDs         = [];
        commandTypes      = [];
        hasTargetFlags    = [];
        lineToProgressMap = [];
        macroIconName     = string.Empty;

        var progressIndex = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();

            var commandMatch = Regex.Match(line, @"^/(\w+)");
            if (!commandMatch.Success) continue;

            var command = commandMatch.Groups[1].Value.ToLower();

            var hasTarget = Regex.IsMatch(line, @"<(?![Ww]ait\.)[^>]+>", RegexOptions.IgnoreCase); // 检查是否有目标后缀（同时排除 <wait.x> 格式以防错误检测）

            switch (command)
            {
                case "echo" or "e":
                    lineToProgressMap[i] = progressIndex;
                    actionIDs.Add(0);
                    commandTypes.Add("echo");
                    hasTargetFlags.Add(false);
                    progressIndex++;
                    break;

                case "wait":
                    lineToProgressMap[i] = progressIndex;
                    actionIDs.Add(0);
                    commandTypes.Add("wait");
                    hasTargetFlags.Add(false);
                    progressIndex++;
                    break;

                case "call":
                    lineToProgressMap[i] = progressIndex;
                    actionIDs.Add(0);
                    commandTypes.Add("call");
                    hasTargetFlags.Add(false);
                    progressIndex++;
                    break;

                case "close":
                    lineToProgressMap[i] = progressIndex;
                    actionIDs.Add(0);
                    commandTypes.Add("close");
                    hasTargetFlags.Add(false);
                    progressIndex++;
                    break;

                case "if":
                    var ifMatch = Regex.Match(line, @"^/if\s+\[.+?\]\s+(/(?:ac|action)\s+.+)$", RegexOptions.IgnoreCase); // 提取 /if 内部的命令进行解析 格式为 /if [条件]
                    if (ifMatch.Success)
                    {
                        var innerCommand = ifMatch.Groups[1].Value;
                        var ifSkillName = ExtractSkillName(innerCommand);
                        var ifActionID = ifSkillName != string.Empty ? MacroCacheHelper.FindActionID(ifSkillName, LocalPlayerState.ClassJobData) : null;

                        lineToProgressMap[i] = progressIndex;
                        actionIDs.Add(ifActionID ?? 0);
                        commandTypes.Add("if-conditional");
                        hasTargetFlags.Add(hasTarget);
                        progressIndex++;
                    }
                    else // 其他类型的 if 命令（如 /if xxx /echo）
                    {
                        lineToProgressMap[i] = progressIndex;
                        actionIDs.Add(0);
                        commandTypes.Add("if-conditional");
                        hasTargetFlags.Add(false);
                        progressIndex++;
                    }
                    break;

                case "action" or "ac":
                    var skillName = ExtractSkillName(line);
                    var actionID = skillName != string.Empty ? MacroCacheHelper.FindActionID(skillName, LocalPlayerState.ClassJobData) : null;

                    if (actionID != null)
                    {
                        lineToProgressMap[i] = progressIndex;
                        actionIDs.Add((uint)actionID);
                        commandTypes.Add("action");
                        hasTargetFlags.Add(hasTarget);
                        progressIndex++;
                    }
                    break;

                default:
                    if (NonExecutableCommands.Contains(command)) // 针对非可执行命令
                    {
                        if (command is "micon" or "macroicon")
                        {
                            var extractedMiconName = ExtractMacroIconNameString(line);
                            if (extractedMiconName != string.Empty)
                                macroIconName = extractedMiconName;
                        }
                    }
                    else
                    {
                        var emoteName = ExtractEmoteName(line);
                        if (!string.IsNullOrEmpty(emoteName) && MacroCacheHelper.Emotes.TryGetValue(emoteName, out var emoteId))
                        {
                            lineToProgressMap[i] = progressIndex;
                            actionIDs.Add(emoteId);
                            commandTypes.Add("emote");
                            hasTargetFlags.Add(hasTarget);
                            progressIndex++;
                        }
                    }
                    break;
            }
        }
    }

    private bool TryExecuteCustomCommand(string command)
    {
        var trimmedCommand = command.Trim();

        var callMatch = Regex.Match(trimmedCommand, @"^/call\s+(.+?)(?:\s+(\d+))?$", RegexOptions.IgnoreCase);
        if (callMatch.Success)
        {
            var macroName = callMatch.Groups[1].Value.Trim();
            int? loopCount = null;

            if (callMatch.Groups[2].Success && int.TryParse(callMatch.Groups[2].Value, out var loops))
                loopCount = loops;

            OpenStandaloneMacroWindowByName(macroName, loopCount, autoRun: true);
            return true;
        }

        if (Regex.IsMatch(trimmedCommand, @"^/close$", RegexOptions.IgnoreCase))
        {
            if (CurrentDisplayWindow != null)
            {
                CurrentDisplayWindow.CloseWindow();
                return true;
            }
            else
                return true;
        }

        return false;
    }

    #endregion

    #region 字符串解析工具方法

    public static int ExtractWaitTime(string input, uint actionID, int defaultIntervalMs = 2500)
    {
        var match = Regex.Match(input, @"<[Ww]ait\.(\d+(?:\.\d+)?)>", RegexOptions.IgnoreCase);
        if (match.Success && float.TryParse(match.Groups[1].Value, out var waitTime))
            return (int)(waitTime * 1000);

        if (actionID > 0)
        {
            if (ModuleConfig.ActionCooldowns.TryGetValue(ActionType.CraftAction, out var craftActions) &&
                craftActions.TryGetValue(actionID, out var craftCooldown))
                return craftCooldown;

            if (ModuleConfig.ActionCooldowns.TryGetValue(ActionType.Action, out var combatActions) &&
                combatActions.TryGetValue(actionID, out var combatCooldown))
                return combatCooldown;
        }

        return defaultIntervalMs;
    }

    public static string ExtractSkillName(string input)
    {
        var match = Regex.Match(input, @"^/ac\s+(""[^""]+""|[^<]+)"); // 去掉开头的 /ac 和可能的空格
        if (!match.Success) return string.Empty;

        var name = match.Groups[1].Value.Trim();

        if (name.StartsWith("\"") && name.EndsWith("\"")) // 如果有引号，去掉引号
            name = name[1..^1];

        name = Regex.Replace(name, @"<.*?>", "").Trim(); // 去掉可能残留的 <wait.X> 或 <数字>

        return name;
    }

    public static string ExtractEmoteName(string input)
    {
        var match = Regex.Match(input, @"^/([^<\s]+)");
        if (!match.Success) return string.Empty;

        return match.Groups[1].Value.Trim();
    }

    private static string ExtractMacroIconNameString(string input)
    {
        var match = Regex.Match(input, @"(?:/macroicon|/micon)\s+(.+)");
        if (match.Success)
            return match.Groups[1].Value;
        else
            return string.Empty;
    }

    private static string GetExecutableActionName(uint actionID, string originalName)
    {
        var action = LuminaGetter.GetRow<FFAction>(actionID);
        if (action == null || action.Value.IsPlayerAction)
            return originalName;

        if (MacroCacheHelper.TryGetAdjustedAction(actionID, out var cachedPlayerActionID)) // 非玩家技能，先查缓存 主要针对那些无法放置到热键栏的技能
        {
            var cachedAction = LuminaGetter.GetRow<FFAction>(cachedPlayerActionID);
            if (cachedAction != null)
                return cachedAction.Value.Name.ToString();
        }


        var manager = ActionManager.Instance();
        if (manager == null)
            return originalName;

        var actionSheet = DService.Data.Excel.GetSheet<FFAction>();
        if (actionSheet == null)
            return originalName;

        foreach (var playerAction in actionSheet) // GetAdjustedActionId 在初始化时并不能获得所有的映射；只有在相应的技能可以被动作栏使用时才会有映射
        {
            if (playerAction.IsPvP || !playerAction.IsPlayerAction || string.IsNullOrEmpty(playerAction.Name.ExtractText()))
                continue;

            var adjustedID = manager->GetAdjustedActionId(playerAction.RowId);
            if (adjustedID == actionID)
            {
                MacroCacheHelper.CacheAdjustedAction(actionID, playerAction.RowId); // 找到后存入缓存减少查表性能开销
                return playerAction.Name.ToString();
            }
        }

        return originalName;
    }

    #endregion
    }
}
