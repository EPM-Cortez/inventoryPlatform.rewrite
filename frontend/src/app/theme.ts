import { createContext, useContext } from 'react';

export type Theme = 'light' | 'dark';

export const THEME_STORAGE_KEY = 'inventory.theme';

export interface ThemeValue {
  theme: Theme;
  setTheme: (theme: Theme) => void;
  toggle: () => void;
}

export const ThemeContext = createContext<ThemeValue | null>(null);

export function useTheme(): ThemeValue {
  const value = useContext(ThemeContext);
  if (!value) throw new Error('useTheme must be used inside <ThemeProvider>.');
  return value;
}

/**
 * Reads the theme the inline script in `index.html` already applied, so React
 * hydrates with the same value the document is painted with. Falling back to a
 * fresh media query here would flash on first render.
 */
export function readInitialTheme(): Theme {
  const applied = document.documentElement.dataset.theme;
  return applied === 'dark' ? 'dark' : 'light';
}
