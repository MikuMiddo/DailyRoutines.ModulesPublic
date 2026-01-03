using DailyRoutines.Abstracts;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Nodes;

namespace DailyRoutines.ModulesPublic;

public unsafe partial class MacroOptimization
{
    internal sealed class DRMacroSettings(DailyModuleBase instance) : NativeAddon
    {
        private readonly DailyModuleBase Instance = instance;

        private CheckboxNode?     LoopCheckbox;
        private NumericInputNode? LoopCountInputNode;
        private NumericInputNode? IntervalInputNode;
        private NumericInputNode? CompletionDelayInputNode;
        private TextNode?         LoopHintNode;
        private TextNode?         LoopCountLabel;
        private TextNode?         IntervalLabel;
        private TextNode?         CompletionDelayLabel;

        internal int CurrentMacroIndex = -1;

        protected override void OnSetup(AtkUnitBase* addon)
        {
            var initialIsLoopEnabled = false;
            var initialLoopCount = 1;
            var initialInterval = 2500;
            if (CurrentMacroIndex >= 0 && CurrentMacroIndex < ModuleConfig.ExtendMacroLists.Count)
            {
                var macro = ModuleConfig.ExtendMacroLists[CurrentMacroIndex];
                initialIsLoopEnabled = macro.IsLoopEnabled;
                initialLoopCount = macro.LoopCount;
                initialInterval = macro.DefaultInterval;
            }

            var mainContainerNode = new ResNode
            {
                Position = ContentStartPosition,
                Size = ContentSize,
                IsVisible = true,
            };
            mainContainerNode.AttachNode(this);

            var titleNode = new TextNode
            {
                IsVisible = true,
                Size = new(280, 20),
                Position = new(10f, 0f),
                SeString = GetLoc("MacroOptimization-Settings-LoopTitle"),
                FontSize = 16,
                AlignmentType = AlignmentType.Left,
                TextColor = KnownColor.White.Vector(),
            };
            titleNode.AttachNode(mainContainerNode);

            LoopCheckbox = new CheckboxNode
            {
                IsVisible = true,
                Size = new(20, 20),
                Position = new(10f, 30f),
                SeString = GetLoc("MacroOptimization-Settings-EnableLoop"),
                IsChecked = initialIsLoopEnabled,
                OnClick = (isChecked) =>
                {
                    if (CurrentMacroIndex < 0 || CurrentMacroIndex >= ModuleConfig.ExtendMacroLists.Count) return;
                    if (LoopCountInputNode == null) return;

                    ModuleConfig.ExtendMacroLists[CurrentMacroIndex].IsLoopEnabled = isChecked;
                    if (LoopCountInputNode.AddButton != null)
                        LoopCountInputNode.AddButton.IsEnabled = isChecked;
                    if (LoopCountInputNode.SubtractButton != null)
                        LoopCountInputNode.SubtractButton.IsEnabled = isChecked;

                    var alpha = isChecked ? 1.0f : 0.5f;
                    if (LoopCountLabel != null)
                        LoopCountLabel.Alpha = alpha;
                    if (LoopCountInputNode != null)
                        LoopCountInputNode.Alpha = alpha;
                    if (LoopHintNode != null)
                        LoopHintNode.Alpha = alpha;

                    ModuleConfig.Save(Instance);
                }
            };
            LoopCheckbox.AttachNode(mainContainerNode);

            LoopCountLabel = new TextNode
            {
                IsVisible = true,
                Size = new(80, 20),
                Position = new(20f, 50f),
                SeString = GetLoc("MacroOptimization-Settings-LoopCount"),
                FontSize = 14,
                AlignmentType = AlignmentType.Left,
                Alpha = initialIsLoopEnabled ? 1.0f : 0.5f,
            };
            LoopCountLabel.AttachNode(mainContainerNode);

            LoopCountInputNode = new NumericInputNode
            {
                IsVisible = true,
                Size = new(120f, 24f),
                Position = new(160f, 47f),
                Value = initialLoopCount,
                Min = 0,
                Max = 9999,
                Step = 1,
                Alpha = initialIsLoopEnabled ? 1.0f : 0.5f,
                OnValueUpdate = (value) =>
                {
                    if (CurrentMacroIndex >= 0 && CurrentMacroIndex < ModuleConfig.ExtendMacroLists.Count)
                    {
                        ModuleConfig.ExtendMacroLists[CurrentMacroIndex].LoopCount = value;
                        ModuleConfig.Save(Instance);
                    }
                }
            };

            if (LoopCountInputNode.AddButton != null)
                LoopCountInputNode.AddButton.IsEnabled = initialIsLoopEnabled;
            if (LoopCountInputNode.SubtractButton != null)
                LoopCountInputNode.SubtractButton.IsEnabled = initialIsLoopEnabled;
            unsafe
            {
                AtkResNode* collisionNode = LoopCountInputNode.CollisionNode;
                if (initialIsLoopEnabled)
                    collisionNode->NodeFlags |= NodeFlags.RespondToMouse | NodeFlags.Focusable;
                else
                    collisionNode->NodeFlags &= ~(NodeFlags.RespondToMouse | NodeFlags.Focusable);
            }
            LoopCountInputNode.AttachNode(mainContainerNode);

            LoopHintNode = new TextNode
            {
                IsVisible = true,
                Size = new(200f, 20f),
                Position = new(20f, 70f),
                SeString = GetLoc("MacroOptimization-Settings-LoopHint"),
                FontSize = 12,
                AlignmentType = AlignmentType.Left,
                TextColor = KnownColor.Gray.Vector(),
                Alpha = initialIsLoopEnabled ? 1.0f : 0.5f,
            };
            LoopHintNode.AttachNode(mainContainerNode);

            var intervalTitleNode = new TextNode
            {
                IsVisible = true,
                Size = new(280, 20),
                Position = new(10f, 100f),
                SeString = GetLoc("MacroOptimization-Settings-IntervalTitle"),
                FontSize = 16,
                AlignmentType = AlignmentType.Left,
                TextColor = KnownColor.White.Vector(),
            };
            intervalTitleNode.AttachNode(mainContainerNode);

            var viewCooldownButton = new TextureButtonNode
            {
                IsVisible = true,
                Size = new(20, 20),
                Position = new(110f, 100f),
                TexturePath = "ui/uld/CircleButtons_hr1.tex",
                TextureCoordinates = new(0, 28),
                TextureSize = new(28, 28),
                TextTooltip = GetLoc("MacroOptimization-Settings-ViewCooldownTooltip"),
                OnClick = () => MacroCooldownViewerAddon?.Toggle()
            };
            viewCooldownButton.AttachNode(mainContainerNode);

            IntervalLabel = new TextNode
            {
                IsVisible = true,
                Size = new(120, 20),
                Position = new(20f, 120f),
                SeString = GetLoc("MacroOptimization-Settings-IntervalMs"),
                FontSize = 14,
                AlignmentType = AlignmentType.Left,
            };
            IntervalLabel.AttachNode(mainContainerNode);

            IntervalInputNode = new NumericInputNode
            {
                IsVisible = true,
                Size = new(120f, 24f),
                Position = new(160f, 117f),
                Value = initialInterval,
                Min = 500,
                Max = 10000,
                Step = 100,
                OnValueUpdate = (value) =>
                {
                    if (CurrentMacroIndex >= 0 && CurrentMacroIndex < ModuleConfig.ExtendMacroLists.Count)
                    {
                        ModuleConfig.ExtendMacroLists[CurrentMacroIndex].DefaultInterval = value;
                        ModuleConfig.Save(Instance);
                    }
                }
            };
            IntervalInputNode.AttachNode(mainContainerNode);

            var intervalHintNode = new TextNode
            {
                IsVisible = true,
                Size = new(260f, 32f),
                Position = new(20f, 140f),
                SeString = GetLoc("MacroOptimization-Settings-IntervalHint"),
                FontSize = 12,
                AlignmentType = AlignmentType.Left,
                TextFlags = TextFlags.MultiLine,
                LineSpacing = 14,
                TextColor = KnownColor.Gray.Vector(),
            };
            intervalHintNode.AttachNode(mainContainerNode);

            CompletionDelayLabel = new TextNode
            {
                IsVisible = true,
                Size = new(160f, 24f),
                Position = new(20f, 185f),
                SeString = GetLoc("MacroOptimization-Settings-CompletionDelayMs"),
                FontSize = 14,
                AlignmentType = AlignmentType.Left
            };
            CompletionDelayLabel.AttachNode(mainContainerNode);

            var initialCompletionDelay = CurrentMacroIndex >= 0 && CurrentMacroIndex < ModuleConfig.ExtendMacroLists.Count
                ? ModuleConfig.ExtendMacroLists[CurrentMacroIndex].CompletionDelay
                : 1000;

            CompletionDelayInputNode = new NumericInputNode
            {
                IsVisible = true,
                Size = new(120f, 24f),
                Position = new(160f, 182f),
                Value = initialCompletionDelay,
                Min = 0,
                Max = 10000,
                Step = 100,
                OnValueUpdate = (value) =>
                {
                    if (CurrentMacroIndex >= 0 && CurrentMacroIndex < ModuleConfig.ExtendMacroLists.Count)
                    {
                        ModuleConfig.ExtendMacroLists[CurrentMacroIndex].CompletionDelay = value;
                        ModuleConfig.Save(Instance);
                    }
                }
            };
            CompletionDelayInputNode.AttachNode(mainContainerNode);

            var completionDelayHintNode = new TextNode
            {
                IsVisible = true,
                Size = new(260f, 32f),
                Position = new(20f, 205f),
                SeString = GetLoc("MacroOptimization-Settings-CompletionDelayHint"),
                FontSize = 12,
                AlignmentType = AlignmentType.Left,
                TextFlags = TextFlags.MultiLine,
                LineSpacing = 14,
                TextColor = KnownColor.Gray.Vector(),
            };
            completionDelayHintNode.AttachNode(mainContainerNode);
        }

