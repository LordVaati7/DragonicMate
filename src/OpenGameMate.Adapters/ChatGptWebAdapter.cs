using System.Runtime.InteropServices;
using System.Text.Json;
using OpenGameMate.Browser;
using OpenGameMate.Core;

namespace OpenGameMate.Adapters;

public interface IAiWebAdapter
{
    Task<InputPreparationResult> PrepareImageAsync(string imagePath, CancellationToken cancellationToken = default);
    Task<TextPreparationResult> PrepareTextAsync(string message, CancellationToken cancellationToken = default);
    Task<InputPreparationResult> PrepareInputAsync(string imagePath, string message, CancellationToken cancellationToken = default);
    Task<SubmissionResult> SubmitAsync(CancellationToken cancellationToken = default);
    Task<AdapterIdleProbeResult> ProbeIdleAsync(CancellationToken cancellationToken = default);
    Task<SubmissionResult> SubmitPreparedInputOnceAsync(long expectedAudioStateVersion,
        TimeSpan requiredSilentDuration, AdapterPageState expectedPageState, CancellationToken cancellationToken = default);
}

public enum SubmitControlDecision
{
    Invoke,
    Wait,
    Fail,
}

public sealed class ChatGptWebAdapter : IAiWebAdapter
{
    private const int MaximumDiagnosticButtonCandidates = 12;
    private const int PollAttempts = 120;
    private const int PollIntervalMilliseconds = 250;
    private const int MaximumMessageCharacters = 8000;
    private const long MaximumImageBytes = 20 * 1024 * 1024;
    private static readonly Lazy<string> ComposerScript = new(() =>
    {
        using var stream = typeof(ChatGptWebAdapter).Assembly.GetManifestResourceStream(
            "OpenGameMate.Adapters.ChatGptComposer.js")
            ?? throw new InvalidOperationException("The embedded composer adapter is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });
    private readonly ChatGptBrowserSession _session;
    private readonly ChatGptAdapterRules _rules;
    private string? _operationId;
    private bool _imagePrepared;

    public ChatGptWebAdapter(ChatGptBrowserSession session, ChatGptAdapterRules? rules = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _rules = rules ?? ChatGptAdapterRules.BuiltIn;
        _rules.Validate();
    }

    public async Task<InputPreparationResult> PrepareInputAsync(
        string imagePath, string message, CancellationToken cancellationToken = default)
    {
        if (!IsMessageValid(message))
        {
            return new(false, false, false, "message-invalid", WebAdapterStatus.InvalidInput);
        }

        var image = await PrepareImageAsync(imagePath, cancellationToken);
        if (image.Status != WebAdapterStatus.Succeeded)
        {
            return image;
        }

        var text = await PrepareTextAsync(message, cancellationToken);
        return image with { TextInserted = text.TextInserted, Code = text.Code,
            Status = text.Status, Diagnostics = text.Diagnostics };
    }

    public async Task<InputPreparationResult> PrepareImageAsync(
        string imagePath, CancellationToken cancellationToken = default)
    {
        _imagePrepared = false;
        _operationId = Guid.NewGuid().ToString("N");
        try
        {
            if (!IsReady()) return new(false, false, false, "not-on-chatgpt", WebAdapterStatus.NotReady);
            if (!File.Exists(imagePath)) return new(false, false, false, "image-file-missing", WebAdapterStatus.InvalidInput);
            if (new FileInfo(imagePath).Length is <= 0 or > MaximumImageBytes)
            {
                return new(false, false, false, "image-file-size-invalid", WebAdapterStatus.InvalidInput);
            }

            var located = await EvaluateActionAsync("begin", requireImage: true);
            var method = ReadBoundedString(located, "method", 32);
            if (method is null || IsPlatformBlocked(located))
            {
                return ImageResult(located, PreparationFailureStatus(located));
            }

            if (method == "file-input")
            {
                using var document = await CallCdpAsync("DOM.getDocument", new { depth = 1, pierce = true });
                var rootId = document.RootElement.GetProperty("root").GetProperty("nodeId").GetInt32();
                using var query = await CallCdpAsync("DOM.querySelector", new
                {
                    nodeId = rootId,
                    selector = $"input[data-opengamemate-file=\"{_operationId}\"]",
                });
                var fileNodeId = query.RootElement.GetProperty("nodeId").GetInt32();
                if (fileNodeId == 0)
                {
                    return new(false, false, false, "marked-file-input-lost", WebAdapterStatus.AdapterInvalid);
                }

                cancellationToken.ThrowIfCancellationRequested();
                using (await CallCdpAsync("DOM.setFileInputFiles", new
                {
                    files = new[] { Path.GetFullPath(imagePath) }, nodeId = fileNodeId,
                }))
                {
                }
                var selected = await EvaluateActionAsync("file-events");
                if (GetCode(selected) != "file-selected") return ImageResult(selected, PreparationFailureStatus(selected));
            }
            else
            {
                var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
                var pasted = await EvaluateActionAsync("paste", imageBase64: Convert.ToBase64String(bytes));
                if (GetCode(pasted) != "paste-dispatched") return ImageResult(pasted, PreparationFailureStatus(pasted));
            }

            for (var attempt = 0; attempt < PollAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var probe = await EvaluateActionAsync("attachment");
                if (IsPlatformBlocked(probe) || GetCode(probe) != "attachment-observed")
                {
                    return ImageResult(probe, PreparationFailureStatus(probe));
                }
                if (GetBoolean(probe, "previewDetected") && !GetBoolean(probe, "uploading"))
                {
                    _imagePrepared = true;
                    return ImageResult(probe, WebAdapterStatus.Succeeded) with { Code = $"{method}-preview-ready" };
                }
                await Task.Delay(PollIntervalMilliseconds, cancellationToken);
            }
            return new(false, false, false, "attachment-ready-timeout", WebAdapterStatus.AdapterInvalid);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsAdapterException(exception))
        {
            return new(false, false, false, "attachment-runtime-failed", WebAdapterStatus.AdapterInvalid);
        }
    }

    public async Task<TextPreparationResult> PrepareTextAsync(
        string message, CancellationToken cancellationToken = default)
    {
        if (!IsMessageValid(message)) return new(false, "message-invalid", WebAdapterStatus.InvalidInput);
        if (!IsReady()) return new(false, "not-on-chatgpt", WebAdapterStatus.NotReady);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_imagePrepared)
            {
                _operationId = Guid.NewGuid().ToString("N");
                var begin = await EvaluateActionAsync("begin");
                if (ReadBoundedString(begin, "method", 32) is null || IsPlatformBlocked(begin))
                {
                    return new(false, GetCode(begin), PreparationFailureStatus(begin), ParseAdapterDiagnostics(begin));
                }
            }
            var result = await EvaluateActionAsync("text", message: message);
            var inserted = GetBoolean(result, "textInserted");
            return new(inserted, GetCode(result), inserted && !IsPlatformBlocked(result)
                ? WebAdapterStatus.Succeeded : PreparationFailureStatus(result), ParseAdapterDiagnostics(result));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsAdapterException(exception))
        {
            return new(false, "text-runtime-failed", WebAdapterStatus.AdapterInvalid);
        }
    }

    public async Task<AdapterIdleProbeResult> ProbeIdleAsync(CancellationToken cancellationToken = default)
    {
        if (!IsReady()) return IdleFailure("not-on-chatgpt", WebAdapterStatus.NotReady);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await EvaluateActionAsync("idle");
            var diagnostics = ParseAdapterDiagnostics(result);
            var composerCount = GetInt32(result, "composerCount");
            var stops = GetInt32(result, "stopButtonCount");
            var sends = GetInt32(result, "sendButtonCount");
            var invalid = composerCount != 1 || stops > 1 || sends > 1;
            var empty = GetBoolean(result, "composerEmpty");
            var status = invalid ? WebAdapterStatus.AdapterInvalid
                : stops == 0 && sends == 1 && !GetBoolean(result, "sendButtonDisabled")
                    ? WebAdapterStatus.Succeeded : WebAdapterStatus.NotReady;
            return new(GetBoolean(result, "domainCorrect"), composerCount, stops, sends,
                GetBoolean(result, "sendButtonDisabled"), GetBoolean(result, "sendButtonInComposerForm"),
                GetCode(result), status, diagnostics, empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsAdapterException(exception))
        {
            return IdleFailure("idle-runtime-failed", WebAdapterStatus.AdapterInvalid);
        }
    }

    public Task<SubmissionResult> SubmitAsync(CancellationToken cancellationToken = default) =>
        SubmitCoreAsync(PollAttempts, null, TimeSpan.Zero, null, cancellationToken);

    public Task<SubmissionResult> SubmitPreparedInputOnceAsync(long expectedAudioStateVersion,
        TimeSpan requiredSilentDuration, AdapterPageState expectedPageState,
        CancellationToken cancellationToken = default) =>
        SubmitCoreAsync(1, expectedAudioStateVersion, requiredSilentDuration, expectedPageState, cancellationToken);

    private async Task<SubmissionResult> SubmitCoreAsync(int pollAttempts, long? expectedAudioStateVersion,
        TimeSpan requiredSilentDuration, AdapterPageState? expectedPageState, CancellationToken cancellationToken)
    {
        var invocationAttempted = false;
        AdapterDiagnostics? diagnostics = null;
        try
        {
            if (!IsReady()) return new(false, false, false, "not-on-chatgpt", WebAdapterStatus.NotReady);
            if (_operationId is null) return new(false, false, false, "no-prepared-input", WebAdapterStatus.AdapterInvalid);
            var invoked = false;
            for (var attempt = 0; attempt < pollAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!AudioAllowsSubmit(expectedAudioStateVersion, requiredSilentDuration))
                {
                    return new(false, false, false, "audio-busy-before-submit", WebAdapterStatus.NotReady);
                }
                var control = await EvaluateActionAsync("control", expectedPageState: expectedPageState);
                diagnostics = ParseAdapterDiagnostics(control);
                if (IsPlatformBlocked(control)) return SubmissionFailure(control, false);
                if (GetCode(control) != "ok") return SubmissionFailure(control, false);
                var decision = GetBoolean(control, "uploading") ? SubmitControlDecision.Wait
                    : DetermineSubmitControlDecision(GetInt32(control, "sendButtonCount"),
                        GetBoolean(control, "sendDisabled"), GetInt32(control, "busyButtonCount"));
                if (decision == SubmitControlDecision.Fail)
                {
                    return new(false, false, false, "send-control-ambiguous", WebAdapterStatus.AdapterInvalid, diagnostics);
                }
                if (decision == SubmitControlDecision.Invoke)
                {
                    if (!AudioAllowsSubmit(expectedAudioStateVersion, requiredSilentDuration))
                    {
                        return new(false, false, false, "audio-changed-before-invoke", WebAdapterStatus.NotReady, diagnostics);
                    }
                    invocationAttempted = true;
                    var invocation = await EvaluateActionAsync("invoke", expectedPageState: expectedPageState);
                    invoked = GetBoolean(invocation, "invoked");
                    if (!invoked) return SubmissionFailure(invocation, false);
                    break;
                }
                await Task.Delay(PollIntervalMilliseconds, cancellationToken);
            }
            if (!invoked) return new(false, false, false, "send-not-ready", WebAdapterStatus.NotReady, diagnostics);

            for (var attempt = 0; attempt < PollAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var observed = await EvaluateActionAsync("post");
                if (IsPlatformBlocked(observed)) return SubmissionFailure(observed, true);
                if (GetCode(observed) is not ("submission-observed" or "submission-pending")) break;
                if (GetBoolean(observed, "consumed"))
                {
                    _imagePrepared = false;
                    return new(true, GetBoolean(observed, "composerCleared"),
                        GetBoolean(observed, "attachmentCleared"), "submission-observed",
                        WebAdapterStatus.Succeeded, ParseAdapterDiagnostics(observed));
                }
                await Task.Delay(PollIntervalMilliseconds, cancellationToken);
            }
            return UnknownSubmission(diagnostics);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && invocationAttempted)
        {
            return UnknownSubmission(diagnostics);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsAdapterException(exception))
        {
            return invocationAttempted ? UnknownSubmission(diagnostics)
                : new(false, false, false, "submit-runtime-failed", WebAdapterStatus.AdapterInvalid, diagnostics);
        }
    }

