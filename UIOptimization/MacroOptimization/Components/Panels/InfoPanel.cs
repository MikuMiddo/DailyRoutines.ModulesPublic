using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using DailyRoutines.Abstracts;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;

namespace DailyRoutines.ModulesPublic;

public unsafe partial class MacroOptimization
{
internal sealed unsafe class InfoPanel : ResNode // 宏信息面板
{
    private readonly MacroConfig          ModuleConfig;
    private readonly DailyModuleBase      ModuleInstance;

    private readonly List<(int line, string message, string level)> ValidationList = [];

    private List<uint> ParsedMacroActionIDs = [];

    private ResNode?                MacroDetailEditContainerNode;
    private ResNode?                MacroDetailEditNode;
    private TextMultiLineInputNodeScrollable? MacroLinesInputNode;
    private IconNode?               MacroIconNode;
    private SimpleImageNode?        MacroRecordButtonBackgroundNode;
    private TextInputNode?          NameInputNode;
    private TextInputNode?          DescriptionInputNode;
    private TextNode?               ExecuteTimeNode;
    private TextureButtonNode?      MacroSettingButtonNode;
    private TextureButtonNode?      MacroRecordButtonNode;
    private TextureButtonNode?      InfiniteLoopButtonNode;
    private SimpleImageNode?        InfiniteLoopButtonBackgroundNode;
    private TextButtonNode?         ExecuteButton;
    private TextButtonNode?         StopButton;
    private TextButtonNode?         PauseButton;
    private CheckboxNode?           FeasibilityCheckbox;
    private TextNode?               FeasibilityResultNode;

    private string MacroContentBuffer     = string.Empty;
    private int    CurrentMacroIndex      = -1;
    private int    CurrentValidationIndex = 0;

    public bool ForceInfiniteLoop = false;

    public Action<int>?         OnExecuteMacro;
    public Action?              OnStopMacro;
    public Action<bool>?        OnPauseMacro;
    public Action<int>?         OnDeleteMacro;
    public Action<string>?      OnMacroContentChanged;
    public Action<int, string>? OnMacroNameChanged;
    public Action<int, string>? OnMacroDescriptionChanged;
    public Action<int, uint>?   OnMacroIconChanged;
    public Action<int>?         OnOpenMacroSettings;
    public Action?              OnToggleRecording;

    public InfoPanel(MacroConfig config, DailyModuleBase instance)
    {
        ModuleConfig = config;
        ModuleInstance = instance;

        Position = new Vector2(267f, 0f);
        Size = new Vector2(380f, 580f);
        IsVisible = true;
    }

