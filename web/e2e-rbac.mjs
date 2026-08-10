import { chromium } from "playwright";
import { readFileSync } from "node:fs";

const companyId = readFileSync("/tmp/cid.txt", "utf8").trim();
const BASE = "http://127.0.0.1:3000";
const chromePath = process.env.PW_CHROMIUM || "/opt/pw-browsers/chromium_headless_shell-1194/chrome-linux/headless_shell";

const browser = await chromium.launch({ executablePath: chromePath });
const page = await browser.newPage();
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

try {
  // --- Aprovador: só purchases.approve/read ---
  await login("aprovador@trino.com");
  await page.getByRole("link", { name: "Compras" }).first().waitFor({ timeout: 8000 });

  const materiaisCount = await page.getByRole("link", { name: "Materiais" }).count();
  check("aprovador NÃO vê o menu Materiais (sem materials.read)", materiaisCount === 0, `count=${materiaisCount}`);
  const comprasCount = await page.getByRole("link", { name: "Compras" }).count();
  check("aprovador vê o menu Compras", comprasCount >= 1);

  await page.getByRole("link", { name: "Compras" }).first().click();
  await page.waitForURL("**/compras");
  await page.waitForLoadState("networkidle");
  await page.getByRole("button", { name: "Aprovar" }).first().waitFor({ timeout: 8000 });
  check("aprovador vê o botão Aprovar na requisição enviada", true);

  const supplierBtn = await page.getByRole("button", { name: "Cadastrar fornecedor" }).count();
  check("aprovador NÃO vê o form de fornecedor (sem purchases.order)", supplierBtn === 0, `count=${supplierBtn}`);

  // --- Admin: tudo ---
  await page.getByRole("button", { name: "Sair" }).click();
  await page.waitForURL("**/login", { timeout: 8000 });
  await login("admin@trino.com");
  await page.getByRole("link", { name: "Materiais" }).first().waitFor({ timeout: 8000 });
  check("admin vê o menu Materiais (tem todas as permissões)", true);

  await page.screenshot({ path: "/tmp/trino-rbac.png", fullPage: true });
} catch (e) {
  check("execução sem exceção", false, String(e).slice(0, 200));
} finally {
  await browser.close();
}

console.log("\n=== E2E UI ciente de papéis ===");
for (const [s, n, x] of results) console.log(`${s}  ${n}${x ? "  — " + x : ""}`);
const failed = results.filter((r) => r[0] === "FAIL").length;
console.log(`\n${results.length - failed}/${results.length} checks OK`);
process.exit(failed ? 1 : 0);
