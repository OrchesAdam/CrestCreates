import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../ErpManagement.Web/wwwroot',
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'https://localhost:5001', secure: false, changeOrigin: true },
      '/connect': { target: 'https://localhost:5001', secure: false, changeOrigin: true },
      '/health': { target: 'https://localhost:5001', secure: false, changeOrigin: true },
      '/scalar': { target: 'https://localhost:5001', secure: false, changeOrigin: true },
      '/openapi': { target: 'https://localhost:5001', secure: false, changeOrigin: true },
    },
  },
});