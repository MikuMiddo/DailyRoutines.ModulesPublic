using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Keys;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Component.GUI;
using InteropGenerator.Runtime;
using KamiToolKit.Classes;
using KamiToolKit.Nodes;
using KamiToolKit.Extensions;
using Lumina.Text.ReadOnly;

namespace DailyRoutines.ModulesPublic;

internal sealed unsafe class TextMultiLineInputNodeScrollable : ResNode
{
    private readonly ResNode clipNode;
    private readonly AutoHeightTextInputNode inputNode;

    private int scrollY;
    private bool stickToBottom = true;

    private bool baseOffsetsCaptured;
    private float baseInputY;
    private float baseCollisionY;

    public TextMultiLineInputNodeScrollable()
    {
        IsVisible = true;

        clipNode = new ResNode
        {
            IsVisible = true,
            NodeFlags = NodeFlags.Clip | NodeFlags.Visible | NodeFlags.Enabled,
        };
        clipNode.AttachNode(this);

        inputNode = new AutoHeightTextInputNode
        {
            IsVisible = true,
            AutoUpdateHeight = true,
        };
        inputNode.HeightChanged += _ => OnContentLayoutChanged();
        inputNode.OnInputReceived += _ => OnContentLayoutChanged();
        inputNode.AttachNode(clipNode);

        inputNode.CollisionNode.AddEvent(AtkEventType.MouseWheel, OnMouseWheel);

        CaptureBaseOffsets();
    }

    public uint MaxLines
    {
        get => inputNode.MaxLines;
        set => inputNode.MaxLines = value;
    }

    public uint MaxBytes
    {
        get => inputNode.MaxBytes;
        set => inputNode.MaxBytes = value;
    }

    public string String
    {
        get => inputNode.String;
        set
        {
            inputNode.String = value;
            OnContentLayoutChanged();
        }
    }

    public ReadOnlySeString SeString
    {
        get => inputNode.SeString;
        set
        {
            inputNode.SeString = value;
            OnContentLayoutChanged();
        }
    }

    public Action<ReadOnlySeString>? OnInputReceived
    {
        get => inputNode.OnInputReceived;
        set => inputNode.OnInputReceived = value;
    }

    public Action<ReadOnlySeString>? OnInputComplete
    {
        get => inputNode.OnInputComplete;
        set => inputNode.OnInputComplete = value;
    }

    public bool IsFocused => inputNode.IsFocused;

    public void ClearFocus() => inputNode.ClearFocus();

    protected override void OnSizeChanged()
    {
        base.OnSizeChanged();

        clipNode.Position = Vector2.Zero;
        clipNode.Size = Size;

        inputNode.X = 0;
        inputNode.Y = 0;
        inputNode.Width = Width;
        inputNode.MinHeight = Height;

        if (inputNode.Height < Height)
            inputNode.Height = Height;

        baseOffsetsCaptured = false;
        CaptureBaseOffsets();
        OnContentLayoutChanged();
    }

    private void CaptureBaseOffsets()
    {
        if (baseOffsetsCaptured)
            return;

        baseInputY = inputNode.Y;
        baseCollisionY = inputNode.CollisionNode.Y;
        baseOffsetsCaptured = true;
    }

    private void OnMouseWheel(AtkEventListener* thisPtr, AtkEventType eventType, int eventParam, AtkEvent* atkEvent, AtkEventData* atkEventData)
    {
        var max = GetMaxScroll();
        if (max <= 0)
            return;

        var step = Math.Max(1, (int)inputNode.CurrentTextNode.LineSpacing) * 3;
        var old = scrollY;

        if (atkEventData->IsScrollUp)
            scrollY = Math.Max(0, scrollY - step);
        else if (atkEventData->IsScrollDown)
            scrollY = Math.Min(max, scrollY + step);

        if (old != scrollY)
        {
            stickToBottom = scrollY >= max - 2;
            ApplyScroll();
            atkEvent->SetEventIsHandled();
        }
    }

    private void OnContentLayoutChanged()
    {
        var max = GetMaxScroll();

        if (stickToBottom)
            scrollY = max;
        else
            EnsureCursorVisible();

        ApplyScroll();
    }

    private void EnsureCursorVisible()
    {
        var max = GetMaxScroll();
        if (max <= 0)
            return;

        var cursorPos = inputNode.CursorPos;
        var text = inputNode.String;
        var clampedPos = Math.Clamp((int)cursorPos, 0, Math.Min(text.Length, ushort.MaxValue));

        var lineIndex = 0;
        for (var i = 0; i < clampedPos; i++)
        {
            if (text[i] == '\r')
                lineIndex++;
        }

        var lineHeight = Math.Max(1, (int)inputNode.CurrentTextNode.LineSpacing);
        var cursorTop = lineIndex * lineHeight;
        var cursorBottom = cursorTop + lineHeight + 20; // 预留一点缓冲

        if (cursorTop < scrollY)
            scrollY = Math.Max(0, cursorTop);
        else if (cursorBottom > scrollY + Height)
            scrollY = Math.Min(max, cursorBottom - (int)Height);

        stickToBottom = scrollY >= max - 2;
    }

    private int GetMaxScroll()
    {
        var max = (int)MathF.Round(inputNode.Height - Height);
        return Math.Max(0, max);
    }

    private void ApplyScroll()
    {
        var max = GetMaxScroll();
        scrollY = Math.Clamp(scrollY, 0, max);

        CaptureBaseOffsets();
        inputNode.Y = baseInputY - scrollY;
        inputNode.CollisionNode.Y = baseCollisionY + scrollY;
    }

    private sealed unsafe class AutoHeightTextInputNode : TextInputNode
    {
        private bool isProgrammaticTextSet;
        private bool enterKeyHandled;
        private bool autoUpdateHeight = true;
        private float minHeight;

