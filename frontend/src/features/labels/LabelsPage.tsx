import { Minus, Plus, Printer, Tags, Trash2, X } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';

import { useAssets, useLabelStocks, usePrintLabelBatch } from '@/api/hooks';
import { ApiError, describeError } from '@/api/problem';
import {
  MAX_LABEL_QUANTITY,
  MAX_LABELS_PER_REQUEST,
  type AssetListItem,
  type LabelDocument,
  type LabelStock,
  type LabelStockInfo,
} from '@/api/types';
import { cn } from '@/lib/cn';
import { formatNumber } from '@/lib/format';
import { Alert } from '@/ui/Alert';
import { Badge } from '@/ui/Badge';
import { Button, IconButton } from '@/ui/Button';
import { Card, CardBody, CardHeader } from '@/ui/Card';
import { Checkbox } from '@/ui/Checkbox';
import { Combobox } from '@/ui/Combobox';
import { EmptyState, ErrorState } from '@/ui/EmptyState';
import { PageHeader } from '@/ui/PageHeader';
import { LoadingBlock } from '@/ui/Spinner';

import { PdfPreview } from './PdfPreview';
import { SheetPositionPicker } from './SheetPositionPicker';

interface QueueItem {
  asset: AssetListItem;
  quantity: number;
}

interface PrintResult {
  document: LabelDocument;
  url: string;
}

