using System;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace DailyRoutines.ModulesPublic;

public unsafe partial class MacroOptimization
{
    private static class SmartTargets
    {
        private enum PartyOrder
        {
            Default,
            ToTop,
            ToBottom,
        }

        public static GameObject* ResolvePlaceholderDetour(PronounModule* module, byte* str, byte a3, byte a4)
        {
            var orig = ResolvePlaceholderHook!.Original(module, str, a3, a4);
            if (orig != null)
                return orig;

            if (str == null)
                return null;

            var decoded = Marshal.PtrToStringUTF8((nint)str);
            if (string.IsNullOrEmpty(decoded))
                return null;

            var candidate = decoded.Trim();
            if (!candidate.StartsWith('<'))
                candidate = '<' + candidate;
            if (!candidate.EndsWith('>'))
                candidate += '>';

            return TryResolve(candidate, out var target) ? target : null;
        }

        public static bool TryResolve(string placeholder, out GameObject* target)
        {
            target = null;

            if (!TryParsePlaceholder(placeholder, out var selector, out var order))
                return false;

            if (selector.StartsWith("party.", StringComparison.OrdinalIgnoreCase))
                return TryResolveParty(selector["party.".Length..], order, out target);

            if (selector.StartsWith("enemy.", StringComparison.OrdinalIgnoreCase))
                return TryResolveEnemy(selector["enemy.".Length..], out target);

            return false;
        }

        public static bool IsSmartTargetPlaceholder(string placeholder)
        {
            if (!TryParsePlaceholder(placeholder, out var selector, out _))
                return false;

            return selector.StartsWith("party.", StringComparison.OrdinalIgnoreCase) ||
                   selector.StartsWith("enemy.", StringComparison.OrdinalIgnoreCase);
        }

        private static uint ResolveStatusID(string nameOrID)
        {
            nameOrID = nameOrID.Trim();
            if (uint.TryParse(nameOrID, out var id))
                return id;

            var row = LuminaGetter.Get<Status>()
                                  .FirstOrDefault(s => s.Name.ToString().Equals(nameOrID, StringComparison.OrdinalIgnoreCase));
            return row.RowId;
        }

        private static bool TryParsePlaceholder(string placeholder, out string selector, out PartyOrder order)
        {
            selector = string.Empty;
            order = PartyOrder.Default;

            var trimmed = placeholder.Trim();
            if (!trimmed.StartsWith('<') || !trimmed.EndsWith('>') || trimmed.Length < 3)
                return false;

            var inner = trimmed[1..^1].Trim();
            if (string.IsNullOrEmpty(inner))
                return false;

            inner = ExtractOrderOption(inner, out order);

            inner = Regex.Replace(inner, @"\s+", " ").Trim();
            inner = Regex.Replace(inner, @"\s*\.\s*", ".", RegexOptions.IgnoreCase);
            inner = Regex.Replace(inner, @"\s*!=\s*", "!=", RegexOptions.IgnoreCase);
            inner = Regex.Replace(inner, @"\s*=\s*", "=", RegexOptions.IgnoreCase);
            inner = Regex.Replace(inner, @"\s*:\s*", ":", RegexOptions.IgnoreCase);

            selector = inner;
            return !string.IsNullOrEmpty(selector);
        }

        private static string ExtractOrderOption(string input, out PartyOrder order)
        {
            order = PartyOrder.Default;
            var updated = input;

            var orderMatch = Regex.Match(updated, @"(?:^|[\s,;|])order\s*=\s*(toTop|toBottom)(?:$|[\s,;|])", RegexOptions.IgnoreCase);
            if (orderMatch.Success)
            {
                order = orderMatch.Groups[1].Value.Equals("toBottom", StringComparison.OrdinalIgnoreCase)
                    ? PartyOrder.ToBottom
                    : PartyOrder.ToTop;

                updated = Regex.Replace(updated, @"(?:^|[\s,;|])order\s*=\s*(toTop|toBottom)(?:$|[\s,;|])", " ", RegexOptions.IgnoreCase);
            }

            if (Regex.IsMatch(updated, @"(?:^|[\s,;|])toTop(?:$|[\s,;|])", RegexOptions.IgnoreCase))
            {
                order = PartyOrder.ToTop;
                updated = Regex.Replace(updated, @"(?:^|[\s,;|])toTop(?:$|[\s,;|])", " ", RegexOptions.IgnoreCase);
            }

            if (Regex.IsMatch(updated, @"(?:^|[\s,;|])toBottom(?:$|[\s,;|])", RegexOptions.IgnoreCase))
            {
                order = PartyOrder.ToBottom;
                updated = Regex.Replace(updated, @"(?:^|[\s,;|])toBottom(?:$|[\s,;|])", " ", RegexOptions.IgnoreCase);
            }

            return updated.Trim();
        }

