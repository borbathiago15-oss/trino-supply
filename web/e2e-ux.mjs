import { chromium } from "playwright";
import { readFileSync } from "node:fs";

const companyId = readFileSync("/tmp/cid.txt", "utf8").trim();
const BASE = "http://127.0.0.1:3000";
const chromePath = process.env.PW_CHROMIUM || "/opt/pw-browsers/chromium_headless_shell-1194/chrome-linux/headless_shell";

const browser = await chromium.launch({ executablePath: chromePath });
const page = await browser.newPage();
const results = [];
const check = (name, ok, extra = "") => results.push([ok ? "PASS" : "FAIL", name, extra]);

try {
  // Login
  await page.goto(`${BASE}/login`, { waitUntil: "networkidle" });
  await page.getByLabel("Empresa (Company ID)").fill(companyId);
  await page.getByLabel("E-mail").fill("admin3@trino.com");
  await page.getByLabel("Senha").fill("senha12345");
  await page.getByRole("button", { name: "Entrar" }).click();
  await page.waitForURL("**/dashboard", { timeout: 10000 });
  check("login", true);

  // Materiais → definir política de reposição pela UI
  await page.getByRole("link", { name: "Materiais" }).first().click();
  await page.waitForURL("**/materiais");
  await page.waitForLoadState("networkidle");

  const pol = page.locator('section:has-text("Política de reposição")');
  await pol.getByLabel("Item (código)").fill("PARAFUSO");
  await pol.getByLabel("Mínimo (repor quando ≤)").fill("10");
  await pol.getByLabel("Máximo (repor até)").fill("100");
  await pol.getByRole("button", { name: "Definir política" }).click();

  // Toast de sucesso aparece
  await page.getByText("Política de reposição definida").waitFor({ timeout: 8000 });
  check("toast de sucesso ao definir política", true);

  // Reposição → a sugestão do PARAFUSO aparece (100 - 0 = 100)
  await page.getByRole("link", { name: "Reposição" }).first().click();
  await page.waitForURL("**/reposicao");
  await page.getByText("PARAFUSO").first().waitFor({ timeout: 8000 });
  check("reposição lista PARAFUSO após política definida pela UI", true);

  await page.screenshot({ path: "/tmp/trino-ux.png", fullPage: true });
  check("screenshot salvo", true, "/tmp/trino-ux.png");
} catch (e) {
  check("execução sem exceção", false, String(e).slice(0, 200));
} finally {
  await browser.close();
}

console.log("\n=== E2E UX (navegador real) ===");
for (const [s, n, x] of results) console.log(`${s}  ${n}${x ? "  — " + x : ""}`);
const failed = results.filter((r) => r[0] === "FAIL").length;
console.log(`\n${results.length - failed}/${results.length} checks OK`);
process.exit(failed ? 1 : 0);
