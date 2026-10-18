using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clici.Core.Navigation;
using Interop.UIAutomationClient;

namespace Clici.PromptNavigation.Probe;

// Private stdin/stdout child of the opt-in native renderer. Proof windows are
// the default; live Terminal and keyboard admission require separate opt-ins.
// No clipboard access, listening socket, transcript file or input replay.
internal sealed class RendererBridge
{
    private const int MaximumCharacters = 1_048_576;
    private const int MaximumRows = 4096;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private readonly CUIAutomation automation = new();
    private readonly Process parent;
    private readonly AgentKind agent;
    private long lastSequence;
    private NavigationCursor? cursor;
    private string? scope;
    private string? lastViewport;
    private long layoutEpoch;
    private RendererKeyboardHook? keyboard;
    private NavigationInputRequest? inFlight;
    private readonly bool proofOnly = Environment.GetEnvironmentVariable("CLICI_RENDERER_TERMINALS") != "1";
    private NavigationIdentity? qualifiedIdentity;
    private IReadOnlyList<RenderedPromptBinding>? qualifiedPrompts;
    private IUIAutomationElement? qualifiedPane;
    private ProofWindow? qualifiedWindow;
    private long qualifiedRevision = -1;
    private (int Width, int Height) qualifiedSize;

    private RendererBridge(int parentId, AgentKind agent = AgentKind.Codex)
    {
        parent = Process.GetProcessById(parentId);
        _ = parent.SafeHandle; // Pin this process incarnation, not just its reusable PID.
        this.agent = agent;
    }

    internal static object Inspect(nint hwnd, IReadOnlyList<RenderedPromptBinding> prompts)
    {
        NativeWindows.RequireProofWindow(hwnd, "CLICI-PROOF-");
        var bridge = new RendererBridge(Environment.ProcessId);
        var root = bridge.automation.ElementFromHandle(hwnd);
        var panes = root.FindAll(TreeScope.TreeScope_Subtree,
            bridge.automation.CreatePropertyCondition(UIA_PropertyIds.UIA_ClassNamePropertyId, "TermControl"));
        if (panes.Length != 1) throw new InvalidOperationException("Read-only inspection requires one proof pane.");
        var pattern = (IUIAutomationTextPattern)panes.GetElement(0).GetCurrentPattern(UIA_PatternIds.UIA_TextPatternId);
        var request = new Request(1, 1, Environment.TickCount64 + 1000, Environment.ProcessId,
            "CLICI_FIXTURE_INSPECTION", 1, "previous", true, true, prompts);
        var timer = Stopwatch.StartNew();
        var observation = bridge.Observe(pattern, request);
        return new
        {
            observation.RowCount,
            observation.ViewportRow,
            observation.AtLive,
            observation.Anchors,
            mapping = RenderedPromptMap.Resolve(prompts, observation.Anchors, observation.RowCount),
            elapsedMilliseconds = timer.Elapsed.TotalMilliseconds
        };
    }

    internal sealed record Request(int Version, long Sequence, long Deadline, int ProcessId,
        string SessionId, long Epoch, string Direction, bool HistoryComplete, bool ComposerOwnsInput,
        IReadOnlyList<RenderedPromptBinding> Prompts, long InputObservedAt = 0);

    internal static void Run(int parentId)
    {
        var bridge = new RendererBridge(parentId);
        using var keyboard = bridge.Start();
        Console.WriteLine("""{"version":1,"ready":true}""");
        while (ReadLine() is { } line)
            bridge.Dispatch(() => JsonSerializer.Deserialize<Request>(line, Json)
                ?? throw new InvalidOperationException("Missing request."));
    }

