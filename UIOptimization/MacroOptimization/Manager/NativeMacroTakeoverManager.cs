using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using Lumina.Text.ReadOnly;

namespace DailyRoutines.ModulesPublic;

public unsafe partial class MacroOptimization
{
    internal static unsafe class NativeMacroTakeoverManager
    {
        private static readonly CompSig                    ExecuteMacroSig = new("48 89 5C 24 ?? 41 56 48 83 EC ?? 80 B9 ?? ?? ?? ?? ?? 4C 8B F2");
        private delegate        ulong                      ExecuteMacroDelegate(RaptureShellModule* raptureShellModule, RaptureMacroModule.Macro* macro);
        private static          Hook<ExecuteMacroDelegate>? ExecuteMacroHook;

        public static void Enable()
        {
            if (ExecuteMacroHook != null)
                return;

            ChatManager.RegPreExecuteCommandInner(OnPreExecuteCommandInner);

            ExecuteMacroHook = ExecuteMacroSig.GetHook<ExecuteMacroDelegate>(ExecuteMacroDetour);
            ExecuteMacroHook.Enable();
        }

        public static void Disable()
        {
            ChatManager.Unreg(OnPreExecuteCommandInner);

            ExecuteMacroHook?.Disable();
            ExecuteMacroHook?.Dispose();
            ExecuteMacroHook = null;
        }

        private static ulong ExecuteMacroDetour(RaptureShellModule* raptureShellModule, RaptureMacroModule.Macro* macro)
        {
            if (macro == null)
                return ExecuteMacroHook!.Original(raptureShellModule, macro);

            var tempMacro = *(MacroLayout*)macro;
            List<nint>? allocated = null;

            try
            {
                for (var i = 0; i < 15; i++)
                {
                    var line = GetMacroLine(ref tempMacro, i).ToString();
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    if (!TryHandleIfCommand(line, out var replacement))
                        continue;

                    var replacementText = replacement ?? string.Empty;
                    var replacementUtf8 = Utf8String.FromString(replacementText);
                    allocated ??= [];
                    allocated.Add((nint)replacementUtf8);
                    GetMacroLine(ref tempMacro, i) = *replacementUtf8;
                }

                return ExecuteMacroHook!.Original(raptureShellModule, (RaptureMacroModule.Macro*)&tempMacro);
            }
            finally
            {
                if (allocated != null)
                {
                    foreach (var ptr in allocated)
                        ((Utf8String*)ptr)->Dtor(true);
                }
            }
        }

        private static void OnPreExecuteCommandInner(ref bool isPrevented, ref ReadOnlySeString message)
        {
            var text = message.ToString();
            if (string.IsNullOrWhiteSpace(text))
                return;

            if (!TryHandleIfCommand(text, out var replacement))
                return;

            if (replacement == null)
            {
                isPrevented = true;
                return;
            }

            message = new(replacement);
        }

        private static ref Utf8String GetMacroLine(ref MacroLayout macro, int index)
        {
            if ((uint)index >= 15)
                throw new ArgumentOutOfRangeException(nameof(index));

            fixed (Utf8String* lines = &macro.Lines.Item0)
            {
                return ref lines[index];
            }
        }

        private static bool TryHandleIfCommand(string text, out string? replacement)
        {
            replacement = null;

            if (!text.StartsWith("/if", StringComparison.OrdinalIgnoreCase))
                return false;

            var current = text;
            for (var depth = 0; depth < 5; depth++)
            {
                var match = Regex.Match(current, @"^/if\s+\[(.+?)\]\s+(/.+)$", RegexOptions.IgnoreCase);
                if (!match.Success)
                    return false;

                var inner = match.Groups[2].Value.Trim();

                if (!MacroExecutor.EvaluateCondition(current))
                    return true;

                if (!inner.StartsWith("/if", StringComparison.OrdinalIgnoreCase))
                {
                    replacement = inner;
                    return true;
                }

                current = inner;
            }

            replacement = null;
            return true;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MacroLayout
        {
            public uint         IconID;
            public uint         MacroIconRowID;
            public Utf8String   Name;
            public MacroLines15 Lines;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MacroLines15
        {
            public Utf8String Item0;
            public Utf8String Item1;
            public Utf8String Item2;
            public Utf8String Item3;
            public Utf8String Item4;
            public Utf8String Item5;
            public Utf8String Item6;
            public Utf8String Item7;
            public Utf8String Item8;
            public Utf8String Item9;
            public Utf8String Item10;
            public Utf8String Item11;
            public Utf8String Item12;
            public Utf8String Item13;
            public Utf8String Item14;
        }

    }
}
