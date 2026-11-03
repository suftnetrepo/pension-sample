import type { BatchScope, RowFilter } from '../services/types';

/**
 * Every cache key in one place, so invalidation is consistent.
 * Held batch keys start with ['batch', id]; committed ones with ['committed', id] (they never change, so they are never invalidated).
 */
const root = (scope: BatchScope) => (scope === 'held' ? 'batch' : 'committed');

export const queryKeys = {
  sources: ['sources'] as const,
  committedList: (search: string, page: number, pageSize: number) => ['committed-list', search, page, pageSize] as const,
  committedListAll: ['committed-list'] as const,
  batch: (id: string, scope: BatchScope = 'held') => [root(scope), id] as const,
  rows: (scope: BatchScope, id: string, page: number, pageSize: number, search: string, show: RowFilter) =>
    [root(scope), id, 'rows', page, pageSize, search, show] as const,
  exceptions: (scope: BatchScope, id: string) => [root(scope), id, 'exceptions'] as const,
  corrections: (scope: BatchScope, id: string) => [root(scope), id, 'corrections'] as const,
  filePreview: (scope: BatchScope, id: string, lines: number) => [root(scope), id, 'file', lines] as const,
};
