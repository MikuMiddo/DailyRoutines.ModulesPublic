using System;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace DailyRoutines.ModulesPublic;

public unsafe partial class MacroOptimization
{
    internal sealed class ActionExecutionDetector
    {
        private bool       WasOffCooldown;
        private uint       PreActionID;
        private ActionType PreActionType = ActionType.None;
        private uint       PreMaxCharges;
        private float      PreRecastElapsed;
        private float      PreAnimationLock;

        public uint LastExecutedActionID { get; private set; } // 最后成功执行的技能ID

        private object? ActionOwner; // 解决不同的宏执行相同技能的竞争机制

        public event Action<ActionType, uint>? OnActionExecuted;

        public ActionExecutionDetector()
        {
            UseActionManager.RegPreUseAction(OnPreUse);
            UseActionManager.RegUseAction(OnPostUse);
        }

        private void OnPreUse(
            ref bool isPrevented,
            ref ActionType actionType,
            ref uint actionID,
            ref ulong targetID,
            ref uint extraParam,
            ref ActionManager.UseActionMode queueState,
            ref uint comboRouteID)
        {
            if (actionType is ActionType.Item or ActionType.EventItem or ActionType.Ornament) // 此处为了防止使用物品（例如殊级恢复药水等）被误判为技能使用导致崩溃
            {
                WasOffCooldown = false;
                PreActionID = 0;
                PreActionType = ActionType.None;
                PreMaxCharges = 0;
                PreRecastElapsed = 0;
                PreAnimationLock = 0;
                return;
            }

            var manager = ActionManager.Instance();
            if (manager == null)
                return;

            var finalActionID = actionID;
            if (actionType == ActionType.Action)
            {
                var adjustedActionID = manager->GetAdjustedActionId(actionID);
                if (adjustedActionID != 0)
                    finalActionID = adjustedActionID;
            }

            WasOffCooldown = UseActionManager.IsActionOffCooldown(actionType, finalActionID);
            PreActionID = finalActionID;
            PreActionType = actionType;
            PreAnimationLock = manager->AnimationLock;

            var recastGroup = manager->GetRecastGroup((int)actionType, finalActionID);
            var recastDetail = manager->GetRecastGroupDetail(recastGroup);
            if (recastDetail != null)
            {
                PreMaxCharges = actionType == ActionType.Action ? ActionManager.GetMaxCharges(finalActionID, 0) : 0u;
                PreRecastElapsed = recastDetail->Elapsed;
            }
            else
            {
                PreMaxCharges = 0;
                PreRecastElapsed = 0;
            }
        }

        private void OnPostUse(
            bool result,
            ActionType actionType,
            uint actionID,
            ulong targetID,
            uint extraParam,
            ActionManager.UseActionMode queueState,
            uint comboRouteID)
        {
            if (actionType is ActionType.Item or ActionType.EventItem or ActionType.Ornament)
                return;

            var manager = ActionManager.Instance();
            if (manager == null)
                return;

            var finalActionID = actionID;
            if (actionType == ActionType.Action)
            {
                var adjustedActionID = manager->GetAdjustedActionId(actionID);
                if (adjustedActionID != 0)
                    finalActionID = adjustedActionID;
            }

            if (PreActionID != finalActionID || PreActionType != actionType)
                return;

            if (!WasOffCooldown)
                return;

            var recastGroup = manager->GetRecastGroup((int)actionType, finalActionID);
            var recastDetail = manager->GetRecastGroupDetail(recastGroup);

            var isNowOnCooldown = !UseActionManager.IsActionOffCooldown(actionType, finalActionID);

            var wasExecuted = false;
            if (PreMaxCharges > 1 && recastDetail != null)
            {
                var recastChanged = recastDetail->Elapsed != PreRecastElapsed;
                wasExecuted = recastChanged || isNowOnCooldown;
            }
            else
            {
                var animationLockChanged = manager->AnimationLock > PreAnimationLock;
                wasExecuted = isNowOnCooldown || animationLockChanged;
            }

            if (!wasExecuted)
                return;

            ActionOwner = null; // 新技能执行时，清理旧的所有权和ID
            LastExecutedActionID = finalActionID;
            OnActionExecuted?.Invoke(actionType, finalActionID);
        }

        public bool TryClaimExecution(uint expectedActionID, object claimer)
        {
            if (LastExecutedActionID == expectedActionID && expectedActionID != 0 && ActionOwner == null)
            {
                ActionOwner = claimer;
                return true;
            }

            return ActionOwner == claimer && LastExecutedActionID == expectedActionID; // 如果是自己已经认领的，返回true（重复检查）
        }

        public void Dispose()
        {
            UseActionManager.Unreg(OnPreUse);
            UseActionManager.Unreg(OnPostUse);
            OnActionExecuted = null;
        }
    }
}
