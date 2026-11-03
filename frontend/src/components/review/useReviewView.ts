import { useState } from 'react';

export type ReviewTab = 'summary' | 'data' | 'exceptions' | 'file';
export type ExceptionFilter = 'all' | 'Error' | 'Warning' | 'unchecked';

/** Which tab and which exceptions filter are showing. Lifted so the commit bar can open "warnings not yet checked". */
export function useReviewView() {
  const [tab, setTab] = useState<ReviewTab>('summary');
  const [filter, setFilter] = useState<ExceptionFilter>('all');
  return { tab, setTab, filter, setFilter, showUnchecked: () => { setFilter('unchecked'); setTab('exceptions'); } };
}

export type ReviewView = ReturnType<typeof useReviewView>;