    public void Build()
    {
        MacroDetailEditContainerNode = new ResNode
        {
            Position = new Vector2(0f, 15f),
            Size = new Vector2(423f, 47f),
            IsVisible = true,
        };
        MacroDetailEditContainerNode.AttachNode(this);

        FeasibilityResultNode = new TextNode
        {
            IsVisible = true,
            Size = new(740, 24),
            Position = new(-190f, -40f),
            SeString = GetLoc("MacroOptimization-Info-FeasibilityNotPassed"),
            FontSize = 14,
            AlignmentType = AlignmentType.Center,
            TextColor = KnownColor.Orange.Vector(),
            TextFlags = TextFlags.Ellipsis,
        };

        FeasibilityResultNode.AddEvent(AtkEventType.MouseWheel, () =>
        {
            if (ValidationList.Count <= 1) return;

            var wheelDelta = UIInputData.Instance()->CursorInputs.MouseWheel;
            if (wheelDelta > 0)
                CurrentValidationIndex = (CurrentValidationIndex - 1 + ValidationList.Count) % ValidationList.Count;
            else if (wheelDelta < 0)
                CurrentValidationIndex = (CurrentValidationIndex + 1) % ValidationList.Count;

            UpdateValidationDisplay();
        });

        FeasibilityResultNode.AttachNode(this);

        var standaloneRunButton = new TextureButtonNode // 独立运行按钮
        {
            IsVisible = true,
            Size = new(24, 24),
            Position = new(240f, -9f),
            TexturePath = "ui/uld/CircleButtons_hr1.tex",
            TextureSize = new(28, 28),
            TextureCoordinates = new(56, 28),
            TextTooltip = GetLoc("MacroOptimization-Info-StandaloneRunTooltip"),
            OnClick = () =>
            {
                if (CurrentMacroIndex >= 0 && CurrentMacroIndex < ModuleConfig.ExtendMacroLists.Count)
                    OpenStandaloneMacroWindowByIndex(CurrentMacroIndex);
            }
        };
        standaloneRunButton.AttachNode(this);

        var helpButton = new TextureButtonNode // 帮助按钮
        {
            IsVisible = true,
            Size = new(24, 24),
            Position = new(260f, -9f),
            TexturePath = "ui/uld/CircleButtons_hr1.tex",
            TextureSize = new(28, 28),
            TextureCoordinates = new(84, 0),
            TextTooltip = GetLoc("MacroOptimization-Info-HelpTooltip"),
            OnClick = () => MacroHelpAddon.Toggle(),
        };
        helpButton.AttachNode(this);

        FeasibilityCheckbox = new CheckboxNode // 可行性检测复选框
        {
            IsVisible = true,
            Size = new(20, 20),
            Position = new(290f, -8f),
            SeString = GetLoc("MacroOptimization-Info-FeasibilityCheck"),
            OnClick = (isChecked) =>
            {
                ValidationList.Clear();
                CurrentValidationIndex = 0;
                if (isChecked)
                    PerformFeasibilityCheck();
                else
                {
                    FeasibilityResultNode.SeString = GetLoc("MacroOptimization-Info-FeasibilityNotPassed");
                    FeasibilityResultNode.TextColor = KnownColor.Orange.Vector();
                    ExecuteButton.IsEnabled = false;
                }
            }
        };
        FeasibilityCheckbox.AttachNode(this);

        var removeButton = new TextButtonNode
        {
            IsVisible = true,
            Size = new(100, 24),
            Position = new(290f, 15f),
            String = GetLoc("Delete"),
            OnClick = () =>
            {
                OnDeleteMacro?.Invoke(CurrentMacroIndex);
            }
        };
        removeButton.AttachNode(this);

        ExecuteButton = new TextButtonNode
        {
            IsVisible = true,
            Size = new(100, 24),
            Position = new(290f, 37f),
            String = GetLoc("Execute"),
            OnClick = () =>
            {
                OnExecuteMacro?.Invoke(CurrentMacroIndex);
            }
        };
        ExecuteButton.AttachNode(this);
        ExecuteButton.IsEnabled = false;

        StopButton = new TextButtonNode
        {
            IsVisible = false,
            Size = new(50, 24),
            Position = new(290f, 37f),
            String = GetLoc("MacroOptimization-Common-Stop"),
            OnClick = () =>
            {
                OnStopMacro?.Invoke();
            }
        };
        StopButton.AttachNode(this);

        PauseButton = new TextButtonNode
        {
            IsVisible = false,
            Size = new(50, 24),
            Position = new(340f, 37f),
            String = GetLoc("Pause"),
            OnClick = () =>
            {
                var shouldResume = PauseButton.String == GetLoc("MacroOptimization-Common-Resume");
                PauseButton.String = shouldResume ? GetLoc("Pause") : GetLoc("MacroOptimization-Common-Resume");
                OnPauseMacro?.Invoke(!shouldResume);
            }
        };
        PauseButton.AttachNode(this);

        MacroLinesInputNode = new TextMultiLineInputNodeScrollable
        {
            Position = new Vector2(0f, 60f),
            Size = new Vector2(388f, 517f),
            IsVisible = true,
        };

        MacroLinesInputNode.OnInputReceived += (input) =>
        {
            var newText = MacroLinesInputNode.String;
            if (newText != MacroContentBuffer)
            {
                MacroContentBuffer = newText;
                OnMacroContentChanged?.Invoke(MacroContentBuffer);
                
                if (FeasibilityCheckbox != null && FeasibilityCheckbox.IsChecked)
                    PerformFeasibilityCheck();
            }
        };

        MacroLinesInputNode.AttachNode(this);
    }

    public void SetMacroIndex(int index) => CurrentMacroIndex = index;

    public void SetMacroData(List<uint> macroLines, List<string> commandTypes)
    {
        ParsedMacroActionIDs = macroLines;

        if (FeasibilityCheckbox != null && FeasibilityCheckbox.IsChecked) // 如果可行性检测已开启，自动进行检测
            PerformFeasibilityCheck();
    }

