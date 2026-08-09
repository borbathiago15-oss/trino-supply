import { chromium } from "playwright";
import { readFileSync } from "node:fs";

const companyId = readFileSync("/tmp/cid.txt", "utf8").trim();
const BASE = "http://127.0.0.1:3000";

// Caminho do Chromium configurável (ambientes CI variam). Default: chrome-headless-shell local.
const chromePath = process.env.PW_CHROMIUM || "/opt/pw-browsers/chromium_headless_shell-1194/chrome-linux/headless_shell";
const browser = await chromium.launch({ executablePath: chromePath });
const page = await browser.newPage();
const results = [];
const check = (name, ok, extra = "") => { results.push([ok ? "PASS" : "FAIL", name, extra]); };

try {
  // 1) Login
  await page.goto(`${BASE}/login`, { waitUntil: "networkidle" });
  check("login page carrega", (await page.title()) === "Trino Supply");
  await page.getByLabel("Empresa (Company ID)").fill(companyId);
  await page.getByLabel("E-mail").fill("admin@trino.com");
  await page.getByLabel("Senha").fill("senha12345");
  await page.getByRole("button", { name: "Entrar" }).click();

  // 2) Redireciona para o painel
  await page.waitForURL("**/dashboard", { timeout: 10000 });
  await page.waitForLoadState("networkidle");
  check("login -> dashboard", page.url().endsWith("/dashboard"));
  const painel = await page.getByRole("heading", { name: "Painel" }).isVisible();
  check("painel visível", painel);

  // 3) Dados reais do backend no painel (1 item, 1 sugestão)
  const body = await page.textContent("body");
  check("painel mostra itens/sugestões (dados do backend)", /Itens cadastrados/.test(body ?? ""));

  // 4) Navega para Reposição e vê a sugestão do PARAFUSO
  await page.getByRole("link", { name: "Reposição" }).first().click();
  await page.waitForURL("**/reposicao");
  await page.waitForLoadState("networkidle");
  const rep = await page.textContent("body");
  check("reposição lista sugestão PARAFUSO", /PARAFUSO/.test(rep ?? ""));

  // 5) Screenshot do painel para evidência
  await page.getByRole("link", { name: "Painel" }).first().click();
  await page.waitForURL("**/dashboard");
  await page.waitForLoadState("networkidle");
  await page.screenshot({ path: "/tmp/trino-dashboard.png", fullPage: true });
  check("screenshot do painel salvo", true, "/tmp/trino-dashboard.png");
} catch (e) {
  check("execução sem exceção", false, String(e).slice(0, 200));
} finally {
  await browser.close();
}

console.log("\n=== RESULTADO E2E (navegador real) ===");
for (const [status, name, extra] of results) console.log(`${status}  ${name}${extra ? "  — " + extra : ""}`);
const failed = results.filter((r) => r[0] === "FAIL").length;
console.log(`\n${results.length - failed}/${results.length} checks OK`);
process.exit(failed ? 1 : 0);
