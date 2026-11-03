import { Download } from 'lucide-react';
import { useFilePreview } from '@/hooks/queries';
import { useDownloadFile } from '@/hooks/mutations';
import { formatNumber } from '@/services/format';
import type { BatchScope } from '@/services/types';
import { InlineError, QueryState } from '@/components/common/Feedback';
import { Button } from '@/components/ui/button';

export function FilePreviewTab({ scope, batchId }: { scope: BatchScope; batchId: string }) {
  const preview = useFilePreview(scope, batchId, 50);
  const download = useDownloadFile();

  return (
    <QueryState query={preview} loading="Loading the file preview…">
      {(p) => (
        <div className="grid grid-cols-1 gap-4">
          <div className="flex flex-wrap items-end justify-between gap-3">
            <div>
              <h2 className="text-base font-semibold">{p.fileName}</h2>
              <p className="text-muted-foreground text-xs">
                First {p.lines.length} of {formatNumber(p.totalLines)} lines{scope === 'committed' ? ' · exactly as delivered at commit' : ''}
              </p>
            </div>
            <div className="grid justify-items-end gap-1">
              <Button variant="secondary" onClick={() => download.run(scope, batchId)} disabled={download.busy}>
                <Download aria-hidden="true" />
                {download.busy ? 'Preparing…' : 'Download full file'}
              </Button>
              <InlineError error={download.error} />
            </div>
          </div>
          <pre className="bg-foreground overflow-x-auto rounded-lg p-4 font-mono text-[13px] leading-relaxed text-[#e7ecf3]" aria-label="IntelliPen file preview">
            {p.lines.join('\n')}
          </pre>
        </div>
      )}
    </QueryState>
  );
}
