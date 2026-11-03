import { useEffect, useState } from 'react';
import { useCommittedList, useSources } from '@/hooks/queries';
import { useUploadBatch } from '@/hooks/mutations';
import type { BatchSummary } from '@/services/types';
import { ErrorBanner, QueryState } from '@/components/common/Feedback';
import { ExpectedFormat, FileDrop, MismatchPanel, SourcePicker, UploadProgress } from '@/components/upload/UploadParts';
import { CommittedTable } from '@/components/committed/CommittedTable';
import { Button } from '@/components/ui/button';
import { Card, CardAction, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from '@/components/ui/card';

const maxBytes = 10 * 1024 * 1024;

export function UploadPage(props: { onUploaded: (batch: BatchSummary) => void; onViewCommitted: (id: string) => void; onAllCommitted: () => void }) {
  const { onUploaded } = props;
  const sources = useSources();
  const recent = useCommittedList('', 1, 5);
  const { upload, phase, percent, cancel, error, reset, busy } = useUploadBatch(onUploaded);
  const [source, setSource] = useState('');
  const [version, setVersion] = useState('');
  const [file, setFile] = useState<File | null>(null);

  // Default to the first source, and to that source's latest agreed version.
  useEffect(() => {
    const list = sources.data;
    if (!list?.length || source) return;
    setSource(list[0].source);
    setVersion(list[0].versions.at(-1)?.version ?? '');
  }, [sources.data, source]);

  const selected = sources.data?.find((s) => s.source === source);
  const chosenVersion = selected?.versions.find((v) => v.version === version);

  function changeSource(s: string) {
    setSource(s);
    setVersion(sources.data?.find((x) => x.source === s)?.versions.at(-1)?.version ?? '');
    reset();
  }

  return (
    <div className="grid grid-cols-1 gap-5">
      <Card>
        <CardHeader>
          <CardTitle className="text-[22px]">Upload a member file</CardTitle>
          <CardDescription>
            Choose the source and its agreed schema version. The file is checked against that layout and transformed. Nothing is saved until you commit.
          </CardDescription>
        </CardHeader>
        <CardContent className="grid grid-cols-1 gap-4">
          <QueryState query={sources} loading="Loading sources…">
            {(list) => (
              <div className="grid grid-cols-1 gap-4">
                <SourcePicker
                  sources={list}
                  source={source}
                  version={version}
                  onSource={changeSource}
                  onVersion={(v) => {
                    setVersion(v);
                    reset();
                  }}
                  disabled={busy}
                />
                {chosenVersion && <ExpectedFormat version={chosenVersion} />}
              </div>
            )}
          </QueryState>

          <FileDrop
            file={file}
            onFile={(f) => {
              setFile(f);
              reset();
            }}
            disabled={busy}
            maxBytes={maxBytes}
          />

          {busy && file && <UploadProgress phase={phase} percent={percent} fileName={file.name} onCancel={cancel} />}

          {error?.mismatch ? (
            <MismatchPanel mismatch={error.mismatch} title={error.title} message={error.message} onDismiss={reset} />
          ) : (
            error && <ErrorBanner error={error} onClose={reset} />
          )}
        </CardContent>
        <CardFooter className="justify-end">
          <Button disabled={!file || !chosenVersion || busy} onClick={() => file && upload({ file, source, version })}>
            {busy ? 'Working…' : 'Upload and process'}
          </Button>
        </CardFooter>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Recently committed</CardTitle>
          <CardAction>
            <Button variant="link" onClick={props.onAllCommitted}>
              View all committed batches
            </Button>
          </CardAction>
        </CardHeader>
        <CardContent>
          <QueryState query={recent} loading="Loading recent batches…">
            {(page) => <CommittedTable items={page.items} onView={props.onViewCommitted} empty="No batches committed yet." />}
          </QueryState>
        </CardContent>
      </Card>
    </div>
  );
}
