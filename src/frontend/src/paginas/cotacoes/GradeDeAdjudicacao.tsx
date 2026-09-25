import { useMemo, useState } from 'react';
import { escolherVencedor, type LoteDaFamilia, type Processo } from '@/api/cotacoes';
import { Campo, Nota } from '@/componentes/formulario';
import { moeda, quantidade } from '@/util/formato';
import {
  adjudicacoesDoFormulario, colunasDaGrade, erroDaDivisao, estaDividido,
  fornecedoresEscolhidos, itensSemVencedor, levarTudoDe, linhasDaGrade,
  melhorPrecoPorItem, quantoDivide, quantosLevaria, totaisPorColuna, type Divisoes,
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
  const [divisoes, setDivisoes] = useState<Divisoes>({});
  const [justificativa, setJustificativa] = useState('');
  const [salvando, setSalvando] = useState(false);

  const totais = totaisPorColuna(linhas, escolhas, divisoes);
  const faltando = itensSemVencedor(linhas, escolhas, divisoes);
  const fornecedores = fornecedoresEscolhidos(linhas, escolhas, divisoes);

  const dividido = (itemId: string) => estaDividido(divisoes, itemId);

  /** Liga e desliga a divisão de um item. Desligar apaga o que foi digitado nele. */
  const alternarDivisao = (itemId: string) => setDivisoes((d) => {
    const { [itemId]: atual, ...resto } = d;
    return atual ? resto : { ...resto, [itemId]: {} };
  });
  const totalDaCompra = Object.values(totais).reduce((s, t) => s + t.selecionado, 0);
  const podeConfirmar = faltando.length === 0 && justificativa.trim().length > 0 && !salvando;

  async function confirmar() {
    setSalvando(true);
    try {
      // item dividido vira uma linha por fornecedor com quantidade; item de vencedor
      // único vira uma linha sem quantidade, que é como o servidor lê "o item inteiro"
      const awards = adjudicacoesDoFormulario(linhas, escolhas, divisoes, justificativa.trim());
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
          Preencher com o melhor preço
        </button>
      </div>
      <Nota>
        <strong>A escolha é sua.</strong> A grade marca o menor preço de cada item e quanto as
        outras ofertas estão acima dele, mas não decide nada: marcar é informação, e o vencedor
        é quem você apontar. Cada item pode ir para um fornecedor diferente, e quem levar mais
        de um recebe todos na mesma O.C. Os dois atalhos são pontos de partida —
        <strong> preencher com o melhor preço</strong> espalha a compra,
        <strong> levar tudo</strong> no topo da coluna a concentra num fornecedor —, e depois de
        qualquer um deles a linha continua editável. Precisa partir o <strong>mesmo item</strong>
        {' '}entre dois? Use <strong>dividir</strong> na linha e diga quanto vai com cada um.
      </Nota>

      <div className="mt-3 overflow-x-auto">
        <table className="min-w-[720px]">
          <thead>
            <tr>
              <th>Produto</th>
              <th className="whitespace-nowrap">Qtde</th>
              {colunas.map((c) => {
                const leva = quantosLevaria(linhas, c.proposalId, divisoes);
                return (
                  <th key={c.proposalId} className="text-center">
                    {c.supplierName} <span className="sub">v{c.version}</span>
                    {/* o atalho que concentra a compra, e o número que diz de quantos itens
                        ele dá conta — "levar tudo (8)" em doze itens conta, sem abrir nada,
                        que este fornecedor não cotou quatro */}
                    <div>
                      <button type="button" className="font-normal underline disabled:no-underline disabled:opacity-40"
                        data-testid={`levar-tudo-${c.proposalId}`} disabled={leva === 0}
                        onClick={() => setEscolhas((x) => levarTudoDe(linhas, c.proposalId, x, divisoes))}>
                        levar tudo ({leva})
                      </button>
                    </div>
                  </th>
                );
              })}
            </tr>
          </thead>
          <tbody>
            {linhas.map((l) => (
              <tr key={l.item.id} data-item={l.item.description}>
                <td>
                  {l.item.description}
                  <div className="sub">{l.item.family}</div>
                </td>
                <td className="sub whitespace-nowrap">
                  {quantidade(l.item.quantity)} {l.item.unitOfMeasure}
                  {/* dividir a quantidade do mesmo item: o caso do lote grande que dois
                      fornecedores atendem juntos, e que a escolha por item não cobria */}
                  <div>
                    <button type="button" className="underline"
                      data-testid={`dividir-${l.item.id}`}
                      onClick={() => alternarDivisao(l.item.id)}>
                      {dividido(l.item.id) ? 'não dividir' : 'dividir'}
                    </button>
                  </div>
                  {erroDaDivisao(l, divisoes) && (
                    <div className="font-bold text-perigo" data-testid={`erro-divisao-${l.item.id}`}>
                      {erroDaDivisao(l, divisoes)}
                    </div>
                  )}
                </td>
                {l.celulas.map((c) => {
                  const escolhida = escolhas[l.item.id] === c.proposalId;
                  const indisponivel = c.total == null || !!c.impedimento;
                  const parte = quantoDivide(divisoes, l.item.id, c.proposalId);
                  return (
                    <td key={c.proposalId}
                      className={'text-center ' + (escolhida && !dividido(l.item.id) ? 'bg-ok-fundo font-semibold' : '')}>
                      {c.total == null ? (
                        // vazio é informação: este fornecedor não cotou este item
                        <span className="sub">—</span>
                      ) : dividido(l.item.id) ? (
                        <>
                          <input type="number" min={0} step="any" className="!w-[92px] text-center"
                            disabled={indisponivel}
                            aria-label={`Quantidade de ${l.item.description} com ${c.supplierName}`}
                            value={divisoes[l.item.id]?.[c.proposalId] ?? ''}
                            onChange={(e) => setDivisoes((d) => ({
                              ...d,
                              [l.item.id]: { ...(d[l.item.id] ?? {}), [c.proposalId]: e.target.value },
                            }))} />
                          <div className="sub">
                            {parte > 0 ? moeda((c.unitPrice ?? 0) * parte) : `${moeda(c.unitPrice ?? 0)}/${l.item.unitOfMeasure}`}
                          </div>
                        </>
                      ) : (
                        <label className="flex items-center justify-center gap-1.5 font-normal">
                          <input type="radio" className="w-auto" name={`vencedor-${l.item.id}`}
                            disabled={indisponivel} checked={escolhida}
                            aria-label={`${c.supplierName} para ${l.item.description}`}
                            onChange={() => setEscolhas((x) => ({ ...x, [l.item.id]: c.proposalId }))} />
                          <span className="text-left">
                            <span className="block">{moeda(c.total)}</span>
                            {/* o unitário é o que o comprador negocia; deixá-lo fora obrigava
                                a dividir o total pela quantidade de cabeça */}
                            <span className="sub block whitespace-nowrap">
                              {moeda(c.unitPrice ?? 0)}/{l.item.unitOfMeasure}
                            </span>
                          </span>
                        </label>
                      )}
                      {/* a comparação é informação permanente, não resultado de apertar um
                          botão: ver o menor preço não pode custar a escolha já feita */}
                      {c.menorPreco && (
                        <div className="sub font-semibold text-ok" data-testid={`menor-${c.proposalId}`}>
                          menor preço
                        </div>
                      )}
                      {c.acimaDoMenor != null && (
                        <div className="sub text-aviso" data-testid={`acima-${c.proposalId}`}>
                          +{quantidade(c.acimaDoMenor)}%
                        </div>
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
          {salvando ? 'Registrando…' : processo.isBudget && !processo.budgetConvertedAt
          ? 'Confirmar escolha e apresentar o orçamento' : 'Confirmar escolha e enviar à aprovação'}
        </button>
      </div>
      <Nota>A escolha, os itens de cada fornecedor e a justificativa ficam na auditoria do processo.</Nota>
    </div>
  );
}
