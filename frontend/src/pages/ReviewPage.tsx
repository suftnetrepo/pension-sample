import type { BatchSummary } from '@/services/types';
import { formatDateTime } from '@/services/format';
import { Pill } from '@/components/common/Feedback';
import { BatchTiles } from '@/components/review/BatchTiles';
import { BatchTabs } from '@/components/review/BatchTabs';
import { CommitBar } from '@/components/review/CommitBar';

export function ReviewPage({ batch, onDiscarded }: { batch: BatchSummary; onDiscarded: () => void }) {
  return (
    <section className="grid grid-cols-1 gap-5">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-[22px] font-semibold">Review the transformed data</h1>
          <p className="text-muted-foreground mt-1">
            Batch {batch.id} · {batch.sourceName} {batch.schemaVersion} · {batch.fileName} · held until {formatDateTime(batch.expiresAtUtc)} · not saved
          </p>
        </div>
        <Pill tone={batch.balanced ? 'ok' : 'error'}>{batch.balanced ? 'Reconciliation balanced' : 'Reconciliation NOT balanced'}</Pill>
      </div>
      <BatchTiles counts={batch.counts!} />
      <BatchTabs scope="held" batch={batch} />
      <CommitBar batch={batch} onDiscarded={onDiscarded} />
    </section>
  );
}
