/** Papéis que podem registrar e estornar lançamentos (spec access-control): `operador` e `admin`. */
const PAPEIS_ESCRITA = ["operador", "admin"];

export function podeOperar(papeis: readonly string[]): boolean {
  return papeis.some((papel) => PAPEIS_ESCRITA.includes(papel));
}
