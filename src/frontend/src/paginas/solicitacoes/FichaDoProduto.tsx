import { useEffect, useState } from 'react';
import { fichaDoProduto, type ProdutoParaEscolha } from '@/api/catalogo';
import { urlDocumento } from '@/api/documentos';
import { Carregando, Erro } from '@/componentes/basicos';
import { moeda, quantidade } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';

/** Foto grande da ficha. Documento exige token, então vira URL de blob, como a miniatura. */
function Foto({ documentId, descricao }: { documentId: string | null; descricao: string }) {
  const [url, setUrl] = useState<string | null>(null);
  const [falhou, setFalhou] = useState(false);
  useEffect(() => {
    if (!documentId) return;
    let vivo = true;
    urlDocumento(documentId).then((u) => { if (vivo) setUrl(u); }).catch(() => { if (vivo) setFalhou(true); });
    return () => { vivo = false; };
  }, [documentId]);

  const moldura = 'flex h-44 w-44 shrink-0 items-center justify-center rounded-lg border border-borda bg-slate-50';
  if (!documentId || falhou) return <div className={moldura}><span className="sub text-center">sem foto no cadastro</span></div>;
  if (!url) return <div className={moldura + ' animate-pulse'} />;
  return <img src={url} alt={descricao} data-testid="foto-do-produto" className="h-44 w-44 shrink-0 rounded-lg border border-borda object-contain" />;
}

const Linha = ({ rotulo, children }: { rotulo: string; children: React.ReactNode }) => (
  <div className="flex gap-2 border-b border-slate-100 py-1.5 text-[13px]">
    <dt className="w-40 shrink-0 text-texto-suave">{rotulo}</dt>
    <dd className="min-w-0 flex-1">{children}</dd>
  </div>
);

/**
 * A ficha do produto, dentro da busca: foto, código, descrição e todo o cadastro — para quem
 * pede ter certeza de que é aquele antes de escolher. A lista mostra uma linha; a dúvida entre
 * "bota biqueira de PVC" e "bota biqueira de composite" se resolve olhando, não adivinhando.
 *
 * O produto com grade abre a ficha do primeiro tamanho: descrição, família e fornecedores são
 * os mesmos, e a tabela de tamanhos mostra o que muda de um para outro (código, preço, C.A.).
 */
export function FichaDoProduto({ produto, aoUsar, aoVoltar }:
  { produto: ProdutoParaEscolha; aoUsar: () => void; aoVoltar: () => void }) {
  const primeiro = produto.sizes[0];
  const { dados: ficha, erro } = useCarregar((s) => fichaDoProduto(primeiro.id, s), [primeiro.id]);
  const foto = ficha?.imageDocumentId ?? produto.sizes.find((v) => v.imageDocumentId)?.imageDocumentId ?? null;

  return (
    <div data-testid="ficha-do-produto">
      {erro && <Erro>{erro}</Erro>}
      {!ficha && !erro && <Carregando texto="Abrindo a ficha…" />}
      {ficha && (
        <>
          <div className="flex flex-col gap-4 sm:flex-row">
            <Foto documentId={foto} descricao={produto.description} />
            <div className="min-w-0 flex-1">
              <h3 className="text-[16px] font-bold text-slate-900">{produto.description}</h3>
              <p className="sub mb-2">{produto.baseCode ?? primeiro.code}</p>
              <dl>
                <Linha rotulo="Família">{ficha.family}</Linha>
                <Linha rotulo="Tipo de produto">{ficha.productTypeLabel ?? '—'}</Linha>
                <Linha rotulo="Unidade">{ficha.unitOfMeasure}</Linha>
                {!produto.hasGrade && (
                  <Linha rotulo="Preço de referência">{ficha.referencePrice != null ? moeda(ficha.referencePrice) : '—'}</Linha>
                )}
                <Linha rotulo="Controla estoque">{ficha.stockControlled ? 'Sim' : 'Não'}</Linha>
                <Linha rotulo="Estoque mínimo">{ficha.minimumQty != null ? quantidade(ficha.minimumQty) : '—'}</Linha>
                <Linha rotulo="Pode ser comprado">{ficha.purchasable ? 'Sim' : 'Não'}</Linha>
              </dl>
            </div>
          </div>

          {produto.hasGrade && (
            <div className="mt-4">
              <p className="rotulo mb-1">Tamanhos</p>
              <table data-testid="tamanhos-da-ficha">
                <thead><tr><th>Tamanho</th><th>Código</th><th>Preço de referência</th><th>C.A.</th></tr></thead>
                <tbody>
                  {produto.sizes.map((v) => (
                    <tr key={v.id}>
                      <td className="font-semibold">{v.size}</td>
                      <td>{v.code}</td>
                      <td>{v.referencePrice != null ? moeda(v.referencePrice) : '—'}</td>
                      <td className={v.compliancePending ? 'text-perigo' : ''}>{v.compliancePending ? 'sem C.A.' : 'ok'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          <div className="mt-4">
            <p className="rotulo mb-1">Fornecedores do produto</p>
            {ficha.suppliers.length === 0 ? (
              <p className="sub">Nenhum fornecedor no cadastro deste produto.</p>
            ) : (
              <table data-testid="fornecedores-da-ficha">
                <thead><tr><th>Fornecedor</th><th>Código no fornecedor</th><th>C.A.</th><th>Último preço</th></tr></thead>
                <tbody>
                  {ficha.suppliers.map((f) => (
                    <tr key={f.id ?? f.supplierName}>
                      <td>{f.supplierName}</td>
                      <td>{f.supplierItemCode ?? '—'}</td>
                      <td>{f.caNumber ?? '—'}</td>
                      <td>{f.lastPrice != null ? moeda(f.lastPrice) : '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>

          {produto.compliancePending && (
            <p role="note" className="mt-3 rounded-lg bg-perigo-fundo px-3 py-2 text-[13px] text-perigo">
              {produto.productTypeLabel ?? 'EPI/EPC'} sem C.A. em nenhum fornecedor — não pode ser solicitado (IC-ERR-023).
            </p>
          )}
        </>
      )}

      <div className="mt-4 flex flex-wrap justify-between gap-2">
        <button type="button" className="botao-secundario" onClick={aoVoltar}>← Voltar à busca</button>
        <button type="button" className="botao" disabled={!ficha || produto.compliancePending} onClick={aoUsar}>
          Usar este produto
        </button>
      </div>
    </div>
  );
}