    public void RefreshDetailEditNode()
    {
        if (CurrentMacroIndex < 0 || CurrentMacroIndex >= ModuleConfig.ExtendMacroLists.Count) return;

        var currentMacro = ModuleConfig.ExtendMacroLists[CurrentMacroIndex];

        if (MacroDetailEditNode == null)
        {
            MacroDetailEditNode = new ResNode { IsVisible = true };
            MacroDetailEditNode.AttachNode(MacroDetailEditContainerNode);

            MacroIconNode = new IconNode
            {
                IsVisible = true,
                Size = new(32),
                Position = new(0f, 0f)
            };
            MacroIconNode.AttachNode(MacroDetailEditNode);

            NameInputNode = new TextInputNode
            {
                Size = new(150.0f, 27.0f),
                IsVisible = true,
                Position = new(40f, 0f),
                OnInputReceived = newName =>
                {
                    var newNameText = newName.ExtractText();
                    ModuleConfig.ExtendMacroLists[CurrentMacroIndex].Name = newNameText;
                    OnMacroNameChanged?.Invoke(CurrentMacroIndex, newNameText);
                    ModuleConfig.Save(ModuleInstance);
                }
            };
            NameInputNode.AttachNode(MacroDetailEditNode);

            ExecuteTimeNode = new TextNode
            {
                Size = new(95.0f, 15.0f),
                IsVisible = true,
                Position = new(190f, 5f),
                FontSize = 12,
                AlignmentType = AlignmentType.Left
            };
            ExecuteTimeNode.AttachNode(MacroDetailEditNode);

            DescriptionInputNode = new TextInputNode
            {
                Size = new(250.0f, 27.0f),
                IsVisible = true,
                Position = new(40f, 20f),
                OnInputReceived = newDescription =>
                {
                    var newDescText = newDescription.ExtractText();
                    ModuleConfig.ExtendMacroLists[CurrentMacroIndex].Description = newDescText;
                    OnMacroDescriptionChanged?.Invoke(CurrentMacroIndex, newDescText);
                    ModuleConfig.Save(ModuleInstance);
                }
            };
            DescriptionInputNode.AttachNode(MacroDetailEditNode);

            MacroSettingButtonNode = new TextureButtonNode
            {
                Position = new(0f, -27f),
                Size = new(28f, 28f),
                IsVisible = true,
                TexturePath = "ui/uld/CircleButtons_hr1.tex",
                TextureSize = new(28, 28),
                TextureCoordinates = new(0, 0),
                TextTooltip = GetLoc("MacroOptimization-Window-Settings"),
                OnClick = () => OnOpenMacroSettings?.Invoke(CurrentMacroIndex),
            };
            MacroSettingButtonNode.AttachNode(MacroDetailEditNode);

            MacroRecordButtonNode = new TextureButtonNode
            {
                Position = new(25f, -26f),
                Size = new(28f, 28f),
                IsVisible = true,
                TexturePath = "ui/uld/lfg_hr1.tex",
                TextureSize = new(28, 28),
                TextureCoordinates = new(28, 135),
                TextTooltip = GetLoc("MacroOptimization-Info-RecordTooltip"),
                OnClick = () => OnToggleRecording?.Invoke(),
            };
            MacroRecordButtonNode.AttachNode(MacroDetailEditNode);

            MacroRecordButtonBackgroundNode = new SimpleImageNode
            {
                Position = new(0f, 0f),
                Size = new(28f, 28f),
                IsVisible = false,
                TexturePath = "ui/uld/circlebuttons_hr1.tex",
                TextureSize = new(28, 28),
                TextureCoordinates = new(84, 84),
            };
            MacroRecordButtonBackgroundNode.AttachNode(MacroRecordButtonNode);
            
            InfiniteLoopButtonNode = new TextureButtonNode
            {
                Position = new(50f, -27f),
                Size = new(28f, 28f),
                IsVisible = true,
                TexturePath = "ui/uld/CircleButtons_hr1.tex",
                TextureSize = new(28, 28),
                TextureCoordinates = new(112, 0),
                TextTooltip = GetLoc("MacroOptimization-Info-ForceInfiniteLoopTooltip"),
                OnClick = () =>
                {
                    ForceInfiniteLoop = !ForceInfiniteLoop;
                    InfiniteLoopButtonBackgroundNode.IsVisible = ForceInfiniteLoop;
                    InfiniteLoopButtonNode.TextTooltip = ForceInfiniteLoop
                                                   ? GetLoc("MacroOptimization-Info-ForceInfiniteLoopEnabledTooltip")
                                                   : GetLoc("MacroOptimization-Info-ForceInfiniteLoopTooltip");
                }
            };
            InfiniteLoopButtonNode.AttachNode(MacroDetailEditNode);

            InfiniteLoopButtonBackgroundNode = new SimpleImageNode
            {
                Position = new(0f, 0f),
                Size = new(28f, 28f),
                IsVisible = false,
                TexturePath = "ui/uld/circlebuttons_hr1.tex",
                TextureSize = new(28, 28),
                TextureCoordinates = new(84, 84),
            };
            InfiniteLoopButtonBackgroundNode.AttachNode(InfiniteLoopButtonNode);
        }

        MacroIconNode.IconId = currentMacro.IconID; // 更新现有节点的内容
        NameInputNode.SeString = currentMacro.Name;
        DescriptionInputNode.SeString = currentMacro.Description;
        ExecuteTimeNode.SeString = GetLoc("MacroOptimization-Info-EstimatedTime", FormatExecuteTime(CalculateTotalExecuteTime()));
    }

