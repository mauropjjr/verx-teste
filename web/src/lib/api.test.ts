// @vitest-environment node
import { beforeEach, describe, expect, it, vi } from "vitest";
import { CookiesFalsos, RedirecionamentoFalso } from "../../testes/next-falso";
import { criarTokenTeste, segundosDesdeEpoca } from "../../testes/tokens";

const cookiesFalsos = vi.hoisted(() => ({ atual: undefined as unknown }));
vi.mock("next/headers", () => ({ cookies: async () => cookiesFalsos.atual }));
vi.mock("next/navigation", async () => {
  const { redirectFalso } = await import("../../testes/next-falso");
  return { redirect: redirectFalso };
});

import {
  consultarConsolidadoDiario,
  consultarExtrato,
  consultarSaldo,
  ErroApi,
  estornarLancamento,
  listarContas,
  registrarLancamento,
} from "./api";

const token = criarTokenTeste({ sub: "u-1", role: "consulta", exp: segundosDesdeEpoca(new Date(Date.now() + 3_600_000)) });

describe("cliente HTTP das APIs (servidor)", () => {
  let fetchFalso: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    const cookies = new CookiesFalsos();
    cookies.set("sessao", token);
    cookiesFalsos.atual = cookies;
    fetchFalso = vi.fn();
    vi.stubGlobal("fetch", fetchFalso);
    vi.stubEnv("LANCAMENTOS_URL", "http://lancamentos.teste");
    vi.stubEnv("CONSOLIDADO_URL", "http://consolidado.teste");
  });

  it("lista contas na API de lançamentos com o token do cookie como Bearer", async () => {
    fetchFalso.mockResolvedValue(Response.json([{ id: "CC-0001", titular: "M*** S***", situacao: "ATIVA" }]));

    const contas = await listarContas();

    expect(contas).toEqual([{ id: "CC-0001", titular: "M*** S***", situacao: "ATIVA" }]);
    const [url, init] = fetchFalso.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("http://lancamentos.teste/api/v1/contas");
    expect(new Headers(init.headers).get("Authorization")).toBe(`Bearer ${token}`);
  });

  it("consulta o extrato com limite e cursor na query", async () => {
    fetchFalso.mockResolvedValue(Response.json({ itens: [], proximoCursor: null }));

    await consultarExtrato("CC-0001", { cursor: "abc+/=", limite: 20 });

    expect(fetchFalso.mock.calls[0][0]).toBe("http://lancamentos.teste/api/v1/contas/CC-0001/lancamentos?limite=20&cursor=abc%2B%2F%3D");
  });

  it("consulta o saldo na API de consolidado", async () => {
    fetchFalso.mockResolvedValue(Response.json({ contaId: "CC-0001", saldo: 10, em: "2026-10-12T12:00:00Z", atualizadoAte: null }));

    await consultarSaldo("CC-0001");

    expect(fetchFalso.mock.calls[0][0]).toBe("http://consolidado.teste/api/v1/contas/CC-0001/saldo");
  });

  it("consulta o saldo em um instante com `em` na query", async () => {
    fetchFalso.mockResolvedValue(Response.json({ contaId: "CC-0001", saldo: 10, em: "2026-10-01T15:00:00Z", atualizadoAte: null }));

    await consultarSaldo("CC-0001", "2026-10-01T15:00:00.000Z");

    expect(fetchFalso.mock.calls[0][0]).toBe("http://consolidado.teste/api/v1/contas/CC-0001/saldo?em=2026-10-01T15%3A00%3A00.000Z");
  });

  it("consulta o consolidado diário do período com de/ate na query", async () => {
    fetchFalso.mockResolvedValue(Response.json({ contaId: "CC-0001", dias: [], atualizadoAte: null }));

    await consultarConsolidadoDiario("CC-0001", "2026-09-14", "2026-10-13");

    expect(fetchFalso.mock.calls[0][0]).toBe(
      "http://consolidado.teste/api/v1/contas/CC-0001/consolidado-diario?de=2026-09-14&ate=2026-10-13",
    );
  });

  it("registra lançamento com POST, Idempotency-Key e valor decimal exato no corpo", async () => {
    fetchFalso.mockResolvedValue(Response.json({ id: "l-1" }, { status: 201 }));

    await registrarLancamento("CC-0001", { tipo: "CREDITO", valor: "1234567890123456.78", descricao: "Depósito" }, "chave-1");

    const [url, init] = fetchFalso.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("http://lancamentos.teste/api/v1/contas/CC-0001/lancamentos");
    expect(init.method).toBe("POST");
    const cabecalhos = new Headers(init.headers);
    expect(cabecalhos.get("Idempotency-Key")).toBe("chave-1");
    expect(cabecalhos.get("Content-Type")).toBe("application/json");
    expect(cabecalhos.get("Authorization")).toBe(`Bearer ${token}`);
    // O valor vai como número JSON literal, sem passar por ponto flutuante (evita perder centavos).
    expect(String(init.body)).toContain('"valor":1234567890123456.78');
    expect(JSON.parse(String(init.body))).toMatchObject({ tipo: "CREDITO", descricao: "Depósito" });
  });

  it("estorna lançamento com POST na rota de estorno e Idempotency-Key", async () => {
    fetchFalso.mockResolvedValue(Response.json({ id: "l-2" }, { status: 201 }));

    await estornarLancamento("CC-0001", "a1b2", "chave-2");

    const [url, init] = fetchFalso.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("http://lancamentos.teste/api/v1/contas/CC-0001/lancamentos/a1b2/estorno");
    expect(init.method).toBe("POST");
    expect(new Headers(init.headers).get("Idempotency-Key")).toBe("chave-2");
  });

  it("falha com Problem Details expõe o problema no ErroApi", async () => {
    fetchFalso.mockResolvedValue(
      Response.json(
        { title: "One or more validation errors occurred.", status: 400, errors: { valor: ["O valor deve ser maior que zero."] } },
        { status: 400, headers: { "Content-Type": "application/problem+json" } },
      ),
    );

    await expect(estornarLancamento("CC-0001", "x", "k")).rejects.toMatchObject({
      status: 400,
      problema: { errors: { valor: ["O valor deve ser maior que zero."] } },
    });
  });

  it("resposta 401 da API manda o usuário de volta ao login", async () => {
    fetchFalso.mockResolvedValue(new Response(null, { status: 401 }));

    await expect(listarContas()).rejects.toEqual(new RedirecionamentoFalso("/login"));
  });

  it("outras falhas viram ErroApi com o status", async () => {
    fetchFalso.mockResolvedValue(new Response(null, { status: 503 }));

    await expect(consultarSaldo("CC-0001")).rejects.toMatchObject({ name: "ErroApi", status: 503 });
    await expect(consultarSaldo("CC-0001")).rejects.toBeInstanceOf(ErroApi);
  });
});
