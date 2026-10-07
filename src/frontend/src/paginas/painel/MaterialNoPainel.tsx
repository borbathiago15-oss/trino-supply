import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import {
  analyticsDeMaterial, FILTROS_MATERIAL_VAZIOS, type FiltrosMaterial, type RelatorioDeMaterial,
} from '@/api/material';
import { variacao } from '@/api/painel';
import { Badge, Carregando, Erro, Kpi, Painel, Vazio } from '@/componentes/basicos';
import { Campo } from '@/componentes/formulario';
import { CORES, GraficoColunas, ListaBarras, rotuloDoMes, type Serie } from '@/componentes/graficos';
import { TabelaResponsiva } from '@/componentes/TabelaResponsiva';
import { moeda, quantidade } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';

const dias = (v: number | null) => (v == null ? '—' : `${v.toLocaleString('pt-BR')} d`);
const horas = (v: number | null) => (v == null ? '—' : `${v.toLocaleString('pt-BR')} h`);
const pct = (v: number | null) => (v == null ? '—' : `${v.toLocaleString('pt-BR')}%`);
const inteiro = (v: number) => quantidade(v);
const valor = (v: number | null | undefined) => (v == null ? '—' : moeda(v));

/**
 * A solicitação de material ao almoxarifado, no Dashboard e na Visão da diretoria.
 *
 * É o outro cano da casa, ao lado da compra: o material que já está em estoque e sai pela porta
 * do almoxarifado. Os filtros são **próprios** (período, centro, família, produto), porque os do
 * painel de compras falam de fornecedor, comprador e prioridade, que o pedido de material não tem.
 *
 * `compacto` é a versão da diretoria: sem formulário, com o período e o centro que a página já
 * escolheu, quatro números e os centros que mais pedem. O número é o mesmo nos dois lugares —
 * sai da mesma rota — e é isso que deixa a diretoria perguntar ao almoxarifado o que o Dashboard
 * respondeu.
 */
