import { NextResponse, type NextRequest } from "next/server";
import { NOME_COOKIE_SESSAO, tokenValido } from "@/lib/token";

/**
 * Sem sessão (cookie ausente, ilegível ou expirado) toda página do portal redireciona para
 * `/login?retorno=<caminho pedido>` (spec web-portal "Acesso sem sessão" / "Sessão expirada").
 */
export function proxy(requisicao: NextRequest) {
  const token = requisicao.cookies.get(NOME_COOKIE_SESSAO)?.value;
  if (tokenValido(token)) {
    return NextResponse.next();
  }

  const destino = new URL("/login", requisicao.url);
  destino.searchParams.set("retorno", requisicao.nextUrl.pathname + requisicao.nextUrl.search);
  const resposta = NextResponse.redirect(destino);
  if (token) {
    resposta.cookies.delete(NOME_COOKIE_SESSAO);
  }
  return resposta;
}

export const config = {
  // Tudo, exceto a própria tela de login e os arquivos estáticos do Next.
  matcher: ["/((?!login|_next/static|_next/image|favicon.ico).*)"],
};
