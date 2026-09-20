import { useState } from 'react';
import { Link } from 'react-router-dom';
import { BADGE_ACHADO, relatorioDeInsights, type Achado } from '@/api/analytics';
import { comparacaoDaEscolha, diasDesde, processosParaMinhaAprovacao, type ProcessoParaAprovar } from '@/api/cotacoes';
import { FILTROS_RELATORIO_VAZIOS, relatorioExecutivo, type RelatorioExecutivo } from '@/api/relatorios';
import { variacao } from '@/api/painel';
import { Badge, Carregando, Erro, Painel } from '@/componentes/basicos';
import { enderecoDoId } from '@/layout/menu';
import { resumoExecutivo } from '@/paginas/relatorios/resumoExecutivo';
import { moeda, quantidade } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';
import { intervaloDoPeriodo, PERIODOS, type Periodo } from './periodo';

const pct = (v: number | null | undefined) => (v == null ? '—' : `${quantidade(v)}%`);

/** Um número grande com a tendência ao lado — o formato em que a diretoria lê. */
function Numero({ rotulo, valor, detalhe, tendencia, tom = 'neutro', testid }: {
  rotulo: string; valor: string; detalhe?: string; testid: string;
  tendencia?: { atual: number; anterior: number; bomQuandoSobe: boolean } | null;
  tom?: 'neutro' | 'atencao' | 'ok';
}) {
  const v = tendencia && tendencia.anterior > 0 ? variacao(tendencia.atual, tendencia.anterior) : null;
  const classeTendencia = !v || v.pct === 0 ? 'text-texto-suave'
    : (v.pct > 0) === tendencia!.bomQuandoSobe ? 'text-ok' : 'text-perigo';
  return (
    <div data-testid={testid} className={`rounded-xl border bg-white px-4 py-4 shadow-sm ${tom === 'atencao' ? 'border-aviso/50' : tom === 'ok' ? 'border-ok/40' : 'border-slate-200/80'}`}>
      <div className="rotulo">{rotulo}</div>
      <div className="mt-1.5 text-[30px] font-extrabold leading-none tracking-tight text-slate-900 tabular-nums">{valor}</div>
      <div className="sub mt-2 flex flex-wrap items-center gap-x-2">
        {v && <span className={`font-semibold ${classeTendencia}`}>{v.sinal} {Math.abs(v.pct)}% vs. anterior</span>}
        {detalhe && <span>{detalhe}</span>}
      </div>
    </div>
  );
}

/** A fila de decisão, compacta: o que espera a diretoria, e há quanto tempo. A decisão se toma na Central. */
function FilaDeDecisao({ fila }: { fila: ProcessoParaAprovar[] }) {
  if (!fila.length) return <p className="sub" data-testid="fila-vazia">Nada aguardando a sua aprovação agora.</p>;
  return (
    <ul className="divide-y divide-borda" data-testid="fila-da-diretoria">
      {fila.map((q) => {
        const e = comparacaoDaEscolha(q);
        const espera = diasDesde(q.decisao?.waitingSince ?? q.managerApproval?.at ?? null);
        return (
          <li key={q.id} data-processo={q.number} className="flex flex-wrap items-center gap-x-4 gap-y-1 py-2.5">
            <span className="font-semibold">{q.number}</span>
            {q.decisao?.priority === 'URGENT' && <Badge classe="bg-perigo-fundo text-perigo">URGENTE</Badge>}
            <span className="min-w-0 flex-1 truncate text-[13px]">
              {q.justification ?? '—'}<span className="sub"> · {q.costCenter}{q.decisao?.requesterLabel ? ` · ${q.decisao.requesterLabel}` : ''}</span>
            </span>
            <span className="text-[13px]">{e.fornecedor ?? '—'}</span>
            <strong className="whitespace-nowrap tabular-nums">{e.total != null ? moeda(e.total) : '—'}</strong>
            {espera != null && (
              <Badge classe={espera >= 5 ? 'bg-perigo-fundo text-perigo' : espera >= 3 ? 'bg-aviso-fundo text-aviso' : 'bg-slate-100 text-slate-600'}>
                {espera === 0 ? 'hoje' : `${espera} dia(s)`}
              </Badge>
            )}
            <Link to="/aprovacoes" className="botao-secundario">Decidir →</Link>
          </li>
        );
      })}
    </ul>
  );
}

/**
 * A página inicial da diretoria: cinco números com tendência, o relatório em três frases,
 * a fila de decisão e os achados. Tudo o que está aqui existe noutra tela — esta só põe
 * na ordem em que um diretor pergunta: como está, o que espera por mim, o que chama atenção.
 */
