import type { Finalidade } from '@/api/solicitacoes';

export const ROTULO_FINALIDADE: Record<Finalidade, string> = { COMPRA: 'Compra', ORCAMENTO: 'Orçamento' };

const OPCOES: { valor: Finalidade; titulo: string; explicacao: string }[] = [
  { valor: 'COMPRA', titulo: 'Compra', explicacao: 'depois da cotação, segue para a aprovação e vira pedido' },
  { valor: 'ORCAMENTO', titulo: 'Orçamento', explicacao: 'só levantar preço: para depois da cotação e volta para você decidir' },
];

/**
 * A finalidade da SC, obrigatória e sem valor marcado de fábrica: é a primeira coisa que o
 * comprador precisa saber, e um "compra" pré-marcado faria todo orçamento esquecido virar compra
 * na fila do Nível 1. O servidor cobra o mesmo (PR-ERR-024).
 */
export function CampoFinalidade({ valor, aoMudar }: { valor: Finalidade | ''; aoMudar: (f: Finalidade) => void }) {
  return (
    <fieldset className="rounded-lg border border-borda p-3" data-testid="campo-finalidade">
      <legend className="px-1 text-[13px] font-bold">Finalidade da SC <span className="font-normal text-texto-suave">(obrigatório)</span></legend>
      <div className="flex flex-col gap-2 sm:flex-row sm:gap-6">
        {OPCOES.map((o) => (
          <label key={o.valor} className="flex items-start gap-2 text-[13.5px]">
            <input type="radio" name="finalidade" value={o.valor} required className="mt-1"
              checked={valor === o.valor} onChange={() => aoMudar(o.valor)} />
            <span><strong>{o.titulo}</strong> <span className="text-texto-suave">— {o.explicacao}</span></span>
          </label>
        ))}
      </div>
    </fieldset>
  );
}
