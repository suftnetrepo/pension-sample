import { QueryCache, QueryClient } from '@tanstack/react-query';
import { ApiError } from '../services/http';
import { queryKeys } from '../hooks/queryKeys';

// Every service call throws ApiError (see services/http.ts), so queries and mutations are typed with it.
declare module '@tanstack/react-query' {
  interface Register {
    defaultError: ApiError;
  }
}

/**
 * One QueryClient for the app.
 * - Retries only transient failures (network, 5xx), twice, never a 4xx answer.
 * - A 410 "gone" from any batch query means the batch closed elsewhere (expired, or committed or discarded in another tab):
 *   the batch summary is refreshed, and the app moves to the right screen.
 */
export function createQueryClient() {
  const client: QueryClient = new QueryClient({
    queryCache: new QueryCache({
      onError: (error, query) => {
        const batchId = query.queryKey[0] === 'batch' ? (query.queryKey[1] as string) : null;
        if (batchId && ApiError.from(error).isGone && query.queryKey.length > 2) {
          void client.invalidateQueries({ queryKey: queryKeys.batch(batchId), exact: true });
        }
      },
    }),
    defaultOptions: {
      queries: {
        retry: (failures, error) => ApiError.from(error).isTransient && failures < 2,
        refetchOnWindowFocus: false,
        staleTime: 30_000,
      },
      mutations: { retry: false },
    },
  });
  return client;
}
