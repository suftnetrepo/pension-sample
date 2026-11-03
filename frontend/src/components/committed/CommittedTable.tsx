import type { CommittedItem } from '@/services/types';
import { formatDateTime, formatNumber } from '@/services/format';
import { Button } from '@/components/ui/button';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';

/** Committed batches with a View action. Used on the upload screen (latest few) and the committed batches page. */
export function CommittedTable({ items, onView, empty }: { items: CommittedItem[]; onView: (id: string) => void; empty: string }) {
  if (items.length === 0) return <p className="text-muted-foreground text-sm">{empty}</p>;
  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Batch</TableHead>
          <TableHead>Source</TableHead>
          <TableHead>File</TableHead>
          <TableHead className="text-right">Accepted</TableHead>
          <TableHead className="text-right">Rejected</TableHead>
          <TableHead>Committed</TableHead>
          <TableHead>
            <span className="sr-only">Actions</span>
          </TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {items.map((b) => (
          <TableRow key={b.id}>
            <TableCell className="font-mono">{b.id}</TableCell>
            <TableCell>
              {b.sourceName} {b.schemaVersion}
            </TableCell>
            <TableCell>{b.fileName}</TableCell>
            <TableCell className="text-right">{formatNumber(b.accepted)}</TableCell>
            <TableCell className="text-right">{formatNumber(b.rejected)}</TableCell>
            <TableCell>
              {b.committedBy} · {formatDateTime(b.committedAtUtc)}
            </TableCell>
            <TableCell className="text-right">
              <Button variant="secondary" size="sm" onClick={() => onView(b.id)} aria-label={`View batch ${b.id}`}>
                View
              </Button>
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
