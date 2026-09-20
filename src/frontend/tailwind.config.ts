import type { Config } from 'tailwindcss';

// Os nomes são semânticos (marca, ok, aviso, perigo) de propósito: a tela diz "isto é
// atenção", e a paleta decide a cor num lugar só. Trocar a paleta inteira foi editar
// este arquivo — nenhuma tela precisou saber que "aviso" deixou de ser amarelo-ouro.
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        fundo: '#0f172a',
        // ação: blue-600, e blue-700 no hover
        marca: { DEFAULT: '#2563eb', escuro: '#1d4ed8', navy: '#031430', navy2: '#0a2450',
          vermelho: '#c20007', prata: '#a1aab5' },
        texto: { DEFAULT: '#0f172a', suave: '#64748b' },
        borda: '#e2e8f0',
        // o fundo da aplicação é slate-50; o cartão é branco em cima dele
        superficie: { DEFAULT: '#ffffff', suave: '#f8fafc' },
        // conforme / no prazo: emerald
        ok: { DEFAULT: '#047857', fundo: '#ecfdf5', borda: '#a7f3d0', forte: '#10b981' },
        // crítico / penalidade: rose
        perigo: { DEFAULT: '#be123c', fundo: '#fff1f2', borda: '#fecdd3', forte: '#f43f5e' },
        // atenção / pendente: amber
        aviso: { DEFAULT: '#b45309', fundo: '#fffbeb', borda: '#fde68a', forte: '#f59e0b' },
      },
      borderRadius: { painel: '12px' },
      boxShadow: {
        tooltip: '0 20px 25px -5px rgb(0 0 0 / 0.1), 0 8px 10px -6px rgb(0 0 0 / 0.1)',
      },
      keyframes: {
        entrada: { from: { opacity: '0', transform: 'translateY(8px)' }, to: { opacity: '1', transform: 'none' } },
      },
      animation: { entrada: 'entrada .45s ease both' },
      fontFamily: { sans: ['Inter', 'system-ui', 'Segoe UI', 'Roboto', 'sans-serif'] },
    },
  },
  plugins: [],
} satisfies Config;