        private static bool TryResolveParty(string selector, PartyOrder order, out GameObject* target)
        {
            target = null;
            var agent = AgentHUD.Instance();
            if (agent == null)
                return false;

            if (Regex.IsMatch(selector, @"^(minHp|maxHp)$", RegexOptions.IgnoreCase))
            {
                target = selector.Equals("minHp", StringComparison.OrdinalIgnoreCase)
                    ? GetPartyMemberWithMinHp(agent)
                    : GetPartyMemberWithMaxHp(agent);
                return target != null;
            }

            if (Regex.Match(selector, @"^job=(.+)$", RegexOptions.IgnoreCase) is { Success: true } jobMatch)
            {
                target = GetPartyMemberByJob(agent, jobMatch.Groups[1].Value, order);
                return target != null;
            }

            if (Regex.Match(selector, @"^role=(.+)$", RegexOptions.IgnoreCase) is { Success: true } roleMatch)
            {
                target = GetPartyMemberByRole(agent, roleMatch.Groups[1].Value, order);
                return target != null;
            }

            if (Regex.Match(selector, @"^buff(!=|=)(.+)$", RegexOptions.IgnoreCase) is { Success: true } buffMatch)
            {
                var shouldHave = buffMatch.Groups[1].Value == "=";
                target = GetPartyMemberByBuff(agent, buffMatch.Groups[2].Value, shouldHave, order);
                return target != null;
            }

            if (Regex.Match(selector, @"^(\d)$", RegexOptions.IgnoreCase) is { Success: true } numMatch)
            {
                var num = int.Parse(numMatch.Groups[1].Value);
                target = num is >= 1 and <= 8 ? GetPartyMemberByIndex(agent, num - 1) : null;
                return target != null;
            }

            if (selector.Equals("lowHp", StringComparison.OrdinalIgnoreCase))
            {
                target = GetPartyLowHpMember(agent, includeSelf: false);
                return target != null;
            }

            if (selector.Equals("lowHpOrMe", StringComparison.OrdinalIgnoreCase))
            {
                target = GetPartyLowHpMember(agent, includeSelf: true);
                return target != null;
            }

            if (selector.Equals("dead", StringComparison.OrdinalIgnoreCase))
            {
                target = GetPartyDeadMember(agent, order);
                return target != null;
            }

            if (selector.Equals("near", StringComparison.OrdinalIgnoreCase))
            {
                target = GetPartyDistanceMember(agent, nearest: true);
                return target != null;
            }

            if (selector.Equals("far", StringComparison.OrdinalIgnoreCase))
            {
                target = GetPartyDistanceMember(agent, nearest: false);
                return target != null;
            }

            if (selector.Equals("dispellable", StringComparison.OrdinalIgnoreCase))
            {
                target = GetPartyDispellableMember(agent, includeSelf: false, order);
                return target != null;
            }

            if (selector.Equals("dispellableOrMe", StringComparison.OrdinalIgnoreCase))
            {
                target = GetPartyDispellableMember(agent, includeSelf: true, order);
                return target != null;
            }

            if (Regex.Match(selector, @"^status(?:(OrMe)?):(\d+)$", RegexOptions.IgnoreCase) is { Success: true } statusMatch)
            {
                var includeSelf = statusMatch.Groups[1].Success;
                var statusID = uint.Parse(statusMatch.Groups[2].Value);
                target = GetPartyMemberByStatus(agent, statusID, includeSelf, order);
                return target != null;
            }

            return false;
        }

        private static bool TryResolveEnemy(string selector, out GameObject* target)
        {
            target = null;

            if (selector.Equals("near", StringComparison.OrdinalIgnoreCase))
            {
                target = GetEnemyDistanceTarget(nearest: true);
                return target != null;
            }

            if (selector.Equals("far", StringComparison.OrdinalIgnoreCase))
            {
                target = GetEnemyDistanceTarget(nearest: false);
                return target != null;
            }

            if (selector.Equals("lowHp", StringComparison.OrdinalIgnoreCase))
            {
                target = GetEnemyLowHpTarget();
                return target != null;
            }

            if (Regex.Match(selector, @"^status:(\d+)$", RegexOptions.IgnoreCase) is { Success: true } statusMatch)
            {
                var statusID = uint.Parse(statusMatch.Groups[1].Value);
                target = GetEnemyByStatus(statusID);
                return target != null;
            }

            return false;
        }

        private static GameObject* GetPartyMemberWithMinHp(AgentHUD* agent)
        {
            var hudPartyMember = agent->PartyMembers.ToArray()
                                     .Where(x => x.Object != null && x.Object->GetIsTargetable() && !x.Object->IsDead())
                                     .OrderBy(x => (float)x.Object->Health / x.Object->MaxHealth)
                                     .FirstOrDefault();
            return (GameObject*)hudPartyMember.Object;
        }

