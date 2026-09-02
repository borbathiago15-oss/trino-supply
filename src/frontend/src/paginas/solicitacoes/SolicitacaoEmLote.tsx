import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { familiasDoCatalogo, gradeDeLote, type LinhaDeLote } from '@/api/catalogo';
import { listarCentrosCusto, type CentroCusto } from '@/api/centrosCusto';
import { listarLocaisDeEntrega, rotuloDoLocal, type LocalEntrega } from '@/api/locais';
import { criarSolicitacao, ROTULO_PRIORIDADE, type Prioridade } from '@/api/solicitacoes';
import { Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { Campo, Grade2 } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { moeda, quantidade } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';

const VAZIO = {
  local: '', centroCusto: '', prioridade: 'NORMAL' as Prioridade, necessidade: '',
  urgenciaMotivo: '', urgenciaImpacto: '', justificativa: '',
};
type Formulario = typeof VAZIO;

/** Só entram na SC os produtos com quantidade informada. */
export function itensComQuantidade(quantidades: Record<string, string>) {
  return Object.entries(quantidades)
    .map(([catalogItemId, valor]) => ({ catalogItemId, quantity: parseFloat(valor) || 0 }))
    .filter((i) => i.quantity > 0);
}

export function SolicitacaoEmLote() {
  const { avisar } = useToast();
  const navegar = useNavigate();
  const [form, setForm] = useState<Formulario>(VAZIO);
  const [familia, setFamilia] = useState('');
  const [busca, setBusca] = useState('');
  const [filtro, setFiltro] = useState<{ familia: string; q: string }>({ familia: '', q: '' });
  const [quantidades, setQuantidades] = useState<Record<string, string>>({});
  const [enviando, setEnviando] = useState(false);

  const apoio = useCarregar(async (signal) => ({
    familias: await familiasDoCatalogo(signal).catch(() => [] as string[]),
    locais: await listarLocaisDeEntrega(signal).catch(() => [] as LocalEntrega[]),
    centros: await listarCentrosCusto(false, signal).catch(() => [] as CentroCusto[]),
  }), []);

  const grade = useCarregar((signal) => gradeDeLote(filtro, signal), [filtro]);

  const urgente = form.prioridade === 'URGENT';
  const campo = (k: keyof Formulario) => ({
    value: form[k] as string,
    onChange: (ev: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: ev.target.value })),
  });
  const buscar = () => setFiltro({ familia, q: busca.trim() });

  async function gerar(ev: FormEvent) {
    ev.preventDefault();
    const items = itensComQuantidade(quantidades);
    if (!items.length) { avisar('Informe a "Qtd. a Solicitar" de ao menos um produto.', 'erro'); return; }
    setEnviando(true);
    try {
      const criada = await criarSolicitacao({
        justification: form.justificativa,
        costCenter: form.centroCusto,
        priority: form.prioridade,
        neededBy: form.necessidade || null,
        items: items.map((i) => ({ ...i, description: '', unitOfMeasure: null })),
        kind: 'CATALOGO',
        deliveryLocation: form.local || null,
        urgencyReason: form.urgenciaMotivo || null,
        urgencyImpact: form.urgenciaImpacto || null,
      });
      avisar(`SC ${criada.number} criada com ${items.length} item(ns). Revise em “Meus Pedidos” e envie.`);
      setQuantidades({});
      navegar('/solicitacoes');
    } catch (e) { avisar(e instanceof Error ? e.message : 'Falha ao gerar a SC.', 'erro'); }
    finally { setEnviando(false); }
  }

  const linhas = grade.dados ?? [];

  return (
    <Painel titulo="Solicitação em Lote — Gerar SC">
      <form onSubmit={gerar}>
        <Grade2>
          <Campo id="lote-local" rotulo="Local de Entrega">
            <select id="lote-local" {...campo('local')}>
              <option value="">Selecione…</option>
              {(apoio.dados?.locais ?? []).map((l) => (
                <option key={l.id} value={rotuloDoLocal(l)}>{rotuloDoLocal(l)}</option>
              ))}
            </select>
          </Campo>
          <Grade2>
            <Campo id="lote-familia" rotulo="Grupo de Produto">
              <select id="lote-familia" value={familia} onChange={(e) => setFamilia(e.target.value)}>
                <option value="">Todos os grupos</option>
                {(apoio.dados?.familias ?? []).map((f) => <option key={f} value={f}>{f}</option>)}
              </select>
            </Campo>
            <Campo id="lote-busca" rotulo="Buscar">
              <input id="lote-busca" placeholder="código ou descrição" value={busca}
                onChange={(e) => setBusca(e.target.value)}
                onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); buscar(); } }} />
            </Campo>
          </Grade2>
        </Grade2>
        <button type="button" className="botao mt-3" onClick={buscar}>Buscar</button>

        <Grade2 className="mt-4">
          <Campo id="lote-cc" rotulo="Centro de Custo">
            <select id="lote-cc" required {...campo('centroCusto')}>
              <option value="">Selecione o centro de custo…</option>
              {(apoio.dados?.centros ?? []).map((c) => (
                <option key={c.id} value={c.code}>{c.code} — {c.name}</option>
              ))}
            </select>
          </Campo>
          <Grade2>
            <Campo id="lote-prioridade" rotulo="Prioridade">
              <select id="lote-prioridade" {...campo('prioridade')}>
                {(Object.keys(ROTULO_PRIORIDADE) as Prioridade[]).map((p) => (
                  <option key={p} value={p}>{ROTULO_PRIORIDADE[p]}</option>
                ))}
              </select>
            </Campo>
            <Campo id="lote-necessidade" rotulo="Necessidade">
              <input id="lote-necessidade" type="date" {...campo('necessidade')} />
            </Campo>
          </Grade2>
        </Grade2>

        {urgente && (
          <Grade2 className="mt-3">
            <Campo id="lote-urg-motivo" rotulo="Justificativa da urgência" dica="(obrigatória)">
              <input id="lote-urg-motivo" required placeholder="por que é urgente?" {...campo('urgenciaMotivo')} />
            </Campo>
            <Campo id="lote-urg-impacto" rotulo="Impacto se não comprar" dica="(obrigatório)">
              <input id="lote-urg-impacto" required placeholder="o que acontece sem a compra?" {...campo('urgenciaImpacto')} />
            </Campo>
          </Grade2>
        )}

        <Campo id="lote-justificativa" rotulo="Justificativa" className="mt-3">
          <input id="lote-justificativa" required placeholder="ex.: Reposição mensal — cobertura de estoque" {...campo('justificativa')} />
        </Campo>

        <h3 className="mb-2 mt-5 text-[14px] font-bold">Geração de Solicitações de Compra</h3>
        {grade.erro && <Erro>{grade.erro}</Erro>}
        {grade.carregando && !grade.dados && <Carregando />}
        {grade.dados && !linhas.length && <Vazio>Nenhum produto encontrado para o filtro.</Vazio>}
        {linhas.length > 0 && (
          <div className="overflow-x-auto">
            <table data-testid="grade-lote" className="min-w-[900px]">
              <thead>
                <tr>
                  <th>Produto</th><th>Preço un.</th><th>Saldo atual</th><th>Prev. entrada</th>
                  <th>Consumo médio</th><th>Cobertura</th><th>Qtd. a solicitar</th>
                </tr>
              </thead>
              <tbody>
                {linhas.map((i: LinhaDeLote) => (
                  <tr key={i.id} data-produto={i.code}>
                    <td className="min-w-[240px]">
                      <span className="font-semibold">{i.code}</span> <span className="sub">({i.description})</span>
                      <div className="sub">Grupo: {i.family}</div>
                    </td>
                    <td className="whitespace-nowrap">{i.referencePrice != null ? moeda(i.referencePrice) : '—'}</td>
                    <td>{quantidade(i.stockAvailable)}</td>
                    <td>{quantidade(i.inboundQty)}</td>
                    <td className="text-marca">{quantidade(i.avgMonthlyConsumption)}</td>
                    <td>{i.coverageDays != null ? `${i.coverageDays} d` : '—'}</td>
                    <td>
                      <input type="number" min="0.01" step="0.01" className="!w-[96px]"
                        aria-label={`Quantidade a solicitar de ${i.description}`}
                        value={quantidades[i.id] ?? ''}
                        onChange={(e) => setQuantidades((q) => ({ ...q, [i.id]: e.target.value }))} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        <button type="submit" className="botao mt-4 w-full" disabled={enviando}>
          {enviando ? 'Gerando…' : 'Gerar SC com as quantidades informadas'}
        </button>
      </form>
    </Painel>
  );
}
