import { chromium } from "playwright";
import { readFileSync } from "node:fs";

const companyId = readFileSync("/tmp/cid.txt", "utf8").trim();
const BASE = "http://127.0.0.1:3000";
const chromePath = process.env.PW_CHROMIUM || "/opt/pw-browsers/chromium_headless_shell-1194/chrome-linux/headless_shell";
const OUT = process.env.SHOT_DIR || "/tmp";

const browser = await chromium.launch({ executablePath: chromePath });
const page = await (await browser.newContext({ viewport: { width: 1360, height: 1000 }, deviceScaleFactor: 2 })).newPage();
const results = [];
const check = (n, ok, x = "") => results.push([ok ? "PASS" : "FAIL", n, x]);

async function login(email) {
  await page.goto(`${BASE}/login`, { waitUntil: "domcontentloaded" });
  await page.getByLabel("Empresa (Company ID)").waitFor({ state: "visible", timeout: 20000 });
  await page.getByLabel("Empresa (Company ID)").fill(companyId);
  await page.getByLabel("E-mail").fill(email);
  await page.getByLabel("Senha").fill("senha12345");
  await page.getByRole("button", { name: "Entrar" }).click();
  await page.waitForURL("**/dashboard", { timeout: 15000 });
  await page.waitForLoadState("networkidle");
}
async function logout() {
  await page.getByRole("button", { name: "Sair" }).click();
  await page.waitForURL("**/login", { timeout: 8000 });
}

try {
  // ===== Junior: Central de Aprovação escopada =====
  await login("junior@trino.com");
  check("nav mostra Dashboard de Suprimentos", (await page.getByRole("link", { name: "Dashboard de Suprimentos" }).count()) >= 1);

  await page.getByRole("link", { name: "Central de Aprovação" }).first().click();
  await page.waitForURL("**/aprovacao");
  await page.waitForLoadState("networkidle");
  // Espera os DADOS renderizarem (o botão Aprovar só existe com pedido na fila).
  await page.getByRole("button", { name: "Aprovar" }).first().waitFor({ timeout: 10000 });

  const temA = await page.getByText("CC-A", { exact: false }).count();
  const temB = await page.getByText("CC-B", { exact: false }).count();
  check("central mostra pedido do centro do junior (CC-A)", temA >= 1, `ccA=${temA}`);
  check("central NÃO mostra pedido de outro centro (CC-B)", temB === 0, `ccB=${temB}`);
  await page.screenshot({ path: `${OUT}/v2-aprovacao.png`, fullPage: true });

  await page.getByRole("button", { name: "Aprovar" }).first().click();
  await page.getByText("Decisão registrada.", { exact: false }).first().waitFor({ timeout: 8000 });
  check("junior aprovou pela Central", true);

  // ===== Admin: dashboards separados =====
  await logout();
  await login("admin@trino.com");
  await page.waitForLoadState("networkidle");
  await page.getByText("Dashboard de Suprimentos").first().waitFor({ timeout: 8000 });
  await page.screenshot({ path: `${OUT}/v2-dash-suprimentos.png`, fullPage: true });
  check("Dashboard de Suprimentos renderizado", true);

  await page.getByRole("link", { name: "Dashboard de Estoque" }).first().click();
  await page.waitForURL("**/dashboard-estoque");
  await page.waitForLoadState("networkidle");
  await page.getByText("Itens por família").first().waitFor({ timeout: 8000 });
  await page.screenshot({ path: `${OUT}/v2-dash-estoque.png`, fullPage: true });
  check("Dashboard de Estoque renderizado (famílias + reposição)", true);

  // ===== Pedido: centro pré-preenche a empresa vinculada =====
  await page.getByRole("link", { name: "Pedido" }).first().click();
  await page.waitForURL("**/compras");
  await page.waitForLoadState("networkidle");
  await page.getByLabel("Centro de custo").first().selectOption({ label: "CC-A — Centro A" });
  const empresa = await page.getByLabel("Empresa do custo (CNPJ)").first().inputValue();
  check("escolher o centro pré-preencheu a empresa vinculada (EP1)", empresa === "EP1", `empresa=${empresa}`);

  // ===== Cadastros: usuários com perfil/centros/bloqueio =====
  await page.getByRole("link", { name: "Cadastros" }).first().click();
  await page.waitForURL("**/cadastros");
  await page.waitForLoadState("networkidle");
  await page.getByText("junior@trino.com", { exact: false }).first().waitFor({ timeout: 10000 });
  const temCentros = await page.getByText("CC-A", { exact: false }).count();
  check("card Usuários lista o junior com seus centros", temCentros >= 1, `centros=${temCentros}`);
  await page.screenshot({ path: `${OUT}/v2-cadastros.png`, fullPage: true });
} catch (e) {
  check("execução sem exceção", false, String(e).slice(0, 400));
  await page.screenshot({ path: `${OUT}/v2-error.png`, fullPage: true }).catch(() => {});
} finally {
  await browser.close();
}

console.log("\n=== E2E v2 (Central de Aprovação, dashboards, vínculo CNPJ↔centro, usuários) ===");
for (const [s, n, x] of results) console.log(`${s}  ${n}${x ? "  — " + x : ""}`);
const failed = results.filter((r) => r[0] === "FAIL").length;
console.log(`\n${results.length - failed}/${results.length} checks OK`);
process.exit(failed ? 1 : 0);
