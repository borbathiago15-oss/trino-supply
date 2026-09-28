import { TOM_DA_FAIXA, textoDaMeta, type ComparacaoComMeta } from '@/api/metas';

/**
 * A linha da meta no card. Sem comparação não desenha nada: indicador sem meta cadastrada
 * mostra só o número, e "sem meta" repetido em todo card seria ruído. A cor vem da faixa
 * que o servidor calculou — a tela não refaz a régua.
 */
export function LinhaDaMeta({ comparacao, formatar }:
  { comparacao?: ComparacaoComMeta; formatar: (v: number) => string }) {
  if (!comparacao) return null;
  return (
    <div className={`mt-1 text-[12px] font-semibold ${TOM_DA_FAIXA[comparacao.faixa]}`} data-testid="linha-da-meta">
      {textoDaMeta(comparacao, formatar)}
    </div>
  );
}