    // Claude Code plugins cannot stream to a child's stdin, so the plugin
    // publishes frames to a private file and the helper polls it. The parent is
    // discovered, never trusted from the command line; the frame still has to
    // name the same session and epoch the plugin bound.
    internal static void RunFrames(string framePath)
    {
        var parentId = ParentProcessId();
        var bridge = new RendererBridge(parentId, AgentKind.ClaudeCode);
        using (var current = Process.GetCurrentProcess())
            if (bridge.parent.StartTime > current.StartTime)
                throw new InvalidOperationException("Parent process identity is not stable.");
        using var keyboard = bridge.Start();
        Console.WriteLine(JsonSerializer.Serialize(new { version = 1, ready = true, parentProcessId = parentId, agent = "claude-code" }, Json));
        SweepStaleFrames(framePath);
        long lastFrame = 0;
        try
        {
            while (!bridge.parent.HasExited)
            {
                var frame = ReadFrame(framePath);
                if (frame is null || frame.Sequence <= lastFrame) { Thread.Sleep(50); continue; }
                lastFrame = frame.Sequence;
                bridge.Dispatch(() =>
                {
                    if (frame.Agent != "claude-code") throw new InvalidOperationException("Unexpected frame agent.");
                    var observed = RendererFrameClock.ObservedTick(frame.ObservedAt,
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Environment.TickCount64)
                        ?? throw new InvalidOperationException("Stale renderer frame.");
                    return new Request(frame.Version, frame.Sequence, observed + RendererFrameClock.MaximumAgeMilliseconds,
                        parentId, frame.SessionId, frame.Epoch, "observe", frame.HistoryComplete,
                        frame.ComposerOwnsInput, frame.Prompts, observed);
                });
            }
        }
        finally
        {
            try { File.Delete(framePath); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private RendererKeyboardHook? Start()
    {
        if (!proofOnly && new System.Security.Principal.WindowsPrincipal(
                System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
            throw new InvalidOperationException("Elevated renderer navigation is not supported.");
        keyboard = Environment.GetEnvironmentVariable("CLICI_RENDERER_KEYS") == "1"
            ? new RendererKeyboardHook(parent, proofOnly) : null;
        return keyboard;
    }

    private void Dispatch(Func<Request> read)
    {
        Request? request = null;
        try
        {
            request = read();
            var result = Navigate(request);
            var resultJson = JsonSerializer.Serialize(result, Json);
            if (resultJson.Contains("\"handled\":true", StringComparison.Ordinal) &&
                Environment.GetEnvironmentVariable("CLICI_RENDERER_RESULT") is { Length: > 0 } resultPath)
                File.WriteAllText(resultPath, resultJson);
            Console.WriteLine(resultJson);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            cursor = null;
            keyboard?.Invalidate();
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                sequence = request?.Sequence ?? 0,
                handled = false,
                action = "Refused",
                reason = exception.GetType().Name,
                detail = exception.Message
            }, Json));
        }
        finally
        {
            if (inFlight is { } active)
                keyboard?.Session.Complete(active, Environment.TickCount64);
            inFlight = null;
        }
    }

    // A frame outlives its helper when the console is destroyed (the tab is
    // closed), so each helper removes siblings nobody has written for an hour;
    // a live plugin rewrites its frame every heartbeat.
    private static void SweepStaleFrames(string framePath)
    {
        if (Path.GetDirectoryName(framePath) is not { Length: > 0 } directory || !Directory.Exists(directory)) return;
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                if (!string.Equals(file, framePath, StringComparison.OrdinalIgnoreCase) &&
                    DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > TimeSpan.FromHours(1))
                    File.Delete(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private static RendererFrame? ReadFrame(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaximumCharacters) return null;
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            return JsonSerializer.Deserialize<RendererFrame>(reader.ReadToEnd(), Json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null; // Absent, mid-write or malformed: wait for the next complete frame.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public nint Reserved1, PebBaseAddress, Reserved2, Reserved3, UniqueProcessId, InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(nint process, int informationClass,
        ref ProcessBasicInformation information, int length, out int returned);

    private static int ParentProcessId()
    {
        var information = new ProcessBasicInformation();
        using var current = Process.GetCurrentProcess();
        if (NtQueryInformationProcess(current.Handle, 0, ref information, Marshal.SizeOf<ProcessBasicInformation>(), out _) != 0)
            throw new InvalidOperationException("Parent process is unknown.");
        return checked((int)information.InheritedFromUniqueProcessId);
    }

    private static string? ReadLine()
    {
        var line = new System.Text.StringBuilder();
        while (Console.Read() is var character && character >= 0)
        {
            if (character == '\n') return line.ToString();
            if (line.Length >= MaximumCharacters) throw new InvalidDataException("Bridge frame exceeds its limit.");
            if (character != '\r') line.Append((char)character);
        }

        return null;
    }

    private object Navigate(Request request)
    {
        var timer = Stopwatch.StartNew();
        object Result(bool handled, string action, string reason, string? messageId = null) => new
        {
            request.Sequence,
            handled,
            action,
            reason,
            messageId,
            admission = keyboard?.LastAdmission,
            invalidation = keyboard?.LastInvalidation,
            revision = keyboard?.Revision,
            elapsedMilliseconds = timer.Elapsed.TotalMilliseconds
        };
        CheckRequest(request);
        var hookRevision = keyboard?.Revision ?? 0;
        if (request.Direction is not ("previous" or "next" or "observe")) throw new InvalidOperationException("Unknown direction.");
        if (request.Direction == "observe")
        {
            // Explicit development evidence path; contains identities and input
            // context only. No terminal or prompt text is written to this file.
            if (Environment.GetEnvironmentVariable("CLICI_RENDERER_REPORT") is { Length: > 0 } report)
                File.WriteAllText(report, JsonSerializer.Serialize(request, Json));
            if (keyboard is null) return Result(false, "Observed", "Metadata received; no navigation requested");
        }

        if (!request.ComposerOwnsInput || request.Prompts.Count == 0)
        {
            keyboard?.Invalidate();
            cursor = null;
            return Result(false, "PassThrough", "Native composer does not own input or has no complete prompts");
        }
        // Keep lease renewal independent of expensive window/pane discovery.
        // The focused COM element and native window are still checked each time;
        // keyboard, mouse and focus events invalidate this cached qualification.
        if (request.Direction == "observe" && keyboard is not null && qualifiedPane is not null &&
            qualifiedWindow is not null && qualifiedIdentity?.Session?.SessionId == request.SessionId &&
            qualifiedIdentity.SessionEpoch == request.Epoch && qualifiedRevision == hookRevision &&
            qualifiedPrompts is not null && qualifiedPrompts.SequenceEqual(request.Prompts) &&
            NativeWindows.IsForeground((nint)qualifiedWindow.Handle) && qualifiedPane.CurrentHasKeyboardFocus != 0)
        {
            var size = qualifiedPane.CurrentBoundingRectangle;
            if (qualifiedSize == (size.right - size.left, size.bottom - size.top))
            {
                if (!PublishLease(request, qualifiedWindow, qualifiedIdentity, hookRevision))
                    return Result(false, "PassThrough", "No fresh native input lease");
                inFlight = keyboard.Session.TakeConfirmed(Environment.TickCount64);
                if (inFlight is null) return Result(false, "Observed", "Keyboard lease renewed; no confirmed press");
            }
        }
        var window = NativeWindows.ForegroundRendererWindow(proofOnly);
        if (window is null) { keyboard?.Invalidate(); return Result(false, "PassThrough", "No foreground proof window"); }
        var root = automation.ElementFromHandle((nint)window.Handle);
        if (root.FindAll(TreeScope.TreeScope_Subtree,
            automation.CreatePropertyCondition(UIA_PropertyIds.UIA_ClassNamePropertyId, "TermControl")).Length != 1)
        {
            keyboard?.Invalidate();
            return Result(false, "PassThrough", "Only a single terminal pane is qualified");
        }
        var condition = automation.CreateAndCondition(
            automation.CreatePropertyCondition(UIA_PropertyIds.UIA_ClassNamePropertyId, "TermControl"),
            automation.CreatePropertyCondition(UIA_PropertyIds.UIA_HasKeyboardFocusPropertyId, true));
        var elements = root.FindAll(TreeScope.TreeScope_Subtree, condition);
        if (elements.Length != 1) { keyboard?.Invalidate(); return Result(false, "PassThrough", "No unique focused pane"); }
        var pane = elements.GetElement(0);
        var pattern = (IUIAutomationTextPattern)pane.GetCurrentPattern(UIA_PatternIds.UIA_TextPatternId);
        var rect = pane.CurrentBoundingRectangle;
        var paneId = $"{window.ProcessId}:{window.Handle}:{string.Join(',', pane.GetRuntimeId())}";
        var currentScope = $"{paneId}:{request.SessionId}:{request.Epoch}:{hookRevision}:{rect.right - rect.left}:{rect.bottom - rect.top}";
        var observation = Observe(pattern, request);
        var mapping = RenderedPromptMap.Resolve(request.Prompts, observation.Anchors, observation.RowCount);
        if (!mapping.Success)
        {
            cursor = null;
            keyboard?.Invalidate();
            return Result(false, "PassThrough", "Incomplete or ambiguous rendered anchors");
        }

        // Native focus/resize/mouse events advance Epoch. Terminal-owned manual
        // scroll is additionally detected by comparing fresh visible ranges.
        if (scope != currentScope)
        {
            cursor = null;
            layoutEpoch++;
        }
        else if (lastViewport != observation.ViewportKey)
        {
            // Passive output can move the live viewport without changing input
            // ownership or layout. Rebase navigation on the fresh viewport;
            // do not discard a confirmed press as though the pane had changed.
            // Actual pointer/focus input already invalidates the hook revision.
            cursor = null;
        }

        scope = currentScope;
        var identity = new NavigationIdentity(paneId, request.Epoch, layoutEpoch,
            new(agent, request.SessionId));
        qualifiedIdentity = identity;
        qualifiedPrompts = request.Prompts;
        qualifiedPane = pane;
        qualifiedWindow = window;
        qualifiedRevision = hookRevision;
        qualifiedSize = (rect.right - rect.left, rect.bottom - rect.top);
        var direction = request.Direction == "previous" ? NavigationDirection.Previous : NavigationDirection.Next;
        if (request.Direction == "observe" && keyboard is not null)
        {
            if (!PublishLease(request, window, identity, hookRevision))
                return Result(false, "PassThrough", "No fresh native input lease");
            inFlight = keyboard.Session.TakeConfirmed(Environment.TickCount64);
            lastViewport = observation.ViewportKey;
            if (inFlight is null) return Result(false, "Observed", "Keyboard lease renewed; no confirmed press");
            direction = inFlight.Direction;
        }
        if (cursor is not null && !mapping.Prompts.Contains(cursor.Prompt)) cursor = null;
        var snapshot = new NavigationSnapshot(identity, request.Sequence, PaneEligibility.Eligible,
            mapping.Prompts[0].Start, observation.RowCount,
            request.HistoryComplete && mapping.ReachesHistoryStart, true, true,
            observation.ViewportRow, observation.AtLive, mapping.Prompts);
        var decision = Clici.Core.Navigation.PromptNavigation.Decide(snapshot, direction, cursor);
        lastViewport = observation.ViewportKey;
        if (decision.Action is not (NavigationAction.Jump or NavigationAction.ReturnToLive))
            return Result(true, decision.Action.ToString(), decision.Reason.ToString());

        var target = decision.Action == NavigationAction.ReturnToLive
            ? pattern.DocumentRange : RowAt(pattern.DocumentRange, (int)decision.Target!.Start);
        if (decision.Action == NavigationAction.ReturnToLive)
            target.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start,
                target, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End);
        CheckLive(request);
        NativeWindows.RequireActiveRendererPane((nint)window.Handle, pane, proofOnly);
        var latestRect = pane.CurrentBoundingRectangle;
        if (latestRect.right - latestRect.left != rect.right - rect.left ||
            latestRect.bottom - latestRect.top != rect.bottom - rect.top ||
            pattern.DocumentRange.GetText(MaximumCharacters + 1) != observation.Text)
            throw new InvalidOperationException("Terminal changed before scrolling.");
        var selected = Selection(pattern);
        // Once this call starts, this key belongs to navigation even if readback
        // fails. Never replay it into the composer after a possible scroll.
        try
        {
            target.ScrollIntoView(1);
            CheckLive(request);
            NativeWindows.RequireActiveRendererPane((nint)window.Handle, pane, proofOnly);
            var visible = pattern.GetVisibleRanges();
            if (visible.Length != 1) throw new InvalidOperationException("Ambiguous scroll readback.");
            var viewport = visible.GetElement(0);
            var reached = decision.Action == NavigationAction.ReturnToLive
                ? viewport.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End,
                    pattern.DocumentRange, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End) >= 0
                : viewport.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start,
                    target, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start) <= 0 &&
                  viewport.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End,
                    target, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start) > 0;
            if (!reached || !SameSelection(selected, Selection(pattern)))
                throw new InvalidOperationException("Scroll target or selection did not verify.");
            lastViewport = ViewportKey(pattern, viewport);
            cursor = decision.Target is { } prompt ? new(identity, prompt) : null;
            return Result(true, decision.Action.ToString(), "Verified", decision.Target?.MessageId);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            cursor = null;
            return Result(true, "Unverified", exception.GetType().Name);
        }
    }

