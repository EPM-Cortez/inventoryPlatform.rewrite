import { useCallback, useMemo, useState, type ReactNode } from 'react';

import {
  readInitialTheme,
  THEME_STORAGE_KEY,
  ThemeContext,
  type Theme,
  type ThemeValue,
} from './theme';

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<Theme>(readInitialTheme);

  const setTheme = useCallback((next: Theme) => {
    setThemeState(next);
    // Both attributes matter: `data-theme` drives our custom properties, and
    // `color-scheme` tells the browser which native form controls and scrollbars
    // to paint.
    document.documentElement.dataset.theme = next;
    document.documentElement.style.colorScheme = next;
    try {
      localStorage.setItem(THEME_STORAGE_KEY, next);
    } catch {
      // Private browsing can block storage. The theme still applies for this session.
    }
  }, []);

  const value = useMemo<ThemeValue>(
    () => ({
      theme,
      setTheme,
      toggle: () => setTheme(theme === 'dark' ? 'light' : 'dark'),
    }),
    [setTheme, theme],
  );

  return <ThemeContext value={value}>{children}</ThemeContext>;
}
