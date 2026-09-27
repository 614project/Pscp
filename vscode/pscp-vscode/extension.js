const vscode = require('vscode');
const cp = require('child_process');
const fs = require('fs');
const path = require('path');
const extensionPackage = require('./package.json');

const PSCP_PROTOCOL_VERSION = 1;
const EXPECTED_LANGUAGE_VERSION = '0.7';
const GENERATED_SCHEME = 'pscp-generated';
const PREVIEW_DEBOUNCE_MS = 300;
const ANALYZING_INDICATOR_DELAY_MS = 300;
const DEFAULT_SAMPLE_TIMEOUT_MS = 2000;

const DEFAULT_REQUEST_TIMEOUT_MS = 8000;
const INITIALIZE_TIMEOUT_MS = 15000;
const DID_CHANGE_DEBOUNCE_MS = 120;

// Guide §14.1: the server tailors markdown, snippets and label details to what the client declares here.
const CLIENT_CAPABILITIES = {
  general: {
    markdown: { parser: 'marked' },
    positionEncodings: ['utf-16']
  },
  workspace: {
    configuration: true,
    workspaceEdit: { documentChanges: true },
    didChangeConfiguration: { dynamicRegistration: false }
  },
  textDocument: {
    synchronization: { didSave: true, willSave: false, dynamicRegistration: false },
    publishDiagnostics: {
      relatedInformation: true,
      codeDescriptionSupport: true,
      versionSupport: true,
      tagSupport: { valueSet: [1, 2] }
    },
    completion: {
      completionItem: {
        snippetSupport: true,
        documentationFormat: ['markdown', 'plaintext'],
        labelDetailsSupport: true,
        insertReplaceSupport: true,
        tagSupport: { valueSet: [1] },
        resolveSupport: { properties: ['documentation', 'detail'] }
      },
      contextSupport: true
    },
    hover: { contentFormat: ['markdown', 'plaintext'] },
    signatureHelp: {
      signatureInformation: {
        documentationFormat: ['markdown', 'plaintext'],
        parameterInformation: { labelOffsetSupport: true },
        activeParameterSupport: true
      }
    },
    documentSymbol: {
      hierarchicalDocumentSymbolSupport: true,
      tagSupport: { valueSet: [1] }
    },
    codeAction: {
      codeActionLiteralSupport: { codeActionKind: { valueSet: ['quickfix', 'refactor', 'source'] } },
      isPreferredSupport: true,
      dataSupport: true,
      resolveSupport: { properties: ['edit'] }
    },
    inlayHint: { resolveSupport: { properties: ['tooltip', 'textEdits'] } },
    rename: { prepareSupport: true },
    semanticTokens: {
      requests: { full: true, range: false },
      tokenTypes: [],
      tokenModifiers: [],
      formats: ['relative']
    },
    foldingRange: { lineFoldingOnly: true },
    documentHighlight: {},
    selectionRange: {}
  }
};

// Guide §14.7: the server feature settings travel in `initializationOptions` and then through
// `workspace/didChangeConfiguration`; they never restart the server.
function getServerFeatureSettings() {
  const config = vscode.workspace.getConfiguration('pscp');
  return {
    inlayHints: {
      inferredTypes: config.get('inlayHints.inferredTypes') !== false,
      rewriteResults: config.get('inlayHints.rewriteResults') !== false,
      accumulatorTypes: config.get('inlayHints.accumulatorTypes') !== false,
      parameterNames: config.get('inlayHints.parameterNames') === true
    },
    hints: {
      loweringDiagnostics: config.get('hints.loweringDiagnostics') === true
    }
  };
}

let client;

async function activate(context) {
  // Guide §14.8: extension and CLI activity in one channel, the server's own output in another.
  const output = vscode.window.createOutputChannel('PSCP');
  const serverOutput = vscode.window.createOutputChannel('PSCP Language Server');
  const diagnostics = vscode.languages.createDiagnosticCollection('pscp');
  const status = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 100);
  const selector = [{ language: 'pscp' }];
  status.command = 'pscp.showStatusActions';
  status.text = '$(sync~spin) PSCP';
  status.show();
  client = new PscpClient(context, serverOutput, diagnostics, status);

  context.subscriptions.push(output, serverOutput, diagnostics, status);
  context.subscriptions.push(vscode.commands.registerCommand('pscp.showServerLog', () => {
    serverOutput.show(true);
  }));

  context.subscriptions.push(vscode.commands.registerCommand('pscp.restartLanguageServer', async () => {
    await client.restart();
    vscode.window.showInformationMessage('PSCP language server restarted.');
  }));

  context.subscriptions.push(vscode.commands.registerCommand('pscp.transpileCurrentFile', async () => {
    await runPscpToolCommand(context, output, 'transpile');
  }));

  context.subscriptions.push(vscode.commands.registerCommand('pscp.runCurrentFile', async () => {
    await runPscpToolCommand(context, output, 'run');
  }));

  const testController = vscode.tests.createTestController('pscp.samples', 'PSCP Samples');
  const preview = new GeneratedCSharpPreview(output);
  context.subscriptions.push(testController, preview);
  context.subscriptions.push(vscode.workspace.registerTextDocumentContentProvider(GENERATED_SCHEME, preview));

  context.subscriptions.push(vscode.commands.registerCommand('pscp.runWithInput', async () => {
    await runWithInputCommand(context, output);
  }));

  context.subscriptions.push(vscode.commands.registerCommand('pscp.runSamples', async () => {
    await runSamplesCommand(context, output, testController);
  }));

  context.subscriptions.push(vscode.commands.registerCommand('pscp.newSample', async () => {
    await newSampleCommand();
  }));

  context.subscriptions.push(vscode.commands.registerCommand('pscp.previewGeneratedCSharp', async () => {
    const editor = vscode.window.activeTextEditor;
    if (!editor || !isPscpDocument(editor.document)) {
      vscode.window.showWarningMessage('Open a .pscp file first.');
      return;
    }

    await preview.show(client, editor.document.uri);
  }));

  // Guide §14.4: the title-bar run button runs the samples when there are any.
  context.subscriptions.push(vscode.commands.registerCommand('pscp.runFileOrSamples', async () => {
    const editor = vscode.window.activeTextEditor;
    const hasSamples = editor && !editor.document.isUntitled && findSamples(editor.document.uri.fsPath).length > 0;
    await (hasSamples ? runSamplesCommand(context, output, testController) : runPscpToolCommand(context, output, 'run'));
  }));

  // Guide §14.8: the status bar opens the actions rather than only the log.
  context.subscriptions.push(vscode.commands.registerCommand('pscp.showStatusActions', async () => {
    const picked = await vscode.window.showQuickPick([
      { label: '$(debug-restart) Restart Language Server', command: 'pscp.restartLanguageServer' },
      { label: '$(output) Show Language Server Log', command: 'pscp.showServerLog' },
      { label: '$(file-code) Preview Generated C#', command: 'pscp.previewGeneratedCSharp' },
      { label: '$(beaker) Run Samples', command: 'pscp.runSamples' }
    ], { title: `PSCP ${client.languageVersion || ''}`.trim() });
    if (picked) {
      await vscode.commands.executeCommand(picked.command);
    }
  }));

  context.subscriptions.push(vscode.workspace.onDidChangeConfiguration(async (event) => {
    // Guide §14.7: only a change to how the server is launched restarts it.
    if (event.affectsConfiguration('pscp.server.path')
      || event.affectsConfiguration('pscp.server.args')
      || event.affectsConfiguration('pscp.sdkPath')
      || event.affectsConfiguration('pscp.languageServerPath')) {
      output.appendLine('PSCP server launch settings changed; restarting language server.');
      await client.restart();
      return;
    }

    if (event.affectsConfiguration('pscp.inlayHints') || event.affectsConfiguration('pscp.hints')) {
      client.sendConfiguration();
    }
  }));

  context.subscriptions.push(vscode.workspace.onDidOpenTextDocument((document) => {
    if (isPscpDocument(document)) {
      client.didOpen(document).catch((error) => logClientError(output, 'didOpen', error));
    }
  }));

  context.subscriptions.push(vscode.workspace.onDidChangeTextDocument((event) => {
    if (isPscpDocument(event.document)) {
      client.didChange(event.document).catch((error) => logClientError(output, 'didChange', error));
      preview.scheduleRefresh(client, event.document.uri);
    }
  }));

  context.subscriptions.push(vscode.workspace.onDidCloseTextDocument((document) => {
    if (isPscpDocument(document)) {
      client.didClose(document).catch((error) => logClientError(output, 'didClose', error));
    }
  }));

  // Guide §11.1 and §14.1: the semantic token legend and the provider set come from the server, so the
  // features are registered once it has answered `initialize`.
  const features = new FeatureRegistrations(context, output, selector);
  context.subscriptions.push(features);
  client.onReady(() => features.register(client));

  try {
    output.appendLine(`Activating PSCP extension ${extensionPackage.version}.`);
    await client.start();
  } catch (error) {
    output.appendLine(String(error));
    vscode.window.showErrorMessage(`Failed to start PSCP language server: ${error.message}`);
  }
}

