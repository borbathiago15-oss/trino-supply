import { useState, type FormEvent } from 'react';
import {
  FILTROS_MATERIAL_VAZIOS, fraseDoValor, pdfDoRelatorioDeMaterial, planilhaDoRelatorioDeMaterial, relatorioDeMaterial,
  ROTULO_MATERIAL, type FiltrosMaterial, type LinhaDeMaterial, type RelatorioDeMaterial as Dados,
  type SolicitacaoDoRelatorioDeMaterial,
} from '@/api/material';
import { abrirBlob, salvarBlob } from '@/api/cliente';
import { Badge, Carregando, Erro, Kpi, Painel, Vazio } from '@/componentes/basicos';
import { Campo } from '@/componentes/formulario';
import { TabelaResponsiva, type ColunaResponsiva } from '@/componentes/TabelaResponsiva';
import { useToast } from '@/componentes/Toast';
import { data, moeda, quantidade } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);
const valor = (v: number | null | undefined) => (v == null ? '—' : moeda(v));
const pct = (v: number | null) => (v == null ? '—' : `${v.toLocaleString('pt-BR')}%`);
const dataCurta = (iso: string) => iso.split('-').reverse().join('/');

/** O prazo da linha, dito e pintado: "estourado (4d de 2d)". Sem prazo é traço, não "no prazo". */
export function prazoDaLinha(s: Pick<SolicitacaoDoRelatorioDeMaterial, 'slaStatus' | 'slaDays' | 'slaMaxDays'>): { texto: string; classe: string } {
  if (!s.slaStatus) return { texto: '—', classe: 'sub' };
  const dias = `${s.slaDays}d de ${s.slaMaxDays}d`;
  if (s.slaStatus === 'ESTOURADO') return { texto: `estourado (${dias})`, classe: 'font-semibold text-perigo' };
  if (s.slaStatus === 'ATENCAO') return { texto: `a vencer (${dias})`, classe: 'font-semibold text-aviso' };
  return { texto: `no prazo (${dias})`, classe: 'text-ok' };
}

/**
 * O relatório de solicitações de material: o que a diretoria pediu — todas as solicitações do
 * período, com valores. Os números são os do bloco do Dashboard (a mesma rota de análise, com
 * a lista completa a mais); o PDF e a planilha saem do mesmo recorte, com os filtros no
 * cabeçalho. Os valores são pelo custo de compra congelado no dia do pedido, e "sem custo" é
 * dito em vez de somado como zero.
 */