        public void OpenWithMacroIndex(int macroIndex)
        {
            if (macroIndex < 0 || macroIndex >= ModuleConfig.ExtendMacroLists.Count) return;

            CurrentMacroIndex = macroIndex;
            Toggle();
        }

        internal void UpdateDisplay()
        {
            if (CurrentMacroIndex < 0 || CurrentMacroIndex >= ModuleConfig.ExtendMacroLists.Count) return;
            if (LoopCheckbox == null || LoopCountInputNode == null || IntervalInputNode == null || CompletionDelayInputNode == null) return;

            var currentMacro = ModuleConfig.ExtendMacroLists[CurrentMacroIndex];

            LoopCheckbox.IsChecked = currentMacro.IsLoopEnabled;
            LoopCountInputNode.Value = currentMacro.LoopCount;
            IntervalInputNode.Value = currentMacro.DefaultInterval;
            CompletionDelayInputNode.Value = currentMacro.CompletionDelay;
            if (LoopCountInputNode.AddButton != null)
                LoopCountInputNode.AddButton.IsEnabled = currentMacro.IsLoopEnabled;
            if (LoopCountInputNode.SubtractButton != null)
                LoopCountInputNode.SubtractButton.IsEnabled = currentMacro.IsLoopEnabled;

            unsafe
            {
                AtkResNode* collisionNode = LoopCountInputNode.CollisionNode;
                if (currentMacro.IsLoopEnabled)
                    collisionNode->NodeFlags |= NodeFlags.RespondToMouse | NodeFlags.Focusable;
                else
                    collisionNode->NodeFlags &= ~(NodeFlags.RespondToMouse | NodeFlags.Focusable);
            }
        }

        protected override void OnFinalize(AtkUnitBase* addon)
        {
            LoopCheckbox = null;
            LoopCountInputNode = null;
            LoopHintNode = null;
            base.OnFinalize(addon);
        }
    }
}
