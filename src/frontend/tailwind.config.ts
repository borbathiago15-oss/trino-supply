import type { Config } from 'tailwindcss';

// Mesmas cores do legado (index.html :root) para o React não destoar do resto.
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        fundo: '#0f172a',
        marca: { DEFAULT: '#1d4ed8', escuro: '#1e40af', navy: '#031430', navy2: '#0a2450',
          vermelho: '#c20007', prata: '#a1aab5' },
        texto: { DEFAULT: '#0f172a', suave: '#64748b' },
        borda: '#e2e8f0',
        superficie: { DEFAULT: '#ffffff', suave: '#f1f5f9' },
        ok: { DEFAULT: '#15803d', fundo: '#f0fdf4' },
        perigo: { DEFAULT: '#b91c1c', fundo: '#fef2f2' },
        aviso: { DEFAULT: '#854d0e', fundo: '#fef9c3' },
      },
      borderRadius: { painel: '12px' },
      keyframes: {
        entrada: { from: { opacity: '0', transform: 'translateY(8px)' }, to: { opacity: '1', transform: 'none' } },
      },
      animation: { entrada: 'entrada .45s ease both' },
      fontFamily: { sans: ['Inter', 'system-ui', 'Segoe UI', 'Roboto', 'sans-serif'] },
    },
  },
  plugins: [],
} satisfies Config;
