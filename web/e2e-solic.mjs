import { chromium } from "playwright";
import { readFileSync } from "node:fs";

const companyId = readFileSync("/tmp/cid.txt", "utf8").trim();
const BASE = "http://127.0.0.1:3000";
const chromePath = process.env.PW_CHROMIUM || "/opt/pw-browsers/chromium_headless_shell-1194/chrome-linux/headless_shell";

const browser = await chromium.launch({ executablePath: chromePath });
const page = await (await browser.newContext()).newPage();
const results = [];
const check = (n, ok, x = "") => results.push([ok ? "PASS" : "FAIL", n, x]);
const card = (name) => page.locator("section", { has: page.getByRole("heading", { name, exact: true }) });

async function login(email) {
  for (let attempt = 1; attempt <= 3; attempt++) {
    try {
      await page.goto(`${BASE}/login`, { waitUntil: "domcontentloaded" });
      await page.getByLabel("Empresa (Company ID)").waitFor({ state: "visible", timeout: 15000 });
      await page.getByLabel("Empresa (Company ID)").fill(companyId);
      await page.getByLabel("E-mail").fill(email);
      await page.getByLabel("Senha").fill("senha12345");
      await page.getByRole("button", { name: "Entrar" }).click();
      await page.waitForURL("**/dashboard", { timeout: 12000 });
      await page.waitForLoadState("networkidle");
      return;
    } catch (e) {
      if (attempt === 3) throw e;
      await page.waitForTimeout(1500);
    }
  }
}

const solic = () => card("Solicitações");

try {
  await login("admin@trino.com");
  await page.getByRole("link", { name: "Almoxarifado" }).first().click();
  await page.waitForURL("**/almoxarifado");
  await page.waitForLoadState("networkidle");

  // Nova solicitação
  const nv = card("Nova solicitação de EPI/Fardamento");
  await nv.getByLabel("Empresa (código)").fill("EP1");
  await nv.getByLabel("Centro de custo (código)").fill("CC1");
  await nv.getByLabel("Gestor aprovador").selectOption({ label: "Admin" });
  await nv.getByLabel("Motivo").selectOption("Danificado");
  await nv.getByLabel("Produto (código/tamanho)").fill("BOTA-42");
  await nv.getByLabel("Quantidade").fill("3");
  await nv.getByRole("button", { name: "Adicionar produto" }).click();
  await nv.getByRole("button", { name: /Enviar solicitação/ }).click();
  await solic().getByText("Danificado").first().waitFor({ timeout: 8000 });
  check("solicitação criada (Pendente)", (await solic().getByText("Pendente").count()) >= 1);

  // Aprovar (admin é o gestor)
  await solic().getByRole("button", { name: "Aprovar" }).first().click();
  await solic().getByText("Aprovado").first().waitFor({ timeout: 8000 });
  check("gestor aprovou (Aprovado)", true);

  // Iniciar separação (tem estoque)
  await solic().getByRole("button", { name: "Iniciar separação" }).first().click();
  await solic().getByText("EmSeparacao").first().waitFor({ timeout: 8000 });
  check("almoxarifado iniciou separação (EmSeparacao)", true);

  // Despachar
  await solic().getByRole("button", { name: "Despachar" }).first().click();
  await solic().getByText("EmRota").first().waitFor({ timeout: 8000 });
  check("despachado (EmRota)", true);

  // Entregar (baixa estoque)
  await solic().getByRole("button", { name: /Entregar/ }).first().click();
  await solic().getByText("Entregue").first().waitFor({ timeout: 8000 });
  check("entregue (Entregue) — baixa no estoque", true);

  await page.screenshot({ path: "/tmp/trino-solic.png", fullPage: true });
} catch (e) {
  check("execução sem exceção", false, String(e).slice(0, 400));
} finally {
  await browser.close();
}

console.log("\n=== E2E Solicitação de almoxarifado (Fluxo A) ===");
for (const [s, n, x] of results) console.log(`${s}  ${n}${x ? "  — " + x : ""}`);
const failed = results.filter((r) => r[0] === "FAIL").length;
console.log(`\n${results.length - failed}/${results.length} checks OK`);
process.exit(failed ? 1 : 0);
