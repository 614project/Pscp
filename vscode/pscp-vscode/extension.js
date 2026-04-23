const vscode = require('vscode');
const cp = require('child_process');
const fs = require('fs');
const path = require('path');
const extensionPackage = require('./package.json');

const DEFAULT_REQUEST_TIMEOUT_MS = 8000;
const INITIALIZE_TIMEOUT_MS = 15000;
const DID_CHANGE_DEBOUNCE_MS = 120;

const SEMANTIC_TOKEN_TYPES = [
  'namespace',
  'type',
  'class',
  'enum',
  'interface',
  'struct',
  'typeParameter',
  'parameter',
  'variable',
  'property',
  'enumMember',
  'event',
  'function',
  'method',
  'macro',
  'keyword',
  'modifier',
  'comment',
  'string',
  'number',
  'regexp',
  'operator'
];

const SEMANTIC_TOKEN_MODIFIERS = [
  'declaration',
  'definition',
  'readonly',
  'static',
  'deprecated',
  'abstract',
  'async',
  'modification',
  'documentation',
  'defaultLibrary',
  'mutable'
];

let client;

async function activate(context) {
  const output = vscode.window.createOutputChannel('PSCP');
  const diagnostics = vscode.languages.createDiagnosticCollection('pscp');
  const status = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 100);
  const selector = [{ language: 'pscp' }];
  status.command = 'pscp.showServerLog';
  status.text = 'PSCP: starting';
  status.show();
  client = new PscpClient(context, output, diagnostics, status);

  context.subscriptions.push(output, diagnostics, status);
  context.subscriptions.push(vscode.commands.registerCommand('pscp.showServerLog', () => {
    output.show(true);
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

  context.subscriptions.push(vscode.workspace.onDidChangeConfiguration(async (event) => {
    if (event.affectsConfiguration('pscp.server')
      || event.affectsConfiguration('pscp.sdkPath')
      || event.affectsConfiguration('pscp.languageServerPath')) {
      output.appendLine('PSCP configuration changed; restarting language server.');
      await client.restart();
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
    }
  }));

  context.subscriptions.push(vscode.workspace.onDidCloseTextDocument((document) => {
    if (isPscpDocument(document)) {
      client.didClose(document).catch((error) => logClientError(output, 'didClose', error));
    }
  }));

  context.subscriptions.push(vscode.languages.registerCompletionItemProvider(selector, {
    provideCompletionItems(document, position, token) {
      return client.request('textDocument/completion', {
        textDocument: toTextDocument(document),
        position: toPosition(position)
      }, token).then(fromCompletionList, fallbackProviderResult(output, 'completion', []));
    }
  }, '.'));

  context.subscriptions.push(vscode.languages.registerHoverProvider(selector, {
    provideHover(document, position, token) {
      return client.request('textDocument/hover', {
        textDocument: toTextDocument(document),
        position: toPosition(position)
      }, token).then(fromHover, fallbackProviderResult(output, 'hover', null));
    }
  }));

  context.subscriptions.push(vscode.languages.registerDefinitionProvider(selector, {
    provideDefinition(document, position, token) {
      return client.request('textDocument/definition', {
        textDocument: toTextDocument(document),
        position: toPosition(position)
      }, token).then(fromDefinition, fallbackProviderResult(output, 'definition', null));
    }
  }));

  context.subscriptions.push(vscode.languages.registerReferenceProvider(selector, {
    provideReferences(document, position, contextInfo, token) {
      return client.request('textDocument/references', {
        textDocument: toTextDocument(document),
        position: toPosition(position),
        context: { includeDeclaration: contextInfo.includeDeclaration }
      }, token).then(fromLocations, fallbackProviderResult(output, 'references', []));
    }
  }));

  context.subscriptions.push(vscode.languages.registerDocumentSymbolProvider(selector, {
    provideDocumentSymbols(document, token) {
      return client.request('textDocument/documentSymbol', {
        textDocument: toTextDocument(document)
      }, token).then(fromDocumentSymbols, fallbackProviderResult(output, 'document symbols', []));
    }
  }));

  context.subscriptions.push(vscode.languages.registerSignatureHelpProvider(selector, {
    provideSignatureHelp(document, position, token) {
      return client.request('textDocument/signatureHelp', {
        textDocument: toTextDocument(document),
        position: toPosition(position)
      }, token).then(fromSignatureHelp, fallbackProviderResult(output, 'signature help', null));
    }
  }, '(', ','));

  context.subscriptions.push(vscode.languages.registerRenameProvider(selector, {
    prepareRename(document, position, token) {
      return client.request('textDocument/prepareRename', {
        textDocument: toTextDocument(document),
        position: toPosition(position)
      }, token).then(fromPrepareRename, fallbackProviderResult(output, 'prepare rename', null));
    },
    provideRenameEdits(document, position, newName, token) {
      return client.request('textDocument/rename', {
        textDocument: toTextDocument(document),
        position: toPosition(position),
        newName
      }, token, { timeoutMs: 12000 }).then(fromWorkspaceEdit, fallbackProviderResult(output, 'rename', null));
    }
  }));

  context.subscriptions.push(vscode.languages.registerInlayHintsProvider(selector, {
    provideInlayHints(document, range, token) {
      return client.request('textDocument/inlayHint', {
        textDocument: toTextDocument(document),
        range: toRange(range)
      }, token).then(fromInlayHints, fallbackProviderResult(output, 'inlay hints', []));
    }
  }));

  context.subscriptions.push(vscode.languages.registerCodeActionsProvider(selector, {
    provideCodeActions(document, range, contextInfo, token) {
      return client.request('textDocument/codeAction', {
        textDocument: toTextDocument(document),
        range: toRange(range),
        context: {
          diagnostics: contextInfo.diagnostics.map(toDiagnosticPayload)
        }
      }, token).then(fromCodeActions, fallbackProviderResult(output, 'code actions', []));
    }
  }));

  const legend = new vscode.SemanticTokensLegend(SEMANTIC_TOKEN_TYPES, SEMANTIC_TOKEN_MODIFIERS);
  context.subscriptions.push(vscode.languages.registerDocumentSemanticTokensProvider(selector, {
    provideDocumentSemanticTokens(document, token) {
      return client.request('textDocument/semanticTokens/full', {
        textDocument: toTextDocument(document)
      }, token).then(fromSemanticTokens, fallbackProviderResult(output, 'semantic tokens', new vscode.SemanticTokens(new Uint32Array())));
    }
  }, legend));

  try {
    output.appendLine(`Activating PSCP extension ${extensionPackage.version}.`);
    await client.start();
  } catch (error) {
    output.appendLine(String(error));
    vscode.window.showErrorMessage(`Failed to start PSCP language server: ${error.message}`);
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
      capabilities: {}
    }, {
      ensureStarted: false,
      timeoutMs: INITIALIZE_TIMEOUT_MS
    });

    this.notify('initialized', {});
    for (const document of vscode.workspace.textDocuments) {
      if (isPscpDocument(document)) {
        this.notifyDidOpen(document);
      }
    }

    this._setStatus('ready');
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
    } else if (message.method === 'window/logMessage' || message.method === 'window/showMessage') {
      const text = message.params && message.params.message ? message.params.message : JSON.stringify(message.params || {});
      this.output.appendLine(`Server: ${text}`);
    }
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

    if (state === 'ready') {
      this.status.text = 'PSCP: ready';
      this.status.tooltip = 'PSCP language server is running.';
    } else if (state === 'starting') {
      this.status.text = 'PSCP: starting';
      this.status.tooltip = 'Starting PSCP language server.';
    } else {
      this.status.text = 'PSCP: stopped';
      this.status.tooltip = 'PSCP language server is not running. Click to open logs.';
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

function isTraceEnabled() {
  const config = vscode.workspace.getConfiguration('pscp');
  return !!config.get('server.trace');
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
    const directExe = path.join(candidate, 'Pscp.LanguageServer.exe');
    const directDll = path.join(candidate, 'Pscp.LanguageServer.dll');
    const sdkExe = path.join(candidate, 'pscp.exe');
    return createDirectServerLaunch(directExe, args)
      || createDirectServerLaunch(directDll, args)
      || createSdkLaunch(sdkExe);
  }

  if (path.basename(candidate).toLowerCase() === 'pscp.exe') {
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
    ? path.join(candidate, 'pscp.exe')
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
  const where = cp.spawnSync('where.exe', ['pscp'], { windowsHide: true, encoding: 'utf8' });
  if (where.status !== 0 || !where.stdout) {
    return null;
  }

  const candidate = where.stdout.split(/\r?\n/).map((value) => value.trim()).find(Boolean);
  return candidate ? createSdkLaunch(candidate) : null;
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

async function runPscpToolCommand(context, output, subcommand) {
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
  const terminal = vscode.window.createTerminal(`PSCP ${subcommand}`);
  const command = [
    quoteShell(executable),
    subcommand,
    quoteShell(editor.document.uri.fsPath),
    ...extraArgs.map(quoteShell)
  ].join(' ');
  output.appendLine(`Running: ${command}`);
  terminal.sendText(command);
  terminal.show();
}

function resolvePscpExecutable(context) {
  const config = vscode.workspace.getConfiguration('pscp');
  const configuredTranspiler = normalizeConfiguredPath(config.get('transpiler.path'));
  if (configuredTranspiler && fs.existsSync(configuredTranspiler)) {
    return fs.statSync(configuredTranspiler).isDirectory()
      ? path.join(configuredTranspiler, 'pscp.exe')
      : configuredTranspiler;
  }

  const configuredServer = normalizeConfiguredPath(config.get('server.path'));
  if (configuredServer && fs.existsSync(configuredServer)) {
    const candidate = fs.statSync(configuredServer).isDirectory()
      ? path.join(configuredServer, 'pscp.exe')
      : configuredServer;
    if (path.basename(candidate).toLowerCase() === 'pscp.exe' && fs.existsSync(candidate)) {
      return candidate;
    }
  }

  const configuredSdk = normalizeConfiguredPath(config.get('sdkPath'));
  if (configuredSdk && fs.existsSync(configuredSdk)) {
    const candidate = fs.statSync(configuredSdk).isDirectory()
      ? path.join(configuredSdk, 'pscp.exe')
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

function quoteShell(value) {
  return `"${String(value).replace(/"/g, '\\"')}"`;
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
    const completion = new vscode.CompletionItem(item.label, mapCompletionKind(item.kind));
    completion.detail = item.detail || undefined;
    completion.documentation = item.documentation && item.documentation.value ? new vscode.MarkdownString(item.documentation.value) : undefined;
    completion.sortText = item.sortText || undefined;
    if (item.insertTextFormat === 2 && item.insertText) {
      completion.insertText = new vscode.SnippetString(item.insertText);
    } else if (item.insertText) {
      completion.insertText = item.insertText;
    }

    return completion;
  });
}

function fromHover(result) {
  if (!result || !result.contents) {
    return null;
  }

  const value = typeof result.contents === 'string'
    ? result.contents
    : result.contents.value;
  return new vscode.Hover(new vscode.MarkdownString(value), result.range ? fromRange(result.range) : undefined);
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
  diagnostic.code = result.code || undefined;
  return diagnostic;
}

function fromDocumentSymbols(result) {
  if (!Array.isArray(result)) {
    return [];
  }

  return result.map((item) => {
    const symbol = new vscode.DocumentSymbol(
      item.name,
      '',
      item.kind || vscode.SymbolKind.Variable,
      fromRange(item.range),
      fromRange(item.selectionRange)
    );
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
    const inlay = new vscode.InlayHint(
      new vscode.Position(hint.position.line, hint.position.character),
      hint.label,
      hint.kind === 2 ? vscode.InlayHintKind.Parameter : vscode.InlayHintKind.Type
    );
    inlay.paddingLeft = !!hint.paddingLeft;
    inlay.paddingRight = !!hint.paddingRight;
    return inlay;
  });
}

function fromCodeActions(result) {
  if (!Array.isArray(result)) {
    return [];
  }

  return result.map((item) => {
    const action = new vscode.CodeAction(item.title, item.kind || vscode.CodeActionKind.QuickFix);
    action.edit = fromWorkspaceEdit(item.edit);
    return action;
  });
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
