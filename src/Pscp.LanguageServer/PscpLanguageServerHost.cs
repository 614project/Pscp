using System.Globalization;
using System.Threading.Channels;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pscp.Transpiler;

namespace Pscp.LanguageServer;

public static class PscpLanguageServerHost
{
    public static Task<int> RunConsoleAsync(CancellationToken cancellationToken = default)
        => new Session(Console.OpenStandardInput(), Console.OpenStandardOutput(), Console.Error).RunAsync(cancellationToken);

    private sealed class Session
    {
        private static readonly string[] SemanticTokenTypes =
        [
            "namespace",
            "type",
            "class",
            "enum",
            "interface",
            "struct",
            "typeParameter",
            "parameter",
            "variable",
            "property",
            "enumMember",
            "event",
            "function",
            "method",
            "macro",
            "keyword",
            "modifier",
            "comment",
            "string",
            "number",
            "regexp",
            "operator",
        ];

        private static readonly string[] SemanticTokenModifiers =
        [
            "declaration",
            "definition",
            "readonly",
            "static",
            "deprecated",
            "abstract",
            "async",
            "modification",
            "documentation",
            "defaultLibrary",
            // PSCP-only modifiers (guide §11.2). The extension declares them in
            // `contributes.semanticTokenModifiers` so themes can name them.
            "mutable",
            "intrinsic",
            "shorthand",
            "rewrite",
        ];

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = null,
            WriteIndented = false,
        };

        private readonly Stream _input;
        private readonly Stream _output;
        private readonly TextWriter _log;
        private readonly DocumentStore _documents = new();
        private readonly Dictionary<Uri, PscpAnalysisResult> _analyses = new();

        // Documents whose text changed and whose analysis has not caught up yet (guide §5.4 rule 2).
        private readonly HashSet<Uri> _dirty = [];

        // Documents a request analysed ahead of schedule, with how long it took; their diagnostics and status
        // still go out once the batch ends.
        private readonly Dictionary<Uri, long> _pendingDiagnostics = [];
        private readonly PscpAnalyzer _analyzer = new();
        private bool _shutdownRequested;

        // Server feature settings the client pushes (guide §14.7, appendix C).
        private bool _inferredTypeHints = true;
        private bool _rewriteResultHints = true;
        private bool _accumulatorTypeHints = true;
        private bool _parameterNameHints;
        private bool _loweringDiagnostics;

        // Client capabilities (guide §12). A client that sends no capabilities gets the plain forms.
        private bool _supportsSnippets;
        private bool _supportsLabelDetails;
        private bool _supportsHierarchicalSymbols = true;

        public Session(Stream input, Stream output, TextWriter log)
        {
            _input = input;
            _output = output;
            _log = log;
        }

