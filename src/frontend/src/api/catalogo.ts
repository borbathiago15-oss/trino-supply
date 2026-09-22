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

/** Um tamanho do produto: item de catálogo com código, preço e C.A. próprios. */
export interface TamanhoDoProduto {
  id: string;
  code: string;
  /** Nulo no produto que não tem grade. */
  size: string | null;
  referencePrice: number | null;
  compliancePending: boolean;
  imageDocumentId: string | null;
}

/**
 * O catálogo como quem pede enxerga: um produto por linha, com a grade de tamanhos junto.
 * A bota do 38 ao 44 é um produto com sete tamanhos, e não sete produtos parecidos.
 */
export interface ProdutoParaEscolha {
  key: string;
  baseCode: string | null;
  description: string;
  family: string;
  unitOfMeasure: string;
  productType: string | null;
  productTypeLabel: string | null;
  hasGrade: boolean;
  /** Nenhum tamanho pode ser pedido: EPI/EPC sem C.A. em fornecedor nenhum (IC-ERR-023). */
  compliancePending: boolean;
  sizes: TamanhoDoProduto[];
}

export async function produtosParaEscolha(
  { familia, q }: { familia?: string; q?: string }, signal?: AbortSignal,
): Promise<ProdutoParaEscolha[]> {
  const params = new URLSearchParams();
  if (familia) params.set('family', familia);
  if (q?.trim()) params.set('q', q.trim());
  const { items } = await api<{ items: ProdutoParaEscolha[] }>(`${base}/picker?${params}`, { signal });
  return (items ?? []).map((p) => ({ ...p, sizes: p.sizes ?? [] }));
}

/** Cadastro da grade: um produto por tamanho, todos com o mesmo código-base. */
export interface DadosDaGrade {
  baseCode: string | null;
  description: string;
  family: string;
  unitOfMeasure: string | null;
  referencePrice: number | null;
  /** Os tamanhos da grade, como o cadastro digitou ("P, M, G" ou "38, 39, 40"). */
  sizes: string[];
  productType: string | null;
  suppliers: Omit<FornecedorDoProduto, 'id'>[];
}

export const criarGradeDeTamanhos = async (dados: DadosDaGrade) =>
  (await api<{ items: Produto[] }>(`${base}/grade`, { method: 'POST', body: dados })).items.map(normalizar);

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

/** Só os nomes das famílias que existem no catálogo. */
export const familiasDoCatalogo = async (signal?: AbortSignal) =>
  (await api<{ families: string[] }>(`${base}/families`, { signal })).families;

/** Linha da grade da Solicitação em Lote: saldo, entrada prevista e cobertura. */
export interface LinhaDeLote {
  id: string;
  code: string;
  description: string;
  family: string;
  unitOfMeasure: string;
  referencePrice: number | null;
  stockAvailable: number;
  inboundQty: number;
  avgMonthlyConsumption: number;
  coverageDays: number | null;
}

export async function gradeDeLote(
  { familia, q }: { familia?: string; q?: string }, signal?: AbortSignal,
): Promise<LinhaDeLote[]> {
  const params = new URLSearchParams();
  if (familia) params.set('family', familia);
  if (q) params.set('q', q);
  const { items } = await api<{ items: LinhaDeLote[] }>(`${base}/batch-view?${params}`, { signal });
  return items;
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