export function MaterialNoPainel({ compacto = false, de = '', ate = '', centroCusto = '' }: {
  compacto?: boolean; de?: string; ate?: string; centroCusto?: string;
}) {
  const inicial: FiltrosMaterial = { ...FILTROS_MATERIAL_VAZIOS, de, ate, centroCusto };
  const [rascunho, setRascunho] = useState<FiltrosMaterial>(inicial);
  const [aplicados, setAplicados] = useState<FiltrosMaterial>(inicial);
  // no modo compacto o recorte é o da página: período e centro chegam por props e mandam
  const filtros: FiltrosMaterial = compacto ? { ...FILTROS_MATERIAL_VAZIOS, de, ate, centroCusto } : aplicados;
  const chaveDosFiltros = JSON.stringify(filtros);
  const { dados, erro, carregando } = useCarregar((signal) => analyticsDeMaterial(filtros, signal), [chaveDosFiltros]);

  const campo = (k: keyof FiltrosMaterial) => ({
    value: rascunho[k] || (k === 'de' ? dados?.from ?? '' : k === 'ate' ? dados?.to ?? '' : ''),
    onChange: (e: { target: { value: string } }) => setRascunho((f) => ({ ...f, [k]: e.target.value })),
  });
  function aplicar(ev: FormEvent) { ev.preventDefault(); setAplicados(rascunho); }
  function limpar() { setRascunho(FILTROS_MATERIAL_VAZIOS); setAplicados(FILTROS_MATERIAL_VAZIOS); }
  // filtro cruzado: tocar a barra aplica o filtro daquela linha; tocar de novo o tira
  function filtrar(campo: 'centroCusto' | 'familia', valor: string) {
    const novo = { ...aplicados, [campo]: aplicados[campo] === valor ? '' : valor };
    setAplicados(novo); setRascunho(novo);
  }

  const titulo = 'Solicitações de material ao almoxarifado';
  if (erro) return <Painel titulo={titulo}><Erro>{erro}</Erro></Painel>;
  const r: RelatorioDeMaterial | undefined = dados ?? undefined;

  if (compacto) {
    return (
      <Painel titulo={titulo} id="material-na-diretoria"
        acoes={(
          <>
            <Link to="/estoque/atendimentos" className="botao-secundario">Painel de Atendimentos →</Link>
            <Link to="/relatorios/material" className="botao-secundario">Relatório completo →</Link>
          </>
        )}>
        {carregando && !r && <Carregando texto="Apurando o almoxarifado…" />}
        {r && <ResumoCompacto r={r} />}
      </Painel>
    );
  }

  return (
    <div data-testid="material-no-painel">
      <Painel titulo={titulo}>
        <p className="sub mb-3">
          O material que já está em estoque e sai pelo almoxarifado, sem compra. Os filtros são deste bloco:
          período, centro de custo, família e produto.
        </p>
        <form onSubmit={aplicar} className="grid grid-cols-2 gap-3 lg:grid-cols-5" data-testid="filtros-material">
          <Campo id="mat-de" rotulo="De"><input id="mat-de" type="date" {...campo('de')} /></Campo>
          <Campo id="mat-ate" rotulo="Até"><input id="mat-ate" type="date" {...campo('ate')} /></Campo>
          <Campo id="mat-cc" rotulo="Centro de custo">
            <select id="mat-cc" {...campo('centroCusto')}>
              <option value="">Todos</option>
              {(r?.filterOptions?.costCenters ?? []).map((c) => <option key={c.code} value={c.code}>{c.code} — {c.name}</option>)}
            </select>
          </Campo>
          <Campo id="mat-familia" rotulo="Família">
            <select id="mat-familia" {...campo('familia')}>
              <option value="">Todas</option>
              {(r?.filterOptions?.families ?? []).map((f) => <option key={f} value={f}>{f}</option>)}
            </select>
          </Campo>
          <Campo id="mat-produto" rotulo="Produto" dica="(nome ou código)">
            <input id="mat-produto" placeholder="ex.: bota, 24001" {...campo('produto')} />
          </Campo>
          <div className="col-span-2 flex flex-wrap items-center gap-2 lg:col-span-5">
            <button type="submit" className="botao">Aplicar ao material</button>
            <button type="button" className="botao-secundario" onClick={limpar}>Tirar os filtros do material</button>
            {r && (
              <span className="sub">Período: {r.from.split('-').reverse().join('/')} a {r.to.split('-').reverse().join('/')}.</span>
            )}
          </div>
        </form>
      </Painel>

      {carregando && !r && <Painel><Carregando texto="Apurando o almoxarifado…" /></Painel>}
      {r && <Analises r={r} filtros={aplicados} aoFiltrar={filtrar} />}
    </div>
  );
}

function ResumoCompacto({ r }: { r: RelatorioDeMaterial }) {
  const k = r.kpis;
  const pendentes = k.awaitingApproval + k.inWarehouseQueue;
  const def = (chave: string) => r.indicators?.[chave];
  return (
    <>
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4" data-testid="numeros-de-material">
        <Kpi rotulo="Pendentes hoje" valor={inteiro(pendentes)}
          detalhe={`${inteiro(k.awaitingApproval)} aguardando aprovação · ${inteiro(k.inWarehouseQueue)} na fila do estoque`}
          definicao={def('awaitingApproval')} />
        <Kpi rotulo="Atendidas no período" valor={inteiro(k.fulfilled)}
          detalhe={`de ${inteiro(k.requested)} solicitada(s) · ${inteiro(k.partial)} parcial(is)`} definicao={def('fulfilled')} />
        <Kpi rotulo="Tempo do pedido à entrega" valor={dias(k.avgTotalDays)}
          detalhe={k.avgFulfillDays != null ? `${dias(k.avgFulfillDays)} no almoxarifado` : 'sem atendimento medido'} definicao={def('avgTotalDays')} />
        <Kpi rotulo="Atendidas no prazo" valor={pct(k.slaMetPercent)}
          detalhe={k.slaBreachedOpen > 0 ? `${inteiro(k.slaBreachedOpen)} na fila com prazo estourado` : 'nada estourado na fila'}
          definicao={def('slaMetPercent')} />
      </div>
      {/* o dinheiro do período, pelo custo congelado no pedido; sem custo é dito, não zero */}
      <p className="sub mt-3" data-testid="valores-de-material">
        Valor pedido no período: <strong>{valor(k.requestedValue)}</strong> · entregue: <strong>{valor(k.deliveredValue)}</strong>
        {(k.itemsWithoutPrice ?? 0) > 0 && <> · {inteiro(k.itemsWithoutPrice ?? 0)} item(ns) sem custo cadastrado</>}
      </p>
      <div className="mt-4 grid grid-cols-1 gap-4 md:grid-cols-2">
        <div>
          <p className="rotulo mb-1.5">Centros que mais pedem</p>
          <ListaBarras linhas={r.byCostCenter.slice(0, 5).map((l) => ({ label: l.label, key: l.key, value: l.count }))} formatar={inteiro} cor={CORES[1]} />
        </div>
        <div>
          <p className="rotulo mb-1.5">Famílias mais pedidas</p>
          <ListaBarras linhas={r.byFamily.slice(0, 5).map((l) => ({ label: l.label, key: l.key, value: l.qty }))} formatar={inteiro} cor={CORES[4]} />
        </div>
      </div>
    </>
  );
}

