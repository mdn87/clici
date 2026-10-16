using System.Diagnostics;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Interop.UIAutomationClient;

namespace Clici.PromptNavigation.Probe;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private const string ProofTitlePrefix = "CLICI-PROOF-";

    [MTAThread]
    private static int Main(string[] args)
    {
        try
        {
            NativeWindows.UsePhysicalCoordinates();
            if (args.Length == 0)
            {
                Console.WriteLine("Commands: windows | snapshot <hwnd> [maxCharacters] | inspect|select|jump <hwnd> <syntheticMarker> | live <hwnd> | watch <hwnd> <seconds> | key <hwnd> enter|ctrl-up|ctrl-down|stream|finish|cancel | place <hwnd> <x> <y> <width> <height> | fixture clean|osc <eventFile>");
                return 0;
            }

            if (args[0] == "fixture")
            {
                if (args.Length != 3 || args[1] is not ("clean" or "osc"))
                {
                    throw new ArgumentException("Expected fixture clean|osc <eventFile>.");
                }

                RunFixture(args[1] == "osc", args[2]);
                return 0;
            }

            if (args[0] == "windows")
            {
                Write(NativeWindows.ListProofWindows(ProofTitlePrefix));
                return 0;
            }

            var hwnd = new nint(long.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture));
            var window = NativeWindows.RequireProofWindow(hwnd, ProofTitlePrefix);
            if (args[0] == "place")
            {
                NativeWindows.Place(hwnd, int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]), int.Parse(args[5]));
                Write(NativeWindows.RequireProofWindow(hwnd, ProofTitlePrefix));
                return 0;
            }

            if (args[0] == "key")
            {
                NativeWindows.SendProofKey(hwnd, args[2]);
                Write(new { result = "KeyDelivered", window, key = args[2] });
                return 0;
            }

            var automation = new CUIAutomation();
            if (args[0] == "watch")
            {
                var seconds = int.Parse(args[2]);
                if (seconds is < 1 or > 30)
                {
                    throw new ArgumentException("Focus observation must be between 1 and 30 seconds.");
                }

                var handler = new ProofFocusHandler(window.ProcessId);
                automation.AddFocusChangedEventHandler(null, handler);
                try
                {
                    Thread.Sleep(seconds * 1000);
                }
                finally
                {
                    automation.RemoveFocusChangedEventHandler(handler);
                }

                Write(new { result = "FocusObserved", window, seconds, events = handler.Events.ToArray() });
                return 0;
            }

            var root = automation.ElementFromHandle(hwnd);
            var condition = automation.CreateAndCondition(
                automation.CreatePropertyCondition(UIA_PropertyIds.UIA_IsTextPatternAvailablePropertyId, true),
                automation.CreatePropertyCondition(UIA_PropertyIds.UIA_ClassNamePropertyId, "TermControl"));
            var elements = root.FindAll(TreeScope.TreeScope_Subtree, condition);
            var terminals = Enumerable.Range(0, elements.Length).Select(elements.GetElement).ToArray();
            var focused = terminals.Where(candidate => candidate.CurrentHasKeyboardFocus != 0).ToArray();
            var element = focused.Length == 1 ? focused[0] : terminals.Length == 1 ? terminals[0] : null;
            if (element is null)
            {
                var controls = new List<object>();
                for (var i = 0; i < elements.Length && i < 20; i++)
                {
                    var candidate = elements.GetElement(i);
                    controls.Add(new
                    {
                        candidate.CurrentName,
                        candidate.CurrentControlType,
                        candidate.CurrentClassName,
                        candidate.CurrentAutomationId,
                        candidate.CurrentHasKeyboardFocus,
                        runtimeId = candidate.GetRuntimeId()
                    });
                }

                Write(new { result = "AmbiguousTextControl", window, textControls = elements.Length, controls });
                return 2;
            }

            var pattern = (IUIAutomationTextPattern)element.GetCurrentPattern(UIA_PatternIds.UIA_TextPatternId);
            if (args[0] == "activate")
            {
                element.SetFocus();
                Write(new { result = "FocusRequested", window, foregroundMatchesTarget = NativeWindows.IsForeground(hwnd) });
                return 0;
            }

            var limit = args.Length > 2 && args[0] == "snapshot" ? int.Parse(args[2]) : 32_768;
            if (limit < 1 || limit > 262_144)
            {
                throw new ArgumentOutOfRangeException(nameof(limit), "Capture must be between 1 and 262144 characters.");
            }

            var timer = Stopwatch.StartNew();
            var before = Visible(pattern, limit);
            if (args[0] is "jump" or "inspect" or "select")
            {
                if (args.Length != 3 || !args[2].StartsWith("CLICI_FIXTURE_", StringComparison.Ordinal))
                {
                    throw new ArgumentException("Only synthetic CLICI_FIXTURE_ markers are accepted by this development probe.");
                }

                var target = pattern.DocumentRange.FindText(args[2], 0, 0);
                if (target is null)
                {
                    Write(new { result = "MarkerNotFound", window, marker = args[2] });
                    return 2;
                }

                if (target.GetText(args[2].Length) != args[2])
                {
                    throw new InvalidOperationException("The provider did not return the exact requested marker; no scroll was started.");
                }

                if (args[0] == "inspect")
                {
                    Write(new
                    {
                        result = "AttributesObserved",
                        window,
                        marker = args[2],
                        occurrences = Inspect(pattern.DocumentRange, args[2])
                    });
                    return 0;
                }

                NativeWindows.RequireActiveProofPane(hwnd, element);
                if (args[0] == "select")
                {
                    target.Select();
                }
                else
                {
                    target.ScrollIntoView(1);
                }
            }
            else if (args[0] == "live")
            {
                var end = pattern.DocumentRange;
                end.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, end,
                    TextPatternRangeEndpoint.TextPatternRangeEndpoint_End);
                NativeWindows.RequireActiveProofPane(hwnd, element);
                end.ScrollIntoView(1);
            }
            else if (args[0] != "snapshot")
            {
                throw new ArgumentException("Unknown command.");
            }

            var document = pattern.DocumentRange;
            var tail = document.Clone();
            tail.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, tail,
                TextPatternRangeEndpoint.TextPatternRangeEndpoint_End);
            var moved = tail.MoveEndpointByUnit(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start,
                TextUnit.TextUnit_Character, -limit);
            var selection = pattern.GetSelection();
            var selected = new List<string>();
            for (var i = 0; i < selection.Length; i++)
            {
                selected.Add(selection.GetElement(i).GetText(limit));
            }

            Write(new
            {
                result = "Observed",
                command = args[0],
                window,
                client = "COM IUIAutomation / Interop.UIAutomationClient 10.19041.0",
                runtimeId = element.GetRuntimeId(),
                elementName = element.CurrentName,
                textControls = terminals.Length,
                panes = terminals.Select(candidate => new
                {
                    runtimeId = candidate.GetRuntimeId(),
                    focused = candidate.CurrentHasKeyboardFocus != 0,
                    rectangle = new[] { candidate.CurrentBoundingRectangle.left, candidate.CurrentBoundingRectangle.top,
                        candidate.CurrentBoundingRectangle.right, candidate.CurrentBoundingRectangle.bottom },
                    visible = Visible((IUIAutomationTextPattern)candidate.GetCurrentPattern(UIA_PatternIds.UIA_TextPatternId), limit)
                }).ToArray(),
                helpText = element.CurrentHelpText,
                focused = element.CurrentHasKeyboardFocus != 0,
                foregroundMatchesTarget = NativeWindows.IsForeground(hwnd),
                supportsTextPattern2 = element.GetCurrentPropertyValue(UIA_PropertyIds.UIA_IsTextPattern2AvailablePropertyId),
                before,
                after = Visible(pattern, limit),
                capturedTailCharacters = -moved,
                captureReachesDocumentStart = tail.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start,
                    document, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start) == 0,
                tail = tail.GetText(limit),
                selected,
                elapsedMilliseconds = timer.Elapsed.TotalMilliseconds
            });
            return 0;
        }
        catch (Exception exception)
        {
            Write(new { result = "Refused", exception = exception.GetType().Name, message = exception.Message, stack = exception.StackTrace });
            return 2;
        }
    }

    private static string[] Visible(IUIAutomationTextPattern pattern, int limit)
    {
        var ranges = pattern.GetVisibleRanges();
        var text = new List<string>();
        for (var i = 0; i < ranges.Length; i++)
        {
            text.Add(ranges.GetElement(i).GetText(limit));
        }

        return text.ToArray();
    }

    private static object[] Inspect(IUIAutomationTextRange document, string marker)
    {
        var remaining = document.Clone();
        var occurrences = new List<object>();
        // Confirm presence in bounded text before FindText. This provider can
        // return an unusable COM range instead of null after the final match.
        while (occurrences.Count < 20 && remaining.GetText(262_144).Contains(marker, StringComparison.Ordinal) &&
            remaining.FindText(marker, 0, 0) is { } target)
        {
            string lineText;
            try
            {
                var line = target.Clone();
                line.ExpandToEnclosingUnit(TextUnit.TextUnit_Line);
                lineText = line.GetText(512);
            }
            catch (Exception exception) when (exception is COMException or ArgumentException)
            {
                lineText = $"Line unavailable: 0x{exception.HResult:X8}";
            }

            var attributes = new Dictionary<string, object?>();
            foreach (var (name, id) in new[]
            {
                ("foreground", UIA_TextAttributeIds.UIA_ForegroundColorAttributeId),
                ("background", UIA_TextAttributeIds.UIA_BackgroundColorAttributeId),
                ("weight", UIA_TextAttributeIds.UIA_FontWeightAttributeId),
                ("readOnly", UIA_TextAttributeIds.UIA_IsReadOnlyAttributeId),
                ("style", UIA_TextAttributeIds.UIA_StyleIdAttributeId)
            })
            {
                try
                {
                    var value = target.GetAttributeValue(id);
                    attributes[name] = value is not null && Marshal.IsComObject(value) ? "MixedOrUnsupported" : value;
                }
                catch (Exception exception) when (exception is COMException or ArgumentException)
                {
                    attributes[name] = $"Unavailable: 0x{exception.HResult:X8}";
                }
            }

            occurrences.Add(new { line = lineText, attributes });
            try
            {
                remaining.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start,
                    target, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End);
            }
            catch (Exception exception) when (exception is COMException or ArgumentException)
            {
                occurrences.Add(new { error = "Cannot advance to the next occurrence", hresult = exception.HResult });
                break;
            }
        }

        return occurrences.ToArray();
    }

    private static void Write(object value) => Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    private static void RunFixture(bool osc, string eventFile)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (osc)
        {
            Console.Write("\x1b]133;A\x07PS CLICI_FIXTURE> \x1b]133;B\x07synthetic-agent\r\n\x1b]133;C\x07");
        }

        Console.WriteLine($"CLICI synthetic conversation; OSC command marks: {osc}; press Enter for a turn, S for 35 streaming rows, Q to finish.");
        var turn = 1;
        var controlLinesRead = 0;
        Console.Write($"> CLICI_FIXTURE_USER_{turn:000}");
        while (true)
        {
            var inputSource = "keyboard";
            ConsoleKeyInfo key;
            while (true)
            {
                if (Console.KeyAvailable)
                {
                    key = Console.ReadKey(true);
                    break;
                }

                var controlFile = eventFile + ".control";
                var commands = File.Exists(controlFile) ? File.ReadAllLines(controlFile) : [];
                if (commands.Length > controlLinesRead)
                {
                    var command = commands[controlLinesRead++];
                    if (command == "stream")
                    {
                        inputSource = "control-file";
                        key = new ConsoleKeyInfo('s', ConsoleKey.S, false, false, false);
                        break;
                    }
                }

                Thread.Sleep(20);
            }

            File.AppendAllText(eventFile, JsonSerializer.Serialize(new
            {
                utc = DateTimeOffset.UtcNow,
                source = inputSource,
                key = key.Key.ToString(),
                modifiers = key.Modifiers.ToString(),
                turn
            }) + Environment.NewLine);
            if (key.Key == ConsoleKey.Q)
            {
                break;
            }

            if (key.Key is ConsoleKey.Enter or ConsoleKey.S)
            {
                Console.WriteLine();
                for (var line = 1; line <= 35; line++)
                {
                    Console.WriteLine($"CLICI_FIXTURE_REPLY_{turn:000}_LINE_{line:000} synthetic response text");
                    if (key.Key == ConsoleKey.S)
                    {
                        Thread.Sleep(40);
                    }
                }

                turn++;
                Console.Write($"> CLICI_FIXTURE_USER_{turn:000}");
            }
            else
            {
                Console.WriteLine($"\r\nCLICI_FIXTURE_PASSTHROUGH {key.Modifiers}+{key.Key}");
                Console.Write($"> CLICI_FIXTURE_USER_{turn:000}");
            }
        }

        if (osc)
        {
            Console.Write("\r\n\x1b]133;D;0\x07");
        }
    }
}

