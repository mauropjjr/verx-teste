import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Locator, type Page } from "@playwright/test";

// Smoke do portal contra o ambiente no ar (docker compose up --build -d): fluxo inteiro só com teclado e
// verificação WCAG 2.1 AA (axe) em cada tela. BASE_URL muda o endereço (padrão http://localhost:3000).

const TAGS_WCAG = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"];

async function semViolacoesWcag(page: Page, tela: string) {
  const { violations } = await new AxeBuilder({ page }).withTags(TAGS_WCAG).analyze();
  const resumo = violations.map((v) => `${v.id} (${v.impact}): ${v.nodes.map((n) => n.target.join(" ")).join(", ")}`);
  expect(resumo, `violações WCAG 2.1 AA na tela ${tela}`).toEqual([]);
}

/**
 * Avança com Tab (ou volta com Shift+Tab) até o elemento receber o foco — só teclado; falha se não chegar em
 * `limite` passos.
 */
async function tabAte(page: Page, alvo: Locator, { voltar = false, limite = 60 } = {}) {
  for (let passo = 0; passo < limite; passo++) {
    if (await alvo.evaluate((el) => el === document.activeElement).catch(() => false)) {
      return;
    }
    await page.keyboard.press(voltar ? "Shift+Tab" : "Tab");
  }
  throw new Error(`O foco não chegou ao elemento esperado com ${limite} Tabs.`);
}

async function entrarPeloTeclado(page: Page, login: string, senha: string) {
  await page.goto("/contas");
  await expect(page).toHaveURL(/\/login\?retorno=/);
  await tabAte(page, page.getByLabel("Usuário"));
  await page.keyboard.type(login);
  await page.keyboard.press("Tab");
  await expect(page.getByLabel("Senha")).toBeFocused();
  await page.keyboard.type(senha);
  await page.keyboard.press("Enter");
  await expect(page.getByRole("heading", { level: 1, name: "Contas" })).toBeVisible();
}

test("@smoke operador faz login, registra um lançamento e consulta o consolidado só com o teclado, sem violações WCAG 2.1 AA", async ({
  page,
}) => {
  test.setTimeout(120_000);
  await page.goto("/login");
  await expect(page.getByRole("heading", { level: 1, name: "Entrar no portal" })).toBeVisible();
  await semViolacoesWcag(page, "login");

  await entrarPeloTeclado(page, "operador", "Operador@123");

  await tabAte(page, page.getByRole("link", { name: "CC-0001" }));
  await page.keyboard.press("Enter");
  await expect(page.getByRole("heading", { level: 1, name: "Conta CC-0001" })).toBeVisible();
  await semViolacoesWcag(page, "conta");

  // Lançamento com valor e descrição únicos para achá-lo no extrato.
  const unico = Date.now();
  const reais = 100 + (unico % 900);
  const centavos = String(unico % 100).padStart(2, "0");
  const valor = `${reais},${centavos}`;
  const descricao = `E2E teclado ${unico}`;

  await tabAte(page, page.getByRole("radio", { name: "Crédito" }));
  await page.keyboard.press("Space");
  await expect(page.getByRole("radio", { name: "Crédito" })).toBeChecked();
  await page.keyboard.press("Tab");
  await expect(page.getByLabel("Valor (R$)")).toBeFocused();
  await page.keyboard.type(valor);
  await tabAte(page, page.getByLabel("Descrição"));
  await page.keyboard.type(descricao);
  const registrar = page.getByRole("button", { name: "Registrar" });
  await tabAte(page, registrar);
  // Foco visível no botão (contorno desenhado pelo navegador com o estilo do portal).
  expect(await registrar.evaluate((el) => getComputedStyle(el).outlineStyle)).not.toBe("none");
  await page.keyboard.press("Enter");

  await expect(page.getByText(`Lançamento registrado: crédito de R$ ${valor}.`)).toBeVisible();
  const extrato = page.getByRole("table", { name: "Extrato da conta CC-0001" });
  await expect(extrato.getByRole("row").filter({ hasText: descricao })).toBeVisible();
  await semViolacoesWcag(page, "conta após o registro");

  // O link fica acima do formulário: volta com Shift+Tab a partir do botão Registrar.
  await tabAte(page, page.getByRole("link", { name: "Consolidado por período" }), { voltar: true });
  await page.keyboard.press("Enter");
  await expect(page.getByRole("heading", { level: 1, name: "Consolidado da conta CC-0001" })).toBeVisible();
  // O período padrão (últimos 30 dias) inclui hoje; consulta de novo pelo botão, também pelo teclado.
  await tabAte(page, page.getByRole("button", { name: "Consultar", exact: true }));
  await page.keyboard.press("Enter");
  await expect(page).toHaveURL(/de=\d{4}-\d{2}-\d{2}&ate=\d{4}-\d{2}-\d{2}/);
  await expect(page.getByRole("table", { name: /^Consolidado diário de / })).toBeVisible();
  await expect(page.getByText(/^Dados atualizados até \d{2}\/\d{2}\/\d{4}/)).toBeVisible();
  await semViolacoesWcag(page, "consolidado");
});

test("usuário consulta não vê ações de escrita na página da conta", async ({ page }) => {
  await entrarPeloTeclado(page, "consulta", "Consulta@123");

  await page.goto("/contas/CC-0001");
  await expect(page.getByRole("table", { name: "Extrato da conta CC-0001" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Registrar" })).toHaveCount(0);
  await expect(page.getByRole("button", { name: /^Estornar/ })).toHaveCount(0);
  await expect(page.getByRole("heading", { name: "Registrar lançamento" })).toHaveCount(0);
});