    private bool PublishLease(Request request, ProofWindow window, NavigationIdentity identity, long revision) =>
        request.InputObservedAt > 0 && request.InputObservedAt <= Environment.TickCount64 &&
        request.Deadline - request.InputObservedAt <= 750 &&
        keyboard!.Publish((nint)window.Handle, new(identity, revision, request.Deadline), request.InputObservedAt, revision);

    private void CheckRequest(Request request)
    {
        if (request.Version != 1 || request.Sequence <= lastSequence || request.ProcessId != parent.Id ||
            string.IsNullOrWhiteSpace(request.SessionId) || request.SessionId.Length > 512 || request.Epoch < 0 ||
            request.Prompts is null || request.Prompts.Count > RenderedPromptMap.MaximumPrompts ||
            request.Prompts.Count == 0 && request.Direction != "observe")
            throw new InvalidOperationException("Invalid renderer frame.");
        lastSequence = request.Sequence;
        CheckLive(request);
    }

    private void CheckLive(Request request)
    {
        if (parent.HasExited || request.Deadline <= Environment.TickCount64 ||
            request.Deadline - Environment.TickCount64 > 1000 ||
            inFlight is not null && keyboard?.Session.CanExecute(inFlight, Environment.TickCount64) != true)
            throw new InvalidOperationException("Renderer request expired or its process exited.");
    }

