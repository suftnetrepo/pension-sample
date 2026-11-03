import { ArrowLeft, Download, Lock } from 'lucide-react';
import { useCommittedBatch } from '@/hooks/queries';
import { useDownloadCorrections, useDownloadExceptions, useDownloadFile, useDownloadReport } from '@/hooks/mutations';
import { formatDateTime } from '@/services/format';
import { ErrorBanner, InlineError, Pill, Spinner } from '@/components/common/Feedback';
import { BatchTiles } from '@/components/review/BatchTiles';
import { BatchTabs } from '@/components/review/BatchTabs';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';

/**
 * One committed batch, read-only: the same tabs as review, plus exports. There is no commit, discard or edit here,
 * and the API behind it only has GET endpoints.
 */
export function CommittedBatchPage({ batchId, onBack }: { batchId: string; onBack: () => void }) {
  const batch = useCommittedBatch(batchId);
  const report = useDownloadReport();
  const file = useDownloadFile();
  const exceptions = useDownloadExceptions();
  const corrections = useDownloadCorrections();

  const back = (
    <Button variant="ghost" className="w-fit" onClick={onBack}>
      <ArrowLeft aria-hidden="true" />
      Committed batches
    </Button>
  );

  if (batch.error)
    return (
      <div className="grid grid-cols-1 gap-4">
        {back}
        <ErrorBanner error={batch.error} onRetry={batch.error.isTransient ? () => batch.refetch() : undefined} />
      </div>
    );
  if (!batch.data) return <Spinner label="Loading the committed batch…" />;

  const b = batch.data;
  return (
    <section className="grid grid-cols-1 gap-5">
      {back}
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <div className="flex flex-wrap items-center gap-3">
            <h1 className="text-[22px] font-semibold">Batch {b.id}</h1>
            <Badge variant="secondary">
              <Lock aria-hidden="true" />
              Committed · read only
            </Badge>
          </div>
          <p className="text-muted-foreground mt-1">
            {b.sourceName} {b.schemaVersion} · {b.fileName} · committed by {b.closedBy} at {formatDateTime(b.closedAtUtc)}
          </p>
        </div>
        <Pill tone={b.balanced ? 'ok' : 'error'}>{b.balanced ? 'Reconciliation balanced' : 'Reconciliation NOT balanced'}</Pill>
      </div>

      {b.counts && <BatchTiles counts={b.counts} />}
      {b.counts && <BatchTabs scope="committed" batch={b} />}

      <Card className="py-5">
        <CardContent className="grid grid-cols-1 gap-2">
          <div className="flex flex-wrap items-center justify-between gap-4">
            <p className="text-muted-foreground text-sm">
              Exports are exactly what was saved at commit. This batch can't be changed, committed again or discarded.
            </p>
            <div className="flex flex-wrap gap-2">
              <Button variant="secondary" onClick={() => report.run('committed', b.id)} disabled={report.busy}>
                <Download aria-hidden="true" />
                {report.busy ? 'Preparing…' : 'Reconciliation report'}
              </Button>
              <Button variant="secondary" onClick={() => file.run('committed', b.id)} disabled={file.busy}>
                <Download aria-hidden="true" />
                {file.busy ? 'Preparing…' : 'IntelliPen file'}
              </Button>
              <Button variant="secondary" onClick={() => exceptions.run('committed', b.id)} disabled={exceptions.busy}>
                <Download aria-hidden="true" />
                {exceptions.busy ? 'Preparing…' : 'Exceptions (CSV)'}
              </Button>
              <Button variant="secondary" onClick={() => corrections.run('committed', b.id)} disabled={corrections.busy}>
                <Download aria-hidden="true" />
                {corrections.busy ? 'Preparing…' : 'Corrections (CSV)'}
              </Button>
            </div>
          </div>
          <InlineError error={report.error ?? file.error ?? exceptions.error ?? corrections.error} />
        </CardContent>
      </Card>
    </section>
  );
}
