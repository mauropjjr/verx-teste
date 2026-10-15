// @vitest-environment node
import { beforeEach, describe, expect, it, vi } from "vitest";
import { CookiesFalsos, RedirecionamentoFalso } from "../../../testes/next-falso";

const cookiesFalsos = vi.hoisted(() => ({ atual: undefined as unknown }));
vi.mock("next/headers", () => ({ cookies: async () => cookiesFalsos.atual }));
vi.mock("next/navigation", async () => {
  const { redirectFalso } = await import("../../../testes/next-falso");
  return { redirect: redirectFalso };
});

import { entrar, sair } from "./acoes";

function formulario(campos: Record<string, string>): FormData {
  const dados = new FormData();
  for (const [nome, valor] of Object.entries(campos)) {
    dados.set(nome, valor);
  }
  return dados;
}

async function capturarRedirecionamento(acao: Promise<unknown>): Promise<string> {
  try {
    await acao;
  } catch (erro) {
    if (erro instanceof RedirecionamentoFalso) {
      return erro.destino;
    }
    throw erro;
  }
  throw new Error("esperava um redirecionamento");
}

describe("Server Action entrar", () => {
  let cookies: CookiesFalsos;
  let fetchFalso: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    cookies = new CookiesFalsos();
    cookiesFalsos.atual = cookies;
    fetchFalso = vi.fn();
    vi.stubGlobal("fetch", fetchFalso);
    vi.stubEnv("IDENTIDADE_URL", "http://identidade.teste");
    vi.stubEnv("COOKIE_SEGURO", "true");
  });

  it("com credenciais válidas grava o token em cookie httpOnly/SameSite=Lax com maxAge da expiração e volta ao retorno", async () => {
    const expiraEm = new Date(Date.now() + 30 * 60_000).toISOString();
    fetchFalso.mockResolvedValue(Response.json({ tokenAcesso: "jwt-do-operador", tipo: "Bearer", expiraEm }));

    const destino = await capturarRedirecionamento(
      entrar(undefined, formulario({ login: "operador", senha: "Operador@123", retorno: "/contas/CC-0002?cursor=x" })),
    );

    expect(destino).toBe("/contas/CC-0002?cursor=x");
    const [url, init] = fetchFalso.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("http://identidade.teste/api/v1/autenticacao/login");
    expect(init.method).toBe("POST");
    expect(JSON.parse(String(init.body))).toEqual({ login: "operador", senha: "Operador@123" });

    const cookie = cookies.valores.get("sessao");
    expect(cookie?.valor).toBe("jwt-do-operador");
    expect(cookie?.opcoes).toMatchObject({ httpOnly: true, sameSite: "lax", secure: true, path: "/" });
    expect(cookie?.opcoes.maxAge).toBeGreaterThanOrEqual(29 * 60);
    expect(cookie?.opcoes.maxAge).toBeLessThanOrEqual(30 * 60);
  });

  it("sem cookie seguro configurado (http local) grava o cookie sem Secure", async () => {
    vi.stubEnv("COOKIE_SEGURO", "false");
    fetchFalso.mockResolvedValue(
      Response.json({ tokenAcesso: "t", tipo: "Bearer", expiraEm: new Date(Date.now() + 60_000).toISOString() }),
    );

    await capturarRedirecionamento(entrar(undefined, formulario({ login: "operador", senha: "x" })));

    expect(cookies.valores.get("sessao")?.opcoes.secure).toBe(false);
  });

  it.each([
    ["externo absoluto", "https://malicioso.example/roubo"],
    ["protocolo relativo", "//malicioso.example"],
    ["barra invertida", "/\\malicioso.example"],
    ["ausente", ""],
  ])("retorno %s não é seguido: vai para /contas (evita open redirect)", async (_caso, retorno) => {
    fetchFalso.mockResolvedValue(
      Response.json({ tokenAcesso: "t", tipo: "Bearer", expiraEm: new Date(Date.now() + 60_000).toISOString() }),
    );

    const destino = await capturarRedirecionamento(entrar(undefined, formulario({ login: "operador", senha: "x", retorno })));

    expect(destino).toBe("/contas");
  });

  it("com credenciais inválidas devolve a mensagem genérica e não grava cookie", async () => {
    fetchFalso.mockResolvedValue(
      Response.json({ title: "Não autorizado", detail: "Login ou senha inválidos." }, { status: 401 }),
    );

    const estado = await entrar(undefined, formulario({ login: "operador", senha: "errada" }));

    expect(estado).toEqual({ erro: "Usuário ou senha inválidos", login: "operador" });
    expect(cookies.valores.has("sessao")).toBe(false);
  });

  it("com login ou senha vazios devolve a mesma mensagem genérica sem chamar a identidade", async () => {
    const estado = await entrar(undefined, formulario({ login: "", senha: "" }));

    expect(estado).toEqual({ erro: "Usuário ou senha inválidos", login: "" });
    expect(fetchFalso).not.toHaveBeenCalled();
  });

  it("com a identidade fora do ar devolve aviso de indisponibilidade", async () => {
    fetchFalso.mockRejectedValue(new TypeError("fetch failed"));

    const estado = await entrar(undefined, formulario({ login: "operador", senha: "x" }));

    expect(estado).toEqual({ erro: "Não foi possível entrar agora. Tente novamente em instantes.", login: "operador" });
  });
});

describe("Server Action sair", () => {
  it("remove o cookie de sessão e volta para /login", async () => {
    const cookies = new CookiesFalsos();
    cookies.set("sessao", "jwt-qualquer");
    cookiesFalsos.atual = cookies;

    const destino = await capturarRedirecionamento(sair());

    expect(destino).toBe("/login");
    expect(cookies.valores.has("sessao")).toBe(false);
  });
});
