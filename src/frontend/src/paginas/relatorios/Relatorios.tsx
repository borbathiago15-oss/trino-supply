import { useState } from 'react';
import {
  FILTROS_RELATORIO_VAZIOS, pdfDoRelatorio, relatorioExecutivo,
  type FiltrosRelatorio, type RelatorioExecutivo,
} from '@/api/relatorios';
import { abrirBlob } from '@/api/cliente';
import { Aviso, Carregando, Erro, FaixaKpis, Kpi, Painel, Vazio } from '@/componentes/basicos';
import { Campo } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { data, moeda, quantidade } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

const pct = (v: number | null | undefined) => (v == null ? '—' : `${quantidade(v)}%`);

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
      <p className="sub mb-3">{explicacao}</p>
      {vazio ? <Vazio>{vazio}</Vazio> : (
        <div className="overflow-x-auto">
          <table data-testid={testid} className={largura}>{children}</table>
        </div>
      )}
    </Painel>
  );
}

function Blocos({ r }: { r: RelatorioExecutivo }) {
  return (
    <>
      <FaixaKpis>
        <Kpi rotulo="Total comprado" valor={moeda(r.kpis.spend)}
          detalhe={`${quantidade(r.kpis.orders)} pedido(s) · ${quantidade(r.kpis.suppliers)} fornecedor(es)`} />
        <Kpi rotulo="Saving negociado" valor={moeda(r.kpis.savingTotal)}
          detalhe={r.kpis.savingPercent != null ? `${quantidade(r.kpis.savingPercent)}% da primeira proposta` : 'sem processo negociado'} />
        <Kpi rotulo="Compras urgentes" valor={pct(r.kpis.urgentPercent)} detalhe="do valor do período" />
        <Kpi rotulo="OTIF" valor={pct(r.kpis.otifPercent)} detalhe="entregas encerradas e medidas" />
        <Kpi rotulo="Sem O.C. do ERP" valor={moeda(r.kpis.withoutErpValue)}
          detalhe={`${quantidade(r.withoutErp.orders)} compra(s) fechada(s) pela exceção`} />
      </FaixaKpis>

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

      <Bloco titulo="3. Concentração por fornecedor" testid="relatorio-fornecedores"
        explicacao={`${quantidade(r.suppliers.supplierCount)} fornecedor(es) no recorte · maior fatia ${pct(r.suppliers.top1Percent)} · 3 maiores ${pct(r.suppliers.top3Percent)} · 5 maiores ${pct(r.suppliers.top5Percent)}.`}
        vazio={r.suppliers.rows.length ? undefined : 'Nenhum pedido no recorte.'}>
        <thead><tr><th>Fornecedor</th><th>Pedidos</th><th>Valor</th><th>% do total</th></tr></thead>
        <tbody>
          {r.suppliers.rows.map((f) => (
            <tr key={f.supplier}>
              <td>{f.supplier}</td>
              <td>{quantidade(f.orders)}</td>
              <td className="whitespace-nowrap">{moeda(f.value)}</td>
              <td><Fatia percent={f.percent} /></td>
            </tr>
          ))}
        </tbody>
      </Bloco>

      <Bloco titulo="4. Peso das compras urgentes" testid="relatorio-urgentes" largura="min-w-[760px]"
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

      <Bloco titulo="5. Entrega no prazo (OTIF) por fornecedor" testid="relatorio-otif"
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

      <Bloco titulo="6. Compras sem O.C. do ERP" testid="relatorio-sem-oc" largura="min-w-[760px]"
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
          Um recorte — período, empresa, centro de custo e comprador — lido por seis ângulos.
          O PDF sai com o mesmo recorte no cabeçalho.
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
      {dados && <Blocos r={dados} />}
    </>
  );
}
