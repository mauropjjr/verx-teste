// Gera JWTs de teste (assinatura falsa): o portal só lê o payload; quem valida a assinatura são as APIs.
function base64Url(texto: string): string {
  return Buffer.from(texto, "utf8").toString("base64url");
}

export function criarTokenTeste(payload: Record<string, unknown>): string {
  return `${base64Url(JSON.stringify({ alg: "RS256", typ: "JWT" }))}.${base64Url(JSON.stringify(payload))}.assinatura`;
}

export function segundosDesdeEpoca(data: Date): number {
  return Math.floor(data.getTime() / 1000);
}
