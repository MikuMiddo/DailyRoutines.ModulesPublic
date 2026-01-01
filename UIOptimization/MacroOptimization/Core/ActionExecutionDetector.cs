using FFXIVClientStructs.FFXIV.Client.Game;
using System;

namespace DailyRoutines.ModulesPublic;

public unsafe partial class MacroOptimization
{
    internal sealed class ActionExecutionDetector
    {
    private bool       WasOffCooldown   = false;
    private uint       PreActionID      = 0;
    private ActionType PreActionType    = ActionType.None;
    private uint       PreMaxCharges    = 0;
    private float      PreRecastElapsed = 0;
    private float      PreAnimationLock = 0;

    public  uint    LastExecutedActionID { get; private set; } = 0; // 最后成功执行的技能ID
    private object? ActionOwner                                = null; // 解决不同的宏执行相同技能的竞争机制

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
        if (actionType is ActionType.Item or ActionType.EventItem or ActionType.Ornament) //此处为了防止使用物品 例如殊级恢复药水等被误判为技能使用 导致崩溃
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
        if (manager == null) return;

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
        if (manager == null) return;

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

        bool wasExecuted = false;

        var isNowOnCooldown = !UseActionManager.IsActionOffCooldown(actionType, finalActionID);

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

        if (wasExecuted)
        {
            ActionOwner = null; // 新技能执行时，清理旧的所有权和ID
            LastExecutedActionID = finalActionID;
            OnActionExecuted?.Invoke(actionType, finalActionID);
        }
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
