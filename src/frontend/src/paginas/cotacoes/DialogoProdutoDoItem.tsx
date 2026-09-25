import { useEffect, useState, type FormEvent } from 'react';
import { buscarProdutos, type Produto } from '@/api/catalogo';
import { definirProdutoDoItem, type ItemDoProcesso, type Processo } from '@/api/cotacoes';
import { listarFamilias, type Familia } from '@/api/familias';
import { Dialogo } from '@/componentes/Dialogo';
import { Campo, Grade2, Nota } from '@/componentes/formulario';
import { moeda } from '@/util/formato';

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

type Modo = 'existente' | 'novo';

/**
 * O item digitado vira produto do catálogo antes da escolha do vencedor (RFQ-ERR-026). Primeiro
 * se procura — o produto pode já existir com outro nome —, e só então se cadastra: o termo da
 * busca começa com a descrição do item, e o cadastro novo começa preenchido com ela.
 */
export function DialogoProdutoDoItem({ processo, item, aoConcluir, aoFechar }: {
  processo: Processo; item: ItemDoProcesso; aoConcluir: (q: Processo) => void; aoFechar: () => void;
}) {
  const [modo, setModo] = useState<Modo>('existente');
  const [termo, setTermo] = useState(item.description.split(/\s+/).slice(0, 2).join(' '));
  const [achados, setAchados] = useState<Produto[] | null>(null);
  const [buscando, setBuscando] = useState(false);
  const [familias, setFamilias] = useState<Familia[]>([]);
  const [novo, setNovo] = useState({
    code: '', description: item.description, family: '', unitOfMeasure: item.unitOfMeasure, referencePrice: '',
  });
  const [salvando, setSalvando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);

  useEffect(() => {
    const c = new AbortController();
    listarFamilias(false, c.signal).then(setFamilias).catch(() => setFamilias([]));
    return () => c.abort();
  }, []);

  async function buscar(ev?: FormEvent) {
    ev?.preventDefault();
    if (termo.trim().length < 2) { setErro('Digite ao menos duas letras para buscar.'); return; }
    setErro(null); setBuscando(true);
    try { setAchados((await buscarProdutos({ q: termo.trim() })).filter((p) => p.active)); }
    catch (e) { setErro(mensagem(e, 'Falha ao buscar no catálogo.')); }
    finally { setBuscando(false); }
  }

  useEffect(() => { void buscar(); }, []); // eslint-disable-line react-hooks/exhaustive-deps

  async function usar(produtoId: string) {
    setSalvando(true); setErro(null);
    try { aoConcluir(await definirProdutoDoItem(processo.id, item.id, { catalogItemId: produtoId })); }
    catch (e) { setErro(mensagem(e, 'Falha ao usar o produto.')); }
    finally { setSalvando(false); }
  }

  async function cadastrar(ev: FormEvent) {
    ev.preventDefault();
    if (novo.description.trim().length < 3) { setErro('Descreva o produto (mín. 3 caracteres).'); return; }
    if (!novo.family) { setErro('Escolha a família do produto.'); return; }
    const preco = novo.referencePrice.trim() ? Number(novo.referencePrice.replace(',', '.')) : null;
    if (preco != null && (Number.isNaN(preco) || preco < 0)) { setErro('O preço de referência não pode ser negativo.'); return; }
    setSalvando(true); setErro(null);
    try {
      aoConcluir(await definirProdutoDoItem(processo.id, item.id, {
        newProduct: {
          code: novo.code.trim() || null, description: novo.description.trim(), family: novo.family,
          unitOfMeasure: novo.unitOfMeasure.trim() || 'UN', referencePrice: preco,
        },
      }));
    } catch (e) { setErro(mensagem(e, 'Falha ao cadastrar o produto.')); }
    finally { setSalvando(false); }
  }

  const campo = (k: keyof typeof novo) => ({
    value: novo[k],
    onChange: (ev: { target: { value: string } }) => setNovo((n) => ({ ...n, [k]: ev.target.value })),
  });

  return (
    <Dialogo titulo={`Cadastrar produto — ${item.description}`} aoFechar={aoFechar} largura="max-w-[680px]">
      <Nota>
        Item fora do catálogo não é comprado: antes de escolher o vencedor, ele vira produto (RFQ-ERR-026).
        A quantidade ({item.quantity} {item.unitOfMeasure}) e as propostas já recebidas continuam valendo.
      </Nota>

      <div className="mt-3 flex gap-2" role="tablist">
        <button type="button" role="tab" aria-selected={modo === 'existente'}
          className={modo === 'existente' ? 'botao' : 'botao-secundario'} onClick={() => setModo('existente')}>
          Usar produto que já existe
        </button>
        <button type="button" role="tab" aria-selected={modo === 'novo'}
          className={modo === 'novo' ? 'botao' : 'botao-secundario'} onClick={() => setModo('novo')}>
          Cadastrar produto novo
        </button>
      </div>

      {erro && <p className="mt-3 rounded-lg bg-perigo-fundo px-3 py-2 text-[13px] text-perigo" role="alert">{erro}</p>}

      {modo === 'existente' && (
        <div className="mt-3">
          <form className="flex items-end gap-2" onSubmit={buscar}>
            <Campo id="pdi-termo" rotulo="Buscar no catálogo" className="flex-1">
              <input id="pdi-termo" value={termo} onChange={(e) => setTermo(e.target.value)} placeholder="código ou parte da descrição" />
            </Campo>
            <button type="submit" className="botao-secundario" disabled={buscando}>{buscando ? 'Buscando…' : 'Buscar'}</button>
          </form>
          {achados && achados.length === 0 && (
            <p className="sub mt-3">Nada encontrado com “{termo}”. Tente outro termo ou cadastre o produto novo.</p>
          )}
          {achados && achados.length > 0 && (
            <ul className="mt-3 max-h-72 divide-y divide-borda overflow-y-auto rounded-lg border border-borda" data-testid="achados-catalogo">
              {achados.slice(0, 30).map((p) => (
                <li key={p.id} className="flex items-center justify-between gap-3 px-3 py-2">
                  <div>
                    <div className="text-[13.5px]"><span className="sub">[{p.code}]</span> {p.description}</div>
                    <div className="sub">{p.family} · {p.unitOfMeasure}{p.referencePrice != null ? ` · ref. ${moeda(p.referencePrice)}` : ''}</div>
                  </div>
                  <button type="button" className="botao-secundario !py-1.5" disabled={salvando} onClick={() => usar(p.id)}>
                    Usar este
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

      {modo === 'novo' && (
        <form className="mt-3" onSubmit={cadastrar} data-testid="cadastro-produto-do-item">
          <Campo id="pdi-descricao" rotulo="Descrição do produto">
            <input id="pdi-descricao" required minLength={3} {...campo('description')} />
          </Campo>
          <Grade2 className="mt-3">
            <Campo id="pdi-familia" rotulo="Família">
              <select id="pdi-familia" required {...campo('family')}>
                <option value="">Escolha a família…</option>
                {familias.map((f) => <option key={f.id} value={f.name}>{f.name}</option>)}
              </select>
            </Campo>
            <Campo id="pdi-unidade" rotulo="Unidade">
              <input id="pdi-unidade" {...campo('unitOfMeasure')} />
            </Campo>
            <Campo id="pdi-codigo" rotulo="Código" dica="(vazio: o sistema gera pela família)">
              <input id="pdi-codigo" {...campo('code')} />
            </Campo>
            <Campo id="pdi-preco" rotulo="Preço de referência" dica="(opcional)">
              <input id="pdi-preco" type="number" step="0.01" min="0" {...campo('referencePrice')} />
            </Campo>
          </Grade2>
          <div className="mt-4 flex justify-end gap-2">
            <button type="button" className="botao-secundario" onClick={aoFechar}>Cancelar</button>
            <button type="submit" className="botao" disabled={salvando}>{salvando ? 'Cadastrando…' : 'Cadastrar e usar no item'}</button>
          </div>
        </form>
      )}
    </Dialogo>
  );
}
