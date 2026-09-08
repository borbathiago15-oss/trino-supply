import { useState } from 'react';
import { propostasVigentes, registrarNegociacao, type Processo } from '@/api/cotacoes';
import { Painel, Vazio } from '@/componentes/basicos';
import { Campo, Grade2, Nota } from '@/componentes/formulario';
import { moeda } from '@/util/formato';

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

const VAZIO = { fornecedor: '', valor: '', percentual: '', notas: '' };

/**
 * O ganho de negociação e o formulário que o registra.
 *
 * O ganho é apurado contra a **primeira** proposta do fornecedor vencedor —
 * quem faz a conta é o backend, e o painel só mostra as duas pontas.
 *
 * Saiu de `ProcessoDetalhe.tsx` no INT-B, com o estado do formulário junto: ele
 * não era usado em nenhum outro lugar da tela.
 */
export function PainelDeNegociacao({ processo: q, podeNegociar, aoRegistrar, aoAvisar }: {
  processo: Processo;
  podeNegociar: boolean;
  aoRegistrar: () => void;
  aoAvisar: (t: string, tipo?: 'ok' | 'erro') => void;
}) {
  const [form, setForm] = useState(VAZIO);
  const vigentes = propostasVigentes(q);

  const campo = (k: keyof typeof VAZIO) => ({
    value: form[k],
    onChange: (e: { target: { value: string } }) => setForm((n) => ({ ...n, [k]: e.target.value })),
  });

  async function negociar() {
    const valor = Number(form.valor.replace(',', '.'));
    const percentual = Number(form.percentual.replace(',', '.'));
    const temValor = form.valor.trim() && !Number.isNaN(valor);
    const temPercentual = form.percentual.trim() && !Number.isNaN(percentual);
    if (!form.fornecedor) { aoAvisar('Escolha o fornecedor negociado.', 'erro'); return; }
    if (!temValor && !temPercentual) {
      aoAvisar('Informe o valor fechado ou o desconto negociado.', 'erro');
      return;
    }
    try {
      await registrarNegociacao(q.id, {
        supplierId: form.fornecedor,
        closedValue: temValor ? valor : null,
        discountPercent: !temValor && temPercentual ? percentual : null,
        notes: form.notas || null,
      });
      aoAvisar('Negociação registrada. O mapa passa a comparar pelo valor fechado.');
      setForm(VAZIO);
      aoRegistrar();
    } catch (e) { aoAvisar(mensagem(e, 'Falha ao registrar a negociação.'), 'erro'); }
  }

  return (
    <Painel titulo="Negociação e ganho">
      {q.saving
        ? <div className={'rounded-lg border px-4 py-3 ' + (q.saving.value > 0 ? 'border-ok/30 bg-ok-fundo' : 'border-borda bg-superficie-suave')}>
            <strong>
              Ganho de negociação: {moeda(q.saving.value)}
              {q.saving.percent != null && ` (${q.saving.percent}%)`}
            </strong>
            <div className="sub">
              Primeira proposta {moeda(q.saving.baselineValue)} → fechado {moeda(q.saving.closedValue)}
              {q.saving.byLabel && ` · negociado por ${q.saving.byLabel}`}
              {q.saving.notes && ` · “${q.saving.notes}”`}
            </div>
            {/* as outras duas réguas do §17. Só aparecem quando existem: o processo com
                proponente único não finge disputa, e a SC sem orçamento não finge meta */}
            {(q.saving.competitionValue != null || q.saving.budgetValue != null) && (
              <ul className="sub mt-2 space-y-1" data-testid="reguas-de-saving">
                {q.saving.competitionValue != null && (
                  <li>
                    <strong>Concorrência do BID: {moeda(q.saving.competitionValue)}</strong>
                    {' '}— maior proposta {moeda(q.saving.competitionBaselineValue ?? 0)} → fechado{' '}
                    {moeda(q.saving.closedValue)}
                  </li>
                )}
                {q.saving.budgetValue != null && (
                  <li>
                    <strong>Contra o orçamento: {moeda(q.saving.budgetValue)}</strong>
                    {' '}— previsto pelo solicitante {moeda(q.saving.budgetBaselineValue ?? 0)} → fechado{' '}
                    {moeda(q.saving.closedValue)}
                  </li>
                )}
              </ul>
            )}
          </div>
        : <Vazio>Nenhuma negociação registrada. O ganho é medido contra a primeira proposta do fornecedor.</Vazio>}

      {podeNegociar && (
        <div className="mt-4 border-t border-borda pt-4" data-testid="form-negociacao">
          <Grade2>
            <Campo id="ng-fornecedor" rotulo="Fornecedor negociado">
              <select id="ng-fornecedor" {...campo('fornecedor')}>
                <option value="">Escolha o fornecedor…</option>
                {vigentes.map((p) => (
                  <option key={p.supplierId} value={p.supplierId}>
                    {p.supplierName} — proposta atual {moeda(p.totalValue)}
                  </option>
                ))}
              </select>
            </Campo>
            <Grade2>
              <Campo id="ng-valor" rotulo="Valor fechado (R$)">
                <input id="ng-valor" type="number" min="0" step="0.01" placeholder="ex.: 95000" {...campo('valor')} />
              </Campo>
              <Campo id="ng-percentual" rotulo="ou desconto (%)">
                <input id="ng-percentual" type="number" min="0" max="99.99" step="0.01" placeholder="ex.: 5" {...campo('percentual')} />
              </Campo>
            </Grade2>
          </Grade2>
          <Campo id="ng-notas" rotulo="O que foi negociado" className="mt-3">
            <input id="ng-notas" placeholder="ex.: 5% de desconto após negociação de prazo" {...campo('notas')} />
          </Campo>
          <button type="button" className="botao mt-3" onClick={negociar}>Registrar negociação</button>
          <Nota>O valor fechado entra como nova versão da proposta e o mapa passa a comparar por ele.</Nota>
        </div>
      )}
    </Painel>
  );
}
