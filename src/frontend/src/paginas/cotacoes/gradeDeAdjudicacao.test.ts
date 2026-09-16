import { describe, expect, it } from 'vitest';
import type { ItemDoProcesso, LoteDaFamilia } from '@/api/cotacoes';
import { lote, oferta, processo, proposta } from '@/test/cotacoes';
import {
  adjudicacoesDoFormulario, colunasDaGrade, erroDaDivisao, estaDividido,
  fornecedoresEscolhidos, itensSemVencedor, levarTudoDe, linhasDaGrade,
  melhorPrecoPorItem, quantoDivide, quantosLevaria, totaisPorColuna,
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

describe('dividir a quantidade do mesmo item', () => {
  const linhas = () => linhasDaGrade(escritorio(), null);

  it('o modo é a presença da chave, e não o que já foi digitado', () => {
    // ligar a divisão e ainda não ter digitado nada é um item SEM escolha feita —
    // fosse pelo digitado, a divisão vazia passaria por resolvida
    expect(estaDividido({}, papel.id)).toBe(false);
    expect(estaDividido({ [papel.id]: {} }, papel.id)).toBe(true);
  });

  it('só conta quantidade que é número positivo', () => {
    const d = { [papel.id]: { 'p-alfa': '7', 'p-beta': 'abc', 'p-gama': '-3', 'p-delta': '' } };
    expect(quantoDivide(d, papel.id, 'p-alfa')).toBe(7);
    expect(quantoDivide(d, papel.id, 'p-beta')).toBe(0);
    expect(quantoDivide(d, papel.id, 'p-gama')).toBe(0);
    expect(quantoDivide(d, papel.id, 'p-delta')).toBe(0);
  });

  it('vírgula funciona: o comprador digita como fala', () => {
    expect(quantoDivide({ [papel.id]: { 'p-alfa': '2,5' } }, papel.id, 'p-alfa')).toBe(2.5);
  });

  it('a soma que não fecha diz quanto falta ou quanto sobra', () => {
    const l = linhas()[0];   // papel, 10 UN
    expect(erroDaDivisao(l, { [papel.id]: { 'p-alfa': '7', 'p-beta': '3' } })).toBeNull();
    expect(erroDaDivisao(l, { [papel.id]: { 'p-alfa': '7' } })).toContain('faltam 3');
    expect(erroDaDivisao(l, { [papel.id]: { 'p-alfa': '7', 'p-beta': '5' } })).toContain('sobram 2');
    expect(erroDaDivisao(l, { [papel.id]: {} })).toContain('faltam 10');
  });

  it('divisão pela metade não é escolha feita', () => {
    const ls = linhas();
    const escolhas = { [caneta.id]: 'p-beta' };
    expect(itensSemVencedor(ls, escolhas, { [papel.id]: { 'p-alfa': '7' } })
      .map((i) => i.description)).toEqual(['Papel ofício A4']);
    expect(itensSemVencedor(ls, escolhas, { [papel.id]: { 'p-alfa': '7', 'p-beta': '3' } })).toEqual([]);
  });

  it('no rodapé cada fornecedor leva o que leva, e não o item inteiro', () => {
    // papel a 5 na Alfa e 7 na Beta; 7 com uma e 3 com a outra
    const totais = totaisPorColuna(linhas(), { [caneta.id]: 'p-beta' },
      { [papel.id]: { 'p-alfa': '7', 'p-beta': '3' } });

    expect(totais['p-alfa'].selecionado).toBe(35);        // 7 × 5
    expect(totais['p-beta'].selecionado).toBe(21 + 30);   // 3 × 7 do papel + a caneta inteira
    // somar o item inteiro dos dois anunciaria 50 + 70 = 120 só no papel
    expect(totais['p-alfa'].selecionado + totais['p-beta'].selecionado).toBe(86);
  });

  it('item dividido conta os dois como fornecedores da compra', () => {
    const fornecedores = fornecedoresEscolhidos(linhas(), { [caneta.id]: 'p-alfa' },
      { [papel.id]: { 'p-alfa': '7', 'p-beta': '3' } });
    expect(fornecedores.sort()).toEqual(['s-alfa', 's-beta']);
  });

  it('quem ficou com zero na divisão não vira fornecedor da compra', () => {
    const fornecedores = fornecedoresEscolhidos(linhas(), { [caneta.id]: 'p-alfa' },
      { [papel.id]: { 'p-alfa': '10', 'p-beta': '' } });
    expect(fornecedores).toEqual(['s-alfa']);
  });

  it('o envio manda quantidade no item dividido e nada no de vencedor único', () => {
    // nulo é como o servidor lê "a quantidade inteira" — é o que mantém idêntico
    // o caminho de sempre
    const awards = adjudicacoesDoFormulario(linhas(), { [caneta.id]: 'p-beta' },
      { [papel.id]: { 'p-alfa': '7', 'p-beta': '3' } }, 'Melhor preço no volume');

    expect(awards).toHaveLength(3);
    expect(awards.filter((a) => a.quotationItemId === papel.id).map((a) => a.quantity))
      .toEqual([7, 3]);
    expect(awards.find((a) => a.quotationItemId === caneta.id)!.quantity).toBeUndefined();
    expect(awards.every((a) => a.justification === 'Melhor preço no volume')).toBe(true);
  });

  it('sem divisão nenhuma, o envio é exatamente o de antes', () => {
    const awards = adjudicacoesDoFormulario(linhas(),
      { [papel.id]: 'p-alfa', [caneta.id]: 'p-beta' }, {}, 'Menor preço');

    expect(awards.map((a) => a.proposalId)).toEqual(['p-alfa', 'p-beta']);
    expect(awards.every((a) => a.quantity === undefined)).toBe(true);
  });
});

/**
 * O sistema indica; o comprador decide.
 *
 * Antes, o menor preço só aparecia quando se apertava "melhor preço por item" — e apertar
 * SUBSTITUÍA todas as escolhas já feitas. Ver a informação custava a decisão. A comparação
 * virou dado da célula: está sempre lá, e não mexe em escolha nenhuma.
 */
describe('a grade indica o menor preço sem decidir nada', () => {
  const linhas = () => linhasDaGrade(escritorio(), null);
  const celula = (itemId: string, proposalId: string) =>
    linhas().find((l) => l.item.id === itemId)!.celulas.find((c) => c.proposalId === proposalId)!;

  it('marca a oferta mais barata de cada item, e cada item tem a sua', () => {
    // no papel a Alfa é melhor (5 × 10 = 50 contra 70); na caneta, a Beta (30 contra 40)
    expect(celula(papel.id, 'p-alfa').menorPreco).toBe(true);
    expect(celula(papel.id, 'p-beta').menorPreco).toBe(false);
    expect(celula(caneta.id, 'p-beta').menorPreco).toBe(true);
    expect(celula(caneta.id, 'p-alfa').menorPreco).toBe(false);
  });

  it('diz quanto a outra oferta está acima, em vez de só apontar para cima', () => {
    // 70 contra 50 são 40% — número que decide se vale trocar preço por prazo
    expect(celula(papel.id, 'p-beta').acimaDoMenor).toBe(40);
    expect(celula(papel.id, 'p-alfa').acimaDoMenor).toBeNull();
    // 40 contra 30 são 33,3%
    expect(celula(caneta.id, 'p-alfa').acimaDoMenor).toBeCloseTo(33.3, 1);
  });

  it('marcar não escolhe: a grade nasce sem vencedor nenhum', () => {
    // é a regra que o cliente pediu por escrito — o sistema pode indicar, a decisão é do
    // comprador. Se a marca escolhesse, não haveria o que decidir
    expect(itensSemVencedor(linhas(), {})).toHaveLength(2);
  });

  it('quem não pode vencer fica fora da comparação', () => {
    // destacar como "menor preço" uma oferta que a adjudicação vai recusar é apontar para
    // uma porta fechada
    const lotes: LoteDaFamilia[] = [lote({
      family: 'MATERIAL DE ESCRITORIO',
      offers: [
        oferta({ proposalId: 'p-alfa', supplierId: 's-alfa', supplierName: 'Alfa',
          homologation: 'PROSPECT', canWin: false }),
        oferta({ proposalId: 'p-beta', supplierId: 's-beta', supplierName: 'Beta' }),
      ],
    })];
    const comImpedimento = linhasDaGrade(escritorio(), lotes)
      .find((l) => l.item.id === papel.id)!;

    const alfa = comImpedimento.celulas.find((c) => c.proposalId === 'p-alfa')!;
    const beta = comImpedimento.celulas.find((c) => c.proposalId === 'p-beta')!;
    expect(alfa.menorPreco).toBe(false);          // é a mais barata, mas não pode vencer
    expect(alfa.acimaDoMenor).toBeNull();
    expect(beta.menorPreco).toBe(true);           // a mais barata ENTRE AS QUE PODEM
  });
});

describe('levar tudo de um fornecedor', () => {
  const linhas = () => linhasDaGrade(escritorio(), null);

  it('concentra a compra num fornecedor, sem apagar o que já estava em outros itens', () => {
    const nova = levarTudoDe(linhas(), 'p-alfa', {});
    expect(nova).toEqual({ [papel.id]: 'p-alfa', [caneta.id]: 'p-alfa' });
  });

  it('não leva o item que o fornecedor não cotou', () => {
    // arrastar o item para uma coluna vazia daria uma escolha que a confirmação recusaria,
    // e o comprador descobriria no erro
    const q = processo({
      families: ['MATERIAL DE ESCRITORIO'],
      items: [papel, caneta],
      proposals: [
        proposta({ id: 'p-so-papel', supplierId: 's-x', supplierName: 'Só Papel', totalValue: 50,
          items: [{ quotationItemId: papel.id, unitPrice: 5, quantity: 10 }] }),
      ],
    });
    const ls = linhasDaGrade(q, null);

    expect(quantosLevaria(ls, 'p-so-papel')).toBe(1);
    expect(levarTudoDe(ls, 'p-so-papel', {})).toEqual({ [papel.id]: 'p-so-papel' });
  });

  it('a linha em divisão fica intacta: ela tem decisão própria', () => {
    // um atalho que apagasse a quantidade digitada destruiria trabalho sem pedir licença
    const divisoes = { [papel.id]: { 'p-alfa': '7', 'p-beta': '3' } };

    expect(quantosLevaria(linhas(), 'p-alfa', divisoes)).toBe(1);
    expect(levarTudoDe(linhas(), 'p-alfa', {}, divisoes)).toEqual({ [caneta.id]: 'p-alfa' });
  });
});
