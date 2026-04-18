const vscode = require('vscode');
const cp = require('child_process');
const fs = require('fs');
const path = require('path');
const extensionPackage = require('./package.json');

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
  const selector = [{ language: 'pscp' }];
  client = new PscpClient(context, output, diagnostics);

  context.subscriptions.push(output, diagnostics);
  context.subscriptions.push(vscode.commands.registerCommand('pscp.restartLanguageServer', async () => {
    await client.restart();
    vscode.window.showInformationMessage('PSCP language server restarted.');
  }));

  context.subscriptions.push(vscode.workspace.onDidOpenTextDocument((document) => {
    if (isPscpDocument(document)) {
      client.didOpen(document);
    }
  }));

  context.subscriptions.push(vscode.workspace.onDidChangeTextDocument((event) => {
    if (isPscpDocument(event.document)) {
      client.didChange(event.document);
    }
  }));

  context.subscriptions.push(vscode.workspace.onDidCloseTextDocument((document) => {
    if (isPscpDocument(document)) {
      client.didClose(document);
    }
  }));

  context.subscriptions.push(vscode.languages.registerCompletionItemProvider(selector, {
    provideCompletionItems(document, position) {
      return client.request('textDocument/completion', {
        textDocument: toTextDocument(document),
        position: toPosition(position)
      }).then(fromCompletionList);
    }
  }, '.'));

  context.subscriptions.push(vscode.languages.registerHoverProvider(selector, {
    provideHover(document, position) {
      return client.request('textDocument/hover', {
        textDocument: toTextDocument(document),
        position: toPosition(position)
      }).then(fromHover);
    }
  }));

  context.subscriptions.push(vscode.languages.registerDefinitionProvider(selector, {
    provideDefinition(document, position) {
      return client.request('textDocument/definition', {
        textDocument: toTextDocument(document),
        position: toPosition(position)
      }).then(fromDefinition);
    }
  }));

  context.subscriptions.push(vscode.languages.registerReferenceProvider(selector, {
    provideReferences(document, position, contextInfo) {
      return client.request('textDocument/references', {
        textDocument: toTextDocument(document),
        position: toPosition(position),
        context: { includeDeclaration: contextInfo.includeDeclaration }
      }).then(fromLocations);
    }
  }));

  context.subscriptions.push(vscode.languages.registerDocumentSymbolProvider(selector, {
    provideDocumentSymbols(document) {
      return client.request('textDocument/documentSymbol', {
        textDocument: toTextDocument(document)
      }).then(fromDocumentSymbols);
    }
  }));

  context.subscriptions.push(vscode.languages.registerSignatureHelpProvider(selector, {
    provideSignatureHelp(document, position) {
      return client.request('textDocument/signatureHelp', {
        textDocument: toTextDocument(document),
        position: toPosition(position)
      }).then(fromSignatureHelp);
    }
  }, '(', ','));

  context.subscriptions.push(vscode.languages.registerRenameProvider(selector, {
    provideRenameEdits(document, position, newName) {
      return client.request('textDocument/rename', {
        textDocument: toTextDocument(document),
        position: toPosition(position),
        newName
      }).then(fromWorkspaceEdit);
    }
  }));

  context.subscriptions.push(vscode.languages.registerInlayHintsProvider(selector, {
    provideInlayHints(document, range) {
      return client.request('textDocument/inlayHint', {
        textDocument: toTextDocument(document),
        range: toRange(range)
      }).then(fromInlayHints);
    }
  }));

  context.subscriptions.push(vscode.languages.registerCodeActionsProvider(selector, {
    provideCodeActions(document, range, contextInfo) {
      return client.request('textDocument/codeAction', {
        textDocument: toTextDocument(document),
        range: toRange(range),
        context: {
          diagnostics: contextInfo.diagnostics.map(toDiagnosticPayload)
        }
      }).then(fromCodeActions);
    }
  }));

  const legend = new vscode.SemanticTokensLegend(SEMANTIC_TOKEN_TYPES, SEMANTIC_TOKEN_MODIFIERS);
  context.subscriptions.push(vscode.languages.registerDocumentSemanticTokensProvider(selector, {
    provideDocumentSemanticTokens(document) {
      return client.request('textDocument/semanticTokens/full', {
        textDocument: toTextDocument(document)
      }).then(fromSemanticTokens);
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

class PscpClient {
  constructor(context, output, diagnostics) {
    this.context = context;
    this.output = output;
    this.diagnostics = diagnostics;
    this.process = null;
    this.buffer = Buffer.alloc(0);
    this.pending = new Map();
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

    const initializeResult = await this.request('initialize', {
      processId: process.pid,
      clientInfo: {
        name: 'vscode-pscp',
        version: extensionPackage.version
      },
      rootUri: vscode.workspace.workspaceFolders && vscode.workspace.workspaceFolders.length > 0
        ? vscode.workspace.workspaceFolders[0].uri.toString()
        : null,
      capabilities: {}
    });

    this.notify('initialized', {});
    for (const document of vscode.workspace.textDocuments) {
      if (isPscpDocument(document)) {
        this.notifyDidOpen(document);
      }
    }

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

    if (this.process) {
      this.process.kill();
      this.process = null;
    }

    this.buffer = Buffer.alloc(0);
    this.ready = null;
  }

  async request(method, params) {
    await this.start();
    const id = this.nextRequestId++;

    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
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

      const message = JSON.parse(body);
      this._handleMessage(message);
    }
  }

  _handleMessage(message) {
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

async function resolveLanguageServerLaunch(context, output) {
  const repoRoot = path.resolve(context.extensionPath, '..', '..');
  const env = createDotnetEnvironment(repoRoot);
  const serverProject = path.join(repoRoot, 'src', 'Pscp.LanguageServer', 'Pscp.LanguageServer.csproj');
  const serverDll = path.join(repoRoot, 'src', 'Pscp.LanguageServer', 'bin', 'Debug', 'net10.0', 'Pscp.LanguageServer.dll');
  const repoFallbackAvailable = fs.existsSync(serverProject);

  const config = vscode.workspace.getConfiguration('pscp');
  const explicitLanguageServer = normalizeConfiguredPath(config.get('languageServerPath'));
  if (explicitLanguageServer) {
    const launch = createDirectServerLaunch(explicitLanguageServer);
    if (launch) {
      return launch;
    }
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

function createDirectServerLaunch(candidate) {
  if (!candidate || !fs.existsSync(candidate)) {
    return null;
  }

  if (candidate.toLowerCase().endsWith('.dll')) {
    return {
      label: candidate,
      command: 'dotnet',
      args: [candidate],
      cwd: path.dirname(candidate),
      env: process.env
    };
  }

  return {
    label: candidate,
    command: candidate,
    args: [],
    cwd: path.dirname(candidate),
    env: process.env,
    sdkExecutable: candidate
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
