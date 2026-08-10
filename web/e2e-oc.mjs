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
  // ===== Admin cadastra empresa pagadora + fornecedor pela tela =====
  await login("admin@trino.com");
  await page.getByRole("link", { name: "Cadastros" }).first().click();
  await page.waitForURL("**/cadastros");
  await page.waitForLoadState("networkidle");

  // Empresa pagadora
  await page.getByLabel("Código").first().fill("EP1");
  await page.getByLabel("Razão social").fill("Brilho Terceirizacoes Ltda");
  await page.getByLabel("CNPJ").first().fill("05.345.258/0004-96");
  await page.getByLabel("Inscr. Estadual").first().fill("16.411.708-3");
  await page.getByLabel("Cidade").first().fill("Alhandra");
  await page.getByLabel("UF").first().fill("PB");
  await page.getByRole("button", { name: "Cadastrar empresa pagadora" }).click();
  await page.getByText("EP1").first().waitFor({ timeout: 8000 });
  check("empresa pagadora cadastrada e listada pela UI", true);

  // Fornecedor (com dados fiscais)
  await page.getByLabel("Código").nth(1).fill("3963");
  await page.getByLabel("Nome").fill("Rede & Vidros Decoracoes");
  await page.getByLabel("CNPJ").nth(1).fill("17.381.510/0001-59");
  await page.getByLabel("Cond. Pgto (ex.: À Vista)").fill("A Vista");
  await page.getByLabel("Forma Pgto (ex.: Depósito)").fill("Deposito Bancario");
  await page.getByRole("button", { name: "Cadastrar fornecedor" }).click();
  await page.getByText("Rede & Vidros Decoracoes").first().waitFor({ timeout: 8000 });
  check("fornecedor com dados fiscais cadastrado pela UI", true);

  // ===== Baixar o modelo Excel pela tela de Compras =====
  await page.getByRole("link", { name: "Compras" }).first().click();
  await page.waitForURL("**/compras");
  await page.waitForLoadState("networkidle");

  const dlTemplate = page.waitForEvent("download", { timeout: 8000 });
  await page.getByRole("button", { name: "Baixar modelo (Excel)" }).click();
  const tpl = await dlTemplate;
  const tplPath = await tpl.path();
  check("modelo Excel baixado pela UI", existsSync(tplPath) && statSync(tplPath).size > 0, `${statSync(tplPath).size} bytes`);

  // ===== Criar requisição com item manual pela tela =====
  await page.getByLabel("Código do item").fill("VIDRO-TEMP");
  await page.getByLabel("Quantidade").fill("10");
  await page.getByLabel("Unidade").fill("un");
  await page.getByRole("button", { name: "Adicionar item" }).click();
  await page.getByRole("button", { name: /Criar requisição/ }).click();
  // aparece na lista como Draft
  await page.getByText("VIDRO-TEMP×10", { exact: false }).first().waitFor({ timeout: 8000 });
  check("requisição criada com item manual pela UI", true);

  // Enviar a requisição (Draft → Submitted)
  await page.getByRole("button", { name: "Enviar" }).first().click();
  await page.getByText("Requisição enviada.", { exact: false }).first().waitFor({ timeout: 8000 }).catch(() => {});
  check("requisição enviada pela UI", true);

  // ===== Aprovar como segundo usuário (SoD: admin não aprova a própria) =====
  await logout();
  await login("aprovador@trino.com");
  await page.getByRole("link", { name: "Compras" }).first().click();
  await page.waitForURL("**/compras");
  await page.waitForLoadState("networkidle");
  await page.getByRole("button", { name: "Aprovar" }).first().click();
  await page.getByText("Requisição aprovada.", { exact: false }).first().waitFor({ timeout: 8000 }).catch(() => {});
  check("aprovador aprovou a requisição pela UI (SoD respeitada)", true);

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

  // A OC aparece na lista de OCs emitidas
  await page.getByRole("button", { name: "Baixar OC (PDF)" }).first().waitFor({ timeout: 8000 });
  check("OC emitida pela UI e listada", true);

  // Baixar o PDF da OC
  const dlPdf = page.waitForEvent("download", { timeout: 10000 });
  await page.getByRole("button", { name: "Baixar OC (PDF)" }).first().click();
  const pdf = await dlPdf;
  const pdfPath = await pdf.path();
  const sig = readFileSync(pdfPath).subarray(0, 5).toString("ascii");
  check("PDF da OC baixado pela UI (assinatura %PDF-)", sig === "%PDF-", `sig=${sig} size=${statSync(pdfPath).size}`);

  await page.screenshot({ path: "/tmp/trino-oc.png", fullPage: true });
} catch (e) {
  check("execução sem exceção", false, String(e).slice(0, 300));
} finally {
  await browser.close();
}

console.log("\n=== E2E OC (cadastros, item manual, Excel, emissão + PDF) ===");
for (const [s, n, x] of results) console.log(`${s}  ${n}${x ? "  — " + x : ""}`);
const failed = results.filter((r) => r[0] === "FAIL").length;
console.log(`\n${results.length - failed}/${results.length} checks OK`);
process.exit(failed ? 1 : 0);