    public string GetTextBuffer() => MacroContentBuffer;
    public void SetTextBuffer(string text)
    {
        MacroContentBuffer = text;
        if (MacroLinesInputNode != null && MacroLinesInputNode.IsFocused)
            MacroLinesInputNode.ClearFocus();
        MacroLinesInputNode.String = text;
    }

    public void UpdateIcon(uint iconID)
    {
        if (CurrentMacroIndex < 0 || CurrentMacroIndex >= ModuleConfig.ExtendMacroLists.Count) return;

        if (MacroIconNode != null)
            MacroIconNode.IconId = iconID;
    }

    public void ShowExecuteButton()
    {
        ExecuteButton.IsVisible = true;
        StopButton.IsVisible = false;
        PauseButton.IsVisible = false;
        PauseButton.String = GetLoc("Pause");
    }

    public void ShowStopPauseButtons()
    {
        ExecuteButton.IsVisible = false;
        StopButton.IsVisible = true;
        PauseButton.IsVisible = true;
    }

    public void SetRecordingState(bool isRecording)
    {
        MacroRecordButtonBackgroundNode.IsVisible = isRecording;
        MacroRecordButtonNode.ImageNode.AddColor = isRecording
            ? new Vector3(40, 40, 40)
            : Vector3.Zero;

        MacroRecordButtonNode.TextTooltip = isRecording
            ? GetLoc("MacroOptimization-Info-RecordingTooltip")
            : GetLoc("MacroOptimization-Info-RecordTooltip");
    }

    private void PerformFeasibilityCheck()
    {
        var lines = Regex.Split(MacroContentBuffer, "\r\n|\r|\n");
        var totalLines = 0;
        var validLines = 0;

        ValidationList.Clear();
        CurrentValidationIndex = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            totalLines++;
            if (HasLineErrors(line, ModuleConfig, out var message, out var level))
            {
                ValidationList.Add((i + 1, message!, level));
                if (level == "warning")
                    validLines++;
            }
            else
                validLines++;
        }

        if (ValidationList.Count == 0)
        {
            FeasibilityResultNode.SeString = GetLoc("MacroOptimization-Info-FeasibilityPassed", validLines, totalLines);
            FeasibilityResultNode.TextColor = KnownColor.Green.Vector();
            ExecuteButton.IsEnabled = true;
        }
        else
        {
            var hasError = ValidationList.Any(v => v.level == "error");
            ExecuteButton.IsEnabled = !hasError;
            UpdateValidationDisplay();
        }
    }

    private void UpdateValidationDisplay()
    {
        if (ValidationList.Count == 0) return;

        if (CurrentValidationIndex < 0 || CurrentValidationIndex >= ValidationList.Count)
            CurrentValidationIndex = 0;

        var current = ValidationList[CurrentValidationIndex];
        var levelText = current.level == "error"
            ? GetLoc("MacroOptimization-Info-ValidationLevelError")
            : GetLoc("MacroOptimization-Info-ValidationLevelWarning");
        var color = current.level == "error" ? KnownColor.Red.Vector() : KnownColor.Yellow.Vector();

        var sameLevelItems = ValidationList.Where(v => v.level == current.level).ToList();
        var currentLevelIndex = sameLevelItems.FindIndex(v => v.line == current.line && v.message == current.message) + 1;
        var sameLevelCount = sameLevelItems.Count;

        FeasibilityResultNode.SeString = GetLoc(
            "MacroOptimization-Info-FeasibilityDetail",
            current.line,
            levelText,
            current.message,
            currentLevelIndex,
            sameLevelCount);
        FeasibilityResultNode.TextColor = color;
    }