internal sealed record ProofWindow(long Handle, int ProcessId, string Title, string Executable, int Left, int Top, int Width, int Height);

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class ProofFocusHandler(int processId) : IUIAutomationFocusChangedEventHandler
{
    public ConcurrentQueue<object> Events { get; } = new();

    public void HandleFocusChangedEvent(IUIAutomationElement sender)
    {
        try
        {
            if (sender.CurrentProcessId == processId && Events.Count < 100)
            {
                Events.Enqueue(new
                {
                    utc = DateTimeOffset.UtcNow,
                    className = sender.CurrentClassName,
                    runtimeId = sender.GetRuntimeId(),
                    focused = sender.CurrentHasKeyboardFocus != 0
                });
            }
        }
        catch (COMException)
        {
            // Disappearing elements are expected during tab/pane teardown.
        }
    }
}

internal static class NativeWindows
{
    internal static bool IsForeground(nint hwnd) => GetForegroundWindow() == hwnd;
    internal static void UsePhysicalCoordinates() => SetProcessDpiAwarenessContext(new nint(-4));

    internal static void RequireActiveProofPane(nint hwnd, IUIAutomationElement element)
    {
        RequireProofWindow(hwnd, "CLICI-PROOF-");
        if (GetForegroundWindow() != hwnd || element.CurrentHasKeyboardFocus == 0)
        {
            throw new InvalidOperationException("The selected proof pane is no longer active; no scroll was started.");
        }
    }

