import type { Config } from 'tailwindcss';

// Os nomes são semânticos (marca, ok, aviso, perigo) de propósito: a tela diz "isto é
// atenção", e a paleta decide a cor num lugar só. Trocar a paleta inteira foi editar
// este arquivo — nenhuma tela precisou saber que "aviso" deixou de ser amarelo-ouro,
// nem que a marca deixou de ser azul para ser o vermelho do Grupo Trino (2026-09).
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        // O fundo escuro é o preto da marca (o do site e das artes do Grupo Trino), não um
        // azul-marinho: o menu, o login, o portal e a parede da TV saem daqui.
        fundo: { DEFAULT: '#0b0d0f', card: '#161616', profundo: '#040607' },
        // ação: o vermelho da seta do logo do Grupo Trino (o bordô do site nos botões), e o
        // tom mais fechado no hover. O laranja, o azul e o verde são a faixa de quatro cores
        // da marca — apoio para gráfico, aviso e destaque, nunca a cor de ação.
        marca: { DEFAULT: '#9d202c', escuro: '#7e1a23', vermelho: '#bd1622',
          laranja: '#df8e24', azul: '#67a6dd', verde: '#a6bf38',
          navy: '#0b0d0f', navy2: '#161616', prata: '#a1aab5' },
        texto: { DEFAULT: '#0f172a', suave: '#64748b' },
        borda: '#e2e8f0',
        // o fundo da aplicação é slate-50; o cartão é branco em cima dele
        superficie: { DEFAULT: '#ffffff', suave: '#f8fafc' },
        // conforme / no prazo: emerald
        ok: { DEFAULT: '#047857', fundo: '#ecfdf5', borda: '#a7f3d0', forte: '#10b981' },
        // crítico / penalidade: rose — tinta clara com texto rose, para não se confundir com
        // o botão de ação, que é bordô sólido
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
