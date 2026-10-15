// @vitest-environment node
import { revalidatePath } from "next/cache";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { CookiesFalsos } from "../../../../../testes/next-falso";
import { criarTokenTeste, segundosDesdeEpoca } from "../../../../../testes/tokens";

const cookiesFalsos = vi.hoisted(() => ({ atual: undefined as unknown }));
vi.mock("next/headers", () => ({ cookies: async () => cookiesFalsos.atual }));
vi.mock("next/navigation", async (importarOriginal) => {
  const { redirectFalso } = await import("../../../../../testes/next-falso");
  return { ...(await importarOriginal<typeof import("next/navigation")>()), redirect: redirectFalso };
});
vi.mock("next/cache", () => ({ revalidatePath: vi.fn() }));

import { estornar, registrar, type EstadoEstorno, type EstadoLancamento } from "./acoes";

const daquiUmaHora = segundosDesdeEpoca(new Date(Date.now() + 3_600_000));

function entrarComo(papel: string) {
  const cookies = new CookiesFalsos();
  cookies.set("sessao", criarTokenTeste({ sub: "u-1", name: papel, role: papel, exp: daquiUmaHora }));
  cookiesFalsos.atual = cookies;
}

function formulario(campos: Record<string, string>): FormData {
  const dados = new FormData();
  for (const [nome, valor] of Object.entries(campos)) {
    dados.set(nome, valor);
  }
  return dados;
}

const inicial: EstadoLancamento = { chave: "chave-form-1" };
const camposValidos = { contaId: "CC-0001", chave: "chave-form-1", tipo: "CREDITO", valor: "1.234,56", descricao: "Depósito em dinheiro" };

describe("Server Action registrar (lançamento)", () => {
  let fetchFalso: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    entrarComo("operador");
    fetchFalso = vi.fn();
    vi.stubGlobal("fetch", fetchFalso);
    vi.stubEnv("LANCAMENTOS_URL", "http://lancamentos.teste");
  });

  it("operador registra com valor pt-BR: envia a chave do formulário, confirma, revalida a página e devolve uma chave nova", async () => {
    fetchFalso.mockResolvedValue(Response.json({ id: "l-1", tipo: "CREDITO", valor: 1234.56 }, { status: 201 }));

    const estado = await registrar(inicial, formulario(camposValidos));

    const [url, init] = fetchFalso.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("http://lancamentos.teste/api/v1/contas/CC-0001/lancamentos");
    expect(new Headers(init.headers).get("Idempotency-Key")).toBe("chave-form-1");
    expect(JSON.parse(String(init.body))).toEqual({ tipo: "CREDITO", valor: 1234.56, descricao: "Depósito em dinheiro" });
    expect(estado.sucesso?.replace(/\s/g, " ")).toBe("Lançamento registrado: crédito de R$ 1.234,56.");
    expect(estado.erro).toBeUndefined();
    expect(estado.chave).not.toBe("chave-form-1");
    expect(estado.chave).toMatch(/^[0-9a-f-]{36}$/);
    expect(revalidatePath).toHaveBeenCalledWith("/contas/CC-0001");
  });

  it.each([
    ["tipo ausente", { tipo: "" }, "tipo", "Escolha crédito ou débito."],
    ["valor vazio", { valor: "" }, "valor", "Informe o valor."],
    ["valor zero", { valor: "0,00" }, "valor", "O valor deve ser maior que zero."],
    ["valor com 3 casas", { valor: "10,123" }, "valor", "Informe um valor válido, com até 2 casas decimais (ex.: 1.234,56)."],
    ["valor com letras", { valor: "dez" }, "valor", "Informe um valor válido, com até 2 casas decimais (ex.: 1.234,56)."],
    ["valor negativo", { valor: "-5" }, "valor", "Informe um valor válido, com até 2 casas decimais (ex.: 1.234,56)."],
    ["descrição vazia", { descricao: "   " }, "descricao", "Informe a descrição."],
    ["descrição longa", { descricao: "x".repeat(141) }, "descricao", "A descrição tem no máximo 140 caracteres."],
  ])("%s: erro no campo, sem chamar a API, mantendo valores e chave", async (_caso, alteracao, campo, mensagem) => {
    const estado = await registrar(inicial, formulario({ ...camposValidos, ...alteracao }));

    expect(fetchFalso).not.toHaveBeenCalled();
    expect(estado.erros).toEqual({ [campo]: mensagem });
    expect(estado.chave).toBe("chave-form-1");
    const { tipo, valor, descricao } = { ...camposValidos, ...alteracao };
    expect(estado.valores).toEqual({ tipo, valor, descricao });
  });

  it("aceita vírgula sem separador de milhar e descrição com 140 caracteres", async () => {
    fetchFalso.mockResolvedValue(Response.json({ id: "l-1" }, { status: 201 }));

    await registrar(inicial, formulario({ ...camposValidos, tipo: "DEBITO", valor: "89,9", descricao: "d".repeat(140) }));

    expect(JSON.parse(String(fetchFalso.mock.calls[0][1].body))).toMatchObject({ tipo: "DEBITO", valor: 89.9 });
  });

  it("erros de validação (400) da API viram erros por campo e a chave é mantida para a retentativa", async () => {
    fetchFalso.mockResolvedValue(
      Response.json(
        { title: "One or more validation errors occurred.", status: 400, errors: { valor: ["O valor deve ser maior que zero, até 9999999999999999.99."] } },
        { status: 400 },
      ),
    );

    const estado = await registrar(inicial, formulario(camposValidos));

    expect(estado.erros).toEqual({ valor: "O valor deve ser maior que zero, até 9999999999999999.99." });
    expect(estado.chave).toBe("chave-form-1");
    expect(estado.valores?.descricao).toBe("Depósito em dinheiro");
  });

  it("recusa de negócio (422) mostra o detalhe do problema", async () => {
    fetchFalso.mockResolvedValue(
      Response.json({ title: "Conta inativa", status: 422, detail: "A conta informada não aceita lançamentos." }, { status: 422 }),
    );

    const estado = await registrar(inicial, formulario(camposValidos));

    expect(estado.erro).toBe("Conta inativa: A conta informada não aceita lançamentos.");
    expect(estado.chave).toBe("chave-form-1");
  });

  it("API fora do ar: aviso para tentar de novo com a mesma chave (a retentativa não duplica)", async () => {
    fetchFalso.mockRejectedValue(new TypeError("fetch failed"));

    const estado = await registrar(inicial, formulario(camposValidos));

    expect(estado.erro).toBe("Não foi possível registrar o lançamento agora. Tente novamente.");
    expect(estado.chave).toBe("chave-form-1");
  });

  it("usuário só com papel consulta é recusado no servidor, sem chamar a API", async () => {
    entrarComo("consulta");

    const estado = await registrar(inicial, formulario(camposValidos));

    expect(estado.erro).toBe("Você não tem permissão para registrar lançamentos.");
    expect(fetchFalso).not.toHaveBeenCalled();
  });
});