    internal static void Place(nint hwnd, int x, int y, int width, int height)
    {
        if (width is < 400 or > 2000 || height is < 300 or > 2000)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Fixture dimensions must be bounded.");
        }

        if (!SetWindowPos(hwnd, 0, x, y, width, height, 0x0004 | 0x0010))
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    internal static void SendProofKey(nint hwnd, string key)
    {
        var (virtualKey, control) = key switch
        {
            "enter" => ((ushort)0x0D, false),
            "ctrl-up" => ((ushort)0x26, true),
            "ctrl-down" => ((ushort)0x28, true),
            "stream" => ((ushort)0x53, false),
            "finish" => ((ushort)0x51, false),
            "cancel" => ((ushort)0x43, true),
            _ => throw new ArgumentException("Only the documented synthetic-fixture test keys are allowed.")
        };
        SetForegroundWindow(hwnd);
        if (GetForegroundWindow() != hwnd)
        {
            throw new InvalidOperationException("Proof window is not foreground; no input was sent.");
        }

        var inputs = new List<Input>();
        if (control)
        {
            inputs.Add(Input.Key(0x11, false));
        }

        inputs.Add(Input.Key(virtualKey, false));
        inputs.Add(Input.Key(virtualKey, true));
        if (control)
        {
            inputs.Add(Input.Key(0x11, true));
        }

        if (SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>()) != inputs.Count)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    internal static IReadOnlyList<ProofWindow> ListProofWindows(string prefix)
    {
        var results = new List<ProofWindow>();
        EnumWindows((hwnd, _) =>
        {
            if (Title(hwnd).StartsWith(prefix, StringComparison.Ordinal))
            {
                results.Add(RequireProofWindow(hwnd, prefix));
            }

            return true;
        }, 0);
        return results;
    }

