import { Check, Copy, Download, ShieldAlert, ShieldCheck } from 'lucide-react';
import { useState } from 'react';

import { agentsApi } from '@/api/endpoints';
import { useAgentInstaller } from '@/api/hooks';
import { downloadBlob } from '@/lib/download';
import { formatBytes } from '@/lib/format';
import { Button } from '@/ui/Button';
import { Card, CardHeader } from '@/ui/Card';
import { useToast } from '@/ui/Toast';

/**
 * The agent installer, its checksum and the unattended install command.
 *
 * The SHA-256 and the signer are the reason this exists: whoever runs the MSI on
 * a machine needs to be able to check that what they downloaded is what the
 * server meant to serve. Showing the download without them would be worse than
 * useless — it would look verified when nothing had been verified.
 *
 * The install command carries a `{token}` placeholder rather than a real
 * enrolment token. Tokens are shown once, at creation, and are never available
 * to paste back into a page.
 */
export function InstallerCard() {
  const installer = useAgentInstaller();
  const toast = useToast();
  const [downloading, setDownloading] = useState(false);
  const [copied, setCopied] = useState<'hash' | 'command' | null>(null);

  // A build that has not been published yet is a normal state, not an error —
  // say nothing rather than showing a broken download.
  if (installer.isError || (!installer.isLoading && !installer.data)) return null;

  const meta = installer.data;

  async function copy(text: string, what: 'hash' | 'command') {
    await navigator.clipboard.writeText(text);
    setCopied(what);
    window.setTimeout(() => setCopied(null), 2000);
  }

  async function download() {
    setDownloading(true);
    try {
      const file = await agentsApi.downloadInstaller();
      downloadBlob(file.blob, file.fileName);
    } catch (error) {
      toast.fromError(error, 'Could not download the installer');
    } finally {
      setDownloading(false);
    }
  }

  return (
    <Card>
      <CardHeader
        title="Agent installer"
        description="Install this on a Windows machine, then enrol it with a token."
        actions={
          meta && (
            <Button variant="primary" onClick={() => void download()} loading={downloading}>
              <Download className="mr-1.5 size-4" />
              Download {meta.fileName}
            </Button>
          )
        }
      />

      {installer.isLoading || !meta ? (
        <p className="p-4 text-sm text-text-muted">Checking for a published build…</p>
      ) : (
        <dl className="grid gap-4 p-4 sm:grid-cols-2">
          <div>
            <dt className="text-xs font-medium text-text-muted">Version</dt>
            <dd className="mt-0.5 font-mono text-sm text-text">
              {meta.version}{' '}
              <span className="text-text-subtle">({formatBytes(meta.sizeBytes)})</span>
            </dd>
          </div>

          <div>
            <dt className="text-xs font-medium text-text-muted">Signature</dt>
            <dd className="mt-0.5 flex items-center gap-1.5 text-sm">
              {meta.signed ? (
                <>
                  <ShieldCheck className="size-4 shrink-0 text-success" aria-hidden="true" />
                  <span className="truncate text-text">{meta.signer ?? 'Signed'}</span>
                </>
              ) : (
                <>
                  <ShieldAlert className="size-4 shrink-0 text-warning" aria-hidden="true" />
                  <span className="text-warning">
                    Unsigned — Windows will warn before it installs
                  </span>
                </>
              )}
            </dd>
          </div>

          <div className="sm:col-span-2">
            <dt className="text-xs font-medium text-text-muted">
              SHA-256 — check this against the downloaded file before installing
            </dt>
            <dd className="mt-1 flex items-start gap-2">
              <code className="min-w-0 flex-1 break-all rounded-sm bg-surface-sunken px-2 py-1.5 font-mono text-2xs text-text-muted">
                {meta.sha256}
              </code>
              <Button
                size="sm"
                onClick={() => void copy(meta.sha256, 'hash')}
                aria-label="Copy the SHA-256 checksum"
              >
                {copied === 'hash' ? (
                  <Check className="size-4 text-success" />
                ) : (
                  <Copy className="size-4" />
                )}
              </Button>
            </dd>
            <p className="mt-1 text-2xs text-text-subtle">
              <code className="font-mono">Get-FileHash .\{meta.fileName}</code> in
              PowerShell prints the same value.
            </p>
          </div>

          <div className="sm:col-span-2">
            <dt className="text-xs font-medium text-text-muted">Unattended install</dt>
            <dd className="mt-1 flex items-start gap-2">
              <code className="min-w-0 flex-1 overflow-x-auto rounded-sm bg-surface-sunken px-2 py-1.5 font-mono text-2xs whitespace-pre text-text-muted">
                {meta.installCommandTemplate}
              </code>
              <Button
                size="sm"
                onClick={() => void copy(meta.installCommandTemplate, 'command')}
                aria-label="Copy the install command"
              >
                {copied === 'command' ? (
                  <Check className="size-4 text-success" />
                ) : (
                  <Copy className="size-4" />
                )}
              </Button>
            </dd>
            <p className="mt-1 text-2xs text-text-subtle">
              Substitute the enrolment token shown when the token was created. It is
              stored only as a hash, so it cannot be looked up again.
            </p>
          </div>
        </dl>
      )}
    </Card>
  );
}
