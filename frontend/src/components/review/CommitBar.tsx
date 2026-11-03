import { useState } from 'react';
import { useCommitBatch, useDiscardBatch } from '@/hooks/mutations';
import type { BatchSummary } from '@/services/types';
import { formatNumber } from '@/services/format';
import { ErrorBanner } from '@/components/common/Feedback';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from '@/components/ui/alert-dialog';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { Checkbox } from '@/components/ui/checkbox';
import { Label } from '@/components/ui/label';

/** "I have reviewed" + Commit / Discard. Commit stays disabled until the box is ticked and the API says it may commit. */
export function CommitBar({ batch, onDiscarded }: { batch: BatchSummary; onDiscarded: () => void }) {
  const [reviewed, setReviewed] = useState(false);
  const commit = useCommitBatch(batch.id);
  const discard = useDiscardBatch(batch.id, onDiscarded);
  const busy = commit.isPending || discard.isPending;
  const counts = batch.counts!;

  const blocked = !batch.balanced
    ? 'The reconciliation does not balance, so this batch cannot be committed.'
    : counts.accepted === 0
      ? 'Every line was rejected. Correct the file and upload it again.'
      : !batch.canCommit
        ? 'You can review this batch, but committing needs the Import.Committer role.'
        : null;
  const error = commit.error ?? discard.error;

  return (
    <Card className="py-5">
      <CardContent className="grid grid-cols-1 gap-3">
        {error && !error.isGone && (
          <ErrorBanner
            error={error}
            onClose={() => {
              commit.reset();
              discard.reset();
            }}
          />
        )}
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div className="grid grid-cols-1 gap-1.5">
            <div className="flex min-h-11 items-center gap-3">
              <Checkbox id="reviewed" checked={reviewed} onCheckedChange={(v) => setReviewed(v === true)} disabled={!!blocked || busy} />
              <Label htmlFor="reviewed" className="cursor-pointer">
                I have reviewed the transformed data, exceptions and reconciliation
              </Label>
            </div>
            <span className="text-muted-foreground text-xs">
              {blocked ??
                `${formatNumber(counts.accepted)} members will be saved and written to the IntelliPen file. ` +
                  `${formatNumber(counts.rejected)} rejected lines are listed in the reconciliation report, not loaded.`}
            </span>
          </div>
          <div className="flex flex-wrap gap-3">
            <AlertDialog>
              <AlertDialogTrigger asChild>
                <Button variant="secondary" disabled={busy}>
                  {discard.isPending ? 'Discarding…' : 'Discard batch'}
                </Button>
              </AlertDialogTrigger>
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>Discard batch {batch.id}?</AlertDialogTitle>
                  <AlertDialogDescription>
                    The transformed data is deleted and nothing is saved. To import this file later you will need to upload it again.
                  </AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel>Keep it</AlertDialogCancel>
                  <AlertDialogAction onClick={() => discard.mutate()}>Yes, discard it</AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
            <Button onClick={() => commit.mutate()} disabled={!reviewed || !!blocked || busy}>
              {commit.isPending ? 'Committing…' : 'Commit and send report'}
            </Button>
          </div>
        </div>
      </CardContent>
    </Card>
  );
}
