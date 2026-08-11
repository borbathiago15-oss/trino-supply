import { chromium } from "playwright";
import { readFileSync } from "node:fs";

const companyId = readFileSync("/tmp/cid.txt", "utf8").trim();
const BASE = "http://127.0.0.1:3000";
const chromePath = process.env.PW_CHROMIUM || "/opt/pw-browsers/chromium_headless_shell-1194/chrome-linux/headless_shell";
const OUT = process.env.SHOT_DIR || "/tmp";

const browser = await chromium.launch({ executablePath: chromePath });
const page = await (await browser.newContext({ viewport: { width: 1360, height: 1000 }, deviceScaleFactor: 2 })).newPage();
page.on("pageerror", (e) => console.log("PAGEERROR:", String(e).slice(0, 120)));
const log = [];

async function login(email) {
  await page.goto(`${BASE}/login`, { waitUntil: "domcontentloaded" });
  const field = page.getByLabel("Empresa (Company ID)");
  await field.waitFor({ state: "visible", timeout: 20000 });
  await field.fill(companyId);
  await page.getByLabel("E-mail").fill(email);
  await page.getByLabel("Senha").fill("senha12345");
  await page.getByRole("button", { name: "Entrar" }).click();
  await page.waitForURL("**/dashboard", { timeout: 15000 });
  await page.waitForLoadState("networkidle");
}

async function shot(path, name) {
  await page.getByRole("link", { name, exact: true }).first().click();
  await page.waitForLoadState("networkidle");
  await page.waitForTimeout(800);
  await page.screenshot({ path: `${OUT}/tour-${path}.png`, fullPage: true });
  log.push(`${path}: ok`);
}

try {
  await login("admin@trino.com");
  await page.screenshot({ path: `${OUT}/tour-painel.png`, fullPage: true });
  log.push("painel: ok");
  await shot("materiais", "Materiais");
  await shot("almoxarifado", "Almoxarifado");
  await shot("reposicao", "Reposição");
  await shot("compras", "Compras");
  await shot("cadastros", "Cadastros");
  console.log("TOUR OK\n" + log.join("\n"));
} catch (e) {
  console.log("TOUR FAIL:", String(e).slice(0, 300));
  await page.screenshot({ path: `${OUT}/tour-error.png`, fullPage: true }).catch(() => {});
} finally {
  await browser.close();
}
