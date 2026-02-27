import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions,
    TransportKind
} from 'vscode-languageclient/node';

let client: LanguageClient | undefined;
let outputChannel: vscode.OutputChannel;

export function activate(context: vscode.ExtensionContext): void {
    outputChannel = vscode.window.createOutputChannel('Calor Language Server');

    const config = vscode.workspace.getConfiguration('calor');
    const enabled = config.get<boolean>('languageServer.enabled', true);

    if (!enabled) {
        outputChannel.appendLine('Calor Language Server is disabled');
        return;
    }

    const serverPath = findServerPath(context, config);
    if (!serverPath) {
        outputChannel.appendLine('Could not find calor-lsp. Please set calor.languageServer.path or add calor-lsp to your PATH.');
        vscode.window.showWarningMessage('Calor Language Server not found. Configure calor.languageServer.path in settings.');
        return;
    }

    outputChannel.appendLine(`Starting Calor Language Server: ${serverPath.command} ${serverPath.args?.join(' ') || ''}`);

    const serverOptions: ServerOptions = {
        command: serverPath.command,
        args: serverPath.args,
        transport: TransportKind.stdio
    };

    const clientOptions: LanguageClientOptions = {
        documentSelector: [{ scheme: 'file', language: 'calor' }],
        synchronize: {
            fileEvents: vscode.workspace.createFileSystemWatcher('**/*.calr')
        },
        outputChannel: outputChannel
    };

    client = new LanguageClient(
        'calorLanguageServer',
        'Calor Language Server',
        serverOptions,
        clientOptions
    );

    client.start().then(() => {
        outputChannel.appendLine('Calor Language Server started successfully');
    }).catch((error) => {
        outputChannel.appendLine(`Failed to start Calor Language Server: ${error}`);
        vscode.window.showErrorMessage(`Failed to start Calor Language Server: ${error.message}`);
    });
}

interface ServerPath {
    command: string;
    args?: string[];
}

function findServerPath(context: vscode.ExtensionContext, config: vscode.WorkspaceConfiguration): ServerPath | null {
    // 1. Check explicit configuration (user override)
    const configuredPath = config.get<string>('languageServer.path');
    if (configuredPath) {
        // Check if it's a DLL path (needs dotnet to run)
        if (configuredPath.endsWith('.dll')) {
            return { command: 'dotnet', args: [configuredPath] };
        }
        return { command: configuredPath };
    }

    // 2. Check bundled server (platform-specific binary)
    const bundledPath = getBundledServerPath(context);
    if (bundledPath && fs.existsSync(bundledPath)) {
        outputChannel.appendLine(`Using bundled server: ${bundledPath}`);
        return { command: bundledPath };
    }

    // 3. Check for calor-lsp in workspace (development scenario)
    const workspaceFolders = vscode.workspace.workspaceFolders;
    if (workspaceFolders) {
        for (const folder of workspaceFolders) {
            // Check for built DLL in typical locations
            for (const buildConfig of ['Debug', 'Release']) {
                const dllPath = path.join(
                    folder.uri.fsPath,
                    'src/Calor.LanguageServer/bin',
                    buildConfig,
                    'net10.0/calor-lsp.dll'
                );
                if (fs.existsSync(dllPath)) {
                    outputChannel.appendLine(`Using workspace server: ${dllPath}`);
                    return { command: 'dotnet', args: [dllPath] };
                }
            }
        }
    }

    // 4. Fall back to PATH
    outputChannel.appendLine('Attempting to use calor-lsp from PATH');
    return { command: 'calor-lsp' };
}

function getBundledServerPath(context: vscode.ExtensionContext): string | null {
    const serverName = process.platform === 'win32' ? 'calor-lsp.exe' : 'calor-lsp';
    const serverPath = path.join(context.extensionPath, 'server', serverName);
    return serverPath;
}

export function deactivate(): Thenable<void> | undefined {
    if (!client) {
        return undefined;
    }
    return client.stop();
}
