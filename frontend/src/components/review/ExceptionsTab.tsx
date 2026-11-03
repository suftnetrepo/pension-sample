import { useState } from 'react';
import { Download } from 'lucide-react';
import { useBatchExceptions } from '@/hooks/queries';
import type { BatchScope } from '@/services/types';
import { useDownloadExceptions } from '@/hooks/mutations';
import { InlineError, Pill, QueryState } from '@/components/common/Feedback';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';

type Only = 'all' | 'Error' | 'Warning';

export function ExceptionsTab({ scope, batchId }: { scope: BatchScope; batchId: string }) {
  const issues = useBatchExceptions(scope, batchId);
  const download = useDownloadExceptions();
  const [only, setOnly] = useState<Only>('all');

  return (
    <div className="grid grid-cols-1 gap-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <p className="text-muted-foreground text-xs">Errors stop a line being loaded. Warnings are loaded but flagged for a check.</p>
        <div className="flex flex-wrap items-end gap-3">
          <div className="grid grid-cols-1 gap-2">
            <Label htmlFor="severity">Show</Label>
            <Select value={only} onValueChange={(v) => setOnly(v as Only)}>
              <SelectTrigger id="severity" className="w-52">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="all">Errors and warnings</SelectItem>
                <SelectItem value="Error">Errors only</SelectItem>
                <SelectItem value="Warning">Warnings only</SelectItem>
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
        {(all) => {
          const list = all.filter((i) => only === 'all' || i.severity === only);
          return (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Severity</TableHead>
                  <TableHead>Line</TableHead>
                  <TableHead>Member</TableHead>
                  <TableHead>Field</TableHead>
                  <TableHead>Value</TableHead>
                  <TableHead>Rule</TableHead>
                  <TableHead>What to do</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {list.map((i, n) => (
                  <TableRow key={n}>
                    <TableCell>
                      <Pill tone={i.severity === 'Error' ? 'error' : 'warn'}>{i.severity}</Pill>
                    </TableCell>
                    <TableCell>{i.lineNumber}</TableCell>
                    <TableCell>{i.key}</TableCell>
                    <TableCell>{i.field}</TableCell>
                    <TableCell className="font-mono">{i.value || '(blank)'}</TableCell>
                    <TableCell className="min-w-52 whitespace-normal">{i.rule}</TableCell>
                    <TableCell className="min-w-52 whitespace-normal">{i.action}</TableCell>
                  </TableRow>
                ))}
                {list.length === 0 && (
                  <TableRow>
                    <TableCell colSpan={7} className="text-muted-foreground">
                      None.
                    </TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
          );
        }}
      </QueryState>
    </div>
  );
}