export function VisaoDaDiretoria() {
  const [periodo, setPeriodo] = useState<Periodo>('mes');
  const { dados, erro, carregando } = useCarregar(async (signal) => {
    const { de, ate } = intervaloDoPeriodo(periodo);
    return {
      relatorio: await relatorioExecutivo({ ...FILTROS_RELATORIO_VAZIOS, de, ate }, signal),
      fila: await processosParaMinhaAprovacao(signal).catch(() => [] as ProcessoParaAprovar[]),
      achados: await relatorioDeInsights(periodo === 'ano' ? 12 : 3, signal).then((r) => r.insights).catch(() => [] as Achado[]),
    };
  }, [periodo]);

  const r: RelatorioExecutivo | undefined = dados?.relatorio;
  const fila = dados?.fila ?? [];
  const valorDaFila = fila.reduce((s, q) => s + (comparacaoDaEscolha(q).total ?? 0), 0);
  const maisAntiga = fila.reduce<number | null>((m, q) => {
    const d = diasDesde(q.decisao?.waitingSince ?? q.managerApproval?.at ?? null);
    return d == null ? m : m == null ? d : Math.max(m, d);
  }, null);
  const excecoes = r ? r.withoutErp.orders + r.urgent.orders : 0;

  return (
    <>
      <Painel titulo="Como está a compra da empresa" acoes={
        <div role="group" aria-label="Período" className="flex gap-1 rounded-lg border border-borda bg-white p-1">
          {PERIODOS.map((p) => (
            <button key={p.chave} type="button" aria-pressed={periodo === p.chave} data-periodo={p.chave}
              onClick={() => setPeriodo(p.chave)}
              className={`rounded-md px-3 py-1 text-[13px] font-semibold ${periodo === p.chave ? 'bg-marca text-white' : 'text-texto-suave hover:bg-slate-50'}`}>
              {p.rotulo}
            </button>
          ))}
        </div>
      }>
        {erro && <Erro>{erro}</Erro>}
        {carregando && !dados && <Carregando texto="Apurando o período…" />}
        {r && (
          <>
            <div className="grid grid-cols-2 gap-3 lg:grid-cols-5" data-testid="numeros-da-diretoria">
              <Numero testid="numero-gasto" rotulo="Gasto no período" valor={moeda(r.kpis.spend)}
                detalhe={`${quantidade(r.kpis.orders)} pedido(s) · ${quantidade(r.kpis.suppliers)} fornecedor(es)`}
                tendencia={{ atual: r.kpis.spend, anterior: r.previous.spend, bomQuandoSobe: false }} />
              <Numero testid="numero-saving" rotulo="Saving negociado" valor={moeda(r.kpis.savingTotal)}
                detalhe={r.kpis.savingPercent != null ? `${pct(r.kpis.savingPercent)} da primeira proposta` : 'sem processo negociado'}
                tendencia={{ atual: r.kpis.savingTotal, anterior: r.previous.savingTotal, bomQuandoSobe: true }} tom="ok" />
              <Numero testid="numero-fila" rotulo="Aguardando a sua aprovação"
                valor={quantidade(fila.length)} tom={fila.length ? 'atencao' : 'neutro'}
                detalhe={fila.length ? `${moeda(valorDaFila)}${maisAntiga != null && maisAntiga > 0 ? ` · a mais antiga há ${maisAntiga} dia(s)` : ''}` : 'fila limpa'} />
              <Numero testid="numero-excecoes" rotulo="Exceções" valor={quantidade(excecoes)} tom={excecoes ? 'atencao' : 'neutro'}
                detalhe={`${quantidade(r.withoutErp.orders)} sem O.C. do ERP · ${quantidade(r.urgent.orders)} urgente(s) (${pct(r.kpis.urgentPercent)} do valor)`} />
              <Numero testid="numero-otif" rotulo="Entrega no prazo (OTIF)" valor={pct(r.kpis.otifPercent)}
                detalhe={r.kpis.otifPercent == null ? 'sem entrega medida' : `antes ${pct(r.previous.otifPercent)}`}
                tendencia={r.kpis.otifPercent != null && r.previous.otifPercent != null
                  ? { atual: r.kpis.otifPercent, anterior: r.previous.otifPercent, bomQuandoSobe: true } : null} />
            </div>
            <ol className="mt-4 list-decimal space-y-1 pl-5 text-[14px]" data-testid="resumo-da-diretoria">
              {resumoExecutivo(r).map((frase) => <li key={frase}>{frase}</li>)}
            </ol>
            <p className="sub mt-3">
              Recorte: todas as empresas e centros, {r.from.split('-').reverse().join('/')} a {r.to.split('-').reverse().join('/')}.{' '}
              <Link to="/relatorios" className="font-semibold text-marca hover:underline">Relatórios completos →</Link>
            </p>
          </>
        )}
      </Painel>

      {dados && (
        <Painel titulo="O que espera a sua decisão" acoes={<Link to="/aprovacoes" className="botao">Central de Aprovação →</Link>}>
          <FilaDeDecisao fila={fila} />
        </Painel>
      )}

      {dados && (
        <Painel titulo="O que chama atenção">
          {!dados.achados.length && (
            <p className="sub" data-testid="sem-achados">Nenhum achado no período: nada de sobrepreço, fracionamento, urgência recorrente ou concentração.</p>
          )}
          {dados.achados.length > 0 && (
            <ul className="space-y-2" data-testid="achados-da-diretoria">
              {dados.achados.slice(0, 5).map((a, i) => (
                <li key={`${a.code}-${i}`} className="flex flex-wrap items-center gap-2 text-[13.5px]">
                  <Badge classe={BADGE_ACHADO[a.severity] ?? BADGE_ACHADO.info}>{a.code}</Badge>
                  <strong>{a.title}</strong>
                  <span className="sub">{a.evidence}</span>
                  {a.view && <Link to={enderecoDoId(a.view)} className="ml-auto text-[13px] font-semibold text-marca hover:underline">ver →</Link>}
                </li>
              ))}
            </ul>
          )}
          {dados.achados.length > 5 && (
            <p className="sub mt-2"><Link to="/insights" className="font-semibold text-marca hover:underline">Todos os {dados.achados.length} achados →</Link></p>
          )}
        </Painel>
      )}
    </>
  );
}
