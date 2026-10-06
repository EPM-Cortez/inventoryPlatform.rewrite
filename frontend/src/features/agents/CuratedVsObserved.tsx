import { Check, Info, TriangleAlert } from 'lucide-react';

import type { AgentDeviceSnapshot } from '@/api/agentTypes';
import type { Asset } from '@/api/types';
import { cn } from '@/lib/cn';
import { Alert } from '@/ui/Alert';
import { Badge } from '@/ui/Badge';
import { Card, CardBody, CardHeader } from '@/ui/Card';

interface Comparison {
  label: string;
  /** What the register says, maintained by a person. */
  curated: string | null;
  /** What the machine reported. */
  observed: string | null;
}

/**
 * Compares the register's own fields against what the machine reports.
 *
 * The agent deliberately never writes to these fields, so the two can drift
 * apart — a replaced motherboard changes the BIOS UUID, a warranty swap changes
 * the serial, someone renames the machine. That divergence is the single most
 * useful thing on this page and it is invisible if the two sets of values are
 * only ever shown in separate panels, so they are put side by side here.
 *
 * Nothing here writes anything. Every difference is a prompt for a person to
 * decide, which is the whole point of the agent not deciding for them.
 */
export function CuratedVsObserved({
  asset,
  snapshot,
}: {
  asset: Asset;
  snapshot: AgentDeviceSnapshot;
}) {
  const rows: Comparison[] = [
    {
      label: 'Serial number',
      curated: asset.serialNumber,
      observed: snapshot.hardware.serialNumber,
    },
    {
      label: 'Manufacturer',
      curated: asset.manufacturer,
      observed: snapshot.hardware.manufacturer,
    },
    { label: 'Model', curated: asset.model, observed: snapshot.hardware.model },
    { label: 'Device name', curated: asset.deviceName, observed: snapshot.hostname },
  ];

  const differing = rows.filter((row) => comparisonState(row) === 'differs');
  const missing = rows.filter((row) => comparisonState(row) === 'blank');

  return (
    <Card>
      <CardHeader
        title="Register against machine"
        description="The agent reports these but never writes them. Anything that disagrees is for a person to settle."
        actions={
          differing.length > 0 ? (
            <Badge tone="warning" dot>
              {differing.length} {differing.length === 1 ? 'difference' : 'differences'}
            </Badge>
          ) : (
            <Badge tone="success" dot>
              Consistent
            </Badge>
          )
        }
      />
      <CardBody className="flex flex-col gap-3">
        <div className="overflow-x-auto">
          <table className="w-full min-w-md text-sm">
            <thead>
              <tr className="border-b border-border text-left text-xs text-text-subtle">
                <th className="pb-2 pr-4 font-medium">Field</th>
                <th className="pb-2 pr-4 font-medium">On the register</th>
                <th className="pb-2 pr-4 font-medium">Reported by the machine</th>
                <th className="pb-2 font-medium">
                  <span className="sr-only">Agreement</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => {
                const state = comparisonState(row);
                return (
                  <tr key={row.label} className="border-b border-border last:border-b-0">
                    <td className="py-2 pr-4 text-text-muted">{row.label}</td>
                    <td
                      className={cn(
                        'py-2 pr-4 font-mono text-xs',
                        state === 'differs' ? 'font-medium text-text' : 'text-text-muted',
                      )}
                    >
                      {row.curated ?? <span className="text-text-subtle">Not set</span>}
                    </td>
                    <td
                      className={cn(
                        'py-2 pr-4 font-mono text-xs',
                        state === 'differs' ? 'font-medium text-warning' : 'text-text-muted',
                      )}
                    >
                      {row.observed ?? (
                        <span className="text-text-subtle">Not reported</span>
                      )}
                    </td>
                    <td className="py-2">
                      {state === 'matches' && (
                        <Check className="size-3.5 text-success" aria-label="Matches" />
                      )}
                      {state === 'differs' && (
                        <TriangleAlert
                          className="size-3.5 text-warning"
                          aria-label="Differs"
                        />
                      )}
                      {state === 'blank' && (
                        <Info className="size-3.5 text-text-subtle" aria-label="Missing" />
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>

        {differing.length > 0 && (
          <Alert tone="warning" title="These do not match">
            {listFields(differing)} differ between the register and the machine. The agent
            will not change the register, so this stays as it is until someone edits the
            asset — or establishes that the machine itself was swapped.
          </Alert>
        )}

        {differing.length === 0 && missing.length > 0 && (
          <Alert tone="info" title="Some fields are only on one side">
            {listFields(missing)} {missing.length === 1 ? 'is' : 'are'} recorded in one
            place but not the other. Nothing is copied across automatically; edit the
            asset if the reported value is the one you want.
          </Alert>
        )}
      </CardBody>
    </Card>
  );
}

type ComparisonState = 'matches' | 'differs' | 'blank';

/**
 * Compared case-insensitively and ignoring surrounding space: manufacturers are
 * wildly inconsistent about how they write their own name, and flagging
 * `Dell Inc.` against `DELL INC.` as a discrepancy would train people to ignore
 * the column.
 */
function comparisonState({ curated, observed }: Comparison): ComparisonState {
  const a = curated?.trim().toLowerCase();
  const b = observed?.trim().toLowerCase();
  if (!a || !b) return 'blank';
  return a === b ? 'matches' : 'differs';
}

function listFields(rows: Comparison[]): string {
  const labels = rows.map((row) => row.label.toLowerCase());
  if (labels.length === 1) return capitalise(labels[0]!);
  return capitalise(
    `${labels.slice(0, -1).join(', ')} and ${labels[labels.length - 1]!}`,
  );
}

function capitalise(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1);
}
