import { useEffect, useState } from 'react';
import { useBatchRows } from '@/hooks/queries';
import { useDebouncedValue } from '@/hooks/useDebouncedValue';
import type { BatchScope, MemberRow, RowFilter } from '@/services/types';
import { formatNumber } from '@/services/format';
import { ErrorBanner, Pill, Spinner } from '@/components/common/Feedback';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { cn } from '@/lib/utils';

const pageSize = 25;

/**
 * Members as transformed. Columns come from the first record (e.g. MEM), so the table follows whatever the
 * IntelliPen mapping defines; selecting a member shows its source line and every target record side by side.
 */
const filters: { value: RowFilter; label: string; scopes: BatchScope[] }[] = [
  { value: 'accepted', label: 'Transformed members', scopes: ['held', 'committed'] },
  { value: 'warnings', label: 'With warnings', scopes: ['held', 'committed'] },
  { value: 'rejected', label: 'Rejected lines', scopes: ['held'] },
  { value: 'all', label: 'All lines', scopes: ['held'] },
];

export function DataTab({ scope, batchId }: { scope: BatchScope; batchId: string }) {
  const [filter, setFilter] = useState<RowFilter>('accepted');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [selectedLine, setSelectedLine] = useState<number | null>(null);
  const query = useDebouncedValue(search.trim());
  const rows = useBatchRows(scope, batchId, page, pageSize, query, filter);

  useEffect(() => setPage(1), [query, filter]);

  const data = rows.data;
  const selected: MemberRow | undefined = data?.items.find((m) => m.lineNumber === selectedLine) ?? data?.items[0];
  const firstRecord = data?.items.find((m) => m.records.length > 0)?.records[0];
  const columns = (firstRecord?.fields ?? []).map((f) => f.name).filter((n) => n !== 'RecordType');
  const pages = data ? Math.max(1, Math.ceil(data.total / pageSize)) : 1;

  return (
    <div className="grid grid-cols-1 gap-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div className="flex flex-wrap gap-3">
          <div className="grid grid-cols-1 gap-2">
            <Label htmlFor="show">Show</Label>
            <Select value={filter} onValueChange={(v) => setFilter(v as RowFilter)}>
              <SelectTrigger id="show" className="w-52">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {filters
                  .filter((f) => f.scopes.includes(scope))
                  .map((f) => (
                    <SelectItem key={f.value} value={f.value}>
                      {f.label}
                    </SelectItem>
                  ))}
              </SelectContent>
            </Select>
          </div>
          <div className="grid grid-cols-1 gap-2">
            <Label htmlFor="search">Search</Label>
            <Input id="search" type="search" className="w-64 max-w-full" placeholder="Member ID or name" value={search} onChange={(e) => setSearch(e.target.value)} />
          </div>
        </div>
        <span className="text-muted-foreground text-xs" aria-live="polite">
          {data ? `${formatNumber(data.total)} ${scope === 'committed' ? 'members' : 'lines'} · page ${page} of ${pages}` : ''}
          {rows.isFetching && data ? ' · updating…' : ''}
        </span>
      </div>

      {rows.error && <ErrorBanner error={rows.error} onRetry={rows.error.isTransient ? () => rows.refetch() : undefined} />}
      {!data && !rows.error && <Spinner label="Loading members…" />}

      {data && (
        <>
          <div className={cn('transition-opacity', rows.isPlaceholderData && 'opacity-60')}>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Line</TableHead>
                  <TableHead>Member</TableHead>
                  <TableHead>Result</TableHead>
                  {columns.map((c) => (
                    <TableHead key={c}>{c}</TableHead>
                  ))}
                  <TableHead>Records</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data.items.map((m) => (
                  <TableRow key={m.lineNumber} data-state={selected?.lineNumber === m.lineNumber ? 'selected' : undefined}>
                    <TableCell>{m.lineNumber}</TableCell>
                    <TableCell>
                      <Button variant="link" size="sm" onClick={() => setSelectedLine(m.lineNumber)} aria-label={`Show line ${m.lineNumber}, ${m.key}`}>
                        {m.key || '(blank)'}
                      </Button>
                    </TableCell>
                    <TableCell>{!m.accepted ? <Pill tone="error">Rejected</Pill> : m.hasWarnings ? <Pill tone="warn">Warning</Pill> : <Pill tone="ok">OK</Pill>}</TableCell>
                    {columns.map((c) => (
                      <TableCell key={c}>{m.records[0]?.fields.find((f) => f.name === c)?.value ?? ''}</TableCell>
                    ))}
                    <TableCell>{m.records.length}</TableCell>
                  </TableRow>
                ))}
                {data.items.length === 0 && (
                  <TableRow>
                    <TableCell colSpan={columns.length + 4} className="text-muted-foreground">
                      No lines match.
                    </TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
          </div>

          <div className="flex justify-between">
            <Button variant="secondary" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
              Previous
            </Button>
            <Button variant="secondary" disabled={page >= pages} onClick={() => setPage((p) => p + 1)}>
              Next
            </Button>
          </div>

          {selected && <MemberDetail member={selected} />}
        </>
      )}
    </div>
  );
}

function MemberDetail({ member }: { member: MemberRow }) {
  return (
    <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
      <div className="rounded-lg border p-4">
        <h3 className="mb-2.5 font-semibold">Source line {member.lineNumber} (as supplied)</h3>
        {member.source.length > 0 ? (
          <KeyValues items={member.source.map((s) => ({ key: s.column, label: s.column, value: s.value || '(blank)' }))} />
        ) : (
          <p className="text-muted-foreground">
            The source line isn't kept after commit, only the IntelliPen records it became. The original file can be matched by its fingerprint on the
            Summary tab.
          </p>
        )}
      </div>
      <div className="rounded-lg border border-[#bfd2ec] bg-[#f7fafe] p-4">
        <h3 className="mb-2.5 font-semibold">IntelliPen records for {member.key}</h3>
        {member.records.length === 0 && <p className="text-destructive">Rejected: not transformed. See the Exceptions tab.</p>}
        {member.records.map((r, i) => (
          <KeyValues key={i} items={r.fields.map((f) => ({ key: f.name, label: `${r.type}.${f.name}`, value: f.value || '(empty)' }))} />
        ))}
      </div>
    </div>
  );
}

function KeyValues({ items }: { items: { key: string; label: string; value: string }[] }) {
  return (
    <dl className="mb-3 grid grid-cols-1 gap-1.5">
      {items.map((i) => (
        <div key={i.key} className="border-muted flex justify-between gap-3 border-b pb-1.5">
          <dt className="text-muted-foreground">{i.label}</dt>
          <dd className="text-right font-mono">{i.value}</dd>
        </div>
      ))}
    </dl>
  );
}