export default function LabelsPage() {
  const stocks = useLabelStocks();
  const print = usePrintLabelBatch();

  const [stock, setStock] = useState<LabelStock | null>(null);
  const [startPosition, setStartPosition] = useState(1);
  const [items, setItems] = useState<QueueItem[]>([]);
  const [issueMissingQr, setIssueMissingQr] = useState(true);
  const [search, setSearch] = useState('');
  const [result, setResult] = useState<PrintResult | null>(null);

  const assets = useAssets({ q: search || undefined, pageSize: 25, sort: 'assetTag' });

  // Object URLs pin the PDF in memory until revoked. Revoking on change *and*
  // unmount is the same cleanup, so one effect covers both.
  useEffect(() => {
    if (!result) return;
    const { url } = result;
    return () => URL.revokeObjectURL(url);
  }, [result]);

  // Default to whatever the server lists first, once.
  const selectedStock: LabelStockInfo | undefined =
    stocks.data?.find((entry) => entry.stock === stock) ?? stocks.data?.[0];

  const totalLabels = items.reduce((sum, item) => sum + item.quantity, 0);
  const overBudget = totalLabels > MAX_LABELS_PER_REQUEST;
  const remaining = MAX_LABELS_PER_REQUEST - totalLabels;

  const pagesNeeded = useMemo(() => {
    if (!selectedStock || totalLabels === 0) return 0;
    const perPage = selectedStock.labelsPerPage;
    if (perPage <= 1) return totalLabels;
    // The first sheet only offers the positions from `startPosition` onwards.
    const firstSheetCapacity = perPage - (startPosition - 1);
    if (totalLabels <= firstSheetCapacity) return 1;
    return 1 + Math.ceil((totalLabels - firstSheetCapacity) / perPage);
  }, [selectedStock, startPosition, totalLabels]);

  function addAsset(asset: AssetListItem) {
    setItems((current) => {
      const existing = current.find((item) => item.asset.id === asset.id);
      if (existing) {
        return current.map((item) =>
          item.asset.id === asset.id
            ? { ...item, quantity: Math.min(item.quantity + 1, MAX_LABEL_QUANTITY) }
            : item,
        );
      }
      return [...current, { asset, quantity: 1 }];
    });
  }

  function setQuantity(assetId: string, quantity: number) {
    setItems((current) =>
      current.map((item) =>
        item.asset.id === assetId
          ? { ...item, quantity: Math.min(Math.max(quantity, 1), MAX_LABEL_QUANTITY) }
          : item,
      ),
    );
  }

  function remove(assetId: string) {
    setItems((current) => current.filter((item) => item.asset.id !== assetId));
  }

  function generate() {
    if (!selectedStock) return;
    print.mutate(
      {
        stock: selectedStock.stock,
        startPosition: selectedStock.supportsStartPosition ? startPosition : 1,
        items: items.map((item) => ({
          assetId: item.asset.id,
          quantity: item.quantity,
        })),
        issueMissingQr,
      },
      {
        onSuccess: (document) => {
          setResult({ document, url: URL.createObjectURL(document.blob) });
        },
      },
    );
  }

  if (stocks.isLoading) return <LoadingBlock label="Loading label stocks…" />;
  if (stocks.isError) {
    return <ErrorState error={stocks.error} onRetry={() => void stocks.refetch()} />;
  }

  const error = print.error;
  const apiError = error instanceof ApiError ? error : null;

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Print labels"
        description="Generate a PDF of asset labels with QR codes, for a label roll or a sheet of Avery labels."
      />

      <div className="grid items-start gap-4 xl:grid-cols-[minmax(0,26rem)_minmax(0,1fr)]">
        <div className="flex flex-col gap-4">
          <Card>
            <CardHeader title="1. Label stock" />
            <CardBody role="radiogroup" aria-label="Label stock" className="flex flex-col gap-2">
              {stocks.data?.map((entry) => (
                <StockOption
                  key={entry.stock}
                  stock={entry}
                  selected={entry.stock === selectedStock?.stock}
                  onSelect={() => {
                    setStock(entry.stock);
                    // A roll has no positions; reset so a stale value cannot 400.
                    if (!entry.supportsStartPosition) setStartPosition(1);
                  }}
                />
              ))}
            </CardBody>
          </Card>

          {selectedStock?.supportsStartPosition && (
            <Card>
              <CardHeader
                title="2. Start position"
                description="For part-used sheets."
              />
              <CardBody>
                <SheetPositionPicker
                  columns={selectedStock.columns}
                  rows={selectedStock.rows}
                  labelWidthMillimetres={selectedStock.labelWidthMillimetres}
                  labelHeightMillimetres={selectedStock.labelHeightMillimetres}
                  startPosition={startPosition}
                  onStartPositionChange={setStartPosition}
                  labelCount={totalLabels}
                  disabled={print.isPending}
                />
              </CardBody>
            </Card>
          )}

          <Card>
            <CardHeader title="Options" />
            <CardBody>
              <Checkbox
                checked={issueMissingQr}
                onCheckedChange={setIssueMissingQr}
                label="Issue QR codes for assets that have none"
                description="Leave this on unless you are reprinting existing labels — with it off, an asset without a QR identifier makes the whole request fail."
              />
            </CardBody>
          </Card>
        </div>

        <div className="flex flex-col gap-4">
          <Card>
            <CardHeader
              title={selectedStock?.supportsStartPosition ? '3. Assets' : '2. Assets'}
              description="Search by tag, serial, make or model. Adding the same asset again increases its quantity."
              actions={
                items.length > 0 && (
                  <Button size="sm" iconLeft={<Trash2 />} onClick={() => setItems([])}>
                    Clear
                  </Button>
                )
              }
            />
            <CardBody className="flex flex-col gap-3">
              <Combobox
                value={null}
                onChange={(assetId) => {
                  const asset = assets.data?.items.find((entry) => entry.id === assetId);
                  if (asset) addAsset(asset);
                }}
                options={(assets.data?.items ?? []).map((asset) => ({
                  value: asset.id,
                  label: asset.assetTag,
                  description:
                    [asset.manufacturer, asset.model].filter(Boolean).join(' ') ||
                    asset.category.name,
                }))}
                loading={assets.isFetching}
                onSearchChange={setSearch}
                serverFiltered
                placeholder="Add an asset…"
                searchPlaceholder="Search assets…"
                aria-label="Add an asset to the print run"
              />

              {items.length === 0 ? (
                <EmptyState
                  icon={<Tags />}
                  title="No labels queued"
                  description="Find an asset above to add it to this print run."
                  className="py-10"
                />
              ) : (
                <ul className="flex flex-col divide-y divide-border rounded-md border border-border">
                  {items.map((item) => (
                    <li
                      key={item.asset.id}
                      className="flex items-center gap-3 px-3 py-2"
                    >
                      <div className="min-w-0 flex-1">
                        <p className="truncate font-mono text-sm font-medium text-text">
                          {item.asset.assetTag}
                        </p>
                        <p className="truncate text-xs text-text-subtle">
                          {[item.asset.manufacturer, item.asset.model]
                            .filter(Boolean)
                            .join(' ') || item.asset.category.name}
                        </p>
                      </div>

                      <div className="flex shrink-0 items-center gap-1">
                        <IconButton
                          label={`Fewer copies of ${item.asset.assetTag}`}
                          size="xs"
                          variant="secondary"
                          disabled={item.quantity <= 1}
                          onClick={() => setQuantity(item.asset.id, item.quantity - 1)}
                        >
                          <Minus />
                        </IconButton>
                        <input
                          type="number"
                          min={1}
                          max={MAX_LABEL_QUANTITY}
                          value={item.quantity}
                          onChange={(event) =>
                            setQuantity(item.asset.id, Number(event.target.value))
                          }
                          aria-label={`Copies of ${item.asset.assetTag}`}
                          className="h-7 w-14 rounded-sm border border-border-strong bg-surface text-center text-sm tabular-nums text-text"
                        />
                        <IconButton
                          label={`More copies of ${item.asset.assetTag}`}
                          size="xs"
                          variant="secondary"
                          disabled={item.quantity >= MAX_LABEL_QUANTITY}
                          onClick={() => setQuantity(item.asset.id, item.quantity + 1)}
                        >
                          <Plus />
                        </IconButton>
                      </div>

                      <IconButton
                        label={`Remove ${item.asset.assetTag}`}
                        size="xs"
                        onClick={() => remove(item.asset.id)}
                      >
                        <X />
                      </IconButton>
                    </li>
                  ))}
                </ul>
              )}
            </CardBody>
          </Card>

          <Card>
            <CardBody className="flex flex-wrap items-center justify-between gap-3">
              <div className="flex flex-wrap items-center gap-2 text-sm">
                <Badge tone={overBudget ? 'danger' : 'neutral'}>
                  {formatNumber(totalLabels)} / {MAX_LABELS_PER_REQUEST} labels
                </Badge>
                {pagesNeeded > 0 && (
                  <Badge tone="neutral">
                    {formatNumber(pagesNeeded)}{' '}
                    {selectedStock?.pageType === 'continuousRoll'
                      ? pagesNeeded === 1
                        ? 'label on the roll'
                        : 'labels on the roll'
                      : pagesNeeded === 1
                        ? 'sheet'
                        : 'sheets'}
                  </Badge>
                )}
                {!overBudget && totalLabels > 0 && (
                  <span className="text-text-subtle">
                    {formatNumber(remaining)} more allowed in this run
                  </span>
                )}
              </div>

              <Button
                variant="primary"
                iconLeft={<Printer />}
                loading={print.isPending}
                disabled={items.length === 0 || overBudget}
                onClick={generate}
              >
                Generate PDF
              </Button>
            </CardBody>
          </Card>

          {overBudget && (
            <Alert tone="danger" title="Too many labels for one run">
              The server allows {MAX_LABELS_PER_REQUEST} labels per request. Remove{' '}
              {formatNumber(totalLabels - MAX_LABELS_PER_REQUEST)} and print the rest in a
              second run.
            </Alert>
          )}

          {error && (
            <Alert tone="danger" title="Could not generate the PDF" role="alert">
              {apiError?.isValidation
                ? apiError.allFieldErrors().join(' ')
                : apiError?.isNotFound
                  ? 'One of the queued assets no longer exists. Remove it and try again.'
                  : describeError(error)}
            </Alert>
          )}

          {result && <PdfPreview document={result.document} url={result.url} />}
        </div>
      </div>
    </div>
  );
}

