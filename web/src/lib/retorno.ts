/** Destino padrão depois do login. */
export const DESTINO_PADRAO = "/contas";

/**
 * Só aceita caminhos relativos ao próprio portal (`/algo`), evitando open redirect para outro domínio
 * (`https://…`, `//host`, `/\host`).
 */
export function caminhoRetornoSeguro(valor: string | null | undefined): string {
  if (!valor || !valor.startsWith("/") || valor.startsWith("//") || valor.startsWith("/\\")) {
    return DESTINO_PADRAO;
  }
  return valor;
}
