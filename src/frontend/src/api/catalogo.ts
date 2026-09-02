import { api, enviarArquivo } from './cliente';

/** Fornecedor de um produto. O C.A. é do par produto+fornecedor. */
export interface FornecedorDoProduto {
  id?: string;
  supplierId: string | null;
  supplierName: string;
  taxId: string | null;
  contact: string | null;
  supplierItemCode: string | null;
  lastPrice: number | null;
  caNumber: string | null;
  notes: string | null;
}

export interface Produto {
  id: string;
  code: string;
  description: string;
  family: string;
  unitOfMeasure: string;
  referencePrice: number | null;
  active: boolean;
  stockControlled: boolean;
  purchasable: boolean;
  minimumQty: number | null;
  productType: string | null;
  productTypeLabel: string | null;
  baseCode: string | null;
  size: string | null;
  imageDocumentId: string | null;
  imageFileName: string | null;
  /** EPI/EPC sem C.A. em nenhum fornecedor: pendência de conformidade. */
  compliancePending: boolean;
  suppliers: FornecedorDoProduto[];
}

export interface TipoDeProduto { key: string; label: string; requiresCa: boolean }

export interface ResumoCatalogo {
  total: number;
  active: number;
  inactive: number;
  compliancePending: number;
  families: { family: string; count: number }[];
}

const base = '/api/v1/items';

const normalizar = (p: Produto): Produto => ({ ...p, suppliers: p.suppliers ?? [] });

export const resumoCatalogo = (signal?: AbortSignal) => api<ResumoCatalogo>(`${base}/summary`, { signal });

export const tiposDeProduto = async (signal?: AbortSignal) =>
  (await api<{ items: TipoDeProduto[] }>('/api/v1/product-types', { signal })).items;

/**
 * A tela nunca lista o acervo inteiro (são milhares de itens): busca por código,
 * trecho da descrição ou família. `incluirInativos` só vale para quem mantém.
 */
export async function buscarProdutos(
  { q, familia, incluirInativos }: { q?: string; familia?: string; incluirInativos?: boolean },
  signal?: AbortSignal,
) {
  const params = new URLSearchParams();
  if (incluirInativos) params.set('all', 'true');
  if (q) params.set('q', q);
  if (familia) params.set('family', familia);
  const { items } = await api<{ items: Produto[] }>(`${base}/?${params}`, { signal });
  return items.map(normalizar);
}

/** Produtos ativos para escolher em outras telas (contrato de parceria, por exemplo). */
export const listarProdutos = async (signal?: AbortSignal) =>
  (await api<{ items: Produto[] }>(`${base}/`, { signal })).items.map(normalizar);

/** Quantos produtos existem por família — usado no cadastro de famílias. */
export async function contarProdutosPorFamilia(signal?: AbortSignal): Promise<Record<string, number>> {
  const { families } = await resumoCatalogo(signal);
  return Object.fromEntries(families.map((f) => [f.family, f.count]));
}

export interface DadosProduto {
  code?: string | null;
  description: string;
  family: string;
  unitOfMeasure: string | null;
  referencePrice: number | null;
  productType: string | null;
  suppliers: Omit<FornecedorDoProduto, 'id'>[];
  /** Saíram da tela: todo produto vale para almoxarifado e para compra. */
  stockControlled: true;
  purchasable: true;
}

export const criarProduto = (dados: DadosProduto) => api<Produto>(`${base}/`, { method: 'POST', body: dados });

export const atualizarProduto = (id: string, dados: Partial<DadosProduto> & { active?: boolean }) =>
  api<Produto>(`${base}/${id}`, { method: 'PATCH', body: dados });

export const enviarFoto = (id: string, arquivo: File) => enviarArquivo(`${base}/${id}/image`, arquivo);

// ---- importação por planilha ----
export type SituacaoLinha = 'NOVO' | 'DUPLICADO' | 'ERRO';

export interface LinhaImportacao {
  line: number;
  code: string;
  description: string;
  size: string | null;
  status: SituacaoLinha;
  message: string | null;
}

export interface ResultadoImportacao {
  fileName: string;
  totalLines: number;
  toCreate: number;
  duplicates: number;
  errors: number;
  committed: boolean;
  warnings: string[];
  rowsTruncated: boolean;
  rows: LinhaImportacao[];
}

export interface OpcoesImportacao {
  arquivo: File;
  family: string;
  productType: string | null;
  unit: string | null;
  sizes: string | null;
  /** false pré-visualiza; true grava. */
  commit: boolean;
}

export async function importarPlanilha(o: OpcoesImportacao): Promise<ResultadoImportacao> {
  const extras: Record<string, string> = {
    family: o.family,
    stockControlled: 'true',
    purchasable: 'true',
    commit: o.commit ? 'true' : 'false',
  };
  if (o.productType) extras.productType = o.productType;
  if (o.unit) extras.unit = o.unit;
  if (o.sizes) extras.sizes = o.sizes;
  return enviarArquivo<ResultadoImportacao>(`${base}/import`, o.arquivo, extras);
}

/** Grades prontas para a importação desdobrar um produto em vários tamanhos. */
export const GRADES_DE_TAMANHO = {
  letras: 'PP, P, M, G, GG, XG, XXG',
  numeros: '34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46',
  limpar: '',
};
