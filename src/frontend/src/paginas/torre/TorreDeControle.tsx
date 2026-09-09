import { useState } from 'react';
import { Link } from 'react-router-dom';
import {
  CLASSE_DO_TOM, destinoDaAcao, ETAPAS, FILTROS_TORRE_VAZIOS, torreDeControle,
  type FiltrosDaTorre, type LinhaDaTorre,
} from '@/api/torre';
import { Aviso, Badge, Carregando, Erro, FaixaKpis, Kpi, Painel, Vazio } from '@/componentes/basicos';
import { Campo } from '@/componentes/formulario';
import { data, moeda, quantidade } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';

/** Um KPI que também filtra: clicar leva a Torre para aquela etapa. */
function KpiFiltro({ rotulo, valor, detalhe, ativo, aoClicar }: {
  rotulo: string; valor: number | string; detalhe?: string; ativo: boolean; aoClicar: () => void;
}) {
  return (
    <button type="button" onClick={aoClicar} aria-pressed={ativo}
      className={'rounded-painel border px-4 py-3 text-left transition-colors '
        + (ativo ? 'border-marca bg-marca/5' : 'border-borda bg-superficie hover:border-marca/40')}>
      <div className="text-[11.5px] font-bold uppercase tracking-wide text-texto-suave">{rotulo}</div>
      <div className="mt-1 text-[22px] font-bold leading-tight">{valor}</div>
      {detalhe && <div className="sub mt-0.5">{detalhe}</div>}
    </button>
  );
}

function Linha({ i }: { i: LinhaDaTorre }) {
  return (
    <tr data-testid={`linha-${i.itemId}`} className={i.late ? 'bg-perigo-fundo/40' : undefined}>
      <td className="whitespace-nowrap">{i.prNumber}<div className="sub">item {i.sequence}</div></td>
      <td className="min-w-[200px]">
        {i.catalogCode ? `[${i.catalogCode}] ` : ''}{i.description}
        {i.priority === 'URGENT' && (
          <Badge classe="ml-2 bg-perigo-fundo text-perigo">URGENTE</Badge>
        )}
      </td>
      <td className="whitespace-nowrap">{quantidade(i.quantity)} {i.unitOfMeasure}</td>
      <td className="whitespace-nowrap">{i.requesterLabel}<div className="sub">{i.company ?? '—'}</div></td>
      <td className="whitespace-nowrap">{i.costCenter}</td>
      <td className="whitespace-nowrap">{i.buyerLabel ?? <span className="sub">sem responsável</span>}</td>
      <td className="whitespace-nowrap">{i.supplierName ?? <span className="sub">—</span>}</td>
      <td className="whitespace-nowrap">{i.stageLabel}</td>
      <td className="whitespace-nowrap">
        <Badge classe={CLASSE_DO_TOM[i.statusTone] ?? CLASSE_DO_TOM['']}>{i.statusLabel}</Badge>
      </td>
      <td className="whitespace-nowrap">
        {data(i.promisedDate ?? i.neededBy)}
        {i.late && <div className="text-[11.5px] font-bold text-perigo">atrasado</div>}
        {/* número de exceção no topo sem o motivo na linha obriga o comprador a
            caçar o processo um a um — a razão vem junto */}
        {i.exceptionReason && (
          <div className="text-[11.5px] font-bold text-aviso" title="Exceção registrada">
            ⚠ {i.exceptionReason}
          </div>
        )}
      </td>
      <td className="whitespace-nowrap">{i.value != null ? moeda(i.value) : <span className="sub">—</span>}</td>
      {/* §5 — a ação rápida: a linha diz o que fazer agora e leva até lá, em vez de
          obrigar o comprador a descobrir a tela certa para cada etapa */}
      <td className="whitespace-nowrap">
        {i.actionLabel ? (
          <Link className={'botao-secundario inline-block ' + (i.needsBuyer ? 'font-semibold' : '')}
            to={destinoDaAcao(i)}>
            {i.actionLabel}
          </Link>
        ) : <span className="sub">—</span>}
      </td>
      {/* o link leva para onde a ação está: o pedido, se já existe; senão a cotação */}
      <td className="whitespace-nowrap">
        {i.purchaseOrderId ? (
          <Link className="text-marca hover:underline" to={`/pedidos/${i.purchaseOrderId}`}>
            {i.purchaseOrderNumber}
          </Link>
        ) : i.quotationId ? (
          <Link className="text-marca hover:underline" to={`/cotacoes/${i.quotationId}`}>
            {i.quotationNumber}
          </Link>
        ) : <span className="sub">—</span>}
      </td>
    </tr>
  );
}

