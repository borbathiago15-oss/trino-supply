import { expect, test } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { abrirAutenticado, ARQUIVO_SESSAO } from './sessao';

/**
 * Torre de Controle. O que só o E2E prova é que a paginação é de verdade —
 * que o servidor corta a página e devolve o total da base, em vez de a tela
 * fatiar uma lista que já veio inteira.
 */
test.describe('Torre de Controle (React)', () => {
  test('abre com os KPIs, a lista por item e o recorte na chamada', async ({ page }) => {
    const consulta = page.waitForResponse((r) => r.url().includes('/api/v1/control-tower'));
    await abrirAutenticado(page, '/torre');
    // voltou a ser uma tela só: a triagem de compra mora aqui dentro, e a de material
    // saiu para o grupo Material — duas telas listando a mesma SC era o conflito
    await expect(page.locator('#titulo-pagina')).toHaveText('Torre de Controle');
    expect((await consulta).status()).toBe(200);

    // os KPIs também são filtros: cada um é um botão
    await expect(page.getByRole('button', { name: /Em cotação/ })).toBeVisible();
    await expect(page.getByRole('button', { name: /Atrasados/ })).toBeVisible();

    // com ou sem dado, a tela se explica
    await expect(page.getByTestId('tabela-torre')
      .or(page.getByText('Nenhum item de compra neste recorte.'))).toBeVisible();
  });

  test('o KPI vira filtro e o pedido sai com a etapa', async ({ page }) => {
    await abrirAutenticado(page, '/torre');
    await expect(page.getByRole('button', { name: /Em cotação/ })).toBeVisible();

    const comEtapa = page.waitForResponse((r) =>
      r.url().includes('/api/v1/control-tower') && r.url().includes('stage=COTACAO'));
    await page.getByRole('button', { name: /Em cotação/ }).click();
    expect((await comEtapa).status()).toBe(200);
  });

  /** §5: os dois KPIs que faltavam existem e o de exceções filtra. */
  test('em faturamento e exceções aparecem, e exceções vira filtro', async ({ page }) => {
    await abrirAutenticado(page, '/torre');
    await expect(page.getByRole('button', { name: /Em faturamento/ })).toBeVisible();
    await expect(page.getByRole('button', { name: /Aguardando recebimento/ })).toBeVisible();

    // as duas filas do recebimento abrem listas diferentes: o que as separa é a nota
    // fiscal, e antes as duas caíam no mesmo filtro de etapa
    const semNota = page.waitForResponse((r) =>
      r.url().includes('/api/v1/control-tower') && r.url().includes('invoicing=true'));
    await page.getByRole('button', { name: /Em faturamento/ }).click();
    expect((await semNota).status()).toBe(200);

    const comNota = page.waitForResponse((r) =>
      r.url().includes('/api/v1/control-tower') && r.url().includes('invoicing=false'));
    await page.getByRole('button', { name: /Aguardando recebimento/ }).click();
    expect((await comNota).status()).toBe(200);

    const comExcecao = page.waitForResponse((r) =>
      r.url().includes('/api/v1/control-tower') && r.url().includes('exception=true'));
    await page.getByRole('button', { name: /Exceções/ }).click();
    expect((await comExcecao).status()).toBe(200);
  });

  /** §5: a fila prioritária e a ação da linha, contra a API de verdade. */
  test('a fila prioritária filtra e a linha oferece a ação da etapa', async ({ page }) => {
    await abrirAutenticado(page, '/torre');
    await expect(page.getByRole('button', { name: /Precisa de você/ })).toBeVisible();

    const daFila = page.waitForResponse((r) =>
      r.url().includes('/api/v1/control-tower') && r.url().includes('needsBuyer=true'));
    await page.getByRole('button', { name: /Precisa de você/ }).click();
    expect((await daFila).status()).toBe(200);

    const tabela = page.getByTestId('tabela-torre');
    if (await tabela.count()) {
      // toda linha da fila do comprador diz o que fazer — com link quando a ação é em
      // outra tela, e sem link quando é aqui mesmo (atribuir, na barra de triagem)
      const acoes = tabela.locator('tbody tr td a.botao-secundario');
      if (await acoes.count()) {
        await expect(acoes.first()).toBeVisible();
        await expect(acoes.first()).toHaveAttribute('href', /\/(cotacoes|pedidos)/);
      } else {
        await expect(tabela).toContainText('aqui ↑');
      }
    } else {
      await expect(page.getByText('Nenhum item de compra neste recorte.')).toBeVisible();
    }
  });

  test('a paginação é do servidor: a página pedida vai na consulta', async ({ page }) => {
    await abrirAutenticado(page, '/torre');
    // esperar o painel de filtros não basta: ele renderiza antes de os dados
    // chegarem, e a contagem ainda não existiria quando o teste perguntasse
    await expect(page.getByTestId('tabela-torre')
      .or(page.getByText('Nenhum item de compra neste recorte.'))).toBeVisible();

    const contagem = page.getByTestId('torre-contagem');
    if (!(await contagem.count())) {
      // sem item no ambiente, a tela mostra o estado vazio — e isso já é o contrato
      await expect(page.getByText('Nenhum item de compra neste recorte.')).toBeVisible();
      return;
    }

    const proxima = page.getByRole('button', { name: 'Próxima' });
    if (await proxima.isEnabled()) {
      const pagina2 = page.waitForResponse((r) =>
        r.url().includes('/api/v1/control-tower') && r.url().includes('page=2'));
      await proxima.click();
      expect((await pagina2).status()).toBe(200);
      await expect(contagem).toContainText('página 2');
      await expect(page.getByRole('button', { name: 'Anterior' })).toBeEnabled();
    } else {
      // uma página só: "Próxima" tem de estar travada, não sumida
      await expect(proxima).toBeDisabled();
    }
  });

  /** §5.1: os filtros que dependem do pedido saem na consulta com o nome certo. */
  test('fornecedor, número da O.C. e faixa de valor vão para o servidor', async ({ page }) => {
    await abrirAutenticado(page, '/torre');
    await expect(page.locator('#tc-fornecedor')).toBeVisible();

    await page.fill('#tc-fornecedor', 'Alfa');
    await page.fill('#tc-oc', '4521');
    await page.fill('#tc-valor-de', '100');
    const consulta = page.waitForResponse((r) =>
      r.url().includes('/api/v1/control-tower')
      && r.url().includes('supplier=Alfa')
      && r.url().includes('orderNumber=4521')
      && r.url().includes('minValue=100'));
    await page.getByRole('button', { name: 'Aplicar filtros' }).click();
    expect((await consulta).status()).toBe(200);

    // e o recorte sem resultado se explica, em vez de mostrar tabela vazia
    await expect(page.getByTestId('tabela-torre')
      .or(page.getByText('Nenhum item de compra neste recorte.'))).toBeVisible();
  });

  /**
   * A marca é por item: a cotação leva só o que foi marcado, e o outro item da mesma SC
   * fica pendente nela. A atribuição continua valendo para a SC inteira.
   */
  test('marcar um item abre a cotação só com ele, e o resto da SC fica pendente', async ({ page }) => {
    const marca = Date.now().toString().slice(-6);
    const { accessToken } = JSON.parse(readFileSync(ARQUIVO_SESSAO, 'utf8'));
    const api = (caminho: string, corpo: unknown) => page.request.post(caminho, {
      headers: { Authorization: 'Bearer ' + accessToken }, data: corpo,
    });
    const criada = await api('/api/v1/purchase-requisitions/', {
      justification: `E2E seleção por item ${marca}`, costCenter: 'E2E-001', priority: 'NORMAL', purpose: 'COMPRA',
      items: [
        { description: `Caneta ${marca}`, quantity: 10, unitOfMeasure: 'UN' },
        { description: `Grampeador ${marca}`, quantity: 2, unitOfMeasure: 'UN' },
      ],
    });
    expect(criada.status(), await criada.text()).toBe(201);
    const sc = (await criada.json()).data;
    expect((await api(`/api/v1/purchase-requisitions/${sc.id}/submit`, {})).ok()).toBeTruthy();

    await abrirAutenticado(page, '/torre');
    await page.fill('#tc-busca', sc.number);
    await page.getByRole('button', { name: 'Aplicar filtros' }).click();
    const tabela = page.getByTestId('tabela-torre');
    await expect(tabela.locator('tbody tr')).toHaveCount(2);

    // só a caneta: o grampeador não é marcado junto, como era quando a marca era da SC
    await tabela.getByLabel(`Selecionar ${sc.number} item 1`).check();
    await expect(tabela.getByLabel(`Selecionar ${sc.number} item 2`)).not.toBeChecked();
    const barra = page.getByTestId('triagem-torre');
    await expect(barra.getByTestId('resumo-selecao')).toContainText('1 item(ns) de 1 solicitação(ões)');
    // sem responsável escolhido, a atribuição continua travada
    await expect(page.getByRole('button', { name: /^Atribuir/ })).toBeDisabled();

    await page.getByTestId('cotar-marcados').click();
    await expect(page).toHaveURL(/\/cotacoes\/[0-9a-f-]+$/);
    const itens = page.getByTestId('itens-cotacao');
    await expect(itens).toContainText(`Caneta ${marca}`);
    await expect(itens).not.toContainText(`Grampeador ${marca}`);

    // de volta à Torre: a caneta está em cotação, e o grampeador segue na Solicitação da mesma SC
    await page.goto('/torre');
    await page.fill('#tc-busca', sc.number);
    await page.getByRole('button', { name: 'Aplicar filtros' }).click();
    const linhas = page.getByTestId('tabela-torre').locator('tbody tr');
    await expect(linhas.filter({ hasText: `Caneta ${marca}` })).toContainText('Cotação');
    await expect(linhas.filter({ hasText: `Grampeador ${marca}` })).toContainText('Solicitação');
  });

  test('filtrar por centro de custo refaz a consulta e limpar desfaz', async ({ page }) => {
    await abrirAutenticado(page, '/torre');
    await expect(page.locator('#tc-cc')).toBeVisible();

    if ((await page.locator('#tc-cc option').count()) > 1) {
      await page.selectOption('#tc-cc', { index: 1 });
      const comCc = page.waitForResponse((r) =>
        r.url().includes('/api/v1/control-tower') && r.url().includes('costCenter='));
      await page.getByRole('button', { name: 'Aplicar filtros' }).click();
      await comCc;

      const semCc = page.waitForResponse((r) =>
        r.url().includes('/api/v1/control-tower') && !r.url().includes('costCenter='));
      await page.getByRole('button', { name: 'Limpar' }).click();
      await semCc;
    } else {
      await page.getByRole('button', { name: 'Limpar' }).click();
    }
    await expect(page.locator('#tc-cc')).toHaveValue('');
  });

  /** O que veio da triagem junto com a unificação: fila por tempo e prioridade na linha. */
  test('o tempo na fila filtra, e a prioridade se muda na própria linha', async ({ page }) => {
    await abrirAutenticado(page, '/torre');
    const faixas = page.getByTestId('faixas-de-fila');
    await expect(faixas).toBeVisible();

    const consulta = page.waitForResponse((r) => r.url().includes('agingBand='));
    await faixas.getByRole('button', { name: /6–10 dias/ }).click();
    expect((await consulta).status()).toBe(200);

    // a régua da urgência é a mesma da triagem: motivo E impacto, os dois obrigatórios
    const mudar = page.getByRole('button', { name: /tornar urgente|voltar a normal/ }).first();
    if (await mudar.count()) {
      await mudar.click();
      const dialogo = page.getByRole('dialog');
      await expect(dialogo.getByRole('button', { name: 'Registrar mudança' })).toBeDisabled();
    }
  });
});
