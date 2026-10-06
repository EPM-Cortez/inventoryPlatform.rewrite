import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useState, type ReactNode } from 'react';

import { ApiError } from '@/api/problem';
import { SessionProvider } from '@/auth/SessionProvider';
import { ToastProvider } from '@/ui/Toast';
import { TooltipProvider } from '@/ui/Tooltip';

import { ThemeProvider } from './ThemeProvider';

export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        // The register changes slowly relative to how fast people navigate it.
        staleTime: 30_000,
        gcTime: 5 * 60_000,
        refetchOnWindowFocus: false,
        retry: (failureCount, error) => {
          // Retrying a 401/403/404/409 cannot succeed and only delays the error
          // the user needs to see. Retry genuine transport failures only.
          if (error instanceof ApiError) {
            return error.status >= 500 && failureCount < 2;
          }
          return failureCount < 2;
        },
      },
      mutations: {
        // A mutation is never safe to replay blindly: assign/transfer and the
        // versioned writes are not idempotent.
        retry: false,
      },
    },
  });
}

export function AppProviders({ children }: { children: ReactNode }) {
  const [queryClient] = useState(createQueryClient);

  return (
    <QueryClientProvider client={queryClient}>
      <ThemeProvider>
        <TooltipProvider>
          <ToastProvider>
            <SessionProvider>{children}</SessionProvider>
          </ToastProvider>
        </TooltipProvider>
      </ThemeProvider>
    </QueryClientProvider>
  );
}