    private bool IsReady() => _session.IsInitialized && _session.IsOnChatGpt();

    private bool AudioAllowsSubmit(long? expectedVersion, TimeSpan requiredSilence)
    {
        var audio = _session.GetAudioSnapshot(DateTimeOffset.UtcNow);
        return audio.IsKnown && !audio.IsPlaying &&
            (expectedVersion is null || audio.Version == expectedVersion.Value && audio.SilentDuration >= requiredSilence);
    }

    private async Task<JsonElement> EvaluateActionAsync(string action, bool requireImage = false,
        string? message = null, string? imageBase64 = null, AdapterPageState? expectedPageState = null)
    {
        if (!IsReady()) throw new InvalidOperationException("The adapter left the allowed origin.");
        var request = JsonSerializer.Serialize(new { action, rules = _rules, operationId = _operationId,
            requireImage, message, imageBase64, expectedPageState = expectedPageState?.ToString() });
        using var response = await CallCdpAsync("Runtime.evaluate", new
        {
            expression = $"({ComposerScript.Value})({request})", returnByValue = true, awaitPromise = true,
        });
        return ParseRuntimeEvaluateResponse(response.RootElement);
    }

    private static bool IsAdapterException(Exception exception) =>
        exception is InvalidOperationException or JsonException or IOException or UnauthorizedAccessException or COMException;

