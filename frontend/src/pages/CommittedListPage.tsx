import { useEffect, useState } from 'react';
import { useCommittedList } from '@/hooks/queries';
import { useDebouncedValue } from '@/hooks/useDebouncedValue';
import { formatNumber } from '@/services/format';
import { ErrorBanner, Spinner } from '@/components/common/Feedback';
import { CommittedTable } from '@/components/committed/CommittedTable';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { cn } from '@/lib/utils';

const pageSize = 20;

/** Every committed batch, newest first, searchable. Opening one shows it read-only. */
export function CommittedListPage({ onView }: { onView: (id: string) => void }) {
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const query = useDebouncedValue(search.trim());
  const list = useCommittedList(query, page, pageSize);
  useEffect(() => setPage(1), [query]);

  const data = list.data;
  const pages = data ? Math.max(1, Math.ceil(data.total / pageSize)) : 1;

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-[22px]">Committed batches</CardTitle>
        <CardDescription>Read-only. Open a batch to see what was committed and to export its report, IntelliPen file or exceptions.</CardDescription>
      </CardHeader>
      <CardContent className="grid grid-cols-1 gap-4">
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div className="grid gap-2">
            <Label htmlFor="committed-search">Search</Label>
            <Input
              id="committed-search"
              type="search"
              className="w-72 max-w-full"
              placeholder="Batch ID, file, source or person"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
          </div>
          <span className="text-muted-foreground text-xs" aria-live="polite">
            {data ? `${formatNumber(data.total)} ${data.total === 1 ? 'batch' : 'batches'} · page ${page} of ${pages}` : ''}
            {list.isFetching && data ? ' · updating…' : ''}
          </span>
        </div>

        {list.error && <ErrorBanner error={list.error} onRetry={list.error.isTransient ? () => list.refetch() : undefined} />}
        {!data && !list.error && <Spinner label="Loading committed batches…" />}
        {data && (
          <div className={cn('transition-opacity', list.isPlaceholderData && 'opacity-60')}>
            <CommittedTable items={data.items} onView={onView} empty={query ? 'No committed batches match.' : 'No batches committed yet.'} />
          </div>
        )}

        {data && pages > 1 && (
          <div className="flex justify-between">
            <Button variant="secondary" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
              Previous
            </Button>
            <Button variant="secondary" disabled={page >= pages} onClick={() => setPage((p) => p + 1)}>
              Next
            </Button>
          </div>
        )}
      </CardContent>
    </Card>
  );
}
