import { useState } from 'react';
import {
  classeDeFaixa, dashboardDeSuprimentos, FILTROS_PAINEL_VAZIOS, filtrosEscondidosAtivos, variacao,
  type DashboardSuprimentos as Dados, type FiltrosPainel, type OpcoesFiltro, type PrazoFamilia, type Rankings,
} from '@/api/painel';
import { Badge, Carregando, Erro, Kpi, Painel, Vazio } from '@/componentes/basicos';
import { Campo } from '@/componentes/formulario';
import { CORES, GraficoColunas, Legenda, ListaBarras, moedaCurta, type Serie } from '@/componentes/graficos';
import { podeVerAnalises } from '@/dominio/papeis';
import { useAvisos } from '@/sessao/AvisosProvider';
import { useUsuario } from '@/sessao/SessaoProvider';
import { moeda, quantidade } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';
import { LinhaDaMeta } from '@/componentes/LinhaDaMeta';
import { TabelaResponsiva } from '@/componentes/TabelaResponsiva';
import { Link } from 'react-router-dom';
import { pontosDeAtencao } from './pontosDeAtencao';
import { CentralDeAvisos } from './CentralDeAvisos';
import { TrilhaDoProcesso } from './TrilhaDoProcesso';

const ROTULO_PRIORIDADE: Record<string, string> = { NORMAL: 'Normal', URGENT: 'Urgente', LOW: 'Baixa', HIGH: 'Alta' };

const ROTULO_FILTRO: Record<keyof FiltrosPainel, string> = {
  de: 'De', ate: 'Até', empresa: 'Empresa', centroCusto: 'Centro de custo', fornecedor: 'Fornecedor',
  comprador: 'Comprador', solicitante: 'Solicitante', prioridade: 'Prioridade', categoria: 'Categoria',
  familia: 'Família', regional: 'Regional', gerente: 'Gerente', cliente: 'Cliente',
};

/**
 * Os filtros que estão valendo, com o nome no lugar do identificador. As datas ficam de fora:
 * o período está sempre à vista nos dois primeiros campos.
 */
export function chipsDosFiltros(f: FiltrosPainel, fo?: OpcoesFiltro) {
  const nome = (k: keyof FiltrosPainel, v: string) => {
    if (k === 'fornecedor') return fo?.suppliers.find((x) => x.id === v)?.label ?? v;
    if (k === 'comprador') return fo?.buyers.find((x) => x.id === v)?.label ?? v;
    if (k === 'solicitante') return fo?.requesters.find((x) => x.id === v)?.label ?? v;
    if (k === 'centroCusto') return fo?.costCenters.find((x) => x.code === v)?.name ?? v;
    if (k === 'prioridade') return ROTULO_PRIORIDADE[v] ?? v;
    return v;
  };
  return (Object.keys(f) as (keyof FiltrosPainel)[])
    .filter((k) => k !== 'de' && k !== 'ate' && f[k] !== '')
    .map((k) => ({ campo: k, rotulo: ROTULO_FILTRO[k], valor: nome(k, f[k]) }));
}

// cada ranking sabe qual filtro global o clique na sua barra aplica (filtro cruzado)
const PAINEIS_RANK: { titulo: string; campo: keyof Rankings; cor: string; filtro: keyof FiltrosPainel }[] = [
  { titulo: 'Fornecedores por valor comprado', campo: 'suppliers', cor: CORES[1], filtro: 'fornecedor' },
  { titulo: 'Famílias por valor solicitado', campo: 'families', cor: CORES[3], filtro: 'familia' },
  { titulo: 'Spend por categoria (O.C.s)', campo: 'categories', cor: CORES[2], filtro: 'categoria' },
  { titulo: 'Compradores por valor', campo: 'buyers', cor: CORES[2], filtro: 'comprador' },
  { titulo: 'Solicitantes por valor', campo: 'requesters', cor: CORES[1], filtro: 'solicitante' },
  { titulo: 'Regionais por valor solicitado', campo: 'regions', cor: CORES[3], filtro: 'regional' },
  { titulo: 'Gerentes por valor solicitado', campo: 'managers', cor: CORES[2], filtro: 'gerente' },
  { titulo: 'Clientes por valor solicitado', campo: 'clients', cor: CORES[1], filtro: 'cliente' },
  { titulo: 'Centros de custo por valor', campo: 'costCenters', cor: CORES[3], filtro: 'centroCusto' },
];

