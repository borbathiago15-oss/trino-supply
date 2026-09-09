import { describe, expect, it } from 'vitest';
import type { ItemDoProcesso, LoteDaFamilia } from '@/api/cotacoes';
import { lote, oferta, processo, proposta } from '@/test/cotacoes';
import {
  colunasDaGrade, fornecedoresEscolhidos, itensSemVencedor, linhasDaGrade,
  melhorPrecoPorItem, totaisPorColuna,
} from './gradeDeAdjudicacao';

/**
 * O caso do cliente, com os números dele: material de escritório, papel e caneta na
 * MESMA família, e os preços cruzados — a Alfa é melhor no papel, a Beta na caneta.
 * Antes da grade esta compra tinha de ir inteira para um dos dois.
 */
const item = (id: string, description: string, quantity: number, sequence: number): ItemDoProcesso =>
  ({ id, sequence, catalogItemId: null, catalogCode: null, description, quantity, unitOfMeasure: 'UN',
     sourcePrNumber: null, family: 'MATERIAL DE ESCRITORIO' });

const papel = item('i-papel', 'Papel ofício A4', 10, 1);
const caneta = item('i-caneta', 'Caixa de caneta', 10, 2);

const escritorio = () => processo({
  families: ['MATERIAL DE ESCRITORIO'],
  items: [caneta, papel],   // fora de ordem de propósito: a grade ordena por sequence
  proposals: [
    proposta({ id: 'p-alfa', supplierId: 's-alfa', supplierName: 'Alfa', totalValue: 90,
      items: [{ quotationItemId: papel.id, unitPrice: 5, quantity: 10 },
              { quotationItemId: caneta.id, unitPrice: 4, quantity: 10 }] }),
    proposta({ id: 'p-beta', supplierId: 's-beta', supplierName: 'Beta', totalValue: 100,
      items: [{ quotationItemId: papel.id, unitPrice: 7, quantity: 10 },
              { quotationItemId: caneta.id, unitPrice: 3, quantity: 10 }] }),
  ],
});