    private sealed record Observation(List<PaintedPromptAnchor> Anchors, string Text,
        int RowCount, long ViewportRow, bool AtLive, string ViewportKey);

    private Observation Observe(IUIAutomationTextPattern pattern, Request request)
    {
        var firstColors = new Dictionary<int, List<(string Nonce, bool End, int[] Colors)>>();
        foreach (var prompt in request.Prompts)
        {
            foreach (var end in new[] { false, true })
            {
                var colors = ColorAnchorProbe.Colors(prompt.Nonce, end);
                if (!firstColors.TryGetValue(colors[0], out var candidates))
                    firstColors.Add(colors[0], candidates = []);
                candidates.Add((prompt.Nonce, end, colors));
            }
        }

        var document = pattern.DocumentRange;
        var text = document.GetText(MaximumCharacters + 1);
        if (text.Length > MaximumCharacters) throw new InvalidOperationException("Terminal capture limit.");
        var visibleRanges = pattern.GetVisibleRanges();
        if (visibleRanges.Length != 1) throw new InvalidOperationException("Ambiguous viewport.");
        var visible = visibleRanges.GetElement(0);
        var found = new Dictionary<string, List<(int Row, bool End)>>(StringComparer.Ordinal);
        // GetText joins soft-wrapped rows without CRLF. Use the same UIA line
        // units for discovery, viewport coordinates and later scroll targets.
        var rows = new List<IUIAutomationTextRange>();
        var viewportRow = 0;
        for (var index = 0; ; index++)
        {
            CheckLive(request);
            if (index >= MaximumRows) throw new InvalidOperationException("Terminal row limit.");
            var row = RowAt(document, index);
            rows.Add(row);
            if (row.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start,
                visible, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start) <= 0) viewportRow = index;
            if (row.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End,
                document, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End) == 0) break;
        }
        for (var index = 0; index < rows.Count; index++)
        {
            CheckLive(request);
            var row = rows[index];
            var beginning = row.GetText(6);
            if (beginning.Length != 6 || beginning.AsSpan().IndexOfAnyExcept(' ') >= 0) continue;
            var cell = row.Clone();
            cell.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End,
                cell, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start);
            cell.MoveEndpointByUnit(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, TextUnit.TextUnit_Character, 1);
            if (cell.GetAttributeValue(UIA_TextAttributeIds.UIA_ForegroundColorAttributeId) is not int first ||
                !firstColors.TryGetValue(first, out var candidates)) continue;
            var actual = new List<int>();
            for (var column = 0; column < 6; column++)
            {
                if (cell.GetText(2) != " " ||
                    cell.GetAttributeValue(UIA_TextAttributeIds.UIA_ForegroundColorAttributeId) is not int color) break;
                actual.Add(color);
                cell.Move(TextUnit.TextUnit_Character, 1);
            }

            foreach (var candidate in candidates.Where(candidate => actual.SequenceEqual(candidate.Colors)))
            {
                if (!found.TryGetValue(candidate.Nonce, out var pair)) found.Add(candidate.Nonce, pair = []);
                pair.Add((index, candidate.End));
            }
        }

        var anchors = new List<PaintedPromptAnchor>();
        foreach (var (nonce, pair) in found)
        {
            if (pair.Count != 2 || pair[0].End || !pair[1].End)
                throw new InvalidOperationException("Partial or duplicate painted anchor.");
            anchors.Add(new(nonce, pair[0].Row, pair[1].Row));
        }

        return new(anchors, text, rows.Count, viewportRow,
            visible.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End,
                document, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End) >= 0,
            ViewportKey(pattern, visible));
    }

    private static IUIAutomationTextRange RowAt(IUIAutomationTextRange document, int index)
    {
        var row = document.Clone();
        row.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End,
            row, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start);
        if (row.Move(TextUnit.TextUnit_Line, index) != index)
            throw new InvalidOperationException("Terminal row mapping changed.");
        row.ExpandToEnclosingUnit(TextUnit.TextUnit_Line);
        return row;
    }

    private static string ViewportKey(IUIAutomationTextPattern pattern, IUIAutomationTextRange visible)
    {
        var prefix = pattern.DocumentRange;
        prefix.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End,
            visible, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start);
        // Kept only in memory. The digest is viewport evidence, never a prompt ID.
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(prefix.GetText(MaximumCharacters))));
    }

    private static IUIAutomationTextRange[] Selection(IUIAutomationTextPattern pattern)
    {
        var selected = pattern.GetSelection();
        return Enumerable.Range(0, selected.Length).Select(index => selected.GetElement(index).Clone()).ToArray();
    }

    private static bool SameSelection(IUIAutomationTextRange[] before, IUIAutomationTextRange[] after) =>
        before.Length == after.Length && before.Zip(after).All(pair => pair.First.Compare(pair.Second) != 0);
}
