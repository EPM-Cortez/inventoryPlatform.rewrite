import { Printer } from 'lucide-react';
import { useEffect, useState } from 'react';

import { useLabelStocks, usePrintAssetLabel } from '@/api/hooks';
import { ApiError, describeError } from '@/api/problem';
import {
  MAX_LABEL_QUANTITY,
  type Guid,
  type LabelDocument,
  type LabelStock,
} from '@/api/types';
import { Alert } from '@/ui/Alert';
import { Button } from '@/ui/Button';
import { Dialog } from '@/ui/Dialog';
import { Field, Input, NativeSelect } from '@/ui/Field';
import { LoadingBlock } from '@/ui/Spinner';

import { PdfPreview } from './PdfPreview';
import { SheetPositionPicker } from './SheetPositionPicker';

export interface PrintLabelDialogProps {
  onClose: () => void;
  assetId: Guid;
  assetTag: string;
}

interface PrintResult {
  document: LabelDocument;
  url: string;
}

/**
 * Prints labels for one asset. The batch page covers many; this is the shortcut
 * from the asset itself, which is where someone stands when a label falls off.
 */
export function PrintLabelDialog({ onClose, assetId, assetTag }: PrintLabelDialogProps) {
  const stocks = useLabelStocks();
  const print = usePrintAssetLabel(assetId);

  const [stock, setStock] = useState<LabelStock | null>(null);
  const [copies, setCopies] = useState(1);
  const [startPosition, setStartPosition] = useState(1);
  const [result, setResult] = useState<PrintResult | null>(null);

  useEffect(() => {
    if (!result) return;
    const { url } = result;
    return () => URL.revokeObjectURL(url);
  }, [result]);

  const selectedStock = stocks.data?.find((entry) => entry.stock === stock) ?? stocks.data?.[0];

  function generate() {
    if (!selectedStock) return;
    print.mutate(
      {
        stock: selectedStock.stock,
        copies,
        // Roll stock rejects anything but 1.
        startPosition: selectedStock.supportsStartPosition ? startPosition : 1,
        issueMissingQr: true,
      },
      {
        onSuccess: (document) => {
          setResult({ document, url: URL.createObjectURL(document.blob) });
        },
      },
    );
  }

  const error = print.error;
  const apiError = error instanceof ApiError ? error : null;

  return (
    <Dialog
      open
      onOpenChange={(next) => !next && onClose()}
      title="Print label"
      description={assetTag}
      size={result ? 'xl' : 'md'}
      dismissible={!print.isPending}
      footer={
        <>
          <Button onClick={onClose} disabled={print.isPending}>
            {result ? 'Close' : 'Cancel'}
          </Button>
          <Button
            variant="primary"
            iconLeft={<Printer />}
            loading={print.isPending}
            disabled={!selectedStock}
            onClick={generate}
          >
            {result ? 'Regenerate' : 'Generate PDF'}
          </Button>
        </>
      }
    >
      {stocks.isLoading ? (
        <LoadingBlock label="Loading label stocks…" />
      ) : (
        <div className="flex flex-col gap-4">
          {error && (
            <Alert tone="danger" title="Could not generate the PDF" role="alert">
              {apiError?.isValidation
                ? apiError.allFieldErrors().join(' ')
                : describeError(error)}
            </Alert>
          )}

          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Label stock">
              {(controlProps) => (
                <NativeSelect
                  {...controlProps}
                  value={selectedStock?.stock ?? ''}
                  onChange={(event) => {
                    const next = event.target.value as LabelStock;
                    setStock(next);
                    const info = stocks.data?.find((entry) => entry.stock === next);
                    if (info && !info.supportsStartPosition) setStartPosition(1);
                  }}
                >
                  {stocks.data?.map((entry) => (
                    <option key={entry.stock} value={entry.stock}>
                      {entry.displayName}
                    </option>
                  ))}
                </NativeSelect>
              )}
            </Field>

            <Field label="Copies" hint={`Up to ${MAX_LABEL_QUANTITY}.`}>
              {(controlProps) => (
                <Input
                  {...controlProps}
                  type="number"
                  min={1}
                  max={MAX_LABEL_QUANTITY}
                  value={copies}
                  onChange={(event) =>
                    setCopies(
                      Math.min(Math.max(Number(event.target.value) || 1, 1), MAX_LABEL_QUANTITY),
                    )
                  }
                />
              )}
            </Field>
          </div>

          {selectedStock?.supportsStartPosition && (
            <div className="border-t border-border pt-4">
              <SheetPositionPicker
                columns={selectedStock.columns}
                rows={selectedStock.rows}
                labelWidthMillimetres={selectedStock.labelWidthMillimetres}
                labelHeightMillimetres={selectedStock.labelHeightMillimetres}
                startPosition={startPosition}
                onStartPositionChange={setStartPosition}
                labelCount={copies}
                disabled={print.isPending}
              />
            </div>
          )}

          {result && <PdfPreview document={result.document} url={result.url} />}
        </div>
      )}
    </Dialog>
  );
}