export function RelatorioDeMaterial() {
  const { avisar } = useToast();
  const [rascunho, setRascunho] = useState<FiltrosMaterial>(FILTROS_MATERIAL_VAZIOS);
  const [aplicados, setAplicados] = useState<FiltrosMaterial>(FILTROS_MATERIAL_VAZIOS);
  const [gerando, setGerando] = useState<'pdf' | 'xlsx' | null>(null);
  const chave = JSON.stringify(aplicados);
  // o relatório anterior fica na tela enquanto o novo recorte vem
  const { dados, erro, carregando } = useCarregar((signal) => relatorioDeMaterial(aplicados, signal), [chave]);

  const campo = (k: keyof FiltrosMaterial) => ({
    value: rascunho[k] || (k === 'de' ? dados?.from ?? '' : k === 'ate' ? dados?.to ?? '' : ''),
    onChange: (e: { target: { value: string } }) => setRascunho((f) => ({ ...f, [k]: e.target.value })),
  });
  function aplicar(ev: FormEvent) { ev.preventDefault(); setAplicados(rascunho); }
  function limpar() { setRascunho(FILTROS_MATERIAL_VAZIOS); setAplicados(FILTROS_MATERIAL_VAZIOS); }

  async function exportarPdf() {
    setGerando('pdf');
    try { avisar('Gerando o PDF do relatório…'); abrirBlob(await pdfDoRelatorioDeMaterial(aplicados)); }
    catch (e) { avisar(mensagem(e, 'Falha ao gerar o PDF.'), 'erro'); }
    finally { setGerando(null); }
  }
  async function baixarPlanilha() {
    setGerando('xlsx');
    try {
      avisar('Gerando a planilha…');
      salvarBlob(await planilhaDoRelatorioDeMaterial(aplicados),
        `solicitacoes-de-material-${dados?.from ?? 'periodo'}_a_${dados?.to ?? 'hoje'}.xlsx`);
    } catch (e) { avisar(mensagem(e, 'Falha ao gerar a planilha.'), 'erro'); }
    finally { setGerando(null); }
  }

  const fo = dados?.filterOptions;
  return (
    <>
      <Painel titulo="Relatório de solicitações de material"
        acoes={(
          <>
            <button type="button" className="botao" onClick={exportarPdf} disabled={!!gerando || !dados}>
              {gerando === 'pdf' ? 'Gerando…' : 'Exportar em PDF'}
            </button>
            <button type="button" className="botao-secundario" onClick={baixarPlanilha} disabled={!!gerando || !dados}>
              {gerando === 'xlsx' ? 'Gerando…' : 'Baixar planilha'}
            </button>
          </>
        )}>
        <p className="sub mb-3">
          Tudo o que foi pedido ao almoxarifado no período, com valores pelo custo de compra do produto no dia do
          pedido. O PDF e a planilha saem com o mesmo recorte; a planilha traz uma linha por item.
        </p>
        <form onSubmit={aplicar} className="grid grid-cols-2 gap-3 lg:grid-cols-6" data-testid="filtros-relatorio-material">
          <Campo id="rm-de" rotulo="De"><input id="rm-de" type="date" {...campo('de')} /></Campo>
          <Campo id="rm-ate" rotulo="Até"><input id="rm-ate" type="date" {...campo('ate')} /></Campo>
          <Campo id="rm-cc" rotulo="Centro de custo">
            <select id="rm-cc" {...campo('centroCusto')}>
              <option value="">Todos</option>
              {(fo?.costCenters ?? []).map((c) => <option key={c.code} value={c.code}>{c.code} — {c.name}</option>)}
            </select>
          </Campo>
          <Campo id="rm-familia" rotulo="Família">
            <select id="rm-familia" {...campo('familia')}>
              <option value="">Todas</option>
              {(fo?.families ?? []).map((f) => <option key={f} value={f}>{f}</option>)}
            </select>
          </Campo>
          <Campo id="rm-produto" rotulo="Produto">
            <input id="rm-produto" placeholder="código ou descrição" {...campo('produto')} />
          </Campo>
          <Campo id="rm-solicitante" rotulo="Solicitante">
            <select id="rm-solicitante" {...campo('solicitante')}>
              <option value="">Todos</option>
              {(fo?.requesters ?? []).map((s) => <option key={s.id} value={s.id}>{s.label}</option>)}
            </select>
          </Campo>
          <div className="col-span-2 flex flex-wrap items-center gap-2 lg:col-span-6">
            <button type="submit" className="botao">Aplicar filtros</button>
            <button type="button" className="botao-secundario" onClick={limpar}>Limpar</button>
            {dados && <span className="sub">Período: {dataCurta(dados.from)} a {dataCurta(dados.to)}.</span>}
          </div>
        </form>
      </Painel>

      {erro && <Painel><Erro>{erro}</Erro></Painel>}
      {carregando && !dados && <Painel><Carregando texto="Apurando o almoxarifado…" /></Painel>}
      {dados && <Blocos r={dados} />}
    </>
  );
}

