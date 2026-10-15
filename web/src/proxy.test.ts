// @vitest-environment node
import { NextRequest } from "next/server";
import { describe, expect, it } from "vitest";
import { criarTokenTeste, segundosDesdeEpoca } from "../testes/tokens";
import { proxy } from "./proxy";

function requisicao(url: string, token?: string): NextRequest {
  const cabecalhos = new Headers();
  if (token) {
    cabecalhos.set("cookie", `sessao=${token}`);
  }
  return new NextRequest(url, { headers: cabecalhos });
}

const daquiUmaHora = segundosDesdeEpoca(new Date(Date.now() + 3_600_000));
const haUmMinuto = segundosDesdeEpoca(new Date(Date.now() - 60_000));

describe("proxy de sessão", () => {
  it("redireciona para /login com o caminho pedido em retorno quando não há sessão", () => {
    const resposta = proxy(requisicao("http://localhost:3000/contas/CC-0001?cursor=abc"));

    expect(resposta.status).toBe(307);
    const destino = new URL(resposta.headers.get("location")!);
    expect(destino.pathname).toBe("/login");
    expect(destino.searchParams.get("retorno")).toBe("/contas/CC-0001?cursor=abc");
  });

  it("redireciona para /login e remove o cookie quando o token expirou", () => {
    const resposta = proxy(requisicao("http://localhost:3000/contas", criarTokenTeste({ sub: "1", exp: haUmMinuto })));

    expect(resposta.status).toBe(307);
    expect(new URL(resposta.headers.get("location")!).searchParams.get("retorno")).toBe("/contas");
    expect(resposta.cookies.get("sessao")?.value).toBe("");
  });

  it("redireciona para /login quando o cookie não é um JWT legível", () => {
    const resposta = proxy(requisicao("http://localhost:3000/contas", "lixo"));

    expect(resposta.status).toBe(307);
  });

  it("deixa a requisição seguir quando o token ainda é válido", () => {
    const resposta = proxy(requisicao("http://localhost:3000/contas", criarTokenTeste({ sub: "1", exp: daquiUmaHora })));

    expect(resposta.headers.get("location")).toBeNull();
    expect(resposta.headers.get("x-middleware-next")).toBe("1");
  });
});
