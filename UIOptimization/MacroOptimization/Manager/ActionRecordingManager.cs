using System;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace DailyRoutines.ModulesPublic;

public unsafe partial class MacroOptimization
{
    internal static unsafe class ActionRecordingManager
    {
        internal static ActionExecutionDetector? ExecutionDetector { get; private set; }

        private static MacroOptimization? ModuleInstance;

        public static void Enable(MacroOptimization module)
        {
            if (ExecutionDetector != null)
                return;

            ModuleInstance = module;

            ExecutionDetector = new ActionExecutionDetector();
            ExecutionDetector.OnActionExecuted += OnActionExecutedHandler;
        }

        public static void Disable()
        {
            if (ExecutionDetector != null)
            {
                ExecutionDetector.OnActionExecuted -= OnActionExecutedHandler;
                ExecutionDetector.Dispose();
                ExecutionDetector = null;
            }

            ModuleInstance = null;
        }

        private static void OnActionExecutedHandler(ActionType actionType, uint actionID)
        {
            RecordActionCooldown(actionType, actionID);

            if (MacroExtendAddon != null && MacroExtendAddon.IsRecording && MacroExtendAddon.IsOpen)
                MacroExtendAddon.RecordAction(actionID);
        }

        private static void RecordActionCooldown(ActionType actionType, uint actionID)
        {
            var module = ModuleInstance;
            if (module == null)
                return;

            if (actionType == ActionType.CraftAction)
            {
                if (actionID <= 100000)
                    return; // 生产技能：需要等待制作完成

                var startTime = DateTime.UtcNow;
                var recordedActionType = actionType;
                var recordedActionID = actionID;

                module.TaskHelper?.Enqueue(() => DService.Condition[ConditionFlag.ExecutingCraftingAction]); // 先等待进入执行状态，然后等待完成
                module.TaskHelper?.Enqueue(() =>
                {
                    if (DService.Condition[ConditionFlag.ExecutingCraftingAction])
                        return false;

                    var cooldownMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds; // 制作完成，记录冷却时间
                    if (cooldownMs > 0 && cooldownMs < 10000)
                    {
                        if (!ModuleConfig.ActionCooldowns.ContainsKey(recordedActionType))
                            ModuleConfig.ActionCooldowns[recordedActionType] = [];

                        ModuleConfig.ActionCooldowns[recordedActionType][recordedActionID] = cooldownMs;
                        module.SaveConfig(ModuleConfig);
                    }
                    return true;
                });
            }
            else if (actionType == ActionType.Action)
            {
                var startTime = DateTime.UtcNow;
                var recordedActionType = actionType;
                var recordedActionID = actionID;

                module.TaskHelper?.Enqueue(() =>
                {
                    var manager = ActionManager.Instance();
                    if (manager == null) return true;

                    if (MacroExecutor.IsPlayerCasting() || MacroExecutor.IsAnimationLocked())
                        return false;

                    var elapsedTimeMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds; // 计算从开始到现在的时间（起手+动画锁）

                    var gcdRecast = manager->GetRecastTime(ActionType.Action, recordedActionID); // 获取复唱时间
                    var gcdTimeMs = (int)(gcdRecast * 1000);

                    var isAbility = MacroExecutor.IsAbilitySkill(recordedActionID);

                    var totalCooldownMs = isAbility ? elapsedTimeMs : Math.Max(elapsedTimeMs, gcdTimeMs); // 能力技录制起手+动画锁时间，GCD技能录制起手+复唱的较大值

                    if (totalCooldownMs > 0 && totalCooldownMs < 300000)
                    {
                        if (!ModuleConfig.ActionCooldowns.ContainsKey(recordedActionType))
                            ModuleConfig.ActionCooldowns[recordedActionType] = [];

                        ModuleConfig.ActionCooldowns[recordedActionType][recordedActionID] = totalCooldownMs;
                        module.SaveConfig(ModuleConfig);
                    }
                    return true;
                });
            }
        }
    }
}
