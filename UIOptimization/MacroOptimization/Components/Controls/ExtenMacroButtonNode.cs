using System;
using System.Numerics;
using Dalamud.Game.Addon.Events;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;

namespace DailyRoutines.ModulesPublic;

internal sealed class ExtenMacroButtonNode : SimpleComponentNode
{
    private readonly NineGridNode HoveredBackgroundNode;
    private readonly NineGridNode SelectedBackgroundNode;
    private readonly IconNode     Icon;
    private readonly TextNode     Name;
    private readonly TextNode     Description;

    public ExtenMacroButtonNode(ExtendMacro macro)
    {
        HoveredBackgroundNode = new SimpleNineGridNode
        {
            NodeId = 2,
            TexturePath = "ui/uld/ListItemA.tex",
            TextureCoordinates = new Vector2(0.0f, 22.0f),
            TextureSize = new Vector2(64.0f, 22.0f),
            TopOffset = 6,
            BottomOffset = 6,
            LeftOffset = 16,
            RightOffset = 1,
            IsVisible = false,
        };
        HoveredBackgroundNode.AttachNode(this);

        SelectedBackgroundNode = new SimpleNineGridNode
        {
            NodeId = 3,
            TexturePath = "ui/uld/ListItemA.tex",
            TextureCoordinates = new Vector2(0.0f, 0.0f),
            TextureSize = new Vector2(64.0f, 22.0f),
            TopOffset = 6,
            BottomOffset = 6,
            LeftOffset = 16,
            RightOffset = 1,
            IsVisible = false,
        };
        SelectedBackgroundNode.AttachNode(this);

        Icon = new IconNode
        {
            IsVisible = true,
            IconId = macro.IconID,
            Size = new(32),
        };
        Icon.AttachNode(this);

        Name = new TextNode
        {
            Size = new(180.0f, 20.0f),
            IsVisible = true,
            SeString = macro.Name,
            FontSize = 16,
            TextFlags = TextFlags.Ellipsis
        };
        Name.AttachNode(this);

        Description = new TextNode
        {
            Size = new(180.0f, 20.0f),
            IsVisible = true,
            SeString = macro.Description,
            FontSize = 16,
            TextFlags = TextFlags.Ellipsis
        };
        Description.AttachNode(this);

        AddEvent(AtkEventType.MouseOver, () =>
        {
            if (!IsSelected)
            {
                IsHovered = true;
                DService.AddonEvent.SetCursor(AddonCursorType.Clickable);
            }
        });

        AddEvent(AtkEventType.MouseDown, () =>
        {
            OnClick?.Invoke();
            if (IsSelected)
                DService.AddonEvent.ResetCursor();
        });

        AddEvent(AtkEventType.MouseOut, () =>
        {
            IsHovered = false;
            DService.AddonEvent.ResetCursor();
        });
    }

    public Action? OnClick { get; set; }

    public bool IsHovered
    {
        get => HoveredBackgroundNode.IsVisible;
        set => HoveredBackgroundNode.IsVisible = value;
    }

    public bool IsSelected
    {
        get => SelectedBackgroundNode.IsVisible;
        set
        {
            SelectedBackgroundNode.IsVisible = value;
            if (value)
                HoveredBackgroundNode.IsVisible = false;
        }
    }

    public void UpdateDisplay(uint? newIconID = null, string? newName = null, string? newDescription = null)
    {
        if (newIconID.HasValue)
            Icon.IconId = newIconID.Value;

        if (newName != null)
            Name.SeString = newName;

        if (newDescription != null)
            Description.SeString = newDescription;
    }

    protected override void OnSizeChanged()
    {
        base.OnSizeChanged();
        CollisionNode.Size = Size;
        HoveredBackgroundNode.Size = Size;
        SelectedBackgroundNode.Size = Size;

        Icon.Size = new Vector2(Height, Height) * 0.75f;
        Icon.Position = new Vector2(Height, Height) * 0.125f;

        Name.Height = Height / 2.0f;
        Name.Position = new Vector2(Height, Height * 0.15f);

        Description.Height = Height / 2.0f;
        Description.Position = new Vector2(Height, Height * 0.55f);
    }
}
