import { useBatch } from "@/hooks/queries";
import { useRoute } from "@/hooks/useRoute";
import {
  ErrorBanner,
  Spinner,
  StepIndicator,
} from "@/components/common/Feedback";
import { Button } from "@/components/ui/button";
import { UploadPage } from "@/pages/UploadPage";
import { ReviewPage } from "@/pages/ReviewPage";
import { ClosedPage } from "@/pages/ClosedPage";
import { CommittedListPage } from "@/pages/CommittedListPage";
import { CommittedBatchPage } from "@/pages/CommittedBatchPage";
import { cn } from "@/lib/utils";

/** The screen is decided by the address bar: a new import, a batch under review, or committed batches (read-only). */
export default function App() {
  const [route, go] = useRoute();
  const importBatchId = route.page === "import" ? route.batchId : null;
  const batch = useBatch(importBatchId);

  const newImport = () => go({ page: "import", batchId: null });
  const viewCommitted = (id: string) => go({ page: "committed", batchId: id });
  const allCommitted = () => go({ page: "committed-list" });

  const status = importBatchId ? batch.data?.status : undefined;
  const current = status === "ReadyForReview" ? 1 : status ? 2 : 0;
  const inCommitted = route.page !== "import";

  return (
    <div className="min-h-screen">
      <header className="bg-card border-b">
        <div className="mx-auto flex max-w-[1200px] flex-wrap items-center justify-between gap-3 px-4 py-3.5 sm:px-8">
          <div className="text-primary flex items-center gap-3">
            <svg
              width="28"
              height="28"
              viewBox="0 0 28 28"
              fill="none"
              aria-hidden="true"
            >
              <rect
                x="1"
                y="1"
                width="26"
                height="26"
                rx="6"
                stroke="currentColor"
                strokeWidth="2"
              />
              <path
                d="M8 18l6-8 6 8"
                stroke="currentColor"
                strokeWidth="2"
                strokeLinecap="round"
                strokeLinejoin="round"
              />
            </svg>
            <div>
              <div className="text-foreground text-base font-semibold">
                IntelliPen Import
              </div>
              <div className="text-muted-foreground text-xs">
                Member data upload · DB
              </div>
            </div>
          </div>
          <nav aria-label="Main" className="flex gap-1">
            <Button
              variant="ghost"
              className={cn(!inCommitted && "bg-accent text-accent-foreground")}
              aria-current={!inCommitted ? "page" : undefined}
              onClick={newImport}
            >
              New import
            </Button>
            <Button
              variant="ghost"
              className={cn(inCommitted && "bg-accent text-accent-foreground")}
              aria-current={inCommitted ? "page" : undefined}
              onClick={allCommitted}
            >
              Committed batches
            </Button>
          </nav>
        </div>
      </header>

      <main className="mx-auto grid max-w-[1200px] grid-cols-1 gap-5 px-4 pt-6 pb-12 sm:px-8">
        {route.page === "committed-list" && (
          <CommittedListPage onView={viewCommitted} />
        )}
        {route.page === "committed" && (
          <CommittedBatchPage
            key={route.batchId}
            batchId={route.batchId}
            onBack={allCommitted}
          />
        )}

        {route.page === "import" && (
          <>
            <StepIndicator current={current} done={status === "Committed"} />

            {!importBatchId && (
              <UploadPage
                onUploaded={(b) => go({ page: "import", batchId: b.id })}
                onViewCommitted={viewCommitted}
                onAllCommitted={allCommitted}
              />
            )}

            {importBatchId && batch.isPending && (
              <Spinner label="Loading the batch…" />
            )}

            {importBatchId && batch.error && (
              <div className="grid grid-cols-1 gap-4">
                <ErrorBanner
                  error={batch.error}
                  onRetry={
                    batch.error.isTransient ? () => batch.refetch() : undefined
                  }
                />
                <div className="flex justify-end">
                  <Button onClick={newImport}>Start a new import</Button>
                </div>
              </div>
            )}

            {batch.data && importBatchId && status === "ReadyForReview" && (
              <ReviewPage
                key={importBatchId}
                batch={batch.data}
                onDiscarded={newImport}
              />
            )}
            {batch.data &&
              importBatchId &&
              status &&
              status !== "ReadyForReview" && (
                <ClosedPage
                  batch={batch.data}
                  onNew={newImport}
                  onViewCommitted={viewCommitted}
                />
              )}
          </>
        )}
      </main>
    </div>
  );
}