// The language features the server advertises. They are torn down and rebuilt on every (re)start, because a
// restarted server may advertise a different legend or a different provider set.
class FeatureRegistrations {
  constructor(context, output, selector) {
    this.context = context;
    this.output = output;
    this.selector = selector;
    this.disposables = [];
  }

  dispose() {
    for (const disposable of this.disposables) {
      disposable.dispose();
    }

    this.disposables = [];
  }

  add(disposable) {
    this.disposables.push(disposable);
  }

  register(activeClient) {
    this.dispose();
    const selector = this.selector;
    const output = this.output;
    const capabilities = activeClient.serverCapabilities;

    if (capabilities.completionProvider) {
      const triggers = capabilities.completionProvider.triggerCharacters || ['.'];
      this.add(vscode.languages.registerCompletionItemProvider(selector, {
        provideCompletionItems(document, position, token) {
          return activeClient.request('textDocument/completion', {
            textDocument: toTextDocument(document),
            position: toPosition(position)
          }, token).then(fromCompletionList, fallbackProviderResult(output, 'completion', []));
        }
      }, ...triggers));
    }

    if (capabilities.hoverProvider) {
      this.add(vscode.languages.registerHoverProvider(selector, {
        provideHover(document, position, token) {
          return activeClient.request('textDocument/hover', {
            textDocument: toTextDocument(document),
            position: toPosition(position)
          }, token).then(fromHover, fallbackProviderResult(output, 'hover', null));
        }
      }));
    }

    if (capabilities.definitionProvider) {
      this.add(vscode.languages.registerDefinitionProvider(selector, {
        provideDefinition(document, position, token) {
          return activeClient.request('textDocument/definition', {
            textDocument: toTextDocument(document),
            position: toPosition(position)
          }, token).then(fromDefinition, fallbackProviderResult(output, 'definition', null));
        }
      }));
    }

    if (capabilities.referencesProvider) {
      this.add(vscode.languages.registerReferenceProvider(selector, {
        provideReferences(document, position, contextInfo, token) {
          return activeClient.request('textDocument/references', {
            textDocument: toTextDocument(document),
            position: toPosition(position),
            context: { includeDeclaration: contextInfo.includeDeclaration }
          }, token).then(fromLocations, fallbackProviderResult(output, 'references', []));
        }
      }));
    }

    if (capabilities.documentSymbolProvider) {
      this.add(vscode.languages.registerDocumentSymbolProvider(selector, {
        provideDocumentSymbols(document, token) {
          return activeClient.request('textDocument/documentSymbol', {
            textDocument: toTextDocument(document)
          }, token).then(fromDocumentSymbols, fallbackProviderResult(output, 'document symbols', []));
        }
      }));
    }

    if (capabilities.signatureHelpProvider) {
      const triggers = capabilities.signatureHelpProvider.triggerCharacters || ['(', ','];
      this.add(vscode.languages.registerSignatureHelpProvider(selector, {
        provideSignatureHelp(document, position, token) {
          return activeClient.request('textDocument/signatureHelp', {
            textDocument: toTextDocument(document),
            position: toPosition(position)
          }, token).then(fromSignatureHelp, fallbackProviderResult(output, 'signature help', null));
        }
      }, ...triggers));
    }

    if (capabilities.renameProvider) {
      this.add(vscode.languages.registerRenameProvider(selector, {
        prepareRename(document, position, token) {
          return activeClient.request('textDocument/prepareRename', {
            textDocument: toTextDocument(document),
            position: toPosition(position)
          }, token).then(fromPrepareRename, fallbackProviderResult(output, 'prepare rename', null));
        },
        provideRenameEdits(document, position, newName, token) {
          return activeClient.request('textDocument/rename', {
            textDocument: toTextDocument(document),
            position: toPosition(position),
            newName
          }, token, { timeoutMs: 12000 }).then(fromWorkspaceEdit, fallbackProviderResult(output, 'rename', null));
        }
      }));
    }

    if (capabilities.inlayHintProvider) {
      this.add(vscode.languages.registerInlayHintsProvider(selector, {
        provideInlayHints(document, range, token) {
          return activeClient.request('textDocument/inlayHint', {
            textDocument: toTextDocument(document),
            range: toRange(range)
          }, token).then(fromInlayHints, fallbackProviderResult(output, 'inlay hints', []));
        }
      }));
    }

    if (capabilities.codeActionProvider) {
      this.add(vscode.languages.registerCodeActionsProvider(selector, {
        provideCodeActions(document, range, contextInfo, token) {
          return activeClient.request('textDocument/codeAction', {
            textDocument: toTextDocument(document),
            range: toRange(range),
            context: {
              diagnostics: contextInfo.diagnostics.map(toDiagnosticPayload)
            }
          }, token).then(fromCodeActions, fallbackProviderResult(output, 'code actions', []));
        }
      }, { providedCodeActionKinds: [vscode.CodeActionKind.QuickFix] }));
    }

    if (capabilities.foldingRangeProvider) {
      this.add(vscode.languages.registerFoldingRangeProvider(selector, {
        provideFoldingRanges(document, contextInfo, token) {
          return activeClient.request('textDocument/foldingRange', {
            textDocument: toTextDocument(document)
          }, token).then(fromFoldingRanges, fallbackProviderResult(output, 'folding ranges', []));
        }
      }));
    }

    if (capabilities.selectionRangeProvider) {
      this.add(vscode.languages.registerSelectionRangeProvider(selector, {
        provideSelectionRanges(document, positions, token) {
          return activeClient.request('textDocument/selectionRange', {
            textDocument: toTextDocument(document),
            positions: positions.map(toPosition)
          }, token).then(fromSelectionRanges, fallbackProviderResult(output, 'selection ranges', []));
        }
      }));
    }

    if (capabilities.documentHighlightProvider) {
      this.add(vscode.languages.registerDocumentHighlightProvider(selector, {
        provideDocumentHighlights(document, position, token) {
          return activeClient.request('textDocument/documentHighlight', {
            textDocument: toTextDocument(document),
            position: toPosition(position)
          }, token).then(fromDocumentHighlights, fallbackProviderResult(output, 'document highlights', []));
        }
      }));
    }

    const legend = activeClient.semanticTokensLegend();
    if (legend) {
      this.add(vscode.languages.registerDocumentSemanticTokensProvider(selector, {
        provideDocumentSemanticTokens(document, token) {
          return activeClient.request('textDocument/semanticTokens/full', {
            textDocument: toTextDocument(document)
          }, token).then(fromSemanticTokens, fallbackProviderResult(output, 'semantic tokens', new vscode.SemanticTokens(new Uint32Array())));
        }
      }, legend));
    }
  }
}

function deactivate() {
  return client ? client.dispose() : undefined;
}

function fallbackProviderResult(output, featureName, fallback) {
  return (error) => {
    const message = error && error.message ? error.message : String(error);
    output.appendLine(`PSCP ${featureName} request failed: ${message}`);
    return fallback;
  };
}

function logClientError(output, featureName, error) {
  const message = error && error.message ? error.message : String(error);
  output.appendLine(`PSCP ${featureName} failed: ${message}`);
}

function getRequestTimeoutMs() {
  const config = vscode.workspace.getConfiguration('pscp');
  const value = Number(config.get('server.requestTimeoutMs'));
  return Number.isFinite(value) && value > 0 ? value : DEFAULT_REQUEST_TIMEOUT_MS;
}

class PscpClient {
  constructor(context, output, diagnostics, status) {
    this.context = context;
    this.output = output;
    this.diagnostics = diagnostics;
    this.status = status;
    this.process = null;
    this.buffer = Buffer.alloc(0);
    this.pending = new Map();
    this.changeTimers = new Map();
    this.nextRequestId = 1;
    this.ready = null;
    this.serverCapabilities = {};
    this.serverInfo = {};
    this.pscpExperimental = {};
    this.readyListeners = [];
    this.analyzingTimer = null;
    this.lastAnalysisMs = null;
  }

  // Guide §11.1: the client must read the legend the server announced instead of keeping its own copy.
  semanticTokensLegend() {
    const provider = this.serverCapabilities.semanticTokensProvider;
    if (!provider || !provider.legend || !Array.isArray(provider.legend.tokenTypes)) {
      return null;
    }

    return new vscode.SemanticTokensLegend(provider.legend.tokenTypes, provider.legend.tokenModifiers || []);
  }

  onReady(listener) {
    this.readyListeners.push(listener);
  }

