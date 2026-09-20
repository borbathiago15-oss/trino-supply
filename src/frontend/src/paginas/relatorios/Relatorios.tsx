import { useState } from 'react';
import {
  FILTROS_RELATORIO_VAZIOS, pdfDoRelatorio, relatorioExecutivo,
  type FiltrosRelatorio, type LinhaSavingRateado, type RelatorioExecutivo,
} from '@/api/relatorios';
import { abrirBlob } from '@/api/cliente';
import { variacao } from '@/api/painel';
import { Aviso, Badge, Carregando, Erro, FaixaKpis, Kpi, Painel } from '@/componentes/basicos';
import { Campo } from '@/componentes/formulario';
import { CORES, GraficoColunas, Legenda, moedaCurta, rotuloDoMes, type Serie } from '@/componentes/graficos';
import { useToast } from '@/componentes/Toast';
import { data, moeda, quantidade } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';
import { resumoExecutivo } from './resumoExecutivo';

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

const pct = (v: number | null | undefined) => (v == null ? '—' : `${quantidade(v)}%`);

/** Curva ABC: A concentra 80% do gasto, B chega a 95%, C é a cauda. */
const CLASSE_ABC: Record<'A' | 'B' | 'C', string> = {
  A: 'bg-ok-fundo text-ok', B: 'bg-aviso-fundo text-aviso', C: 'bg-slate-100 text-slate-600',
};

/** Barra proporcional: a fatia de cada linha lida de relance, sem virar gráfico. */
function Fatia({ percent }: { percent: number }) {
  return (
    <div className="flex items-center gap-2">
      <div className="h-1.5 w-16 shrink-0 rounded-full bg-slate-100">
        <div className="h-1.5 rounded-full bg-marca" style={{ width: `${Math.min(100, Math.max(0, percent))}%` }} />
      </div>
      <span className="whitespace-nowrap">{pct(percent)}</span>
    </div>
  );
}

/**
 * Um bloco do relatório: título, a frase que diz de onde o número vem e a tabela.
 * A explicação não é enfeite — número de diretoria sem a régua ao lado vira
 * discussão sobre o que ele significa, não sobre o que fazer com ele.
 */
function Bloco({ titulo, explicacao, vazio, testid, largura = 'min-w-[620px]', children }: {
  titulo: string; explicacao: string; vazio?: string; testid: string;
  largura?: string; children: React.ReactNode;
}) {
  return (
    <Painel titulo={titulo}>
      <ComoECalculado>{explicacao}</ComoECalculado>
      {vazio ? <p className="sub" data-testid={`${testid}-vazio`}>{vazio}</p> : (
        <div className="overflow-x-auto">
          <table data-testid={testid} className={largura}>{children}</table>
        </div>
      )}
    </Painel>
  );
}

/**
 * A régua do número fica a um clique, não em cima dele: a diretoria lê o número primeiro
 * e abre a explicação quando discorda dele — que é quando ela importa.
 */
export function ComoECalculado({ children }: { children: React.ReactNode }) {
  return (
    <details className="mb-3">
      <summary className="cursor-pointer text-[12.5px] font-semibold text-marca">como é calculado</summary>
      <p className="sub mt-1">{children}</p>
    </details>
  );
}

export type Aba = 'geral' | 'saving' | 'fornecedores' | 'demanda' | 'excecoes';
export const ABAS: { chave: Aba; rotulo: string; blocos: string }[] = [
  { chave: 'geral', rotulo: 'Visão geral', blocos: '3–4' },
  { chave: 'saving', rotulo: 'Saving', blocos: '2, 5–6' },
  { chave: 'fornecedores', rotulo: 'Fornecedores', blocos: '7, 9–10, 12' },
  { chave: 'demanda', rotulo: 'Demanda e prazos', blocos: '1, 8, 11, 13' },
  { chave: 'excecoes', rotulo: 'Exceções', blocos: '14' },
];

/** Catorze blocos em cinco abas: a rolagem de cinco telas virou uma escolha. */
function Abas({ valor, aoMudar }: { valor: Aba; aoMudar: (a: Aba) => void }) {
  return (
    <div role="tablist" aria-label="Blocos do relatório" className="mb-4 flex flex-wrap gap-1 rounded-xl border border-borda bg-white p-1">
      {ABAS.map((a) => (
        <button key={a.chave} type="button" role="tab" aria-selected={valor === a.chave} data-aba={a.chave}
          onClick={() => aoMudar(a.chave)}
          className={`rounded-lg px-3 py-1.5 text-[13px] font-semibold ${valor === a.chave ? 'bg-marca text-white' : 'text-texto-suave hover:bg-slate-50'}`}>
          {a.rotulo} <span className={`text-[11px] font-normal ${valor === a.chave ? 'text-white/80' : ''}`}>({a.blocos})</span>
        </button>
      ))}
    </div>
  );
}

