import path from 'node:path';
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

// In development, calls to /api go to the .NET API, so the browser sees one origin and no CORS set-up is needed.
// Override the target with VITE_API_TARGET (e.g. http://localhost:5101).
const target = process.env.VITE_API_TARGET ?? 'https://localhost:7101';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': path.resolve(__dirname, './src') },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': { target, changeOrigin: true, secure: false },
    },
  },
});