describe('grade de adjudicação', () => {
  it('uma coluna por proposta vigente e uma linha por item, na ordem do processo', () => {
    const q = escritorio();
    expect(colunasDaGrade(q).map((c) => c.supplierName)).toEqual(['Alfa', 'Beta']);
    expect(linhasDaGrade(q, null).map((l) => l.item.description))
      .toEqual(['Papel ofício A4', 'Caixa de caneta']);
  });

  it('a célula traz o total do item naquele fornecedor', () => {
    const [linhaPapel, linhaCaneta] = linhasDaGrade(escritorio(), null);
    expect(linhaPapel.celulas.map((c) => c.total)).toEqual([50, 70]);   // 10 × 5 e 10 × 7
    expect(linhaCaneta.celulas.map((c) => c.total)).toEqual([40, 30]);  // 10 × 4 e 10 × 3
  });

  it('item que o fornecedor não cotou fica vazio, não zerado', () => {
    // zero é um preço; "não cotou" é ausência de preço, e a tela precisa dizer a diferença
    const q = processo({
      items: [papel, caneta],
      proposals: [proposta({ id: 'p-alfa', supplierId: 's-alfa', supplierName: 'Alfa',
        items: [{ quotationItemId: papel.id, unitPrice: 5, quantity: 10 }] })],
    });
    const [linhaPapel, linhaCaneta] = linhasDaGrade(q, null);
    expect(linhaPapel.celulas[0].total).toBe(50);
    expect(linhaCaneta.celulas[0].total).toBeNull();
    expect(linhaCaneta.celulas[0].unitPrice).toBeNull();
  });

  it('melhor preço escolhe o mais barato de cada item — e é a divisão', () => {
    const linhas = linhasDaGrade(escritorio(), null);
    expect(melhorPrecoPorItem(linhas)).toEqual({ [papel.id]: 'p-alfa', [caneta.id]: 'p-beta' });
    // dois fornecedores: é exatamente o que a adjudicação por família não conseguia fazer
    expect(fornecedoresEscolhidos(linhas, melhorPrecoPorItem(linhas))).toHaveLength(2);
  });

  it('melhor preço não inventa vencedor para item que ninguém cotou', () => {
    const q = processo({
      items: [papel, caneta],
      proposals: [proposta({ id: 'p-alfa', supplierId: 's-alfa', supplierName: 'Alfa',
        items: [{ quotationItemId: papel.id, unitPrice: 5, quantity: 10 }] })],
    });
    expect(melhorPrecoPorItem(linhasDaGrade(q, null))).toEqual({ [papel.id]: 'p-alfa' });
  });

  it('melhor preço pula quem não pode vencer, mesmo sendo o mais barato', () => {
    // a Beta é mais barata na caneta, mas não está homologada (SUP-ERR-030):
    // sugeri-la seria propor a escolha que o servidor vai recusar
    const lotes: LoteDaFamilia[] = [lote({
      family: 'MATERIAL DE ESCRITORIO',
      offers: [
        oferta({ supplierId: 's-alfa', proposalId: 'p-alfa' }),
        oferta({ supplierId: 's-beta', proposalId: 'p-beta', homologation: 'PROSPECT', canWin: false }),
      ],
    })];
    const linhas = linhasDaGrade(escritorio(), lotes);
    expect(melhorPrecoPorItem(linhas)).toEqual({ [papel.id]: 'p-alfa', [caneta.id]: 'p-alfa' });
    expect(linhas[1].celulas[1].impedimento).toContain('SUP-ERR-030');
  });

  it('"não cotou a família inteira" não vira impedimento de coluna', () => {
    // na grade isso já aparece como célula vazia, item a item; repetir como impedimento
    // barraria o fornecedor nos itens que ele cotou — que é o que a grade veio destravar
    const lotes: LoteDaFamilia[] = [lote({
      family: 'MATERIAL DE ESCRITORIO',
      offers: [oferta({ supplierId: 's-alfa', proposalId: 'p-alfa', complete: false, canWin: false })],
    })];
    expect(linhasDaGrade(escritorio(), lotes)[0].celulas[0].impedimento).toBeNull();
  });

  it('os totais por coluna separam o que foi cotado do que foi selecionado', () => {
    const linhas = linhasDaGrade(escritorio(), null);
    const totais = totaisPorColuna(linhas, { [papel.id]: 'p-alfa', [caneta.id]: 'p-beta' });
    expect(totais['p-alfa']).toEqual({ cotado: 90, selecionado: 50 });
    expect(totais['p-beta']).toEqual({ cotado: 100, selecionado: 30 });
    // 80 é menos do que qualquer fornecedor sozinho (90 e 100) — o ganho da divisão
    expect(totais['p-alfa'].selecionado + totais['p-beta'].selecionado).toBe(80);
  });

  it('itens sem vencedor são os que faltam para confirmar', () => {
    const linhas = linhasDaGrade(escritorio(), null);
    expect(itensSemVencedor(linhas, {}).map((i) => i.description))
      .toEqual(['Papel ofício A4', 'Caixa de caneta']);
    expect(itensSemVencedor(linhas, { [papel.id]: 'p-alfa' }).map((i) => i.description))
      .toEqual(['Caixa de caneta']);
    expect(itensSemVencedor(linhas, { [papel.id]: 'p-alfa', [caneta.id]: 'p-beta' })).toEqual([]);
  });

  it('escolher tudo do mesmo fornecedor não é compra dividida', () => {
    const linhas = linhasDaGrade(escritorio(), null);
    expect(fornecedoresEscolhidos(linhas, { [papel.id]: 'p-alfa', [caneta.id]: 'p-alfa' }))
      .toEqual(['s-alfa']);
  });
});
