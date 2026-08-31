import type { LeituraFarol } from '@trino/contratos';

const CLASSES: Record<LeituraFarol['cor'], { ponto: string; texto: string }> = {
  VERDE: { ponto: 'bg-farol-verde', texto: 'text-green-700' },
  AMARELO: { ponto: 'bg-farol-amarelo', texto: 'text-yellow-700' },
  VERMELHO: { ponto: 'bg-farol-vermelho', texto: 'text-red-700' },
  PRETO: { ponto: 'bg-farol-preto', texto: 'text-slate-900 font-semibold' },
  NEUTRO: { ponto: 'bg-farol-neutro', texto: 'text-slate-400' },
};

const DESCRICAO: Record<LeituraFarol['cor'], string> = {
  VERDE: 'Dentro do prazo',
  AMARELO: 'Atenção: passou da metade do prazo',
  VERMELHO: 'Crítico: prazo quase esgotado',
  PRETO: 'SLA estourado',
  NEUTRO: 'Sem SLA em contagem',
};

/**
 * Farol de SLA: cor + tempo ÚTIL restante. Quando o cronômetro está congelado
 * (a bola não está com compras), o farol diz isso — senão o vermelho pareceria
 * culpa de quem está esperando resposta do solicitante.
 */
export function FarolSla({ leitura }: { leitura: LeituraFarol }) {
  const classe = CLASSES[leitura.cor];
  return (
    <div className="flex items-center gap-2" title={`${DESCRICAO[leitura.cor]} · ${leitura.rotulo}`}>
      <span aria-hidden className={`h-2.5 w-2.5 shrink-0 rounded-full ${classe.ponto}`} />
      <span className={`text-sm ${classe.texto}`}>
        {leitura.rotulo}
        {leitura.pausado ? <span className="ml-1 text-xs text-slate-400">(pausado)</span> : null}
      </span>
      <span className="sr-only">{DESCRICAO[leitura.cor]}</span>
    </div>
  );
}
