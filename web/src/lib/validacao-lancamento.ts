import type { TipoLancamento } from "./tipos";

/** Mesmo limite de descrição da API de lançamentos (Lancamento.TamanhoMaximoDescricao). */
export const TAMANHO_MAXIMO_DESCRICAO = 140;

export type CampoLancamento = "tipo" | "valor" | "descricao";
export type CamposLancamento = Record<CampoLancamento, string>;
export type ErrosLancamento = Partial<Record<CampoLancamento, string>>;

export type LancamentoValidado = { tipo: TipoLancamento; valor: string; descricao: string };

const MENSAGEM_VALOR_INVALIDO = "Informe um valor válido, com até 2 casas decimais (ex.: 1.234,56).";

// Valor em pt-BR: vírgula decimal, ponto de milhar opcional (1.234,56 ou 1234,56). Até 16 dígitos inteiros,
// como na API (máximo 9999999999999999,99).
const VALOR_SEM_MILHAR = /^\d{1,16}(,\d{1,2})?$/;
const VALOR_COM_MILHAR = /^\d{1,3}(\.\d{3}){1,5}(,\d{1,2})?$/;

/** Converte o valor digitado em pt-BR para decimal normalizado (`1234.56`); `null` se fora do formato. */
export function normalizarValor(texto: string): string | null {
  const valor = texto.trim();
  if (!VALOR_SEM_MILHAR.test(valor) && !VALOR_COM_MILHAR.test(valor)) {
    return null;
  }
  const [inteiro, centavos] = valor.replace(/\./g, "").split(",");
  const inteiroLimpo = inteiro.replace(/^0+(?=\d)/, "");
  if (inteiroLimpo.length > 16) {
    return null;
  }
  return centavos === undefined ? inteiroLimpo : `${inteiroLimpo}.${centavos}`;
}

/** Validação por campo antes de chamar a API (as mesmas regras do validador da API de lançamentos). */
export function validarLancamento(
  campos: CamposLancamento,
): { valido: true; lancamento: LancamentoValidado } | { valido: false; erros: ErrosLancamento } {
  const erros: ErrosLancamento = {};

  if (campos.tipo !== "CREDITO" && campos.tipo !== "DEBITO") {
    erros.tipo = "Escolha crédito ou débito.";
  }

  const valor = normalizarValor(campos.valor);
  if (campos.valor.trim() === "") {
    erros.valor = "Informe o valor.";
  } else if (valor === null) {
    erros.valor = MENSAGEM_VALOR_INVALIDO;
  } else if (/^0+(\.0+)?$/.test(valor)) {
    erros.valor = "O valor deve ser maior que zero.";
  }

  const descricao = campos.descricao.trim();
  if (descricao === "") {
    erros.descricao = "Informe a descrição.";
  } else if (descricao.length > TAMANHO_MAXIMO_DESCRICAO) {
    erros.descricao = `A descrição tem no máximo ${TAMANHO_MAXIMO_DESCRICAO} caracteres.`;
  }

  if (Object.keys(erros).length > 0 || valor === null) {
    return { valido: false, erros };
  }
  return { valido: true, lancamento: { tipo: campos.tipo as TipoLancamento, valor, descricao } };
}
