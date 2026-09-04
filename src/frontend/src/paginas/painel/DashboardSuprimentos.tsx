import { useState } from 'react';
import {
  classeDeFaixa, dashboardDeSuprimentos, FILTROS_PAINEL_VAZIOS, variacao,
  type DashboardSuprimentos as Dados, type FiltrosPainel, type PrazoFamilia, type Rankings,
} from '@/api/painel';
import { Badge, Carregando, Erro, Kpi, Painel, Vazio } from '@/componentes/basicos';
import { Campo } from '@/componentes/formulario';
import { CORES, GraficoColunas, Legenda, ListaBarras, moedaCurta, type Serie } from '@/componentes/graficos';
import { podeComprar, podeDecidirSc, temModulo } from '@/dominio/papeis';
import { useUsuario } from '@/sessao/SessaoProvider';
import { moeda, quantidade } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';
import { CentralDeAvisos } from './CentralDeAvisos';

/** Mesma regra do `canSeeSupplyAnalytics()` do legado: papel e módulo. */
export const podeVerAnalises = (u: Parameters<typeof podeComprar>[0]) =>
  (podeDecidirSc(u) || podeComprar(u) || u.role === 'Auditor')
  && (temModulo(u, 'SOLICITACOES') || temModulo(u, 'APROVACAO') || temModulo(u, 'COMPRAS'));

const PAINEIS_RANK: { titulo: string; campo: keyof Rankings; cor: string }[] = [
  { titulo: 'Fornecedores por valor comprado', campo: 'suppliers', cor: CORES[1] },
  { titulo: 'Famílias por valor solicitado', campo: 'families', cor: CORES[3] },
  { titulo: 'Spend por categoria (O.C.s)', campo: 'categories', cor: CORES[2] },
  { titulo: 'Compradores por valor', campo: 'buyers', cor: CORES[2] },
  { titulo: 'Solicitantes por valor', campo: 'requesters', cor: CORES[1] },
  { titulo: 'Regionais por valor solicitado', campo: 'regions', cor: CORES[3] },
  { titulo: 'Gerentes por valor solicitado', campo: 'managers', cor: CORES[2] },
  { titulo: 'Clientes por valor solicitado', campo: 'clients', cor: CORES[1] },
  { titulo: 'Centros de custo por valor', campo: 'costCenters', cor: CORES[3] },
];