function Blocos({ r }: { r: Dados }) {
  const k = r.kpis;
  const semCusto = k.itemsWithoutPrice ?? 0;
  return (
    <>
      <div className="mb-4 grid grid-cols-2 gap-3 lg:grid-cols-4" data-testid="kpis-relatorio-material">
        <Kpi rotulo="Solicitadas" valor={quantidade(k.requested)}
          detalhe={`${quantidade(k.requestedQty)} itens pedidos · ${quantidade(k.rejected)} recusada(s) · ${quantidade(k.cancelled)} cancelada(s)`} />
        <Kpi rotulo="Atendidas" valor={quantidade(k.fulfilled)}
          detalhe={`${quantidade(k.partial)} parcial(is) · ${quantidade(k.purchaseRouteItems)} item(ns) foram para compra`} />
        <Kpi rotulo="Pendentes hoje" valor={quantidade(k.awaitingApproval + k.inWarehouseQueue)}
          detalhe={`${quantidade(k.awaitingApproval)} aguardando o centro · ${quantidade(k.inWarehouseQueue)} na fila do estoque`} />
        <Kpi rotulo="Atendidas no prazo" valor={pct(k.slaMetPercent)}
          detalhe={k.slaMeasured > 0 ? `${quantidade(k.slaMeasured)} medida(s) · ${quantidade(k.slaBreachedOpen)} na fila com prazo estourado` : 'sem atendimento medido'} />
        <Kpi rotulo="Valor pedido" valor={valor(k.requestedValue)} detalhe="o que os centros pediram" />
        <Kpi rotulo="Valor liberado" valor={valor(k.approvedValue)} detalhe="o que o Nível 1 liberou" />
        <Kpi rotulo="Valor entregue" valor={valor(k.deliveredValue)} detalhe="o que saiu do almoxarifado" />
        <Kpi rotulo="Itens sem custo" valor={quantidade(semCusto)}
          detalhe={semCusto > 0
            ? <span className="text-perigo">fora dos valores: o produto ainda não tem custo de compra</span>
            : 'todos os itens do período têm custo'} />
      </div>

      <Ranking titulo="1. Por centro de custo" rotulo="Centro de custo" linhas={r.byCostCenter} testid="rm-por-centro" />
      <Ranking titulo="2. Por família" rotulo="Família" linhas={r.byFamily} testid="rm-por-familia" />
      <Ranking titulo="3. Por produto" rotulo="Produto" linhas={r.byProduct} testid="rm-por-produto" />
      <Ranking titulo="4. Por solicitante" rotulo="Solicitante" linhas={r.byRequester} testid="rm-por-solicitante" />

      <Painel titulo="5. Mês a mês">
        {!r.months.some((m) => m.requested + m.fulfilled + m.rejected > 0)
          ? <Vazio>Nenhuma solicitação no período.</Vazio>
          : (
            <TabelaResponsiva testid="rm-meses" linhas={r.months} chave={(m) => m.month} colunas={[
              { titulo: 'Mês', principal: true, render: (m) => m.month },
              { titulo: 'Solicitadas', classe: 'text-right', render: (m) => quantidade(m.requested) },
              { titulo: 'Atendidas', classe: 'text-right', render: (m) => quantidade(m.fulfilled) },
              { titulo: 'Recusadas', classe: 'text-right', render: (m) => quantidade(m.rejected) },
              { titulo: 'Valor pedido', classe: 'text-right whitespace-nowrap', render: (m) => valor(m.requestedValue) },
              { titulo: 'Valor entregue', classe: 'text-right whitespace-nowrap', render: (m) => valor(m.deliveredValue) },
            ]} />
          )}
      </Painel>

      <Painel titulo="6. Prazo de atendimento por família">
        {!r.slaByFamily.length
          ? <Vazio>Sem atendimento medido no período.</Vazio>
          : (
            <TabelaResponsiva testid="rm-prazos" linhas={r.slaByFamily} chave={(p) => p.family} colunas={[
              { titulo: 'Família', principal: true, render: (p) => p.family },
              { titulo: 'Prazo (dias)', classe: 'text-right', render: (p) => p.maxDays ?? '—' },
              { titulo: 'Atendimentos medidos', classe: 'text-right', render: (p) => quantidade(p.measured) },
              { titulo: 'No prazo', classe: 'text-right', render: (p) => (p.maxDays == null ? '—'
                : `${quantidade(p.met)} (${p.measured ? Math.round(p.met * 100 / p.measured) : 0}%)`) },
              { titulo: 'Média (dias)', classe: 'text-right', render: (p) => (p.avgDays == null ? '—' : p.avgDays.toLocaleString('pt-BR')) },
            ]} />
          )}
      </Painel>

      <Painel titulo={`7. Todas as solicitações do período — ${quantidade((r.requisitions ?? []).length)}`}>
        <p className="sub mb-3">
          Da mais recente para a mais antiga. "Sem custo" é item sem custo de compra cadastrado, não zero. Os itens de cada
          solicitação abrem na linha; a planilha os traz um por linha.
        </p>
        {!(r.requisitions ?? []).length
          ? <Vazio>Nenhuma solicitação no período.</Vazio>
          : <TabelaResponsiva testid="rm-solicitacoes" minLargura={1100} colunasNoCard={1}
              linhas={r.requisitions ?? []} chave={(s) => s.id} colunas={COLUNAS_DA_LISTA} />}
      </Painel>
    </>
  );
}

