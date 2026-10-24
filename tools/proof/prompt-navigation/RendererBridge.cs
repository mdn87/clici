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
    private NativeAdmission? admission;
    private NavigationInputRequest? inFlight;
    private readonly bool proofOnly = Environment.GetEnvironmentVariable("CLICI_RENDERER_TERMINALS") != "1";
    private NavigationIdentity? qualifiedIdentity;
    private IReadOnlyList<RenderedPromptBinding>? qualifiedPrompts;
    private IUIAutomationElement? qualifiedPane;
    private ProofWindow? qualifiedWindow;
    private long qualifiedRevision = -1;
    private (int Width, int Height) qualifiedSize;
    private IUIAutomationTextRange? qualifiedWitness;
    private string? witnessNonce;
    private long nextDiscoveryAt;
    private long fullScans;

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
        IReadOnlyList<RenderedPromptBinding> Prompts, long InputObservedAt = 0, ulong AdmissionWord = 0);

    internal static void Run(int parentId)
    {
        var bridge = new RendererBridge(parentId);
        // v2 starts with a private handshake. The native parent assigns the Job
        // before sending this, so no hook can be installed outside its lifetime.
        if (Environment.GetEnvironmentVariable("CLICI_RENDERER_PROTOCOL") == "2")
        {
            if (ParentProcessId() != parentId) throw new InvalidOperationException("Unexpected native parent.");
            using var hello = JsonDocument.Parse(ReadLine() ?? throw new InvalidOperationException("Missing admission handshake."));
            if (hello.RootElement.GetProperty("version").GetInt32() != 2) throw new InvalidOperationException("Admission version mismatch.");
            bridge.admission = new NativeAdmission((nint)hello.RootElement.GetProperty("mappingHandle").GetInt64(),
                parentId, hello.RootElement.GetProperty("incarnation").GetUInt64());
        }
        using var admission = bridge.admission;
        using var keyboard = bridge.Start();
        Console.WriteLine(JsonSerializer.Serialize(new { version = admission is null ? 1 : 2, ready = true }, Json));
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
            ? new RendererKeyboardHook(parent, proofOnly, admission) : null;
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
            // Ordinary typing can revoke an in-flight native observation. That
            // changes input eligibility, not the verified terminal layout.
            keyboard?.Invalidate(admission is not null && request is not null &&
                admission.Word != request.AdmissionWord ? 4 : 0);
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
            fullScans,
            hookTiming = keyboard?.Timing,
            elapsedMilliseconds = timer.Elapsed.TotalMilliseconds
        };
        CheckRequest(request);
        var hookRevision = keyboard?.Revision ?? 0;
        var layoutRevision = keyboard?.LayoutRevision ?? 0;
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
        // A known background pane never scans the foreground session's history.
        if (qualifiedPane is not null && qualifiedWindow is not null &&
            (!NativeWindows.IsForeground((nint)qualifiedWindow.Handle) || qualifiedPane.CurrentHasKeyboardFocus == 0))
        {
            keyboard?.Invalidate(2);
            return Result(false, "PassThrough", "Bound pane is not focused");
        }
        // Keep lease renewal independent of expensive window/pane discovery.
        // The focused COM element and native window are still checked each time;
        // keyboard, mouse and focus events invalidate this cached qualification.
        if (request.Direction == "observe" && keyboard is not null && qualifiedPane is not null &&
            qualifiedWindow is not null && qualifiedIdentity?.Session?.SessionId == request.SessionId &&
            qualifiedIdentity.SessionEpoch == request.Epoch && qualifiedRevision == layoutRevision &&
            qualifiedPrompts is not null && qualifiedPrompts.SequenceEqual(request.Prompts) &&
            NativeWindows.IsForeground((nint)qualifiedWindow.Handle) && qualifiedPane.CurrentHasKeyboardFocus != 0)
        {
            var size = qualifiedPane.CurrentBoundingRectangle;
            if (qualifiedSize == (size.right - size.left, size.bottom - size.top) && WitnessMatches())
            {
                if (!PublishLease(request, qualifiedWindow, qualifiedIdentity, hookRevision))
                    return Result(false, "PassThrough", "No fresh native input lease");
                inFlight = keyboard.Session.TakeConfirmed(Environment.TickCount64);
                if (inFlight is null) return Result(false, "Observed", "Keyboard lease renewed; no confirmed press");
            }
        }
        if (inFlight is null && Environment.TickCount64 < nextDiscoveryAt)
            return Result(false, "PassThrough", "Discovery retry is throttled");
        nextDiscoveryAt = Environment.TickCount64 + 1000;
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
        var currentScope = $"{paneId}:{request.SessionId}:{request.Epoch}:{layoutRevision}:{rect.right - rect.left}:{rect.bottom - rect.top}";
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
        qualifiedRevision = layoutRevision;
        qualifiedSize = (rect.right - rect.left, rect.bottom - rect.top);
        var witness = observation.Anchors[0];
        qualifiedWitness = RowAt(observation.Range, (int)witness.StartRow);
        witnessNonce = witness.Nonce;
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
            request.HistoryComplete && mapping.ReachesHistoryStart && !observation.Truncated, true, true,
            observation.ViewportRow, observation.AtLive, mapping.Prompts);
        var decision = Clici.Core.Navigation.PromptNavigation.Decide(snapshot, direction, cursor);
        lastViewport = observation.ViewportKey;
        if (decision.Action is not (NavigationAction.Jump or NavigationAction.ReturnToLive))
            return Result(true, decision.Action.ToString(), decision.Reason.ToString());

        var target = decision.Action == NavigationAction.ReturnToLive
            ? pattern.DocumentRange : RowAt(observation.Range, (int)decision.Target!.Start);
        if (decision.Action == NavigationAction.ReturnToLive)
            target.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start,
                target, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End);
        CheckLive(request);
        NativeWindows.RequireActiveRendererPane((nint)window.Handle, pane, proofOnly);
        var latestRect = pane.CurrentBoundingRectangle;
        var latestCapture = Capture(pattern.DocumentRange);
        if (latestCapture.Range.Compare(observation.Range) == 0 ||
            latestRect.right - latestRect.left != rect.right - rect.left ||
            latestRect.bottom - latestRect.top != rect.bottom - rect.top ||
            latestCapture.Range.GetText(MaximumCharacters + 1) != observation.Text)
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
            lastViewport = ViewportKey(observation.Range, viewport);
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
        keyboard!.Publish((nint)window.Handle, new(identity, revision, request.Deadline), request.InputObservedAt, revision, request.AdmissionWord);

    private bool WitnessMatches()
    {
        if (qualifiedWitness is null || witnessNonce is null) return false;
        var cell = qualifiedWitness.Clone();
        cell.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End,
            cell, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start);
        cell.MoveEndpointByUnit(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, TextUnit.TextUnit_Character, 1);
        foreach (var expected in ColorAnchorProbe.Colors(witnessNonce, false))
        {
            if (cell.GetText(2) != " " || cell.GetAttributeValue(UIA_TextAttributeIds.UIA_ForegroundColorAttributeId) is not int actual || actual != expected)
                return false;
            cell.Move(TextUnit.TextUnit_Character, 1);
        }
        return true;
    }

    private void CheckRequest(Request request)
    {
        if (request.Version != (admission is null ? 1 : 2) || request.Sequence <= lastSequence || request.ProcessId != parent.Id ||
            string.IsNullOrWhiteSpace(request.SessionId) || request.SessionId.Length > 512 || request.Epoch < 0 ||
            request.Prompts is null || request.Prompts.Count > RenderedPromptMap.MaximumPrompts ||
            request.Prompts.Count == 0 && request.Direction != "observe")
            throw new InvalidOperationException("Invalid renderer frame.");
        lastSequence = request.Sequence;
        CheckLive(request);
    }

    private void CheckLive(Request request)
    {
        if (parent.HasExited)
            throw new InvalidOperationException("Renderer process exited.");
        if (request.Deadline <= Environment.TickCount64 || request.Deadline - Environment.TickCount64 > 1000)
            throw new InvalidOperationException("Renderer frame expired or exceeds its deadline bound.");
        if (admission is not null && (admission.Word != request.AdmissionWord ||
                request.ComposerOwnsInput != ((request.AdmissionWord & 1) != 0)))
            throw new InvalidOperationException("Native input admission changed.");
        if (inFlight is not null && keyboard?.Session.CanExecute(inFlight, Environment.TickCount64) != true)
            throw new InvalidOperationException($"Navigation gesture expired or was invalidated (remaining {inFlight.DeadlineMilliseconds - Environment.TickCount64} ms).");
    }

    private sealed record Observation(List<PaintedPromptAnchor> Anchors, string Text,
        int RowCount, long ViewportRow, bool AtLive, string ViewportKey,
        IUIAutomationTextRange Range, bool Truncated);

    private Observation Observe(IUIAutomationTextPattern pattern, Request request)
    {
        fullScans++;
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

        var capture = Capture(pattern.DocumentRange);
        var document = capture.Range;
        var text = document.GetText(MaximumCharacters + 1);
        if (text.Length > MaximumCharacters) throw new InvalidOperationException("Terminal capture limit.");
        var visibleRanges = pattern.GetVisibleRanges();
        if (visibleRanges.Length != 1) throw new InvalidOperationException("Ambiguous viewport.");
        var visible = visibleRanges.GetElement(0);
        var found = new Dictionary<string, List<(int Row, bool End)>>(StringComparer.Ordinal);
        // Narrow candidate blocks before reading colors instead of walking every
        // row over COM. Coordinates still use UIA line units, including soft wraps.
        var rowCount = capture.Rows;
        var viewportRow = RowIndex(document, visible, rowCount);
        var pending = new Stack<(IUIAutomationTextRange Range, int Start, int Count)>();
        pending.Push((document.Clone(), 0, rowCount));
        while (pending.TryPop(out var block))
        {
            CheckLive(request);
            var row = block.Range;
            // Reject whole uniform-color blocks that cannot contain an anchor.
            // Mixed/unsupported attributes still take the complete path below.
            if (row.GetAttributeValue(UIA_TextAttributeIds.UIA_ForegroundColorAttributeId) is int uniformColor &&
                !firstColors.ContainsKey(uniformColor)) continue;
            var chunk = row.GetText(MaximumCharacters);
            var (aligned, candidatesByRow) = AnchorRowCandidates.Analyze(chunk, block.Count);
            if (candidatesByRow.Count == 0) continue;
            if (!aligned)
            {
                if (block.Count <= 1)
                    throw new InvalidOperationException("Terminal row mapping changed.");
                var half = block.Count / 2;
                // Subdivide the existing provider range. Recreating every child
                // from the document origin multiplies cross-process calls and
                // repeats traversal through the same history on dense output.
                var left = row.Clone();
                left.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End,
                    left, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start);
                if (left.MoveEndpointByUnit(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End,
                        TextUnit.TextUnit_Line, half) != half)
                    throw new InvalidOperationException("Terminal row mapping changed.");
                var right = row.Clone();
                right.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start,
                    left, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End);
                pending.Push((right, block.Start + half, block.Count - half));
                pending.Push((left, block.Start, half));
                continue;
            }
            row.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End,
                row, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start);
            row.ExpandToEnclosingUnit(TextUnit.TextUnit_Line);
            var position = 0;
            // A small color range can reject many blank rows at once, even
            // when text elsewhere in the document uses different colors.
            foreach (var group in candidatesByRow.GroupBy(index => index / 64))
            {
                CheckLive(request);
                var first = group.First();
                var delta = first - position;
                if (row.Move(TextUnit.TextUnit_Line, delta) != delta)
                    throw new InvalidOperationException("Terminal row mapping changed.");
                position = first;
                var colorBlock = row.Clone();
                var span = group.Last() - first;
                if (colorBlock.MoveEndpointByUnit(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End,
                        TextUnit.TextUnit_Line, span) != span)
                    throw new InvalidOperationException("Terminal row mapping changed.");
                if (colorBlock.GetAttributeValue(UIA_TextAttributeIds.UIA_ForegroundColorAttributeId) is int blockColor &&
                    !firstColors.ContainsKey(blockColor)) continue;
                foreach (var candidateRow in group)
                {
                    CheckLive(request);
                    delta = candidateRow - position;
                    if (row.Move(TextUnit.TextUnit_Line, delta) != delta)
                        throw new InvalidOperationException("Terminal row mapping changed.");
                    position = candidateRow;
                    if (row.GetAttributeValue(UIA_TextAttributeIds.UIA_ForegroundColorAttributeId) is int rowColor &&
                        !firstColors.ContainsKey(rowColor)) continue;
                    var cell = row.Clone();
                    cell.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End,
                        cell, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start);
                    cell.MoveEndpointByUnit(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, TextUnit.TextUnit_Character, 1);
                    var actual = new List<int>();
                    for (var column = 0; column < 6; column++)
                    {
                        if (cell.GetText(2) != " " ||
                            cell.GetAttributeValue(UIA_TextAttributeIds.UIA_ForegroundColorAttributeId) is not int color) break;
                        if (column == 0 && !firstColors.ContainsKey(color)) break;
                        actual.Add(color);
                        cell.Move(TextUnit.TextUnit_Character, 1);
                    }
                    if (actual.Count != 6 || !firstColors.TryGetValue(actual[0], out var candidates)) continue;
                    foreach (var candidate in candidates.Where(candidate => actual.SequenceEqual(candidate.Colors)))
                    {
                        if (!found.TryGetValue(candidate.Nonce, out var pair)) found.Add(candidate.Nonce, pair = []);
                        pair.Add((block.Start + candidateRow, candidate.End));
                    }
                }
            }
        }

        var anchors = new List<PaintedPromptAnchor>();
        foreach (var (nonce, pair) in found)
        {
            pair.Sort((left, right) => left.Row.CompareTo(right.Row));
            if (pair.Count != 2 || pair[0].End || !pair[1].End)
                throw new InvalidOperationException("Partial or duplicate painted anchor.");
            anchors.Add(new(nonce, pair[0].Row, pair[1].Row));
        }

        return new(anchors, text, rowCount, viewportRow,
            visible.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End,
                document, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End) >= 0,
            ViewportKey(document, visible), document, capture.Truncated);
    }

    // Keep the most recent physical rows, rather than requiring the entire
    // terminal history to fit. No work or coordinate depends on its total size.
    private static (IUIAutomationTextRange Range, int Rows, bool Truncated) Capture(IUIAutomationTextRange document)
    {
        var last = document.Clone();
        last.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start,
            document, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End);
        last.MoveEndpointByUnit(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, TextUnit.TextUnit_Character, -1);
        last.ExpandToEnclosingUnit(TextUnit.TextUnit_Line);
        var range = document.Clone();
        range.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start,
            last, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start);
        var moved = range.MoveEndpointByUnit(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start,
            TextUnit.TextUnit_Line, -(MaximumRows - 1));
        if (moved > 0 || moved < -(MaximumRows - 1))
            throw new InvalidOperationException("Terminal row mapping changed.");
        return (range, 1 - moved, range.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start,
            document, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start) > 0);
    }

    private static int RowIndex(IUIAutomationTextRange document, IUIAutomationTextRange position, int rows)
    {
        if (position.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start,
                document, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start) < 0) return -1;
        // Count toward the live end, which is inside the bounded capture.
        // Walking backward would escape the range into older terminal history.
        var suffix = position.Clone();
        suffix.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End,
            document, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End);
        var moved = suffix.MoveEndpointByUnit(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start,
            TextUnit.TextUnit_Line, MaximumRows);
        if (moved < 1 || moved > rows || suffix.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start,
                document, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End) != 0)
            throw new InvalidOperationException("Terminal row mapping changed.");
        return rows - moved;
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

    private static string ViewportKey(IUIAutomationTextRange document, IUIAutomationTextRange visible)
    {
        if (visible.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start,
                document, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start) < 0) return "OutsideCapture";
        var prefix = document.Clone();
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
