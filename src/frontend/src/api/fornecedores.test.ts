import { beforeEach, describe, expect, it, vi } from 'vitest';
import { acharFornecedor, chaveDoNome, type Fornecedor } from './fornecedores';
import { api } from './cliente';

vi.mock('./cliente', async (importar) => ({
  ...(await importar<typeof import('./cliente')>()),
  api: vi.fn(),
}));

const fornecedor = (f: Partial<Fornecedor>): Fornecedor => ({
  id: 'f1', legalName: 'Pontes Tour', tradeName: null, taxId: null, email: null,
  phone: '81999990000', active: true, homologationStatus: 'PROSPECT',
  effectiveHomologation: 'PROSPECT', documents: [],
  contract: {
    number: null, validFrom: null, validUntil: null, notes: null,
    valueLimit: null, consumed: null, balance: null, current: false, items: [],
  },
  ...f,
});

/** Cada chamada da busca devolve uma página; a ordem é a das buscas feitas. */
const respondeCom = (...paginas: Fornecedor[][]) => {
  let i = 0;
  vi.mocked(api).mockImplementation(async () => {
    const itens = paginas[i++] ?? [];
    return { items: itens, total: itens.length } as never;
  });
};

describe('chave da razão social', () => {
  it('a mesma empresa digitada de outro jeito dá a mesma chave', () => {
    // é a regra que o servidor usa em SupplierService.ChaveDoNome; divergir aqui faria a
    // tela reaproveitar um fornecedor e o servidor recusar outro
    expect(chaveDoNome('  Pontes  Tóur. ')).toBe('PONTESTOUR');
    expect(chaveDoNome('pontes-tour')).toBe('PONTESTOUR');
    expect(chaveDoNome('Aços do Brasil 2')).toBe('ACOSDOBRASIL2');
  });

  it('empresas diferentes continuam diferentes', () => {
    // o sufixo societário não é normalizado de propósito: recusar "Alfa Ltda" porque
    // existe "Alfa ME" barraria cadastro legítimo
    expect(chaveDoNome('Alfa Ltda')).not.toBe(chaveDoNome('Alfa ME'));
  });
});

describe('achar o fornecedor que já existe', () => {
  beforeEach(() => vi.resetAllMocks());

  it('acha pela razão social mesmo escrita de outro jeito', async () => {
    respondeCom([fornecedor({ id: 'f9', legalName: 'PONTES TOUR' })]);
    expect((await acharFornecedor('Pontes Tour'))!.id).toBe('f9');
  });

  it('nome parecido não é o mesmo nome', async () => {
    // a busca do servidor é por trecho: "Pontes Tour" traz "Pontes Tour Logística",
    // que é outra empresa
    respondeCom([fornecedor({ id: 'f9', legalName: 'Pontes Tour Logística' })]);
    expect(await acharFornecedor('Pontes Tour')).toBeNull();
  });

  it('sem achar pelo nome, o CNPJ é o segundo caminho', async () => {
    // o mesmo fornecedor cadastrado com a razão social escrita por extenso ainda é
    // encontrável pelo documento, que é identidade
    respondeCom([], [fornecedor({ id: 'f7', legalName: 'Pontes Turismo SA', taxId: '11222333000181' })]);
    expect((await acharFornecedor('Pontes Tour', '11.222.333/0001-81'))!.id).toBe('f7');
  });

  it('sem CNPJ informado, não sai procurando por documento', async () => {
    respondeCom([], [fornecedor({ id: 'f7' })]);
    expect(await acharFornecedor('Empresa Nova')).toBeNull();
    expect(api).toHaveBeenCalledTimes(1);
  });
});
