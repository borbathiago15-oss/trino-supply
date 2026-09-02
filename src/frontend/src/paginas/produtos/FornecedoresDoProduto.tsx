import type { FornecedorDoProduto } from '@/api/catalogo';
import type { Fornecedor } from '@/api/fornecedores';
import { Campo, Grade2 } from '@/componentes/formulario';

export interface LinhaFornecedor extends Omit<FornecedorDoProduto, 'id'> {
  chave: string;
  /** Preço e C.A. ficam como texto enquanto o formulário está aberto. */
  precoTexto: string;
}

let sequencia = 0;
export const novaLinhaFornecedor = (): LinhaFornecedor => ({
  chave: 'f' + ++sequencia, supplierId: null, supplierName: '', taxId: null, contact: null,
  supplierItemCode: null, lastPrice: null, caNumber: null, notes: null, precoTexto: '',
});

export const daFornecedorDoProduto = (f: FornecedorDoProduto): LinhaFornecedor => ({
  ...f, chave: 'f' + ++sequencia, precoTexto: f.lastPrice != null ? String(f.lastPrice) : '',
});

/** Linhas do formulário viram o corpo da API; linha sem nome de fornecedor é descartada. */
export const fornecedoresDoFormulario = (linhas: LinhaFornecedor[]) =>
  linhas
    .map(({ chave: _chave, precoTexto, ...f }) => ({
      ...f,
      supplierName: f.supplierName.trim(),
      lastPrice: precoTexto ? parseFloat(precoTexto) : null,
    }))
    .filter((f) => f.supplierName);

/** EPI e EPC só passam quando pelo menos um fornecedor tem o C.A. informado. */
export const faltaCa = (linhas: LinhaFornecedor[], exigeCa: boolean) =>
  exigeCa && !fornecedoresDoFormulario(linhas).some((f) => f.caNumber?.trim());

const FORA_DO_CADASTRO = '__outro';

export function FornecedoresDoProduto({ linhas, fornecedores, exigeCa, aoMudar }: {
  linhas: LinhaFornecedor[];
  fornecedores: Fornecedor[];
  exigeCa: boolean;
  aoMudar: (linhas: LinhaFornecedor[]) => void;
}) {
  const editar = (chave: string, campos: Partial<LinhaFornecedor>) =>
    aoMudar(linhas.map((l) => (l.chave === chave ? { ...l, ...campos } : l)));

  /** Escolher um fornecedor do cadastro traz nome, CNPJ e contato junto. */
  function escolher(linha: LinhaFornecedor, valor: string) {
    if (valor === FORA_DO_CADASTRO || !valor) {
      editar(linha.chave, { supplierId: null });
      return;
    }
    const f = fornecedores.find((x) => x.id === valor);
    if (!f) return;
    editar(linha.chave, {
      supplierId: f.id,
      supplierName: f.legalName || f.tradeName || '',
      taxId: linha.taxId || f.taxId,
      contact: linha.contact || f.email || f.phone,
    });
  }

  return (
    <div className="flex flex-col gap-3">
      {linhas.map((l) => (
        <div key={l.chave} className="rounded-lg border border-borda p-3" data-linha-fornecedor>
          <Grade2>
            <Campo rotulo="Fornecedor" dica="(do cadastro de fornecedores)">
              <select aria-label="Fornecedor do produto" value={l.supplierId ?? FORA_DO_CADASTRO}
                onChange={(e) => escolher(l, e.target.value)}>
                {fornecedores.map((f) => (
                  <option key={f.id} value={f.id}>{f.legalName || f.tradeName}</option>
                ))}
                <option value={FORA_DO_CADASTRO}>Outro — não cadastrado</option>
              </select>
            </Campo>
            <Grade2>
              <Campo rotulo="CNPJ/CPF">
                <input aria-label="CNPJ do fornecedor" placeholder="preenchido pelo cadastro"
                  value={l.taxId ?? ''} onChange={(e) => editar(l.chave, { taxId: e.target.value })} />
              </Campo>
              <Campo rotulo="Contato">
                <input aria-label="Contato do fornecedor" placeholder="telefone, e-mail ou vendedor"
                  value={l.contact ?? ''} onChange={(e) => editar(l.chave, { contact: e.target.value })} />
              </Campo>
            </Grade2>
          </Grade2>

          {!l.supplierId && (
            <Campo rotulo="Nome do fornecedor" dica="(fora do cadastro)" className="mt-3">
              <input aria-label="Nome do fornecedor fora do cadastro" placeholder="razão social ou nome de mercado"
                value={l.supplierName} onChange={(e) => editar(l.chave, { supplierName: e.target.value })} />
            </Campo>
          )}

          <Grade2 className="mt-3">
            <Campo rotulo="Código no fornecedor">
              <input aria-label="Código no fornecedor" placeholder="opcional"
                value={l.supplierItemCode ?? ''} onChange={(e) => editar(l.chave, { supplierItemCode: e.target.value })} />
            </Campo>
            <Grade2>
              <Campo rotulo="Último preço (R$)">
                <input type="number" min={0} step="0.01" aria-label="Último preço" placeholder="opcional"
                  value={l.precoTexto} onChange={(e) => editar(l.chave, { precoTexto: e.target.value })} />
              </Campo>
              <div className="flex items-end">
                <button type="button" className="botao-perigo w-full"
                  onClick={() => aoMudar(linhas.filter((x) => x.chave !== l.chave))}>Remover</button>
              </div>
            </Grade2>
          </Grade2>

          {exigeCa && (
            <Campo rotulo="C.A. deste fornecedor" dica="(Certificado de Aprovação — cada fornecedor tem o seu)" className="mt-3">
              <input aria-label="C.A. do fornecedor" placeholder="ex.: 12345"
                value={l.caNumber ?? ''} onChange={(e) => editar(l.chave, { caNumber: e.target.value })} />
            </Campo>
          )}

          <Campo rotulo="Observação" className="mt-3">
            <input aria-label="Observação do fornecedor" placeholder="opcional — condição, prazo, embalagem…"
              value={l.notes ?? ''} onChange={(e) => editar(l.chave, { notes: e.target.value })} />
          </Campo>
        </div>
      ))}
      {!linhas.length && <p className="sub">Nenhum fornecedor vinculado a este produto ainda.</p>}
    </div>
  );
}