        // Guide §5.4: messages are handled in the order they arrived, but everything already waiting is read
        // first. That batch is what makes the two rules the guide adds possible: a `$/cancelRequest` for a
        // request still in the batch is honoured, and a run of `didChange` on one document costs one analysis
        // instead of one each.
        public async Task<int> RunAsync(CancellationToken cancellationToken)
        {
            // A plain unbounded channel: the `SingleReader` variant does not support `Count`, which the drain
            // loop below needs to tell whether more messages are already waiting.
            Channel<JsonDocument?> incoming = Channel.CreateUnbounded<JsonDocument?>(new UnboundedChannelOptions
            {
                SingleWriter = true,
            });
            Task reader = ReadLoopAsync(incoming.Writer, cancellationToken);
            List<JsonDocument> batch = [];
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (!await incoming.Reader.WaitToReadAsync(cancellationToken))
                    {
                        break;
                    }

                    bool endOfInput = false;
                    bool shouldExit = false;

                    // Everything waiting is handled before anything is analysed, and a message that arrives
                    // while the batch is being handled joins the next pass. A burst of keystrokes therefore
                    // costs one analysis however the stream happens to be chunked.
                    do
                    {
                        while (incoming.Reader.TryRead(out JsonDocument? message))
                        {
                            if (message is null)
                            {
                                endOfInput = true;
                                break;
                            }

                            batch.Add(message);
                        }

                        shouldExit = await HandleBatchAsync(batch, cancellationToken);
                        foreach (JsonDocument message in batch)
                        {
                            message.Dispose();
                        }

                        batch.Clear();
                    }
                    while (!shouldExit && !endOfInput && incoming.Reader.Count > 0);

                    if (shouldExit || endOfInput)
                    {
                        break;
                    }

                    // Rule 2: the analysis of a changed document runs once the batch is drained.
                    await AnalyzeDirtyDocumentsAsync(cancellationToken);
                }
            }
            finally
            {
                foreach (JsonDocument message in batch)
                {
                    message.Dispose();
                }

                await reader.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None).ContinueWith(
                    _ => { },
                    TaskScheduler.Default);
            }

            return _shutdownRequested ? 0 : 1;
        }

        private async Task ReadLoopAsync(ChannelWriter<JsonDocument?> writer, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    JsonDocument? message;
                    try
                    {
                        message = await ReadMessageAsync(_input, cancellationToken);
                    }
                    catch (JsonException ex)
                    {
                        // The whole body was consumed, so the stream is still in sync: report and keep serving.
                        await _log.WriteLineAsync($"Malformed JSON-RPC message: {ex.Message}");
                        await WriteMessageAsync(
                            _output,
                            new JsonObject
                            {
                                ["jsonrpc"] = "2.0",
                                ["id"] = null,
                                ["error"] = new JsonObject
                                {
                                    ["code"] = -32700,
                                    ["message"] = "Parse error.",
                                },
                            },
                            cancellationToken);
                        continue;
                    }

                    await writer.WriteAsync(message, cancellationToken);
                    if (message is null)
                    {
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // The session is shutting down.
            }
            finally
            {
                writer.TryComplete();
            }
        }

        private async Task<bool> HandleBatchAsync(List<JsonDocument> batch, CancellationToken cancellationToken)
        {
            // Rule 4: a request cancelled anywhere in this batch is answered with RequestCancelled and the
            // work is never done.
            HashSet<string> cancelled = [];
            foreach (JsonDocument message in batch)
            {
                if (message.RootElement.TryGetProperty("method", out JsonElement method)
                    && method.ValueKind == JsonValueKind.String
                    && method.GetString() == "$/cancelRequest"
                    && message.RootElement.TryGetProperty("params", out JsonElement @params)
                    && @params.TryGetProperty("id", out JsonElement id))
                {
                    cancelled.Add(RequestKey(id));
                }
            }

            foreach (JsonDocument message in batch)
            {
                if (cancelled.Count > 0
                    && message.RootElement.TryGetProperty("id", out JsonElement id)
                    && message.RootElement.TryGetProperty("method", out JsonElement method)
                    && method.ValueKind == JsonValueKind.String
                    && method.GetString() != "$/cancelRequest"
                    && cancelled.Contains(RequestKey(id)))
                {
                    await SendErrorAsync(id, -32800, "Request cancelled.", cancellationToken);
                    continue;
                }

                if (await HandleMessageAsync(message.RootElement, cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }

        private static string RequestKey(JsonElement id)
            => id.ValueKind == JsonValueKind.String ? "s:" + id.GetString() : "n:" + id.ToString();

        // Guide §13.3: the client learns when an analysis starts and finishes, so it can show progress only
        // for an analysis that actually takes a while.
        private async Task AnalyzeDirtyDocumentsAsync(CancellationToken cancellationToken)
        {
            while (_pendingDiagnostics.Count > 0)
            {
                (Uri analysed, long elapsedMs) = _pendingDiagnostics.First();
                _pendingDiagnostics.Remove(analysed);
                if (_analyses.TryGetValue(analysed, out PscpAnalysisResult? ready))
                {
                    // The analysis already ran, to answer a request: the pair still goes out so the client
                    // sees the same shape, with a window too short to show progress for.
                    await SendStatusAsync("analyzing", ready.Snapshot, null, null, cancellationToken);
                    await PublishDiagnosticsAsync(ready, cancellationToken);
                    await SendStatusAsync("idle", ready.Snapshot, elapsedMs, null, cancellationToken);
                }
            }

            while (_dirty.Count > 0)
            {
                Uri uri = _dirty.First();
                _dirty.Remove(uri);
                if (!_documents.TryGet(uri, out DocumentSnapshot? snapshot) || snapshot is null)
                {
                    continue;
                }

                await SendStatusAsync("analyzing", snapshot, null, null, cancellationToken);
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    PscpAnalysisResult analysis = _analyzer.Analyze(snapshot);
                    // Rule 5 and 6: a newer version arrived while this ran, so its result is the one to keep.
                    if (_documents.TryGet(uri, out DocumentSnapshot? latest) && latest is not null && latest.Version > snapshot.Version)
                    {
                        _dirty.Add(uri);
                        continue;
                    }

                    _analyses[uri] = analysis;
                    _pendingDiagnostics.Remove(uri);
                    await PublishDiagnosticsAsync(analysis, cancellationToken);
                    await SendStatusAsync("idle", snapshot, watch.ElapsedMilliseconds, null, cancellationToken);
                }
                catch (Exception ex)
                {
                    await _log.WriteLineAsync(ex.ToString());
                    await SendStatusAsync("error", snapshot, watch.ElapsedMilliseconds, ex.Message, cancellationToken);
                }
            }
        }

        private Task SendStatusAsync(string state, DocumentSnapshot snapshot, long? analysisMs, string? message, CancellationToken cancellationToken)
        {
            JsonObject payload = new()
            {
                ["state"] = state,
                ["uri"] = snapshot.Uri.ToString(),
                ["version"] = snapshot.Version,
            };
            if (analysisMs is not null)
            {
                payload["analysisMs"] = analysisMs;
            }

            if (message is not null)
            {
                payload["message"] = message;
            }

            return SendNotificationAsync("pscp/status", payload, cancellationToken);
        }

        private async Task<bool> HandleMessageAsync(JsonElement message, CancellationToken cancellationToken)
        {
            JsonElement id = default;
            bool hasId = message.TryGetProperty("id", out id);
            string? method = message.TryGetProperty("method", out JsonElement methodElement) ? methodElement.GetString() : null;
            JsonElement @params = message.TryGetProperty("params", out JsonElement paramsElement) ? paramsElement : default;

            if (string.IsNullOrWhiteSpace(method))
            {
                if (hasId)
                {
                    await SendErrorAsync(id, -32600, "Invalid request.", cancellationToken);
                }

                return false;
            }

            try
            {
                switch (method)
                {
                    case "initialize":
                        ReadClientCapabilities(@params);
                        ReadFeatureSettings(@params.TryGetProperty("initializationOptions", out JsonElement options) ? options : default);
                        if (hasId)
                        {
                            await SendResultAsync(id, CreateInitializeResult(), cancellationToken);
                        }

                        return false;
                    case "initialized":
                    case "$/cancelRequest":
                        return false;
                    case "workspace/didChangeConfiguration":
                        ReadFeatureSettings(@params.TryGetProperty("settings", out JsonElement settings) ? settings : default);
                        return false;
                    case "shutdown":
                        _shutdownRequested = true;
                        if (hasId)
                        {
                            await SendResultAsync(id, null, cancellationToken);
                        }

                        return false;
                    case "exit":
                        return true;
                    case "textDocument/didOpen":
                        await HandleDidOpenAsync(@params, cancellationToken);
                        return false;
                    case "textDocument/didChange":
                        await HandleDidChangeAsync(@params, cancellationToken);
                        return false;
                    case "textDocument/didClose":
                        await HandleDidCloseAsync(@params, cancellationToken);
                        return false;
                    case "textDocument/hover":
                        await SendResultAsync(id, HandleHover(@params), cancellationToken);
                        return false;
                    case "textDocument/completion":
                        await SendResultAsync(id, HandleCompletion(@params), cancellationToken);
                        return false;
                    case "textDocument/definition":
                        await SendResultAsync(id, HandleDefinition(@params), cancellationToken);
                        return false;
                    case "textDocument/references":
                        await SendResultAsync(id, HandleReferences(@params), cancellationToken);
                        return false;
                    case "textDocument/documentSymbol":
                        await SendResultAsync(id, HandleDocumentSymbols(@params), cancellationToken);
                        return false;
                    case "textDocument/semanticTokens/full":
                    case "textDocument/semanticTokens/range":
                        await SendResultAsync(id, HandleSemanticTokens(@params), cancellationToken);
                        return false;
                    case "textDocument/signatureHelp":
                        await SendResultAsync(id, HandleSignatureHelp(@params), cancellationToken);
                        return false;
                    case "textDocument/inlayHint":
                        await SendResultAsync(id, HandleInlayHints(@params), cancellationToken);
                        return false;
                    case "textDocument/rename":
                    case "textDocument/prepareRename":
                    {
                        // Guide §10.3: a refusal is an error response carrying the reason, so the editor can
                        // show it instead of silently doing nothing.
                        JsonNode? result = method == "textDocument/rename"
                            ? HandleRename(@params, out string? refusal)
                            : HandlePrepareRename(@params, out refusal);
                        if (refusal is not null)
                        {
                            await SendErrorAsync(id, -32602, refusal, cancellationToken);
                        }
                        else
                        {
                            await SendResultAsync(id, result, cancellationToken);
                        }

                        return false;
                    }
                    case "textDocument/codeAction":
                        await SendResultAsync(id, HandleCodeAction(@params), cancellationToken);
                        return false;
                    case "textDocument/foldingRange":
                        await SendResultAsync(id, HandleFoldingRange(@params), cancellationToken);
                        return false;
                    case "textDocument/selectionRange":
                        await SendResultAsync(id, HandleSelectionRange(@params), cancellationToken);
                        return false;
                    case "textDocument/documentHighlight":
                        await SendResultAsync(id, HandleDocumentHighlight(@params), cancellationToken);
                        return false;
                    case "pscp/generatedCSharp":
                        await SendResultAsync(id, HandleGeneratedCSharp(@params), cancellationToken);
                        return false;
                    default:
                        if (hasId)
                        {
                            await SendErrorAsync(id, -32601, $"Method not found: {method}", cancellationToken);
                        }

                        return false;
                }
            }
            catch (Exception ex)
            {
                await _log.WriteLineAsync(ex.ToString());
                if (hasId)
                {
                    await SendErrorAsync(id, -32603, ex.Message, cancellationToken);
                }

                return false;
            }
        }

        // The client decides whether snippets, markdown documentation and hierarchical document symbols are
        // usable (guide §12): the server sends only what the client asked for.
        private void ReadClientCapabilities(JsonElement @params)
        {
            if (@params.ValueKind != JsonValueKind.Object
                || !@params.TryGetProperty("capabilities", out JsonElement capabilities)
                || capabilities.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (capabilities.TryGetProperty("textDocument", out JsonElement textDocument) && textDocument.ValueKind == JsonValueKind.Object)
            {
                if (textDocument.TryGetProperty("completion", out JsonElement completion)
                    && completion.TryGetProperty("completionItem", out JsonElement completionItem)
                    && completionItem.ValueKind == JsonValueKind.Object)
                {
                    _supportsSnippets = completionItem.TryGetProperty("snippetSupport", out JsonElement snippet)
                        && snippet.ValueKind == JsonValueKind.True;
                    _supportsLabelDetails = completionItem.TryGetProperty("labelDetailsSupport", out JsonElement labelDetails)
                        && labelDetails.ValueKind == JsonValueKind.True;
                }

                if (textDocument.TryGetProperty("documentSymbol", out JsonElement documentSymbol)
                    && documentSymbol.TryGetProperty("hierarchicalDocumentSymbolSupport", out JsonElement hierarchical))
                {
                    _supportsHierarchicalSymbols = hierarchical.ValueKind == JsonValueKind.True;
                }
            }
        }

        // Guide §14.7: the settings arrive as `initializationOptions` at startup and through
        // `workspace/didChangeConfiguration` afterwards, and never restart the server.
        private void ReadFeatureSettings(JsonElement settings)
        {
            if (settings.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            // The client may send the `pscp` section or its contents.
            JsonElement root = settings.TryGetProperty("pscp", out JsonElement nested) ? nested : settings;
            if (root.TryGetProperty("inlayHints", out JsonElement inlayHints) && inlayHints.ValueKind == JsonValueKind.Object)
            {
                _inferredTypeHints = ReadFlag(inlayHints, "inferredTypes", _inferredTypeHints);
                _rewriteResultHints = ReadFlag(inlayHints, "rewriteResults", _rewriteResultHints);
                _accumulatorTypeHints = ReadFlag(inlayHints, "accumulatorTypes", _accumulatorTypeHints);
                _parameterNameHints = ReadFlag(inlayHints, "parameterNames", _parameterNameHints);
            }

            if (root.TryGetProperty("hints", out JsonElement hints) && hints.ValueKind == JsonValueKind.Object)
            {
                _loweringDiagnostics = ReadFlag(hints, "loweringDiagnostics", _loweringDiagnostics);
            }
        }

        private static bool ReadFlag(JsonElement owner, string name, bool fallback)
            => owner.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.ValueKind == JsonValueKind.True
                : fallback;

        private JsonObject CreateInitializeResult()
        {
            return new JsonObject
            {
                ["serverInfo"] = new JsonObject
                {
                    ["name"] = "pscp-language-server",
                    ["version"] = PscpVersionInfo.ToolVersion,
                },
                ["capabilities"] = new JsonObject
                {
                    ["positionEncoding"] = "utf-16",
                    ["textDocumentSync"] = new JsonObject
                    {
                        ["openClose"] = true,
                        ["change"] = 1,
                    },
                    ["hoverProvider"] = true,
                    ["definitionProvider"] = true,
                    ["referencesProvider"] = true,
                    ["documentSymbolProvider"] = true,
                    ["foldingRangeProvider"] = true,
                    ["selectionRangeProvider"] = true,
                    ["documentHighlightProvider"] = true,
                    ["renameProvider"] = true,
                    ["prepareRenameProvider"] = true,
                    ["codeActionProvider"] = new JsonObject
                    {
                        ["codeActionKinds"] = new JsonArray("quickfix"),
                    },
                    ["inlayHintProvider"] = true,
                    ["completionProvider"] = new JsonObject
                    {
                        ["resolveProvider"] = false,
                        ["triggerCharacters"] = new JsonArray("."),
                    },
                    ["signatureHelpProvider"] = new JsonObject
                    {
                        // Guide §9.2: a space is a trigger too, because of space-call.
                        ["triggerCharacters"] = new JsonArray("(", ",", " "),
                        ["retriggerCharacters"] = new JsonArray(",", " "),
                    },
                    ["semanticTokensProvider"] = new JsonObject
                    {
                        ["legend"] = new JsonObject
                        {
                            ["tokenTypes"] = ToJsonArray(SemanticTokenTypes),
                            ["tokenModifiers"] = ToJsonArray(SemanticTokenModifiers),
                        },
                        ["full"] = true,
                        ["range"] = true,
                    },
                    // Guide §4.2: PSCP-specific protocol and versions.
                    ["experimental"] = new JsonObject
                    {
                        ["pscp"] = new JsonObject
                        {
                            ["protocolVersion"] = 1,
                            ["languageVersion"] = PscpVersionInfo.LanguageVersion,
                            ["toolVersion"] = PscpVersionInfo.ToolVersion,
                            ["requests"] = new JsonArray("pscp/generatedCSharp"),
                            ["notifications"] = new JsonArray("pscp/status"),
                        },
                    },
                },
            };
        }

        private async Task HandleDidOpenAsync(JsonElement @params, CancellationToken cancellationToken)
        {
            JsonElement textDocument = @params.GetProperty("textDocument");
            Uri uri = new(textDocument.GetProperty("uri").GetString()!, UriKind.Absolute);
            string text = textDocument.GetProperty("text").GetString() ?? string.Empty;
            int version = textDocument.TryGetProperty("version", out JsonElement versionElement) ? versionElement.GetInt32() : 0;
            _documents.Open(uri, text, version);
            // Guide §5.4: an open is scheduled like a change, so both report `pscp/status` the same way.
            _analyses.Remove(uri);
            _dirty.Add(uri);
            await Task.CompletedTask;
        }

        private async Task HandleDidChangeAsync(JsonElement @params, CancellationToken cancellationToken)
        {
            JsonElement textDocument = @params.GetProperty("textDocument");
            Uri uri = new(textDocument.GetProperty("uri").GetString()!, UriKind.Absolute);
            int version = textDocument.TryGetProperty("version", out JsonElement versionElement) ? versionElement.GetInt32() : 0;
            string currentText = _documents.TryGet(uri, out DocumentSnapshot? existing) && existing is not null
                ? existing.Text
                : TryReadLocalFile(uri);
            string updatedText = ApplyTextChanges(currentText, @params.GetProperty("contentChanges"));
            _documents.Change(uri, version, updatedText);
            // Guide §5.4 rule 2: the text is current immediately, the analysis is only scheduled.
            _analyses.Remove(uri);
            _dirty.Add(uri);
            await Task.CompletedTask;
        }

        private async Task HandleDidCloseAsync(JsonElement @params, CancellationToken cancellationToken)
        {
            JsonElement textDocument = @params.GetProperty("textDocument");
            Uri uri = new(textDocument.GetProperty("uri").GetString()!, UriKind.Absolute);
            _documents.Close(uri, out _);
            _analyses.Remove(uri);
            _dirty.Remove(uri);
            await SendNotificationAsync(
                "textDocument/publishDiagnostics",
                new JsonObject
                {
                    ["uri"] = uri.ToString(),
                    ["diagnostics"] = new JsonArray(),
                },
                cancellationToken);
        }

        private JsonNode? HandleHover(JsonElement @params)
        {
            if (!TryGetAnalysisAndOffset(@params, out PscpAnalysisResult? analysis, out int offset))
            {
                return null;
            }

            int tokenIndex = FindTokenIndexAtOffset(analysis!, offset);
            if (tokenIndex < 0)
            {
                return null;
            }

            if (!analysis!.HoverByTokenIndex.TryGetValue(tokenIndex, out PscpHoverEntry? hover))
            {
                return null;
            }

            return new JsonObject
            {
                ["contents"] = new JsonObject
                {
                    ["kind"] = "markdown",
                    ["value"] = hover.Markdown,
                },
                ["range"] = ToRange(analysis.Snapshot.LineIndex, analysis.Tokens[tokenIndex].Span),
            };
        }

        private JsonNode HandleCompletion(JsonElement @params)
        {
            if (!TryGetAnalysisAndOffset(@params, out PscpAnalysisResult? analysis, out int offset))
            {
                return new JsonObject
                {
                    ["isIncomplete"] = false,
                    ["items"] = new JsonArray(),
                };
            }

            List<PscpCompletionEntry> entries = BuildCompletionItems(analysis!, offset);
            JsonArray items = new();
            foreach (PscpCompletionEntry entry in entries.OrderBy(item => item.SortText ?? item.Label, StringComparer.Ordinal).ThenBy(item => item.Label, StringComparer.Ordinal))
            {
                JsonObject? labelDetails = !_supportsLabelDetails || (entry.LabelDetail is null && entry.LabelDescription is null)
                    ? null
                    : new JsonObject();
                if (labelDetails is not null)
                {
                    if (entry.LabelDetail is not null)
                    {
                        labelDetails["detail"] = entry.LabelDetail;
                    }

                    if (entry.LabelDescription is not null)
                    {
                        labelDetails["description"] = entry.LabelDescription;
                    }
                }

                // Guide §8.4: a snippet goes out only when the client asked for one.
                bool snippet = entry.InsertTextFormat == 2;
                string? insertText = snippet && !_supportsSnippets ? null : entry.InsertText;
                items.Add(new JsonObject
                {
                    ["label"] = entry.Label,
                    ["labelDetails"] = labelDetails,
                    ["kind"] = entry.Kind,
                    ["detail"] = entry.Detail,
                    ["documentation"] = entry.Documentation is null
                        ? null
                        : new JsonObject
                        {
                            ["kind"] = "markdown",
                            ["value"] = entry.Documentation,
                        },
                    ["insertText"] = insertText,
                    ["insertTextFormat"] = insertText is null ? null : entry.InsertTextFormat,
                    ["sortText"] = entry.SortText ?? entry.Label,
                });
            }

            return new JsonObject
            {
                ["isIncomplete"] = false,
                ["items"] = items,
            };
        }

        private JsonNode? HandleDefinition(JsonElement @params)
        {
            if (!TryResolveSymbol(@params, out PscpAnalysisResult? analysis, out PscpServerSymbol? symbol))
            {
                return null;
            }

            if (symbol!.IsIntrinsic)
            {
                return null;
            }

            return ToLocation(analysis!.Snapshot.Uri, analysis.Snapshot.LineIndex, symbol.SelectionSpan);
        }

        private JsonNode HandleReferences(JsonElement @params)
        {
            bool includeDeclaration = @params.TryGetProperty("context", out JsonElement context)
                && context.TryGetProperty("includeDeclaration", out JsonElement include)
                && include.GetBoolean();

            if (!TryResolveSymbol(@params, out PscpAnalysisResult? analysis, out PscpServerSymbol? symbol))
            {
                return new JsonArray();
            }

            JsonArray results = new();
            foreach (PscpServerReference reference in analysis!.References.Where(reference => reference.SymbolId == symbol!.Id))
            {
                if (!includeDeclaration && reference.IsDeclaration)
                {
                    continue;
                }

                results.Add(ToLocation(analysis.Snapshot.Uri, analysis.Snapshot.LineIndex, reference.Span));
            }

            return results;
        }

        // Guide §10.5.
        private JsonNode HandleFoldingRange(JsonElement @params)
        {
            if (!TryGetAnalysis(@params, out PscpAnalysisResult? analysis))
            {
                return new JsonArray();
            }

            JsonArray results = new();
            foreach (PscpFoldingRange range in PscpNavigation.ComputeFoldingRanges(analysis!))
            {
                JsonObject item = new()
                {
                    ["startLine"] = range.StartLine,
                    ["startCharacter"] = range.StartCharacter,
                    ["endLine"] = range.EndLine,
                    ["endCharacter"] = range.EndCharacter,
                };
                if (range.Kind is not null)
                {
                    item["kind"] = range.Kind;
                }

                results.Add(item);
            }

            return results;
        }

        // Guide §10.5: the result is one chain per requested position, linked by `parent`.
        private JsonNode HandleSelectionRange(JsonElement @params)
        {
            if (!TryGetAnalysis(@params, out PscpAnalysisResult? analysis)
                || !@params.TryGetProperty("positions", out JsonElement positions)
                || positions.ValueKind != JsonValueKind.Array)
            {
                return new JsonArray();
            }

            JsonArray results = new();
            foreach (JsonElement position in positions.EnumerateArray())
            {
                int offset = analysis!.Snapshot.LineIndex.GetOffset(
                    position.GetProperty("line").GetInt32(),
                    position.GetProperty("character").GetInt32());
                IReadOnlyList<TextSpan> chain = PscpNavigation.ComputeSelectionRange(analysis, offset);
                JsonObject? head = null;
                for (int i = chain.Count - 1; i >= 0; i--)
                {
                    JsonObject node = new() { ["range"] = ToRange(analysis.Snapshot.LineIndex, chain[i]) };
                    if (head is not null)
                    {
                        node["parent"] = head;
                    }

                    head = node;
                }

                results.Add(head ?? new JsonObject { ["range"] = ToRange(analysis!.Snapshot.LineIndex, new TextSpan(offset, 0)) });
            }

            return results;
        }

        // Guide §10.2: a read is Read(2) and a write is Write(3).
        private JsonNode HandleDocumentHighlight(JsonElement @params)
        {
            if (!TryResolveSymbol(@params, out PscpAnalysisResult? analysis, out PscpServerSymbol? symbol))
            {
                return new JsonArray();
            }

            JsonArray results = new();
            foreach (PscpServerReference reference in analysis!.References.Where(reference => reference.SymbolId == symbol!.Id))
            {
                results.Add(new JsonObject
                {
                    ["range"] = ToRange(analysis.Snapshot.LineIndex, reference.Span),
                    ["kind"] = reference.IsWrite || reference.IsDeclaration ? 3 : 2,
                });
            }

            return results;
        }

        private JsonNode HandleDocumentSymbols(JsonElement @params)
        {
            if (!TryGetAnalysis(@params, out PscpAnalysisResult? analysis))
            {
                return new JsonArray();
            }

            JsonArray results = new();
            if (_supportsHierarchicalSymbols)
            {
                foreach (PscpDocumentSymbol symbol in analysis!.DocumentSymbols)
                {
                    results.Add(ToDocumentSymbol(analysis, symbol));
                }

                return results;
            }

            // A client without hierarchical support gets the flattened SymbolInformation form, where the
            // nesting survives as `containerName`.
            void Flatten(PscpDocumentSymbol symbol, string? container)
            {
                results.Add(new JsonObject
                {
                    ["name"] = symbol.Name,
                    ["kind"] = symbol.Kind,
                    ["location"] = new JsonObject
                    {
                        ["uri"] = analysis!.Snapshot.Uri.ToString(),
                        ["range"] = ToRange(analysis.Snapshot.LineIndex, symbol.Range),
                    },
                    ["containerName"] = container,
                });
                foreach (PscpDocumentSymbol child in symbol.Children)
                {
                    Flatten(child, symbol.Name);
                }
            }

            foreach (PscpDocumentSymbol symbol in analysis!.DocumentSymbols)
            {
                Flatten(symbol, null);
            }

            return results;
        }

        private static JsonObject ToDocumentSymbol(PscpAnalysisResult analysis, PscpDocumentSymbol symbol)
        {
            JsonArray children = new();
            foreach (PscpDocumentSymbol child in symbol.Children)
            {
                children.Add(ToDocumentSymbol(analysis, child));
            }

            return new JsonObject
            {
                ["name"] = symbol.Name,
                ["detail"] = symbol.Detail,
                ["kind"] = symbol.Kind,
                ["range"] = ToRange(analysis.Snapshot.LineIndex, symbol.Range),
                ["selectionRange"] = ToRange(analysis.Snapshot.LineIndex, symbol.SelectionRange),
                ["children"] = children,
            };
        }

        private bool IsHintEnabled(PscpInlayHintKind kind)
            => kind switch
            {
                PscpInlayHintKind.InferredType => _inferredTypeHints,
                PscpInlayHintKind.RewriteResult => _rewriteResultHints,
                PscpInlayHintKind.AccumulatorType => _accumulatorTypeHints,
                _ => _parameterNameHints,
            };

        private static (int Start, int End) ReadRange(JsonElement @params, PscpAnalysisResult analysis)
        {
            if (@params.ValueKind != JsonValueKind.Object || !@params.TryGetProperty("range", out JsonElement range))
            {
                return (0, int.MaxValue);
            }

            LineIndex lineIndex = analysis.Snapshot.LineIndex;
            return (
                lineIndex.GetOffset(range.GetProperty("start").GetProperty("line").GetInt32(), range.GetProperty("start").GetProperty("character").GetInt32()),
                lineIndex.GetOffset(range.GetProperty("end").GetProperty("line").GetInt32(), range.GetProperty("end").GetProperty("character").GetInt32()));
        }

        private JsonNode HandleSemanticTokens(JsonElement @params)
        {
            if (!TryGetAnalysis(@params, out PscpAnalysisResult? analysis))
            {
                return new JsonObject { ["data"] = new JsonArray() };
            }

            Dictionary<string, int> tokenTypeIndex = SemanticTokenTypes
                .Select((value, index) => (value, index))
                .ToDictionary(pair => pair.value, pair => pair.index, StringComparer.Ordinal);
            Dictionary<string, int> modifierIndex = SemanticTokenModifiers
                .Select((value, index) => (value, index))
                .ToDictionary(pair => pair.value, pair => pair.index, StringComparer.Ordinal);

            // `semanticTokens/range` asks for one viewport's worth of tokens (guide §17, P3).
            (int rangeStart, int rangeEnd) = ReadRange(@params, analysis!);

            JsonArray data = new();
            int previousLine = 0;
            int previousChar = 0;

            foreach (SemanticTokenClassification token in analysis!.SemanticTokens
                .Where(token => token.Span.End >= rangeStart && token.Span.Start <= rangeEnd)
                .OrderBy(token => token.Span.Start))
            {
                (int line, int character) = analysis.Snapshot.LineIndex.GetPosition(token.Span.Start);
                int deltaLine = line - previousLine;
                int deltaStart = deltaLine == 0 ? character - previousChar : character;
                int length = Math.Max(1, token.Span.Length);
                int encodedType = tokenTypeIndex.TryGetValue(token.TokenType, out int typeIndex) ? typeIndex : tokenTypeIndex["variable"];
                int encodedModifiers = 0;
                foreach (string modifier in token.Modifiers)
                {
                    if (modifierIndex.TryGetValue(modifier, out int modifierBit))
                    {
                        encodedModifiers |= 1 << modifierBit;
                    }
                }

                data.Add(deltaLine);
                data.Add(deltaStart);
                data.Add(length);
                data.Add(encodedType);
                data.Add(encodedModifiers);

                previousLine = line;
                previousChar = character;
            }

            return new JsonObject { ["data"] = data };
        }

        private JsonNode? HandleSignatureHelp(JsonElement @params)
        {
            if (!TryGetAnalysisAndOffset(@params, out PscpAnalysisResult? analysis, out int offset))
            {
                return null;
            }

            // A parenthesised call wins; otherwise the cursor may be inside a space-call (guide §9.1).
            if (!TryFindSignatureContext(analysis!, offset, out string? signatureKey, out int activeParameter)
                && !PscpSignatures.TryFindSpaceCall(analysis!, offset, out signatureKey, out activeParameter))
            {
                return null;
            }

            if (!TryResolveSignature(analysis!, signatureKey!, out PscpSignatureEntry? signature))
            {
                return null;
            }

            JsonArray signatures = new();
            foreach (PscpSignatureForm form in signature!.Forms)
            {
                JsonArray parameters = new();
                foreach (PscpSignatureParameter parameter in form.Parameters)
                {
                    // Guide §9.4: the label is an offset pair into the signature, so the editor highlights the
                    // right span even when a parameter name appears twice.
                    parameters.Add(new JsonObject
                    {
                        ["label"] = new JsonArray(parameter.Start, parameter.End),
                    });
                }

                signatures.Add(new JsonObject
                {
                    ["label"] = form.Label,
                    ["documentation"] = signature.Documentation,
                    ["parameters"] = parameters,
                });
            }

            int activeSignature = PscpSignatures.ChooseOverload(signature.Forms, activeParameter + 1);
            PscpSignatureForm active = signature.Forms[activeSignature];
            return new JsonObject
            {
                ["signatures"] = signatures,
                ["activeSignature"] = activeSignature,
                ["activeParameter"] = Math.Clamp(activeParameter, 0, Math.Max(0, active.Parameters.Count - 1)),
            };
        }

        private static bool TryResolveSignature(PscpAnalysisResult analysis, string key, out PscpSignatureEntry? signature)
            => analysis.Signatures.TryGetValue(key, out signature)
                || analysis.Signatures.TryGetValue(GetLastSignatureSegment(key), out signature);

        private JsonNode HandleInlayHints(JsonElement @params)
        {
            if (!TryGetAnalysis(@params, out PscpAnalysisResult? analysis))
            {
                return new JsonArray();
            }

            // Guide §12.1: only the hints inside the requested range.
            (int rangeStart, int rangeEnd) = ReadRange(@params, analysis!);

            JsonArray hints = new();
            foreach (PscpInlayHintEntry hint in analysis!.InlayHints)
            {
                if (hint.Span.End < rangeStart || hint.Span.End > rangeEnd || !IsHintEnabled(hint.Kind))
                {
                    continue;
                }

                JsonObject item = new()
                {
                    ["position"] = ToPosition(analysis.Snapshot.LineIndex, hint.Span.End),
                    ["label"] = hint.Label,
                    // A parameter name is InlayHintKind.Parameter (2); everything else describes a type (1).
                    ["kind"] = hint.Kind == PscpInlayHintKind.ParameterName ? 2 : 1,
                    ["paddingLeft"] = true,
                };
                if (hint.Tooltip is not null)
                {
                    // Guide §12.1: a hint explains itself with a tooltip.
                    item["tooltip"] = new JsonObject
                    {
                        ["kind"] = "markdown",
                        ["value"] = hint.Tooltip,
                    };
                }

                hints.Add(item);
            }

            return hints;
        }

        private JsonNode? HandleRename(JsonElement @params, out string? refusal)
        {
            if (!TryPrepareRename(@params, out PscpAnalysisResult? analysis, out PscpServerSymbol? symbol, out refusal))
            {
                return null;
            }

            string newName = @params.GetProperty("newName").GetString() ?? string.Empty;
            refusal = DescribeInvalidName(newName) ?? DescribeRenameConflict(analysis!, symbol!, newName);
            if (refusal is not null)
            {
                return null;
            }

            JsonArray edits = new();
            foreach (PscpServerReference reference in analysis!.References.Where(reference => reference.SymbolId == symbol!.Id))
            {
                edits.Add(new JsonObject
                {
                    ["range"] = ToRange(analysis.Snapshot.LineIndex, reference.Span),
                    ["newText"] = newName,
                });
            }

            return new JsonObject
            {
                ["changes"] = new JsonObject
                {
                    [analysis.Snapshot.Uri.ToString()] = edits,
                },
            };
        }

        private JsonNode? HandlePrepareRename(JsonElement @params, out string? refusal)
        {
            if (!TryPrepareRename(@params, out PscpAnalysisResult? analysis, out PscpServerSymbol? symbol, out refusal))
            {
                return null;
            }

            return new JsonObject
            {
                ["range"] = ToRange(analysis!.Snapshot.LineIndex, symbol!.SelectionSpan),
                ["placeholder"] = symbol.Name,
            };
        }

        // Guide §10.3: an intrinsic, `_`, a tuple projection, a keyword and a .NET member cannot be renamed.
        private bool TryPrepareRename(JsonElement @params, out PscpAnalysisResult? analysis, out PscpServerSymbol? symbol, out string? refusal)
        {
            refusal = null;
            if (!TryGetAnalysisAndOffset(@params, out analysis, out int offset))
            {
                symbol = null;
                refusal = "No PSCP document to rename in.";
                return false;
            }

            int tokenIndex = FindTokenIndexAtOffset(analysis!, offset);
            symbol = null;
            if (tokenIndex < 0)
            {
                refusal = "There is nothing to rename here.";
                return false;
            }

            Token token = analysis!.Tokens[tokenIndex];
            if (token.Kind == TokenKind.IntegerLiteral && tokenIndex > 0 && analysis.Tokens[tokenIndex - 1].Kind == TokenKind.Dot)
            {
                refusal = "A tuple projection such as `.1` is part of the syntax and cannot be renamed.";
                return false;
            }

            if (token.Kind != TokenKind.Identifier || PscpIntrinsics.Keywords.Contains(token.Text))
            {
                refusal = $"`{token.Text}` is a keyword, not a name this file declares.";
                return false;
            }

            if (!TryResolveSymbol(@params, out analysis, out symbol) || symbol is null)
            {
                refusal = PscpIntrinsics.BuiltinTypes.Contains(token.Text)
                    ? $"`{token.Text}` is a built-in type name and cannot be renamed."
                    : $"`{token.Text}` is not declared in this file: a .NET member is renamed where it is defined.";
                return false;
            }

            if (symbol.IsIntrinsic)
            {
                refusal = $"`{symbol.Name}` is a PSCP intrinsic and cannot be renamed.";
                return false;
            }

            if (symbol.Name == "_")
            {
                refusal = "`_` is the discard token, not a binding.";
                return false;
            }

            return true;
        }

        // Guide §10.3: the new name must be a legal identifier that the implementation has not reserved.
        private static string? DescribeInvalidName(string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
            {
                return "The new name is empty.";
            }

            if (newName == "_")
            {
                return "`_` is the discard token, not a name.";
            }

            if (PscpIntrinsics.Keywords.Contains(newName) || PscpIntrinsics.StatementKeywords.Contains(newName))
            {
                return $"`{newName}` is a reserved keyword (spec §3.3).";
            }

            if (newName.StartsWith("__pscp", StringComparison.Ordinal) || newName.StartsWith("__Pscp", StringComparison.Ordinal))
            {
                return $"`{newName}` uses the `__pscp` prefix, which the implementation reserves (spec §3.2).";
            }

            return IsValidRenameIdentifier(newName) ? null : $"`{newName}` is not a valid identifier (spec §3.2).";
        }

        // Guide §10.3: the rename must not collide with another declaration in the same scope, and must not
        // change what an existing reference resolves to. Shadowing an intrinsic is allowed; it only adds the
        // PSCP5001 information diagnostic.
        private static string? DescribeRenameConflict(PscpAnalysisResult analysis, PscpServerSymbol symbol, string newName)
        {
            foreach (PscpServerSymbol other in analysis.Symbols)
            {
                if (other.Id == symbol.Id || other.IsIntrinsic || !string.Equals(other.Name, newName, StringComparison.Ordinal))
                {
                    continue;
                }

                // Two declarations conflict when either one's scope covers the other's declaration.
                bool overlaps = Covers(other.Scope, symbol.DeclarationSpan.Start) || Covers(symbol.Scope, other.DeclarationSpan.Start);
                if (overlaps)
                {
                    (int line, int character) = analysis.Snapshot.LineIndex.GetPosition(other.DeclarationSpan.Start);
                    return $"`{newName}` is already declared at line {line + 1}, column {character + 1}, and would be shadowed or shadow this one.";
                }
            }

            return null;
        }

        private static bool Covers(ScopeSpan scope, int offset)
            => scope.Start <= offset && offset <= scope.End;

        // Guide §12.2: quick fixes for the diagnostics in the requested range, as versioned document changes.
        private JsonNode HandleCodeAction(JsonElement @params)
        {
            if (!TryGetAnalysis(@params, out PscpAnalysisResult? analysis))
            {
                return new JsonArray();
            }

            JsonElement range = @params.GetProperty("range");
            LineIndex lineIndex = analysis!.Snapshot.LineIndex;
            int start = lineIndex.GetOffset(range.GetProperty("start").GetProperty("line").GetInt32(), range.GetProperty("start").GetProperty("character").GetInt32());
            int end = lineIndex.GetOffset(range.GetProperty("end").GetProperty("line").GetInt32(), range.GetProperty("end").GetProperty("character").GetInt32());
            JsonArray actions = new();
            foreach (PscpQuickFix fix in PscpCodeActions.Compute(analysis, new TextSpan(start, Math.Max(0, end - start))))
            {
                JsonArray edits = new();
                foreach (PscpTextEdit edit in fix.Edits)
                {
                    edits.Add(new JsonObject
                    {
                        ["range"] = ToRange(lineIndex, edit.Span),
                        ["newText"] = edit.NewText,
                    });
                }

                JsonObject action = new()
                {
                    ["title"] = fix.Title,
                    ["kind"] = "quickfix",
                    ["diagnostics"] = new JsonArray(ToLspDiagnostic(analysis, fix.Diagnostic)),
                    ["edit"] = new JsonObject
                    {
                        ["documentChanges"] = new JsonArray(new JsonObject
                        {
                            ["textDocument"] = new JsonObject
                            {
                                ["uri"] = analysis.Snapshot.Uri.ToString(),
                                ["version"] = analysis.Snapshot.Version,
                            },
                            ["edits"] = edits,
                        }),
                    },
                };
                if (fix.IsPreferred)
                {
                    action["isPreferred"] = true;
                }

                actions.Add(action);
            }

            return actions;
        }

        // Guide §13.2: the C# `pscp transpile` would write for the latest analyzed version.
        private JsonNode HandleGeneratedCSharp(JsonElement @params)
        {
            if (!TryGetAnalysis(@params, out PscpAnalysisResult? analysis) || analysis!.FrontEnd is null)
            {
                return new JsonObject();
            }

            bool Option(string name) => @params.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            string className = Path.GetFileNameWithoutExtension(analysis.Snapshot.Uri.LocalPath);
            TranspilationOptions options = new(
                "Pscp.Generated",
                new string(className.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray()) + "Program",
                Option("verbose") ? HelperEmissionMode.Verbose : HelperEmissionMode.Compact,
                Option("explain"),
                Option("explain") ? analysis.Snapshot.Text : null,
                Option("pretty"));
            string? csharp = null;
            try
            {
                csharp = PscpTranspiler.Generate(analysis.FrontEnd, options);
            }
            catch (Exception ex)
            {
                _log.WriteLine(ex.ToString());
            }

            JsonArray diagnostics = new();
            foreach (PscpServerDiagnostic diagnostic in analysis.Diagnostics.Where(diagnostic => diagnostic.Severity == ServerDiagnosticSeverity.Error))
            {
                diagnostics.Add(ToLspDiagnostic(analysis, diagnostic));
            }

            return new JsonObject
            {
                ["uri"] = analysis.Snapshot.Uri.ToString(),
                ["version"] = analysis.Snapshot.Version,
                ["languageVersion"] = PscpVersionInfo.LanguageVersion,
                ["toolVersion"] = PscpVersionInfo.ToolVersion,
                ["csharp"] = csharp,
                ["diagnostics"] = diagnostics,
                ["elapsedMs"] = watch.ElapsedMilliseconds,
            };
        }

        // Guide §6.3: the code links to its spec section; deprecated names and unnecessary code are tagged; the
        // declaration a warning refers to is related information.
        private static JsonObject ToLspDiagnostic(PscpAnalysisResult analysis, PscpServerDiagnostic diagnostic)
        {
            JsonObject result = new()
            {
                ["range"] = ToRange(analysis.Snapshot.LineIndex, diagnostic.Span),
                ["severity"] = (int)diagnostic.Severity,
                ["code"] = diagnostic.Code,
                ["source"] = "pscp",
                ["message"] = diagnostic.Message,
            };

            if (DiagnosticCodes.GetSpecLink(diagnostic.Code) is string link)
            {
                result["codeDescription"] = new JsonObject { ["href"] = link };
            }

            if (DiagnosticCodes.IsUnnecessary(diagnostic.Code) || DiagnosticCodes.IsDeprecation(diagnostic.Code))
            {
                result["tags"] = new JsonArray(DiagnosticCodes.IsUnnecessary(diagnostic.Code) ? 1 : 2);
            }

            if (diagnostic.RelatedSpan is TextSpan related)
            {
                result["relatedInformation"] = new JsonArray(new JsonObject
                {
                    ["location"] = new JsonObject
                    {
                        ["uri"] = analysis.Snapshot.Uri.ToString(),
                        ["range"] = ToRange(analysis.Snapshot.LineIndex, related),
                    },
                    ["message"] = "declared here",
                });
            }

            return result;
        }

        private async Task PublishDiagnosticsAsync(PscpAnalysisResult analysis, CancellationToken cancellationToken)
        {
            JsonArray diagnostics = new();
            foreach (PscpServerDiagnostic diagnostic in analysis.Diagnostics)
            {
                // Appendix C: the lowering information diagnostics (PSCP51xx) are opt-in.
                if (!_loweringDiagnostics && diagnostic.Code.StartsWith("PSCP51", StringComparison.Ordinal))
                {
                    continue;
                }

                diagnostics.Add(ToLspDiagnostic(analysis, diagnostic));
            }

            await SendNotificationAsync(
                "textDocument/publishDiagnostics",
                new JsonObject
                {
                    ["uri"] = analysis.Snapshot.Uri.ToString(),
                    ["version"] = analysis.Snapshot.Version,
                    ["diagnostics"] = diagnostics,
                },
                cancellationToken);
        }

        private bool TryGetAnalysis(JsonElement @params, out PscpAnalysisResult? analysis)
        {
            Uri uri = new(@params.GetProperty("textDocument").GetProperty("uri").GetString()!, UriKind.Absolute);
            return TryGetAnalysis(uri, out analysis);
        }

        private bool TryGetAnalysis(Uri uri, out PscpAnalysisResult? analysis)
        {
            // Guide §5.4 rule 3: a request is answered from the latest version of the document, so a pending
            // change is analysed before the answer rather than after.
            if (_dirty.Contains(uri) && _documents.TryGet(uri, out DocumentSnapshot? pending) && pending is not null)
            {
                _dirty.Remove(uri);
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                analysis = _analyzer.Analyze(pending);
                _analyses[uri] = analysis;
                _pendingDiagnostics[uri] = watch.ElapsedMilliseconds;
                return true;
            }

            if (_analyses.TryGetValue(uri, out analysis))
            {
                return true;
            }

            string text = TryReadLocalFile(uri);
            DocumentSnapshot snapshot = _documents.Open(uri, text, version: 0);
            analysis = _analyzer.Analyze(snapshot);
            _analyses[uri] = analysis;
            return true;
        }

        private bool TryGetAnalysisAndOffset(JsonElement @params, out PscpAnalysisResult? analysis, out int offset)
        {
            offset = 0;
            if (!TryGetAnalysis(@params, out analysis))
            {
                return false;
            }

            JsonElement position = @params.GetProperty("position");
            offset = analysis!.Snapshot.LineIndex.GetOffset(position.GetProperty("line").GetInt32(), position.GetProperty("character").GetInt32());
            return true;
        }

        private bool TryResolveSymbol(JsonElement @params, out PscpAnalysisResult? analysis, out PscpServerSymbol? symbol)
        {
            symbol = null;
            if (!TryGetAnalysisAndOffset(@params, out analysis, out int offset))
            {
                return false;
            }

            int tokenIndex = FindTokenIndexAtOffset(analysis!, offset);
            if (tokenIndex < 0 || !analysis!.TokenToSymbolId.TryGetValue(tokenIndex, out string? symbolId))
            {
                return false;
            }

            symbol = analysis.Symbols.FirstOrDefault(candidate => candidate.Id == symbolId);
            return symbol is not null;
        }

        private static int FindTokenIndexAtOffset(PscpAnalysisResult analysis, int offset)
        {
            int fallback = -1;
            for (int i = 0; i < analysis.Tokens.Count; i++)
            {
                Token token = analysis.Tokens[i];
                if (token.Kind == TokenKind.EndOfFile)
                {
                    break;
                }

                if (offset < token.Position)
                {
                    break;
                }

                fallback = i;
                if (offset < token.Position + Math.Max(token.Text.Length, 1))
                {
                    return i;
                }
            }

            return fallback;
        }

        private static JsonArray ToJsonArray(IEnumerable<string> values)
        {
            JsonArray array = new();
            foreach (string value in values)
            {
                array.Add(value);
            }

            return array;
        }

        private static JsonObject ToPosition(LineIndex lineIndex, int offset)
        {
            (int line, int character) = lineIndex.GetPosition(offset);
            return new JsonObject
            {
                ["line"] = line,
                ["character"] = character,
            };
        }

        private static JsonObject ToRange(LineIndex lineIndex, TextSpan span)
        {
            return new JsonObject
            {
                ["start"] = ToPosition(lineIndex, span.Start),
                ["end"] = ToPosition(lineIndex, span.End),
            };
        }

        private static JsonObject ToLocation(Uri uri, LineIndex lineIndex, TextSpan span)
        {
            return new JsonObject
            {
                ["uri"] = uri.ToString(),
                ["range"] = ToRange(lineIndex, span),
            };
        }

        private static string ApplyTextChanges(string currentText, JsonElement changes)
        {
            string text = currentText;
            foreach (JsonElement change in changes.EnumerateArray())
            {
                string replacement = change.GetProperty("text").GetString() ?? string.Empty;
                if (!change.TryGetProperty("range", out JsonElement range))
                {
                    text = replacement;
                    continue;
                }

                LineIndex lineIndex = new(text);
                int start = lineIndex.GetOffset(range.GetProperty("start").GetProperty("line").GetInt32(), range.GetProperty("start").GetProperty("character").GetInt32());
                int end = lineIndex.GetOffset(range.GetProperty("end").GetProperty("line").GetInt32(), range.GetProperty("end").GetProperty("character").GetInt32());
                text = text[..start] + replacement + text[end..];
            }

            return text;
        }

        private static string TryReadLocalFile(Uri uri)
            => uri.IsFile && File.Exists(uri.LocalPath) ? File.ReadAllText(uri.LocalPath, Encoding.UTF8) : string.Empty;

        private List<PscpCompletionEntry> BuildCompletionItems(PscpAnalysisResult analysis, int offset)
        {
            List<PscpCompletionEntry> items = [];
            int tokenIndex = FindTokenIndexAtOffset(analysis, offset);
            int previousIndex = tokenIndex >= 0 && analysis.Tokens[tokenIndex].Position >= offset
                ? tokenIndex - 1
                : tokenIndex;
            bool statementStart = previousIndex < 0;
            while (previousIndex >= 0 && analysis.Tokens[previousIndex].Kind is TokenKind.NewLine or TokenKind.Semicolon)
            {
                statementStart = true;
                previousIndex--;
            }

            TokenKind previousKind = previousIndex >= 0 ? analysis.Tokens[previousIndex].Kind : TokenKind.NewLine;
            if (previousKind is TokenKind.OpenBrace or TokenKind.Then or TokenKind.Else or TokenKind.Do)
            {
                statementStart = true;
            }

            if (previousKind == TokenKind.Dot)
            {
                int receiverIndex = previousIndex - 1;
                while (receiverIndex >= 0 && analysis.Tokens[receiverIndex].Kind is TokenKind.NewLine or TokenKind.Semicolon)
                {
                    receiverIndex--;
                }

                foreach (PscpCompletionEntry item in GetMemberCompletions(analysis, receiverIndex))
                {
                    items.Add(item);
                }

                return Deduplicate(items);
            }

            // A type is the only candidate after `new`, `:` (the declared type) and `<` (a type argument);
            // guide §8.1.
            if (previousKind is TokenKind.New or TokenKind.Colon)
            {
                AddTypeCompletions(analysis, offset, items);
                return Deduplicate(items);
            }

            // Guide §8.1: after `is` a type, a constant and the pattern keywords are the candidates.
            if (previousKind == TokenKind.Is)
            {
                AddTypeCompletions(analysis, offset, items);
                AddKeywordCompletions(PscpIntrinsics.PatternKeywords, items);
                return Deduplicate(items);
            }

            // Guide §8.1: only a namespace follows `using`.
            if (previousKind == TokenKind.Using)
            {
                foreach (PscpCompletionEntry entry in PscpExternalMetadata.GetTopLevelCompletions().Where(entry => entry.Kind == 9))
                {
                    items.Add(entry.InGroup(PscpIntrinsics.SortDotNet));
                }

                return Deduplicate(items);
            }

            // Guide §8.1: the collection helper names are candidates as a pipe target and nowhere else.
            if (previousKind is TokenKind.PipeGreater or TokenKind.LessPipe)
            {
                foreach (PscpCompletionEntry entry in PscpIntrinsics.CollectionMembers.Values)
                {
                    items.Add(entry.InGroup(PscpIntrinsics.SortIntrinsic));
                }
            }

            foreach (KeyValuePair<string, PscpCompletionEntry> intrinsic in PscpIntrinsics.Globals)
            {
                items.Add(intrinsic.Value);
            }

            foreach (PscpCompletionEntry entry in PscpExternalMetadata.GetTopLevelCompletions())
            {
                items.Add(entry.InGroup(PscpIntrinsics.SortDotNet));
            }

            foreach (KeyValuePair<string, PscpCompletionEntry> intrinsic in PscpIntrinsics.IntrinsicFunctions)
            {
                items.Add(intrinsic.Value);
            }

            AddKeywordCompletions(
                statementStart
                    ? PscpIntrinsics.StatementKeywords.Concat(PscpIntrinsics.ExpressionKeywords).ToArray()
                    : PscpIntrinsics.ExpressionKeywords,
                items);

            if (statementStart && _supportsSnippets)
            {
                items.AddRange(PscpIntrinsics.StatementSnippets);
            }

            AddTypeCompletions(analysis, offset, items);
            AddSymbolCompletions(analysis, offset, items);
            return Deduplicate(items);
        }

        private static void AddKeywordCompletions(IEnumerable<string> keywords, List<PscpCompletionEntry> items)
        {
            foreach (string keyword in keywords.OrderBy(value => value, StringComparer.Ordinal))
            {
                items.Add(new PscpCompletionEntry(keyword, 14, "keyword", $"`{keyword}` keyword.", null, null, PscpIntrinsics.SortKeyword + keyword));
            }
        }

        private static void AddTypeCompletions(PscpAnalysisResult analysis, int offset, List<PscpCompletionEntry> items)
        {
            foreach (KeyValuePair<string, string> builtin in PscpIntrinsics.TypeCompletionDetails)
            {
                items.Add(new PscpCompletionEntry(builtin.Key, 7, builtin.Value, "Built-in type.", null, null, PscpIntrinsics.SortIntrinsic + builtin.Key));
            }

            foreach (PscpServerSymbol symbol in analysis.Symbols.Where(symbol => symbol.Kind is PscpServerSymbolKind.Type))
            {
                items.Add(SymbolCompletion(symbol, PscpIntrinsics.SortTopLevel));
            }

            foreach (PscpCompletionEntry entry in PscpExternalMetadata.GetTopLevelCompletions())
            {
                items.Add(entry.InGroup(PscpIntrinsics.SortDotNet));
            }
        }

        // Guide §8.3: a local, a parameter or a local function comes first, the nearest scope first; a
        // top-level symbol follows.
        private static void AddSymbolCompletions(PscpAnalysisResult analysis, int offset, List<PscpCompletionEntry> items)
        {
            PscpServerSymbol[] visible = analysis.Symbols
                .Where(symbol => symbol.DeclarationSpan.Start <= offset && symbol.Scope.Start <= offset && offset <= symbol.Scope.End)
                .OrderByDescending(symbol => symbol.Scope.Start)
                .ThenBy(symbol => symbol.Name, StringComparer.Ordinal)
                .ToArray();

            // The enclosing scopes, innermost first: the rank orders group 0 so that the nearest declaration
            // of a shadowed name is offered first.
            int[] scopeStarts = visible.Select(symbol => symbol.Scope.Start).Distinct().OrderByDescending(start => start).ToArray();
            foreach (PscpServerSymbol symbol in visible)
            {
                if (IsTopLevelScope(analysis, symbol))
                {
                    items.Add(SymbolCompletion(symbol, PscpIntrinsics.SortTopLevel));
                    continue;
                }

                int rank = Math.Min(Array.IndexOf(scopeStarts, symbol.Scope.Start), 99);
                items.Add(SymbolCompletion(symbol, PscpIntrinsics.SortLocal + rank.ToString("D2", CultureInfo.InvariantCulture)));
            }
        }

        private static bool IsTopLevelScope(PscpAnalysisResult analysis, PscpServerSymbol symbol)
            => symbol.Scope.Start == 0 && symbol.Scope.End >= analysis.Snapshot.Text.Length;

        private static PscpCompletionEntry SymbolCompletion(PscpServerSymbol symbol, string group)
            => new(
                symbol.Name,
                ToCompletionKind(symbol.Kind),
                symbol.TypeDisplay,
                symbol.Documentation,
                null,
                null,
                group + symbol.Name,
                null,
                symbol.TypeDisplay);

        private static IEnumerable<PscpCompletionEntry> GetMemberCompletions(PscpAnalysisResult analysis, int receiverIndex)
        {
            if (receiverIndex < 0 || receiverIndex >= analysis.Tokens.Count)
            {
                return Array.Empty<PscpCompletionEntry>();
            }

            string receiverName = BuildReceiverChain(analysis.Tokens, receiverIndex);
            bool instanceContext = false;
            PscpServerSymbol? receiverSymbol = null;
            if (analysis.TokenToSymbolId.TryGetValue(receiverIndex, out string? symbolId))
            {
                receiverSymbol = analysis.Symbols.FirstOrDefault(candidate => candidate.Id == symbolId);
                if (receiverSymbol?.IsIntrinsic == true)
                {
                    receiverName = receiverSymbol.Name;
                }
                else if (!string.IsNullOrWhiteSpace(receiverSymbol?.TypeDisplay))
                {
                    receiverName = receiverSymbol.TypeDisplay!;
                    instanceContext = true;
                }
            }
            else if (TryInferIndexedReceiverType(analysis, receiverIndex, out string? indexedReceiverType))
            {
                receiverName = indexedReceiverType!;
                instanceContext = true;
            }

            IEnumerable<PscpCompletionEntry> intrinsicMembers = receiverName switch
            {
                "stdin" => PscpIntrinsics.StdinMembers.Values,
                "stdout" => PscpIntrinsics.StdoutMembers.Values,
                "Array" => PscpIntrinsics.ArrayMembers.Values,
                _ => Array.Empty<PscpCompletionEntry>(),
            };

            if (receiverName is "stdin" or "stdout")
            {
                return intrinsicMembers;
            }

            List<PscpCompletionEntry> members = [];
            members.AddRange(intrinsicMembers);

            // Guide §8.3: field and property, then the PSCP member alias, then the .NET method.
            if (instanceContext
                && analysis.TypeMembers.TryGetValue(NormalizeTypeMemberReceiver(receiverName), out IReadOnlyDictionary<string, PscpServerSymbol>? userMembers))
            {
                members.AddRange(userMembers.Values.Select(symbol => SymbolCompletion(
                    symbol,
                    symbol.Kind is PscpServerSymbolKind.Property or PscpServerSymbolKind.Local
                        ? PscpIntrinsics.SortTypeMember
                        : PscpIntrinsics.SortTopLevel)));
            }

            if (IsCollectionLikeReceiver(receiverName, instanceContext, analysis.Tokens[receiverIndex].Kind))
            {
                members.AddRange(PscpIntrinsics.CollectionMembers.Values);
            }

            foreach (PscpCompletionEntry entry in PscpExternalMetadata.GetMemberCompletions(receiverName, instanceContext))
            {
                members.Add(entry.InGroup(entry.Kind is 5 or 10 ? PscpIntrinsics.SortTypeMember : PscpIntrinsics.SortDotNet));
            }

            if (ShouldOfferComparatorMembers(receiverName, instanceContext, receiverSymbol))
            {
                members.AddRange(PscpIntrinsics.ComparatorMembers.Values);
            }

            return members;
        }

        private static List<PscpCompletionEntry> Deduplicate(IEnumerable<PscpCompletionEntry> items)
        {
            Dictionary<string, PscpCompletionEntry> unique = new(StringComparer.Ordinal);
            foreach (PscpCompletionEntry item in items)
            {
                unique.TryAdd(item.Label, item);
            }

            return unique.Values.ToList();
        }

        private static int ToCompletionKind(PscpServerSymbolKind kind)
        {
            return kind switch
            {
                PscpServerSymbolKind.Function => 3,
                PscpServerSymbolKind.Type => 7,
                PscpServerSymbolKind.Method => 2,
                PscpServerSymbolKind.Property => 10,
                _ => 6,
            };
        }

        private static bool ShouldOfferComparatorMembers(string receiverName, bool instanceContext, PscpServerSymbol? receiverSymbol)
        {
            if (instanceContext)
            {
                return false;
            }

            if (receiverSymbol?.Kind == PscpServerSymbolKind.Type)
            {
                return true;
            }

            string normalized = PscpExternalMetadata.NormalizeTypeReceiver(receiverName);
            return PscpIntrinsics.BuiltinTypes.Contains(normalized)
                || receiverName.StartsWith('(')
                || receiverName.EndsWith("[]", StringComparison.Ordinal);
        }

        private static bool IsCollectionLikeReceiver(string receiverName, bool instanceContext, TokenKind receiverTokenKind)
        {
            if (!instanceContext && receiverTokenKind is not TokenKind.CloseParen and not TokenKind.CloseBracket)
            {
                return false;
            }

            if (!instanceContext && receiverTokenKind is (TokenKind.CloseParen or TokenKind.CloseBracket))
            {
                return true;
            }

            string normalized = PscpExternalMetadata.NormalizeTypeReceiver(receiverName);
            return normalized is "Array" or "List" or "LinkedList" or "Queue" or "Stack" or "HashSet" or "SortedSet" or "SortedDictionary" or "Dictionary" or "String"
                || normalized.StartsWith("IEnumerable", StringComparison.Ordinal);
        }

        private static bool TryInferIndexedReceiverType(PscpAnalysisResult analysis, int receiverIndex, out string? type)
        {
            type = null;
            if (receiverIndex < 0
                || receiverIndex >= analysis.Tokens.Count
                || analysis.Tokens[receiverIndex].Kind != TokenKind.CloseBracket)
            {
                return false;
            }

            int openBracket = FindMatchingBackward(analysis.Tokens, receiverIndex, TokenKind.OpenBracket, TokenKind.CloseBracket);
            if (openBracket <= 0)
            {
                return false;
            }

            int sourceIndex = PreviousNonTrivia(analysis.Tokens, openBracket - 1);
            string? sourceType = null;
            if (sourceIndex >= 0 && analysis.Tokens[sourceIndex].Kind == TokenKind.CloseBracket)
            {
                _ = TryInferIndexedReceiverType(analysis, sourceIndex, out sourceType);
            }
            else if (sourceIndex >= 0
                && analysis.TokenToSymbolId.TryGetValue(sourceIndex, out string? symbolId)
                && analysis.Symbols.FirstOrDefault(candidate => candidate.Id == symbolId) is PscpServerSymbol sourceSymbol
                && !string.IsNullOrWhiteSpace(sourceSymbol.TypeDisplay))
            {
                sourceType = sourceSymbol.TypeDisplay;
            }

            type = InferElementType(sourceType);
            return type is not null;
        }

        private static string NormalizeTypeMemberReceiver(string receiverName)
        {
            string normalized = receiverName.Trim();
            if (normalized.EndsWith("?", StringComparison.Ordinal))
            {
                normalized = normalized[..^1].TrimEnd();
            }

            return PscpExternalMetadata.NormalizeTypeReceiver(normalized);
        }

        private static string? InferElementType(string? type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return null;
            }

            string normalized = type.Trim();
            if (normalized.EndsWith("?", StringComparison.Ordinal))
            {
                normalized = normalized[..^1];
            }

            if (normalized.EndsWith("[]", StringComparison.Ordinal))
            {
                return normalized[..^2];
            }

            int open = normalized.IndexOf('<');
            int close = normalized.LastIndexOf('>');
            if (open > 0 && close > open)
            {
                string name = PscpExternalMetadata.NormalizeTypeReceiver(normalized[..open]);
                if (name is "IEnumerable" or "List" or "LinkedList" or "Queue" or "Stack" or "HashSet" or "SortedSet")
                {
                    return normalized[(open + 1)..close].Trim();
                }
            }

            return normalized is "string" or "String" ? "char" : null;
        }

        private static int PreviousNonTrivia(IReadOnlyList<Token> tokens, int index)
        {
            while (index >= 0 && tokens[index].Kind is TokenKind.NewLine or TokenKind.Semicolon)
            {
                index--;
            }

            return index;
        }

        private static int FindMatchingBackward(IReadOnlyList<Token> tokens, int closeIndex, TokenKind openKind, TokenKind closeKind)
        {
            int depth = 0;
            for (int i = closeIndex; i >= 0; i--)
            {
                if (tokens[i].Kind == closeKind)
                {
                    depth++;
                }
                else if (tokens[i].Kind == openKind)
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private static bool IsValidRenameIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "_" || PscpIntrinsics.Keywords.Contains(value))
            {
                return false;
            }

            if (!char.IsLetter(value[0]) && value[0] != '_')
            {
                return false;
            }

            for (int i = 1; i < value.Length; i++)
            {
                if (!char.IsLetterOrDigit(value[i]) && value[i] != '_')
                {
                    return false;
                }
            }

            return true;
        }

        private static string GetLastSignatureSegment(string signatureKey)
        {
            int dot = signatureKey.LastIndexOf('.');
            return dot >= 0 && dot + 1 < signatureKey.Length ? signatureKey[(dot + 1)..] : signatureKey;
        }

        private static string BuildReceiverChain(IReadOnlyList<Token> tokens, int receiverIndex)
        {
            List<string> segments = [tokens[receiverIndex].Text];
            int cursor = receiverIndex - 1;
            while (cursor >= 1)
            {
                while (cursor >= 0 && tokens[cursor].Kind is TokenKind.NewLine or TokenKind.Semicolon)
                {
                    cursor--;
                }

                if (cursor < 1 || tokens[cursor].Kind != TokenKind.Dot)
                {
                    break;
                }

                int previousIdentifier = cursor - 1;
                while (previousIdentifier >= 0 && tokens[previousIdentifier].Kind is TokenKind.NewLine or TokenKind.Semicolon)
                {
                    previousIdentifier--;
                }

                if (previousIdentifier < 0 || tokens[previousIdentifier].Kind != TokenKind.Identifier)
                {
                    break;
                }

                segments.Insert(0, tokens[previousIdentifier].Text);
                cursor = previousIdentifier - 1;
            }

            return string.Join(".", segments);
        }

        private static bool TryFindSignatureContext(PscpAnalysisResult analysis, int offset, out string? signatureKey, out int activeParameter)
        {
            signatureKey = null;
            activeParameter = 0;
            Stack<(int openParen, int callStart)> stack = new();

            for (int i = 0; i < analysis.Tokens.Count; i++)
            {
                Token token = analysis.Tokens[i];
                if (token.Kind == TokenKind.EndOfFile || token.Position > offset)
                {
                    break;
                }

                if (token.Kind == TokenKind.OpenParen)
                {
                    int startIndex = FindCallStart(analysis.Tokens, i - 1);
                    if (startIndex >= 0)
                    {
                        stack.Push((i, startIndex));
                    }
                }
                else if (token.Kind == TokenKind.CloseParen && stack.Count > 0)
                {
                    stack.Pop();
                }
            }

            if (stack.Count == 0)
            {
                return false;
            }

            (int openParen, int callStart) = stack.Peek();
            signatureKey = BuildCallNameFromTokens(analysis.Tokens, callStart, openParen);
            activeParameter = CountTopLevelCommas(analysis.Tokens, openParen + 1, offset);
            return !string.IsNullOrWhiteSpace(signatureKey);
        }

        private static int FindCallStart(IReadOnlyList<Token> tokens, int index)
        {
            while (index >= 0 && tokens[index].Kind is TokenKind.NewLine or TokenKind.Semicolon)
            {
                index--;
            }

            while (index >= 0 && tokens[index].Kind is TokenKind.Identifier or TokenKind.Dot or TokenKind.GreaterThan or TokenKind.LessThan or TokenKind.Comma)
            {
                index--;
            }

            return index + 1;
        }

        private static string BuildCallNameFromTokens(IReadOnlyList<Token> tokens, int start, int endExclusive)
            => string.Concat(tokens.Skip(start).Take(Math.Max(0, endExclusive - start)).Where(token => token.Kind is TokenKind.Identifier or TokenKind.Dot).Select(token => token.Text));

        private static int CountTopLevelCommas(IReadOnlyList<Token> tokens, int start, int offset)
        {
            int count = 0;
            int parenDepth = 0;
            int bracketDepth = 0;
            int braceDepth = 0;

            for (int i = start; i < tokens.Count; i++)
            {
                Token token = tokens[i];
                if (token.Position >= offset)
                {
                    break;
                }

                switch (token.Kind)
                {
                    case TokenKind.OpenParen:
                        parenDepth++;
                        break;
                    case TokenKind.CloseParen:
                        if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0)
                        {
                            return count;
                        }

                        parenDepth = Math.Max(0, parenDepth - 1);
                        break;
                    case TokenKind.OpenBracket:
                        bracketDepth++;
                        break;
                    case TokenKind.CloseBracket:
                        bracketDepth = Math.Max(0, bracketDepth - 1);
                        break;
                    case TokenKind.OpenBrace:
                        braceDepth++;
                        break;
                    case TokenKind.CloseBrace:
                        braceDepth = Math.Max(0, braceDepth - 1);
                        break;
                    case TokenKind.Comma:
                        if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0)
                        {
                            count++;
                        }

                        break;
                }
            }

            return count;
        }

        private async Task SendResultAsync(JsonElement id, JsonNode? result, CancellationToken cancellationToken)
        {
            await WriteMessageAsync(
                _output,
                new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = JsonNode.Parse(id.GetRawText()),
                    ["result"] = result,
                },
                cancellationToken);
        }

        private async Task SendErrorAsync(JsonElement id, int code, string message, CancellationToken cancellationToken)
        {
            await WriteMessageAsync(
                _output,
                new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = JsonNode.Parse(id.GetRawText()),
                    ["error"] = new JsonObject
                    {
                        ["code"] = code,
                        ["message"] = message,
                    },
                },
                cancellationToken);
        }

        private async Task SendNotificationAsync(string method, JsonNode? @params, CancellationToken cancellationToken)
        {
            await WriteMessageAsync(
                _output,
                new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["method"] = method,
                    ["params"] = @params,
                },
                cancellationToken);
        }

        private static async Task<JsonDocument?> ReadMessageAsync(Stream input, CancellationToken cancellationToken)
        {
            List<byte> headerBytes = [];
            byte[] one = new byte[1];
            while (true)
            {
                int read = await input.ReadAsync(one.AsMemory(0, 1), cancellationToken);
                if (read == 0)
                {
                    return null;
                }

                headerBytes.Add(one[0]);
                int count = headerBytes.Count;
                if (count >= 4
                    && headerBytes[count - 4] == '\r'
                    && headerBytes[count - 3] == '\n'
                    && headerBytes[count - 2] == '\r'
                    && headerBytes[count - 1] == '\n')
                {
                    break;
                }
            }

            string headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
            int contentLength = 0;
            foreach (string line in headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
            {
                if (!line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                contentLength = int.Parse(line["Content-Length:".Length..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                break;
            }

            if (contentLength <= 0)
            {
                return null;
            }

            byte[] body = new byte[contentLength];
            int offset = 0;
            while (offset < body.Length)
            {
                int read = await input.ReadAsync(body.AsMemory(offset, body.Length - offset), cancellationToken);
                if (read == 0)
                {
                    throw new EndOfStreamException("Unexpected end of stream while reading message body.");
                }

                offset += read;
            }

            return JsonDocument.Parse(body);
        }

        private static async Task WriteMessageAsync(Stream output, JsonNode payload, CancellationToken cancellationToken)
        {
            byte[] body = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
            byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
            await output.WriteAsync(header.AsMemory(0, header.Length), cancellationToken);
            await output.WriteAsync(body.AsMemory(0, body.Length), cancellationToken);
            await output.FlushAsync(cancellationToken);
        }
    }
}
