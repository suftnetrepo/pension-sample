import { downloadFile, getJson, openHtml, request, sendJson, uploadWithProgress, type UploadHandle } from './http';
import type { BatchScope, BatchSummary, CommittedItem, Correction, FilePreview, Issue, Me, MemberRow, Paged, RowFilter, Source } from './types';

/** Held batches live under /imports (review, commit, discard); committed ones under /committed (GET only). */
const base = (scope: BatchScope, id: string) => `${scope === 'held' ? '/imports' : '/committed'}/${encodeURIComponent(id)}`;

/** Every call the UI makes to the API. Components never call this directly: they use the hooks in src/hooks. */
export const importsService = {
  me: (signal?: AbortSignal) => getJson<Me>('/me', signal),

  sources: (signal?: AbortSignal) => getJson<Source[]>('/sources', signal),

  upload: (file: File, source: string, version: string, onProgress: (percent: number) => void): UploadHandle<BatchSummary> => {
    const form = new FormData();
    form.append('file', file);
    form.append('source', source);
    form.append('version', version);
    return uploadWithProgress<BatchSummary>('/imports', form, onProgress);
  },

  // ---- Read: the same calls for a held or a committed batch ----

  get: (scope: BatchScope, id: string, signal?: AbortSignal) => getJson<BatchSummary>(base(scope, id), signal),

  rows: (scope: BatchScope, id: string, page: number, pageSize: number, search: string, show: RowFilter, signal?: AbortSignal) =>
    getJson<Paged<MemberRow>>(
      `${base(scope, id)}/rows?` + new URLSearchParams({ page: String(page), pageSize: String(pageSize), search, show }),
      signal,
    ),

  exceptions: (scope: BatchScope, id: string, signal?: AbortSignal) => getJson<Issue[]>(`${base(scope, id)}/exceptions`, signal),

  /** One member's real values. Needs the Import.RevealSensitive role; audited by the server. */
  reveal: (scope: BatchScope, id: string, lineNumber: number) => getJson<MemberRow>(`${base(scope, id)}/rows/${lineNumber}/reveal`),

  corrections: (scope: BatchScope, id: string, signal?: AbortSignal) => getJson<Correction[]>(`${base(scope, id)}/corrections`, signal),

  filePreview: (scope: BatchScope, id: string, lines: number, signal?: AbortSignal) =>
    getJson<FilePreview>(`${base(scope, id)}/file?lines=${lines}`, signal),

  downloadExceptions: (scope: BatchScope, id: string) => downloadFile(`${base(scope, id)}/exceptions.csv`, `${id}_exceptions.csv`),

  downloadCorrections: (scope: BatchScope, id: string) => downloadFile(`${base(scope, id)}/corrections.csv`, `${id}_corrections.csv`),

  downloadFile: (scope: BatchScope, id: string) => downloadFile(`${base(scope, id)}/file/download`, `${id}.csv`),

  downloadReport: (scope: BatchScope, id: string) =>
    downloadFile(`${base(scope, id)}/report?download=true`, `${id}_reconciliation-report.html`),

  openReport: (scope: BatchScope, id: string) => openHtml(`${base(scope, id)}/report`),

  // ---- Committed batches (read-only) ----

  committed: (search: string, page: number, pageSize: number, signal?: AbortSignal) =>
    getJson<Paged<CommittedItem>>('/committed?' + new URLSearchParams({ search, page: String(page), pageSize: String(pageSize) }), signal),

  // ---- Change a held batch ----

  commit: (id: string) => sendJson<BatchSummary>(`${base('held', id)}/commit`, 'POST', { reviewed: true }),

  /** Mark warnings as checked by the signed-in user. Returns the updated summary. */
  acknowledge: (id: string, issueIds: number[], note: string) =>
    sendJson<BatchSummary>(`${base('held', id)}/acknowledgements`, 'POST', { issueIds, note: note.trim() || undefined }),

  /** Take back one check. Returns the updated summary. */
  unacknowledge: (id: string, issueId: number) => sendJson<BatchSummary>(`${base('held', id)}/acknowledgements/${issueId}`, 'DELETE'),

  discard: async (id: string) => {
    await request(base('held', id), { method: 'DELETE' });
  },
};
