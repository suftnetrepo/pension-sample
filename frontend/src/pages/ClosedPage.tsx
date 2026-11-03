import { CircleCheck } from 'lucide-react';
import type { BatchSummary } from '@/services/types';
import { formatDateTime, formatNumber } from '@/services/format';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from '@/components/ui/card';

/** The end of a batch: committed (with what happened), or discarded / expired. */
export function ClosedPage({ batch, onNew, onViewCommitted }: { batch: BatchSummary; onNew: () => void; onViewCommitted: (id: string) => void }) {
  const c = batch.commit;
  return (
    <Card>
      {batch.status === 'Committed' && c ? (
        <>
          <CardHeader>
            <CardTitle className="flex items-center gap-3 text-[22px]">
              <CircleCheck className="text-success size-9" aria-hidden="true" />
              Batch {batch.id} committed
            </CardTitle>
          </CardHeader>
          <CardContent>
            <ul className="grid list-disc gap-1.5 pl-5">
              <li>
                {batch.sourceName} {batch.schemaVersion} · {batch.fileName}
              </li>
              <li>
                {formatNumber(c.membersSaved)} members saved (
                {Object.entries(c.recordCounts)
                  .map(([type, n]) => `${formatNumber(n)} ${type}`)
                  .join(', ')}{' '}
                records)
              </li>
              <li>{formatNumber(c.rejectedLines)} rejected lines listed in the report, not loaded</li>
              <li>IntelliPen file produced: {c.outputFileName}</li>
              <li className={c.reportSent ? undefined : 'text-destructive'}>Reconciliation report: {c.reportError ?? c.reportDestination}</li>
              <li>
                Committed by {batch.closedBy} at {formatDateTime(batch.closedAtUtc)}. The temporary copy has been deleted.
              </li>
            </ul>
          </CardContent>
        </>
      ) : (
        <CardHeader>
          <CardTitle className="text-[22px]">
            Batch {batch.id} {batch.status === 'Expired' ? 'expired' : 'discarded'}
          </CardTitle>
          <CardDescription>
            {batch.status === 'Expired'
              ? 'It was not committed in time and has been deleted. Nothing was saved. Upload the file again to continue.'
              : `Discarded by ${batch.closedBy ?? 'a user'} at ${formatDateTime(batch.closedAtUtc)}. Nothing was saved.`}
          </CardDescription>
        </CardHeader>
      )}
      <CardFooter className="flex-wrap justify-end gap-3">
        {batch.status === 'Committed' && (
          <Button variant="secondary" onClick={() => onViewCommitted(batch.id)}>
            View committed batch
          </Button>
        )}
        <Button onClick={onNew}>Start a new import</Button>
      </CardFooter>
    </Card>
  );
}
