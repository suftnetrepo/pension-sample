import type { ReactNode } from 'react';
import { Download, ExternalLink } from 'lucide-react';
import { useDownloadReport, useOpenReport } from '@/hooks/mutations';
import type { BatchScope, BatchSummary, ReconciliationLine } from '@/services/types';
import { formatDateTime, formatMoney, formatNumber, shortHash } from '@/services/format';
import { InlineError } from '@/components/common/Feedback';
import { Button } from '@/components/ui/button';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { cn } from '@/lib/utils';

const show = (l: ReconciliationLine, n: number) => (l.isMoney ? formatMoney(n) : formatNumber(n));

export function SummaryTab({ scope, batch }: { scope: BatchScope; batch: BatchSummary }) {
  const report = useOpenReport();
  const reportFile = useDownloadReport();
  return (
    <div className="grid grid-cols-1 gap-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 className="text-base font-semibold">Reconciliation</h2>
          <p className="text-muted-foreground text-xs">Every measure must satisfy: source = transformed + rejected.</p>
        </div>
        <div className="grid justify-items-end gap-1">
          <div className="flex flex-wrap justify-end gap-2">
            <Button variant="secondary" onClick={() => report.run(scope, batch.id)} disabled={report.busy}>
              <ExternalLink aria-hidden="true" />
              {report.busy ? 'Opening…' : 'Open full report'}
            </Button>
            <Button variant="secondary" onClick={() => reportFile.run(scope, batch.id)} disabled={reportFile.busy}>
              <Download aria-hidden="true" />
              {reportFile.busy ? 'Preparing…' : 'Download report'}
            </Button>
          </div>
          <InlineError error={report.error ?? reportFile.error} />
        </div>
      </div>

      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Measure</TableHead>
            <TableHead className="text-right">Source file</TableHead>
            <TableHead className="text-right">Transformed</TableHead>
            <TableHead className="text-right">Rejected</TableHead>
            <TableHead className="text-right">Difference</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {batch.reconciliation.map((l) => (
            <TableRow key={l.measure}>
              <TableCell>{l.measure}</TableCell>
              <TableCell className="text-right">{show(l, l.source)}</TableCell>
              <TableCell className="text-right">{show(l, l.transformed)}</TableCell>
              <TableCell className="text-right">{show(l, l.rejected)}</TableCell>
              <TableCell className={cn('text-right font-semibold', l.difference === 0 ? 'text-success' : 'text-destructive')}>{show(l, l.difference)}</TableCell>
            </TableRow>
          ))}
          {batch.recordChecks.map((c) => (
            <TableRow key={c.type}>
              <TableCell>{c.type} records written</TableCell>
              <TableCell className="text-right">{formatNumber(c.expected)} expected</TableCell>
              <TableCell className="text-right">{formatNumber(c.actual)}</TableCell>
              <TableCell className="text-right">-</TableCell>
              <TableCell className={cn('text-right font-semibold', c.ok ? 'text-success' : 'text-destructive')}>{c.ok ? 'OK' : 'Mismatch'}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>

      <dl className="grid grid-cols-1 gap-x-6 gap-y-3 sm:grid-cols-2">
        <Fact label="Source and schema">
          {batch.sourceName} {batch.schemaVersion}
        </Fact>
        <Fact label="IntelliPen mapping">{batch.mapping ? `${batch.mapping.name} v${batch.mapping.version}` : '-'}</Fact>
        <Fact label="File">
          {batch.fileName} · fingerprint {shortHash(batch.fileSha256)}
        </Fact>
        <Fact label="Uploaded">
          {batch.uploadedBy} · {formatDateTime(batch.uploadedAtUtc)}
        </Fact>
        {scope === 'committed' && batch.commit && (
          <>
            <Fact label="Committed">
              {batch.closedBy} · {formatDateTime(batch.closedAtUtc)}
            </Fact>
            <Fact label="IntelliPen file">
              {batch.commit.outputFileName} · fingerprint {shortHash(batch.commit.outputFileSha256)}
            </Fact>
          </>
        )}
        {batch.counts && (
          <>
            <Fact label="Warnings checked">
              {formatNumber(batch.counts.warningsChecked)} of {formatNumber(batch.counts.warnings)}
            </Fact>
            <Fact label="Automatic corrections">
              {formatNumber(batch.counts.corrections)}
              {batch.counts.corrections > 0 && ' · listed in the Exceptions tab'}
            </Fact>
          </>
        )}
        <Fact label="Trailer record count" className={batch.trailerOk ? 'text-success' : 'text-destructive'}>
          {batch.trailerOk ? 'Agrees with the file' : 'Does not agree'}
        </Fact>
      </dl>
    </div>
  );
}

function Fact({ label, className, children }: { label: string; className?: string; children: ReactNode }) {
  return (
    <div>
      <dt className="text-muted-foreground text-xs">{label}</dt>
      <dd className={cn('mt-0.5', className)}>{children}</dd>
    </div>
  );
}
