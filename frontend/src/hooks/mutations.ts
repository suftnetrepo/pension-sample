import { useCallback, useRef, useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../services/http';
import { importsService } from '../services/importsService';
import type { BatchSummary } from '../services/types';
import { queryKeys } from './queryKeys';

export type UploadPhase = 'idle' | 'uploading' | 'processing';

/**
 * Upload with progress and cancel. Phase goes uploading (0–100%) → processing (server working) → done.
 * On success the batch summary is put straight into the cache, so the review screen needs no extra request.
 */
export function useUploadBatch(onUploaded: (batch: BatchSummary) => void) {
  const client = useQueryClient();
  const [percent, setPercent] = useState(0);
  const [phase, setPhase] = useState<UploadPhase>('idle');
  const cancelRef = useRef<() => void>(() => {});

  const mutation = useMutation<BatchSummary, ApiError, { file: File; source: string; version: string }>({
    mutationFn: async ({ file, source, version }) => {
      setPercent(0);
      setPhase('uploading');
      const handle = importsService.upload(file, source, version, (p) => {
        setPercent(p);
        if (p >= 100) setPhase('processing');
      });
      cancelRef.current = handle.cancel;
      try {
        return await handle.promise;
      } catch (e) {
        throw ApiError.from(e);
      }
    },
    onSuccess: (batch) => {
      client.setQueryData(queryKeys.batch(batch.id), batch);
      onUploaded(batch);
    },
    onSettled: () => setPhase('idle'),
  });

  const cancel = useCallback(() => cancelRef.current(), []);
  const error = mutation.error && mutation.error.kind !== 'cancelled' ? mutation.error : null;
  return { upload: mutation.mutate, phase, percent, cancel, error, reset: mutation.reset, busy: mutation.isPending };
}

export function useCommitBatch(id: string) {
  const client = useQueryClient();
  return useMutation<BatchSummary, ApiError>({
    mutationFn: () => importsService.commit(id).catch((e) => Promise.reject(ApiError.from(e))),
    onSuccess: (batch) => {
      client.setQueryData(queryKeys.batch(id), batch);
      client.removeQueries({ queryKey: queryKeys.batch(id), exact: false, predicate: (q) => q.queryKey.length > 2 });
      void client.invalidateQueries({ queryKey: queryKeys.committedListAll });
    },
    onError: (e) => e.isGone && client.invalidateQueries({ queryKey: queryKeys.batch(id), exact: true }),
  });
}

export function useDiscardBatch(id: string, onDiscarded: () => void) {
  const client = useQueryClient();
  return useMutation<void, ApiError>({
    mutationFn: () => importsService.discard(id).catch((e) => Promise.reject(ApiError.from(e))),
    onSuccess: () => {
      client.removeQueries({ queryKey: queryKeys.batch(id) });
      onDiscarded();
    },
  });
}

/**
 * A one-off action with its own busy and error state: downloads, opening the report.
 * Errors stay with the button that caused them instead of replacing the page.
 */
export function useAction<TArgs extends unknown[]>(action: (...args: TArgs) => Promise<void>) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const run = useCallback(
    async (...args: TArgs) => {
      setBusy(true);
      setError(null);
      try {
        await action(...args);
      } catch (e) {
        setError(ApiError.from(e));
      } finally {
        setBusy(false);
      }
    },
    [action],
  );
  return { run, busy, error, clearError: () => setError(null) };
}

// Each takes (scope, batchId): the same buttons work for a held batch and a committed one.
export const useDownloadExceptions = () => useAction(importsService.downloadExceptions);
export const useDownloadFile = () => useAction(importsService.downloadFile);
export const useDownloadReport = () => useAction(importsService.downloadReport);
export const useOpenReport = () => useAction(importsService.openReport);
