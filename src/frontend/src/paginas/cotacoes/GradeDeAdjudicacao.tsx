import { useMemo, useState } from 'react';
import { escolherVencedor, type LoteDaFamilia, type Processo } from '@/api/cotacoes';
import { Campo, Nota } from '@/componentes/formulario';
import { moeda, quantidade } from '@/util/formato';
import {
  colunasDaGrade, fornecedoresEscolhidos, itensSemVencedor, linhasDaGrade,
  melhorPrecoPorItem, totaisPorColuna,
} from './gradeDeAdjudicacao';

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

/**
 * A grade de adjudicação: uma linha por item, uma coluna por fornecedor, e a escolha
 * no cruzamento.
 *
 * É a resposta ao caso que a adjudicação por família não cobria: numa compra de material
 * de escritório, um fornecedor tem o melhor preço no papel e outro na caneta. Antes a
 * família era indivisível e a compra inteira ia para um dos dois, pagando a mais no item
 * do outro. Aqui cada item vai para quem o vence.
 */
export function GradeDeAdjudicacao({ processo, lotes, aoConcluir, aoAvisar }: {
  processo: Processo;
  lotes: LoteDaFamilia[] | null;
  aoConcluir: () => void;
  aoAvisar: (t: string, tipo?: 'ok' | 'erro') => void;
}) {
  const colunas = useMemo(() => colunasDaGrade(processo), [processo]);
  const linhas = useMemo(() => linhasDaGrade(processo, lotes), [processo, lotes]);
  const [escolhas, setEscolhas] = useState<Record<string, string>>({});
  const [justificativa, setJustificativa] = useState('');
  const [salvando, setSalvando] = useState(false);

  const totais = totaisPorColuna(linhas, escolhas);
  const faltando = itensSemVencedor(linhas, escolhas);
  const fornecedores = fornecedoresEscolhidos(linhas, escolhas);
  const totalDaCompra = Object.values(totais).reduce((s, t) => s + t.selecionado, 0);
  const podeConfirmar = faltando.length === 0 && justificativa.trim().length > 0 && !salvando;

  async function confirmar() {
    setSalvando(true);
    try {
      const awards = linhas.map((l) => ({
        // a família vem do item no servidor: mandá-la daqui seria repetir um dado
        // que ele já tem, e que esta tela poderia errar
        family: '', quotationItemId: l.item.id, proposalId: escolhas[l.item.id],
        criteria: [], justification: justificativa.trim(),
      }));
      await escolherVencedor(processo.id, {
        proposalId: awards[0].proposalId, criteria: [], justification: justificativa.trim(), awards,
      });
      aoAvisar(fornecedores.length > 1
        ? `Compra dividida entre ${fornecedores.length} fornecedores. O processo seguiu para a aprovação.`
        : 'Fornecedor escolhido. O processo seguiu para a aprovação.');
      aoConcluir();
    } catch (e) { aoAvisar(mensagem(e, 'Falha ao registrar a adjudicação.'), 'erro'); }
    finally { setSalvando(false); }
  }

  return (
    <div data-testid="grade-adjudicacao">
      <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-[14px] font-bold">Escolher o vencedor de cada item</h3>
        <button type="button" className="botao-secundario !py-1.5"
          onClick={() => setEscolhas(melhorPrecoPorItem(linhas))}>
          Melhor preço por item
        </button>
      </div>
      <Nota>
        Cada item vai para quem o vence — não é preciso dar a compra inteira a um fornecedor
        só. Quem levar mais de um item recebe todos na mesma O.C.
      </Nota>

      <div className="mt-3 overflow-x-auto">
        <table className="min-w-[720px]">
          <thead>
            <tr>
              <th>Produto</th>
              <th className="whitespace-nowrap">Qtde</th>
              {colunas.map((c) => (
                <th key={c.proposalId} className="text-center">
                  {c.supplierName} <span className="sub">v{c.version}</span>
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {linhas.map((l) => (
              <tr key={l.item.id} data-item={l.item.description}>
                <td>
                  {l.item.description}
                  <div className="sub">{l.item.family}</div>
                </td>
                <td className="sub whitespace-nowrap">{quantidade(l.item.quantity)} {l.item.unitOfMeasure}</td>
                {l.celulas.map((c) => {
                  const escolhida = escolhas[l.item.id] === c.proposalId;
                  const indisponivel = c.total == null || !!c.impedimento;
                  return (
                    <td key={c.proposalId} className={'text-center ' + (escolhida ? 'bg-ok-fundo font-semibold' : '')}>
                      {c.total == null ? (
                        // vazio é informação: este fornecedor não cotou este item
                        <span className="sub">—</span>
                      ) : (
                        <label className="flex items-center justify-center gap-1.5 font-normal">
                          <input type="radio" className="w-auto" name={`vencedor-${l.item.id}`}
                            disabled={indisponivel} checked={escolhida}
                            aria-label={`${c.supplierName} para ${l.item.description}`}
                            onChange={() => setEscolhas((x) => ({ ...x, [l.item.id]: c.proposalId }))} />
                          <span>{moeda(c.total)}</span>
                        </label>
                      )}
                      {c.impedimento && c.total != null && (
                        <div className="sub text-perigo" data-impedimento>{c.impedimento}</div>
                      )}
                    </td>
                  );
                })}
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr>
              <td colSpan={2} className="sub">Total cotado pelo fornecedor</td>
              {colunas.map((c) => (
                <td key={c.proposalId} className="sub text-center">{moeda(totais[c.proposalId]?.cotado ?? 0)}</td>
              ))}
            </tr>
            <tr>
              <td colSpan={2} className="font-semibold">Total selecionado</td>
              {colunas.map((c) => (
                <td key={c.proposalId} className="text-center font-semibold" data-selecionado={c.supplierName}>
                  {moeda(totais[c.proposalId]?.selecionado ?? 0)}
                </td>
              ))}
            </tr>
          </tfoot>
        </table>
      </div>

      <p className="mt-3 text-[13px]" data-testid="resumo-grade">
        <strong>Total da compra: {moeda(totalDaCompra)}</strong>
        {fornecedores.length > 1 && <span className="sub"> · dividida entre {fornecedores.length} fornecedores</span>}
      </p>

      <Campo id="ga-justificativa" rotulo="Justificativa da escolha" dica="(obrigatória)" className="mt-3">
        <input id="ga-justificativa" placeholder="Por que estes fornecedores vencem?"
          value={justificativa} onChange={(e) => setJustificativa(e.target.value)} />
      </Campo>

      {faltando.length > 0 && (
        <p className="mt-2 text-[12.5px] text-perigo" data-testid="faltando">
          Falta escolher o vencedor de: {faltando.map((i) => i.description).join(', ')}.
        </p>
      )}

      <div className="mt-3">
        <button type="button" className="botao" disabled={!podeConfirmar} onClick={confirmar}>
          {salvando ? 'Registrando…' : 'Confirmar escolha e enviar à aprovação'}
        </button>
      </div>
      <Nota>A escolha, os itens de cada fornecedor e a justificativa ficam na auditoria do processo.</Nota>
    </div>
  );
}
