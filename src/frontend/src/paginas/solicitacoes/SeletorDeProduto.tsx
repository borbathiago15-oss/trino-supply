import { useState } from 'react';
import { produtosParaEscolha, type ProdutoParaEscolha } from '@/api/catalogo';
import { Carregando, Erro } from '@/componentes/basicos';
import { Dialogo } from '@/componentes/Dialogo';
import { Campo } from '@/componentes/formulario';
import { moeda } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';
import { useDebounce } from '@/util/useDebounce';

/** Quantos produtos a lista mostra antes de pedir um recorte melhor. */
export const TETO_DA_LISTA = 40;

/**
 * Com o que a busca é feita. Sem família e sem termo não se busca nada: o catálogo tem
 * milhares de itens, e listar todos é o que fazia o solicitante rolar a tela atrás da bota.
 */
export const podeBuscar = (familia: string, termo: string) => !!familia || termo.trim().length >= 2;

/**
 * Escolha do produto na SC: primeiro a família, depois a busca — e o resultado vem por
 * produto, com a grade de tamanhos junta, em vez de uma linha por tamanho.
 */
export function SeletorDeProduto({ familias, aoEscolher, aoFechar }: {
  familias: string[];
  aoEscolher: (p: ProdutoParaEscolha) => void;
  aoFechar: () => void;
}) {
  const [familia, setFamilia] = useState('');
  const [busca, setBusca] = useState('');
  const termo = useDebounce(busca);
  const buscar = podeBuscar(familia, termo);
  const { dados, erro, carregando } = useCarregar(
    async (signal) => (buscar ? produtosParaEscolha({ familia, q: termo }, signal) : []),
    [familia, termo, buscar],
  );

  const achados = dados ?? [];
  const mostrados = achados.slice(0, TETO_DA_LISTA);

  return (
    <Dialogo titulo="Escolher produto do catálogo" aoFechar={aoFechar} largura="max-w-[760px]"
      acoes={<button type="button" className="botao-secundario" onClick={aoFechar}>Fechar</button>}>
      <div className="grid gap-3 sm:grid-cols-[220px_1fr]">
        <Campo id="sel-familia" rotulo="Família">
          <select id="sel-familia" value={familia} onChange={(e) => setFamilia(e.target.value)}>
            <option value="">Todas as famílias</option>
            {familias.map((f) => <option key={f} value={f}>{f}</option>)}
          </select>
        </Campo>
        <Campo id="sel-busca" rotulo="Buscar produto" dica="(código ou parte da descrição)">
          <input id="sel-busca" autoFocus placeholder="ex.: bota, luva, 12003"
            value={busca} onChange={(e) => setBusca(e.target.value)} />
        </Campo>
      </div>

      <div className="mt-3 max-h-[46vh] overflow-y-auto">
        {erro && <Erro>{erro}</Erro>}
        {!buscar && (
          <p className="sub py-6 text-center">Escolha a família ou digite ao menos duas letras para buscar.</p>
        )}
        {buscar && carregando && !dados && <Carregando texto="Buscando no catálogo…" />}
        {buscar && dados && !achados.length && (
          <p className="sub py-6 text-center">
            Nenhum produto encontrado. Feche e descreva o item — a SC aceita produto fora do catálogo.
          </p>
        )}
        {mostrados.length > 0 && (
          <ul className="divide-y divide-borda" data-testid="produtos-encontrados">
            {mostrados.map((p) => (
              <li key={p.key}>
                <button type="button" data-produto={p.baseCode ?? p.sizes[0]?.code}
                  disabled={p.compliancePending}
                  onClick={() => aoEscolher(p)}
                  className="flex w-full flex-wrap items-center gap-x-3 gap-y-1 px-1 py-2.5 text-left hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-60">
                  <span className="font-semibold">{p.description}</span>
                  <span className="sub">{p.baseCode ?? p.sizes[0]?.code} · {p.family} · {p.unitOfMeasure}</span>
                  {p.hasGrade && (
                    <span className="rounded-full bg-marca/10 px-2 py-0.5 text-[11.5px] font-semibold text-marca">
                      {p.sizes.length} tamanho(s): {p.sizes.map((v) => v.size).join(', ')}
                    </span>
                  )}
                  {p.sizes[0]?.referencePrice != null && !p.hasGrade && (
                    <span className="sub">ref. {moeda(p.sizes[0].referencePrice)}</span>
                  )}
                  {p.compliancePending && (
                    <span className="text-[12px] font-semibold text-perigo">
                      {p.productTypeLabel ?? 'EPI/EPC'} sem C.A. — não pode ser solicitado (IC-ERR-023)
                    </span>
                  )}
                </button>
              </li>
            ))}
          </ul>
        )}
        {achados.length > mostrados.length && (
          <p className="sub mt-2 text-center">
            Mostrando {mostrados.length} de {achados.length}. Refine a busca para ver o resto.
          </p>
        )}
      </div>
      <p className="sub mt-3">
        Produto com tamanho (bota, luva, fardamento) vem com a grade: você escolhe o produto uma vez
        e informa a quantidade de cada tamanho.
      </p>
    </Dialogo>
  );
}
