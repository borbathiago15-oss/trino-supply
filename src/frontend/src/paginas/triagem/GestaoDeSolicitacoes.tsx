import { useMemo, useState } from 'react';
import {
  designar, designarEmLote, diasNaFila, FAIXAS_AGING, faixaDeAging,
  FILTROS_VAZIOS, listarDemandas, listarResponsaveis, rotuloDoResponsavel,
  type Demanda, type EscopoTriagem, type FiltrosTriagem, type ItemDemanda, type Responsavel,
} from '@/api/triagem';
import { Badge, Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { Campo } from '@/componentes/formulario';
import { DialogoDePrioridade, type Pleito } from './DialogoDePrioridade';
import { useToast } from '@/componentes/Toast';
import { podeTriar } from '@/dominio/papeis';
import { classeDoTom } from '@/dominio/tons';
import { useUsuario } from '@/sessao/SessaoProvider';
import { data, moeda, quantidade } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

/**
 * Uma demanda vira uma linha por item, como no ERP. Sem itens detalhados ela
 * ainda rende uma linha, com o resumo no lugar do produto.
 */
export const linhasDe = (t: Demanda): ItemDemanda[] =>
  t.items.length ? t.items : [{
    id: t.id, sequence: 1, code: null, description: t.summary || '—', size: null,
    quantity: null, unitOfMeasure: null, family: null, quotationNumber: null,
    purchaseOrderNumber: null, processStatusLabel: null, processStatusTone: null, processStatusHint: null,
  }];

/** Quantas demandas caem em cada faixa de tempo na fila. */
export const contarPorFaixa = (itens: Demanda[], agora = Date.now()) =>
  FAIXAS_AGING.map((_, i) => itens.filter((t) => faixaDeAging(t, agora) === i).length);

export function GestaoDeSolicitacoes() {
  const usuario = useUsuario();
  const { avisar } = useToast();
  const podeDesignar = podeTriar(usuario);

  const [filtros, setFiltros] = useState<FiltrosTriagem>(FILTROS_VAZIOS);
  const [faixa, setFaixa] = useState('');
  const [marcadas, setMarcadas] = useState<Record<string, boolean>>({});
  const [responsavelLote, setResponsavelLote] = useState('');
  const [pleito, setPleito] = useState<Pleito | null>(null);

  const { dados, erro, carregando, recarregar } = useCarregar(
    (signal) => listarDemandas(filtros, signal),
    [filtros.escopo, filtros.solicitante, filtros.responsavel, filtros.situacao],
  );
  const equipe = useCarregar(
    async (signal) => (podeDesignar ? listarResponsaveis(signal).catch(() => [] as Responsavel[]) : []),
    [podeDesignar],
  );
  const responsaveis = equipe.dados ?? [];

  const visiveis = useMemo(() => {
    // Só material. A demanda de COMPRA é triada na Torre de Controle, na própria linha
    // do item — as duas telas listando a mesma SC era o que fazia o comprador ter de
    // escolher em qual acreditar. O endpoint continua servindo os dois tipos porque a
    // Torre usa as mesmas chamadas de atribuição; o recorte é de quem lê.
    const todas = (dados?.items ?? []).filter((t) => t.kind === 'MR');
    return faixa === '' ? todas : todas.filter((t) => faixaDeAging(t) === Number(faixa));
  }, [dados, faixa]);
  const porFaixa = useMemo(() => contarPorFaixa(visiveis), [visiveis]);
  const selecionadas = visiveis.filter((t) => marcadas[t.id]);

  const mexerFiltro = (k: keyof FiltrosTriagem) => ({
    value: filtros[k],
    onChange: (e: { target: { value: string } }) => setFiltros((f) => ({ ...f, [k]: e.target.value })),
  });

  function limpar() { setFiltros(FILTROS_VAZIOS); setFaixa(''); setMarcadas({}); }

  async function designarUma(t: Demanda, responsibleId: string) {
    try {
      await designar(t.kind, t.id, responsibleId || null);
      avisar(responsibleId ? 'Demanda designada ao responsável.' : 'Designação removida.');
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao designar a demanda.'), 'erro'); recarregar(); }
  }

  async function designarSelecionadas() {
    if (!selecionadas.length || !responsavelLote) return;
    try {
      const r = await designarEmLote(selecionadas.map((t) => ({ kind: t.kind, id: t.id })), responsavelLote);
      avisar(`${r.assigned} demanda(s) designada(s)` + (r.failed.length ? ` · ${r.failed.length} falhou(aram)` : '') + '.',
        r.failed.length ? 'erro' : 'ok');
      setMarcadas({});
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao designar em lote.'), 'erro'); }
  }

  const abrirPleito = (demanda: Demanda, para: 'URGENT' | 'NORMAL') =>
    setPleito({ id: demanda.id, numero: demanda.number, para });

  return (
    <>
      <Painel titulo="Triagem de Material" acoes={
        dados && <span className="sub">{visiveis.length} de {dados.total} demanda(s)</span>
      }>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-5">
          <Campo id="tri-escopo" rotulo="Escopo">
            <select id="tri-escopo" value={filtros.escopo}
              onChange={(e) => setFiltros((f) => ({ ...f, escopo: e.target.value as EscopoTriagem }))}>
              <option value="TODAS">Todas as demandas</option>
              <option value="NAO_ATRIBUIDAS">Sem responsável</option>
              <option value="MINHAS">Designadas a mim</option>
            </select>
          </Campo>
          <Campo id="tri-solicitante" rotulo="Solicitante">
            <select id="tri-solicitante" {...mexerFiltro('solicitante')}>
              <option value="">Todos</option>
              {(dados?.requesters ?? []).map((r) => <option key={r} value={r}>{r}</option>)}
            </select>
          </Campo>
          <Campo id="tri-responsavel" rotulo="Comprador / responsável">
            <select id="tri-responsavel" {...mexerFiltro('responsavel')}>
              <option value="">Todos</option>
              <option value="SEM">— sem responsável —</option>
              {(dados?.assignees ?? []).map((a) => <option key={a} value={a}>{a}</option>)}
            </select>
          </Campo>
          <Campo id="tri-situacao" rotulo="Situação">
            <select id="tri-situacao" {...mexerFiltro('situacao')}>
              <option value="">Todas</option>
              {(dados?.statuses ?? []).map((s) => <option key={s.key} value={s.key}>{s.label}</option>)}
            </select>
          </Campo>
          <Campo id="tri-faixa" rotulo="Tempo na fila">
            <select id="tri-faixa" value={faixa} onChange={(e) => setFaixa(e.target.value)}>
              <option value="">Todos</option>
              {FAIXAS_AGING.map((f, i) => <option key={f.rotulo} value={i}>{f.rotulo}</option>)}
            </select>
          </Campo>
        </div>
        <div className="mt-3 flex flex-wrap items-center gap-2">
          <button type="button" className="botao-secundario" onClick={limpar}>Limpar filtros</button>
          {porFaixa.map((n, i) => n > 0 && (
            <Badge key={FAIXAS_AGING[i].rotulo} classe={FAIXAS_AGING[i].classe}>
              {FAIXAS_AGING[i].rotulo}: {n}
            </Badge>
          ))}
        </div>

        <p className="sub mt-3">
          Toda solicitação de compra e toda solicitação de material entra aqui: designe o responsável
          pela continuidade (comprador ou almoxarife) e acompanhe a situação de cada pedido.
        </p>

        {podeDesignar && (
          <div className="mt-3 flex flex-wrap items-center gap-2 rounded-lg bg-superficie-suave px-3 py-2">
            <span className="sub">Marque demandas e mande todas para:</span>
            <select aria-label="Responsável do lote" className="w-auto" value={responsavelLote}
              onChange={(e) => setResponsavelLote(e.target.value)}>
              <option value="">Escolha o responsável…</option>
              {responsaveis.map((r) => <option key={r.id} value={r.id}>{rotuloDoResponsavel(r)}</option>)}
            </select>
            <button type="button" className="botao" disabled={!selecionadas.length || !responsavelLote}
              onClick={designarSelecionadas}>
              Designar selecionadas ({selecionadas.length})
            </button>
          </div>
        )}

        {erro && <Erro>{erro}</Erro>}
        {carregando && !dados && <Carregando />}
        {dados && !visiveis.length && <Vazio>Nenhuma demanda neste filtro. ✔</Vazio>}

        {visiveis.length > 0 && (
          <div className="mt-3 overflow-x-auto">
            <table data-testid="tabela-demandas" className="min-w-[1180px]">
              <thead>
                <tr>
                  <th>SC / Item</th><th>Produto | Serviço</th><th>Tam.</th><th>Solicitante</th>
                  <th>Centro de custo</th><th>Qtde.</th><th>Emissão</th><th>Necessidade</th>
                  <th>Prioridade</th><th>Comprador</th><th>Status</th>
                </tr>
              </thead>
              <tbody>
                {visiveis.map((t) => {
                  const itens = linhasDe(t);
                  const urgente = t.priority === 'URGENT';
                  const banda = FAIXAS_AGING[faixaDeAging(t)];
                  return itens.map((i, idx) => (
                    <tr key={t.id + '/' + i.id} data-demanda={t.number}
                      className={urgente ? 'text-perigo' : undefined}>
                      <td className="whitespace-nowrap">
                        {idx === 0 && (
                          <div className="flex items-center gap-1.5">
                            {podeDesignar && (
                              <input type="checkbox" className="w-auto" checked={!!marcadas[t.id]}
                                aria-label={`Selecionar ${t.number} para designar em lote`}
                                onChange={(e) => setMarcadas((m) => ({ ...m, [t.id]: e.target.checked }))} />
                            )}
                            <strong>{t.number}</strong>
                          </div>
                        )}
                        <div className="sub">{t.number}/{String(i.sequence || idx + 1).padStart(4, '0')}</div>
                      </td>
                      <td className="min-w-[220px]">
                        {i.code && <span className="sub">[{i.code}] </span>}{i.description}
                        {idx === 0 && t.justification && <div className="sub">{t.justification}</div>}
                      </td>
                      <td>{i.size || '—'}</td>
                      <td>{t.requesterLabel}</td>
                      <td>{t.costCenter}</td>
                      <td className="whitespace-nowrap">
                        {i.quantity != null ? `${quantidade(i.quantity)} ${i.unitOfMeasure ?? ''}`.trim() : '—'}
                      </td>
                      <td className="whitespace-nowrap">
                        {data(t.openedAt)}
                        {idx === 0 && (
                          <div className="mt-0.5">
                            <Badge classe={banda.classe} title={`há ${diasNaFila(t)} dia(s) na fila`}>
                              {banda.rotulo}
                            </Badge>
                          </div>
                        )}
                      </td>
                      <td className="whitespace-nowrap">{data(t.neededBy)}</td>
                      <td className="min-w-[170px]">
                        {urgente
                          ? <>
                              <strong>URGENTE</strong>
                              {t.urgencyReason && <div className="text-[12px]">{t.urgencyReason}</div>}
                              {t.urgencyImpact && <div className="text-[12px]">Impacto: {t.urgencyImpact}</div>}
                            </>
                          : 'Normal'}
                        {idx === 0 && t.priorityChangedByLabel && (
                          <div className="sub">
                            alterada por {t.priorityChangedByLabel}
                            {t.priorityChangeReason ? ` — ${t.priorityChangeReason}` : ''}
                          </div>
                        )}
                        {idx === 0 && podeDesignar && t.kind === 'SC' && (
                          <button type="button" className="botao-secundario mt-1"
                            onClick={() => abrirPleito(t, urgente ? 'NORMAL' : 'URGENT')}>
                            {urgente ? 'Voltar a Normal' : 'Tornar Urgente'}
                          </button>
                        )}
                      </td>
                      {idx === 0 && (
                        <td rowSpan={itens.length} className="min-w-[200px]">
                          {podeDesignar
                            ? <select aria-label={`Responsável por ${t.number}`} value={t.assignedToId ?? ''}
                                onChange={(e) => designarUma(t, e.target.value)}>
                                <option value="">— sem responsável —</option>
                                {responsaveis.map((r) => (
                                  <option key={r.id} value={r.id}>{rotuloDoResponsavel(r)}</option>
                                ))}
                              </select>
                            : (t.assignedToLabel ?? '—')}
                          {t.assignedByLabel && <div className="sub">designado por {t.assignedByLabel}</div>}
                        </td>
                      )}
                      {/* SC separada em processos diferentes: cada item mostra a sua situação */}
                      {t.splitProcesses ? (
                        <td className="min-w-[170px]">
                          <Badge classe={classeDoTom(i.processStatusTone)} title={i.processStatusHint ?? undefined}>
                            {i.processStatusLabel || '—'}
                          </Badge>
                          <div className="sub">
                            {i.quotationNumber || 'sem processo'}
                            {i.purchaseOrderNumber ? ` · O.C. ${i.purchaseOrderNumber}` : ''}
                          </div>
                          {i.family && <div className="sub">{i.family}</div>}
                        </td>
                      ) : idx === 0 ? (
                        <td rowSpan={itens.length} className="min-w-[170px]">
                          <Badge classe={classeDoTom(t.processStatusTone)} title={t.processStatusHint ?? undefined}>
                            {t.processStatusLabel || t.status}
                          </Badge>
                          {t.estimatedValue != null && <div className="sub">{moeda(t.estimatedValue)}</div>}
                        </td>
                      ) : null}
                    </tr>
                  ));
                })}
              </tbody>
            </table>
          </div>
        )}
      </Painel>

      {pleito && (
        <DialogoDePrioridade pleito={pleito} aoFechar={() => setPleito(null)}
          aoSalvar={recarregar} aoAvisar={avisar} />
      )}
    </>
  );
}