  // Guide §4.3. A version mismatch never disables a standard LSP feature; it only turns off the PSCP-only
  // requests the server did not advertise, and says so in the status bar.
  _checkVersions() {
    if (!this.pscpExperimental.protocolVersion) {
      this.output.appendLine('The PSCP language server did not advertise experimental.pscp; using standard LSP features only.');
      return 'This language server predates v0.7: the PSCP-only features are off.';
    }

    if (this.pscpExperimental.protocolVersion < PSCP_PROTOCOL_VERSION) {
      this.output.appendLine(`The PSCP language server speaks protocol ${this.pscpExperimental.protocolVersion}; the extension expects ${PSCP_PROTOCOL_VERSION}.`);
      return `The language server speaks PSCP protocol ${this.pscpExperimental.protocolVersion}, the extension expects ${PSCP_PROTOCOL_VERSION}.`;
    }

    if (this.languageVersion && this.languageVersion !== EXPECTED_LANGUAGE_VERSION) {
      this.output.appendLine(`Language version ${this.languageVersion} from the server, ${EXPECTED_LANGUAGE_VERSION} expected by the extension.`);
    }

    return null;
  }

  // Guide §14.1: only the features the server advertises are used.
  supports(capability) {
    return !!this.serverCapabilities[capability];
  }

  supportsRequest(method) {
    return Array.isArray(this.pscpExperimental.requests) && this.pscpExperimental.requests.includes(method);
  }

  get languageVersion() {
    return this.pscpExperimental.languageVersion || null;
  }

  get toolVersion() {
    return this.pscpExperimental.toolVersion || (this.serverInfo && this.serverInfo.version) || null;
  }

  sendConfiguration() {
    this.notify('workspace/didChangeConfiguration', { settings: { pscp: getServerFeatureSettings() } });
  }

  async start() {
    if (this.ready) {
      return this.ready;
    }

    this.ready = this._start();
    try {
      await this.ready;
    } catch (error) {
      this.ready = null;
      throw error;
    }
  }

  async _start() {
    this._setStatus('starting');
    const launch = await resolveLanguageServerLaunch(this.context, this.output);
    this.serverPath = launch.label;
    this.output.appendLine(`Starting PSCP language server via ${launch.label}`);

    this.process = cp.spawn(launch.command, launch.args, {
      cwd: launch.cwd,
      env: launch.env,
      stdio: ['pipe', 'pipe', 'pipe']
    });

    this.process.stdout.on('data', (chunk) => this._handleData(chunk));
    this.process.stderr.on('data', (chunk) => this.output.append(chunk.toString()));
    this.process.on('exit', (code, signal) => this._handleExit(code, signal));
    this.process.on('error', (error) => this._handleExit(-1, error.message));

    const initializeResult = await this._request('initialize', {
      processId: process.pid,
      clientInfo: {
        name: 'vscode-pscp',
        version: extensionPackage.version
      },
      rootUri: vscode.workspace.workspaceFolders && vscode.workspace.workspaceFolders.length > 0
        ? vscode.workspace.workspaceFolders[0].uri.toString()
        : null,
      initializationOptions: getServerFeatureSettings(),
      capabilities: CLIENT_CAPABILITIES
    }, {
      ensureStarted: false,
      timeoutMs: INITIALIZE_TIMEOUT_MS
    });

    this.serverCapabilities = (initializeResult && initializeResult.capabilities) || {};
    this.serverInfo = (initializeResult && initializeResult.serverInfo) || {};
    this.pscpExperimental = (this.serverCapabilities.experimental && this.serverCapabilities.experimental.pscp) || {};
    this.versionWarning = this._checkVersions();
    this.notify('initialized', {});
    for (const listener of this.readyListeners) {
      try {
        listener(this);
      } catch (error) {
        this.output.appendLine(`PSCP feature registration failed: ${error.message}`);
      }
    }

    for (const document of vscode.workspace.textDocuments) {
      if (isPscpDocument(document)) {
        this.notifyDidOpen(document);
      }
    }

    this._setStatus(this.versionWarning ? 'warning' : 'ready');
    return initializeResult;
  }

  async restart() {
    await this.dispose();
    this.ready = null;
    await this.start();
  }

  async dispose() {
    for (const [id, pending] of this.pending.entries()) {
      pending.reject(new Error('PSCP language server stopped.'));
      this.pending.delete(id);
    }

    for (const timer of this.changeTimers.values()) {
      clearTimeout(timer);
    }
    this.changeTimers.clear();
    if (this.analyzingTimer) {
      clearTimeout(this.analyzingTimer);
      this.analyzingTimer = null;
    }

    if (this.process) {
      this.process.kill();
      this.process = null;
    }

    this.buffer = Buffer.alloc(0);
    this.ready = null;
    this._setStatus('stopped');
  }

  async request(method, params, token, options = {}) {
    return this._request(method, params, {
      ...options,
      token
    });
  }

  async _request(method, params, options = {}) {
    if (options.ensureStarted !== false) {
      await this.start();
    }

    if (!this.process || !this.process.stdin.writable) {
      throw new Error('PSCP language server is not running.');
    }

    const id = this.nextRequestId++;

    return new Promise((resolve, reject) => {
      let settled = false;
      let cancellationSubscription;
      const timeoutMs = Number.isFinite(options.timeoutMs) && options.timeoutMs > 0
        ? options.timeoutMs
        : getRequestTimeoutMs();
      const timer = setTimeout(() => {
        if (settled) {
          return;
        }

        settled = true;
        this.pending.delete(id);
        if (cancellationSubscription && typeof cancellationSubscription.dispose === 'function') {
          cancellationSubscription.dispose();
        }

        this.notify('$/cancelRequest', { id });
        reject(new Error(`${method} timed out after ${timeoutMs} ms.`));
      }, timeoutMs);

      const cleanup = () => {
        clearTimeout(timer);
        if (cancellationSubscription && typeof cancellationSubscription.dispose === 'function') {
          cancellationSubscription.dispose();
        }
      };

      const complete = (callback, value) => {
        if (settled) {
          return;
        }

        settled = true;
        cleanup();
        callback(value);
      };

      this.pending.set(id, {
        resolve: (value) => complete(resolve, value),
        reject: (error) => complete(reject, error)
      });

      if (options.token) {
        if (options.token.isCancellationRequested) {
          this.pending.delete(id);
          cleanup();
          reject(new Error(`${method} was cancelled.`));
          return;
        }

        cancellationSubscription = options.token.onCancellationRequested(() => {
          if (settled) {
            return;
          }

          settled = true;
          this.pending.delete(id);
          cleanup();
          this.notify('$/cancelRequest', { id });
          reject(new Error(`${method} was cancelled.`));
        });
      }

      this._send({
        jsonrpc: '2.0',
        id,
        method,
        params
      });
    });
  }

  async didOpen(document) {
    await this.start();
    this.notifyDidOpen(document);
  }

  async didChange(document) {
    await this.start();
    const key = document.uri.toString();
    const existing = this.changeTimers.get(key);
    if (existing) {
      clearTimeout(existing);
    }

    const timer = setTimeout(() => {
      this.changeTimers.delete(key);
      this.notify('textDocument/didChange', {
        textDocument: {
          uri: document.uri.toString(),
          version: document.version
        },
        contentChanges: [
          {
            text: document.getText()
          }
        ]
      });
    }, DID_CHANGE_DEBOUNCE_MS);
    this.changeTimers.set(key, timer);
  }

  async didClose(document) {
    if (!this.process) {
      return;
    }

    this.notify('textDocument/didClose', {
      textDocument: toTextDocument(document)
    });
  }

  notifyDidOpen(document) {
    this.notify('textDocument/didOpen', {
      textDocument: {
        uri: document.uri.toString(),
        languageId: document.languageId,
        version: document.version,
        text: document.getText()
      }
    });
  }

  notify(method, params) {
    if (!this.process) {
      return;
    }

    this._send({
      jsonrpc: '2.0',
      method,
      params
    });
  }

  _send(message) {
    if (!this.process || !this.process.stdin.writable) {
      return;
    }

    if (isTraceEnabled()) {
      this.output.appendLine(`--> ${message.method || `response ${message.id}`}`);
    }

    const body = Buffer.from(JSON.stringify(message), 'utf8');
    const header = Buffer.from(`Content-Length: ${body.length}\r\n\r\n`, 'ascii');
    this.process.stdin.write(Buffer.concat([header, body]));
  }

  _handleData(chunk) {
    this.buffer = Buffer.concat([this.buffer, chunk]);

    while (true) {
      const separator = this.buffer.indexOf('\r\n\r\n');
      if (separator < 0) {
        return;
      }

      const header = this.buffer.slice(0, separator).toString('ascii');
      const match = /Content-Length:\s*(\d+)/i.exec(header);
      if (!match) {
        this.buffer = Buffer.alloc(0);
        return;
      }

      const length = Number.parseInt(match[1], 10);
      const messageStart = separator + 4;
      if (this.buffer.length < messageStart + length) {
        return;
      }

      const body = this.buffer.slice(messageStart, messageStart + length).toString('utf8');
      this.buffer = this.buffer.slice(messageStart + length);

      let message;
      try {
        message = JSON.parse(body);
      } catch (error) {
        this.output.appendLine(`Failed to parse PSCP language server message: ${error.message}`);
        continue;
      }

      this._handleMessage(message);
    }
  }

