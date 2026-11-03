import type { Counts } from '@/services/types';
import { formatNumber } from '@/services/format';
import { Card } from '@/components/ui/card';
import { cn } from '@/lib/utils';

/** Lines read / transformed / rejected / warnings, for a held or a committed batch. */
export function BatchTiles({ counts }: { counts: Counts }) {
  return (
    <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
      <Tile label="Lines read" value={counts.linesRead} />
      <Tile label="Members transformed" value={counts.accepted} tone="ok" />
      <Tile label="Rejected (errors)" value={counts.rejected} tone="error" />
      <Tile label="Warnings" value={counts.warnings} tone="warn" />
    </div>
  );
}

function Tile({ label, value, tone }: { label: string; value: number; tone?: 'ok' | 'error' | 'warn' }) {
  return (
    <Card className="gap-1 px-4 py-4">
      <span className="text-muted-foreground text-xs">{label}</span>
      <span className={cn('text-[26px] font-semibold', tone === 'ok' && 'text-success', tone === 'error' && 'text-destructive', tone === 'warn' && 'text-warning')}>
        {formatNumber(value)}
      </span>
    </Card>
  );
}
