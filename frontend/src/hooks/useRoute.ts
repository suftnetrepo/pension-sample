import { useCallback, useEffect, useState } from 'react';

/**
 * Where the user is, kept in the address bar so a refresh or a shared link returns to the same place:
 *   (nothing)            new import
 *   ?batch=ID            a batch under review (or just closed)
 *   ?view=committed      the committed batches list
 *   ?committed=ID        one committed batch, read-only
 */
export type Route =
  | { page: 'import'; batchId: string | null }
  | { page: 'committed-list' }
  | { page: 'committed'; batchId: string };

function read(): Route {
  const q = new URLSearchParams(window.location.search);
  const committed = q.get('committed');
  if (committed) return { page: 'committed', batchId: committed };
  if (q.get('view') === 'committed') return { page: 'committed-list' };
  return { page: 'import', batchId: q.get('batch') };
}

function write(route: Route, replace = false) {
  const url = new URL(window.location.href);
  url.search = '';
  if (route.page === 'import' && route.batchId) url.searchParams.set('batch', route.batchId);
  if (route.page === 'committed-list') url.searchParams.set('view', 'committed');
  if (route.page === 'committed') url.searchParams.set('committed', route.batchId);
  if (replace) window.history.replaceState(null, '', url);
  else window.history.pushState(null, '', url);
}

export function useRoute() {
  const [route, setRoute] = useState<Route>(read);

  useEffect(() => {
    const onPop = () => setRoute(read());
    window.addEventListener('popstate', onPop);
    return () => window.removeEventListener('popstate', onPop);
  }, []);

  /** `replace` swaps the current history entry instead of adding one (used for redirects, so Back doesn't bounce). */
  const go = useCallback((next: Route, replace = false) => {
    write(next, replace);
    setRoute(next);
    window.scrollTo({ top: 0 });
  }, []);

  return [route, go] as const;
}