  _handleMessage(message) {
    if (isTraceEnabled()) {
      this.output.appendLine(`<-- ${message.method || `response ${message.id}`}`);
    }

    // A request from the server carries both an id and a method. Leaving it unanswered would hang the
    // server, so an unknown one gets MethodNotFound (guide §14.1).
    if (Object.prototype.hasOwnProperty.call(message, 'id') && message.method) {
      this._respondToServerRequest(message);
      return;
    }

    if (Object.prototype.hasOwnProperty.call(message, 'id')) {
      const pending = this.pending.get(message.id);
      if (!pending) {
        return;
      }

      this.pending.delete(message.id);
      if (message.error) {
        pending.reject(new Error(message.error.message || 'Language server request failed.'));
      } else {
        pending.resolve(message.result);
      }

      return;
    }

    if (message.method === 'textDocument/publishDiagnostics') {
      this._handleDiagnostics(message.params);
    } else if (message.method === 'pscp/status') {
      this._handleStatus(message.params);
    } else if (message.method === 'window/logMessage' || message.method === 'window/showMessage') {
      const text = message.params && message.params.message ? message.params.message : JSON.stringify(message.params || {});
      this.output.appendLine(`Server: ${text}`);
    }
  }

  _respondToServerRequest(message) {
    if (message.method === 'workspace/configuration') {
      const items = (message.params && message.params.items) || [];
      const result = items.map((item) => {
        const config = vscode.workspace.getConfiguration(item.section ? undefined : 'pscp');
        return item.section ? config.get(item.section) ?? null : getServerFeatureSettings();
      });
      this._send({ jsonrpc: '2.0', id: message.id, result });
      return;
    }

    if (message.method === 'window/workDoneProgress/create') {
      this._send({ jsonrpc: '2.0', id: message.id, result: null });
      return;
    }

    this._send({
      jsonrpc: '2.0',
      id: message.id,
      error: { code: -32601, message: `Method not found: ${message.method}` }
    });
  }

  // Guide §13.3: show progress only for an analysis that lasts longer than 300 ms, so a normal keystroke
  // does not make the status bar flicker.
  _handleStatus(params) {
    const state = params && params.state;
    if (state === 'analyzing') {
      if (!this.analyzingTimer) {
        this.analyzingTimer = setTimeout(() => {
          this.analyzingTimer = null;
          this._setStatus('analyzing');
        }, ANALYZING_INDICATOR_DELAY_MS);
      }

      return;
    }

    if (this.analyzingTimer) {
      clearTimeout(this.analyzingTimer);
      this.analyzingTimer = null;
    }

    if (state === 'error') {
      this.output.appendLine(`Analysis failed: ${(params && params.message) || 'unknown error'}`);
      this._setStatus('warning');
      return;
    }

    this.lastAnalysisMs = params && typeof params.analysisMs === 'number' ? params.analysisMs : this.lastAnalysisMs;
    this._setStatus(this.versionWarning ? 'warning' : 'ready');
  }

  _handleDiagnostics(params) {
    const uri = vscode.Uri.parse(params.uri);
    const diagnostics = (params.diagnostics || []).map(fromDiagnostic);
    this.diagnostics.set(uri, diagnostics);
  }

  _handleExit(code, signal) {
    if (this.process) {
      this.output.appendLine(`PSCP language server exited (${code}, ${signal}).`);
    }

    for (const [id, pending] of this.pending.entries()) {
      pending.reject(new Error('PSCP language server exited.'));
      this.pending.delete(id);
    }

    this.process = null;
    this.ready = null;
    this._setStatus('stopped');
  }

  _setStatus(state) {
    if (!this.status) {
      return;
    }

    // Guide §14.8.
    if (state === 'ready') {
      const version = this.languageVersion;
      this.status.text = version ? `$(check) PSCP ${version}` : '$(check) PSCP';
      this.status.tooltip = [
        `Server: ${this.serverPath || 'resolved automatically'}`,
        `Tool version: ${this.toolVersion || 'unknown'}`,
        `Language version: ${version || 'unknown'}`,
        this.lastAnalysisMs === null ? 'Not analysed yet' : `Last analysis: ${this.lastAnalysisMs} ms`
      ].join('\n');
    } else if (state === 'starting') {
      this.status.text = '$(sync~spin) PSCP';
      this.status.tooltip = 'Starting PSCP language server.';
    } else if (state === 'analyzing') {
      this.status.text = '$(sync~spin) PSCP';
      this.status.tooltip = 'Analyzing the current PSCP file.';
    } else if (state === 'warning') {
      this.status.text = `$(warning) PSCP ${this.languageVersion || ''}`.trim();
      this.status.tooltip = this.versionWarning || 'The PSCP language server version does not match the extension.';
    } else {
      this.status.text = '$(error) PSCP';
      this.status.tooltip = 'PSCP language server is not running. Click for actions.';
    }
  }
}

function isPscpDocument(document) {
  return document.languageId === 'pscp' && (document.uri.scheme === 'file' || document.uri.scheme === 'untitled');
}

function createDotnetEnvironment(repoRoot) {
  return {
    ...process.env,
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE: '1',
    DOTNET_CLI_HOME: path.join(repoRoot, '.dotnet'),
    DOTNET_CLI_TELEMETRY_OPTOUT: '1'
  };
}

// Guide §14.8: `pscp.trace.server` is the standard setting; `pscp.server.trace` stays for compatibility.
function isTraceEnabled() {
  const config = vscode.workspace.getConfiguration('pscp');
  const trace = String(config.get('trace.server') || 'off');
  return trace !== 'off' || !!config.get('server.trace');
}

async function resolveLanguageServerLaunch(context, output) {
  const repoRoot = path.resolve(context.extensionPath, '..', '..');
  const env = createDotnetEnvironment(repoRoot);
  const serverProject = path.join(repoRoot, 'src', 'Pscp.LanguageServer', 'Pscp.LanguageServer.csproj');
  const serverDll = path.join(repoRoot, 'src', 'Pscp.LanguageServer', 'bin', 'Debug', 'net10.0', 'Pscp.LanguageServer.dll');
  const repoFallbackAvailable = fs.existsSync(serverProject);

  const config = vscode.workspace.getConfiguration('pscp');
  const configuredServerPath = normalizeConfiguredPath(config.get('server.path'));
  if (configuredServerPath) {
    const launch = createConfiguredServerLaunch(configuredServerPath, getConfiguredStringArray(config.get('server.args')));
    if (launch) {
      return launch;
    }
  }

  const explicitLanguageServer = normalizeConfiguredPath(config.get('languageServerPath'));
  if (explicitLanguageServer) {
    const launch = createDirectServerLaunch(explicitLanguageServer);
    if (launch) {
      return launch;
    }
  }

  const bundledLaunch = createBundledServerLaunch(context);
  if (bundledLaunch) {
    output.appendLine(`Using bundled PSCP language server: ${bundledLaunch.label}`);
    return bundledLaunch;
  }

  const explicitSdk = normalizeConfiguredPath(config.get('sdkPath'));
  if (explicitSdk) {
    const launch = createSdkLaunch(explicitSdk);
    if (launch) {
      noteSdkVersion(launch, output, repoFallbackAvailable, true);
      return launch;
    }
  }

  for (const candidate of getInstalledSdkCandidates()) {
    const launch = createSdkLaunch(candidate);
    if (launch && noteSdkVersion(launch, output, repoFallbackAvailable, false)) {
      return launch;
    }
  }

  const pathLaunch = createPathSdkLaunch();
  if (pathLaunch && noteSdkVersion(pathLaunch, output, repoFallbackAvailable, false)) {
    return pathLaunch;
  }

  if (!fs.existsSync(serverProject)) {
    throw new Error('Could not locate an installed PSCP SDK or a repository language server project.');
  }

  output.appendLine('Building repository PSCP language server...');
  await runProcess('dotnet', ['build', serverProject, '-nologo', '-v', 'q', '-p:UseSharedCompilation=false'], { cwd: repoRoot, env }, output);
  return {
    label: serverDll,
    command: 'dotnet',
    args: [serverDll],
    cwd: repoRoot,
    env
  };
}