    private static bool HasLineErrors(string line, MacroConfig config, out string? message, out string level)
    {
        if (!line.StartsWith('/'))
        {
            message = GetLoc("MacroOptimization-Validate-MustStartWithSlash");
            level = "error";
            return true;
        }

        if (line.StartsWith("/if", StringComparison.OrdinalIgnoreCase)) // 特殊处理 /if 条件命令，强制要求中括号 /if [条件]
        {
            var ifMatch = Regex.Match(line, @"^/if\s+\[(.+?)\]\s+(/.+)$", RegexOptions.IgnoreCase);
            if (!ifMatch.Success)
            {
                message = GetLoc("MacroOptimization-Validate-IfConditionFormat");
                level = "error";
                return true;
            }

            var condition = ifMatch.Groups[1].Value.Trim();
            var innerCommand = ifMatch.Groups[2].Value.Trim();

            if (HasConditionErrors(condition, out message, out level)) // 检查条件表达式是否合法
                return true;

            if (HasLineErrors(innerCommand, config, out message, out level)) // 递归检查内部命令
            {
                message = GetLoc("MacroOptimization-Validate-IfInnerError", message ?? string.Empty);
                return true;
            }

            message = null;
            level = string.Empty;
            return false;
        }

        if (HasCommandErrors(line, config, out message, out level))
            return true;

        var bracketErrors = CheckBracketSuffix(line);
        if (!string.IsNullOrEmpty(bracketErrors))
        {
            message = bracketErrors;
            level = "error";
            return true;
        }

        var skillName = MacroExecutor.ExtractSkillName(line);
        if (!string.IsNullOrEmpty(skillName))
        {
            var foundActionID = MacroCacheHelper.FindActionID(skillName, LocalPlayerState.ClassJobData);
            if (foundActionID == null)
            {
                message = GetLoc("MacroOptimization-Validate-SkillNotFound", skillName);
                level = "error";
                return true;
            }
        }

        var echoMatch = Regex.Match(line, @"^/(echo|e)(\s|$)", RegexOptions.IgnoreCase);
        if (echoMatch.Success)
        {
            var command = echoMatch.Groups[1].Value;
            if (!line.Substring(echoMatch.Groups[0].Value.Length - echoMatch.Groups[2].Value.Length).Contains(' ') ||
                string.IsNullOrWhiteSpace(line.Substring(("/" + command).Length)))
            {
                message = GetLoc("MacroOptimization-Validate-EchoNoOutput");
                level = "warning";
                return true;
            }
        }

        message = null;
        level = string.Empty;
        return false;
    }

    private static bool HasConditionErrors(string condition, out string? message, out string level)
    {
        if (Regex.IsMatch(condition, @"^combat=(true|false)$", RegexOptions.IgnoreCase)) // 战斗状态判断可以不带前缀
        {
            message = null;
            level = string.Empty;
            return false;
        }

        if (Regex.IsMatch(condition, @"^job=.+$", RegexOptions.IgnoreCase)) // 职业判断可以不带前缀
        {
            message = null;
            level = string.Empty;
            return false;
        }

        var targetPrefixMatch = Regex.Match(condition, @"^(self|target|focus|party)\.(.+)$", RegexOptions.IgnoreCase); // 解析带目标前缀的条件 (self.xxx / target.xxx / focus.xxx / party.xxx / party.N.xxx)
        if (!targetPrefixMatch.Success)
        {
            message = GetLoc("MacroOptimization-Validate-ConditionMissingPrefix", condition);
            level = "error";
            return true;
        }

        var prefix = targetPrefixMatch.Groups[1].Value.ToLower();
        var actualCondition = targetPrefixMatch.Groups[2].Value;

        if (prefix != "party") // 非 party 前缀直接验证条件
            return HasUnsupportedCondition(actualCondition, out message, out level);

        var partyIndexMatch = Regex.Match(actualCondition, @"^(\d+)\.(.+)$"); // party 前缀：检查是否为队伍成员索引格式 party.1.hp<50
        if (!partyIndexMatch.Success)
            return HasPartyConditionErrors(actualCondition, out message, out level); // 队伍整体条件: party.hp<30 / party.minHp<30 / party.count>4

        var indexStr = partyIndexMatch.Groups[1].Value;
        var memberCondition = partyIndexMatch.Groups[2].Value;

        if (!int.TryParse(indexStr, out var index) || index < 1 || index > 8)
        {
            message = GetLoc("MacroOptimization-Validate-PartyIndexRange", indexStr);
            level = "error";
            return true;
        }

        return HasUnsupportedCondition(memberCondition, out message, out level);
    }