describe("Server Action estornar", () => {
  let fetchFalso: ReturnType<typeof vi.fn>;
  const inicialEstorno: EstadoEstorno = { chave: "chave-estorno-1" };
  const camposEstorno = { contaId: "CC-0001", lancamentoId: "6f1c2b9e-0000-4000-8000-000000000001", chave: "chave-estorno-1" };

  beforeEach(() => {
    entrarComo("admin");
    fetchFalso = vi.fn();
    vi.stubGlobal("fetch", fetchFalso);
    vi.stubEnv("LANCAMENTOS_URL", "http://lancamentos.teste");
  });

  it("estorna com a chave de idempotência, confirma e revalida a página", async () => {
    fetchFalso.mockResolvedValue(Response.json({ id: "e-1" }, { status: 201 }));

    const estado = await estornar(inicialEstorno, formulario(camposEstorno));

    const [url, init] = fetchFalso.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("http://lancamentos.teste/api/v1/contas/CC-0001/lancamentos/6f1c2b9e-0000-4000-8000-000000000001/estorno");
    expect(new Headers(init.headers).get("Idempotency-Key")).toBe("chave-estorno-1");
    expect(estado.sucesso).toBe("Lançamento estornado.");
    expect(revalidatePath).toHaveBeenCalledWith("/contas/CC-0001");
  });

  it("409 mostra \"Lançamento já estornado\"", async () => {
    fetchFalso.mockResolvedValue(
      Response.json({ title: "Lançamento já estornado", status: 409, detail: "Um lançamento só pode ser estornado uma vez." }, { status: 409 }),
    );

    const estado = await estornar(inicialEstorno, formulario(camposEstorno));

    expect(estado.erro).toBe("Lançamento já estornado");
    expect(estado.chave).toBe("chave-estorno-1");
  });

  it("usuário só com papel consulta é recusado no servidor, sem chamar a API", async () => {
    entrarComo("consulta");

    const estado = await estornar(inicialEstorno, formulario(camposEstorno));

    expect(estado.erro).toBe("Você não tem permissão para estornar lançamentos.");
    expect(fetchFalso).not.toHaveBeenCalled();
  });
});