function createConfiguredServerLaunch(candidate, args = []) {
  if (!candidate || !fs.existsSync(candidate)) {
    return null;
  }

  const stat = fs.statSync(candidate);
  if (stat.isDirectory()) {
    const directBinary = path.join(candidate, 'Pscp.LanguageServer');
    const directExe = path.join(candidate, 'Pscp.LanguageServer.exe');
    const directDll = path.join(candidate, 'Pscp.LanguageServer.dll');
    const sdkExecutable = path.join(candidate, getPscpExecutableName());
    return createDirectServerLaunch(directBinary, args)
      || createDirectServerLaunch(directExe, args)
      || createDirectServerLaunch(directDll, args)
      || createSdkLaunch(sdkExecutable);
  }

  if (path.basename(candidate).toLowerCase() === getPscpExecutableName()) {
    return createSdkLaunch(candidate);
  }

  return createDirectServerLaunch(candidate, args);
}

function createBundledServerLaunch(context) {
  const serverDirectory = path.join(context.extensionPath, 'server');
  return createDirectServerLaunch(path.join(serverDirectory, 'Pscp.LanguageServer.exe'))
    || createDirectServerLaunch(path.join(serverDirectory, 'Pscp.LanguageServer.dll'));
}

function createDirectServerLaunch(candidate, extraArgs = []) {
  if (!candidate || !fs.existsSync(candidate)) {
    return null;
  }

  if (candidate.toLowerCase().endsWith('.dll')) {
    return {
      label: candidate,
      command: 'dotnet',
      args: [candidate, ...extraArgs],
      cwd: path.dirname(candidate),
      env: process.env
    };
  }

  return {
    label: candidate,
    command: candidate,
    args: extraArgs,
    cwd: path.dirname(candidate),
    env: process.env
  };
}

function createSdkLaunch(candidate) {
  if (!candidate) {
    return null;
  }

  const sdkExecutable = fs.existsSync(candidate) && fs.statSync(candidate).isDirectory()
    ? path.join(candidate, getPscpExecutableName())
    : candidate;
  if (!fs.existsSync(sdkExecutable)) {
    return null;
  }

  return {
    label: sdkExecutable,
    command: sdkExecutable,
    args: ['lsp'],
    cwd: path.dirname(sdkExecutable),
    env: process.env,
    sdkExecutable
  };
}

function createPathSdkLaunch() {
  const finder = process.platform === 'win32' ? 'where.exe' : 'which';
  const result = cp.spawnSync(finder, ['pscp'], { windowsHide: true, encoding: 'utf8' });
  if (result.status !== 0 || !result.stdout) {
    return null;
  }

  const candidate = result.stdout.split(/\r?\n/).map((value) => value.trim()).find(Boolean);
  return candidate ? createSdkLaunch(candidate) : null;
}

function getPscpExecutableName() {
  return process.platform === 'win32' ? 'pscp.exe' : 'pscp';
}

function getInstalledSdkCandidates() {
  const candidates = [];
  if (process.env.LOCALAPPDATA) {
    candidates.push(path.join(process.env.LOCALAPPDATA, 'Programs', 'Pscp', 'pscp.exe'));
  }

  if (process.env.ProgramFiles) {
    candidates.push(path.join(process.env.ProgramFiles, 'Pscp', 'pscp.exe'));
  }

  return candidates;
}

function normalizeConfiguredPath(value) {
  return typeof value === 'string' && value.trim().length > 0
    ? path.resolve(value.trim())
    : '';
}

function getConfiguredStringArray(value) {
  return Array.isArray(value) ? value.filter((item) => typeof item === 'string') : [];
}

function runProcess(command, args, options, output) {
  return new Promise((resolve, reject) => {
    const child = cp.spawn(command, args, {
      ...options,
      stdio: ['ignore', 'pipe', 'pipe']
    });

    let stdio = '';
    child.stdout.on('data', (chunk) => {
      stdio += chunk.toString();
    });
    child.stderr.on('data', (chunk) => {
      stdio += chunk.toString();
    });
    child.on('error', reject);
    child.on('exit', (code) => {
      if (stdio.trim().length > 0) {
        output.append(stdio);
      }

      if (code === 0) {
        resolve();
      } else {
        reject(new Error(`${command} ${args.join(' ')} failed with exit code ${code}.`));
      }
    });
  });
}

function noteSdkVersion(launch, output, repoFallbackAvailable, treatAsExplicit) {
  const versionInfo = probeSdkVersion(launch);
  if (!versionInfo) {
    output.appendLine(`Using PSCP SDK without version probe: ${launch.label}`);
    return true;
  }

  output.appendLine(`Found PSCP SDK ${versionInfo.toolVersion} (language ${versionInfo.languageVersion}) via ${launch.label}`);
  if (treatAsExplicit) {
    return true;
  }

  if (compareVersions(versionInfo.toolVersion, extensionPackage.version) < 0) {
    output.appendLine(`Installed SDK ${versionInfo.toolVersion} is older than extension ${extensionPackage.version}.`);
    if (repoFallbackAvailable) {
      output.appendLine('Falling back to repository language server build for a newer server implementation.');
      return false;
    }
  }

  return true;
}

function probeSdkVersion(launch) {
  if (!launch.sdkExecutable) {
    return null;
  }

  const result = cp.spawnSync(launch.sdkExecutable, ['version'], {
    cwd: launch.cwd,
    env: launch.env,
    windowsHide: true,
    encoding: 'utf8'
  });

  if (result.status !== 0 || !result.stdout) {
    return null;
  }

  const match = /pscp CLI (\d+\.\d+\.\d+) \(language (\d+\.\d+)\)/i.exec(result.stdout);
  if (!match) {
    return null;
  }

  return {
    toolVersion: match[1],
    languageVersion: match[2]
  };
}

function compareVersions(left, right) {
  const leftParts = String(left).split('.').map((value) => Number.parseInt(value, 10) || 0);
  const rightParts = String(right).split('.').map((value) => Number.parseInt(value, 10) || 0);
  const length = Math.max(leftParts.length, rightParts.length);
  for (let index = 0; index < length; index += 1) {
    const delta = (leftParts[index] || 0) - (rightParts[index] || 0);
    if (delta !== 0) {
      return delta;
    }
  }

  return 0;
}

async function runPscpToolCommand(context, output, subcommand, extra = []) {
  // Guide §14.7: an untrusted workspace may have set `pscp.transpiler.args`, so the run commands stay off.
  if (!vscode.workspace.isTrusted) {
    vscode.window.showWarningMessage('PSCP run and transpile commands are disabled in a restricted workspace. Trust the folder to enable them.');
    return;
  }

  const editor = vscode.window.activeTextEditor;
  if (!editor || !isPscpDocument(editor.document)) {
    vscode.window.showWarningMessage('Open a .pscp file first.');
    return;
  }

  if (editor.document.isUntitled) {
    vscode.window.showWarningMessage('Save the .pscp file before running PSCP commands.');
    return;
  }

  if (editor.document.isDirty) {
    await editor.document.save();
  }

  const executable = resolvePscpExecutable(context);
  if (!executable) {
    vscode.window.showErrorMessage('Could not locate pscp.exe. Install the PSCP SDK or set pscp.transpiler.path / pscp.sdkPath.');
    return;
  }

  const config = vscode.workspace.getConfiguration('pscp');
  const extraArgs = getConfiguredStringArray(config.get('transpiler.args'));
  const toolArgs = [subcommand, editor.document.uri.fsPath, ...extra, ...extraArgs];
  const isDll = executable.toLowerCase().endsWith('.dll');
  const processPath = isDll ? 'dotnet' : executable;
  const processArgs = isDll ? [executable, ...toolArgs] : toolArgs;
  const cwd = path.dirname(editor.document.uri.fsPath);
  output.appendLine(`Running: ${[processPath, ...processArgs].map((value) => JSON.stringify(value)).join(' ')}`);

  // Run pscp as a process task rather than typing a command line into the user's shell: arguments are passed
  // verbatim (no shell quoting rules; PowerShell cannot invoke a quoted path without `&`), file names such as
  // `$(cmd).pscp` are never interpreted by a shell, and the task terminal stays open with the program output.
  const folder = vscode.workspace.getWorkspaceFolder(editor.document.uri);
  if (folder) {
    const task = new vscode.Task(
      { type: 'pscp', command: subcommand },
      folder,
      `PSCP ${subcommand}`,
      'pscp',
      new vscode.ProcessExecution(processPath, processArgs, { cwd }));
    task.presentationOptions = {
      reveal: vscode.TaskRevealKind.Always,
      focus: true,
      panel: vscode.TaskPanelKind.Dedicated,
      clear: true
    };
    const execution = await vscode.tasks.executeTask(task);
    if (subcommand === 'transpile') {
      await openGeneratedFileWhenDone(execution, editor.document.uri);
    }

    return;
  }

  // Files opened outside a workspace cannot host a task; fall back to the integrated terminal with quoting
  // that matches the user's default shell.
  const terminal = vscode.window.createTerminal({ name: `PSCP ${subcommand}`, cwd });
  terminal.sendText(buildShellCommandLine(vscode.env.shell, processPath, processArgs));
  terminal.show();
}

