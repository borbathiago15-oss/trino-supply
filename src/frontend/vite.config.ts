import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

/**
 * O build gera o wwwroot inteiro do app .NET. Com o legado fora, o React é o
 * frontend: serve na raiz, e os estáticos da marca vêm de `public/`.
 */
const saida = fileURLToPath(new URL('../backend/Foundation/TrinoSupply.Foundation.Api/wwwroot', import.meta.url));

export default defineConfig({
  base: '/',
  plugins: [react()],
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
  build: { outDir: saida, emptyOutDir: true, sourcemap: false },
  server: {
    port: 5173,
    // em desenvolvimento o Vite serve o React e repassa API e assets ao .NET
    proxy: {
      '/api': process.env.API_URL ?? 'http://127.0.0.1:5099',
      '/assets': process.env.API_URL ?? 'http://127.0.0.1:5099',
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
    css: false,
  },
});