const COLUNAS_DA_LISTA: ColunaResponsiva<SolicitacaoDoRelatorioDeMaterial>[] = [
  { titulo: 'Número', principal: true, classe: 'whitespace-nowrap', render: (s) => (
    <><span className="font-semibold">{s.number}</span><div className="sub">{data(s.createdAt)}</div></>
  ) },
  { titulo: 'Centro de custo', render: (s) => <span title={s.costCenter}>{s.costCenterName}</span> },
  { titulo: 'Solicitante', render: (s) => s.requester },
  { titulo: 'Situação', render: (s) => {
    const marca = ROTULO_MATERIAL[s.status] ?? { rotulo: s.statusLabel, classe: 'bg-superficie-forte text-texto-suave' };
    return (
      <>
        <Badge classe={marca.classe}>{marca.rotulo}</Badge>
        {s.purchaseRequisitionNumber && <div className="sub">faltante: {s.purchaseRequisitionNumber}</div>}
      </>
    );
  } },
  { titulo: 'Itens', render: (s) => <Itens s={s} /> },
  { titulo: 'Valor pedido', classe: 'text-right whitespace-nowrap', render: (s) => fraseDoValor(s.requestedValue, s.itemsWithoutPrice) ?? '—' },
  { titulo: 'Valor entregue', classe: 'text-right whitespace-nowrap', render: (s) => valor(s.deliveredValue) },
  { titulo: 'Prazo', classe: 'whitespace-nowrap', render: (s) => { const p = prazoDaLinha(s); return <span className={p.classe}>{p.texto}</span>; } },
];

/** Os itens da solicitação, recolhidos na linha: abrir um não faz a tabela inteira crescer. */
function Itens({ s }: { s: SolicitacaoDoRelatorioDeMaterial }) {
  return (
    <details>
      <summary className="cursor-pointer text-marca">{quantidade(s.items)} item(ns) · {quantidade(s.requestedQty)} un.</summary>
      <ul className="mt-1 space-y-0.5 text-[12.5px]">
        {s.itemList.map((i) => (
          <li key={i.code + i.description}>
            {quantidade(i.qty)} {i.unit} × [{i.code}] {i.description}
            {i.unitPrice != null ? ` · ${moeda(i.unitPrice)}/${i.unit}` : <span className="text-perigo"> · sem custo</span>}
            {i.deliveredQty > 0 && ` · entregue ${quantidade(i.deliveredQty)}`}
            {` (${i.statusLabel})`}
          </li>
        ))}
      </ul>
    </details>
  );
}

function Ranking({ titulo, rotulo, linhas, testid }: { titulo: string; rotulo: string; linhas: LinhaDeMaterial[]; testid: string }) {
  const colunas: ColunaResponsiva<LinhaDeMaterial>[] = [
    { titulo: rotulo, principal: true, render: (l) => l.label },
    { titulo: 'Solicitações', classe: 'text-right', render: (l) => quantidade(l.count) },
    { titulo: 'Qtd pedida', classe: 'text-right', render: (l) => quantidade(l.qty) },
    { titulo: 'Qtd entregue', classe: 'text-right', render: (l) => quantidade(l.delivered) },
    { titulo: 'Valor pedido', classe: 'text-right whitespace-nowrap', render: (l) => valor(l.requestedValue) },
    { titulo: 'Valor entregue', classe: 'text-right whitespace-nowrap', render: (l) => valor(l.deliveredValue) },
  ];
  return (
    <Painel titulo={titulo}>
      {!linhas.length
        ? <Vazio>Nenhuma solicitação no período.</Vazio>
        : <TabelaResponsiva testid={testid} linhas={linhas} chave={(l) => l.key} colunas={colunas} />}
    </Painel>
  );
}