        private static GameObject* GetPartyMemberWithMaxHp(AgentHUD* agent)
        {
            var hudPartyMember = agent->PartyMembers.ToArray()
                                     .Where(x => x.Object != null && x.Object->GetIsTargetable() && !x.Object->IsDead())
                                     .OrderByDescending(x => (float)x.Object->Health / x.Object->MaxHealth)
                                     .FirstOrDefault();
            return (GameObject*)hudPartyMember.Object;
        }

        private static GameObject* GetPartyLowHpMember(AgentHUD* agent, bool includeSelf)
        {
            if (!includeSelf && agent->PartyMemberCount == 1)
                return null;

            var hudPartyMember = agent->PartyMembers.ToArray()
                                     .Where(x => includeSelf || x.ContentId != LocalPlayerState.ContentID)
                                     .Where(x => x.Object != null &&
                                                 x.Object->GetIsTargetable() &&
                                                 !x.Object->IsDead() &&
                                                 x.Object->Health != x.Object->MaxHealth)
                                     .OrderBy(x => (float)x.Object->Health / x.Object->MaxHealth)
                                     .FirstOrDefault();
            return (GameObject*)hudPartyMember.Object;
        }

        private static GameObject* GetPartyDeadMember(AgentHUD* agent, PartyOrder order)
        {
            if (agent->PartyMemberCount == 1)
                return null;

            var members = agent->PartyMembers.ToArray().AsEnumerable();
            if (order == PartyOrder.ToBottom)
                members = members.Reverse();

            var hudPartyMember = members.Where(x => x.ContentId != LocalPlayerState.ContentID &&
                                                    x.Object != null &&
                                                    x.Object->GetIsTargetable() &&
                                                    x.Object->IsDead())
                                        .FirstOrDefault();
            return (GameObject*)hudPartyMember.Object;
        }

        private static GameObject* GetPartyDistanceMember(AgentHUD* agent, bool nearest)
        {
            if (agent->PartyMemberCount == 1)
                return null;

            var localPlayer = Control.GetLocalPlayer();
            if (localPlayer == null)
                return null;

            var candidates = agent->PartyMembers.ToArray()
                                 .Where(x => x.ContentId != LocalPlayerState.ContentID &&
                                             x.Object != null &&
                                             x.Object->GetIsTargetable() &&
                                             !x.Object->IsDead());

            var hudPartyMember = nearest
                ? candidates.OrderBy(x => Vector3.DistanceSquared(localPlayer->Position, x.Object->Position)).FirstOrDefault()
                : candidates.OrderByDescending(x => Vector3.DistanceSquared(localPlayer->Position, x.Object->Position)).FirstOrDefault();
            return (GameObject*)hudPartyMember.Object;
        }

        private static GameObject* GetPartyDispellableMember(AgentHUD* agent, bool includeSelf, PartyOrder order)
        {
            if (!includeSelf && agent->PartyMemberCount == 1)
                return null;

            var members = agent->PartyMembers.ToArray().AsEnumerable();
            if (!includeSelf)
                members = members.Where(x => x.ContentId != LocalPlayerState.ContentID);

            if (order == PartyOrder.ToBottom)
                members = members.Reverse();

            var hudPartyMember = members.Where(x => x.Object != null && x.Object->GetIsTargetable() && !x.Object->IsDead())
                                        .FirstOrDefault(x =>
                                        {
                                            var statuses = x.Object->GetStatusManager()->Status;
                                            foreach (var status in statuses)
                                            {
                                                if (PresetSheet.DispellableStatuses.ContainsKey(status.StatusId))
                                                    return true;
                                            }

                                            return false;
                                        });
            return (GameObject*)hudPartyMember.Object;
        }

        private static GameObject* GetPartyMemberByStatus(AgentHUD* agent, uint statusID, bool includeSelf, PartyOrder order)
        {
            if (!includeSelf && agent->PartyMemberCount == 1)
                return null;

            var members = agent->PartyMembers.ToArray().AsEnumerable();
            if (!includeSelf)
                members = members.Where(x => x.ContentId != LocalPlayerState.ContentID);

            if (order == PartyOrder.ToBottom)
                members = members.Reverse();

            var hudPartyMember = members.Where(x => x.Object != null && x.Object->GetIsTargetable() && !x.Object->IsDead())
                                        .FirstOrDefault(x =>
                                        {
                                            var statuses = x.Object->GetStatusManager()->Status;
                                            foreach (var status in statuses)
                                            {
                                                if (status.StatusId == statusID)
                                                    return true;
                                            }

                                            return false;
                                        });
            return (GameObject*)hudPartyMember.Object;
        }

