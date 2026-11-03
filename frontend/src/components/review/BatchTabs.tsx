import { useState } from 'react';
import type { BatchScope, BatchSummary } from '@/services/types';
import { SummaryTab } from '@/components/review/SummaryTab';
import { DataTab } from '@/components/review/DataTab';
import { ExceptionsTab } from '@/components/review/ExceptionsTab';
import { FilePreviewTab } from '@/components/review/FilePreviewTab';
import { Card, CardContent } from '@/components/ui/card';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';

type Tab = 'summary' | 'data' | 'exceptions' | 'file';

/** Summary / Transformed data / Exceptions / File preview: the same four tabs for review and for a committed batch. */
export function BatchTabs({ scope, batch }: { scope: BatchScope; batch: BatchSummary }) {
  const [tab, setTab] = useState<Tab>('summary');
  const counts = batch.counts!;
  return (
    <Card className="py-4">
      <CardContent className="px-4 sm:px-5">
        <Tabs value={tab} onValueChange={(v) => setTab(v as Tab)} className="gap-4">
          <TabsList aria-label="Batch sections" className="h-auto">
            <TabsTrigger value="summary">Summary</TabsTrigger>
            <TabsTrigger value="data">Transformed data</TabsTrigger>
            <TabsTrigger value="exceptions">Exceptions ({counts.errors + counts.warnings})</TabsTrigger>
            <TabsTrigger value="file">File preview</TabsTrigger>
          </TabsList>
          <TabsContent value="summary">
            <SummaryTab scope={scope} batch={batch} />
          </TabsContent>
          <TabsContent value="data">
            <DataTab scope={scope} batchId={batch.id} />
          </TabsContent>
          <TabsContent value="exceptions">
            <ExceptionsTab scope={scope} batchId={batch.id} />
          </TabsContent>
          <TabsContent value="file">
            <FilePreviewTab scope={scope} batchId={batch.id} />
          </TabsContent>
        </Tabs>
      </CardContent>
    </Card>
  );
}
