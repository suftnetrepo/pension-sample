import type { ReactNode } from 'react';
import { AlertCircle, Check, Loader2 } from 'lucide-react';
import type { ApiError } from '@/services/http';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Progress } from '@/components/ui/progress';
import { cn } from '@/lib/utils';

/** A dismissible error message. `onRetry` shows a Try again button (for transient failures). */
export function ErrorBanner({ error, onClose, onRetry }: { error: ApiError; onClose?: () => void; onRetry?: () => void }) {
  return (
    <Alert variant="destructive">
      <AlertCircle />
      <AlertTitle>{error.title}</AlertTitle>
      <AlertDescription>
        <p>{error.message}</p>
        {(onRetry || onClose) && (
          <div className="mt-2 flex gap-2">
            {onRetry && (
              <Button variant="outline" size="sm" onClick={onRetry}>
                Try again
              </Button>
            )}
            {onClose && (
              <Button variant="link" size="sm" className="text-destructive" onClick={onClose} aria-label="Dismiss message">
                Dismiss
              </Button>
            )}
          </div>
        )}
      </AlertDescription>
    </Alert>
  );
}

/** A small inline error under the button that caused it. */
export function InlineError({ error }: { error: ApiError | null }) {
  if (!error) return null;
  return (
    <p className="text-destructive text-xs" role="alert">
      {error.title}: {error.message}
    </p>
  );
}

export function Spinner({ label }: { label: string }) {
  return (
    <div className="text-muted-foreground flex items-center gap-2 py-4" role="status" aria-live="polite">
      <Loader2 className="size-4 animate-spin motion-reduce:animate-none" aria-hidden="true" />
      {label}
    </div>
  );
}

/**
 * Renders loading, error or the content for one query, so every tab handles the three states the same way.
 */
export function QueryState<T>(props: {
  query: { data: T | undefined; isPending: boolean; error: ApiError | null; refetch: () => unknown };
  loading: string;
  children: (data: T) => ReactNode;
}) {
  const { query } = props;
  if (query.error) return <ErrorBanner error={query.error} onRetry={query.error.isTransient ? () => query.refetch() : undefined} />;
  if (query.isPending || query.data === undefined) return <Spinner label={props.loading} />;
  return <>{props.children(query.data)}</>;
}

/** Determinate (0–100) or indeterminate (null) progress. */
export function ProgressBar({ percent, label }: { percent: number | null; label: string }) {
  return <Progress value={percent} aria-label={label} className="my-3" />;
}

const steps = ['Upload', 'Review', 'Commit'];

/** Upload → Review → Commit. `current` is 0-based; `done` ticks the last step once committed. */
export function StepIndicator({ current, done }: { current: number; done: boolean }) {
  return (
    <nav aria-label="Progress">
      <ol className="flex flex-wrap gap-2">
        {steps.map((label, i) => {
          const state = i < current || (done && i === current) ? 'done' : i === current ? 'active' : 'todo';
          return (
            <li
              key={label}
              aria-current={state === 'active' ? 'step' : undefined}
              className={cn(
                'flex items-center gap-2 rounded-full px-3.5 py-2 text-[13px] font-medium',
                state === 'active' && 'bg-primary text-primary-foreground',
                state === 'done' && 'bg-success-soft text-success',
                state === 'todo' && 'bg-card text-muted-foreground border',
              )}
            >
              <span
                className={cn(
                  'flex size-[22px] items-center justify-center rounded-full text-xs',
                  state === 'active' && 'text-primary bg-white',
                  state === 'done' && 'bg-success text-white',
                  state === 'todo' && 'bg-muted',
                )}
              >
                {state === 'done' ? <Check className="size-3.5" aria-hidden="true" /> : i + 1}
              </span>
              {label}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}

/** Status badge: OK / Warning / Error tones on top of shadcn's Badge. */
export function Pill({ tone, children }: { tone: 'ok' | 'warn' | 'error'; children: ReactNode }) {
  const variant = tone === 'ok' ? 'success' : tone === 'warn' ? 'warning' : 'destructive';
  return <Badge variant={variant}>{children}</Badge>;
}