    private static bool HasPartyConditionErrors(string condition, out string? message, out string level)
    {
        if (Regex.IsMatch(condition, @"^hp\s*(?:[<>]=?|!=|=)\s*\d+$")) // party.hp<30 - 队伍中有人血量<30% (支持空格: hp != 30)
        {
            message = null;
            level = string.Empty;
            return false;
        }

        if (Regex.IsMatch(condition, @"^minHp\s*(?:[<>]=?|!=|=)\s*\d+$", RegexOptions.IgnoreCase)) // party.minHp<30 - 队伍最低血量<30% (支持空格: minHp != 30)
        {
            message = null;
            level = string.Empty;
            return false;
        }

        if (Regex.IsMatch(condition, @"^count\s*(?:[<>]=?|!=|=)\s*\d+$", RegexOptions.IgnoreCase)) // party.count>4 或 party.count=8 或 party.count!=8 (支持空格: count != 8)
        {
            message = null;
            level = string.Empty;
            return false;
        }

        message = GetLoc("MacroOptimization-Validate-UnsupportedPartyCondition", condition);
        level = "error";
        return true;
    }

    private static bool HasUnsupportedCondition(string condition, out string? message, out string level)
    {
        if (condition.Equals("exists", StringComparison.OrdinalIgnoreCase) ||
            condition.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            message = null;
            level = string.Empty;
            return false;
        }

        if (Regex.IsMatch(condition, @"^hp\s*(?:[<>]=?|!=|=)\s*\d+$")) // hp<50 或 hp>80 或 hp=100 或 hp!=100 - HP条件 (支持空格: hp != 100)
        {
            message = null;
            level = string.Empty;
            return false;
        }

        if (Regex.IsMatch(condition, @"^mp\s*(?:[<>]=?|!=|=)\s*\d+$")) // mp<30 或 mp>50 或 mp=100 或 mp!=0 - MP条件 (支持空格: mp != 0)
        {
            message = null;
            level = string.Empty;
            return false;
        }

        if (Regex.IsMatch(condition, @"^buff\s*(?:=|!=)\s*.+$", RegexOptions.IgnoreCase)) // buff=名称或ID 或 buff!=名称或ID - Buff存在/不存在 (支持空格: buff != 再生)
        {
            message = null;
            level = string.Empty;
            return false;
        }

        if (Regex.IsMatch(condition, @"^name\s*(?:=|!=)\s*.+$", RegexOptions.IgnoreCase)) // name=名称 或 name!=名称 - 名称匹配/不匹配 (支持空格: name != BOSS)
        {
            message = null;
            level = string.Empty;
            return false;
        }

        message = GetLoc("MacroOptimization-Validate-UnsupportedConditionExpression", condition);
        level = "error";
        return true;
    }

