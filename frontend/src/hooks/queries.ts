import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { importsService } from '../services/importsService';
import type { BatchScope, RowFilter } from '../services/types';
import { queryKeys } from './queryKeys';

// Read hooks. A held batch never changes until it closes, and a committed batch never changes at all, so rows, exceptions
// and file previews are cached for good (staleTime: Infinity); switching tabs or pages back and forth costs no extra requests.

/** Sources and their agreed versions: they change only with a release. */
export function useSources() {
  return useQuery({ queryKey: queryKeys.sources, queryFn: ({ signal }) => importsService.sources(signal), staleTime: Infinity });
}

/** Committed batches, newest first. Keeps the previous page on screen while the next loads. */
export function useCommittedList(search: string, page: number, pageSize: number) {
  return useQuery({
    queryKey: queryKeys.committedList(search, page, pageSize),
    queryFn: ({ signal }) => importsService.committed(search, page, pageSize, signal),
    placeholderData: keepPreviousData,
  });
}

/** A held batch's summary: drives which screen is shown during an import. */
export function useBatch(id: string | null) {
  return useQuery({
    queryKey: queryKeys.batch(id ?? ''),
    queryFn: ({ signal }) => importsService.get('held', id!, signal),
    enabled: !!id,
  });
}

/** A committed batch's summary (read-only, never changes). */
export function useCommittedBatch(id: string) {
  return useQuery({
    queryKey: queryKeys.batch(id, 'committed'),
    queryFn: ({ signal }) => importsService.get('committed', id, signal),
    staleTime: Infinity,
  });
}

/** One page of transformed members. Keeps the previous page on screen while the next one loads. */
export function useBatchRows(scope: BatchScope, id: string, page: number, pageSize: number, search: string, show: RowFilter) {
  return useQuery({
    queryKey: queryKeys.rows(scope, id, page, pageSize, search, show),
    queryFn: ({ signal }) => importsService.rows(scope, id, page, pageSize, search, show, signal),
    placeholderData: keepPreviousData,
    staleTime: Infinity,
  });
}

export function useBatchExceptions(scope: BatchScope, id: string) {
  return useQuery({
    queryKey: queryKeys.exceptions(scope, id),
    queryFn: ({ signal }) => importsService.exceptions(scope, id, signal),
    staleTime: Infinity,
  });
}

export function useBatchCorrections(scope: BatchScope, id: string) {
  return useQuery({
    queryKey: queryKeys.corrections(scope, id),
    queryFn: ({ signal }) => importsService.corrections(scope, id, signal),
    staleTime: Infinity,
  });
}

export function useFilePreview(scope: BatchScope, id: string, lines = 50) {
  return useQuery({
    queryKey: queryKeys.filePreview(scope, id, lines),
    queryFn: ({ signal }) => importsService.filePreview(scope, id, lines, signal),
    staleTime: Infinity,
  });
}
