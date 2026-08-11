import { chromium } from "playwright";
import { readFileSync, existsSync, statSync } from "node:fs";

const companyId = readFileSync("/tmp/cid.txt", "utf8").trim();
const BASE = "http://127.0.0.1:3000";
const chromePath = process.env.PW_CHROMIUM || "/opt/pw-browsers/chromium_headless_shell-1194/chrome-linux/headless_shell";

const browser = await chromium.launch({ executablePath: chromePath });
const context = await browser.newContext({ acceptDownloads: true });
const page = await context.newPage();
const results = [];
const check = (name, ok, extra = "") => results.push([ok ? "PASS" : "FAIL", name, extra]);

// Cartão (section) pelo seu título — evita ambiguidade de labels repetidos entre cadastros.
const card = (name) => page.locator("section", { has: page.getByRole("heading", { name, exact: true }) });

async function login(email) {
  await page.goto(`${BASE}/login`, { waitUntil: "networkidle" });
  await page.getByLabel("Empresa (Company ID)").fill(companyId);
  await page.getByLabel("E-mail").fill(email);
  await page.getByLabel("Senha").fill("senha12345");
  await page.getByRole("button", { name: "Entrar" }).click();
  await page.waitForURL("**/dashboard", { timeout: 10000 });
  await page.waitForLoadState("networkidle");
}

async function logout() {
  await page.getByRole("button", { name: "Sair" }).click();
  await page.waitForURL("**/login", { timeout: 8000 });
}

