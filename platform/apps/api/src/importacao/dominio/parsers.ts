/**
 * Leitura e validação de arquivos de importação — DOMÍNIO PURO (sem Prisma,
 * sem Nest, sem I/O). Recebe o conteúdo já lido e devolve linhas ou erros.
 */

import { createHash } from 'node:crypto';

export type TipoLote = 'CATALOGO_SKU' | 'PARAMETRO_ESTOQUE' | 'FORNECEDOR' | 'ORCAMENTO_CC';

export interface LinhaBruta {
  numeroLinha: number;
  conteudo: Record<string, string>;
}

/** SHA-256 do conteúdo — a chave de idempotência do arquivo. */
export function checksumArquivo(conteudo: Buffer | string): string {
  return createHash('sha256').update(conteudo).digest('hex');
}

/**
 * CSV enxuto: primeira linha é cabeçalho, separador `;` ou `,` detectado pela
 * frequência no cabeçalho, aspas duplas escapadas por duplicação.
 * Escolha consciente de não trazer dependência: o formato de entrada é um CSV
 * de planilha, não um dialeto arbitrário.
 */
export function lerCsv(texto: string): { cabecalho: string[]; linhas: LinhaBruta[] } {
  const semBom = texto.replace(/^﻿/, '');
  const linhasTexto = semBom.split(/\r?\n/).filter((l) => l.trim().length > 0);
  if (linhasTexto.length === 0) return { cabecalho: [], linhas: [] };

  const separador = (linhasTexto[0].match(/;/g)?.length ?? 0) >= (linhasTexto[0].match(/,/g)?.length ?? 0) ? ';' : ',';
  const cabecalho = dividir(linhasTexto[0], separador).map((c) => c.trim().toLowerCase());

  const linhas: LinhaBruta[] = linhasTexto.slice(1).map((linha, indice) => {
    const valores = dividir(linha, separador);
    const conteudo: Record<string, string> = {};
    cabecalho.forEach((coluna, i) => {
      conteudo[coluna] = (valores[i] ?? '').trim();
    });
    // Numeração de linha do ARQUIVO (1 = cabeçalho), que é o que o usuário vê
    // ao abrir a planilha para corrigir.
    return { numeroLinha: indice + 2, conteudo };
  });

  return { cabecalho, linhas };
}

function dividir(linha: string, separador: string): string[] {
  const saida: string[] = [];
  let atual = '';
  let dentroDeAspas = false;
  for (let i = 0; i < linha.length; i += 1) {
    const c = linha[i];
    if (c === '"') {
      if (dentroDeAspas && linha[i + 1] === '"') { atual += '"'; i += 1; }
      else dentroDeAspas = !dentroDeAspas;
    } else if (c === separador && !dentroDeAspas) {
      saida.push(atual); atual = '';
    } else {
      atual += c;
    }
  }
  saida.push(atual);
  return saida;
}

export interface ErroValidacao {
  campo: string;
  mensagem: string;
}

export interface ResultadoValidacao<T> {
  valido: boolean;
  dados?: T;
  erros: ErroValidacao[];
}

const texto = (v: unknown) => String(v ?? '').trim();
const numero = (v: unknown) => {
  // Aceita "1.234,56" (planilha BR) e "1234.56".
  const bruto = texto(v).replace(/\s/g, '');
  if (bruto === '') return NaN;
  const normalizado = bruto.includes(',') ? bruto.replace(/\./g, '').replace(',', '.') : bruto;
  return Number(normalizado);
};
const booleano = (v: unknown) => ['1', 'true', 'sim', 's', 'x'].includes(texto(v).toLowerCase());

/**
 * Validadores por tipo de lote. Cada um devolve TODOS os erros da linha, não
 * só o primeiro: quem corrige a planilha quer resolver de uma vez.
 */