function Analises({ r, filtros, aoFiltrar }: {
  r: RelatorioDeMaterial; filtros: FiltrosMaterial; aoFiltrar: (campo: 'centroCusto' | 'familia', valor: string) => void;
}) {
  const k = r.kpis;
  const v = variacao(k.requested, k.requestedPrev);
  const def = (chave: string) => r.indicators?.[chave];
  const meses = r.months.map((m) => m.month);
  const series: Serie[] = [
    { nome: 'Solicitadas', cor: CORES[1], valores: r.months.map((m) => m.requested) },
    { nome: 'Atendidas', cor: CORES[3], valores: r.months.map((m) => m.fulfilled) },
    { nome: 'Recusadas', cor: CORES[5], valores: r.months.map((m) => m.rejected) },
  ];
  const barras = (linhas: typeof r.byCostCenter, medida: 'count' | 'qty') =>
    linhas.map((l) => ({ label: l.label, key: l.key, value: l[medida] }));

  return (
    <>
      <div className="mb-4 grid grid-cols-2 gap-3 lg:grid-cols-4" data-testid="kpis-material">
        <Kpi rotulo="Solicitadas" valor={inteiro(k.requested)} definicao={def('requested')}
          detalhe={k.requestedPrev > 0 ? <span className={v.classe}>{v.sinal} {v.pct}% vs. período anterior</span> : `${inteiro(k.requestedQty)} itens pedidos`} />
        <Kpi rotulo="Aguardando aprovação" valor={inteiro(k.awaitingApproval)} detalhe="hoje, do responsável do centro" definicao={def('awaitingApproval')} />
        <Kpi rotulo="Na fila do estoque" valor={inteiro(k.inWarehouseQueue)}
          detalhe={k.slaBreachedOpen > 0 ? <span className="text-perigo">{inteiro(k.slaBreachedOpen)} com prazo estourado</span> : 'nenhuma com prazo estourado'}
          definicao={def('inWarehouseQueue')} />
        <Kpi rotulo="Atendidas" valor={inteiro(k.fulfilled)}
          detalhe={`${inteiro(k.partial)} parcial(is) · ${inteiro(k.purchaseRouteItems)} item(ns) foram para compra`} definicao={def('fulfilled')} />
        <Kpi rotulo="Tempo até a aprovação" valor={horas(k.avgApprovalHours)} definicao={def('avgApprovalHours')} />
        <Kpi rotulo="Tempo no almoxarifado" valor={dias(k.avgFulfillDays)} detalhe="da aprovação à entrega" definicao={def('avgFulfillDays')} />
        <Kpi rotulo="Tempo do pedido à entrega" valor={dias(k.avgTotalDays)} definicao={def('avgTotalDays')} />
        <Kpi rotulo="Atendidas no prazo" valor={pct(k.slaMetPercent)}
          detalhe={k.slaMeasured > 0 ? `${inteiro(k.slaMeasured)} atendimento(s) medido(s)` : 'sem atendimento medido'} definicao={def('slaMetPercent')} />
        {/* os valores, pelo custo congelado no dia do pedido: nulo é traço, porque zero diria "de graça" */}
        <Kpi rotulo="Valor pedido" valor={valor(k.requestedValue)}
          detalhe={(k.itemsWithoutPrice ?? 0) > 0 ? <span className="text-perigo">{inteiro(k.itemsWithoutPrice ?? 0)} item(ns) sem custo, fora da soma</span> : 'custo de compra no dia do pedido'} />
        <Kpi rotulo="Valor liberado" valor={valor(k.approvedValue)} detalhe="o que o Nível 1 do centro liberou" />
        <Kpi rotulo="Valor entregue" valor={valor(k.deliveredValue)} detalhe="nas atendidas do período" />
        <Kpi rotulo="Relatório completo" valor={<Link to="/relatorios/material" className="text-[16px] text-marca hover:underline">abrir →</Link>}
          detalhe="todas as solicitações, PDF e planilha" />
      </div>

      <Painel titulo="Solicitações de material por mês">
        {!r.months.some((m) => m.requested + m.fulfilled + m.rejected > 0)
          ? <Vazio>Nenhuma solicitação de material no período.</Vazio>
          : <GraficoColunas rotulos={meses.map(rotuloDoMes)} series={series} titulo="Solicitações de material por mês" />}
      </Painel>

      <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
        <Painel titulo="Top solicitações por centro de custo">
          <ListaBarras linhas={barras(r.byCostCenter, 'count')} formatar={inteiro} cor={CORES[1]}
            marcada={filtros.centroCusto || undefined} aoClicar={(l) => aoFiltrar('centroCusto', l.key ?? l.label)} />
        </Painel>
        <Painel titulo="Solicitações por família — quantidade pedida">
          <ListaBarras linhas={barras(r.byFamily, 'qty')} formatar={inteiro} cor={CORES[4]}
            marcada={filtros.familia || undefined} aoClicar={(l) => aoFiltrar('familia', l.key ?? l.label)} />
        </Painel>
        <Painel titulo="Solicitações por produto — quantidade pedida">
          <ListaBarras linhas={barras(r.byProduct, 'qty')} formatar={inteiro} cor={CORES[2]} />
        </Painel>
        <Painel titulo="Quem mais pede">
          <ListaBarras linhas={barras(r.byRequester, 'count')} formatar={inteiro} cor={CORES[0]} />
        </Painel>
      </div>

      <Painel titulo="Prazo de atendimento por família — meta × realizado">
        <p className="sub mb-2">
          A meta vem de Cadastros → Prazo de atendimento, contada da aprovação do centro. O realizado é das solicitações atendidas no período.
        </p>
        {!r.slaByFamily.length && <Vazio>Nenhum atendimento no período.</Vazio>}
        {r.slaByFamily.length > 0 && (
          <TabelaResponsiva linhas={r.slaByFamily} chave={(f) => f.family} testid="tabela-prazo-material" minLargura={640} colunasNoCard={1} colunas={[
            { titulo: 'Família', principal: true, render: (f) => f.family },
            { titulo: 'Meta', render: (f) => (f.maxDays != null ? `${f.maxDays} d` : <span className="sub">sem cobrança</span>) },
            { titulo: 'Realizado (média)', render: (f) => dias(f.avgDays) },
            { titulo: 'Atendidas', render: (f) => inteiro(f.measured) },
            { titulo: 'No prazo', render: (f) => (f.maxDays == null ? <span className="sub">—</span>
              : <Badge classe={f.met === f.measured ? 'bg-ok-fundo text-ok' : f.met * 2 >= f.measured ? 'bg-aviso-fundo text-aviso' : 'bg-perigo-fundo text-perigo'}>
                  {inteiro(f.met)} de {inteiro(f.measured)}
                </Badge>) },
          ]} />
        )}
      </Painel>
    </>
  );
}
