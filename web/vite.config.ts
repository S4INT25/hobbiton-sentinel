import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    proxy: {
      '/api': 'https://sentinel.bi.hobbiton.tech',
    },
  },
  build: {
    // served by Sentinel.Api in production (SPA fallback to index.html)
    outDir: '../src/Sentinel.Api/wwwroot',
    emptyOutDir: true,
  },
});