/** O mês do gráfico (`2026-09`) como período de filtro: do primeiro ao último dia. */
export function periodoDoMes(mes: string): { de: string; ate: string } {
  const [ano, m] = mes.split('-').map(Number);
  const ultimo = new Date(Date.UTC(ano, m, 0)).getUTCDate();
  return { de: `${mes}-01`, ate: `${mes}-${String(ultimo).padStart(2, '0')}` };
}

function Prazos({ familias }: { familias: PrazoFamilia[] }) {
  if (!familias.length)
    return <Vazio>Defina os prazos-meta em Cadastros → Famílias de Produtos para acompanhar meta × realizado.</Vazio>;
  const etapa = (f: PrazoFamilia, i: number) => {
    const e = f.stages[i];
    if (!e) return <span className="sub">—</span>;
    return (
      <div className="md:min-w-[150px]">
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
      </div>
    );
  };
  return (
    <TabelaResponsiva linhas={familias} chave={(f) => f.family} testid="tabela-prazos" minLargura={900} colunasNoCard={1} colunas={[
      { titulo: 'Família', principal: true, render: (f) => <strong>{f.family}</strong> },
      ...familias[0].stages.map((e, i) => ({ titulo: e.stage, render: (f: PrazoFamilia) => etapa(f, i) })),
      { titulo: 'Total', classe: 'whitespace-nowrap', render: (f) => (
        <>
          {f.targetTotal != null ? <strong>meta {f.targetTotal}d</strong> : <span className="sub">—</span>}
          <div className="sub">{f.actualTotal != null ? `real ${f.actualTotal}d` : 'sem dado'}</div>
        </>
      ) },
    ]} />
  );
}

