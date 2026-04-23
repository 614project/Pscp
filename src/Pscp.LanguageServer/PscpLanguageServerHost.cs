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
            "mutable",
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
        private readonly PscpAnalyzer _analyzer = new();
        private bool _shutdownRequested;

        public Session(Stream input, Stream output, TextWriter log)
        {
            _input = input;
            _output = output;
            _log = log;
        }

        public async Task<int> RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                JsonDocument? message = await ReadMessageAsync(_input, cancellationToken);
                if (message is null)
                {
                    break;
                }

                using (message)
                {
                    bool shouldExit = await HandleMessageAsync(message.RootElement, cancellationToken);
                    if (shouldExit)
                    {
                        return _shutdownRequested ? 0 : 1;
                    }
                }
            }

            return _shutdownRequested ? 0 : 1;
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
                        if (hasId)
                        {
                            await SendResultAsync(id, CreateInitializeResult(), cancellationToken);
                        }

                        return false;
                    case "initialized":
                    case "$/cancelRequest":
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
                        await SendResultAsync(id, HandleSemanticTokens(@params), cancellationToken);
                        return false;
                    case "textDocument/signatureHelp":
                        await SendResultAsync(id, HandleSignatureHelp(@params), cancellationToken);
                        return false;
                    case "textDocument/inlayHint":
                        await SendResultAsync(id, HandleInlayHints(@params), cancellationToken);
                        return false;
                    case "textDocument/rename":
                        await SendResultAsync(id, HandleRename(@params), cancellationToken);
                        return false;
                    case "textDocument/prepareRename":
                        await SendResultAsync(id, HandlePrepareRename(@params), cancellationToken);
                        return false;
                    case "textDocument/codeAction":
                        await SendResultAsync(id, HandleCodeAction(@params), cancellationToken);
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
                    ["renameProvider"] = true,
                    ["prepareRenameProvider"] = true,
                    ["codeActionProvider"] = true,
                    ["inlayHintProvider"] = true,
                    ["completionProvider"] = new JsonObject
                    {
                        ["resolveProvider"] = false,
                        ["triggerCharacters"] = new JsonArray("."),
                    },
                    ["signatureHelpProvider"] = new JsonObject
                    {
                        ["triggerCharacters"] = new JsonArray("(", ","),
                        ["retriggerCharacters"] = new JsonArray(","),
                    },
                    ["semanticTokensProvider"] = new JsonObject
                    {
                        ["legend"] = new JsonObject
                        {
                            ["tokenTypes"] = ToJsonArray(SemanticTokenTypes),
                            ["tokenModifiers"] = ToJsonArray(SemanticTokenModifiers),
                        },
                        ["full"] = true,
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
            DocumentSnapshot snapshot = _documents.Open(uri, text, version);
            PscpAnalysisResult analysis = _analyzer.Analyze(snapshot);
            _analyses[uri] = analysis;
            await PublishDiagnosticsAsync(analysis, cancellationToken);
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
            DocumentSnapshot snapshot = _documents.Change(uri, version, updatedText);
            PscpAnalysisResult analysis = _analyzer.Analyze(snapshot);
            _analyses[uri] = analysis;
            await PublishDiagnosticsAsync(analysis, cancellationToken);
        }

        private async Task HandleDidCloseAsync(JsonElement @params, CancellationToken cancellationToken)
        {
            JsonElement textDocument = @params.GetProperty("textDocument");
            Uri uri = new(textDocument.GetProperty("uri").GetString()!, UriKind.Absolute);
            _documents.Close(uri, out _);
            _analyses.Remove(uri);
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
                items.Add(new JsonObject
                {
                    ["label"] = entry.Label,
                    ["kind"] = entry.Kind,
                    ["detail"] = entry.Detail,
                    ["documentation"] = entry.Documentation is null
                        ? null
                        : new JsonObject
                        {
                            ["kind"] = "markdown",
                            ["value"] = entry.Documentation,
                        },
                    ["insertText"] = entry.InsertText,
                    ["insertTextFormat"] = entry.InsertTextFormat,
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

        private JsonNode HandleDocumentSymbols(JsonElement @params)
        {
            if (!TryGetAnalysis(@params, out PscpAnalysisResult? analysis))
            {
                return new JsonArray();
            }

            JsonArray results = new();
            foreach (PscpDocumentSymbol symbol in analysis!.DocumentSymbols)
            {
                results.Add(new JsonObject
                {
                    ["name"] = symbol.Name,
                    ["kind"] = symbol.Kind,
                    ["range"] = ToRange(analysis.Snapshot.LineIndex, symbol.Range),
                    ["selectionRange"] = ToRange(analysis.Snapshot.LineIndex, symbol.SelectionRange),
                    ["children"] = new JsonArray(),
                });
            }

            return results;
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

            JsonArray data = new();
            int previousLine = 0;
            int previousChar = 0;

            foreach (SemanticTokenClassification token in analysis!.SemanticTokens.OrderBy(token => token.Span.Start))
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

            if (!TryFindSignatureContext(analysis!, offset, out string? signatureKey, out int activeParameter))
            {
                return null;
            }

            if (!analysis!.Signatures.TryGetValue(signatureKey!, out PscpSignatureEntry? signature)
                && !analysis.Signatures.TryGetValue(GetLastSignatureSegment(signatureKey!), out signature))
            {
                return null;
            }

            JsonArray parameters = new();
            foreach (string parameter in signature.Parameters)
            {
                parameters.Add(new JsonObject
                {
                    ["label"] = parameter,
                });
            }

            JsonArray signatures = new()
            {
                new JsonObject
                {
                    ["label"] = signature.Label,
                    ["documentation"] = signature.Documentation,
                    ["parameters"] = parameters,
                },
            };

            return new JsonObject
            {
                ["signatures"] = signatures,
                ["activeSignature"] = 0,
                ["activeParameter"] = Math.Clamp(activeParameter, 0, Math.Max(0, signature.Parameters.Count - 1)),
            };
        }

        private JsonNode HandleInlayHints(JsonElement @params)
        {
            if (!TryGetAnalysis(@params, out PscpAnalysisResult? analysis))
            {
                return new JsonArray();
            }

            JsonArray hints = new();
            foreach (PscpInlayHintEntry hint in analysis!.InlayHints)
            {
                hints.Add(new JsonObject
                {
                    ["position"] = ToPosition(analysis.Snapshot.LineIndex, hint.Span.End),
                    ["label"] = hint.Label,
                    ["kind"] = 1,
                    ["paddingLeft"] = true,
                });
            }

            return hints;
        }

        private JsonNode? HandleRename(JsonElement @params)
        {
            if (!TryResolveSymbol(@params, out PscpAnalysisResult? analysis, out PscpServerSymbol? symbol) || symbol!.IsIntrinsic)
            {
                return null;
            }

            string newName = @params.GetProperty("newName").GetString() ?? string.Empty;
            if (!IsValidRenameIdentifier(newName))
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

        private JsonNode? HandlePrepareRename(JsonElement @params)
        {
            if (!TryResolveSymbol(@params, out PscpAnalysisResult? analysis, out PscpServerSymbol? symbol)
                || symbol!.IsIntrinsic
                || symbol.Name == "_")
            {
                return null;
            }

            return new JsonObject
            {
                ["range"] = ToRange(analysis!.Snapshot.LineIndex, symbol.SelectionSpan),
                ["placeholder"] = symbol.Name,
            };
        }

        private JsonNode HandleCodeAction(JsonElement @params)
        {
            if (!TryGetAnalysis(@params, out PscpAnalysisResult? analysis))
            {
                return new JsonArray();
            }

            JsonArray actions = new();
            foreach (PscpCodeActionEntry action in analysis!.CodeActions)
            {
                actions.Add(new JsonObject
                {
                    ["title"] = action.Title,
                    ["kind"] = action.Kind,
                    ["edit"] = new JsonObject
                    {
                        ["changes"] = new JsonObject
                        {
                            [analysis.Snapshot.Uri.ToString()] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["range"] = ToRange(analysis.Snapshot.LineIndex, action.Span),
                                    ["newText"] = action.ReplacementText,
                                },
                            },
                        },
                    },
                });
            }

            return actions;
        }

        private async Task PublishDiagnosticsAsync(PscpAnalysisResult analysis, CancellationToken cancellationToken)
        {
            JsonArray diagnostics = new();
            foreach (PscpServerDiagnostic diagnostic in analysis.Diagnostics)
            {
                diagnostics.Add(new JsonObject
                {
                    ["range"] = ToRange(analysis.Snapshot.LineIndex, diagnostic.Span),
                    ["severity"] = (int)diagnostic.Severity,
                    ["code"] = diagnostic.Code,
                    ["source"] = "pscp",
                    ["message"] = diagnostic.Message,
                });
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
            while (previousIndex >= 0 && analysis.Tokens[previousIndex].Kind is TokenKind.NewLine or TokenKind.Semicolon)
            {
                previousIndex--;
            }

            if (previousIndex >= 0 && analysis.Tokens[previousIndex].Kind == TokenKind.Dot)
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

            foreach (KeyValuePair<string, PscpCompletionEntry> intrinsic in PscpIntrinsics.Globals)
            {
                items.Add(intrinsic.Value);
            }

            foreach (PscpCompletionEntry entry in PscpExternalMetadata.GetTopLevelCompletions())
            {
                items.Add(entry);
            }

            foreach (KeyValuePair<string, PscpCompletionEntry> intrinsic in PscpIntrinsics.IntrinsicFunctions)
            {
                items.Add(intrinsic.Value);
            }

            foreach (string keyword in PscpIntrinsics.Keywords.OrderBy(value => value, StringComparer.Ordinal))
            {
                items.Add(new PscpCompletionEntry(keyword, 14, "keyword", $"`{keyword}` keyword.", null, null, keyword));
            }

            foreach (KeyValuePair<string, string> builtin in PscpIntrinsics.TypeCompletionDetails.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                items.Add(new PscpCompletionEntry(builtin.Key, 7, builtin.Value, "Built-in type.", null, null, builtin.Key));
            }

            foreach (PscpServerSymbol symbol in analysis.Symbols
                .Where(symbol => symbol.DeclarationSpan.Start <= offset && symbol.Scope.Start <= offset && offset <= symbol.Scope.End)
                .OrderBy(symbol => symbol.Name, StringComparer.Ordinal)
                .ThenByDescending(symbol => symbol.Scope.Start))
            {
                items.Add(new PscpCompletionEntry(
                    symbol.Name,
                    ToCompletionKind(symbol.Kind),
                    symbol.TypeDisplay,
                    symbol.Documentation,
                    null,
                    null,
                    symbol.Name));
            }

            return Deduplicate(items);
        }

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

            if (IsCollectionLikeReceiver(receiverName, instanceContext, analysis.Tokens[receiverIndex].Kind))
            {
                members.AddRange(PscpIntrinsics.CollectionMembers.Values);
            }

            members.AddRange(PscpExternalMetadata.GetMemberCompletions(receiverName, instanceContext));

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

            if (receiverTokenKind is TokenKind.CloseParen or TokenKind.CloseBracket)
            {
                return true;
            }

            string normalized = PscpExternalMetadata.NormalizeTypeReceiver(receiverName);
            return receiverName.EndsWith("[]", StringComparison.Ordinal)
                || normalized is "Array" or "List" or "LinkedList" or "Queue" or "Stack" or "HashSet" or "SortedSet"
                || normalized.StartsWith("IEnumerable", StringComparison.Ordinal);
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
