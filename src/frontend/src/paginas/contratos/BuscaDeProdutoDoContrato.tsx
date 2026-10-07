import { useState } from 'react';
import { buscarProdutos, type Produto } from '@/api/catalogo';
import { Carregando, Erro } from '@/componentes/basicos';
import { Dialogo } from '@/componentes/Dialogo';
import { Campo } from '@/componentes/formulario';
import { podeBuscar } from '@/dominio/buscaDeProduto';
import { moeda } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';
import { useDebounce } from '@/util/useDebounce';

/** Quantos produtos a lista mostra antes de pedir um recorte melhor. */
export const TETO_DA_BUSCA = 40;

/**
 * Escolher o produto do contrato <strong>buscando, não rolando</strong>.
 *
 * <p>
 * O campo era um <code>select</code> com o catálogo inteiro dentro: num acervo de milhares de
 * itens, achar a bota era rolar a lista até ela — e o <code>select</code> nativo não deixa
 * digitar para filtrar. É o mesmo problema que a SC já tinha resolvido, e por isso a régua da
 * busca é a <strong>mesma</strong> (<code>dominio/buscaDeProduto</code>): família ou duas
 * letras antes de consultar. Repetir o piso aqui faria as telas divergirem no primeiro ajuste.
 * </p>
 *
 * <p>
 * O resultado é o <strong>produto individual</strong>, e não a grade junta como na SC: no
 * contrato a bota 38 e a 39 têm preço próprio — são linhas diferentes do contrato, porque é o
 * preço de cada uma que fica fixo. Agrupar a grade aqui obrigaria a desfazê-la na linha seguinte.
 * </p>
 */
export function BuscaDeProdutoDoContrato({ familias, aoEscolher, aoFechar }: {
  familias: string[];
  aoEscolher: (p: Produto) => void;
  aoFechar: () => void;
}) {
  const [familia, setFamilia] = useState('');
  const [busca, setBusca] = useState('');
  const termo = useDebounce(busca);
  const buscar = podeBuscar(familia, termo);
  const { dados, erro, carregando } = useCarregar(
    async (signal) => (buscar ? buscarProdutos({ q: termo, familia }, signal) : []),
    [familia, termo, buscar],
  );

  const achados = dados ?? [];
  const mostrados = achados.slice(0, TETO_DA_BUSCA);

  return (
    <Dialogo titulo="Escolher produto do contrato" aoFechar={aoFechar} largura="max-w-[760px]"
      acoes={<button type="button" className="botao-secundario" onClick={aoFechar}>Fechar</button>}>
      <div className="grid gap-3 sm:grid-cols-[220px_1fr]">
        <Campo id="ct-busca-familia" rotulo="Família">
          <select id="ct-busca-familia" value={familia} onChange={(e) => setFamilia(e.target.value)}>
            <option value="">Todas as famílias</option>
            {familias.map((f) => <option key={f} value={f}>{f}</option>)}
          </select>
        </Campo>
        <Campo id="ct-busca-termo" rotulo="Buscar produto" dica="(código ou parte da descrição)">
          <input id="ct-busca-termo" autoFocus placeholder="ex.: balde, luva, 02090081"
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
          <p className="sub py-6 text-center">Nenhum produto encontrado com esse termo.</p>
        )}
        {mostrados.length > 0 && (
          <ul className="divide-y divide-borda" data-testid="produtos-do-contrato">
            {mostrados.map((p) => (
              <li key={p.id}>
                <button type="button" data-produto={p.code} onClick={() => aoEscolher(p)}
                  className="flex w-full flex-wrap items-center gap-x-3 gap-y-1 px-1 py-2.5 text-left hover:bg-superficie-suave">
                  <span className="font-semibold">{p.description}</span>
                  <span className="sub">[{p.code}] · {p.family} · {p.unitOfMeasure}</span>
                  {p.referencePrice != null && <span className="sub">ref. {moeda(p.referencePrice)}</span>}
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
        O preço, o prazo de entrega e a condição de pagamento são fixados <strong>por produto</strong>.
        Produto com tamanho entra uma linha por tamanho: a bota 38 e a 39 têm preço próprio.
      </p>
    </Dialogo>
  );
}