// Guide §14.4: `pscp transpile` writes `<file>.g.cs`; the command opens it once the task finishes.
async function openGeneratedFileWhenDone(execution, sourceUri) {
  const generated = vscode.Uri.file(`${sourceUri.fsPath}.g.cs`);
  await new Promise((resolve) => {
    const subscription = vscode.tasks.onDidEndTaskProcess((event) => {
      if (event.execution === execution) {
        subscription.dispose();
        resolve();
      }
    });
  });

  try {
    const document = await vscode.workspace.openTextDocument(generated);
    await vscode.window.showTextDocument(document, { preview: false });
  } catch {
    // `pscp transpile -o` may have been redirected elsewhere; the task terminal already says where.
  }
}

// Guide §14.5: `dir/name.pscp` is paired with `name.in`/`name.out` and `name.<k>.in`/`name.<k>.out`.
function findSamples(sourcePath) {
  const directory = path.dirname(sourcePath);
  const stem = path.basename(sourcePath, '.pscp');
  let entries;
  try {
    entries = fs.readdirSync(directory);
  } catch {
    return [];
  }

  const samples = [];
  for (const entry of entries.sort()) {
    if (!entry.endsWith('.in')) {
      continue;
    }

    const name = entry.slice(0, -'.in'.length);
    if (name !== stem && !(name.startsWith(`${stem}.`) && !name.slice(stem.length + 1).includes('.'))) {
      continue;
    }

    const expected = path.join(directory, `${name}.out`);
    samples.push({
      name,
      input: path.join(directory, entry),
      expected: fs.existsSync(expected) ? expected : null
    });
  }

  return samples;
}

async function runSamplesCommand(context, output, testController) {
  if (!vscode.workspace.isTrusted) {
    vscode.window.showWarningMessage('PSCP sample runs are disabled in a restricted workspace.');
    return;
  }

  const editor = vscode.window.activeTextEditor;
  if (!editor || !isPscpDocument(editor.document) || editor.document.isUntitled) {
    vscode.window.showWarningMessage('Open and save a .pscp file first.');
    return;
  }

  if (editor.document.isDirty) {
    await editor.document.save();
  }

  const samples = findSamples(editor.document.uri.fsPath);
  if (samples.length === 0) {
    vscode.window.showInformationMessage(`No samples next to ${path.basename(editor.document.uri.fsPath)}. Create one with "PSCP: New Sample".`);
    return;
  }

  const report = await runPscpTestJson(context, output, editor.document.uri.fsPath);
  if (!report) {
    return;
  }

  publishSampleResults(testController, editor.document.uri, report);
  if (!report.build || report.build.ok === false) {
    vscode.window.showErrorMessage('PSCP sample run failed to build. See the PSCP output channel.');
    return;
  }

  const results = report.samples || [];
  const passed = results.filter((sample) => sample.status === 'passed').length;
  const message = `PSCP samples: ${passed}/${results.length} passed.`;
  if (passed === results.length) {
    vscode.window.showInformationMessage(message);
  } else {
    vscode.window.showWarningMessage(message);
    output.show(true);
  }
}

// `pscp test --json` builds once and runs every sample (guide §14.5).
async function runPscpTestJson(context, output, sourcePath) {
  const executable = resolvePscpExecutable(context);
  if (!executable) {
    vscode.window.showErrorMessage('Could not locate pscp.exe. Install the PSCP SDK or set pscp.transpiler.path / pscp.sdkPath.');
    return null;
  }

  const config = vscode.workspace.getConfiguration('pscp');
  const configuration = config.get('samples.configuration') || 'Debug';
  const timeoutMs = Number(config.get('samples.timeoutMs')) || DEFAULT_SAMPLE_TIMEOUT_MS;
  const tolerance = config.get('samples.floatTolerance');
  const args = ['test', sourcePath, '--json', '-c', String(configuration), '--timeout', String(timeoutMs)];
  if (Number.isFinite(Number(tolerance)) && Number(tolerance) > 0) {
    args.push('--float-tolerance', String(tolerance));
  }

  const isDll = executable.toLowerCase().endsWith('.dll');
  const command = isDll ? 'dotnet' : executable;
  const commandArgs = isDll ? [executable, ...args] : args;
  output.appendLine(`Running: ${[command, ...commandArgs].map((value) => JSON.stringify(value)).join(' ')}`);

  return new Promise((resolve) => {
    const child = cp.spawn(command, commandArgs, { cwd: path.dirname(sourcePath) });
    let stdout = '';
    let stderr = '';
    child.stdout.on('data', (chunk) => { stdout += chunk.toString(); });
    child.stderr.on('data', (chunk) => { stderr += chunk.toString(); });
    child.on('error', (error) => {
      output.appendLine(`pscp test failed to start: ${error.message}`);
      resolve(null);
    });
    child.on('close', () => {
      if (stderr.trim().length > 0) {
        output.appendLine(stderr.trim());
      }

      try {
        resolve(JSON.parse(stdout));
      } catch (error) {
        output.appendLine(`Could not read the pscp test report: ${error.message}`);
        output.appendLine(stdout);
        resolve(null);
      }
    });
  });
}

// Guide §14.5: the results land in the Testing panel, with a diff on each failure.
function publishSampleResults(testController, sourceUri, report) {
  if (!testController) {
    return;
  }

  // `pscp test --json` names the sample files relative to the source directory.
  const directory = path.dirname(sourceUri.fsPath);
  const fileItem = testController.createTestItem(sourceUri.toString(), path.basename(sourceUri.fsPath), sourceUri);
  testController.items.add(fileItem);
  const run = testController.createTestRun(new vscode.TestRunRequest([fileItem]), 'PSCP samples', false);
  for (const sample of report.samples || []) {
    const item = testController.createTestItem(`${sourceUri.toString()}#${sample.name}`, sample.name, sourceUri);
    fileItem.children.add(item);
    run.started(item);
    if (sample.status === 'passed') {
      run.passed(item, sample.elapsedMs);
    } else if (sample.status === 'noExpected') {
      run.skipped(item);
      run.appendOutput(`${sample.name}: no expected output\r\n${(sample.actual || '').replace(/\n/g, '\r\n')}\r\n`);
    } else if (sample.status === 'timeout') {
      run.failed(item, new vscode.TestMessage(`Timed out after ${report.timeoutMs || ''} ms.`), sample.elapsedMs);
    } else {
      const message = sample.expected
        ? vscode.TestMessage.diff(`${sample.name} did not match ${path.basename(sample.expected)}`, readTextOrEmpty(path.resolve(directory, sample.expected)), sample.actual || '')
        : new vscode.TestMessage(sample.stderr || `${sample.name} exited with ${sample.exitCode}.`);
      run.failed(item, message, sample.elapsedMs);
    }
  }

  run.end();
}

function readTextOrEmpty(filePath) {
  try {
    return fs.readFileSync(filePath, 'utf8');
  } catch {
    return '';
  }
}

// Guide §14.4: `pscp run <file> --stdin-file <in>`, with the sample `.in` files offered first.
async function runWithInputCommand(context, output) {
  const editor = vscode.window.activeTextEditor;
  if (!editor || !isPscpDocument(editor.document) || editor.document.isUntitled) {
    vscode.window.showWarningMessage('Open and save a .pscp file first.');
    return;
  }

  const samples = findSamples(editor.document.uri.fsPath);
  const picks = samples.map((sample) => ({ label: path.basename(sample.input), description: sample.input }));
  picks.push({ label: 'Choose a file...', description: '' });
  const picked = await vscode.window.showQuickPick(picks, { title: 'PSCP: input file' });
  if (!picked) {
    return;
  }

  let inputPath = picked.description;
  if (!inputPath) {
    const chosen = await vscode.window.showOpenDialog({
      canSelectMany: false,
      openLabel: 'Use as stdin',
      defaultUri: vscode.Uri.file(path.dirname(editor.document.uri.fsPath))
    });
    if (!chosen || chosen.length === 0) {
      return;
    }

    inputPath = chosen[0].fsPath;
  }

  await runPscpToolCommand(context, output, 'run', ['--stdin-file', inputPath]);
}