    private static bool HasCommandErrors(string line, MacroConfig config, out string? message, out string level)
    {
        var commandMatch = Regex.Match(line, @"^/(\w*)");
        if (!commandMatch.Success)
        {
            message = null;
            level = string.Empty;
            return false;
        }

        var commandWithSlash = commandMatch.Groups[0].Value;
        var commandName = commandMatch.Groups[1].Value;

        if (commandWithSlash == "/")
        {
            message = GetLoc("MacroOptimization-Validate-IncompleteCommandSlash");
            level = "error";
            return true;
        }

        if (!SupportedCommands.Contains(commandName)) // 检查是否为支持的命令
        {
            var emoteName = MacroExecutor.ExtractEmoteName(line);
            if (!string.IsNullOrEmpty(emoteName) && MacroCacheHelper.Emotes.ContainsKey(emoteName))
            {
                message = null;
                level = string.Empty;
                return false;
            }

            message = GetLoc("MacroOptimization-Validate-UnknownCommand", commandWithSlash);
            level = "warning";
            return true;
        }

        if (!CommandsRequiringArgs.Contains(commandName)) // 检查需要参数的命令是否提供了参数
        {
            message = null;
            level = string.Empty;
            return false;
        }

        var afterCommand = line[commandWithSlash.Length..].Trim();
        if (string.IsNullOrWhiteSpace(afterCommand))
        {
            message = GetLoc("MacroOptimization-Validate-MissingCommandArgs", commandWithSlash);
            level = "error";
            return true;
        }

        if (commandName.Equals("call", StringComparison.OrdinalIgnoreCase)) // /call 命令特殊验证：检查宏名称是否存在
        {
            var callMatch = Regex.Match(line, @"^/call\s+(.+?)(?:\s+\d+)?$", RegexOptions.IgnoreCase);
            if (callMatch.Success)
            {
                var macroName = callMatch.Groups[1].Value.Trim();
                var matchingMacros = config.ExtendMacroLists.Where(m => m.Name == macroName).ToList();

                if (matchingMacros.Count == 0)
                {
                    message = GetLoc("MacroOptimization-Chat-MacroNotFound", macroName);
                    level = "error";
                    return true;
                }

                if (matchingMacros.Count > 1)
                {
                    message = GetLoc("MacroOptimization-Validate-DuplicateMacroNames", matchingMacros.Count, macroName);
                    level = "warning";
                    return true;
                }
            }
        }

        message = null;
        level = string.Empty;
        return false;
    }

    private float CalculateTotalExecuteTime()
    {
        if (CurrentMacroIndex < 0 || CurrentMacroIndex >= ModuleConfig.ExtendMacroLists.Count)
            return 0f;

        var lines = Regex.Split(MacroContentBuffer, "\r\n|\r|\n");
        var totalTimeMs = 0;
        var macroIndex = 0;
        var defaultInterval = ModuleConfig.ExtendMacroLists[CurrentMacroIndex].DefaultInterval;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var commandMatch = Regex.Match(line, @"^/(\w+)"); // 跳过不可执行命令
            if (commandMatch.Success && NonExecutableCommands.Contains(commandMatch.Groups[1].Value))
                continue;

            var actionID = macroIndex < ParsedMacroActionIDs.Count ? ParsedMacroActionIDs[macroIndex] : 0u;
            totalTimeMs += MacroExecutor.ExtractWaitTime(line, actionID, defaultInterval);
            macroIndex++;
        }

