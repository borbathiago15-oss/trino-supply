import type { Config } from 'tailwindcss';

// Os nomes são semânticos (marca, ok, aviso, perigo) de propósito: a tela diz "isto é
// atenção", e a paleta decide a cor num lugar só. Trocar a paleta inteira foi editar
// este arquivo — nenhuma tela precisou saber que "aviso" deixou de ser amarelo-ouro,
// nem que a marca deixou de ser azul para ser o vermelho do Grupo Trino (2026-09).
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  // o tema é escolha da pessoa (classe `dark` no <html>), não do sistema operacional sozinho
  darkMode: 'class',
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
        // As superfícies e os textos das telas de trabalho são variáveis CSS (index.css):
        // é o que faz o tema escuro existir sem cada tela saber dele. Tela nova usa estes
        // nomes — `bg-white` e `text-slate-900` fixos não escurecem.
        texto: { DEFAULT: 'rgb(var(--texto) / <alpha-value>)', suave: 'rgb(var(--texto-suave) / <alpha-value>)' },
        borda: { DEFAULT: 'rgb(var(--borda) / <alpha-value>)', suave: 'rgb(var(--borda-suave) / <alpha-value>)' },
        // o fundo da aplicação (suave), o cartão em cima dele (DEFAULT) e o realce (forte)
        superficie: {
          DEFAULT: 'rgb(var(--superficie) / <alpha-value>)',
          suave: 'rgb(var(--superficie-suave) / <alpha-value>)',
          forte: 'rgb(var(--superficie-forte) / <alpha-value>)',
        },
        // conforme / no prazo: emerald — texto e tinta trocam com o tema, o `forte` (barras,
        // pontos) é o mesmo nos dois
        ok: { DEFAULT: 'rgb(var(--ok) / <alpha-value>)', fundo: 'rgb(var(--ok-fundo) / <alpha-value>)',
          borda: 'rgb(var(--ok-borda) / <alpha-value>)', forte: '#10b981' },
        // crítico / penalidade: rose — tinta clara com texto rose, para não se confundir com
        // o botão de ação, que é bordô sólido
        perigo: { DEFAULT: 'rgb(var(--perigo) / <alpha-value>)', fundo: 'rgb(var(--perigo-fundo) / <alpha-value>)',
          borda: 'rgb(var(--perigo-borda) / <alpha-value>)', forte: '#f43f5e' },
        // atenção / pendente: amber
        aviso: { DEFAULT: 'rgb(var(--aviso) / <alpha-value>)', fundo: 'rgb(var(--aviso-fundo) / <alpha-value>)',
          borda: 'rgb(var(--aviso-borda) / <alpha-value>)', forte: '#f59e0b' },
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
