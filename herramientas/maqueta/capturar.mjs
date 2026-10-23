import { chromium } from 'playwright-core';
import { readdirSync } from 'node:fs';
const base = process.env.HOME + '/Library/Caches/ms-playwright/chromium_headless_shell-1248/';
const ejecutable = base + readdirSync(base).find(n => n.startsWith('chrome-headless-shell')) + '/chrome-headless-shell';
const [maqueta, salida] = process.argv.slice(2);
const navegador = await chromium.launch({ executablePath: ejecutable });
const pagina = await navegador.newPage({ viewport: { width: 1300, height: 1500 }, deviceScaleFactor: 2 });
await pagina.goto('file://' + maqueta);
for (const id of ['escritorio', 'menu-miniatura', 'menu-bandeja'])
  await pagina.locator('#' + id).screenshot({ path: `${salida}/${id}.png` });
await navegador.close();
