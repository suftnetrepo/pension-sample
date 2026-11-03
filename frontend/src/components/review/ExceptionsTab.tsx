import { useState } from 'react';
import { Check, Download, Undo2 } from 'lucide-react';
import { useBatchCorrections, useBatchExceptions } from '@/hooks/queries';
import type { Acknowledgement, BatchScope, Issue } from '@/services/types';
import { useAcknowledge, useDownloadCorrections, useDownloadExceptions, useUnacknowledge } from '@/hooks/mutations';
import { formatDateTime } from '@/services/format';
import { InlineError, Pill, QueryState } from '@/components/common/Feedback';
import type { ExceptionFilter } from '@/components/review/useReviewView';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';

interface Props {
  scope: BatchScope;
  batchId: string;
  filter: ExceptionFilter;
  onFilterChange: (filter: ExceptionFilter) => void;
}

/**
 * Errors and warnings, then the automatic corrections. In a held batch a reviewer ticks warnings and marks them as checked;
 * a committed batch shows who checked each one, read-only.
 */
export function ExceptionsTab({ scope, batchId, filter, onFilterChange }: Props) {
  const held = scope === 'held';
  const issues = useBatchExceptions(scope, batchId);
  const download = useDownloadExceptions();

  return (
    <div className="grid grid-cols-1 gap-8">
      <div className="grid grid-cols-1 gap-4">
        <div className="flex flex-wrap items-end justify-between gap-3">
          <p className="text-muted-foreground text-xs">
            Errors stop a line being loaded: they are corrected in the source file and the file sent again.
            {held ? ' Warnings are loaded, but each one must be checked before the batch can be committed.' : ' Warnings were loaded; who checked each is shown.'}
          </p>
          <div className="flex flex-wrap items-end gap-3">
            <div className="grid grid-cols-1 gap-2">
              <Label htmlFor="severity">Show</Label>
              <Select value={filter} onValueChange={(v) => onFilterChange(v as ExceptionFilter)}>
                <SelectTrigger id="severity" className="w-60">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">Errors and warnings</SelectItem>
                  <SelectItem value="Error">Errors only</SelectItem>
                  <SelectItem value="Warning">Warnings only</SelectItem>
                  {held && <SelectItem value="unchecked">Warnings not yet checked</SelectItem>}
                </SelectContent>
              </Select>
            </div>
            <div className="grid justify-items-end gap-1">
              <Button variant="secondary" onClick={() => download.run(scope, batchId)} disabled={download.busy}>
                <Download aria-hidden="true" />
                {download.busy ? 'Preparing…' : 'Download exceptions (CSV)'}
              </Button>
              <InlineError error={download.error} />
            </div>
          </div>
        </div>

        <QueryState query={issues} loading="Loading exceptions…">
          {(all) => <IssueTable all={all} held={held} batchId={batchId} filter={filter} />}
        </QueryState>
      </div>

      <CorrectionsSection scope={scope} batchId={batchId} />
    </div>
  );
}

