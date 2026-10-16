using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clici.Core.Navigation;
using Interop.UIAutomationClient;

namespace Clici.PromptNavigation.Probe;

// Private stdin/stdout child of the opt-in native renderer. Development-only:
// it operates solely in CLICI-PROOF-* portable Terminal windows. No global hook,
// clipboard access, listening socket, transcript file, or shell input injection.
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
    private long lastSequence;
    private NavigationCursor? cursor;
    private string? scope;
    private string? lastViewport;
    private long layoutEpoch;

    private RendererBridge(int parentId)
    {
        parent = Process.GetProcessById(parentId);
        _ = parent.SafeHandle; // Pin this process incarnation, not just its reusable PID.
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
        IReadOnlyList<RenderedPromptBinding> Prompts);

    internal static void Run(int parentId)
    {
        var bridge = new RendererBridge(parentId);
        Console.WriteLine("{\"version\":1,\"ready\":true}");
        while (ReadLine() is { } line)
        {
            Request? request = null;
            try
            {
                request = JsonSerializer.Deserialize<Request>(line, Json);
                if (request is null) throw new InvalidOperationException("Missing request.");
                var result = bridge.Navigate(request);
                Console.WriteLine(JsonSerializer.Serialize(result, Json));
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                bridge.cursor = null;
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    sequence = request?.Sequence ?? 0,
                    handled = false,
                    action = "Refused",
                    reason = exception.GetType().Name,
                    detail = exception.Message
                }, Json));
            }
        }
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
            elapsedMilliseconds = timer.Elapsed.TotalMilliseconds
        };
        CheckRequest(request);
        if (request.Direction is not ("previous" or "next" or "observe")) throw new InvalidOperationException("Unknown direction.");
        if (request.Direction == "observe")
        {
            cursor = null;
            // Explicit development evidence path; contains identities and input
            // context only. No terminal or prompt text is written to this file.
            if (Environment.GetEnvironmentVariable("CLICI_RENDERER_REPORT") is { Length: > 0 } report)
                File.WriteAllText(report, JsonSerializer.Serialize(request, Json));
            return Result(false, "Observed", "Metadata received; no navigation requested");
        }

        if (!request.ComposerOwnsInput) return Result(false, "PassThrough", "Native composer does not own input");
        var window = NativeWindows.ListProofWindows("CLICI-PROOF-")
            .SingleOrDefault(candidate => NativeWindows.IsForeground((nint)candidate.Handle));
        if (window is null) return Result(false, "PassThrough", "No foreground proof window");
        var root = automation.ElementFromHandle((nint)window.Handle);
        var condition = automation.CreateAndCondition(
            automation.CreatePropertyCondition(UIA_PropertyIds.UIA_ClassNamePropertyId, "TermControl"),
            automation.CreatePropertyCondition(UIA_PropertyIds.UIA_HasKeyboardFocusPropertyId, true));
        var elements = root.FindAll(TreeScope.TreeScope_Subtree, condition);
        if (elements.Length != 1) return Result(false, "PassThrough", "No unique focused pane");
        var pane = elements.GetElement(0);
        var pattern = (IUIAutomationTextPattern)pane.GetCurrentPattern(UIA_PatternIds.UIA_TextPatternId);
        var rect = pane.CurrentBoundingRectangle;
        var paneId = $"{window.ProcessId}:{window.Handle}:{string.Join(',', pane.GetRuntimeId())}";
        var currentScope = $"{paneId}:{request.SessionId}:{request.Epoch}:{rect.right - rect.left}:{rect.bottom - rect.top}";
        var observation = Observe(pattern, request);
        var mapping = RenderedPromptMap.Resolve(request.Prompts, observation.Anchors, observation.RowCount);
        if (!mapping.Success)
        {
            cursor = null;
            return Result(false, "PassThrough", "Incomplete or ambiguous rendered anchors");
        }

        // Native focus/resize/mouse events advance Epoch. Terminal-owned manual
        // scroll is additionally detected by comparing fresh visible ranges.
        if (scope != currentScope || lastViewport != observation.ViewportKey)
        {
            cursor = null;
            layoutEpoch++;
        }

        scope = currentScope;
        var identity = new NavigationIdentity(paneId, request.Epoch, layoutEpoch,
            new(AgentKind.Codex, request.SessionId));
        if (cursor is not null && !mapping.Prompts.Contains(cursor.Prompt)) cursor = null;
        var snapshot = new NavigationSnapshot(identity, request.Sequence, PaneEligibility.Eligible,
            mapping.Prompts[0].Start, observation.RowCount,
            request.HistoryComplete && mapping.ReachesHistoryStart, true, true,
            observation.ViewportRow, observation.AtLive, mapping.Prompts);
        var direction = request.Direction == "previous" ? NavigationDirection.Previous : NavigationDirection.Next;
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
        NativeWindows.RequireActiveProofPane((nint)window.Handle, pane);
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
            NativeWindows.RequireActiveProofPane((nint)window.Handle, pane);
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
            request.Deadline - Environment.TickCount64 > 1000)
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
        var lines = text.Split("\r\n", StringSplitOptions.None);
        if (lines.Length > MaximumRows) throw new InvalidOperationException("Terminal row limit.");
        var visibleRanges = pattern.GetVisibleRanges();
        if (visibleRanges.Length != 1) throw new InvalidOperationException("Ambiguous viewport.");
        var visible = visibleRanges.GetElement(0);
        var found = new Dictionary<string, List<(int Row, bool End)>>(StringComparer.Ordinal);
        var prefix = document.Clone();
        prefix.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End,
            visible, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start);
        var viewportRow = prefix.GetText(MaximumCharacters).Count(character => character == '\n');
        for (var index = 0; index < lines.Length; index++)
        {
            CheckLive(request);
            if (lines[index].Length < 6 || lines[index].AsSpan(0, 6).IndexOfAnyExcept(' ') >= 0) continue;
            var row = RowAt(document, index);
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

        if (lines.Length == 0 || RowAt(document, lines.Length - 1).CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End,
            document, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End) != 0)
            throw new InvalidOperationException("Incomplete physical row mapping.");
        var anchors = new List<PaintedPromptAnchor>();
        foreach (var (nonce, pair) in found)
        {
            if (pair.Count != 2 || pair[0].End || !pair[1].End)
                throw new InvalidOperationException("Partial or duplicate painted anchor.");
            anchors.Add(new(nonce, pair[0].Row, pair[1].Row));
        }

        return new(anchors, text, lines.Length, viewportRow,
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
