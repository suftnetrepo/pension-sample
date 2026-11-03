import { Component, type ErrorInfo, type ReactNode } from 'react';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from '@/components/ui/card';

/** Catches unexpected rendering errors, so a bug shows a clear message instead of a blank page. */
export class ErrorBoundary extends Component<{ children: ReactNode }, { error: Error | null }> {
  state: { error: Error | null } = { error: null };

  static getDerivedStateFromError(error: Error) {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('Unexpected error in the import screens', error, info.componentStack);
  }

  render() {
    if (!this.state.error) return this.props.children;
    return (
      <main className="mx-auto max-w-[1200px] px-4 py-6 sm:px-8">
        <Card role="alert">
          <CardHeader>
            <CardTitle className="text-xl">Something went wrong on this screen</CardTitle>
            <CardDescription>Nothing has been saved. Reload the page to continue; a batch under review is kept for 24 hours.</CardDescription>
          </CardHeader>
          <CardContent />
          <CardFooter className="justify-end">
            <Button onClick={() => window.location.reload()}>Reload</Button>
          </CardFooter>
        </Card>
      </main>
    );
  }
}