export const VALIDADORES: Record<TipoLote, (linha: Record<string, string>) => ResultadoValidacao<any>> = {
  CATALOGO_SKU: (linha) => {
    const erros: ErroValidacao[] = [];
    const codigoTipo = texto(linha.codigo_tipo ?? linha.tipo_produto);
    const codigo = texto(linha.codigo);
    const descricao = texto(linha.descricao);
    const unidade = texto(linha.unidade_medida ?? linha.unidade).toUpperCase();
    const unidadesValidas = ['UN', 'CX', 'KG', 'L', 'M', 'M2', 'M3', 'PAR', 'PC', 'RL'];

    if (!codigoTipo) erros.push({ campo: 'codigo_tipo', mensagem: 'Informe o código do tipo de produto.' });
    if (!codigo) erros.push({ campo: 'codigo', mensagem: 'Informe o código do SKU.' });
    if (codigo.length > 40) erros.push({ campo: 'codigo', mensagem: 'Código do SKU passa de 40 caracteres.' });
    if (!descricao) erros.push({ campo: 'descricao', mensagem: 'Informe a descrição.' });
    if (!unidadesValidas.includes(unidade)) {
      erros.push({ campo: 'unidade_medida', mensagem: `Unidade inválida (use ${unidadesValidas.join(', ')}).` });
    }
    return erros.length > 0
      ? { valido: false, erros }
      : {
          valido: true,
          erros: [],
          dados: {
            codigoTipo, codigo, descricao, unidadeMedida: unidade,
            exigeCa: booleano(linha.exige_ca),
            varianteCodigo: texto(linha.variante_codigo) || codigo,
            tamanho: texto(linha.tamanho) || null,
            cor: texto(linha.cor) || null,
          },
        };
  },

  PARAMETRO_ESTOQUE: (linha) => {
    const erros: ErroValidacao[] = [];
    const varianteCodigo = texto(linha.variante_codigo ?? linha.codigo);
    const centroCusto = texto(linha.centro_custo ?? linha.codigo_centro_custo);
    const rop = numero(linha.ponto_pedido_rop ?? linha.rop);
    const seguranca = numero(linha.estoque_seguranca);
    const leadTime = numero(linha.lead_time_dias);

    if (!varianteCodigo) erros.push({ campo: 'variante_codigo', mensagem: 'Informe o código da variante.' });
    if (!centroCusto) erros.push({ campo: 'centro_custo', mensagem: 'Informe o centro de custo.' });
    if (!Number.isFinite(rop) || rop < 0) erros.push({ campo: 'ponto_pedido_rop', mensagem: 'ROP precisa ser número >= 0.' });
    if (!Number.isFinite(seguranca) || seguranca < 0) {
      erros.push({ campo: 'estoque_seguranca', mensagem: 'Estoque de segurança precisa ser número >= 0.' });
    }
    if (!Number.isFinite(leadTime) || leadTime < 0 || !Number.isInteger(leadTime)) {
      erros.push({ campo: 'lead_time_dias', mensagem: 'Lead time precisa ser inteiro >= 0.' });
    }
    return erros.length > 0
      ? { valido: false, erros }
      : {
          valido: true, erros: [],
          dados: {
            varianteCodigo, centroCusto, pontoPedidoRop: rop, estoqueSeguranca: seguranca,
            leadTimeDias: leadTime, gerarScAutomatica: booleano(linha.gerar_sc_automatica),
          },
        };
  },

  FORNECEDOR: (linha) => {
    const erros: ErroValidacao[] = [];
    const cnpj = texto(linha.cnpj).replace(/\D/g, '');
    const razaoSocial = texto(linha.razao_social);
    const email = texto(linha.email_contato ?? linha.email);

    if (!/^[0-9]{14}$/.test(cnpj)) erros.push({ campo: 'cnpj', mensagem: 'CNPJ precisa ter 14 dígitos.' });
    if (!razaoSocial) erros.push({ campo: 'razao_social', mensagem: 'Informe a razão social.' });
    if (email && !/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email)) {
      erros.push({ campo: 'email_contato', mensagem: 'E-mail inválido.' });
    }
    return erros.length > 0
      ? { valido: false, erros }
      : {
          valido: true, erros: [],
          dados: {
            cnpj, razaoSocial, nomeFantasia: texto(linha.nome_fantasia) || null,
            emailContato: email || null, telefone: texto(linha.telefone) || null,
          },
        };
  },

  ORCAMENTO_CC: (linha) => {
    const erros: ErroValidacao[] = [];
    const centroCusto = texto(linha.centro_custo ?? linha.codigo_centro_custo);
    const exercicio = numero(linha.exercicio);
    const valorOrcado = numero(linha.valor_orcado);

    if (!centroCusto) erros.push({ campo: 'centro_custo', mensagem: 'Informe o centro de custo.' });
    if (!Number.isInteger(exercicio) || exercicio < 2000 || exercicio > 2100) {
      erros.push({ campo: 'exercicio', mensagem: 'Exercício precisa ser um ano entre 2000 e 2100.' });
    }
    if (!Number.isFinite(valorOrcado) || valorOrcado < 0) {
      erros.push({ campo: 'valor_orcado', mensagem: 'Valor orçado precisa ser número >= 0.' });
    }
    return erros.length > 0
      ? { valido: false, erros }
      : { valido: true, erros: [], dados: { centroCusto, exercicio, valorOrcado } };
  },
};

/** Mensagem única e legível para gravar em staging_linha_importacao. */
export function mensagemDeErros(erros: ErroValidacao[]): string {
  return erros.map((e) => `${e.campo}: ${e.mensagem}`).join(' | ');
}
