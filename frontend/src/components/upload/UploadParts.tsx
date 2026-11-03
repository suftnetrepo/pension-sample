import { useRef, useState, type DragEvent } from 'react';
import { AlertCircle, ChevronDown, FileText, Upload } from 'lucide-react';
import type { SchemaMismatch, Source, SourceVersion } from '@/services/types';
import { describeDateFormat, describeDelimiter } from '@/services/format';
import type { UploadPhase } from '@/hooks/mutations';
import { ProgressBar } from '@/components/common/Feedback';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible';
import { Label } from '@/components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { cn } from '@/lib/utils';

/** Source and Version dropdowns. Version lists only the versions agreed for the chosen source. */
export function SourcePicker(props: {
  sources: Source[];
  source: string;
  version: string;
  onSource: (s: string) => void;
  onVersion: (v: string) => void;
  disabled: boolean;
}) {
  const selected = props.sources.find((s) => s.source === props.source);
  return (
    <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
      <div className="grid grid-cols-1 gap-2">
        <Label htmlFor="source">Source</Label>
        <Select value={props.source} onValueChange={props.onSource} disabled={props.disabled}>
          <SelectTrigger id="source">
            <SelectValue placeholder="Choose a source" />
          </SelectTrigger>
          <SelectContent>
            {props.sources.map((s) => (
              <SelectItem key={s.source} value={s.source}>
                {s.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <div className="grid grid-cols-1 gap-2">
        <Label htmlFor="version">Schema version</Label>
        <Select value={props.version} onValueChange={props.onVersion} disabled={props.disabled || !selected}>
          <SelectTrigger id="version">
            <SelectValue placeholder="Choose a version" />
          </SelectTrigger>
          <SelectContent>
            {selected?.versions.map((v) => (
              <SelectItem key={v.version} value={v.version}>
                {v.version}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <div className="grid grid-cols-1 gap-2">
        <span className="text-sm font-medium">Scheme</span>
        <div className="bg-muted/60 flex h-10 items-center rounded-md border px-3">{selected?.scheme ?? '-'}</div>
      </div>
    </div>
  );
}

/** What the chosen version expects, so users can check their file before uploading. */
export function ExpectedFormat({ version }: { version: SourceVersion }) {
  const [open, setOpen] = useState(false);
  return (
    <Collapsible open={open} onOpenChange={setOpen} className="bg-muted/30 grid grid-cols-1 gap-1.5 rounded-lg border p-4">
      <div>
        <span className="font-semibold">This version expects: </span>
        {version.columns.length} columns · {describeDelimiter(version.delimiter)} · dates as {describeDateFormat(version.dateFormat)} · first line is
        headings
      </div>
      <div className="text-muted-foreground text-xs">
        {version.description}
        {version.agreement && ` · Agreement: ${version.agreement}`}
      </div>
      <CollapsibleTrigger asChild>
        <Button variant="link" size="sm" className="w-fit">
          {open ? 'Hide' : 'Show'} the expected columns
          <ChevronDown className={cn('transition-transform', open && 'rotate-180')} aria-hidden="true" />
        </Button>
      </CollapsibleTrigger>
      <CollapsibleContent>
        <ol className="list-decimal columns-1 gap-6 pl-5 font-mono text-[13px] sm:columns-3">
          {version.columns.map((c) => (
            <li key={c}>{c}</li>
          ))}
        </ol>
      </CollapsibleContent>
    </Collapsible>
  );
}

/** Drag-and-drop area plus a Choose file button. Checks type and size before anything is sent. */
export function FileDrop({ file, onFile, disabled, maxBytes }: { file: File | null; onFile: (f: File | null) => void; disabled: boolean; maxBytes: number }) {
  const input = useRef<HTMLInputElement>(null);
  const [dragging, setDragging] = useState(false);
  const [problem, setProblem] = useState('');
  const maxMb = Math.round(maxBytes / 1048576);

  function choose(f: File | undefined) {
    if (!f) return;
    if (!f.name.toLowerCase().endsWith('.csv')) return setProblem('Only .csv files can be uploaded.');
    if (f.size > maxBytes) return setProblem(`The file is larger than ${maxMb} MB.`);
    setProblem('');
    onFile(f);
  }

  function drop(e: DragEvent) {
    e.preventDefault();
    setDragging(false);
    if (!disabled) choose(e.dataTransfer.files[0]);
  }

  return (
    <div className="grid grid-cols-1 gap-2">
      {!file ? (
        <div
          className={cn(
            'text-primary flex flex-col items-center gap-3 rounded-xl border-2 border-dashed border-[#b8c3d1] bg-[#fafbfd] px-6 py-10 text-center transition-colors',
            dragging && 'border-primary bg-accent',
          )}
          onDragOver={(e) => {
            e.preventDefault();
            setDragging(true);
          }}
          onDragLeave={() => setDragging(false)}
          onDrop={drop}
        >
          <Upload className="size-10" strokeWidth={1.6} aria-hidden="true" />
          <div className="text-foreground font-semibold">Drag a CSV file here</div>
          <div className="text-muted-foreground text-xs">CSV only · up to {maxMb} MB</div>
          <Button variant="secondary" onClick={() => input.current?.click()} disabled={disabled}>
            Choose file
          </Button>
          <input ref={input} type="file" accept=".csv,text/csv" hidden onChange={(e) => choose(e.target.files?.[0])} />
        </div>
      ) : (
        <div className="flex items-center justify-between rounded-xl border bg-[#fafbfd] px-4 py-4">
          <div className="flex items-center gap-3">
            <FileText className="text-primary size-6" aria-hidden="true" />
            <div>
              <div className="font-semibold">{file.name}</div>
              <div className="text-muted-foreground text-xs">{(file.size / 1024).toFixed(1)} KB</div>
            </div>
          </div>
          <Button variant="link" onClick={() => onFile(null)} disabled={disabled}>
            Remove
          </Button>
        </div>
      )}
      {problem && (
        <p className="text-destructive text-sm" role="alert">
          {problem}
        </p>
      )}
    </div>
  );
}

/** Upload progress, then "processing" while the server works, with Cancel throughout. */
export function UploadProgress({ phase, percent, fileName, onCancel }: { phase: UploadPhase; percent: number; fileName: string; onCancel: () => void }) {
  return (
    <div className="rounded-lg border p-4" aria-live="polite">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <span className="font-semibold">
          {phase === 'uploading' ? `Uploading ${fileName}: ${percent}%` : `Processing ${fileName}: checking, transforming and reconciling`}
        </span>
        <Button variant="secondary" size="sm" onClick={onCancel}>
          Cancel
        </Button>
      </div>
      <ProgressBar percent={phase === 'uploading' ? percent : null} label={phase === 'uploading' ? 'Upload progress' : 'Processing'} />
    </div>
  );
}

/** The file didn't match the agreed layout: exactly what differs, and the likely cause. */
export function MismatchPanel({ mismatch, title, message, onDismiss }: { mismatch: SchemaMismatch; title: string; message: string; onDismiss: () => void }) {
  return (
    <Alert variant="destructive">
      <AlertCircle />
      <AlertTitle>{title}</AlertTitle>
      <AlertDescription className="gap-2">
        <p>{message}</p>
        {mismatch.hint && <p>{mismatch.hint}</p>}
        {mismatch.matchesInstead && (
          <p>
            These headings match <strong>{mismatch.matchesInstead}</strong>. Did you choose the wrong source or version?
          </p>
        )}
        <dl className="grid grid-cols-1 gap-1.5">
          {mismatch.missing.length > 0 && <MismatchList label={`Missing columns (${mismatch.missing.length})`} items={mismatch.missing} />}
          {mismatch.unexpected.length > 0 && !mismatch.hint && (
            <MismatchList label={`Unexpected columns (${mismatch.unexpected.length})`} items={mismatch.unexpected} />
          )}
          {mismatch.duplicated.length > 0 && <MismatchList label="Columns appearing twice" items={mismatch.duplicated} />}
        </dl>
        <Button variant="link" size="sm" className="text-destructive" onClick={onDismiss}>
          Dismiss
        </Button>
      </AlertDescription>
    </Alert>
  );
}

function MismatchList({ label, items }: { label: string; items: string[] }) {
  return (
    <div>
      <dt className="font-semibold">{label}</dt>
      <dd className="mt-0.5 font-mono text-[13px]">{items.join(', ')}</dd>
    </div>
  );
}
