// Mirrors backend/src/IntellipenImport.Application/Imports/Contracts.cs. Keep the two in step.

export type BatchStatus = 'ReadyForReview' | 'Committed' | 'Discarded' | 'Expired';

export interface SourceVersion {
  version: string;
  description: string;
  agreement: string;
  delimiter: string;
  dateFormat: string;
  columns: string[];
}

export interface Source {
  source: string;
  name: string;
  scheme: string;
  versions: SourceVersion[];
}

export interface Mapping {
  name: string;
  version: string;
  description: string;
}

export interface Counts {
  linesRead: number;
  accepted: number;
  rejected: number;
  errors: number;
  warnings: number;
  linesWithWarnings: number;
}

export interface ReconciliationLine {
  measure: string;
  isMoney: boolean;
  source: number;
  transformed: number;
  rejected: number;
  difference: number;
}

export interface RecordCheck {
  type: string;
  expected: number;
  actual: number;
  ok: boolean;
}

export interface CommitOutcome {
  membersSaved: number;
  recordCounts: Record<string, number>;
  rejectedLines: number;
  outputFileName: string;
  outputFileSha256: string;
  reportSent: boolean;
  reportDestination: string;
  reportError: string | null;
}

export interface BatchSummary {
  id: string;
  status: BatchStatus;
  source: string;
  sourceName: string;
  schemaVersion: string;
  fileName: string;
  fileSha256: string;
  mapping: Mapping | null;
  uploadedBy: string;
  uploadedAtUtc: string;
  expiresAtUtc: string;
  counts: Counts | null;
  reconciliation: ReconciliationLine[];
  recordChecks: RecordCheck[];
  trailerOk: boolean;
  balanced: boolean;
  canCommit: boolean;
  closedBy: string | null;
  closedAtUtc: string | null;
  commit: CommitOutcome | null;
}

export interface TargetRecord {
  type: string;
  fields: { name: string; value: string }[];
}

export interface MemberRow {
  lineNumber: number;
  key: string;
  accepted: boolean;
  hasWarnings: boolean;
  source: { column: string; value: string }[];
  records: TargetRecord[];
}

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
}

export interface Issue {
  severity: 'Error' | 'Warning';
  lineNumber: number;
  key: string;
  field: string;
  value: string;
  rule: string;
  action: string;
}

export interface FilePreview {
  fileName: string;
  totalLines: number;
  lines: string[];
}

/** A committed batch in the read-only list. */
export interface CommittedItem {
  id: string;
  source: string;
  sourceName: string;
  schemaVersion: string;
  fileName: string;
  committedBy: string;
  committedAtUtc: string;
  accepted: number;
  rejected: number;
  outputFileName: string;
}

/** What differs when a file doesn't match the agreed schema (sent with a 422). */
export interface SchemaMismatch {
  missing: string[];
  unexpected: string[];
  duplicated: string[];
  hint: string | null;
  matchesInstead: string | null;
}

export type RowFilter = 'accepted' | 'rejected' | 'warnings' | 'all';

/** Which store a batch is read from: held for review (/imports) or committed, read-only (/committed). */
export type BatchScope = 'held' | 'committed';
