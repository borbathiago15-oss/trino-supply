import type { Config } from 'tailwindcss';

// Mesmas cores do legado (index.html :root) para o React não destoar do resto.
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        fundo: '#0f172a',
        marca: { DEFAULT: '#1d4ed8', escuro: '#1e40af', navy: '#031430' },
        texto: { DEFAULT: '#0f172a', suave: '#64748b' },
        borda: '#e2e8f0',
        superficie: { DEFAULT: '#ffffff', suave: '#f1f5f9' },
        ok: { DEFAULT: '#15803d', fundo: '#f0fdf4' },
        perigo: { DEFAULT: '#b91c1c', fundo: '#fef2f2' },
        aviso: { DEFAULT: '#854d0e', fundo: '#fef9c3' },
      },
      borderRadius: { painel: '12px' },
      fontFamily: { sans: ['Inter', 'system-ui', 'Segoe UI', 'Roboto', 'sans-serif'] },
    },
  },
  plugins: [],
} satisfies Config;