        private static GameObject* GetPartyMemberByJob(AgentHUD* agent, string jobName, PartyOrder order)
        {
            jobName = jobName.Trim();
            if (string.IsNullOrEmpty(jobName))
                return null;

            var targetJob = LuminaGetter.Get<ClassJob>()
                                        .FirstOrDefault(job =>
                                            job.Name.ToString().Equals(jobName, StringComparison.OrdinalIgnoreCase) ||
                                            job.Abbreviation.ToString().Equals(jobName, StringComparison.OrdinalIgnoreCase));
            if (targetJob.RowId == 0)
                return null;

            var members = agent->PartyMembers.ToArray().AsEnumerable();
            if (order == PartyOrder.ToBottom)
                members = members.Reverse();

            var hudPartyMember = members.FirstOrDefault(x =>
                x.Object != null && x.Object->GetIsTargetable() && x.Object->ClassJob == targetJob.RowId);
            return (GameObject*)hudPartyMember.Object;
        }

        private static GameObject* GetPartyMemberByRole(AgentHUD* agent, string roleName, PartyOrder order)
        {
            roleName = roleName.Trim();
            byte targetRole = roleName.ToLowerInvariant() switch
            {
                "tank" or "t" => 1,
                "healer" or "heal" or "h" => 4,
                "dps" or "d" => 2,
                "melee" => 2,
                "ranged" or "range" => 3,
                _ => 0
            };

            if (targetRole == 0)
                return null;

            var members = agent->PartyMembers.ToArray().AsEnumerable();
            if (order == PartyOrder.ToBottom)
                members = members.Reverse();

            var hudPartyMember = members.FirstOrDefault(x =>
            {
                if (x.Object == null || !x.Object->GetIsTargetable())
                    return false;

                var job = LuminaGetter.GetRow<ClassJob>(x.Object->ClassJob);
                return job?.Role == targetRole;
            });
            return (GameObject*)hudPartyMember.Object;
        }

        private static GameObject* GetPartyMemberByBuff(AgentHUD* agent, string buffStr, bool shouldHaveBuff, PartyOrder order)
        {
            buffStr = buffStr.Trim();
            if (string.IsNullOrWhiteSpace(buffStr))
                return null;

            var buffID = ResolveStatusID(buffStr);
            if (buffID == 0)
                return null;

            var members = agent->PartyMembers.ToArray().AsEnumerable();
            if (order == PartyOrder.ToBottom)
                members = members.Reverse();

            var hudPartyMember = members.FirstOrDefault(x =>
            {
                if (x.Object == null || !x.Object->GetIsTargetable())
                    return false;

                var statusList = StatusList.CreateStatusListReference((nint)x.Object->GetStatusManager());
                if (statusList == null)
                    return false;

                var hasBuff = statusList.HasStatus(buffID);
                return shouldHaveBuff ? hasBuff : !hasBuff;
            });
            return (GameObject*)hudPartyMember.Object;
        }

        private static GameObject* GetPartyMemberByIndex(AgentHUD* agent, int index)
        {
            var partyMembers = agent->PartyMembers.ToArray();
            if (index < 0 || index >= partyMembers.Length)
                return null;

            return (GameObject*)partyMembers[index].Object;
        }

        private static GameObject* GetEnemyDistanceTarget(bool nearest)
        {
            var localPlayer = Control.GetLocalPlayer();
            if (localPlayer == null)
                return null;

            var candidates = DService.ObjectTable.Where(IsValidEnemyTarget);
            var enemy = nearest
                ? candidates.MinBy(x => Vector3.DistanceSquared(localPlayer->Position, x.Position))
                : candidates.MaxBy(x => Vector3.DistanceSquared(localPlayer->Position, x.Position));

            return enemy == null ? null : (GameObject*)enemy.ToStruct();
        }

        private static GameObject* GetEnemyLowHpTarget()
        {
            var enemy = DService.ObjectTable
                                .Where(IsValidEnemyTarget)
                                .OfType<IBattleChara>()
                                .Where(x => x.CurrentHp != x.MaxHp)
                                .OrderBy(x => (float)x.CurrentHp / x.MaxHp)
                                .FirstOrDefault();
            return enemy == null ? null : (GameObject*)enemy.ToStruct();
        }

        private static GameObject* GetEnemyByStatus(uint statusID)
        {
            var enemy = DService.ObjectTable
                                .Where(IsValidEnemyTarget)
                                .OfType<IBattleChara>()
                                .FirstOrDefault(x => x.StatusList.HasStatus(statusID));
            return enemy == null ? null : (GameObject*)enemy.ToStruct();
        }

        private static bool IsValidEnemyTarget(IGameObject obj) =>
            obj is IBattleChara { Battalion: BattalionFlags.Enemy, IsTargetable: true, IsDead: false } chara &&
            chara.YalmDistanceX <= 45 &&
            chara.YalmDistanceZ <= 45 &&
            ((GameObject*)chara.ToStruct())->IsReadyToDraw();
    }
}