/**
 * Torre de Controle — a tela do comprador.
 *
 * **Uma linha por item, nunca uma por solicitação.** Uma SC de cinco itens pode
 * ter um entregue, dois em cotação e dois parados esperando alguém; olhar a SC
 * inteira esconde exatamente o que precisa de ação.
 *
 * Os KPIs do topo filtram: o número é a pergunta, e clicar nele é a resposta.
 * Eles contam a base inteira, não a página — KPI que muda ao virar a página não
 * é indicador, é contagem de tela.
 */
export function TorreDeControle() {
  const [rascunho, setRascunho] = useState<FiltrosDaTorre>(FILTROS_TORRE_VAZIOS);
  const [aplicados, setAplicados] = useState<FiltrosDaTorre>(FILTROS_TORRE_VAZIOS);
  const { dados, erro, carregando } = useCarregar(
    (signal) => torreDeControle(aplicados, signal), [aplicados]);

  const campo = (k: keyof FiltrosDaTorre) => ({
    value: String(rascunho[k] ?? ''),
    onChange: (e: { target: { value: string } }) => setRascunho((f) => ({ ...f, [k]: e.target.value })),
  });
  const fo = dados?.filterOptions;

  /** Aplicar sempre volta para a primeira página: filtro novo, contagem nova. */
  const aplicar = (extra: Partial<FiltrosDaTorre> = {}) => {
    const f = { ...rascunho, ...extra, pagina: 1 };
    setRascunho(f);
    setAplicados(f);
  };
  const limpar = () => { setRascunho(FILTROS_TORRE_VAZIOS); setAplicados({ ...FILTROS_TORRE_VAZIOS }); };
  const irPara = (p: number) => setAplicados((f) => ({ ...f, pagina: p }));
  const porEtapa = (etapa: string) =>
    aplicar({ etapa: aplicados.etapa === etapa ? '' : etapa, atrasados: false });

  return (
    <>
      {dados && (
        <FaixaKpis>
          <Kpi rotulo="Itens em aberto" valor={quantidade(dados.kpis.total)}
            detalhe={`${moeda(dados.kpis.valor)} estimados`} />
          {/* §5 — a fila prioritária. Vem primeiro entre os filtros porque é por onde
              o comprador começa o dia: o que espera ele, na ordem em que aperta */}
          <KpiFiltro rotulo="Precisa de você"
            valor={quantidade(dados.kpis.novos + dados.kpis.emCotacao + dados.kpis.aguardandoOc)}
            detalhe="atrasado e urgente primeiro"
            ativo={aplicados.minhaFila}
            aoClicar={() => aplicar({ minhaFila: !aplicados.minhaFila, etapa: '', atrasados: false })} />
          <KpiFiltro rotulo="Novos" valor={quantidade(dados.kpis.novos)} detalhe="aguardando o comprador"
            ativo={aplicados.etapa === 'SOLICITACAO'} aoClicar={() => porEtapa('SOLICITACAO')} />
          <KpiFiltro rotulo="Em cotação" valor={quantidade(dados.kpis.emCotacao)} detalhe="sourcing em andamento"
            ativo={aplicados.etapa === 'COTACAO'} aoClicar={() => porEtapa('COTACAO')} />
          <KpiFiltro rotulo="Aguardando aprovação" valor={quantidade(dados.kpis.aguardandoAprovacao)}
            detalhe="níveis 1 e 2" ativo={aplicados.etapa === 'APROVACAO'} aoClicar={() => porEtapa('APROVACAO')} />
          <KpiFiltro rotulo="Aguardando O.C." valor={quantidade(dados.kpis.aguardandoOc)}
            detalhe="aprovado, sem O.C. registrada"
            ativo={aplicados.etapa === 'ORDEM_DE_COMPRA'} aoClicar={() => porEtapa('ORDEM_DE_COMPRA')} />
          {/* §5: duas filas, e o que as separa é a nota fiscal — sem NF a bola está
              com o fornecedor; com NF e sem entrega, com o almoxarifado */}
          <KpiFiltro rotulo="Em faturamento" valor={quantidade(dados.kpis.emFaturamento)}
            detalhe="O.C. emitida, sem NF"
            ativo={aplicados.etapa === 'RECEBIMENTO'} aoClicar={() => porEtapa('RECEBIMENTO')} />
          <KpiFiltro rotulo="Aguardando recebimento" valor={quantidade(dados.kpis.aguardandoRecebimento)}
            detalhe="NF lançada, material a caminho"
            ativo={aplicados.etapa === 'RECEBIMENTO'} aoClicar={() => porEtapa('RECEBIMENTO')} />
          <KpiFiltro rotulo="Atrasados" valor={quantidade(dados.kpis.atrasados)} detalhe="passaram da previsão"
            ativo={aplicados.atrasados} aoClicar={() => aplicar({ atrasados: !aplicados.atrasados, etapa: '' })} />
          <KpiFiltro rotulo="Urgentes" valor={quantidade(dados.kpis.urgentes)} detalhe="prioridade URGENT"
            ativo={aplicados.prioridade === 'URGENT'}
            aoClicar={() => aplicar({ prioridade: aplicados.prioridade === 'URGENT' ? '' : 'URGENT' })} />
          <KpiFiltro rotulo="Exceções" valor={quantidade(dados.kpis.excecoes)}
            detalhe="sem O.C. do ERP, cancelado ou devolvido"
            ativo={aplicados.excecoes}
            aoClicar={() => aplicar({ excecoes: !aplicados.excecoes, etapa: '' })} />
        </FaixaKpis>
      )}

      <Painel titulo="Filtros">
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <Campo id="tc-busca" rotulo="Buscar" dica="(SC, produto ou código)">
            <input id="tc-busca" placeholder="PR-2026-000123, luva…" {...campo('busca')}
              onKeyDown={(e) => { if (e.key === 'Enter') aplicar(); }} />
          </Campo>
          <Campo id="tc-etapa" rotulo="Etapa">
            <select id="tc-etapa" {...campo('etapa')}>
              <option value="">Todas</option>
              {ETAPAS.map((e) => <option key={e.key} value={e.key}>{e.label}</option>)}
            </select>
          </Campo>
          <Campo id="tc-cc" rotulo="Centro de custo">
            <select id="tc-cc" {...campo('centroCusto')}>
              <option value="">Todos</option>
              {(fo?.costCenters ?? []).map((c) => (
                <option key={c.code} value={c.code}>{c.code} — {c.name}</option>
              ))}
            </select>
          </Campo>
          <Campo id="tc-empresa" rotulo="Empresa">
            <select id="tc-empresa" {...campo('empresa')}>
              <option value="">Todas</option>
              {(fo?.companies ?? []).map((e) => <option key={e} value={e}>{e}</option>)}
            </select>
          </Campo>
          <Campo id="tc-comprador" rotulo="Comprador">
            <select id="tc-comprador" {...campo('comprador')}>
              <option value="">Todos</option>
              {(fo?.buyers ?? []).map((b) => <option key={b.id} value={b.id}>{b.label}</option>)}
            </select>
          </Campo>
          <Campo id="tc-solicitante" rotulo="Solicitante">
            <select id="tc-solicitante" {...campo('solicitante')}>
              <option value="">Todos</option>
              {(fo?.requesters ?? []).map((r) => <option key={r.id} value={r.id}>{r.label}</option>)}
            </select>
          </Campo>
          <Campo id="tc-familia" rotulo="Família">
            <select id="tc-familia" {...campo('familia')}>
              <option value="">Todas</option>
              {(fo?.families ?? []).map((f) => <option key={f} value={f}>{f}</option>)}
            </select>
          </Campo>
          <Campo id="tc-prioridade" rotulo="Prioridade">
            <select id="tc-prioridade" {...campo('prioridade')}>
              <option value="">Todas</option>
              {['URGENT', 'HIGH', 'NORMAL', 'LOW'].map((p) => <option key={p} value={p}>{p}</option>)}
            </select>
          </Campo>

          {/* §5.1 — os quatro últimos dependem do pedido: item ainda não comprado
              não tem fornecedor, O.C. nem valor fechado, e por isso sai do recorte */}
          <Campo id="tc-fornecedor" rotulo="Fornecedor">
            <input id="tc-fornecedor" placeholder="parte do nome" {...campo('fornecedor')}
              onKeyDown={(e) => { if (e.key === 'Enter') aplicar(); }} />
          </Campo>
          <Campo id="tc-oc" rotulo="Número da O.C." dica="(nossa ou do ERP)">
            <input id="tc-oc" placeholder="PO-2026-000123 ou 4521" {...campo('numeroOc')}
              onKeyDown={(e) => { if (e.key === 'Enter') aplicar(); }} />
          </Campo>
          <Campo id="tc-de" rotulo="SC criada de">
            <input id="tc-de" type="date" {...campo('de')} />
          </Campo>
          <Campo id="tc-ate" rotulo="SC criada até">
            <input id="tc-ate" type="date" {...campo('ate')} />
          </Campo>
          <Campo id="tc-prazo-de" rotulo="Previsão de">
            <input id="tc-prazo-de" type="date" {...campo('prazoDe')} />
          </Campo>
          <Campo id="tc-prazo-ate" rotulo="Previsão até">
            <input id="tc-prazo-ate" type="date" {...campo('prazoAte')} />
          </Campo>
          <Campo id="tc-valor-de" rotulo="Valor de (R$)">
            <input id="tc-valor-de" type="number" min="0" step="0.01" placeholder="0,00"
              {...campo('valorDe')} onKeyDown={(e) => { if (e.key === 'Enter') aplicar(); }} />
          </Campo>
          <Campo id="tc-valor-ate" rotulo="Valor até (R$)">
            <input id="tc-valor-ate" type="number" min="0" step="0.01" placeholder="0,00"
              {...campo('valorAte')} onKeyDown={(e) => { if (e.key === 'Enter') aplicar(); }} />
          </Campo>
        </div>
        <div className="mt-3 flex flex-wrap gap-2">
          <button type="button" className="botao" onClick={() => aplicar()}>Aplicar filtros</button>
          <button type="button" className="botao-secundario" onClick={limpar}>Limpar</button>
        </div>
      </Painel>

      <Painel titulo="Itens de compra">
        <p className="sub mb-3">
          Uma linha por <strong>item</strong>, e não por solicitação: itens da mesma SC podem
          estar em etapas diferentes ao mesmo tempo, e é isso que a Torre mostra.
        </p>
        {erro && <Erro>{erro}</Erro>}
        {dados?.capped && (
          <Aviso testid="torre-teto">
            O filtro alcançou o teto de {quantidade(dados.cap)} itens analisados. Estreite o
            período ou o centro de custo para a contagem fechar.
          </Aviso>
        )}
        {carregando && !dados && <Carregando texto="Montando a fila…" />}
        {dados && !dados.items.length && <Vazio>Nenhum item de compra neste recorte.</Vazio>}
        {!!dados?.items.length && (
          <>
            <div className="overflow-x-auto">
              <table data-testid="tabela-torre" className="min-w-[1180px]">
                <thead>
                  <tr>
                    <th>SC</th><th>Produto</th><th>Qtd.</th><th>Solicitante</th><th>CC</th>
                    <th>Comprador</th><th>Fornecedor</th><th>Etapa</th><th>Situação</th>
                    <th>Previsão</th><th>Valor</th><th>Ação</th><th>Processo</th>
                  </tr>
                </thead>
                <tbody>{dados.items.map((i) => <Linha key={i.itemId} i={i} />)}</tbody>
              </table>
            </div>
            <div className="mt-3 flex flex-wrap items-center justify-between gap-2">
              <span className="sub" data-testid="torre-contagem">
                {quantidade(dados.total)} item(ns) · página {dados.page} de {Math.max(1, dados.pages)}
              </span>
              <div className="flex gap-2">
                <button type="button" className="botao-secundario" disabled={dados.page <= 1}
                  onClick={() => irPara(dados.page - 1)}>Anterior</button>
                <button type="button" className="botao-secundario" disabled={dados.page >= dados.pages}
                  onClick={() => irPara(dados.page + 1)}>Próxima</button>
              </div>
            </div>
          </>
        )}
      </Painel>
    </>
  );
}
