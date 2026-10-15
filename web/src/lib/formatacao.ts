// Formatação pt-BR: moeda em BRL e datas no fuso de Brasília (o mesmo do consolidado diário).

const FUSO = "America/Sao_Paulo";

const moeda = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" });
const dataHora = new Intl.DateTimeFormat("pt-BR", { dateStyle: "short", timeStyle: "short", timeZone: FUSO });

export function formatarMoeda(valor: number): string {
  return moeda.format(valor);
}

export function formatarDataHora(instante: string): string {
  return dataHora.format(new Date(instante));
}

const SITUACOES: Record<string, string> = { ATIVA: "Ativa", INATIVA: "Inativa" };

export function formatarSituacao(situacao: string): string {
  return SITUACOES[situacao] ?? situacao;
}

export function formatarTipo(tipo: string): string {
  return tipo === "CREDITO" ? "Crédito" : tipo === "DEBITO" ? "Débito" : tipo;
}

/** Dia `yyyy-MM-dd` (data civil, sem fuso) como `dd/MM/yyyy`. */
export function formatarDia(dia: string): string {
  const [ano, mes, diaDoMes] = dia.split("-");
  return `${diaDoMes}/${mes}/${ano}`;
}
