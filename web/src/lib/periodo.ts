// Período e instante das consultas do consolidado, no fuso de Brasília (o mesmo do consolidado diário).

const FUSO = "America/Sao_Paulo";
const MS_POR_DIA = 86_400_000;

/** Mesmo limite da API de consolidado (ConsultarConsolidadoDiario.DiasMaximos). */
export const DIAS_MAXIMOS_PERIODO = 366;
export const DIAS_PERIODO_PADRAO = 30;

const partesDataHora = new Intl.DateTimeFormat("en-CA", {
  timeZone: FUSO,
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
  hour: "2-digit",
  minute: "2-digit",
  second: "2-digit",
  hourCycle: "h23",
});

function partesEmBrasilia(instante: Date): Record<string, number> {
  return Object.fromEntries(
    partesDataHora
      .formatToParts(instante)
      .filter((parte) => parte.type !== "literal")
      .map((parte) => [parte.type, Number(parte.value)]),
  );
}

/** Dia `yyyy-MM-dd` → número de dias desde a época (UTC), ou `null` se a data não existe. */
function diaParaNumero(dia: string): number | null {
  const partes = /^(\d{4})-(\d{2})-(\d{2})$/.exec(dia);
  if (!partes) {
    return null;
  }
  const [ano, mes, diaDoMes] = [Number(partes[1]), Number(partes[2]), Number(partes[3])];
  const data = new Date(Date.UTC(ano, mes - 1, diaDoMes));
  if (data.getUTCFullYear() !== ano || data.getUTCMonth() !== mes - 1 || data.getUTCDate() !== diaDoMes) {
    return null;
  }
  return data.getTime() / MS_POR_DIA;
}

function numeroParaDia(numero: number): string {
  return new Date(numero * MS_POR_DIA).toISOString().slice(0, 10);
}

export function hojeEmBrasilia(agora: Date = new Date()): string {
  const p = partesEmBrasilia(agora);
  return numeroParaDia(Date.UTC(p.year, p.month - 1, p.day) / MS_POR_DIA);
}

/** Últimos 30 dias, hoje incluído. */
export function periodoPadrao(agora: Date = new Date()): { de: string; ate: string } {
  const ate = hojeEmBrasilia(agora);
  return { de: numeroParaDia((diaParaNumero(ate) as number) - (DIAS_PERIODO_PADRAO - 1)), ate };
}

export type ResultadoPeriodo =
  | { valido: true; de: string; ate: string }
  | { valido: false; campo: "de" | "ate"; mensagem: string };

export function validarPeriodo(de: string, ate: string): ResultadoPeriodo {
  const inicio = diaParaNumero(de);
  if (inicio === null) {
    return { valido: false, campo: "de", mensagem: "Informe uma data inicial válida." };
  }
  const fim = diaParaNumero(ate);
  if (fim === null) {
    return { valido: false, campo: "ate", mensagem: "Informe uma data final válida." };
  }
  if (fim < inicio) {
    return { valido: false, campo: "ate", mensagem: "A data final deve ser igual ou posterior à inicial." };
  }
  if (fim - inicio + 1 > DIAS_MAXIMOS_PERIODO) {
    return { valido: false, campo: "ate", mensagem: `O período pode ter no máximo ${DIAS_MAXIMOS_PERIODO} dias.` };
  }
  return { valido: true, de, ate };
}

/**
 * Valor de um `<input type="datetime-local">` (`yyyy-MM-ddTHH:mm`), lido como horário de Brasília, em ISO-8601
 * UTC; `null` se fora do formato ou inexistente.
 */
export function instanteLocalParaUtc(valor: string): string | null {
  const partes = /^(\d{4}-\d{2}-\d{2})T(\d{2}):(\d{2})(?::(\d{2}))?$/.exec(valor);
  const dia = partes ? diaParaNumero(partes[1]) : null;
  if (!partes || dia === null) {
    return null;
  }
  const [hora, minuto, segundo] = [Number(partes[2]), Number(partes[3]), Number(partes[4] ?? 0)];
  if (hora > 23 || minuto > 59 || segundo > 59) {
    return null;
  }
  // Trata o horário como se fosse UTC, mede o deslocamento do fuso nesse instante e corrige.
  const comoUtc = dia * MS_POR_DIA + ((hora * 60 + minuto) * 60 + segundo) * 1000;
  const p = partesEmBrasilia(new Date(comoUtc));
  const deslocamento = Date.UTC(p.year, p.month - 1, p.day, p.hour, p.minute, p.second) - comoUtc;
  return new Date(comoUtc - deslocamento).toISOString();
}
