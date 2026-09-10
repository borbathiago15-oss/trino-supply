import { api, enviarArquivo } from './cliente';

/** Situação de homologação. Só HOMOLOGADO pode vencer uma cotação (SUP-ERR-030). */
export type SituacaoHomologacao = 'PROSPECT' | 'EM_HOMOLOGACAO' | 'HOMOLOGADO' | 'RESTRITO' | 'BLOQUEADO';

export const ROTULO_HOMOLOGACAO: Record<SituacaoHomologacao, { rotulo: string; classe: string }> = {
  PROSPECT: { rotulo: 'Prospect', classe: 'bg-slate-100 text-slate-600' },
  EM_HOMOLOGACAO: { rotulo: 'Em homologação', classe: 'bg-teal-50 text-teal-800' },
  HOMOLOGADO: { rotulo: 'Homologado', classe: 'bg-ok-fundo text-ok' },
  RESTRITO: { rotulo: 'Restrito', classe: 'bg-aviso-fundo text-aviso' },
  BLOQUEADO: { rotulo: 'Bloqueado', classe: 'bg-perigo-fundo text-perigo' },
};

export type TipoDocumento = 'CND_FEDERAL' | 'FGTS' | 'CNDT' | 'CONTRATO_SOCIAL' | 'OUTRO';

export const ROTULO_DOCUMENTO: Record<TipoDocumento, string> = {
  CND_FEDERAL: 'CND Federal',
  FGTS: 'Certificado de regularidade do FGTS',
  CNDT: 'CNDT (débitos trabalhistas)',
  CONTRATO_SOCIAL: 'Contrato social',
  OUTRO: 'Outro documento',
};

export interface DocumentoFornecedor {
  id: string;
  type: TipoDocumento;
  label: string | null;
  documentId: string;
  fileName: string;
  validUntil: string | null;
  expired: boolean;
  expiringDays: number | null;
  uploadedByLabel: string | null;
}

export interface ItemContrato {
  id?: string;
  catalogItemId: string | null;
  catalogCode: string | null;
  description: string | null;
  unitOfMeasure: string | null;
  unitPrice: number;
  paymentTerms: string | null;
  paymentDays: number | null;
  deliveryDays: number | null;
  notes: string | null;
}

export interface ContratoFornecedor {
  number: string | null;
  validFrom: string | null;
  validUntil: string | null;
  notes: string | null;
  valueLimit: number | null;
  consumed: number | null;
  balance: number | null;
  current: boolean;
  items: ItemContrato[];
}

export interface Fornecedor {
  id: string;
  legalName: string;
  tradeName: string | null;
  /** Nulo no pré-cadastro: cotar exige só nome e telefone; o CNPJ vem antes de homologar. */
  taxId: string | null;
  email: string | null;
  phone: string | null;
  active: boolean;
  homologationStatus: SituacaoHomologacao;
  effectiveHomologation: SituacaoHomologacao;
  documents: DocumentoFornecedor[];
  contract: ContratoFornecedor;
}

/**
 * Situação que vale hoje: a efetiva já considera certidão vencida. Quando ela
 * restringe um fornecedor que o gestor marcou como homologado, a tela avisa
 * que a restrição é automática.
 */
export function situacaoEfetiva(f: Pick<Fornecedor, 'homologationStatus' | 'effectiveHomologation'>) {
  const efetiva = f.effectiveHomologation ?? f.homologationStatus ?? 'HOMOLOGADO';
  return { efetiva, restritoPorCertidao: efetiva === 'RESTRITO' && f.homologationStatus === 'HOMOLOGADO' };
}

const base = '/api/v1/suppliers';

/** Lista inteira, para os seletores de fornecedor de outras telas. */
export const listarFornecedores = async (incluirInativos = false, signal?: AbortSignal) =>
  (await api<{ items: Fornecedor[] }>(`${base}/${incluirInativos ? '?all=true' : ''}`, { signal })).items;

export interface PaginaDeFornecedores { itens: Fornecedor[]; total: number }