    private static bool IsMessageValid(string message) =>
        !string.IsNullOrWhiteSpace(message) && message.Length <= MaximumMessageCharacters;

    private static bool IsPlatformBlocked(JsonElement result) =>
        GetBoolean(result, "quotaDetected") || GetBoolean(result, "platformErrorDetected");

    private static WebAdapterStatus PreparationFailureStatus(JsonElement result) =>
        GetBoolean(result, "quotaDetected") ? WebAdapterStatus.QuotaReached
        : GetBoolean(result, "platformErrorDetected") ? WebAdapterStatus.PlatformError
        : GetCode(result) is "composer-draft-present" or "composer-attachment-present" or "conversation-busy"
            ? WebAdapterStatus.NotReady : WebAdapterStatus.AdapterInvalid;

    private static InputPreparationResult ImageResult(JsonElement result, WebAdapterStatus status) =>
        new(GetBoolean(result, "fileSelected"), GetBoolean(result, "previewDetected"), false,
            GetCode(result), status, ParseAdapterDiagnostics(result));

    private static SubmissionResult SubmissionFailure(JsonElement result, bool invoked) =>
        new(invoked, false, false, GetCode(result), PreparationFailureStatus(result), ParseAdapterDiagnostics(result));

    private static SubmissionResult UnknownSubmission(AdapterDiagnostics? diagnostics) =>
        new(true, false, false, "submission-outcome-unknown", WebAdapterStatus.OutcomeUnknown, diagnostics);

