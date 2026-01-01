using System.Numerics;
using Dalamud.Game.Addon.Events;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;

namespace DailyRoutines.ModulesPublic;

internal sealed class ExtenMacroButtonNode : SimpleComponentNode
{
    private readonly NineGridNode hoveredBackgroundNode;
    private readonly NineGridNode selectedBackgroundNode;
    private readonly IconNode IconNode;
    private readonly TextNode NameNode;
    private readonly TextNode DescriptionNode;

    public ExtenMacroButtonNode(ExtendMacro Macro)
    {
        hoveredBackgroundNode = new SimpleNineGridNode
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
        hoveredBackgroundNode.AttachNode(this);

        selectedBackgroundNode = new SimpleNineGridNode
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
        selectedBackgroundNode.AttachNode(this);

        IconNode = new IconNode
        {
            IsVisible = true,
            IconId = Macro.IconID,
            Size = new(32),
        };
        IconNode.AttachNode(this);

        NameNode = new TextNode
        {
            Size = new(180.0f, 20.0f),
            IsVisible = true,
            SeString = Macro.Name,
            FontSize = 16,
            TextFlags = TextFlags.Ellipsis
        };
        NameNode.AttachNode(this);

        DescriptionNode = new TextNode
        {
            Size = new(180.0f, 20.0f),
            IsVisible = true,
            SeString = Macro.Description,
            FontSize = 16,
            TextFlags = TextFlags.Ellipsis
        };
        DescriptionNode.AttachNode(this);

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

    public System.Action? OnClick { get; set; }

    public bool IsHovered
    {
        get => hoveredBackgroundNode.IsVisible;
        set => hoveredBackgroundNode.IsVisible = value;
    }

    public bool IsSelected
    {
        get => selectedBackgroundNode.IsVisible;
        set
        {
            selectedBackgroundNode.IsVisible = value;
            if (value)
                hoveredBackgroundNode.IsVisible = false;
        }
    }

    public void UpdateDisplay(uint? newIconID = null, string? newName = null, string? newDescription = null)
    {
        if (newIconID.HasValue)
            IconNode.IconId = newIconID.Value;

        if (newName != null)
            NameNode.SeString = newName;

        if (newDescription != null)
            DescriptionNode.SeString = newDescription;
    }

    protected override void OnSizeChanged()
    {
        base.OnSizeChanged();
        CollisionNode.Size = Size;
        hoveredBackgroundNode.Size = Size;
        selectedBackgroundNode.Size = Size;

        IconNode.Size = new Vector2(Height, Height) * 0.75f;
        IconNode.Position = new Vector2(Height, Height) * 0.125f;

        NameNode.Height = Height / 2.0f;
        NameNode.Position = new Vector2(Height, Height * 0.15f);

        DescriptionNode.Height = Height / 2.0f;
        DescriptionNode.Position = new Vector2(Height, Height * 0.55f);
    }
}
