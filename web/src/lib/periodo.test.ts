import { describe, expect, it } from "vitest";
import { instanteLocalParaUtc, periodoPadrao, validarPeriodo } from "./periodo";

describe("período do consolidado", () => {
  it("padrão são os últimos 30 dias até hoje no fuso de Brasília", () => {
    // 01:30 UTC de 14/10 ainda é 13/10 em Brasília (UTC−3).
    expect(periodoPadrao(new Date("2026-10-14T01:30:00Z"))).toEqual({ de: "2026-09-14", ate: "2026-10-13" });
    expect(periodoPadrao(new Date("2026-03-01T12:00:00Z"))).toEqual({ de: "2026-01-31", ate: "2026-03-01" });
  });

  it("aceita intervalo de até 366 dias com início anterior ou igual ao fim", () => {
    expect(validarPeriodo("2025-10-13", "2026-10-13")).toEqual({ valido: true, de: "2025-10-13", ate: "2026-10-13" });
    expect(validarPeriodo("2026-10-13", "2026-10-13")).toMatchObject({ valido: true });
  });

  it.each([
    ["data inicial inválida", "2026-13-01", "2026-10-13", "de", "Informe uma data inicial válida."],
    ["data final ausente", "2026-10-01", "", "ate", "Informe uma data final válida."],
    ["início depois do fim", "2026-10-14", "2026-10-13", "ate", "A data final deve ser igual ou posterior à inicial."],
    ["mais de 366 dias", "2025-10-12", "2026-10-13", "ate", "O período pode ter no máximo 366 dias."],
  ])("recusa %s", (_caso, de, ate, campo, mensagem) => {
    expect(validarPeriodo(de, ate)).toEqual({ valido: false, campo, mensagem });
  });

  it("converte o instante digitado (horário de Brasília) para ISO UTC", () => {
    expect(instanteLocalParaUtc("2026-10-01T12:00")).toBe("2026-10-01T15:00:00.000Z");
    expect(instanteLocalParaUtc("2026-12-31T22:30")).toBe("2027-01-01T01:30:00.000Z");
    expect(instanteLocalParaUtc("31/12/2026")).toBeNull();
    expect(instanteLocalParaUtc("2026-02-30T10:00")).toBeNull();
  });
});