/** As três frases que abrem o relatório: gasto, saving, exceções. */
function ResumoExecutivo({ r }: { r: RelatorioExecutivo }) {
  const [gasto, saving, excecoes] = resumoExecutivo(r);
  return (
    <Painel titulo="Em três frases">
      <ol className="list-decimal space-y-1.5 pl-5 text-[14px]" data-testid="resumo-executivo">
        <li>{gasto}</li><li>{saving}</li><li>{excecoes}</li>
      </ol>
    </Painel>
  );
}

/**
 * "antes: R$ 10.000,00 (▲ 20%)" — o mesmo número na janela anterior, e a variação.
 * Sem isso o KPI é um número solto: a pergunta seguinte da diretoria é sempre
 * "e no período passado?".
 */
export function Antes({ atual, anterior, formatar = moeda }:
  { atual: number; anterior: number; formatar?: (v: number) => string }) {
  const v = variacao(atual, anterior);
  return (
    <span data-testid="antes">
      antes: {formatar(anterior)}
      {/* sem base não há variação: "▲ 100%" sobre zero é aritmética, não leitura */}
      {anterior > 0 && <> <span className={v.classe}>({v.sinal} {Math.abs(v.pct)}%)</span></>}
    </span>
  );
}

const REGUAS: { chave: 'negotiation' | 'competition' | 'budget'; titulo: string; contra: string; ausente: string }[] = [
  { chave: 'negotiation', titulo: 'Negociação', contra: 'contra a primeira proposta do fornecedor vencedor',
    ausente: 'Nenhum processo negociado no recorte.' },
  { chave: 'competition', titulo: 'Concorrência', contra: 'contra a maior proposta completa do BID',
    ausente: 'Não se aplica: nenhum processo do recorte teve mais de um proponente.' },
  { chave: 'budget', titulo: 'Orçamento', contra: 'contra o valor que o solicitante informou na SC',
    ausente: 'Não se aplica: nenhum processo do recorte teve orçamento em todas as SCs.' },
];

/**
 * As três réguas do saving, lado a lado e nunca somadas: "negociamos bem", "a disputa
 * valeu" e "gastamos menos do que o previsto" são três respostas, não uma.
 */
function Reguas({ r }: { r: RelatorioExecutivo }) {
  return (
    <div className="grid grid-cols-1 gap-3 md:grid-cols-3" data-testid="relatorio-reguas">
      {REGUAS.map((g) => {
        const regua = r.savingRulers[g.chave];
        return (
          <div key={g.chave} data-regua={g.chave} className="rounded-xl border border-slate-200/80 bg-white px-4 py-3.5 shadow-sm">
            <div className="rotulo">{g.titulo}</div>
            {regua.processes === 0 ? (
              <p className="mt-2 text-[13px] text-texto-suave">{g.ausente}</p>
            ) : (
              <>
                <div className="mt-1.5 text-3xl font-extrabold leading-none tracking-tight text-slate-900 tabular-nums">
                  {moeda(regua.saving)}
                </div>
                <div className="sub mt-1.5">
                  {pct(regua.percent)} {g.contra} · {quantidade(regua.processes)} processo(s)
                </div>
                <div className="sub">base {moeda(regua.baseline)} → fechado {moeda(regua.closed)}</div>
              </>
            )}
          </div>
        );
      })}
    </div>
  );
}

