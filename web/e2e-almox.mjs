import { chromium } from "playwright";
import { readFileSync, statSync, existsSync } from "node:fs";

const companyId = readFileSync("/tmp/cid.txt", "utf8").trim();
const BASE = "http://127.0.0.1:3000";
const chromePath = process.env.PW_CHROMIUM || "/opt/pw-browsers/chromium_headless_shell-1194/chrome-linux/headless_shell";

const browser = await chromium.launch({ executablePath: chromePath });
const context = await browser.newContext({ acceptDownloads: true });
const page = await context.newPage();
const results = [];
const check = (name, ok, extra = "") => results.push([ok ? "PASS" : "FAIL", name, extra]);
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

try {
  await login("admin@trino.com");
  await page.getByRole("link", { name: "Almoxarifado" }).first().click();
  await page.waitForURL("**/almoxarifado");
  await page.waitForLoadState("networkidle");

  // Cadastrar colaborador
  const col = card("Colaboradores");
  await col.getByLabel("Nome").fill("Jose da Silva");
  await col.getByLabel("Matrícula").fill("MAT-0091");
  await col.getByLabel("Data de contratação").fill("2026-02-01");
  await col.getByLabel("Centro de custo (código)").fill("CC-OBRAS");
  await col.getByLabel("Empresa (código)").fill("EP1");
  await col.getByRole("button", { name: "Cadastrar colaborador" }).click();
  await col.getByText("Jose da Silva").first().waitFor({ timeout: 8000 });
  check("colaborador cadastrado pela UI", true);

  // Baixa de consumo
  const bx = card("Baixa de consumo (entrega de EPI/Fardamento)");
  await bx.getByLabel("Colaborador").selectOption({ label: "Jose da Silva (MAT-0091)" });
  await bx.getByLabel("Motivo").selectOption("Nova contratação");
  // empresa/centro pré-preenchidos pelo colaborador; garante valores:
  await bx.getByLabel("Empresa (código)").fill("EP1");
  await bx.getByLabel("Centro de custo (código)").fill("CC-OBRAS");
  await bx.getByLabel("Produto (código)").fill("BOTA-42");
  await bx.getByLabel("Quantidade").fill("1");
  await bx.getByRole("button", { name: "Adicionar produto" }).click();
  await bx.getByText("BOTA-42").first().waitFor({ timeout: 6000 });
  check("produto adicionado à baixa", true);

  // Registrar baixa → dispara download da ficha
  const dl = page.waitForEvent("download", { timeout: 12000 });
  await bx.getByRole("button", { name: /Registrar baixa e gerar ficha/ }).click();
  const ficha = await dl;
  const fpath = await ficha.path();
  const sig = existsSync(fpath) ? readFileSync(fpath).subarray(0, 5).toString("ascii") : "";
  check("baixa gerou a Ficha de Entrega (PDF)", sig === "%PDF-", `size=${existsSync(fpath) ? statSync(fpath).size : 0}`);

  // A baixa aparece na lista, com botão de Ficha
  const lst = card("Baixas de consumo");
  await lst.getByText("Jose da Silva").first().waitFor({ timeout: 8000 });
  const dl2 = page.waitForEvent("download", { timeout: 10000 });
  await lst.getByRole("button", { name: "Ficha (PDF)" }).first().click();
  const f2 = await dl2;
  check("re-baixa da ficha pela lista", readFileSync(await f2.path()).subarray(0, 5).toString("ascii") === "%PDF-");

  await page.screenshot({ path: "/tmp/trino-almox.png", fullPage: true });
} catch (e) {
  check("execução sem exceção", false, String(e).slice(0, 400));
} finally {
  await browser.close();
}

console.log("\n=== E2E Almoxarifado (colaborador, baixa, ficha PDF) ===");
for (const [s, n, x] of results) console.log(`${s}  ${n}${x ? "  — " + x : ""}`);
const failed = results.filter((r) => r[0] === "FAIL").length;
console.log(`\n${results.length - failed}/${results.length} checks OK`);
process.exit(failed ? 1 : 0);