function IssueTable({ all, held, batchId, filter }: { all: Issue[]; held: boolean; batchId: string; filter: ExceptionFilter }) {
  const acknowledge = useAcknowledge(batchId);
  const undo = useUnacknowledge(batchId);
  const [selected, setSelected] = useState<ReadonlySet<number>>(new Set());
  const [note, setNote] = useState('');

  const list = all.filter((i) => {
    if (filter === 'all') return true;
    if (filter === 'unchecked') return i.severity === 'Warning' && !i.checked;
    return i.severity === filter;
  });
  const canTick = (i: Issue) => held && i.severity === 'Warning' && !i.checked;
  const tickable = list.filter(canTick).map((i) => i.id);
  const chosen = tickable.filter((id) => selected.has(id));
  const allChosen = tickable.length > 0 && chosen.length === tickable.length;

  const toggle = (id: number, on: boolean) =>
    setSelected((prev) => {
      const next = new Set(prev);
      if (on) next.add(id);
      else next.delete(id);
      return next;
    });

  const mark = () =>
    acknowledge.mutate(
      { issueIds: chosen, note },
      {
        onSuccess: () => {
          setSelected(new Set());
          setNote('');
        },
      },
    );

  return (
    <div className="grid grid-cols-1 gap-3">
      {held && tickable.length > 0 && (
        <div className="bg-muted/40 grid grid-cols-1 gap-3 rounded-md border p-3 lg:grid-cols-[auto_1fr_auto] lg:items-center">
          <div className="flex min-h-11 items-center gap-3">
            <Checkbox
              id="select-all-warnings"
              checked={allChosen}
              onCheckedChange={(v) => setSelected(v === true ? new Set(tickable) : new Set())}
              disabled={acknowledge.isPending}
            />
            <Label htmlFor="select-all-warnings" className="cursor-pointer">
              Select all shown warnings ({tickable.length})
            </Label>
          </div>
          <Input
            aria-label="Note for the warnings you mark as checked (optional)"
            placeholder="Note (optional), e.g. confirmed with the employer"
            maxLength={500}
            value={note}
            onChange={(e) => setNote(e.target.value)}
            disabled={acknowledge.isPending}
          />
          <div className="grid justify-items-end gap-1">
            <Button onClick={mark} disabled={chosen.length === 0 || acknowledge.isPending}>
              <Check aria-hidden="true" />
              {acknowledge.isPending ? 'Saving…' : `Mark as checked${chosen.length > 0 ? ` (${chosen.length})` : ''}`}
            </Button>
            <InlineError error={acknowledge.error} />
          </div>
        </div>
      )}

      <Table>
        <TableHeader>
          <TableRow>
            {held && (
              <TableHead className="w-10">
                <span className="sr-only">Select</span>
              </TableHead>
            )}
            <TableHead>Severity</TableHead>
            <TableHead>Line</TableHead>
            <TableHead>Member</TableHead>
            <TableHead>Field</TableHead>
            <TableHead>Value</TableHead>
            <TableHead>Rule</TableHead>
            <TableHead>What to do</TableHead>
            <TableHead>Who acts</TableHead>
            <TableHead>Checked</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {list.map((i) => (
            <TableRow key={i.id}>
              {held && (
                <TableCell>
                  {canTick(i) && (
                    <Checkbox
                      aria-label={`Select the warning on line ${i.lineNumber}, ${i.field}`}
                      checked={selected.has(i.id)}
                      onCheckedChange={(v) => toggle(i.id, v === true)}
                      disabled={acknowledge.isPending}
                    />
                  )}
                </TableCell>
              )}
              <TableCell>
                <Pill tone={i.severity === 'Error' ? 'error' : 'warn'}>{i.severity}</Pill>
              </TableCell>
              <TableCell>{i.lineNumber}</TableCell>
              <TableCell>{i.key}</TableCell>
              <TableCell>{i.field}</TableCell>
              <TableCell className="font-mono">{i.value || '(blank)'}</TableCell>
              <TableCell className="min-w-52 whitespace-normal">{i.rule}</TableCell>
              <TableCell className="min-w-52 whitespace-normal">{i.action}</TableCell>
              <TableCell>{i.owner}</TableCell>
              <TableCell className="min-w-44 whitespace-normal">
                {i.checked ? (
                  <div className="grid justify-items-start gap-1">
                    <CheckedBadge checked={i.checked} />
                    {held && (
                      <Button variant="ghost" size="sm" onClick={() => undo.mutate(i.id)} disabled={undo.isPending}>
                        <Undo2 aria-hidden="true" />
                        Undo
                      </Button>
                    )}
                  </div>
                ) : i.severity === 'Warning' ? (
                  <span className="text-muted-foreground text-xs">Not checked</span>
                ) : (
                  <span className="text-muted-foreground">-</span>
                )}
              </TableCell>
            </TableRow>
          ))}
          {list.length === 0 && (
            <TableRow>
              <TableCell colSpan={held ? 10 : 9} className="text-muted-foreground">
                None.
              </TableCell>
            </TableRow>
          )}
        </TableBody>
      </Table>
      <InlineError error={undo.error} />
    </div>
  );
}

function CheckedBadge({ checked }: { checked: Acknowledgement }) {
  return (
    <div className="grid justify-items-start gap-0.5">
      <Badge variant="success">
        <Check aria-hidden="true" />
        {checked.by} · {formatDateTime(checked.atUtc)}
      </Badge>
      {checked.note && <span className="text-muted-foreground text-xs">{checked.note}</span>}
    </div>
  );
}

/** Format fixes made automatically while reading the file, so nothing changes silently. */
function CorrectionsSection({ scope, batchId }: { scope: BatchScope; batchId: string }) {
  const corrections = useBatchCorrections(scope, batchId);
  const download = useDownloadCorrections();
  return (
    <section className="grid grid-cols-1 gap-3" aria-labelledby="corrections-heading">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 id="corrections-heading" className="text-base font-semibold">
            Automatic corrections{corrections.data ? ` (${corrections.data.length})` : ''}
          </h2>
          <p className="text-muted-foreground text-xs">
            Safe format fixes made while reading the file. They never change which lines are accepted or what is written to the IntelliPen file.
          </p>
        </div>
        <div className="grid justify-items-end gap-1">
          <Button variant="secondary" onClick={() => download.run(scope, batchId)} disabled={download.busy}>
            <Download aria-hidden="true" />
            {download.busy ? 'Preparing…' : 'Download corrections (CSV)'}
          </Button>
          <InlineError error={download.error} />
        </div>
      </div>
      <QueryState query={corrections} loading="Loading corrections…">
        {(list) => (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Line</TableHead>
                <TableHead>Member</TableHead>
                <TableHead>Column</TableHead>
                <TableHead>Before</TableHead>
                <TableHead>After</TableHead>
                <TableHead>What was done</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {list.map((c, n) => (
                <TableRow key={n}>
                  <TableCell>{c.lineNumber}</TableCell>
                  <TableCell>{c.key}</TableCell>
                  <TableCell>{c.column}</TableCell>
                  <TableCell className="font-mono whitespace-pre">{c.before}</TableCell>
                  <TableCell className="font-mono">{c.after}</TableCell>
                  <TableCell className="min-w-52 whitespace-normal">{c.description}</TableCell>
                </TableRow>
              ))}
              {list.length === 0 && (
                <TableRow>
                  <TableCell colSpan={6} className="text-muted-foreground">
                    None.
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        )}
      </QueryState>
    </section>
  );
}