    private static AdapterIdleProbeResult IdleFailure(string code, WebAdapterStatus status) =>
        new(false, 0, 0, 0, false, false, code, status,
            new(AdapterPageState.Unknown, 0, [], AdapterFailureStage.Readiness));

    public static bool HasExpectedPageState(AdapterPageState currentPageState, AdapterPageState? expectedPageState) =>
        expectedPageState is null || currentPageState == expectedPageState;

    public static WebAdapterStatus ClassifyPreparationProbe(bool imageAdded, bool textInserted,
        bool quotaDetected, bool platformErrorDetected) =>
        quotaDetected ? WebAdapterStatus.QuotaReached : platformErrorDetected ? WebAdapterStatus.PlatformError
            : imageAdded && textInserted ? WebAdapterStatus.Succeeded : WebAdapterStatus.AdapterInvalid;

    public static SubmitControlDecision DetermineSubmitControlDecision(int sendButtonCount,
        bool sendButtonDisabled, int busyButtonCount)
    {
        if (sendButtonCount is < 0 or > 1 || busyButtonCount is < 0 or > 1) return SubmitControlDecision.Fail;
        if (busyButtonCount == 1 || sendButtonCount == 1 && sendButtonDisabled) return SubmitControlDecision.Wait;
        return sendButtonCount == 1 ? SubmitControlDecision.Invoke : SubmitControlDecision.Fail;
    }
    public static JsonElement ParseRuntimeEvaluateResponse(string responseJson)
    {
        using var response = JsonDocument.Parse(responseJson);
        return ParseRuntimeEvaluateResponse(response.RootElement);
    }