function Analises({ dados, filtros, aoFiltrar, aoFiltrarMes }: {
  dados: Dados;
  filtros: FiltrosPainel;
  aoFiltrar: (campo: keyof FiltrosPainel, valor: string) => void;
  aoFiltrarMes: (mes: string) => void;
}) {
  const k = dados.kpis;
  const pontos = pontosDeAtencao(dados);
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
  const def = (chave: string) => dados.indicators?.[chave];
  const meta = (chave: string, formatar: (v: number) => string) =>
    <LinhaDaMeta comparacao={dados.goals?.[chave]} formatar={formatar} />;
  const dias = (v: number) => `${v.toLocaleString('pt-BR')} d`;

  return (
    <>
      <div className="mb-4 grid grid-cols-2 gap-3 lg:grid-cols-3 xl:grid-cols-5" data-testid="kpis-painel">
        <Kpi rotulo="Solicitações" valor={quantidade(k.prCount)} definicao={def('prCount')}
          detalhe={k.prPrevCount > 0
            ? <span className={v.classe}>{v.sinal} {Math.abs(v.pct)}% vs período anterior ({quantidade(k.prPrevCount)})</span>
            // sem base anterior a variação não é calculável — "▲ 100%" diria um crescimento que ninguém mediu
            : <span className="sub">sem base no período anterior</span>} />
        <Kpi rotulo="Valor solicitado" valor={moedaCurta(k.prTotalValue)} definicao={def('prTotalValue')} />
        <Kpi rotulo="Aprovadas" valor={quantidade(k.approvedCount)} detalhe={moedaCurta(k.approvedValue)}
          definicao={def('approvedCount')} />
        <Kpi rotulo="Aguardando aprovação" valor={quantidade(k.pendingApproval)} definicao={def('pendingApproval')} />
        <Kpi rotulo="Em atraso" valor={quantidade(k.overdue)} detalhe="data de necessidade vencida"
          definicao={def('overdue')} meta={meta('overdue', quantidade)} />
        <Kpi rotulo="Tempo médio de aprovação" valor={k.avgApprovalDays != null ? `${k.avgApprovalDays} d` : '—'}
          definicao={def('avgApprovalDays')} meta={meta('avgApprovalDays', dias)} />
        <Kpi rotulo="Valor comprado" valor={moedaCurta(k.poTotalValue)} detalhe={`${quantidade(k.poCount)} pedido(s) · pela data da aprovação`}
          definicao={def('poTotalValue')} meta={meta('poTotalValue', moedaCurta)} />
        <Kpi rotulo="Pedidos em aberto" valor={quantidade(k.poOpen)}
          detalhe={k.poLate > 0 ? `${k.poLate} há +7 dias` : 'nenhum atrasado'} definicao={def('poOpen')} />
        <Kpi rotulo="Tempo médio de entrega" valor={k.avgReceiveDays != null ? `${k.avgReceiveDays} d` : '—'}
          detalhe="aprovação → recebimento" definicao={def('avgReceiveDays')} meta={meta('avgReceiveDays', dias)} />
      </div>

      <Painel titulo="Pontos de atenção">
        {pontos.length === 0
          ? <p className="sub" data-testid="sem-pontos">Nada chama atenção no período filtrado.</p>
          : (
            <ul className="flex flex-col gap-1.5" data-testid="pontos-de-atencao">
              {pontos.map((p) => {
                const classe = `block rounded-lg border px-3 py-2.5 text-[13.5px] ${p.tom === 'perigo'
                  ? 'border-perigo/30 bg-perigo-fundo text-perigo' : 'border-aviso/30 bg-aviso-fundo text-aviso'}`;
                return (
                  <li key={p.chave}>
                    {/* o alvo é a linha inteira, com altura de toque: no celular é onde o dedo cai */}
                    {p.destino
                      ? <Link to={p.destino} className={`${classe} hover:brightness-95`}>{p.texto} <span aria-hidden>→</span></Link>
                      : <div className={classe}>{p.texto}</div>}
                  </li>
                );
              })}
            </ul>
          )}
      </Painel>

      <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
        <Painel titulo="Solicitações por mês — situação">
          <Legenda series={porSituacao} />
          <GraficoColunas rotulos={meses} series={porSituacao} empilhado
            titulo="Solicitações por mês, por situação" aoClicarRotulo={aoFiltrarMes} />
        </Painel>
        <Painel titulo="Valor comprado por mês — pela data da aprovação">
          <GraficoColunas rotulos={meses} formatar={moedaCurta} titulo="Valor comprado por mês" aoClicarRotulo={aoFiltrarMes}
            series={[{ nome: 'Valor comprado', cor: CORES[1], valores: dados.months.map((m) => m.poValue) }]} />
        </Painel>
      </div>

      <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
        {PAINEIS_RANK.map((p) => (
          <Painel key={p.campo} titulo={p.titulo}>
            {/* filtro cruzado: tocar a barra aplica o filtro daquela linha em todo o painel */}
            <ListaBarras linhas={dados.rankings?.[p.campo] ?? []} cor={p.cor} marcada={filtros[p.filtro] || undefined}
              aoClicar={(l) => aoFiltrar(p.filtro, l.key ?? l.label)} />
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
              {meta('saving', moeda)}
              {sv.referenceOrders > 0 && (
                <div className="sub">
                  Saving de referência (× último preço pago): <strong>{moeda(sv.referenceTotal)}</strong> em {quantidade(sv.referenceOrders)} pedido(s)
                </div>
              )}
            </div>
            <TabelaResponsiva linhas={sv.items} chave={(x) => x.number} testid="tabela-saving" minLargura={720} colunas={[
              { titulo: 'Processo', principal: true, render: (x) => x.number },
              { titulo: 'Fornecedor', render: (x) => x.supplier || '—' },
              { titulo: '1ª proposta', classe: 'whitespace-nowrap', render: (x) => moeda(x.baseline) },
              { titulo: 'Fechado', classe: 'whitespace-nowrap', render: (x) => moeda(x.closed) },
              { titulo: 'Ganho', classe: 'whitespace-nowrap', render: (x) => <strong>{moeda(x.value)}</strong> },
              { titulo: 'Negociado por', classe: 'sub', render: (x) => x.byLabel || '—' },
            ]} />
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
          <TabelaResponsiva linhas={dados.supplierTable} chave={(x) => x.supplier} testid="tabela-fornecedores" minLargura={680} colunas={[
            { titulo: 'Fornecedor', principal: true, render: (x) => x.supplier },
            { titulo: 'Pedidos', render: (x) => quantidade(x.orders) },
            { titulo: 'Em aberto', render: (x) => (x.open > 0 ? <Badge classe="bg-superficie-forte text-texto-suave">{x.open}</Badge> : '0') },
            { titulo: 'Quantidade', render: (x) => quantidade(x.quantity) },
            { titulo: 'Valor', classe: 'whitespace-nowrap', render: (x) => moeda(x.value) },
            { titulo: 'OTIF', render: (x) => (x.otifPercent != null
              ? <Badge classe={classeDeFaixa(x.otifPercent, 90, 70)} title={`${x.otifMeasured} entrega(s) medida(s)`}>{x.otifPercent}%</Badge>
              : <span className="sub">sem medição</span>) },
          ]} />
        )}
      </Painel>

      <Painel titulo="Painel do comprador">
        <p className="sub mb-2">
          Visão multidimensional do período: processos conduzidos, valor comprado, ganho negociado,
          prazo médio até a O.C., backlog atual e OTIF das entregas.
        </p>
        {!dados.buyerPanel?.length && <Vazio>Nenhum processo de compra no período.</Vazio>}
        {dados.buyerPanel?.length > 0 && (
          <TabelaResponsiva linhas={dados.buyerPanel} chave={(b) => b.label} testid="painel-comprador" minLargura={900} colunas={[
            { titulo: 'Comprador', principal: true, render: (b) => b.label },
            { titulo: 'Score', render: (b) => (b.compositeScore != null
              ? <Badge classe={classeDeFaixa(b.compositeScore, 85, 65)} title="OTIF 40 · agilidade 40 · saving 20 — relativo ao período">{b.compositeScore}</Badge>
              : <span className="sub">—</span>) },
            { titulo: 'Processos', render: (b) => quantidade(b.processes) },
            { titulo: 'Com O.C.', render: (b) => quantidade(b.closed) },
            { titulo: 'Valor comprado', classe: 'whitespace-nowrap', render: (b) => moeda(b.poValue) },
            { titulo: 'Saving', classe: 'whitespace-nowrap', render: (b) => (b.savingTotal > 0 ? <strong>{moeda(b.savingTotal)}</strong> : moeda(0)) },
            { titulo: 'Dias até a O.C.', render: (b) => (b.avgDaysToPo != null ? `${b.avgDaysToPo}d` : <span className="sub">—</span>) },
            { titulo: 'Backlog atual', render: (b) => (b.backlog > 0 ? <Badge classe="bg-aviso-fundo text-aviso">{quantidade(b.backlog)}</Badge> : '0') },
            { titulo: 'OTIF', render: (b) => (b.otifPercent != null
              ? <Badge classe={classeDeFaixa(b.otifPercent, 90, 70)}>{b.otifPercent}%</Badge>
              : <span className="sub">sem medição</span>) },
          ]} />
        )}
      </Painel>
    </>
  );
}

export function DashboardSuprimentos() {
  const usuario = useUsuario();
  const { avisos } = useAvisos();
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
  // o chip é do filtro que está valendo: tirá-lo refaz a consulta na hora
  function remover(k: keyof FiltrosPainel) {
    const sem = { ...aplicados, [k]: '' };
    setAplicados(sem); setRascunho(sem);
  }
  const [maisFiltros, setMaisFiltros] = useState(false);
  // filtro cruzado: o toque na barra aplica na hora, sem passar pelo "Aplicar" — a barra é a escolha
  function filtrar(campo: keyof FiltrosPainel, valor: string) {
    const novo = { ...aplicados, [campo]: aplicados[campo] === valor ? '' : valor };
    setAplicados(novo); setRascunho(novo);
  }
  function filtrarMes(mes: string) {
    const novo = { ...aplicados, ...periodoDoMes(mes) };
    setAplicados(novo); setRascunho(novo);
  }
  const escondidos = filtrosEscondidosAtivos(aplicados);
  const chips = chipsDosFiltros(aplicados, fo);

  return (
    <>
      <TrilhaDoProcesso avisos={avisos} usuario={usuario} />
      <CentralDeAvisos />

      {veAnalises && (
        <>
          <Painel titulo="Filtros">
            <div className="grid grid-cols-2 gap-3 lg:grid-cols-4" data-testid="filtros-principais">
              <Campo id="sd-de" rotulo="De"><input id="sd-de" type="date" {...campo('de')} /></Campo>
              <Campo id="sd-ate" rotulo="Até"><input id="sd-ate" type="date" {...campo('ate')} /></Campo>
              <Campo id="sd-empresa" rotulo="Empresa">
                <select id="sd-empresa" {...campo('empresa')}>
                  <option value="">Todas</option>
                  {(fo?.companies ?? []).map((c) => <option key={c} value={c}>{c}</option>)}
                </select>
              </Campo>
              <Campo id="sd-cc" rotulo="Centro de custo">
                <select id="sd-cc" {...campo('centroCusto')}>
                  <option value="">Todos</option>
                  {(fo?.costCenters ?? []).map((c) => (
                    <option key={c.code} value={c.code}>{c.name} ({c.code})</option>
                  ))}
                </select>
              </Campo>
            </div>

            {maisFiltros && (
              <div className="mt-3 grid grid-cols-1 gap-3 border-t border-borda-suave pt-3 sm:grid-cols-2 lg:grid-cols-4"
                data-testid="mais-filtros">
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
                <Campo id="sd-prioridade" rotulo="Prioridade">
                  <select id="sd-prioridade" {...campo('prioridade')}>
                    <option value="">Todas</option>
                    {(fo?.priorities ?? []).map((x) => <option key={x} value={x}>{ROTULO_PRIORIDADE[x] ?? x}</option>)}
                  </select>
                </Campo>
                <Campo id="sd-categoria" rotulo="Categoria">
                  <select id="sd-categoria" {...campo('categoria')}>
                    <option value="">Todas</option>
                    {(fo?.categories ?? []).map((c) => <option key={c} value={c}>{c}</option>)}
                  </select>
                </Campo>
                <Campo id="sd-familia" rotulo="Família">
                  <select id="sd-familia" {...campo('familia')}>
                    <option value="">Todas</option>
                    {(fo?.families ?? []).map((f) => <option key={f} value={f}>{f}</option>)}
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
            )}

            <div className="mt-3 flex flex-wrap items-center gap-2">
              <button type="button" className="botao" onClick={() => setAplicados(rascunho)}>Aplicar filtros</button>
              <button type="button" className="botao-secundario" aria-expanded={maisFiltros}
                onClick={() => setMaisFiltros((m) => !m)}>
                {maisFiltros ? 'Menos filtros' : 'Mais filtros'}
                {escondidos > 0 && <span className="ml-1.5 rounded-full bg-marca px-1.5 text-[11px] text-white"
                  data-testid="contador-filtros">{escondidos}</span>}
              </button>
              <button type="button" className="botao-secundario" onClick={limpar}>Limpar</button>
            </div>

            {chips.length > 0 && (
              <div className="mt-3 flex flex-wrap gap-1.5" data-testid="filtros-ativos">
                {chips.map((c) => (
                  <button key={c.campo} type="button"
                    className="rounded-full border border-borda bg-superficie-suave px-2.5 py-1 text-[12px] hover:border-marca"
                    aria-label={`Remover filtro ${c.rotulo}: ${c.valor}`} onClick={() => remover(c.campo)}>
                    {c.rotulo}: <strong>{c.valor}</strong> ✕
                  </button>
                ))}
              </div>
            )}
          </Painel>

          {erro && <Painel><Erro>{erro}</Erro></Painel>}
          {carregando && !dados && <Painel><Carregando texto="Apurando o período…" /></Painel>}
          {dados && <Analises dados={dados} filtros={aplicados} aoFiltrar={filtrar} aoFiltrarMes={filtrarMes} />}
        </>
      )}
    </>
  );
}
