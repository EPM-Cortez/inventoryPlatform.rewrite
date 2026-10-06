import { Check, Copy, Download, Printer } from 'lucide-react';
import { useState } from 'react';

import { downloadBlob } from '@/lib/download';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';

export interface RecoveryCodeListProps {
  codes: string[];
  /** Used in the downloaded filename and printed sheet. */
  accountName: string;
}

/**
 * Recovery codes, shown exactly once.
 *
 * The server stores only hashes, so there is no second chance to read these. The
 * whole component is built around getting them somewhere safe before the user
 * navigates away: copy, download and print are all one click, and the warning
 * says plainly that closing loses them.
 */
export function RecoveryCodeList({ codes, accountName }: RecoveryCodeListProps) {
  const [copied, setCopied] = useState(false);

  const asText = codes.join('\n');

  async function copyAll() {
    try {
      await navigator.clipboard.writeText(asText);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 2000);
    } catch {
      // Clipboard can be blocked; download and print still work.
    }
  }

  function download() {
    downloadBlob(
      new Blob([`Inventory recovery codes for ${accountName}\n\n${asText}\n`], {
        type: 'text/plain',
      }),
      `inventory-recovery-codes-${accountName.replace(/[^a-z0-9]+/gi, '-').toLowerCase()}.txt`,
    );
  }

  function print() {
    const frame = document.createElement('iframe');
    frame.style.position = 'fixed';
    frame.style.inset = 'auto 0 0 auto';
    frame.style.width = '0';
    frame.style.height = '0';
    frame.style.border = '0';
    frame.srcdoc = `<!doctype html><html><head><title>Recovery codes</title>
      <style>
        body { font-family: system-ui, sans-serif; margin: 24mm; }
        h1 { font-size: 14pt; }
        p { font-size: 10pt; color: #444; }
        ul { columns: 2; font-family: ui-monospace, monospace; font-size: 12pt; }
        li { margin-bottom: 4mm; }
      </style></head><body>
      <h1>Inventory recovery codes — ${accountName}</h1>
      <p>Each code works once. Keep this somewhere safe and private.</p>
      <ul>${codes.map((code) => `<li>${code}</li>`).join('')}</ul>
      </body></html>`;
    frame.onload = () => {
      frame.contentWindow?.focus();
      frame.contentWindow?.print();
      window.setTimeout(() => frame.remove(), 60_000);
    };
    document.body.append(frame);
  }

  return (
    <div className="flex flex-col gap-3">
      <Alert tone="warning" title="Save these now — they are not shown again">
        Each code signs you in once if you lose your authenticator. The system keeps
        only a hash, so nobody can retrieve them for you later.
      </Alert>

      <ul className="grid grid-cols-2 gap-2 rounded-md border border-border bg-surface-sunken p-3 sm:grid-cols-3">
        {codes.map((code) => (
          <li
            key={code}
            className="select-all text-center font-mono text-sm tracking-tight text-text"
          >
            {code}
          </li>
        ))}
      </ul>

      <div className="flex flex-wrap gap-2">
        <Button
          size="sm"
          iconLeft={copied ? <Check className="text-success" /> : <Copy />}
          onClick={() => void copyAll()}
        >
          {copied ? 'Copied' : 'Copy all'}
        </Button>
        <Button size="sm" iconLeft={<Download />} onClick={download}>
          Download
        </Button>
        <Button size="sm" iconLeft={<Printer />} onClick={print}>
          Print
        </Button>
      </div>
    </div>
  );
}
