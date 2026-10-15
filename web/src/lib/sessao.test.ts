// @vitest-environment node
import { beforeEach, describe, expect, it, vi } from "vitest";
import { CookiesFalsos } from "../../testes/next-falso";
import { criarTokenTeste, segundosDesdeEpoca } from "../../testes/tokens";

const cookiesFalsos = vi.hoisted(() => ({ atual: undefined as unknown }));
vi.mock("next/headers", () => ({ cookies: async () => cookiesFalsos.atual }));

import { obterSessao } from "./sessao";

const daquiUmaHora = segundosDesdeEpoca(new Date(Date.now() + 3_600_000));

describe("obterSessao", () => {
  let cookies: CookiesFalsos;

  beforeEach(() => {
    cookies = new CookiesFalsos();
    cookiesFalsos.atual = cookies;
  });

  it("expõe usuário, nome e todos os papéis do token", async () => {
    cookies.set("sessao", criarTokenTeste({ sub: "u-1", name: "Operador Demo", role: ["consulta", "operador"], exp: daquiUmaHora }));

    expect(await obterSessao()).toEqual({ usuarioId: "u-1", nome: "Operador Demo", papeis: ["consulta", "operador"] });
  });

  it("aceita um único papel como texto", async () => {
    cookies.set("sessao", criarTokenTeste({ sub: "u-2", name: "Consulta", role: "consulta", exp: daquiUmaHora }));

    expect((await obterSessao())?.papeis).toEqual(["consulta"]);
  });

  it("sem cookie ou com token expirado não há sessão", async () => {
    expect(await obterSessao()).toBeNull();

    cookies.set("sessao", criarTokenTeste({ sub: "u-1", role: "operador", exp: segundosDesdeEpoca(new Date(Date.now() - 1000)) }));
    expect(await obterSessao()).toBeNull();
  });
});