        return totalTimeMs / 1000f;
    }

    public void UpdateExecuteTime()
    {
        if (ExecuteTimeNode != null)
            ExecuteTimeNode.SeString = GetLoc("MacroOptimization-Info-EstimatedTime", FormatExecuteTime(CalculateTotalExecuteTime()));
    }

    private static string FormatExecuteTime(float seconds)
    {
        if (seconds >= 1000)
            return ((int)seconds).ToString();
        if (seconds >= 100)
            return seconds.ToString("0.0");
        if (seconds >= 10)
            return seconds.ToString("0.00");
        return seconds.ToString("0.000");
    }

    private static string CheckBracketSuffix(string line)
    {
        if (line.Contains('<') && !line.Contains('>'))
        {
            var incompleteMatch = Regex.Match(line, @"<[^>]*$");
            if (incompleteMatch.Success)
                return GetLoc("MacroOptimization-Validate-SuffixIncomplete", incompleteMatch.Value);
        }

        var bracketMatches = Regex.Matches(line, @"<[^>]*>", RegexOptions.IgnoreCase);
        foreach (Match match in bracketMatches)
        {
            var bracketText = match.Value;
            var innerText = bracketText[1..^1].Trim();

            if (string.IsNullOrEmpty(innerText))
                return GetLoc("MacroOptimization-Validate-SuffixEmpty", bracketText);

            if (innerText.StartsWith("wait", StringComparison.OrdinalIgnoreCase)) // <wait.1> 或 <wait.1.5>
            {
                if (!Regex.IsMatch(bracketText, @"<[Ww]ait\.\d+(?:\.\d+)?>"))
                    return GetLoc("MacroOptimization-Validate-WaitSyntaxError", bracketText);
                continue;
            }

            if (innerText.StartsWith("meandmemberstatus:", StringComparison.OrdinalIgnoreCase) || // <meandmemberstatus:123> / <memberstatus:123> / <enemystatus:123>
                innerText.StartsWith("memberstatus:", StringComparison.OrdinalIgnoreCase) ||
                innerText.StartsWith("enemystatus:", StringComparison.OrdinalIgnoreCase))
            {
                var colonIndex = innerText.IndexOf(':');
                var statusIDPart = innerText[(colonIndex + 1)..];
                if (string.IsNullOrWhiteSpace(statusIDPart) || !uint.TryParse(statusIDPart, out _))
                    return GetLoc("MacroOptimization-Validate-StatusIdFormatError", bracketText);
                continue;
            }

            if (innerText.Length > 0 && char.IsDigit(innerText[0])) // <1> 到 <8> 队伍编号
            {
                if (!uint.TryParse(innerText, out var num) || num < 1 || num > 8)
                    return GetLoc("MacroOptimization-Validate-PartyNumberRange", bracketText);
                continue;
            }

            var normalizedText = Regex.Replace(innerText, @"\s+", " ").Trim();

            if (Regex.IsMatch(normalizedText, @"^(party|enemy)\s*\.", RegexOptions.IgnoreCase)) // <party.xxx> / <enemy.xxx> 智能目标格式
            {
                var smartText = Regex.Replace(normalizedText, @"(?:^|[\s,;|])order\s*=\s*(toTop|toBottom)(?:$|[\s,;|])", " ", RegexOptions.IgnoreCase);
                smartText = Regex.Replace(smartText, @"(?:^|[\s,;|])toTop(?:$|[\s,;|])", " ", RegexOptions.IgnoreCase);
                smartText = Regex.Replace(smartText, @"(?:^|[\s,;|])toBottom(?:$|[\s,;|])", " ", RegexOptions.IgnoreCase);
                smartText = Regex.Replace(smartText, @"\s+", " ").Trim();

                smartText = Regex.Replace(smartText, @"\s*\.\s*", ".", RegexOptions.IgnoreCase);
                smartText = Regex.Replace(smartText, @"\s*!=\s*", "!=", RegexOptions.IgnoreCase);
                smartText = Regex.Replace(smartText, @"\s*=\s*", "=", RegexOptions.IgnoreCase);
                smartText = Regex.Replace(smartText, @"\s*:\s*", ":", RegexOptions.IgnoreCase);

                if (Regex.IsMatch(smartText, @"^party\.(minHp|maxHp)$", RegexOptions.IgnoreCase)) // party.minHp / party.maxHp
                    continue;

                if (Regex.Match(smartText, @"^party\.(\d)$", RegexOptions.IgnoreCase) is { Success: true } partyNumMatch) // party.1 到 party.8
                {
                    var num = int.Parse(partyNumMatch.Groups[1].Value);
                    if (num < 1 || num > 8)
                        return GetLoc("MacroOptimization-Validate-PartyNumberRange", bracketText);
                    continue;
                }

                if (Regex.IsMatch(smartText, @"^party\.job=.+$", RegexOptions.IgnoreCase))
                    continue;

                if (Regex.IsMatch(smartText, @"^party\.role=.+$", RegexOptions.IgnoreCase))
                    continue;

                if (Regex.IsMatch(smartText, @"^party\.buff(!=|=).+$", RegexOptions.IgnoreCase))
                    continue;

                if (Regex.IsMatch(smartText, @"^party\.(lowHp|lowHpOrMe|dead|near|far|dispellable|dispellableOrMe)$", RegexOptions.IgnoreCase))
                    continue;

                if (Regex.IsMatch(smartText, @"^party\.status(OrMe)?:\d+$", RegexOptions.IgnoreCase))
                    continue;

                if (Regex.IsMatch(smartText, @"^enemy\.(near|far|lowHp)$", RegexOptions.IgnoreCase))
                    continue;

                if (Regex.IsMatch(smartText, @"^enemy\.status:\d+$", RegexOptions.IgnoreCase))
                    continue;

                return GetLoc("MacroOptimization-Validate-UnknownSmartTargetFormat", bracketText);
            }

            if (!SupportedSuffixes.Contains(innerText))
                return GetLoc("MacroOptimization-Validate-UnknownSuffix", bracketText);
        }

        return string.Empty;
    }

}
}