/** Busca no servidor, para a tela de cadastro — que antes filtrava no navegador. */
export async function buscarFornecedores(
  { busca, incluirInativos, tamanho }: { busca?: string; incluirInativos?: boolean; tamanho?: number } = {},
  signal?: AbortSignal,
): Promise<PaginaDeFornecedores> {
  const params = new URLSearchParams();
  if (incluirInativos) params.set('all', 'true');
  if (busca?.trim()) params.set('q', busca.trim());
  if (tamanho) params.set('tamanho', String(tamanho));
  const consulta = params.toString();
  const r = await api<{ items: Fornecedor[]; total: number }>(
    `${base}/${consulta ? `?${consulta}` : ''}`, { signal });
  return { itens: r.items ?? [], total: r.total ?? 0 };
}

/**
 * Chave de comparação da razão social — o mesmo `SupplierService.ChaveDoNome` do servidor:
 * maiúsculas, sem acento, só letras e dígitos. "Pontes Tour", "PONTES  TOUR." e
 * "Pontes Tóur" são a mesma empresa digitada por pessoas diferentes.
 */
export const chaveDoNome = (nome: string) =>
  nome.normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/[^0-9a-zA-Z]/g, '').toUpperCase();

/**
 * O fornecedor que já ocupa esta razão social ou este CPF/CNPJ, ou nulo.
 *
 * Existe para o pré-cadastro da cotação **reaproveitar** o cadastro em vez de criar um
 * segundo — era assim que a cotação ficava presa a um PROSPECT sem CNPJ enquanto o
 * homologado era outro registro com o mesmo nome. O guarda de verdade é o `SUP-ERR-015`
 * do servidor; isto aqui é a conveniência que evita o erro. A busca é a do servidor, então
 * uma diferença de espaçamento pode escapar daqui — e aí é o servidor que recusa e explica.
 */
export async function acharFornecedor(razaoSocial: string, documento?: string | null) {
  const chave = chaveDoNome(razaoSocial);
  const { itens } = await buscarFornecedores({ busca: razaoSocial, incluirInativos: true });
  const porNome = itens.find((f) => chaveDoNome(f.legalName) === chave);
  if (porNome) return porNome;

  const digitos = (documento ?? '').replace(/\D/g, '');
  if (!digitos) return null;
  const { itens: porDocumento } = await buscarFornecedores({ busca: digitos, incluirInativos: true });
  return porDocumento.find((f) => f.taxId === digitos) ?? null;
}

export interface DadosFornecedor {
  legalName: string;
  tradeName: string | null;
  /** Opcional (§7): o pré-cadastro entra sem CNPJ e o completa quando ganha o BID. */
  taxId: string | null;
  email: string | null;
  phone: string | null;
}

export const criarFornecedor = (dados: DadosFornecedor) => api<Fornecedor>(`${base}/`, { method: 'POST', body: dados });

/**
 * A razão social não muda depois do cadastro. O CNPJ muda **uma vez**: o pré-cadastro
 * nasce sem ele e a edição completa o registro; gravado, ele vira identidade e a API
 * ignora nova tentativa de troca.
 */
export const atualizarFornecedor = (id: string, dados:
  { tradeName?: string; email?: string; phone?: string; active?: boolean; taxId?: string }) =>
  api<Fornecedor>(`${base}/${id}`, { method: 'PATCH', body: dados });

export const salvarHomologacao = (id: string, status: SituacaoHomologacao) =>
  api<Fornecedor>(`${base}/${id}/homologation`, { method: 'PATCH', body: { status } });

export async function anexarDocumento(id: string, arquivo: File, tipo: TipoDocumento, validUntil: string, rotulo: string) {
  const extras: Record<string, string> = { type: tipo };
  if (validUntil) extras.validUntil = validUntil;
  if (rotulo) extras.label = rotulo;
  await enviarArquivo(`${base}/${id}/documents`, arquivo, extras);
}

export const removerDocumento = (id: string, docId: string) =>
  api<unknown>(`${base}/${id}/documents/${docId}`, { method: 'DELETE' });

export const gerarChavePortal = (id: string) =>
  api<{ accessKey: string }>(`${base}/${id}/portal-key`, { method: 'POST', body: {} });

export interface DadosContrato {
  number: string | null;
  valueLimit: number | null;
  validFrom: string | null;
  validUntil: string | null;
  notes: string | null;
  items: ItemContrato[];
}

export const salvarContrato = (id: string, dados: DadosContrato) =>
  api<Fornecedor>(`${base}/${id}/contract`, { method: 'PUT', body: dados });