function StockOption({
  stock,
  selected,
  onSelect,
}: {
  stock: LabelStockInfo;
  selected: boolean;
  onSelect: () => void;
}) {
  return (
    <button
      type="button"
      role="radio"
      aria-checked={selected}
      // The visible text sits in nested spans beside a decorative swatch, which
      // does not reliably compute into an accessible name.
      aria-label={stock.displayName}
      onClick={onSelect}
      className={cn(
        'flex items-start gap-3 rounded-lg border p-3 text-left transition-colors',
        selected
          ? 'border-accent bg-accent-soft'
          : 'border-border bg-surface hover:border-border-strong hover:bg-surface-hover',
      )}
    >
      <span
        className={cn(
          'mt-0.5 w-14 shrink-0 rounded-xs border',
          selected ? 'border-accent bg-accent' : 'border-border-strong bg-surface',
        )}
        // Silhouette of the real label at a fixed width, so the two stocks are
        // distinguishable by shape at a glance. Deriving the height from the
        // aspect ratio keeps it proportional without letting millimetres leak
        // into layout units.
        style={{
          aspectRatio: `${stock.labelWidthMillimetres} / ${stock.labelHeightMillimetres}`,
        }}
        aria-hidden
      />
      <span className="min-w-0">
        <span
          className={cn(
            'block text-base font-medium',
            selected ? 'text-accent-soft-fg' : 'text-text',
          )}
        >
          {stock.displayName}
        </span>
        <span className="mt-0.5 block text-xs text-text-subtle">
          {stock.labelWidthMillimetres} × {stock.labelHeightMillimetres} mm ·{' '}
          {stock.pageType === 'continuousRoll'
            ? 'Continuous roll'
            : `${stock.labelsPerPage} per A4 sheet (${stock.columns} × ${stock.rows})`}
        </span>
      </span>
    </button>
  );
}