// Guide §14.4: the next free `.in`/`.out` pair.
async function newSampleCommand() {
  const editor = vscode.window.activeTextEditor;
  if (!editor || !isPscpDocument(editor.document) || editor.document.isUntitled) {
    vscode.window.showWarningMessage('Open and save a .pscp file first.');
    return;
  }

  const directory = path.dirname(editor.document.uri.fsPath);
  const stem = path.basename(editor.document.uri.fsPath, '.pscp');
  let index = 1;
  while (fs.existsSync(path.join(directory, `${stem}.${index}.in`)) || fs.existsSync(path.join(directory, `${stem}.${index}.out`))) {
    index++;
  }

  const inputUri = vscode.Uri.file(path.join(directory, `${stem}.${index}.in`));
  const outputUri = vscode.Uri.file(path.join(directory, `${stem}.${index}.out`));
  const empty = new Uint8Array();
  await vscode.workspace.fs.writeFile(inputUri, empty);
  await vscode.workspace.fs.writeFile(outputUri, empty);
  await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(inputUri), { preview: false });
  await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(outputUri), {
    preview: false,
    viewColumn: vscode.ViewColumn.Beside
  });
}

// Guide §14.6: the generated C# opens as a read-only virtual document beside the source and follows it.
class GeneratedCSharpPreview {
  constructor(output) {
    this.output = output;
    this.contents = new Map();
    this.emitter = new vscode.EventEmitter();
    this.onDidChange = this.emitter.event;
    this.timers = new Map();
  }

  dispose() {
    for (const timer of this.timers.values()) {
      clearTimeout(timer);
    }

    this.timers.clear();
    this.emitter.dispose();
  }

  provideTextDocumentContent(uri) {
    return this.contents.get(uri.toString()) || '// The PSCP language server has not produced C# for this file yet.';
  }

  previewUriFor(sourceUri) {
    return vscode.Uri.parse(`${GENERATED_SCHEME}:${sourceUri.path}.g.cs`);
  }

  async show(activeClient, sourceUri) {
    if (!activeClient.supportsRequest('pscp/generatedCSharp')) {
      vscode.window.showWarningMessage('This PSCP language server does not support the generated C# preview.');
      return;
    }

    await this.refresh(activeClient, sourceUri);
    const previewUri = this.previewUriFor(sourceUri);
    const document = await vscode.workspace.openTextDocument(previewUri);
    await vscode.languages.setTextDocumentLanguage(document, 'csharp');
    await vscode.window.showTextDocument(document, {
      viewColumn: vscode.ViewColumn.Beside,
      preview: true,
      preserveFocus: true
    });
  }

  isOpen(sourceUri) {
    const previewUri = this.previewUriFor(sourceUri).toString();
    return vscode.workspace.textDocuments.some((document) => document.uri.toString() === previewUri);
  }

  scheduleRefresh(activeClient, sourceUri) {
    if (!this.isOpen(sourceUri)) {
      return;
    }

    const key = sourceUri.toString();
    const existing = this.timers.get(key);
    if (existing) {
      clearTimeout(existing);
    }

    this.timers.set(key, setTimeout(() => {
      this.timers.delete(key);
      this.refresh(activeClient, sourceUri).catch((error) => this.output.appendLine(`Generated C# preview failed: ${error.message}`));
    }, PREVIEW_DEBOUNCE_MS));
  }

  async refresh(activeClient, sourceUri) {
    const config = vscode.workspace.getConfiguration('pscp');
    const result = await activeClient.request('pscp/generatedCSharp', {
      textDocument: { uri: sourceUri.toString() },
      pretty: config.get('preview.pretty') !== false,
      explain: config.get('preview.explain') === true
    });
    const previewUri = this.previewUriFor(sourceUri);
    const key = previewUri.toString();
    if (result && typeof result.csharp === 'string' && result.csharp.length > 0) {
      this.contents.set(key, result.csharp);
    } else if (this.contents.has(key)) {
      // The source has errors: keep the last good C# and say which version it came from (guide §14.6).
      const stale = this.contents.get(key).replace(/^\/\/ The source has errors.*\n/, '');
      const version = result && result.version !== undefined ? result.version : 'the last successful analysis';
      this.contents.set(key, `// The source has errors right now; showing the result of version ${version}.\n${stale}`);
    } else {
      this.contents.set(key, '// The source has errors and no C# has been generated yet.');
    }

    this.emitter.fire(previewUri);
  }
}

function buildShellCommandLine(shell, command, args) {
  const shellName = (String(shell || '').split(/[\\/]/).pop() || '').toLowerCase();
  if (shellName.includes('pwsh') || shellName.includes('powershell')) {
    const quote = (value) => `'${String(value).replace(/'/g, "''")}'`;
    return ['&', quote(command), ...args.map(quote)].join(' ');
  }

  if (shellName === 'cmd.exe' || shellName === 'cmd') {
    // cmd has no escape for `%` inside quotes; `"` cannot appear in Windows file names.
    const quote = (value) => `"${String(value).replace(/%/g, '"%"')}"`;
    return [quote(command), ...args.map(quote)].join(' ');
  }

  const quote = (value) => `'${String(value).replace(/'/g, "'\\''")}'`;
  return [quote(command), ...args.map(quote)].join(' ');
}

function resolvePscpExecutable(context) {
  const config = vscode.workspace.getConfiguration('pscp');
  const configuredTranspiler = normalizeConfiguredPath(config.get('transpiler.path'));
  if (configuredTranspiler && fs.existsSync(configuredTranspiler)) {
    return fs.statSync(configuredTranspiler).isDirectory()
      ? path.join(configuredTranspiler, getPscpExecutableName())
      : configuredTranspiler;
  }

  const configuredServer = normalizeConfiguredPath(config.get('server.path'));
  if (configuredServer && fs.existsSync(configuredServer)) {
    const candidate = fs.statSync(configuredServer).isDirectory()
      ? path.join(configuredServer, getPscpExecutableName())
      : configuredServer;
    if (path.basename(candidate).toLowerCase() === getPscpExecutableName() && fs.existsSync(candidate)) {
      return candidate;
    }
  }

  const configuredSdk = normalizeConfiguredPath(config.get('sdkPath'));
  if (configuredSdk && fs.existsSync(configuredSdk)) {
    const candidate = fs.statSync(configuredSdk).isDirectory()
      ? path.join(configuredSdk, getPscpExecutableName())
      : configuredSdk;
    if (fs.existsSync(candidate)) {
      return candidate;
    }
  }

  for (const candidate of getInstalledSdkCandidates()) {
    const launch = createSdkLaunch(candidate);
    if (launch) {
      return launch.sdkExecutable;
    }
  }

  const pathLaunch = createPathSdkLaunch();
  if (pathLaunch) {
    return pathLaunch.sdkExecutable;
  }

  return null;
}

function toTextDocument(document) {
  return { uri: document.uri.toString() };
}

function toPosition(position) {
  return { line: position.line, character: position.character };
}

function toRange(range) {
  return {
    start: toPosition(range.start),
    end: toPosition(range.end)
  };
}

function toDiagnosticPayload(diagnostic) {
  return {
    range: toRange(diagnostic.range),
    severity: diagnostic.severity,
    code: diagnostic.code,
    source: diagnostic.source,
    message: diagnostic.message
  };
}

function fromCompletionList(result) {
  if (!result || !Array.isArray(result.items)) {
    return [];
  }

  return result.items.map((item) => {
    const label = item.labelDetails
      ? { label: item.label, detail: item.labelDetails.detail, description: item.labelDetails.description }
      : item.label;
    const completion = new vscode.CompletionItem(label, mapCompletionKind(item.kind));
    completion.detail = item.detail || undefined;
    completion.documentation = fromMarkupContent(item.documentation);
    completion.sortText = item.sortText || undefined;
    completion.filterText = item.filterText || undefined;
    completion.preselect = !!item.preselect;
    if (Array.isArray(item.tags) && item.tags.includes(1)) {
      completion.tags = [vscode.CompletionItemTag.Deprecated];
    }

    if (item.textEdit && item.textEdit.range) {
      completion.range = fromRange(item.textEdit.range);
    }

    const insert = item.textEdit ? item.textEdit.newText : item.insertText;
    if (insert) {
      completion.insertText = item.insertTextFormat === 2 ? new vscode.SnippetString(insert) : insert;
    }

    if (Array.isArray(item.additionalTextEdits) && item.additionalTextEdits.length > 0) {
      completion.additionalTextEdits = item.additionalTextEdits.map((edit) => new vscode.TextEdit(fromRange(edit.range), edit.newText));
    }

    return completion;
  });

// `documentation` is either a plain string or a MarkupContent (guide §14.1).
function fromMarkupContent(value) {
  if (!value) {
    return undefined;
  }

  if (typeof value === 'string') {
    return value;
  }

  return value.kind === 'markdown' ? new vscode.MarkdownString(value.value) : value.value;
}
}

function fromHover(result) {
  if (!result || !result.contents) {
    return null;
  }

  // `contents` is a MarkupContent, a MarkedString, or an array of MarkedStrings (guide §14.1).
  const parts = (Array.isArray(result.contents) ? result.contents : [result.contents])
    .map((content) => {
      if (typeof content === 'string') {
        return new vscode.MarkdownString(content);
      }

      if (content.language) {
        return new vscode.MarkdownString().appendCodeblock(content.value, content.language);
      }

      return new vscode.MarkdownString(content.value);
    })
    .filter((part) => part.value.length > 0);
  if (parts.length === 0) {
    return null;
  }

  return new vscode.Hover(parts, result.range ? fromRange(result.range) : undefined);
}