/** Uma das duas tabelas do saving rateado — família ou fornecedor — com a mesma forma. */
function SavingRateado({ titulo, linhas, testid }: { titulo: string; linhas: LinhaSavingRateado[]; testid: string }) {
  if (!linhas.length) return <p className="sub">Nenhum processo com saving no recorte.</p>;
  return (
    <div className="overflow-x-auto">
      <table data-testid={testid}>
        <thead><tr><th>{titulo}</th><th>Processos</th><th>Comprado</th><th>Saving</th><th>%</th></tr></thead>
        <tbody>
          {linhas.map((l) => (
            <tr key={l.label}>
              <td>{l.label}</td>
              <td>{quantidade(l.processes)}</td>
              <td className="whitespace-nowrap">{moeda(l.spend)}</td>
              <td className="whitespace-nowrap"><strong>{moeda(l.saving)}</strong></td>
              <td className="whitespace-nowrap">{pct(l.savingPercent)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** Diferença contra o último preço pago: negativa em vermelho, porque é ela que pede ação. */
const Diferenca = ({ valor }: { valor: number }) => (
  <strong className={valor < 0 ? 'text-perigo' : valor > 0 ? 'text-ok' : ''}>{moeda(valor)}</strong>
);

function Blocos({ r, aba, aoMudar }: { r: RelatorioExecutivo; aba: Aba; aoMudar: (a: Aba) => void }) {
  const a = r.previous;
  const meses = r.months.map((m) => m.month);
  const seriesDoMes: Serie[] = [
    { nome: 'Total comprado', cor: CORES[1], valores: r.months.map((m) => m.spend) },
    { nome: 'Saving negociado', cor: CORES[3], valores: r.months.map((m) => m.saving) },
  ];

  return (
    <>
      <FaixaKpis>
        <Kpi rotulo="Total comprado" valor={moeda(r.kpis.spend)}
          detalhe={<>
            {quantidade(r.kpis.orders)} pedido(s) · {quantidade(r.kpis.suppliers)} fornecedor(es)
            <br /><Antes atual={r.kpis.spend} anterior={a.spend} />
          </>} />
        <Kpi rotulo="Saving negociado" valor={moeda(r.kpis.savingTotal)}
          detalhe={<>
            {r.kpis.savingPercent != null ? `${quantidade(r.kpis.savingPercent)}% da primeira proposta` : 'sem processo negociado'}
            <br /><Antes atual={r.kpis.savingTotal} anterior={a.savingTotal} />
          </>} />
        <Kpi rotulo="Compras urgentes" valor={pct(r.kpis.urgentPercent)}
          detalhe={<>do valor do período<br />antes: {pct(a.urgentPercent)}</>} />
        <Kpi rotulo="OTIF" valor={pct(r.kpis.otifPercent)}
          detalhe={<>entregas encerradas e medidas<br />antes: {pct(a.otifPercent)}</>} />
        <Kpi rotulo="Sem O.C. do ERP" valor={moeda(r.kpis.withoutErpValue)}
          detalhe={`${quantidade(r.withoutErp.orders)} compra(s) fechada(s) pela exceção`} />
        <Kpi rotulo="Prazo médio de pagamento (DPO)"
          valor={r.payment.weightedDays != null ? `${quantidade(r.payment.weightedDays)} dias` : '—'}
          detalhe={`ponderado pelo valor · ${quantidade(r.payment.ordersWithDays)} pedido(s) com prazo`} />
        <Kpi rotulo="Aderência à O.C. do ERP" valor={pct(r.adherence.percent)}
          detalhe={`${quantidade(r.adherence.formal)} de ${quantidade(r.adherence.orders)} pedido(s) já julgados`} />
      </FaixaKpis>
      <p className="sub -mt-2 mb-4" data-testid="periodo-anterior">
        "Antes" é a janela de mesmo tamanho logo antes do recorte: {data(a.from)} a {data(a.to)}, com os mesmos filtros.
      </p>

      {(r.coverage.ordersWithoutPr > 0 || r.coverage.capped) && (
        <Painel>
          <Aviso testid="cobertura-do-recorte">
            {r.coverage.ordersWithoutPr > 0 && (
              <>
                <strong>{quantidade(r.coverage.ordersWithoutPr)} pedido(s)</strong>, somando{' '}
                {moeda(r.coverage.valueWithoutPr)}, não têm solicitação de origem — logo não têm
                empresa nem centro de custo, e ficam de fora de qualquer filtro por esses dois campos.{' '}
              </>
            )}
            {r.coverage.capped && (
              <>O período tem mais de {quantidade(r.coverage.cap)} pedidos: este relatório traz os{' '}
                {quantidade(r.coverage.cap)} mais recentes. Reduza o período para fechar o total.</>
            )}
          </Aviso>
        </Painel>
      )}

      <ResumoExecutivo r={r} />
      <Abas valor={aba} aoMudar={aoMudar} />
      {aba === 'geral' && (<>
      <Painel titulo="3. Saving do período, mês a mês">
        <p className="sub mb-3">
          O pedido conta no mês em que foi criado; o processo conta uma vez, no mês do primeiro pedido que o
          fechou — a mesma regra do bloco por comprador. Mês sem pedido aparece zerado: a linha do tempo não pula mês.
        </p>
        <Legenda series={seriesDoMes} />
        <GraficoColunas rotulos={meses} series={seriesDoMes} formatar={moedaCurta} titulo="Total comprado e saving por mês" />
        <div className="mt-3 overflow-x-auto">
          <table data-testid="relatorio-meses" className="min-w-[620px]">
            <thead><tr><th>Mês</th><th>Total comprado</th><th>Pedidos</th><th>Processos</th><th>Saving</th><th>%</th></tr></thead>
            <tbody>
              {r.months.map((m) => (
                <tr key={m.month} data-mes={m.month}>
                  <td className="whitespace-nowrap font-semibold">{rotuloDoMes(m.month)}</td>
                  <td className="whitespace-nowrap">{moeda(m.spend)}</td>
                  <td>{quantidade(m.orders)}</td>
                  <td>{quantidade(m.processes)}</td>
                  <td className="whitespace-nowrap"><strong>{moeda(m.saving)}</strong></td>
                  <td className="whitespace-nowrap">{pct(m.savingPercent)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Painel>
      <Painel titulo="4. As três réguas do saving">
        <p className="sub mb-3">
          Três perguntas, três números: <strong>negociação</strong> mede o que o comprador arrancou do mesmo
          fornecedor; <strong>concorrência</strong>, o que valeu ter chamado mais gente para o BID;{' '}
          <strong>orçamento</strong>, o quanto ficou abaixo do que o solicitante previa. Cada régua conta só o
          processo que a tem, e elas não se somam.
        </p>
        <Reguas r={r} />
      </Painel>
      </>)}
      {aba === 'saving' && (<>
      <Bloco titulo="2. Saving por comprador" testid="relatorio-saving" largura="min-w-[760px]"
        explicacao="Ganho de negociação apurado contra a primeira proposta do fornecedor vencedor. Cada processo entra uma vez, mesmo quando a compra foi dividida em várias O.C.s."
        vazio={r.buyers.length ? undefined : 'Nenhum pedido no recorte.'}>
        <thead>
          <tr>
            <th>Comprador</th><th>Processos</th><th>Base (1ª proposta)</th>
            <th>Fechado</th><th>Saving</th><th>%</th><th>Total comprado</th>
          </tr>
        </thead>
        <tbody>
          {r.buyers.map((b) => (
            <tr key={b.buyer}>
              <td>{b.buyer}</td>
              <td>{quantidade(b.processes)}</td>
              <td className="whitespace-nowrap">{moeda(b.baseline)}</td>
              <td className="whitespace-nowrap">{moeda(b.closed)}</td>
              <td className="whitespace-nowrap"><strong>{moeda(b.saving)}</strong></td>
              <td className="whitespace-nowrap">{pct(b.savingPercent)}</td>
              <td className="whitespace-nowrap">{moeda(b.spend)}</td>
            </tr>
          ))}
        </tbody>
      </Bloco>
      <Painel titulo="5. Saving por família e por fornecedor">
        <p className="sub mb-3">
          Onde a negociação rende e onde não rende. O saving é do processo: quando o processo virou mais de uma
          O.C., ele é rateado entre elas pelo valor de cada uma e, dentro da O.C., entre as famílias pelo valor
          dos itens. Uma O.C. de uma família só — o caso comum — sai exata. Pedido sem processo de cotação não
          tem saving a ratear.
        </p>
        <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
          <SavingRateado titulo="Família" linhas={r.savingByFamily} testid="relatorio-saving-familia" />
          <SavingRateado titulo="Fornecedor" linhas={r.savingBySupplier} testid="relatorio-saving-fornecedor" />
        </div>
      </Painel>
      <Bloco titulo="6. Saving de referência (× último preço pago)" testid="relatorio-referencia" largura="min-w-[820px]"
        explicacao={`Preço fechado contra o último preço pago do mesmo produto de catálogo, congelado no registro da O.C. Não se mistura ao saving de negociação: um mede a conversa com o fornecedor, o outro a história de preço do produto. ${quantidade(r.reference.items)} item(ns) em ${quantidade(r.reference.orders)} pedido(s) — ganho ${moeda(r.reference.gain)} · perda ${moeda(r.reference.loss)} · líquido ${moeda(r.reference.net)}. A perda vem primeiro: é ela que pede ação.`}
        vazio={r.reference.items ? undefined : 'Nenhum item do recorte tem preço pago anterior para comparar.'}>
        <thead><tr><th>Pedido</th><th>Produto</th><th>Fornecedor</th><th>Qtd</th><th>Último pago</th><th>Fechado</th><th>Diferença</th></tr></thead>
        <tbody>
          {r.reference.rows.map((l, i) => (
            <tr key={`${l.order}-${l.catalogCode ?? l.description}-${i}`}>
              <td className="whitespace-nowrap">{l.order}</td>
              <td>{l.description}{l.catalogCode && <div className="sub">{l.catalogCode}</div>}</td>
              <td>{l.supplier}</td>
              <td className="whitespace-nowrap">{quantidade(l.quantity)}</td>
              <td className="whitespace-nowrap">{moeda(l.lastPaidUnitPrice)}</td>
              <td className="whitespace-nowrap">{moeda(l.unitPrice)}</td>
              <td className="whitespace-nowrap"><Diferenca valor={l.saving} /></td>
            </tr>
          ))}
        </tbody>
      </Bloco>
      </>)}
      {aba === 'fornecedores' && (<>
      <Bloco titulo="7. Concentração por fornecedor" testid="relatorio-fornecedores"
        explicacao={`${quantidade(r.suppliers.supplierCount)} fornecedor(es) no recorte · maior fatia ${pct(r.suppliers.top1Percent)} · 3 maiores ${pct(r.suppliers.top3Percent)} · 5 maiores ${pct(r.suppliers.top5Percent)}.`}
        vazio={r.suppliers.rows.length ? undefined : 'Nenhum pedido no recorte.'}>
        <thead><tr><th>Fornecedor</th><th>Pedidos</th><th>Valor</th><th>% do total</th><th>Acumulado</th><th>ABC</th></tr></thead>
        <tbody>
          {r.suppliers.rows.map((f) => (
            <tr key={f.supplier}>
              <td>{f.supplier}</td>
              <td>{quantidade(f.orders)}</td>
              <td className="whitespace-nowrap">{moeda(f.value)}</td>
              <td><Fatia percent={f.percent} /></td>
              <td className="whitespace-nowrap sub">{f.cumulative != null ? pct(f.cumulative) : '—'}</td>
              <td>{f.class && <Badge classe={CLASSE_ABC[f.class]}>{f.class}</Badge>}</td>
            </tr>
          ))}
        </tbody>
      </Bloco>
      <Painel titulo="9. Concorrências (BIDs) — quem ganhou">
        <p className="sub mb-3">
          Proponente é quem mandou proposta — convidado que não respondeu não conta como disputa. Vencedor de
          concorrência é o fornecedor da O.C. de um processo com dois ou mais proponentes.
        </p>
        <FaixaKpis>
          <Kpi rotulo="Processos cotados" valor={quantidade(r.bids.processes)} detalhe="com O.C. no recorte" />
          <Kpi rotulo="Proponentes por BID" valor={r.bids.averageProponents != null ? quantidade(r.bids.averageProponents) : '—'} detalhe="média" />
          <Kpi rotulo="Com disputa" valor={quantidade(r.bids.withCompetition)} detalhe="dois ou mais proponentes" />
        </FaixaKpis>
        {!r.bids.winners.length && <p className="sub">Nenhum processo com disputa fechou no recorte.</p>}
        {r.bids.winners.length > 0 && (
          <div className="overflow-x-auto">
            <table data-testid="relatorio-vencedores">
              <thead><tr><th>Vencedor de concorrência</th><th>Vitórias</th><th>Valor</th></tr></thead>
              <tbody>
                {r.bids.winners.map((v) => (
                  <tr key={v.supplier}>
                    <td className="font-semibold">{v.supplier}</td>
                    <td>{quantidade(v.wins)}</td>
                    <td className="whitespace-nowrap">{moeda(v.value)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Painel>
      <Bloco titulo="12. Entrega no prazo (OTIF) por fornecedor" testid="relatorio-otif"
        explicacao="Só entram entregas encerradas com data prometida registrada. OTIF = chegou no prazo E completo; entrega em aberto não conta nem a favor nem contra."
        vazio={r.otif.length ? undefined : 'Nenhuma entrega encerrada com data prometida no recorte.'}>
        <thead><tr><th>Fornecedor</th><th>Entregas medidas</th><th>No prazo</th><th>Completo</th><th>OTIF</th></tr></thead>
        <tbody>
          {r.otif.map((o) => (
            <tr key={o.supplier}>
              <td>{o.supplier}</td>
              <td>{quantidade(o.measured)}</td>
              <td>{pct(o.onTimePercent)}</td>
              <td>{pct(o.inFullPercent)}</td>
              <td><strong>{pct(o.otifPercent)}</strong></td>
            </tr>
          ))}
        </tbody>
      </Bloco>
      <Bloco titulo="10. Formas e prazos de pagamento" testid="relatorio-pagamento"
        explicacao={(r.payment.weightedDays != null
          ? `DPO ${quantidade(r.payment.weightedDays)} dias, ponderado pelo valor de ${quantidade(r.payment.ordersWithDays)} pedido(s) (${moeda(r.payment.valueWithDays)}). `
          : 'Nenhum pedido do recorte tem prazo de pagamento legível. ')
          + 'O prazo vem da proposta vencedora; sem ela, do texto da condição gravada na O.C.'}
        vazio={r.payment.terms.length ? undefined : 'Nenhum pedido no recorte.'}>
        <thead><tr><th>Condição comercial</th><th>Dias</th><th>Pedidos</th><th>Valor</th><th>% do total</th></tr></thead>
        <tbody>
          {r.payment.terms.map((t) => (
            <tr key={t.term}>
              <td>{t.term}</td>
              <td className="whitespace-nowrap">{t.days != null ? `${t.days} d` : <span className="sub">—</span>}</td>
              <td>{quantidade(t.orders)}</td>
              <td className="whitespace-nowrap">{moeda(t.value)}</td>
              <td><Fatia percent={t.percent} /></td>
            </tr>
          ))}
        </tbody>
      </Bloco>
      </>)}
      {aba === 'demanda' && (<>
      <Bloco titulo="1. Compras por família" testid="relatorio-familias"
        explicacao="O que foi comprado no período, pelo valor dos itens do pedido — material de limpeza, fardamento, EPI e o resto do catálogo."
        vazio={r.families.length ? undefined : 'Nenhuma compra no recorte.'}>
        <thead><tr><th>Família</th><th>Valor</th><th>Quantidade</th><th>Pedidos</th><th>% do total</th></tr></thead>
        <tbody>
          {r.families.map((f) => (
            <tr key={f.family}>
              <td>{f.family}</td>
              <td className="whitespace-nowrap">{moeda(f.value)}</td>
              <td className="whitespace-nowrap">{quantidade(f.quantity)}</td>
              <td>{quantidade(f.orders)}</td>
              <td><Fatia percent={f.percent} /></td>
            </tr>
          ))}
        </tbody>
      </Bloco>
      <Painel titulo="8. Origem da demanda — quem pediu, de onde, material ou serviço">
        <p className="sub mb-3">
          Os centros de custo e os solicitantes por valor comprado, com o gestor do centro quando o cadastro o
          tem. Serviço é a família SERVIÇOS ou a cotação do tipo serviço; o resto é fornecimento de materiais.
        </p>
        <div className="grid grid-cols-1 gap-4 xl:grid-cols-3">
          <div className="overflow-x-auto xl:col-span-1">
            <table data-testid="relatorio-centros">
              <thead><tr><th>Centro de custo</th><th>Pedidos</th><th>Valor</th><th>%</th></tr></thead>
              <tbody>
                {r.demand.costCenters.map((cc) => (
                  <tr key={cc.code}>
                    <td>
                      <span className="font-semibold">{cc.code}</span> — {cc.name}
                      {cc.manager && <div className="sub">gestor: {cc.manager}</div>}
                    </td>
                    <td>{quantidade(cc.orders)}</td>
                    <td className="whitespace-nowrap">{moeda(cc.value)}</td>
                    <td><Fatia percent={cc.percent} /></td>
                  </tr>
                ))}
                {!r.demand.costCenters.length && <tr><td colSpan={4} className="sub">Nenhum pedido no recorte.</td></tr>}
              </tbody>
            </table>
          </div>
          <div className="overflow-x-auto">
            <table data-testid="relatorio-solicitantes">
              <thead><tr><th>Solicitante</th><th>SCs</th><th>Pedidos</th><th>Comprado</th></tr></thead>
              <tbody>
                {r.demand.requesters.map((q) => (
                  <tr key={q.requester}>
                    <td>{q.requester}</td>
                    <td>{quantidade(q.requisitions)}</td>
                    <td>{quantidade(q.orders)}</td>
                    <td className="whitespace-nowrap">{moeda(q.value)}</td>
                  </tr>
                ))}
                {!r.demand.requesters.length && <tr><td colSpan={4} className="sub">Nenhum pedido com solicitação de origem.</td></tr>}
              </tbody>
            </table>
          </div>
          <div data-testid="relatorio-escopo" className="rounded-xl border border-slate-200/80 bg-white px-4 py-3.5 shadow-sm">
            <div className="rotulo">Materiais × serviços</div>
            <div className="mt-3 flex h-3 overflow-hidden rounded-full bg-slate-100" aria-hidden>
              <div className="h-3 bg-marca" style={{ width: `${r.demand.scope.materialsPercent}%` }} />
              <div className="h-3 bg-aviso-forte" style={{ width: `${r.demand.scope.servicesPercent}%` }} />
            </div>
            <div className="mt-3 flex flex-col gap-1 text-[13px]">
              <span><span className="mr-2 inline-block h-2.5 w-2.5 rounded-sm bg-marca" />Materiais <strong>{pct(r.demand.scope.materialsPercent)}</strong> · {moeda(r.demand.scope.materials)}</span>
              <span><span className="mr-2 inline-block h-2.5 w-2.5 rounded-sm bg-aviso-forte" />Serviços <strong>{pct(r.demand.scope.servicesPercent)}</strong> · {moeda(r.demand.scope.services)}</span>
            </div>
          </div>
        </div>
      </Painel>
      <Bloco titulo="11. Peso das compras urgentes" testid="relatorio-urgentes" largura="min-w-[760px]"
        explicacao={`${quantidade(r.urgent.orders)} pedido(s) vindos de solicitação urgente — ${moeda(r.urgent.value)} (${pct(r.urgent.percent)} do período). Urgência exige motivo e impacto declarados na SC.`}
        vazio={r.urgent.orders ? undefined : 'Nenhuma compra urgente no recorte.'}>
        <thead><tr><th>Pedido</th><th>SC</th><th>Fornecedor</th><th>Valor</th><th>Motivo declarado</th></tr></thead>
        <tbody>
          {r.urgent.items.map((u) => (
            <tr key={u.number}>
              <td className="whitespace-nowrap">{u.number}<div className="sub">{data(u.issuedOn)}</div></td>
              <td className="whitespace-nowrap">{u.prNumber ?? '—'}
                {u.requester && <div className="sub">{u.requester}</div>}</td>
              <td>{u.supplier}</td>
              <td className="whitespace-nowrap">{moeda(u.value)}</td>
              <td className="min-w-[220px]">{u.reason ?? '—'}
                {u.impact && <div className="sub">{u.impact}</div>}</td>
            </tr>
          ))}
        </tbody>
      </Bloco>
      <Bloco titulo="13. Tempo do ciclo" testid="relatorio-ciclo" largura="min-w-[480px]"
        explicacao="Mediana em dias de cada etapa, no recorte. Cada etapa conta pelo seu próprio relógio e só entra quando as duas marcas existem. Mediana, não média: um processo parado por meses não esconde os outros que andaram em uma semana.">
        <thead><tr><th>Etapa</th><th>Medidos</th><th>Mediana</th></tr></thead>
        <tbody>
          {r.cycleTimes.map((e) => (
            <tr key={e.stage} data-etapa={e.stage}>
              <td>{e.title}</td>
              <td>{quantidade(e.measured)}</td>
              <td className="whitespace-nowrap">
                {e.medianDays != null ? <strong>{quantidade(e.medianDays)} d</strong> : <span className="sub">sem medição</span>}
              </td>
            </tr>
          ))}
        </tbody>
      </Bloco>
      </>)}
      {aba === 'excecoes' && (<>
      <Bloco titulo="14. Compras sem O.C. do ERP" testid="relatorio-sem-oc" largura="min-w-[760px]"
        explicacao={`${quantidade(r.withoutErp.orders)} compra(s) fechada(s) pela exceção — ${moeda(r.withoutErp.value)} (${pct(r.withoutErp.percent)} do período). A regra é a O.C. do SENIOR; a justificativa abaixo é a única exceção que libera o fechamento. Outros ${quantidade(r.withoutErp.pendingOrders)} pedido(s) (${moeda(r.withoutErp.pendingValue)}) seguem em aberto com a O.C. por registrar — fila, não exceção`
          + (r.withoutErp.closedWithoutReason > 0
            ? `; e ${quantidade(r.withoutErp.closedWithoutReason)} andaram sem O.C. e sem justificativa nenhuma.`
            : '.')}
        vazio={r.withoutErp.items.length ? undefined : 'Toda compra fechada no recorte tem O.C. do ERP.'}>
        <thead><tr><th>Pedido</th><th>Fornecedor</th><th>Valor</th><th>Comprador</th><th>Justificativa</th></tr></thead>
        <tbody>
          {r.withoutErp.items.map((s) => (
            <tr key={s.number}>
              <td className="whitespace-nowrap">{s.number}<div className="sub">{data(s.issuedOn)}</div></td>
              <td>{s.supplier}{s.costCenter && <div className="sub">{s.costCenter}</div>}</td>
              <td className="whitespace-nowrap">{moeda(s.value)}</td>
              <td>{s.buyer}</td>
              <td className="min-w-[220px]">
                {s.reason ?? <span className="text-perigo">Sem justificativa registrada</span>}
              </td>
            </tr>
          ))}
        </tbody>
      </Bloco>
      </>)}













    </>
  );
}

/**
 * Relatórios (diretoria): um recorte — período, empresa, centro de custo e
 * comprador — respondido por seis leituras, e o mesmo recorte impresso em PDF.
 *
 * O botão do PDF pede o relatório de novo ao servidor em vez de imprimir a tela:
 * é o que garante que a folha levada para a reunião traz o mesmo recorte do
 * cabeçalho, e não a soma de um filtro que alguém mexeu depois de carregar.
 */
export function Relatorios() {
  const [rascunho, setRascunho] = useState<FiltrosRelatorio>(FILTROS_RELATORIO_VAZIOS);
  const [aplicados, setAplicados] = useState<FiltrosRelatorio>(FILTROS_RELATORIO_VAZIOS);
  const [gerando, setGerando] = useState(false);
  const [aba, setAba] = useState<Aba>('geral');
  const { avisar } = useToast();
  // o relatório anterior fica na tela enquanto o novo recorte vem: trocar de
  // filtro não deve apagar o que a pessoa está lendo
  const { dados, erro, carregando } = useCarregar(
    (signal) => relatorioExecutivo(aplicados, signal), [aplicados]);

  const campo = (k: keyof FiltrosRelatorio) => ({
    value: rascunho[k] || (k === 'de' ? dados?.from ?? '' : k === 'ate' ? dados?.to ?? '' : ''),
    onChange: (e: { target: { value: string } }) => setRascunho((f) => ({ ...f, [k]: e.target.value })),
  });
  const fo = dados?.filterOptions;

  function aplicar() {
    const f = { ...rascunho, de: rascunho.de || dados?.from || '', ate: rascunho.ate || dados?.to || '' };
    setRascunho(f);
    setAplicados(f);
  }

  function limpar() {
    setRascunho(FILTROS_RELATORIO_VAZIOS);
    setAplicados({ ...FILTROS_RELATORIO_VAZIOS });
  }

  async function exportar() {
    setGerando(true);
    try {
      avisar('Gerando o PDF do relatório…');
      abrirBlob(await pdfDoRelatorio(aplicados));
    } catch (e) {
      avisar(mensagem(e, 'Falha ao gerar o PDF do relatório.'), 'erro');
    } finally {
      setGerando(false);
    }
  }

  return (
    <>
      <Painel titulo="Relatórios de compras"
        acoes={
          <button type="button" className="botao" onClick={exportar} disabled={gerando || !dados}>
            {gerando ? 'Gerando…' : 'Exportar em PDF'}
          </button>
        }>
        <p className="sub mb-3">
          Um recorte — período, empresa, centro de custo e comprador — lido por catorze ângulos, com o
          período anterior ao lado de cada número. O PDF sai com o mesmo recorte no cabeçalho.
        </p>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-5">
          <Campo id="rel-de" rotulo="De"><input id="rel-de" type="date" {...campo('de')} /></Campo>
          <Campo id="rel-ate" rotulo="Até"><input id="rel-ate" type="date" {...campo('ate')} /></Campo>
          <Campo id="rel-empresa" rotulo="Empresa">
            <select id="rel-empresa" {...campo('empresa')}>
              <option value="">Todas</option>
              {(fo?.companies ?? []).map((e) => <option key={e} value={e}>{e}</option>)}
            </select>
          </Campo>
          <Campo id="rel-cc" rotulo="Centro de custo">
            <select id="rel-cc" {...campo('centroCusto')}>
              <option value="">Todos</option>
              {(fo?.costCenters ?? []).map((c) => (
                <option key={c.code} value={c.code}>{c.code} — {c.name}</option>
              ))}
            </select>
          </Campo>
          <Campo id="rel-comprador" rotulo="Comprador">
            <select id="rel-comprador" {...campo('comprador')}>
              <option value="">Todos</option>
              {(fo?.buyers ?? []).map((b) => <option key={b.id} value={b.id}>{b.label}</option>)}
            </select>
          </Campo>
        </div>
        <div className="mt-3 flex flex-wrap gap-2">
          <button type="button" className="botao" onClick={aplicar}>Aplicar filtros</button>
          <button type="button" className="botao-secundario" onClick={limpar}>Limpar</button>
        </div>
        {dados && (
          <p className="sub mt-3" data-testid="recorte-aplicado">
            {data(dados.from)} a {data(dados.to)} · Empresa: {dados.companyLabel ?? 'todas'} ·
            {' '}Centro de custo: {dados.costCenterLabel ?? 'todos'} ·
            {' '}Comprador: {dados.buyerLabel ?? 'todos'}
          </p>
        )}
      </Painel>

      {erro && <Painel><Erro>{erro}</Erro></Painel>}
      {carregando && !dados && <Painel><Carregando texto="Apurando o recorte…" /></Painel>}
      {dados && <Blocos r={dados} aba={aba} aoMudar={setAba} />}
    </>
  );
}
