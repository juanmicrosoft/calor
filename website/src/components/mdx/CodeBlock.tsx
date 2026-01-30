'use client';

import { useState } from 'react';
import { Check, Copy } from 'lucide-react';
import { cn } from '@/lib/utils';

interface CodeBlockProps {
  code: string;
  language: string;
  filename?: string;
  showLineNumbers?: boolean;
}

export function CodeBlock({
  code,
  language,
  filename,
  showLineNumbers = false,
}: CodeBlockProps) {
  const [copied, setCopied] = useState(false);

  const copyToClipboard = async () => {
    await navigator.clipboard.writeText(code);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  // Map common language aliases
  const languageMap: Record<string, string> = {
    csharp: 'cs',
    'c#': 'cs',
    bash: 'bash',
    shell: 'bash',
    sh: 'bash',
    powershell: 'powershell',
    ps1: 'powershell',
    javascript: 'js',
    typescript: 'ts',
    opal: 'text', // OPAL doesn't have built-in highlighting yet
  };

  const normalizedLanguage = languageMap[language.toLowerCase()] || language;

  // Determine the display language label
  const languageLabels: Record<string, string> = {
    cs: 'C#',
    csharp: 'C#',
    js: 'JavaScript',
    ts: 'TypeScript',
    bash: 'Bash',
    powershell: 'PowerShell',
    json: 'JSON',
    yaml: 'YAML',
    text: 'Plain Text',
    opal: 'OPAL',
  };

  const displayLanguage = languageLabels[normalizedLanguage] || language;

  const lines = code.trim().split('\n');

  return (
    <div className="group relative my-4 rounded-lg border bg-zinc-950 dark:bg-zinc-900">
      {/* Header */}
      <div className="flex items-center justify-between border-b border-zinc-800 px-4 py-2">
        <div className="flex items-center gap-2">
          {filename && (
            <span className="text-xs text-zinc-400 font-mono">{filename}</span>
          )}
          {!filename && displayLanguage && (
            <span className="text-xs text-zinc-500">{displayLanguage}</span>
          )}
        </div>
        <button
          onClick={copyToClipboard}
          className="flex items-center gap-1 rounded px-2 py-1 text-xs text-zinc-400 hover:bg-zinc-800 hover:text-zinc-200 transition-colors"
        >
          {copied ? (
            <>
              <Check className="h-3.5 w-3.5" />
              Copied
            </>
          ) : (
            <>
              <Copy className="h-3.5 w-3.5" />
              Copy
            </>
          )}
        </button>
      </div>

      {/* Code */}
      <div className="overflow-x-auto">
        <pre className="p-4 text-sm leading-6">
          <code className={cn('text-zinc-100', `language-${normalizedLanguage}`)}>
            {showLineNumbers
              ? lines.map((line, i) => (
                  <span key={i} className="block">
                    <span className="inline-block w-8 mr-4 text-right text-zinc-600 select-none">
                      {i + 1}
                    </span>
                    {line}
                  </span>
                ))
              : code.trim()}
          </code>
        </pre>
      </div>
    </div>
  );
}
