using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using FFAction = Lumina.Excel.Sheets.Action;

namespace DailyRoutines.ModulesPublic;

public unsafe partial class MacroOptimization
{
    internal static unsafe class MacroCacheHelper
    {
        private static readonly Dictionary<string, uint>                                         AllActionsCache               = [];
        private static readonly Dictionary<string, Dictionary<uint, uint>>                       JobSpecificActionsCache       = [];
        private static readonly Dictionary<uint, uint>                                           AdjustedToOriginalActionCache = [];
        private static readonly Dictionary<uint, (uint iconId, string name, bool isCraftAction)> ActionDisplayCache            = [];
        private static readonly Dictionary<string, uint>                                         AllIconsCache                 = [];
        private static readonly Dictionary<string, Dictionary<uint, uint>>                       JobSpecificIconsCache         = [];
        private static readonly Dictionary<string, uint>                                         EmoteCache                    = [];

        public static void RebuildAdjustedActionCache()
        {
            AdjustedToOriginalActionCache.Clear();

            var actionSheet = DService.Data.Excel.GetSheet<FFAction>();
            if (actionSheet == null) return;

            var manager = ActionManager.Instance();
            if (manager == null) return;

            foreach (var action in actionSheet)
            {
                if (action.IsPvP || !action.IsPlayerAction || string.IsNullOrEmpty(action.Name.ExtractText()))
                    continue;

                var adjustedID = manager->GetAdjustedActionId(action.RowId);
                if (adjustedID == action.RowId)
                    continue;

                var adjustedAction = LuminaGetter.GetRow<FFAction>(adjustedID);
                if (adjustedAction == null)
                    continue;

                if (adjustedAction.Value.IsPlayerAction)
                    AdjustedToOriginalActionCache.TryAdd(adjustedID, action.RowId);
                else
                    AdjustedToOriginalActionCache[adjustedID] = action.RowId;
            }
        }

        public static void InitializeGlobalActionCache()
        {
            var actionSheet = DService.Data.Excel.GetSheet<FFAction>();
            var craftActionSheet = DService.Data.Excel.GetSheet<CraftAction>();

            if (actionSheet == null || craftActionSheet == null) return;

            foreach (var action in actionSheet)
            {
                if (action.IsPvP || string.IsNullOrEmpty(action.Name.ExtractText()))
                    continue;

                var name = action.Name.ExtractText();

                ActionDisplayCache.TryAdd(action.RowId, (action.Icon, name, false));

                if (action.IsPlayerAction)
                {
                    if (!AllActionsCache.ContainsKey(name))
                        AllActionsCache[name] = action.RowId;

                    if (action.ClassJob.RowId > 0)
                    {
                        var jobID = action.ClassJob.RowId;
                        if (!JobSpecificActionsCache.ContainsKey(name))
                            JobSpecificActionsCache[name] = [];
                        JobSpecificActionsCache[name][jobID] = action.RowId;
                    }
                }
                else
                {
                    if (action.ClassJob.RowId > 0)
                    {
                        var jobID = action.ClassJob.RowId;
                        if (!JobSpecificActionsCache.ContainsKey(name))
                            JobSpecificActionsCache[name] = [];

                        if (!JobSpecificActionsCache[name].ContainsKey(jobID))
                            JobSpecificActionsCache[name][jobID] = action.RowId;
                    }
                    else
                        AllActionsCache.TryAdd(name, action.RowId);
                }
            }

            foreach (var craftAction in craftActionSheet)
            {
                if (string.IsNullOrEmpty(craftAction.Name.ExtractText()))
                    continue;

                var name = craftAction.Name.ExtractText();

                ActionDisplayCache.TryAdd(craftAction.RowId, (craftAction.Icon, name, true));

                if (!AllActionsCache.ContainsKey(name))
                    AllActionsCache[name] = craftAction.RowId;

                var jobID = craftAction.ClassJob.RowId;
                if (!JobSpecificActionsCache.ContainsKey(name))
                    JobSpecificActionsCache[name] = [];
                JobSpecificActionsCache[name][jobID] = craftAction.RowId;
            }

            RebuildAdjustedActionCache();
        }

