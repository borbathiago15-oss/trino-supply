/**
 * Os hexágonos de contorno neon do Grupo Trino — vermelho, azul, laranja e verde, com brilho,
 * espalhados sobre o preto —, a assinatura visual das artes da empresa. É decoração das
 * superfícies escuras (login, cockpit, pé do menu), desenhada em SVG e não em imagem: escala
 * a qualquer tela, pesa nada e as cores saem da mesma faixa da marca.
 *
 * Cada hexágono é um SVG quadrado próprio, posicionado em percentual do contêiner e com
 * `aspect-ratio: 1`: um SVG só esticado sobre a área inteira deformava o hexágono conforme a
 * proporção da tela. Fica atrás de tudo e fora do fluxo (`aria-hidden`, `pointer-events-none`):
 * quem lê a tela não o percebe como conteúdo, e o leitor de tela não o anuncia.
 */
const CORES = ['#9d202c', '#67a6dd', '#df8e24', '#a6bf38'] as const;

/** Hexágono de ponta para cima, centrado em (50, 50) num quadro de 100. */
const PONTOS = Array.from({ length: 6 }, (_, i) => {
  const a = (Math.PI / 180) * (60 * i - 90);
  return `${(50 + 40 * Math.cos(a)).toFixed(1)},${(50 + 40 * Math.sin(a)).toFixed(1)}`;
}).join(' ');

interface Hexagono { x: number; y: number; largura: number; cor: number; rot: number }

/**
 * Os arranjos, em percentual da largura (x, largura) e da altura (y) do contêiner. O do login
 * deixa livre o canto superior esquerdo, onde ficam o logo e o texto; o da parede da TV fica
 * nas bordas, porque o meio é área de leitura; o do menu é uma fileira curta.
 */
const ARRANJOS: Record<'fundo' | 'cantos' | 'rodape', Hexagono[]> = {
  fundo: [
    { x: 74, y: 4, largura: 13, cor: 1, rot: 6 },
    { x: 90, y: 28, largura: 11, cor: 3, rot: -8 },
    { x: 62, y: 46, largura: 9, cor: 2, rot: 10 },
    { x: 84, y: 66, largura: 14, cor: 0, rot: -5 },
    { x: 48, y: 82, largura: 11, cor: 1, rot: 7 },
    { x: 4, y: 84, largura: 12, cor: 2, rot: -6 },
    { x: -3, y: 52, largura: 9, cor: 3, rot: 4 },
  ],
  cantos: [
    { x: -2, y: -6, largura: 8, cor: 1, rot: 6 },
    { x: 93, y: -5, largura: 8, cor: 2, rot: -8 },
    { x: -3, y: 82, largura: 9, cor: 0, rot: 5 },
    { x: 92, y: 80, largura: 9, cor: 3, rot: -6 },
  ],
  rodape: [
    { x: 2, y: 8, largura: 24, cor: 0, rot: 8 },
    { x: 36, y: 22, largura: 20, cor: 1, rot: -6 },
    { x: 68, y: 4, largura: 26, cor: 3, rot: 5 },
  ],
};

export function HexagonosDaMarca({ variante = 'fundo', opacidade = 1, className = '' }: {
  variante?: keyof typeof ARRANJOS;
  /** A parede da TV é área de leitura: lá os hexágonos ficam mais discretos. */
  opacidade?: number;
  /** Para esconder onde não cabem — no celular o painel do login é curto e o texto ocupa tudo. */
  className?: string;
}) {
  return (
    <div aria-hidden="true" data-testid="hexagonos-da-marca"
      className={`pointer-events-none absolute inset-0 overflow-hidden ${className}`} style={{ opacity: opacidade }}>
      {ARRANJOS[variante].map((h, i) => (
        <svg key={i} viewBox="0 0 100 100" className="absolute"
          style={{ left: `${h.x}%`, top: `${h.y}%`, width: `${h.largura}%`, aspectRatio: '1 / 1', transform: `rotate(${h.rot}deg)` }}>
          <g fill="none" stroke={CORES[h.cor]} strokeLinejoin="round">
            {/* o brilho: o mesmo traço, mais grosso e translúcido, por baixo — é o "neon" das artes */}
            <polygon points={PONTOS} strokeWidth="4" opacity="0.28" />
            <polygon points={PONTOS} strokeWidth="2.2" opacity="0.5" />
            <polygon points={PONTOS} strokeWidth="1.1" />
          </g>
        </svg>
      ))}
    </div>
  );
}