    internal static ProofWindow RequireProofWindow(nint hwnd, string prefix)
    {
        var title = Title(hwnd);
        GetWindowThreadProcessId(hwnd, out var pid);
        using var process = Process.GetProcessById((int)pid);
        var path = process.MainModule?.FileName ?? "";
        if (!title.StartsWith(prefix, StringComparison.Ordinal) ||
            !Path.GetFileName(path).Equals("WindowsTerminal.exe", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(Path.Combine(Path.GetDirectoryName(path) ?? "", ".portable")))
        {
            throw new InvalidOperationException("Target must be an explicitly named CLICI-PROOF- portable Windows Terminal fixture.");
        }

        GetWindowRect(hwnd, out var rect);
        return new ProofWindow(hwnd.ToInt64(), (int)pid, title, path,
            rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    private static string Title(nint hwnd)
    {
        var title = new StringBuilder(512);
        GetWindowText(hwnd, title, title.Capacity);
        return title.ToString();
    }

    private delegate bool EnumWindowsCallback(nint hwnd, nint lparam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputData Data;
        public static Input Key(ushort key, bool up) => new()
        {
            Type = 1,
            Data = new InputData { Keyboard = new KeyboardInput { VirtualKey = key, Flags = (up ? 2u : 0u) | (key is 0x26 or 0x28 ? 1u : 0u) } }
        };
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputData
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput { public ushort VirtualKey, Scan; public uint Flags, Time; public nuint ExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public nuint ExtraInfo; }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint lparam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(nint context);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
}