function fromDefinition(result) {
  if (!result) {
    return null;
  }

  if (Array.isArray(result)) {
    return result.map(fromLocation);
  }

  return fromLocation(result);
}

function fromLocations(result) {
  if (!Array.isArray(result)) {
    return [];
  }

  return result.map(fromLocation);
}

function fromLocation(result) {
  return new vscode.Location(vscode.Uri.parse(result.uri), fromRange(result.range));
}

function fromRange(range) {
  return new vscode.Range(
    new vscode.Position(range.start.line, range.start.character),
    new vscode.Position(range.end.line, range.end.character)
  );
}

function fromDiagnostic(result) {
  const diagnostic = new vscode.Diagnostic(fromRange(result.range), result.message, mapDiagnosticSeverity(result.severity));
  diagnostic.source = result.source || 'pscp';
  // `codeDescription.href` turns the code into a link to the spec section (guide §6.3).
  if (result.code && result.codeDescription && result.codeDescription.href) {
    diagnostic.code = {
      value: result.code,
      target: vscode.Uri.parse(result.codeDescription.href)
    };
  } else {
    diagnostic.code = result.code || undefined;
  }

  if (Array.isArray(result.tags) && result.tags.length > 0) {
    diagnostic.tags = result.tags
      .map((tag) => (tag === 1 ? vscode.DiagnosticTag.Unnecessary : tag === 2 ? vscode.DiagnosticTag.Deprecated : null))
      .filter((tag) => tag !== null);
  }

  if (Array.isArray(result.relatedInformation)) {
    diagnostic.relatedInformation = result.relatedInformation.map((info) => new vscode.DiagnosticRelatedInformation(
      fromLocation(info.location),
      info.message
    ));
  }

  return diagnostic;
}

function fromDocumentSymbols(result) {
  if (!Array.isArray(result)) {
    return [];
  }

  return result.map((item) => {
    const symbol = new vscode.DocumentSymbol(
      item.name,
      item.detail || '',
      mapSymbolKind(item.kind),
      fromRange(item.range),
      fromRange(item.selectionRange)
    );
    if (Array.isArray(item.tags) && item.tags.includes(1)) {
      symbol.tags = [vscode.SymbolTag.Deprecated];
    }

    symbol.children = Array.isArray(item.children) ? fromDocumentSymbols(item.children) : [];
    return symbol;
  });
}

function fromSignatureHelp(result) {
  if (!result || !Array.isArray(result.signatures) || result.signatures.length === 0) {
    return null;
  }

  const help = new vscode.SignatureHelp();
  help.activeSignature = result.activeSignature || 0;
  help.activeParameter = result.activeParameter || 0;
  help.signatures = result.signatures.map((signature) => {
    const info = new vscode.SignatureInformation(signature.label, signature.documentation || undefined);
    info.parameters = Array.isArray(signature.parameters)
      ? signature.parameters.map((parameter) => new vscode.ParameterInformation(parameter.label))
      : [];
    return info;
  });
  return help;
}

function fromWorkspaceEdit(result) {
  if (!result || !result.changes) {
    return null;
  }

  const edit = new vscode.WorkspaceEdit();
  for (const [uri, changes] of Object.entries(result.changes)) {
    for (const change of changes) {
      edit.replace(vscode.Uri.parse(uri), fromRange(change.range), change.newText);
    }
  }

  return edit;
}

function fromPrepareRename(result) {
  if (!result || !result.range) {
    return null;
  }

  return {
    range: fromRange(result.range),
    placeholder: result.placeholder || undefined
  };
}

function fromInlayHints(result) {
  if (!Array.isArray(result)) {
    return [];
  }

  return result.map((hint) => {
    // `label` is a string or an array of InlayHintLabelParts (guide §14.1).
    const label = Array.isArray(hint.label)
      ? hint.label.map((part) => {
        const labelPart = new vscode.InlayHintLabelPart(part.value);
        labelPart.tooltip = fromMarkupContent(part.tooltip);
        if (part.location) {
          labelPart.location = fromLocation(part.location);
        }

        return labelPart;
      })
      : hint.label;
    const inlay = new vscode.InlayHint(
      new vscode.Position(hint.position.line, hint.position.character),
      label,
      hint.kind === 2 ? vscode.InlayHintKind.Parameter : vscode.InlayHintKind.Type
    );
    inlay.paddingLeft = !!hint.paddingLeft;
    inlay.paddingRight = !!hint.paddingRight;
    inlay.tooltip = fromMarkupContent(hint.tooltip);
    if (Array.isArray(hint.textEdits)) {
      inlay.textEdits = hint.textEdits.map((edit) => new vscode.TextEdit(fromRange(edit.range), edit.newText));
    }

    return inlay;
  });
}

function fromCodeActions(result) {
  if (!Array.isArray(result)) {
    return [];
  }

  return result.map((item) => {
    // The server sends the kind as an LSP string; vscode.CodeAction needs a CodeActionKind (guide §14.1).
    const kind = item.kind
      ? vscode.CodeActionKind.Empty.append(item.kind)
      : vscode.CodeActionKind.QuickFix;
    const action = new vscode.CodeAction(item.title, kind);
    action.edit = fromWorkspaceEdit(item.edit);
    action.isPreferred = !!item.isPreferred;
    if (Array.isArray(item.diagnostics)) {
      action.diagnostics = item.diagnostics.map(fromDiagnostic);
    }

    if (item.command) {
      action.command = {
        command: item.command.command,
        title: item.command.title || item.title,
        arguments: item.command.arguments
      };
    }

    return action;
  });
}

function fromFoldingRanges(result) {
  if (!Array.isArray(result)) {
    return [];
  }

  return result.map((range) => new vscode.FoldingRange(
    range.startLine,
    range.endLine,
    range.kind === 'comment' ? vscode.FoldingRangeKind.Comment
      : range.kind === 'imports' ? vscode.FoldingRangeKind.Imports
        : range.kind === 'region' ? vscode.FoldingRangeKind.Region
          : undefined
  ));
}

function fromSelectionRanges(result) {
  if (!Array.isArray(result)) {
    return [];
  }

  // The server sends each chain innermost-first with a `parent` link; vscode.SelectionRange nests the same way.
  return result.map((node) => {
    const chain = [];
    for (let current = node; current; current = current.parent) {
      chain.push(fromRange(current.range));
    }

    let selection;
    for (let i = chain.length - 1; i >= 0; i--) {
      selection = new vscode.SelectionRange(chain[i], selection);
    }

    return selection;
  }).filter((selection) => selection !== undefined);
}

function fromDocumentHighlights(result) {
  if (!Array.isArray(result)) {
    return [];
  }

  return result.map((highlight) => new vscode.DocumentHighlight(
    fromRange(highlight.range),
    highlight.kind === 3 ? vscode.DocumentHighlightKind.Write
      : highlight.kind === 1 ? vscode.DocumentHighlightKind.Text
        : vscode.DocumentHighlightKind.Read
  ));
}

function fromSemanticTokens(result) {
  return new vscode.SemanticTokens(new Uint32Array((result && result.data) || []));
}

function mapDiagnosticSeverity(severity) {
  switch (severity) {
    case 1:
      return vscode.DiagnosticSeverity.Error;
    case 2:
      return vscode.DiagnosticSeverity.Warning;
    case 3:
      return vscode.DiagnosticSeverity.Information;
    case 4:
      return vscode.DiagnosticSeverity.Hint;
    default:
      return vscode.DiagnosticSeverity.Information;
  }
}

// LSP SymbolKind is 1-based and vscode.SymbolKind is 0-based, so the number needs shifting (guide §14.1).
function mapSymbolKind(kind) {
  const shifted = Number.isInteger(kind) ? kind - 1 : vscode.SymbolKind.Variable;
  return shifted >= 0 && shifted <= vscode.SymbolKind.TypeParameter ? shifted : vscode.SymbolKind.Variable;
}

function mapCompletionKind(kind) {
  switch (kind) {
    case 2:
      return vscode.CompletionItemKind.Method;
    case 3:
      return vscode.CompletionItemKind.Function;
    case 6:
      return vscode.CompletionItemKind.Variable;
    case 7:
      return vscode.CompletionItemKind.Class;
    case 9:
      return vscode.CompletionItemKind.Module;
    case 10:
      return vscode.CompletionItemKind.Property;
    case 14:
      return vscode.CompletionItemKind.Keyword;
    case 15:
      return vscode.CompletionItemKind.Snippet;
    default:
      return vscode.CompletionItemKind.Text;
  }
}

module.exports = {
  activate,
  deactivate
};
