import { useState } from 'react';
import { Link } from 'react-router-dom';
import {
  comparacaoDaEscolha, conflitoDeSegregacao, decidir, diasDesde, minhasDecisoes, processosParaMinhaAprovacao,
  ROTULO_DECISAO, ROTULO_RFQ, type Alcada, type Decisao, type DecisaoRecente, type ProcessoParaAprovar,
} from '@/api/cotacoes';
import {
  aprovarMaterial, listarSolicitacoesMaterial, recusarMaterial, type SolicitacaoMaterial,
} from '@/api/material';
import {
  aprovacoesPendentes, aprovarSolicitacao, devolverSolicitacao, rejeitarSolicitacao,
  situacaoDaSc, type SolicitacaoCompra,
} from '@/api/solicitacoes';
import { Aviso, Badge, Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { DialogoMotivo } from '@/componentes/DialogoMotivo';
import { Nota } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { useUsuario } from '@/sessao/SessaoProvider';
import { data, dataHora, moeda, quantidade } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';
import { resumoDosItens } from '@/paginas/solicitacoes/MeusPedidos';

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

/** Endereço do processo, que a fila abre com o número já resolvido. */
export const linkDoProcesso = (id: string) => `/cotacoes/${id}`;

/** A alçada que o processo espera — é ela que diz qual rota decide. */
export const alcadaDe = (status: string): Alcada | null =>
  status === 'AGUARDANDO_GERENTE' ? 'manager' : status === 'AGUARDANDO_DIRETOR' ? 'director' : null;

type Acao =
  | { tipo: 'decidir'; q: ProcessoParaAprovar; alcada: Alcada; decisao: Decisao }
  | { tipo: 'aprovar-sc'; sc: SolicitacaoCompra }
  | { tipo: 'devolver-sc'; sc: SolicitacaoCompra }
  | { tipo: 'rejeitar-sc'; sc: SolicitacaoCompra }
  | { tipo: 'aprovar-material'; mr: SolicitacaoMaterial }
  | { tipo: 'recusar-material'; mr: SolicitacaoMaterial };

export function CentralDeAprovacao() {
  const { avisar } = useToast();
  const usuario = useUsuario();
  const [acao, setAcao] = useState<Acao | null>(null);
  const [liberado, setLiberado] = useState<Record<string, string>>({});

  const { dados, erro, carregando, recarregar } = useCarregar(async (signal) => ({
    // cada fila é opcional: quem não tem acesso a uma continua vendo as outras
    processos: await processosParaMinhaAprovacao(signal).catch(() => [] as ProcessoParaAprovar[]),
    materiais: (await listarSolicitacoesMaterial(signal).catch(() => [] as SolicitacaoMaterial[]))
      .filter((r) => r.status === 'AGUARDANDO_APROVACAO'),
    solicitacoes: await aprovacoesPendentes(signal).catch(() => [] as SolicitacaoCompra[]),
    decididos: await minhasDecisoes(signal).catch(() => [] as DecisaoRecente[]),
  }), []);

  const processos = dados?.processos ?? [];
  const materiais = dados?.materiais ?? [];
  const solicitacoes = dados?.solicitacoes ?? [];
  const decididos = dados?.decididos ?? [];
  const vazia = dados && !processos.length && !materiais.length && !solicitacoes.length;

  /** Quantidade liberada de um item: o que o aprovador digitou, ou tudo. */
  const quantidadeLiberada = (mr: SolicitacaoMaterial, itemId: string, pedida: number) => {
    const valor = liberado[`${mr.id}:${itemId}`];
    return valor === undefined || valor === '' ? pedida : parseFloat(valor) || 0;
  };

  async function concluir(texto: string) {
    if (!acao) return;
    setAcao(null);
    try {
      if (acao.tipo === 'decidir') {
        await decidir(acao.q.id, acao.alcada, acao.decisao, texto || null);
        avisar(acao.decisao === 'APROVAR' ? `${acao.q.number} aprovado.`
          : acao.decisao === 'AJUSTES' ? `${acao.q.number} devolvido ao comprador para ajustes.`
          : `${acao.q.number} rejeitado.`);
      } else if (acao.tipo === 'aprovar-sc') {
        await aprovarSolicitacao(acao.sc.id, texto || null);
        avisar(`Solicitação ${acao.sc.number} aprovada.`);
      } else if (acao.tipo === 'devolver-sc') {
        await devolverSolicitacao(acao.sc.id, texto);
        avisar(`Solicitação ${acao.sc.number} devolvida para ajuste.`);
      } else if (acao.tipo === 'rejeitar-sc') {
        await rejeitarSolicitacao(acao.sc.id, texto);
        avisar(`Solicitação ${acao.sc.number} rejeitada.`);
      } else if (acao.tipo === 'recusar-material') {
        await recusarMaterial(acao.mr.id, texto);
        avisar('Solicitação de material recusada.');
      } else {
        const items = acao.mr.items.map((i) => ({
          itemId: i.itemId, quantity: quantidadeLiberada(acao.mr, i.itemId, i.quantity),
        }));
        await aprovarMaterial(acao.mr.id, items, texto || null);
        avisar('Solicitação liberada para o almoxarifado.');
      }
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao concluir a ação.'), 'erro'); }
  }

  return (
    <>
      <Painel titulo="Compras aguardando a sua decisão">
        {erro && <Erro>{erro}</Erro>}
        {carregando && !dados && <Carregando />}
        {dados && !processos.length && (
          <Vazio icone="ok" titulo="Fila limpa">
            {vazia ? 'Nenhuma aprovação pendente para você agora.' : 'Nenhuma compra aguardando a sua aprovação.'}
          </Vazio>
        )}
        {/*
          Um card por processo, e o mesmo card no notebook e no celular. O que sustenta a
          decisão está aqui: quem pediu e por quê, a escolha do comprador contra a mais
          barata, a justificativa dele, quem já deu o Nível 1 e há quanto tempo espera.
          O processo completo fica a um clique — é o segundo caminho, não o primeiro.
        */}
        {processos.length > 0 && (
          <ul className="flex flex-col gap-3" data-testid="fila-decisao">
            {processos.map((q) => (
              <CardDeDecisao key={q.id} q={q} usuarioId={usuario.id}
                aoDecidir={(alcada, decisao) => setAcao({ tipo: 'decidir', q, alcada, decisao })} />
            ))}
          </ul>
        )}
      </Painel>

      {materiais.length > 0 && (
        <Painel titulo="Material do almoxarifado — sua aprovação">
          <Nota>Aprove como está ou ajuste a quantidade liberada; a quantidade pedida não muda.</Nota>
          <div className="mt-3 overflow-x-auto">
            <table data-testid="tabela-material">
              <thead><tr><th>Solicitação</th><th>Solicitante</th><th>Itens</th><th>Ações</th></tr></thead>
              <tbody>
                {materiais.map((r) => (
                  <tr key={r.id} data-material={r.number}>
                    <td className="whitespace-nowrap">
                      <span className="font-semibold">{r.number}</span>
                      <div className="sub">CC: {r.costCenter}</div>
                    </td>
                    <td>{r.requesterLabel}{r.notes && <div className="sub">{r.notes}</div>}</td>
                    <td className="min-w-[320px]">
                      {r.items.map((i) => (
                        <div key={i.itemId} className="mb-1 flex flex-wrap items-center gap-2">
                          <span>{i.description}</span>
                          <input type="number" min={0} step="0.01" max={i.quantity} className="!w-[96px]"
                            aria-label={`Quantidade liberada de ${i.description}`}
                            value={liberado[`${r.id}:${i.itemId}`] ?? String(i.quantity)}
                            onChange={(e) => setLiberado((l) => ({ ...l, [`${r.id}:${i.itemId}`]: e.target.value }))} />
                          <span className="sub">de {quantidade(i.quantity)} {i.unitOfMeasure}</span>
                        </div>
                      ))}
                    </td>
                    <td className="whitespace-nowrap">
                      <div className="flex gap-1.5">
                        <button type="button" className="botao" onClick={() => setAcao({ tipo: 'aprovar-material', mr: r })}>Aprovar</button>
                        <button type="button" className="botao-perigo" onClick={() => setAcao({ tipo: 'recusar-material', mr: r })}>Recusar</button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Painel>
      )}

      {solicitacoes.length > 0 && (
        <Painel titulo="Solicitações do fluxo anterior">
          <Nota>Autorização sem preço, do fluxo que existia antes da cotação obrigatória.</Nota>
          <div className="mt-3 overflow-x-auto">
            <table data-testid="tabela-scs" className="min-w-[900px]">
              <thead><tr><th>Número</th><th>Resumo</th><th>Valor est.</th><th>Situação</th><th>Ações</th></tr></thead>
              <tbody>
                {solicitacoes.map((r) => {
                  const marca = situacaoDaSc(r);
                  return (
                    <tr key={r.id} data-solicitacao={r.number}>
                      <td className="whitespace-nowrap">
                        <span className="font-semibold">{r.number}</span>
                        <div className="sub">{r.requesterLabel}</div>
                      </td>
                      <td className="min-w-[300px]">
                        {r.justification}
                        <div className="sub">{resumoDosItens(r)}</div>
                        <div className="sub">CC: {r.costCenter}</div>
                      </td>
                      <td className="whitespace-nowrap">{moeda(r.totalEstimatedValue)}</td>
                      <td><Badge classe={marca.classe}>{marca.rotulo}</Badge></td>
                      <td className="whitespace-nowrap">
                        <div className="flex gap-1.5">
                          <button type="button" className="botao" onClick={() => setAcao({ tipo: 'aprovar-sc', sc: r })}>Aprovar</button>
                          <button type="button" className="botao-secundario" onClick={() => setAcao({ tipo: 'devolver-sc', sc: r })}>Devolver</button>
                          <button type="button" className="botao-perigo" onClick={() => setAcao({ tipo: 'rejeitar-sc', sc: r })}>Rejeitar</button>
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </Painel>
      )}

      {decididos.length > 0 && (
        <Painel titulo="Suas decisões recentes">
          <div className="overflow-x-auto">
            <table data-testid="decisoes-recentes">
              <thead><tr><th>Quando</th><th>Processo</th><th>Decisão</th><th>Fornecedor</th><th>Valor</th><th>Situação hoje</th></tr></thead>
              <tbody>
                {decididos.map((d) => (
                  <tr key={`${d.quotationId}-${d.occurredAt}`} data-processo={d.number}>
                    <td className="whitespace-nowrap">{dataHora(d.occurredAt)}</td>
                    <td><Link className="font-semibold text-marca hover:underline" to={linkDoProcesso(d.quotationId)}>{d.number}</Link></td>
                    <td>{ROTULO_DECISAO[d.eventType] ?? d.eventType}{d.note && <div className="sub">{d.note}</div>}</td>
                    <td>{d.supplierName ?? '—'}</td>
                    <td className="whitespace-nowrap">{d.totalValue != null ? moeda(d.totalValue) : '—'}</td>
                    <td><Badge classe={(ROTULO_RFQ[d.status] ?? { classe: '' }).classe}>{(ROTULO_RFQ[d.status] ?? { rotulo: d.status }).rotulo}</Badge></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Nota>Últimos 30 dias. O que você aprovou segue com o comprador; a situação de hoje mostra onde chegou.</Nota>
        </Painel>
      )}

      {acao?.tipo === 'decidir' && acao.decisao === 'APROVAR' && (
        <DialogoMotivo titulo={`Aprovar ${acao.q.number}`} rotulo="Comentário da aprovação" dica="(opcional)"
          rotuloConfirmar="Aprovar" aoConfirmar={concluir} aoFechar={() => setAcao(null)} />
      )}
      {acao?.tipo === 'decidir' && acao.decisao === 'AJUSTES' && (
        <DialogoMotivo titulo={`Pedir ajustes em ${acao.q.number}`} rotulo="O que o comprador deve ajustar?" obrigatorio
          rotuloConfirmar="Solicitar ajustes" aoConfirmar={concluir} aoFechar={() => setAcao(null)} />
      )}
      {acao?.tipo === 'decidir' && acao.decisao === 'REJEITAR' && (
        <DialogoMotivo titulo={`Rejeitar ${acao.q.number}`} rotulo="Motivo da rejeição" obrigatorio perigo
          rotuloConfirmar="Rejeitar" aoConfirmar={concluir} aoFechar={() => setAcao(null)} />
      )}
      {acao?.tipo === 'aprovar-sc' && (
        <DialogoMotivo titulo={`Aprovar ${acao.sc.number}`} rotulo="Comentário da aprovação" dica="(opcional)"
          rotuloConfirmar="Aprovar" aoConfirmar={concluir} aoFechar={() => setAcao(null)} />
      )}
      {acao?.tipo === 'devolver-sc' && (
        <DialogoMotivo titulo={`Devolver ${acao.sc.number}`} rotulo="O que o solicitante deve ajustar?" obrigatorio
          rotuloConfirmar="Devolver" aoConfirmar={concluir} aoFechar={() => setAcao(null)} />
      )}
      {acao?.tipo === 'rejeitar-sc' && (
        <DialogoMotivo titulo={`Rejeitar ${acao.sc.number}`} rotulo="Motivo da rejeição" obrigatorio perigo
          rotuloConfirmar="Rejeitar" aoConfirmar={concluir} aoFechar={() => setAcao(null)} />
      )}
      {acao?.tipo === 'aprovar-material' && (
        <DialogoMotivo titulo={`Liberar ${acao.mr.number}`} rotulo="Observação da aprovação" dica="(opcional)"
          rotuloConfirmar="Liberar" aoConfirmar={concluir} aoFechar={() => setAcao(null)} />
      )}
      {acao?.tipo === 'recusar-material' && (
        <DialogoMotivo titulo={`Recusar ${acao.mr.number}`} rotulo="Justificativa da recusa" obrigatorio perigo
          rotuloConfirmar="Recusar" aoConfirmar={concluir} aoFechar={() => setAcao(null)} />
      )}
    </>
  );
}

const ROTULO_NIVEL: Record<number, string> = { 1: 'Nível 1 — gestor do centro', 2: 'Nível 2 — diretoria' };

/**
 * O card de decisão. Cada bloco responde a uma pergunta que o aprovador faria antes de
 * apertar o botão: quem pediu e por quê; o que o comprador escolheu e por que não a mais
 * barata; o que o sistema mede (compliance, contrato, orçamento); quem já aprovou.
 */
export function CardDeDecisao({ q, usuarioId, aoDecidir }: {
  q: ProcessoParaAprovar; usuarioId?: string; aoDecidir: (alcada: Alcada, decisao: Decisao) => void;
}) {
  const d = q.decisao;
  const escolha = comparacaoDaEscolha(q);
  const alcada = alcadaDe(q.status);
  const conflito = alcada ? conflitoDeSegregacao(q, usuarioId, alcada) : null;
  const espera = diasDesde(d?.waitingSince ?? q.managerApproval?.at ?? null);
  const esperaClasse = espera == null ? '' : espera >= 5 ? 'bg-perigo-fundo text-perigo' : espera >= 3 ? 'bg-aviso-fundo text-aviso' : 'bg-slate-100 text-slate-600';
  const urgente = d?.priority === 'URGENT';
  const penalidades = d?.compliancePenalties ?? [];
  const acimaDoOrcamento = d?.budget != null && escolha.total != null && escolha.total > d.budget;
  const saving = q.saving;

  return (
    <li data-processo={q.number} data-nivel={d?.level ?? ''} className="rounded-xl border border-borda bg-white p-4 shadow-sm">
      {/* cabeçalho: o que é, quanto custa, há quanto tempo espera */}
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-[15px] font-bold">{q.number}</span>
            <Badge classe="bg-marca/10 text-marca">{ROTULO_NIVEL[d?.level ?? (alcada === 'director' ? 2 : 1)]}</Badge>
            {urgente && <Badge classe="bg-perigo-fundo text-perigo">URGENTE</Badge>}
            {espera != null && (
              <Badge classe={esperaClasse}>{espera === 0 ? 'chegou hoje' : `espera há ${espera} dia${espera === 1 ? '' : 's'}`}</Badge>
            )}
          </div>
          <div className="sub mt-0.5">
            Centro {q.costCenter}{q.sourcePrNumbers.length ? ` · ${q.sourcePrNumbers.join(', ')}` : q.sourcePrNumber ? ` · ${q.sourcePrNumber}` : ''}
            {' · '}conduzido por {q.createdByLabel ?? '—'}
          </div>
        </div>
        <div className="text-right">
          <div className="rotulo">Valor da compra</div>
          <div className="text-[22px] font-bold leading-tight text-slate-900" data-testid="valor-da-compra">
            {escolha.total != null ? moeda(escolha.total) : '—'}
          </div>
          {d?.budget != null && (
            <div className={`text-[12px] ${acimaDoOrcamento ? 'font-semibold text-perigo' : 'text-texto-suave'}`}>
              {acimaDoOrcamento ? 'acima do' : 'dentro do'} orçamento de {moeda(d.budget)}
            </div>
          )}
        </div>
      </div>

      <div className="mt-3 grid gap-3 md:grid-cols-2">
        {/* quem pediu e por quê */}
        <div className="rounded-lg bg-slate-50 px-3 py-2">
          <div className="rotulo">Quem pediu e por quê</div>
          <div className="text-[13.5px]"><strong>{d?.requesterLabel || '—'}</strong>{d?.neededBy && <span className="sub"> · precisa até {data(d.neededBy)}</span>}</div>
          <div className="text-[13px]">{q.justification || '—'}</div>
          {urgente && (d?.urgencyReason || d?.urgencyImpact) && (
            <div className="mt-1 text-[12.5px] text-perigo">
              Urgência: {d?.urgencyReason}{d?.urgencyImpact ? ` — se não comprar: ${d.urgencyImpact}` : ''}
            </div>
          )}
          <div className="sub mt-1">
            {q.items.length} {q.items.length === 1 ? 'item' : 'itens'}
            {q.items.length > 0 && `: ${q.items.slice(0, 3).map((i) => i.description).join(', ')}${q.items.length > 3 ? '…' : ''}`}
          </div>
        </div>

        {/* o que o comprador escolheu */}
        <div className="rounded-lg bg-slate-50 px-3 py-2">
          <div className="rotulo">Escolha do comprador</div>
          <div className="text-[13.5px]"><strong>{escolha.fornecedor ?? '—'}</strong>
            {escolha.deliveryDays != null && <span className="sub"> · entrega em {escolha.deliveryDays} dia(s)</span>}
            {escolha.paymentTerms && <span className="sub"> · {escolha.paymentTerms}</span>}
          </div>
          <div className="text-[13px]" data-testid="comparacao">
            {escolha.maisBarata
              ? <>É <strong className="text-aviso">{quantidade(escolha.acimaPercent)}% acima</strong> da mais barata ({escolha.maisBarata.supplierName}, {moeda(escolha.maisBarata.totalValue)}).</>
              : escolha.propostas > 1
                ? <>É a <strong className="text-ok">mais barata</strong> entre {escolha.propostas} propostas.</>
                : <span className="text-aviso">Proposta única: não houve outra para comparar.</span>}
          </div>
          {q.selection?.justification && (
            <div className="mt-1 text-[12.5px]">
              <span className="sub">Justificativa{q.selection.criteria ? ` (${q.selection.criteria})` : ''}:</span> {q.selection.justification}
            </div>
          )}
        </div>
      </div>

      {/* o que o sistema mede: saving, compliance, contrato, Nível 1 */}
      <div className="mt-2 flex flex-wrap items-center gap-2 text-[12.5px]" data-testid="medidas">
        {saving && saving.value > 0 && <Badge classe="bg-ok-fundo text-ok">saving de negociação {moeda(saving.value)}</Badge>}
        {saving?.competitionValue != null && saving.competitionValue > 0 && <Badge classe="bg-ok-fundo text-ok">concorrência {moeda(saving.competitionValue)}</Badge>}
        {saving?.budgetValue != null && saving.budgetValue > 0 && <Badge classe="bg-ok-fundo text-ok">abaixo do orçamento {moeda(saving.budgetValue)}</Badge>}
        {d?.contractNumber && <Badge classe="bg-blue-50 text-blue-800">contrato {d.contractNumber}</Badge>}
        {d?.complianceScore != null && (
          <Badge classe={d.complianceScore >= 100 ? 'bg-ok-fundo text-ok' : d.complianceScore >= 70 ? 'bg-aviso-fundo text-aviso' : 'bg-perigo-fundo text-perigo'}>
            compliance {d.complianceScore}
          </Badge>
        )}
        {penalidades.map((p) => (
          <span key={p.code} className="text-aviso" title={p.evidence}>· {p.label} (-{Math.abs(p.points)})</span>
        ))}
        {q.managerApproval && (
          <span className="sub">· Nível 1 por {q.managerApproval.byLabel ?? '—'} em {data(q.managerApproval.at)}</span>
        )}
      </div>

      {/* a decisão */}
      <div className="mt-3 flex flex-wrap items-center gap-2">
        {conflito ? (
          <Aviso testid="conflito-segregacao">{conflito}</Aviso>
        ) : alcada && (
          <>
            <button type="button" className="botao" onClick={() => aoDecidir(alcada, 'APROVAR')}>Aprovar</button>
            <button type="button" className="botao-secundario" onClick={() => aoDecidir(alcada, 'AJUSTES')}>Solicitar ajustes</button>
            <button type="button" className="botao-perigo" onClick={() => aoDecidir(alcada, 'REJEITAR')}>Rejeitar</button>
          </>
        )}
        <Link className="ml-auto text-[13px] font-semibold text-marca hover:underline" to={linkDoProcesso(q.id)}>Ver processo completo →</Link>
      </div>
    </li>
  );
}