        private delegate InputCallbackResult TextInputCallback(AtkUnitBase* addon, InputCallbackType type, CStringPointer rawString, CStringPointer evaluatedString, int eventKind);
        private TextInputCallback? callbackOverride;

        private AtkComponentTextInput* TextComponent => (AtkComponentTextInput*)ComponentBase;

        public event Action<float>? HeightChanged;

        public AutoHeightTextInputNode()
        {
            TextLimitsNode.AlignmentType = AlignmentType.BottomRight;
            AutoSelectAll = false;

            CurrentTextNode.TextFlags |= TextFlags.MultiLine;
            CurrentTextNode.LineSpacing = 14;

            Flags |= TextInputFlags.MultiLine;

            CollisionNode.AddEvent(AtkEventType.InputReceived, InputComplete);

            TextComponent->InputSanitizationFlags = AllowedEntities.UppercaseLetters |
                                                    AllowedEntities.LowercaseLetters |
                                                    AllowedEntities.Numbers |
                                                    AllowedEntities.SpecialCharacters |
                                                    AllowedEntities.CharacterList |
                                                    AllowedEntities.OtherCharacters |
                                                    AllowedEntities.Payloads |
                                                    AllowedEntities.Unknown9;

            TextComponent->ComponentTextData.Flags2 = TextInputFlags2.MultiLine |
                                                      TextInputFlags2.AllowSymbolInput |
                                                      TextInputFlags2.AllowNumberInput;

            TextComponent->ComponentTextData.MaxLine = byte.MaxValue;
            TextComponent->ComponentTextData.MaxByte = ushort.MaxValue;

            callbackOverride = CustomCallback;
            ((AtkComponentInputBase*)TextComponent)->Callback =
                (delegate* unmanaged<AtkUnitBase*, InputCallbackType, CStringPointer, CStringPointer, int, InputCallbackResult>)
                Marshal.GetFunctionPointerForDelegate(callbackOverride);
        }

        public bool AutoUpdateHeight
        {
            get => autoUpdateHeight;
            set
            {
                autoUpdateHeight = value;
                if (value)
                    UpdateHeightForContent();
            }
        }

        public float MinHeight
        {
            get => minHeight;
            set
            {
                minHeight = Math.Max(0, value);
                if (AutoUpdateHeight)
                    UpdateHeightForContent();
            }
        }

        public uint MaxLines
        {
            get => TextComponent->ComponentTextData.MaxLine;
            set => TextComponent->ComponentTextData.MaxLine = value;
        }

        public uint MaxBytes
        {
            get => TextComponent->ComponentTextData.MaxByte;
            set => TextComponent->ComponentTextData.MaxByte = value;
        }

        public override string String
        {
            get => base.String;
            set
            {
                isProgrammaticTextSet = true;
                base.String = value;
                isProgrammaticTextSet = false;
                UpdateHeightForContent();
            }
        }

        public override ReadOnlySeString SeString
        {
            get => base.SeString;
            set
            {
                isProgrammaticTextSet = true;
                base.SeString = value;
                isProgrammaticTextSet = false;
                UpdateHeightForContent();
            }
        }

        public override Action<ReadOnlySeString>? OnInputReceived
        {
            get => base.OnInputReceived;
            set
            {
                base.OnInputReceived = _ =>
                {
                    if (isProgrammaticTextSet)
                        return;

                    if (AutoUpdateHeight)
                        UpdateHeightForContent();
                };

                base.OnInputReceived += value;
            }
        }

        internal int CursorPos => TextComponent->CursorPos;

        private void UpdateHeightForContent()
        {
            if (!AutoUpdateHeight)
                return;

            var text = String;
            var lineCount = CountLines(text);
            var lineHeight = Math.Max(1, (int)CurrentTextNode.LineSpacing);
            var contentHeight = Math.Max(MinHeight, lineCount * lineHeight + 20);

            var oldHeight = Height;
            Height = contentHeight;

            if (Math.Abs(contentHeight - oldHeight) > 0.1f)
                HeightChanged?.Invoke(Height);
        }

        private static int CountLines(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 1;

            var lines = 1;
            foreach (var c in text)
            {
                if (c == '\r')
                    lines++;
            }

            return lines;
        }

        private void InputComplete()
        {
            var enterDown = DService.KeyState[VirtualKey.RETURN];
            if (!enterDown)
                enterKeyHandled = false;

            if (enterDown && !enterKeyHandled)
            {
                var cursorPos = TextComponent->CursorPos;

                enterKeyHandled = true;
                using var utf8String = new Utf8String();
                utf8String.SetString("\r");
                TextComponent->WriteString(&utf8String);

                var nextCursorPos = cursorPos + 1;
                TextComponent->CursorPos = nextCursorPos;
                TextComponent->SelectionStart = nextCursorPos;
                TextComponent->SelectionEnd = nextCursorPos;
            }
        }

        private InputCallbackResult CustomCallback(AtkUnitBase* addon, InputCallbackType type, CStringPointer rawString, CStringPointer evaluatedString, int eventKind)
        {
            switch (type)
            {
                case InputCallbackType.Enter:
                    OnInputComplete?.Invoke(TextComponent->EvaluatedString.AsSpan());
                    break;
                case InputCallbackType.TextChanged:
                    OnInputReceived?.Invoke(TextComponent->EvaluatedString.AsSpan());
                    break;
                case InputCallbackType.Escape:
                    OnEscapeEntered?.Invoke();
                    break;
                case InputCallbackType.FocusLost:
                    OnFocusLost?.Invoke();
                    break;
                case InputCallbackType.Tab:
                    OnTabEntered?.Invoke();
                    break;
            }

            return InputCallbackResult.None;
        }
    }
}
