import { Play, RotateCcw } from 'lucide-react';
import { useState } from 'react';

import type { LegacyReconciliation } from '@/api/adminTypes';
import { useCreateReconciliationDryRun } from '@/api/hooks';
import { describeError } from '@/api/problem';
import type { AssetImportMode } from '@/api/types';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Card, CardBody, CardHeader } from '@/ui/Card';
import { PageHeader } from '@/ui/PageHeader';

import { ImportFilePicker } from './ImportShared';
import { ImportModePicker } from './ImportModePicker';
import { ReconciliationReportView } from './ReconciliationReportView';

/**
 * Legacy cutover: compare an export from the old system against this register,
 * then record a decision about it.
 *
 * The dry run never writes assets. What it produces is evidence — counts, the
 * issues found, and a fingerprint — and sign-off records a named decision about
 * that evidence. Neither step commits the import.
 */
export default function ReconciliationPage() {
  const [file, setFile] = useState<File | null>(null);
  const [mode, setMode] = useState<AssetImportMode>('createOnly');
  const [result, setResult] = useState<LegacyReconciliation | null>(null);

  const dryRun = useCreateReconciliationDryRun();

  function start() {
    if (!file) return;
    dryRun.mutate({ file, mode }, { onSuccess: setResult });
  }

  function reset() {
    setResult(null);
    setFile(null);
    dryRun.reset();
  }

  return (
    <div className="mx-auto flex w-full max-w-5xl flex-col gap-5">
      <PageHeader
        title="Legacy reconciliation"
        description="Compare an export from the old system against this register and record a decision about the result."
        actions={
          result && (
            <Button iconLeft={<RotateCcw />} onClick={reset}>
              Start another dry run
            </Button>
          )
        }
      />

      {result ? (
        <ReconciliationReportView
          reconciliation={result}
          onUpdate={setResult}
          onRestart={reset}
        />
      ) : (
        <>
          <Alert tone="info" title="A dry run changes nothing">
            The file is read, compared and scored. No asset is created or edited by this
            page — committing an import is still done from the{' '}
            <strong>asset import</strong> wizard.
          </Alert>

          <Card>
            <CardHeader
              title="1. The legacy export"
              description="The same CSV layout the asset import expects."
            />
            <CardBody>
              <ImportFilePicker file={file} onFileChange={setFile} />
            </CardBody>
          </Card>

          <Card>
            <CardHeader
              title="2. How rows that already exist should be treated"
              description="This only affects what the comparison reports as planned; nothing is written either way."
            />
            <CardBody>
              <ImportModePicker
                value={mode}
                onChange={setMode}
                createOnlyValue="createOnly"
                upsertValue="upsertByAssetTag"
                keyNoun="asset tag"
              />
            </CardBody>
          </Card>

          {dryRun.error && (
            <Alert tone="danger" title="The dry run could not be produced" role="alert">
              {describeError(dryRun.error)}
            </Alert>
          )}

          <div className="flex justify-end gap-2">
            <Button
              variant="primary"
              size="lg"
              iconLeft={<Play />}
              loading={dryRun.isPending}
              disabled={!file}
              onClick={start}
            >
              Run the comparison
            </Button>
          </div>
        </>
      )}
    </div>
  );
}
