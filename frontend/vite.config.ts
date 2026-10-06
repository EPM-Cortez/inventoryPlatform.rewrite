import { fileURLToPath, URL } from 'node:url';

import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { loadEnv } from 'vite';
// `vitest/config` re-exports `defineConfig` with the `test` key typed.
import { defineConfig } from 'vitest/config';

/**
 * The API issues `__Host-` prefixed, `SameSite=Strict` cookies for both the auth
 * session and the antiforgery token. Strict cookies are never sent on cross-site
 * requests, so a CORS setup cannot work: the browser must believe the API lives on
 * the same origin as the app. In development that is what this proxy is for, and in
 * production the built assets must be served from the same origin as `/api`.
 *
 * Do not "fix" a login loop by adding CORS headers — check the proxy first.
 */
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '');
  const apiTarget = env.VITE_API_PROXY_TARGET ?? 'https://localhost:7270';

  const proxy = {
    target: apiTarget,
    changeOrigin: false,
    // The .NET development certificate is self-signed.
    secure: false,
  };

  return {
    plugins: [react(), tailwindcss()],
    css: {
      // Declared inline so Vite cannot pick up an unrelated parent PostCSS
      // configuration that conflicts with this project's Tailwind 4 stylesheet.
      // `@tailwindcss/vite` handles the CSS itself; no PostCSS plugins needed.
      postcss: { plugins: [] },
    },
    resolve: {
      alias: {
        '@': fileURLToPath(new URL('./src', import.meta.url)),
      },
    },
    server: {
      port: 5173,
      strictPort: true,
      proxy: {
        '/api': proxy,
        '/health': proxy,
        '/openapi': proxy,
      },
    },
    build: {
      target: 'es2022',
      sourcemap: true,
      rollupOptions: {
        output: {
          // Framework code changes far less often than feature code, so give it
          // its own long-lived chunk rather than invalidating one big bundle on
          // every release.
          manualChunks: (id) => {
            if (!id.includes('node_modules')) return undefined;
            if (/[\\/]node_modules[\\/](react|react-dom|react-router|scheduler)[\\/]/.test(id)) {
              return 'react';
            }
            if (id.includes('@tanstack')) return 'query';
            return undefined;
          },
        },
      },
    },
    test: {
      environment: 'jsdom',
      globals: true,
      setupFiles: ['./src/test/setup.ts'],
      include: ['src/**/*.{test,spec}.{ts,tsx}'],
      css: false,
    },
  };
});
