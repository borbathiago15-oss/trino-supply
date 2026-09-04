import { useMemo, useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { buscarProdutos, familiasDoCatalogo, type Produto } from '@/api/catalogo';
import { listarCentrosCusto } from '@/api/centrosCusto';
import { criarSolicitacaoMaterial } from '@/api/material';
import { Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { Campo, Grade2, Nota } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { useCarregar } from '@/util/useCarregar';

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

/** C.A. do EPI vem do par produto+fornecedor — mostra todos os que houver. */
export const casDoProduto = (p: Produto) =>
  (p.suppliers ?? []).filter((f) => f.caNumber).map((f) => `${f.caNumber} (${f.supplierName})`).join(' · ');

export interface Escolha { marcado: boolean; quantidade: string }
const VAZIA: Escolha = { marcado: false, quantidade: '' };

/**
 * Traduz a grade em itens para a API e diz o que falta. As duas mensagens são
 * as do legado: marcar sem quantidade era o erro mais comum da tela.
 */
export function itensEscolhidos(escolhas: Record<string, Escolha>) {
  const marcados = Object.entries(escolhas).filter(([, e]) => e.marcado);
  const items = marcados
    .map(([catalogItemId, e]) => ({ catalogItemId, quantity: Number(e.quantidade.replace(',', '.')) }))
    .filter((i) => i.quantity > 0);
  if (!marcados.length) return { items: [], erro: 'Marque ao menos um produto da lista.' };
  if (items.length < marcados.length) return { items: [], erro: 'Informe a quantidade dos itens marcados.' };
  return { items, erro: null };
}

export function SolicitarMaterial() {
  const { avisar } = useToast();
  const navegar = useNavigate();
  const [centroCusto, setCentroCusto] = useState('');
  const [observacoes, setObservacoes] = useState('');
  const [familia, setFamilia] = useState('');
  const [escolhas, setEscolhas] = useState<Record<string, Escolha>>({});
  const [enviando, setEnviando] = useState(false);

  const base = useCarregar(async (signal) => ({
    centros: await listarCentrosCusto(false, signal),
    familias: await familiasDoCatalogo(signal),
  }), []);

  const produtos = useCarregar(
    async (signal) => (familia ? buscarProdutos({ familia }, signal) : []),
    [familia],
  );

  const lista = produtos.dados ?? [];
  const marcados = useMemo(() => Object.values(escolhas).filter((e) => e.marcado).length, [escolhas]);

  const mexer = (id: string, mudanca: Partial<Escolha>) =>
    setEscolhas((e) => ({ ...e, [id]: { ...VAZIA, ...e[id], ...mudanca } }));

  async function enviar(ev: FormEvent) {
    ev.preventDefault();
    const { items, erro } = itensEscolhidos(escolhas);
    if (erro) { avisar(erro, 'erro'); return; }
    setEnviando(true);
    try {
      await criarSolicitacaoMaterial({ costCenter: centroCusto, notes: observacoes || null, items });
      avisar('Solicitação enviada ao almoxarifado.');
      navegar('/material');
    } catch (e) { avisar(mensagem(e, 'Falha ao enviar a solicitação.'), 'erro'); }
    finally { setEnviando(false); }
  }

  return (
    <Painel titulo="Solicitar Material ao Almoxarifado">
      <Nota>
        Escolha os itens e as quantidades. A solicitação vai para a aprovação do responsável do seu
        centro de custo e, depois de liberada, para o almoxarifado atender — o que não houver em
        estoque vira solicitação de compra automaticamente.
      </Nota>

      {base.erro && <Erro>{base.erro}</Erro>}
      {base.carregando && !base.dados && <Carregando />}

      {base.dados && (
        <form className="mt-3" onSubmit={enviar}>
          <Grade2>
            <Campo id="mr-cc" rotulo="Centro de custo">
              <select id="mr-cc" required value={centroCusto} onChange={(e) => setCentroCusto(e.target.value)}>
                <option value="">Selecione o centro de custo…</option>
                {base.dados.centros.map((c) => (
                  <option key={c.id} value={c.code}>{c.code} — {c.name}</option>
                ))}
              </select>
            </Campo>
            <Campo id="mr-notes" rotulo="Observações">
              <input id="mr-notes" placeholder="opcional" value={observacoes}
                onChange={(e) => setObservacoes(e.target.value)} />
            </Campo>
          </Grade2>

          <Campo id="mr-family" rotulo="Família de produtos" className="mt-3">
            <select id="mr-family" value={familia} onChange={(e) => setFamilia(e.target.value)}>
              <option value="">Selecione a família…</option>
              {base.dados.familias.map((f) => <option key={f} value={f}>{f}</option>)}
            </select>
          </Campo>

          <div className="mt-3">
            {produtos.erro && <Erro>{produtos.erro}</Erro>}
            {!familia && <Vazio>Escolha uma família para ver os produtos.</Vazio>}
            {familia && produtos.carregando && <Carregando texto="Carregando os produtos…" />}
            {familia && !produtos.carregando && !lista.length && (
              <Vazio>Nenhum produto cadastrado nesta família.</Vazio>
            )}
            {lista.length > 0 && (
              <div className="overflow-x-auto">
                <table data-testid="grade-produtos" className="min-w-[720px]">
                  <thead>
                    <tr>
                      <th className="w-10"></th><th>Produto</th><th>Tam.</th><th>C.A.</th>
                      <th>Unid.</th><th className="w-32">Qtd</th>
                    </tr>
                  </thead>
                  <tbody>
                    {lista.map((p) => {
                      const escolha = escolhas[p.id] ?? VAZIA;
                      const cas = casDoProduto(p);
                      return (
                        <tr key={p.id} data-produto={p.code}>
                          <td>
                            <input type="checkbox" className="w-auto" checked={escolha.marcado}
                              aria-label={`Selecionar ${p.description}`}
                              onChange={(e) => mexer(p.id, { marcado: e.target.checked })} />
                          </td>
                          <td>{p.description}<div className="sub">{p.code}</div></td>
                          <td>{p.size || '—'}</td>
                          <td className="sub">{cas || '—'}</td>
                          <td>{p.unitOfMeasure}</td>
                          <td>
                            <input type="number" min="0.01" step="0.01" placeholder="0" value={escolha.quantidade}
                              aria-label={`Quantidade de ${p.description}`}
                              onChange={(e) => mexer(p.id, { quantidade: e.target.value })} />
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </div>

          <div className="mt-4 flex flex-wrap items-center gap-3">
            <button type="submit" className="botao" disabled={enviando}>
              {enviando ? 'Enviando…' : 'Enviar ao almoxarifado'}
            </button>
            <span className="sub">{marcados} produto(s) marcado(s).</span>
          </div>
        </form>
      )}
    </Painel>
  );
}