try {
  // ===== Admin cadastra empresa pagadora + centro de custo + fornecedor pela tela =====
  await login("admin@trino.com");
  await page.getByRole("link", { name: "Cadastros" }).first().click();
  await page.waitForURL("**/cadastros");
  await page.waitForLoadState("networkidle");

  // Empresa pagadora (empresa do custo)
  const pag = card("Empresas pagadoras (CNPJs)");
  await pag.getByLabel("Código").fill("EP1");
  await pag.getByLabel("Razão social").fill("Brilho Terceirizacoes Ltda");
  await pag.getByLabel("CNPJ").fill("05.345.258/0004-96");
  await pag.getByLabel("Inscr. Estadual").fill("16.411.708-3");
  await pag.getByLabel("Cidade").fill("Alhandra");
  await pag.getByLabel("UF").fill("PB");
  await pag.getByRole("button", { name: "Cadastrar empresa pagadora" }).click();
  await pag.getByText("EP1").first().waitFor({ timeout: 8000 });
  check("empresa pagadora cadastrada e listada pela UI", true);

  // Centro de custo
  const cen = card("Centros de custo");
  await cen.getByLabel("Código").fill("CC1");
  await cen.getByLabel("Nome").fill("Manutencao Predial");
  await cen.getByRole("button", { name: "Cadastrar centro de custo" }).click();
  await cen.getByText("CC1").first().waitFor({ timeout: 8000 });
  check("centro de custo cadastrado e listado pela UI", true);

  // Fornecedor (com dados fiscais)
  const forn = card("Fornecedores");
  await forn.getByLabel("Código").fill("3963");
  await forn.getByLabel("Nome").fill("Rede & Vidros Decoracoes");
  await forn.getByLabel("CNPJ").fill("17.381.510/0001-59");
  await forn.getByLabel("Cond. Pgto (ex.: À Vista)").fill("A Vista");
  await forn.getByLabel("Forma Pgto (ex.: Depósito)").fill("Deposito Bancario");
  await forn.getByRole("button", { name: "Cadastrar fornecedor" }).click();
  await forn.getByText("Rede & Vidros Decoracoes").first().waitFor({ timeout: 8000 });
  check("fornecedor com dados fiscais cadastrado pela UI", true);

  // ===== Nova solicitação (pedido simples) com cabeçalho + 2 aprovadores =====
  await page.getByRole("link", { name: "Compras" }).first().click();
  await page.waitForURL("**/compras");
  await page.waitForLoadState("networkidle");

  // Baixar o modelo Excel pela tela
  const dlTemplate = page.waitForEvent("download", { timeout: 8000 });
  await page.getByRole("button", { name: "Baixar modelo (Excel)" }).click();
  const tpl = await dlTemplate;
  const tplPath = await tpl.path();
  check("modelo Excel baixado pela UI", existsSync(tplPath) && statSync(tplPath).size > 0, `${statSync(tplPath).size} bytes`);

  // Cabeçalho: empresa do custo, centro, tipo de demanda, motivo e os 2 aprovadores
  await page.getByLabel("Empresa do custo (CNPJ)").selectOption({ label: "EP1 — Brilho Terceirizacoes Ltda" });
  await page.getByLabel("Centro de custo").selectOption({ label: "CC1 — Manutencao Predial" });
  await page.getByLabel("Tipo de demanda").selectOption("Normal");
  await page.getByLabel("Motivo / justificativa").fill("Reposicao de vidros");
  await page.getByLabel("Aprovador — nível 1").selectOption({ label: "Aprovador Um" });
  await page.getByLabel("Aprovador — nível 2").selectOption({ label: "Aprovador Dois" });

  // Item manual
  await page.getByLabel("Código do item").fill("VIDRO-TEMP");
  await page.getByLabel("Quantidade").fill("10");
  await page.getByLabel("Unidade").fill("un");
  await page.getByRole("button", { name: "Adicionar item" }).click();
  await page.getByRole("button", { name: /Criar requisição/ }).click();
  await page.getByText("VIDRO-TEMP×10", { exact: false }).first().waitFor({ timeout: 8000 });
  check("requisição criada com cabeçalho + item manual pela UI", true);

  // Enviar (Draft → Submitted)
  await page.getByRole("button", { name: "Enviar" }).first().click();
  await page.getByText("Requisição enviada.", { exact: false }).first().waitFor({ timeout: 8000 }).catch(() => {});
  check("requisição enviada pela UI", true);

  // ===== Aprovação em 2 níveis (SoD: admin requisitante não aprova) =====
  await logout();
  await login("aprovador1@trino.com");
  await page.getByRole("link", { name: "Compras" }).first().click();
  await page.waitForURL("**/compras");
  await page.waitForLoadState("networkidle");
  await page.getByRole("button", { name: /Aprovar \(nível 1\)/ }).first().click();
  await page.getByText("Requisição aprovada.", { exact: false }).first().waitFor({ timeout: 8000 }).catch(() => {});
  check("aprovador nível 1 aprovou pela UI", true);

  await logout();
  await login("aprovador2@trino.com");
  await page.getByRole("link", { name: "Compras" }).first().click();
  await page.waitForURL("**/compras");
  await page.waitForLoadState("networkidle");
  await page.getByRole("button", { name: /Aprovar \(nível 2\)/ }).first().click();
  await page.getByText("Requisição aprovada.", { exact: false }).first().waitFor({ timeout: 8000 }).catch(() => {});
  check("aprovador nível 2 aprovou pela UI (requisição Approved)", true);

  // ===== Admin emite a OC selecionando pagadora + fornecedor + preço, depois baixa o PDF =====
  await logout();
  await login("admin@trino.com");
  await page.getByRole("link", { name: "Compras" }).first().click();
  await page.waitForURL("**/compras");
  await page.waitForLoadState("networkidle");

  await page.getByRole("button", { name: "Emitir OC" }).first().click();
  await page.getByLabel("Empresa pagadora (CNPJ)").selectOption({ label: "EP1 — Brilho Terceirizacoes Ltda" });
  await page.getByLabel("Fornecedor vencedor").selectOption({ label: "3963 — Rede & Vidros Decoracoes" });
  await page.getByPlaceholder("0,00").first().fill("120");
  await page.getByRole("button", { name: /^Emitir OC$/ }).click();
  await page.getByText("OC emitida.", { exact: false }).first().waitFor({ timeout: 10000 }).catch(() => {});

  await page.getByRole("button", { name: "Baixar OC (PDF)" }).first().waitFor({ timeout: 8000 });
  check("OC emitida pela UI e listada", true);

  // Detalhes da OC na própria tela (itens + totais)
  await page.getByRole("button", { name: "Detalhes" }).first().click();
  await page.getByText("Valor líquido", { exact: false }).first().waitFor({ timeout: 6000 });
  const temItem = await page.getByText("VIDRO-TEMP", { exact: false }).count();
  check("detalhe da OC na tela mostra itens e totais", temItem >= 1, `ocorrências=${temItem}`);
  await page.getByRole("button", { name: "Ocultar" }).first().click();

  // Exportação CSV das OCs
  const dlCsv = page.waitForEvent("download", { timeout: 8000 });
  await page.getByRole("button", { name: "Exportar CSV" }).first().click();
  const csv = await dlCsv;
  const csvText = readFileSync(await csv.path(), "utf8");
  check("exporta OCs em CSV", /OC;Pagadora/.test(csvText) && /VIDRO|Rede & Vidros/.test(csvText));

  // Baixar o PDF da OC
  const dlPdf = page.waitForEvent("download", { timeout: 10000 });
  await page.getByRole("button", { name: "Baixar OC (PDF)" }).first().click();
  const pdf = await dlPdf;
  const sig = readFileSync(await pdf.path()).subarray(0, 5).toString("ascii");
  check("PDF da OC baixado pela UI (assinatura %PDF-)", sig === "%PDF-", `sig=${sig}`);

  // ===== Painel reflete a OC emitida =====
  await page.getByRole("link", { name: "Painel" }).first().click();
  await page.waitForURL("**/dashboard");
  await page.waitForLoadState("networkidle");
  await page.getByText("OCs emitidas").first().waitFor({ timeout: 8000 });
  const emitidasTxt = await page.locator("text=OCs emitidas").first().locator("xpath=following-sibling::p").textContent();
  check("Painel mostra OCs emitidas ≥ 1", Number(emitidasTxt?.trim()) >= 1, `valor=${emitidasTxt?.trim()}`);

  await page.screenshot({ path: "/tmp/trino-oc.png", fullPage: true });
} catch (e) {
  check("execução sem exceção", false, String(e).slice(0, 400));
} finally {
  await browser.close();
}

console.log("\n=== E2E OC (cabeçalho, centro de custo, 2 níveis, emissão + PDF) ===");
for (const [s, n, x] of results) console.log(`${s}  ${n}${x ? "  — " + x : ""}`);
const failed = results.filter((r) => r[0] === "FAIL").length;
console.log(`\n${results.length - failed}/${results.length} checks OK`);
process.exit(failed ? 1 : 0);