        public static void InitializeIconCache()
        {
            var actionSheet = DService.Data.Excel.GetSheet<FFAction>();
            var craftActionSheet = DService.Data.Excel.GetSheet<CraftAction>();
            var itemSheet = DService.Data.Excel.GetSheet<Item>();
            var emoteSheet = DService.Data.Excel.GetSheet<Emote>();

            if (actionSheet == null || craftActionSheet == null || itemSheet == null || emoteSheet == null) return;

            foreach (var action in actionSheet)
            {
                if (action.IsPvP || string.IsNullOrEmpty(action.Name.ExtractText()))
                    continue;

                var name = action.Name.ExtractText();

                if (action.IsPlayerAction)
                {
                    if (!AllIconsCache.ContainsKey(name))
                        AllIconsCache[name] = action.Icon;

                    if (action.ClassJob.RowId > 0)
                    {
                        var jobID = action.ClassJob.RowId;
                        if (!JobSpecificIconsCache.ContainsKey(name))
                            JobSpecificIconsCache[name] = [];
                        JobSpecificIconsCache[name][jobID] = action.Icon;
                    }
                }
                else
                {
                    if (action.ClassJob.RowId > 0)
                    {
                        var jobID = action.ClassJob.RowId;
                        if (!JobSpecificIconsCache.ContainsKey(name))
                            JobSpecificIconsCache[name] = [];

                        if (!JobSpecificIconsCache[name].ContainsKey(jobID))
                            JobSpecificIconsCache[name][jobID] = action.Icon;
                    }
                    else
                        AllIconsCache.TryAdd(name, action.Icon);
                }
            }

            foreach (var craftAction in craftActionSheet)
            {
                if (string.IsNullOrEmpty(craftAction.Name.ExtractText()))
                    continue;

                var name = craftAction.Name.ExtractText();
                if (!AllIconsCache.ContainsKey(name))
                    AllIconsCache[name] = craftAction.Icon;

                var jobID = craftAction.ClassJob.RowId;
                if (!JobSpecificIconsCache.ContainsKey(name))
                    JobSpecificIconsCache[name] = [];
                JobSpecificIconsCache[name][jobID] = craftAction.Icon;
            }

            foreach (var item in itemSheet)
            {
                if (string.IsNullOrEmpty(item.Name.ExtractText()))
                    continue;

                var name = item.Name.ExtractText();
                if (!AllIconsCache.ContainsKey(name))
                    AllIconsCache[name] = item.Icon;
            }

            foreach (var emote in emoteSheet)
            {
                if (string.IsNullOrEmpty(emote.Name.ExtractText()))
                    continue;

                var name = emote.Name.ExtractText();
                if (!AllIconsCache.ContainsKey(name))
                    AllIconsCache[name] = emote.Icon;
            }
        }

        public static void InitializeEmoteCache()
        {
            var emoteSheet = DService.Data.Excel.GetSheet<Emote>();
            if (emoteSheet == null) return;

            foreach (var emote in emoteSheet)
            {
                if (string.IsNullOrEmpty(emote.Name.ExtractText()))
                    continue;

                var name = emote.Name.ExtractText();
                if (!EmoteCache.ContainsKey(name))
                    EmoteCache[name] = emote.RowId;
            }
        }

        public static uint? FindActionID(string skillName, ClassJob currentClassJob)
        {
            if (JobSpecificActionsCache.TryGetValue(skillName, out var jobActions) &&
                jobActions.TryGetValue(currentClassJob.RowId, out var jobSpecificId))
                return jobSpecificId;

            if (AllActionsCache.TryGetValue(skillName, out var generalId))
                return generalId;

            return null;
        }

        public static uint? FindMacroIconID(string miconString, ClassJob currentClassJob)
        {
            if (MacroCacheHelper.JobSpecificIcons.TryGetValue(miconString, out var jobIcons) &&
                jobIcons.TryGetValue(currentClassJob.RowId, out var jobSpecificIcon))
                return jobSpecificIcon;

            if (MacroCacheHelper.AllIcons.TryGetValue(miconString, out var generalIcon))
                return generalIcon;

            return null;
        }

        public static bool TryGetActionDisplay(uint actionID, out (uint iconId, string name, bool isCraftAction) displayInfo)
            => ActionDisplayCache.TryGetValue(actionID, out displayInfo);

        public static bool TryGetAdjustedAction(uint adjustedActionID, out uint originalActionID)
            => AdjustedToOriginalActionCache.TryGetValue(adjustedActionID, out originalActionID);

        public static void CacheAdjustedAction(uint adjustedActionID, uint originalActionID)
            => AdjustedToOriginalActionCache[adjustedActionID] = originalActionID;

        public static void CacheAdjustedActionIfMissing(uint adjustedActionID, uint originalActionID)
            => AdjustedToOriginalActionCache.TryAdd(adjustedActionID, originalActionID);

        public static void ClearAdjustedActionCache() => AdjustedToOriginalActionCache.Clear();

        public static IReadOnlyDictionary<string, Dictionary<uint, uint>> JobSpecificActions => JobSpecificActionsCache;
        public static IReadOnlyDictionary<string, uint> AllIcons => AllIconsCache;
        public static IReadOnlyDictionary<string, Dictionary<uint, uint>> JobSpecificIcons => JobSpecificIconsCache;
        public static IReadOnlyDictionary<string, uint> Emotes => EmoteCache;
    }
}