    public static AdapterDiagnostics ParseAdapterDiagnostics(JsonElement submitResult)
    {
        if (!submitResult.TryGetProperty("diagnostics", out var diagnostics) ||
            diagnostics.ValueKind != JsonValueKind.Object)
        {
            return new(
                AdapterPageState.Unknown,
                0,
                [],
                AdapterFailureStage.RuntimeEvaluation);
        }

        var pageState = TryReadEnum(diagnostics, "pageState", AdapterPageState.Unknown);
        var failureStage = TryReadEnum(
            diagnostics,
            "failureStage",
            AdapterFailureStage.RuntimeEvaluation);
        var detectedButtonCount = diagnostics.TryGetProperty("detectedButtonCount", out var count) &&
            count.TryGetInt32(out var parsedCount) && parsedCount >= 0
                ? parsedCount
                : 0;
        var candidates = new List<AdapterButtonCandidate>();
        if (diagnostics.TryGetProperty("candidateButtons", out var candidateArray) &&
            candidateArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var candidate in candidateArray.EnumerateArray().Take(MaximumDiagnosticButtonCandidates))
            {
                if (candidate.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                candidates.Add(new AdapterButtonCandidate(
                    ReadBoundedString(candidate, "tagName", 32) ?? "button",
                    ReadBoundedInteger(candidate, "scopeDepth", 0, 32),
                    ReadBoundedString(candidate, "type", 32),
                    ReadBoundedString(candidate, "role", 32),
                    GetBoolean(candidate, "disabled"),
                    GetBoolean(candidate, "ariaDisabled"),
                    ReadBoundedString(candidate, "ariaLabel", 120, allowLocalizedText: true),
                    ReadBoundedString(candidate, "dataTestId", 120)));
            }
        }

        return new(pageState, detectedButtonCount, candidates, failureStage);
    }

    private static JsonElement ParseRuntimeEvaluateResponse(JsonElement response)
    {
        if (response.TryGetProperty("exceptionDetails", out _))
        {
            throw new InvalidOperationException("The page-side adapter probe failed.");
        }

        if (!response.TryGetProperty("result", out var remoteObject) ||
            remoteObject.ValueKind != JsonValueKind.Object ||
            !remoteObject.TryGetProperty("value", out var value))
        {
            throw new InvalidOperationException("The page-side adapter probe returned an unsupported response shape.");
        }

        return value.Clone();
    }

    private async Task<JsonDocument> CallCdpAsync(string method, object parameters)
    {
        var json = JsonSerializer.Serialize(parameters);
        var result = await _session.Core.CallDevToolsProtocolMethodAsync(method, json);
        return JsonDocument.Parse(result);
    }

    private static string GetCode(JsonElement element) =>
        element.TryGetProperty("code", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "unknown"
            : "unknown";

    private static bool GetBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;

    private static int GetInt32(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var result)
            ? result
            : -1;

    private static TEnum TryReadEnum<TEnum>(JsonElement element, string propertyName, TEnum fallback)
        where TEnum : struct, Enum =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String &&
        Enum.TryParse<TEnum>(value.GetString(), ignoreCase: false, out var parsed)
            ? parsed
            : fallback;

    private static string? ReadBoundedString(
        JsonElement element,
        string propertyName,
        int maximumLength,
        bool allowLocalizedText = false)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var result = value.GetString();
        if (string.IsNullOrWhiteSpace(result) || result.Length > maximumLength || result.Any(char.IsControl))
        {
            return null;
        }

        if (!allowLocalizedText && result.Any(
                character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '_' and not '-'))
        {
            return null;
        }

        return result;
    }

    private static int ReadBoundedInteger(
        JsonElement element,
        string propertyName,
        int minimum,
        int maximum) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.TryGetInt32(out var result) && result >= minimum && result <= maximum
            ? result
            : minimum;
}