function Prazos({ familias }: { familias: PrazoFamilia[] }) {
  if (!familias.length)
    return <Vazio>Defina os prazos-meta em Cadastros → Famílias de Produtos para acompanhar meta × realizado.</Vazio>;
  return (
    <div className="overflow-x-auto">
      <table data-testid="tabela-prazos" className="min-w-[900px]">
        <thead>
          <tr>
            <th>Família</th>
            {familias[0].stages.map((e) => <th key={e.stage}>{e.stage}</th>)}
            <th>Total</th>
          </tr>
        </thead>
        <tbody>
          {familias.map((f) => (
            <tr key={f.family}>
              <td><strong>{f.family}</strong></td>
              {f.stages.map((e) => (
                <td key={e.stage} className="min-w-[150px]">
                  {e.target != null ? `meta ${e.target}d` : <span className="sub">sem meta</span>}
                  <div className={e.late ? 'text-[12px] font-bold text-perigo' : 'sub'}>
                    {e.actual != null ? `real ${e.actual}d${e.late ? ' ⚠' : ''}` : 'sem dado'}
                  </div>
                  {e.median != null && <div className="sub">mediana {e.median}d</div>}
                  {e.withinSlaPct != null && (
                    <div className="mt-0.5">
                      <Badge classe={classeDeFaixa(e.withinSlaPct, 80, 50)}>{e.withinSlaPct}% no prazo</Badge>
                      <span className="sub"> ({e.measured})</span>
                    </div>
                  )}
                </td>
              ))}
              <td className="whitespace-nowrap">
                {f.targetTotal != null ? <strong>meta {f.targetTotal}d</strong> : <span className="sub">—</span>}
                <div className="sub">{f.actualTotal != null ? `real ${f.actualTotal}d` : 'sem dado'}</div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function Analises({ dados }: { dados: Dados }) {
  const k = dados.kpis;
  const v = variacao(k.prCount, k.prPrevCount);
  const meses = dados.months.map((m) => m.month);
  const porSituacao: Serie[] = [
    { nome: 'Aprovadas', cor: CORES[3], valores: dados.months.map((m) => m.approved) },
    { nome: 'Em aprovação', cor: CORES[1], valores: dados.months.map((m) => m.inApproval) },
    { nome: 'Devolvidas', cor: CORES[4], valores: dados.months.map((m) => m.returned) },
    { nome: 'Rejeitadas/Canceladas', cor: CORES[5], valores: dados.months.map((m) => m.rejectedOrCancelled) },
    { nome: 'Rascunho', cor: CORES[0], valores: dados.months.map((m) => m.draft) },
  ];
  const sv = dados.saving;

  return (
    <>
      <div className="mb-4 grid grid-cols-2 gap-3 lg:grid-cols-3 xl:grid-cols-5">
        <Kpi rotulo="Solicitações" valor={quantidade(k.prCount)}
          detalhe={<span className={v.classe}>{v.sinal} {Math.abs(v.pct)}% vs período anterior ({quantidade(k.prPrevCount)})</span>} />
        <Kpi rotulo="Valor solicitado" valor={moedaCurta(k.prTotalValue)} />
        <Kpi rotulo="Aprovadas" valor={quantidade(k.approvedCount)} detalhe={moedaCurta(k.approvedValue)} />
        <Kpi rotulo="Aguardando aprovação" valor={quantidade(k.pendingApproval)} />
        <Kpi rotulo="Em atraso" valor={quantidade(k.overdue)} detalhe="data de necessidade vencida" />
        <Kpi rotulo="Tempo médio de aprovação" valor={k.avgApprovalDays != null ? `${k.avgApprovalDays} d` : '—'} />
        <Kpi rotulo="Pedidos de compra" valor={quantidade(k.poCount)} detalhe={moedaCurta(k.poTotalValue)} />
        <Kpi rotulo="Pedidos em aberto" valor={quantidade(k.poOpen)}
          detalhe={k.poLate > 0 ? `${k.poLate} há +7 dias` : 'nenhum atrasado'} />
        <Kpi rotulo="Tempo médio de entrega" valor={k.avgReceiveDays != null ? `${k.avgReceiveDays} d` : '—'}
          detalhe="emissão → recebimento" />
      </div>

      <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
        <Painel titulo="Solicitações por mês — situação">
          <Legenda series={porSituacao} />
          <GraficoColunas rotulos={meses} series={porSituacao} empilhado
            titulo="Solicitações por mês, por situação" />
        </Painel>
        <Painel titulo="Valor comprado por mês (pedidos)">
          <GraficoColunas rotulos={meses} formatar={moedaCurta} titulo="Valor comprado por mês"
            series={[{ nome: 'Valor comprado', cor: CORES[1], valores: dados.months.map((m) => m.poValue) }]} />
        </Painel>
      </div>

      <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
        {PAINEIS_RANK.map((p) => (
          <Painel key={p.campo} titulo={p.titulo}>
            <ListaBarras linhas={dados.rankings?.[p.campo] ?? []} cor={p.cor} />
          </Painel>
        ))}
      </div>

      <Painel titulo="Ganho de negociação (saving)">
        {!sv?.processes && (
          <Vazio>Nenhuma negociação com ganho registrada no período. O ganho é apurado em Compras → Processos de Cotação.</Vazio>
        )}
        {sv && sv.processes > 0 && (
          <>
            <div className="mb-3 rounded-lg border border-ok/30 bg-ok-fundo px-4 py-3">
              <strong>
                {moeda(sv.total)} economizados em {quantidade(sv.processes)} negociação(ões) — {sv.percent}% sobre {moeda(sv.baseline)}
              </strong>
              <div className="sub">Primeiras propostas {moeda(sv.baseline)} → fechado {moeda(sv.closed)}</div>
              {sv.referenceOrders > 0 && (
                <div className="sub">
                  Saving de referência (× último preço pago): <strong>{moeda(sv.referenceTotal)}</strong> em {quantidade(sv.referenceOrders)} pedido(s)
                </div>
              )}
            </div>
            <div className="overflow-x-auto">
              <table data-testid="tabela-saving" className="min-w-[720px]">
                <thead>
                  <tr><th>Processo</th><th>Fornecedor</th><th>1ª proposta</th><th>Fechado</th><th>Ganho</th><th>Negociado por</th></tr>
                </thead>
                <tbody>
                  {sv.items.map((x) => (
                    <tr key={x.number}>
                      <td>{x.number}</td>
                      <td>{x.supplier || '—'}</td>
                      <td className="whitespace-nowrap">{moeda(x.baseline)}</td>
                      <td className="whitespace-nowrap">{moeda(x.closed)}</td>
                      <td className="whitespace-nowrap"><strong>{moeda(x.value)}</strong></td>
                      <td className="sub">{x.byLabel || '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
      </Painel>

      <Painel titulo="Prazos do processo — meta da família × realizado">
        <p className="sub mb-2">
          A meta vem de Cadastros → Famílias de Produtos. O realizado é medido nos processos do período.
        </p>
        <Prazos familias={dados.leadTimes ?? []} />
      </Painel>

      <Painel titulo="Fornecedor — pedidos no período">
        {!dados.supplierTable?.length && <Vazio>Nenhum pedido de compra no período.</Vazio>}
        {dados.supplierTable?.length > 0 && (
          <div className="overflow-x-auto">
            <table data-testid="tabela-fornecedores" className="min-w-[680px]">
              <thead>
                <tr><th>Fornecedor</th><th>Pedidos</th><th>Em aberto</th><th>Quantidade</th><th>Valor</th><th>OTIF</th></tr>
              </thead>
              <tbody>
                {dados.supplierTable.map((s) => (
                  <tr key={s.supplier}>
                    <td>{s.supplier}</td>
                    <td>{quantidade(s.orders)}</td>
                    <td>{s.open > 0 ? <Badge classe="bg-slate-100 text-slate-600">{s.open}</Badge> : '0'}</td>
                    <td>{quantidade(s.quantity)}</td>
                    <td className="whitespace-nowrap">{moeda(s.value)}</td>
                    <td>
                      {s.otifPercent != null
                        ? <Badge classe={classeDeFaixa(s.otifPercent, 90, 70)} title={`${s.otifMeasured} entrega(s) medida(s)`}>
                            {s.otifPercent}%
                          </Badge>
                        : <span className="sub">sem medição</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Painel>

      <Painel titulo="Painel do comprador">
        <p className="sub mb-2">
          Visão multidimensional do período: processos conduzidos, valor comprado, ganho negociado,
          prazo médio até a O.C., backlog atual e OTIF das entregas.
        </p>
        {!dados.buyerPanel?.length && <Vazio>Nenhum processo de compra no período.</Vazio>}
        {dados.buyerPanel?.length > 0 && (
          <div className="overflow-x-auto">
            <table data-testid="painel-comprador" className="min-w-[900px]">
              <thead>
                <tr>
                  <th>Comprador</th><th>Score</th><th>Processos</th><th>Com O.C.</th><th>Valor comprado</th>
                  <th>Saving</th><th>Dias até a O.C.</th><th>Backlog atual</th><th>OTIF</th>
                </tr>
              </thead>
              <tbody>
                {dados.buyerPanel.map((b) => (
                  <tr key={b.label}>
                    <td>{b.label}</td>
                    <td>
                      {b.compositeScore != null
                        ? <Badge classe={classeDeFaixa(b.compositeScore, 85, 65)}
                            title="OTIF 40 · agilidade 40 · saving 20 — relativo ao período">
                            {b.compositeScore}
                          </Badge>
                        : <span className="sub">—</span>}
                    </td>
                    <td>{quantidade(b.processes)}</td>
                    <td>{quantidade(b.closed)}</td>
                    <td className="whitespace-nowrap">{moeda(b.poValue)}</td>
                    <td className="whitespace-nowrap">
                      {b.savingTotal > 0 ? <strong>{moeda(b.savingTotal)}</strong> : moeda(0)}
                    </td>
                    <td>{b.avgDaysToPo != null ? `${b.avgDaysToPo}d` : <span className="sub">—</span>}</td>
                    <td>{b.backlog > 0 ? <Badge classe="bg-aviso-fundo text-aviso">{quantidade(b.backlog)}</Badge> : '0'}</td>
                    <td>
                      {b.otifPercent != null
                        ? <Badge classe={classeDeFaixa(b.otifPercent, 90, 70)}>{b.otifPercent}%</Badge>
                        : <span className="sub">sem medição</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Painel>
    </>
  );
}

export function DashboardSuprimentos() {
  const usuario = useUsuario();
  const veAnalises = podeVerAnalises(usuario);
  const [rascunho, setRascunho] = useState<FiltrosPainel>(FILTROS_PAINEL_VAZIOS);
  const [aplicados, setAplicados] = useState<FiltrosPainel>(FILTROS_PAINEL_VAZIOS);

  const { dados, erro, carregando } = useCarregar(
    async (signal) => (veAnalises ? dashboardDeSuprimentos(aplicados, signal) : null),
    [aplicados, veAnalises],
  );

  // o período em branco vem resolvido do servidor: mostra o que ele escolheu
  const campo = (k: keyof FiltrosPainel) => ({
    value: rascunho[k] || (k === 'de' ? dados?.from ?? '' : k === 'ate' ? dados?.to ?? '' : ''),
    onChange: (e: { target: { value: string } }) => setRascunho((f) => ({ ...f, [k]: e.target.value })),
  });
  const fo = dados?.filterOptions;

  function limpar() { setRascunho(FILTROS_PAINEL_VAZIOS); setAplicados(FILTROS_PAINEL_VAZIOS); }

  return (
    <>
      <CentralDeAvisos />

      {veAnalises && (
        <>
          <Painel titulo="Filtros">
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-5">
              <Campo id="sd-de" rotulo="De"><input id="sd-de" type="date" {...campo('de')} /></Campo>
              <Campo id="sd-ate" rotulo="Até"><input id="sd-ate" type="date" {...campo('ate')} /></Campo>
              <Campo id="sd-fornecedor" rotulo="Fornecedor">
                <select id="sd-fornecedor" {...campo('fornecedor')}>
                  <option value="">Todos</option>
                  {(fo?.suppliers ?? []).map((s) => <option key={s.id} value={s.id}>{s.label}</option>)}
                </select>
              </Campo>
              <Campo id="sd-comprador" rotulo="Comprador">
                <select id="sd-comprador" {...campo('comprador')}>
                  <option value="">Todos</option>
                  {(fo?.buyers ?? []).map((b) => <option key={b.id} value={b.id}>{b.label}</option>)}
                </select>
              </Campo>
              <Campo id="sd-solicitante" rotulo="Solicitante">
                <select id="sd-solicitante" {...campo('solicitante')}>
                  <option value="">Todos</option>
                  {(fo?.requesters ?? []).map((r) => <option key={r.id} value={r.id}>{r.label}</option>)}
                </select>
              </Campo>
              <Campo id="sd-familia" rotulo="Família">
                <select id="sd-familia" {...campo('familia')}>
                  <option value="">Todas</option>
                  {(fo?.families ?? []).map((f) => <option key={f} value={f}>{f}</option>)}
                </select>
              </Campo>
              <Campo id="sd-cc" rotulo="Centro de custo">
                <select id="sd-cc" {...campo('centroCusto')}>
                  <option value="">Todos</option>
                  {(fo?.costCenters ?? []).map((c) => (
                    <option key={c.code} value={c.code}>{c.code} — {c.name}</option>
                  ))}
                </select>
              </Campo>
              <Campo id="sd-regional" rotulo="Regional">
                <select id="sd-regional" {...campo('regional')}>
                  <option value="">Todas</option>
                  {(fo?.regions ?? []).map((r) => <option key={r} value={r}>{r}</option>)}
                </select>
              </Campo>
              <Campo id="sd-gerente" rotulo="Gerente">
                <select id="sd-gerente" {...campo('gerente')}>
                  <option value="">Todos</option>
                  {(fo?.managers ?? []).map((m) => <option key={m} value={m}>{m}</option>)}
                </select>
              </Campo>
              <Campo id="sd-cliente" rotulo="Cliente">
                <select id="sd-cliente" {...campo('cliente')}>
                  <option value="">Todos</option>
                  {(fo?.clients ?? []).map((c) => <option key={c} value={c}>{c}</option>)}
                </select>
              </Campo>
            </div>
            <div className="mt-3 flex flex-wrap gap-2">
              <button type="button" className="botao" onClick={() => setAplicados(rascunho)}>Aplicar filtros</button>
              <button type="button" className="botao-secundario" onClick={limpar}>Limpar</button>
            </div>
          </Painel>

          {erro && <Painel><Erro>{erro}</Erro></Painel>}
          {carregando && !dados && <Painel><Carregando texto="Apurando o período…" /></Painel>}
          {dados && <Analises dados={dados} />}
        </>
      )}
    </>
  );
}
