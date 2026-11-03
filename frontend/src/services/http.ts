import type { SchemaMismatch } from './types';

/** Base path of the API. In development Vite proxies it to the .NET API (see vite.config.ts). */
export const apiBase = '/api/v1';

export type ErrorKind = 'http' | 'network' | 'cancelled';

/**
 * Every failure the UI can show. `message` is always plain English, ready for the screen:
 * the API's problem-details "detail" for HTTP errors, a fixed sentence for network problems.
 */
export class ApiError extends Error {
  constructor(
    public readonly kind: ErrorKind,
    public readonly status: number,
    public readonly title: string,
    message: string,
    public readonly mismatch: SchemaMismatch | null = null,
  ) {
    super(message);
  }

  get isGone() {
    return this.status === 410;
  }

  /** Worth retrying automatically: the network or the server, never a 4xx answer. */
  get isTransient() {
    return this.kind === 'network' || this.status >= 500;
  }

  static network() {
    return new ApiError('network', 0, 'Cannot reach the server', 'Check your connection and that the service is running, then try again.');
  }

  static cancelled() {
    return new ApiError('cancelled', 0, 'Cancelled', 'The upload was cancelled.');
  }

  static from(e: unknown): ApiError {
    if (e instanceof ApiError) return e;
    return new ApiError('http', 0, 'Unexpected error', e instanceof Error ? e.message : String(e));
  }
}

/**
 * Where a sign-in token would be added. In development the API signs requests in as a development user.
 * In production, call setTokenProvider with a function returning an Entra ID access token (e.g. from MSAL).
 */
let tokenProvider: (() => Promise<string | null>) | null = null;
export function setTokenProvider(provider: () => Promise<string | null>) {
  tokenProvider = provider;
}

async function authHeaders(): Promise<Record<string, string>> {
  const token = tokenProvider ? await tokenProvider() : null;
  return token ? { Authorization: `Bearer ${token}` } : {};
}

async function problemFrom(status: number, statusText: string, body: string): Promise<ApiError> {
  try {
    const p = JSON.parse(body);
    return new ApiError('http', status, p.title ?? statusText, p.detail ?? `The request failed (${status}).`, p.mismatch ?? null);
  } catch {
    return new ApiError('http', status, statusText || 'Error', `The request failed (${status}).`);
  }
}

/** fetch with sign-in, problem-details errors and network errors turned into ApiError. */
export async function request(path: string, init: RequestInit = {}): Promise<Response> {
  let response: Response;
  try {
    response = await fetch(apiBase + path, { ...init, headers: { ...(await authHeaders()), ...init.headers } });
  } catch (e) {
    if (e instanceof DOMException && e.name === 'AbortError') throw ApiError.cancelled();
    throw ApiError.network();
  }
  if (!response.ok) throw await problemFrom(response.status, response.statusText, await response.text());
  return response;
}

export async function getJson<T>(path: string, signal?: AbortSignal): Promise<T> {
  return (await request(path, { signal })).json() as Promise<T>;
}

export async function sendJson<T>(path: string, method: 'POST' | 'PUT', body: unknown): Promise<T> {
  const response = await request(path, { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
  return response.json() as Promise<T>;
}

export interface UploadHandle<T> {
  promise: Promise<T>;
  cancel: () => void;
}

/**
 * Multipart upload with progress. fetch can't report upload progress, so this one call uses XMLHttpRequest.
 * `onProgress` receives 0–100 while the file is sent; after 100 the server is processing.
 */
export function uploadWithProgress<T>(path: string, form: FormData, onProgress: (percent: number) => void): UploadHandle<T> {
  const xhr = new XMLHttpRequest();
  const promise = new Promise<T>((resolve, reject) => {
    authHeaders().then((headers) => {
      xhr.open('POST', apiBase + path);
      Object.entries(headers).forEach(([k, v]) => xhr.setRequestHeader(k, v));
      xhr.upload.onprogress = (e) => e.lengthComputable && onProgress(Math.round((e.loaded / e.total) * 100));
      xhr.onload = async () => {
        if (xhr.status >= 200 && xhr.status < 300) resolve(JSON.parse(xhr.responseText) as T);
        else reject(await problemFrom(xhr.status, xhr.statusText, xhr.responseText));
      };
      xhr.onerror = () => reject(ApiError.network());
      xhr.onabort = () => reject(ApiError.cancelled());
      xhr.send(form);
    }, reject);
  });
  return { promise, cancel: () => xhr.abort() };
}

/** Downloads through fetch (so the sign-in token is sent) and saves with the server's file name. */
export async function downloadFile(path: string, fallbackName: string): Promise<void> {
  const response = await request(path);
  const disposition = response.headers.get('Content-Disposition') ?? '';
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
  const url = URL.createObjectURL(await response.blob());
  const a = document.createElement('a');
  a.href = url;
  a.download = match ? decodeURIComponent(match[1]) : fallbackName;
  a.click();
  URL.revokeObjectURL(url);
}

/** Fetches an HTML page (the report) and opens it in a new tab. */
export async function openHtml(path: string): Promise<void> {
  const html = await (await request(path)).text();
  const url = URL.createObjectURL(new Blob([html], { type: 'text/html' }));
  window.open(url, '_blank', 'noopener');
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}
