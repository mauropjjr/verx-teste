// Dublês das APIs de request do Next (cookies() e redirect()) para testar Server Actions e páginas fora do
// runtime do Next. Usados via vi.mock("next/headers") / vi.mock("next/navigation") em cada teste.

export type OpcoesCookie = {
  httpOnly?: boolean;
  sameSite?: string;
  secure?: boolean;
  path?: string;
  maxAge?: number;
};

export class CookiesFalsos {
  readonly valores = new Map<string, { valor: string; opcoes: OpcoesCookie }>();

  get(nome: string) {
    const item = this.valores.get(nome);
    return item ? { name: nome, value: item.valor } : undefined;
  }

  set(nome: string, valor: string, opcoes: OpcoesCookie = {}) {
    this.valores.set(nome, { valor, opcoes });
  }

  delete(nome: string) {
    this.valores.delete(nome);
  }
}

/** Erro lançado pelo redirect() falso — como o real, interrompe a execução. */
export class RedirecionamentoFalso extends Error {
  constructor(readonly destino: string) {
    super(`NEXT_REDIRECT:${destino}`);
  }
}

export function redirectFalso(destino: string): never {
  throw new RedirecionamentoFalso(destino);
}
