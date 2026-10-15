// Teste de carga do consolidado (spec balance-consolidation "Desempenho do consolidado", design D9).
//
// Cenário principal "consultas": constant-arrival-rate de 50 req/s por 2 min mesclando consolidado diário
// (últimos 30/90/365 dias e meses fechados do último ano) e saldo (atual e em instantes passados) nas contas
// CC-0001..CC-0005, com o token obtido no login do setup(). Meta: ≤ 5% de requisições com erro ou perdidas.
//
// Cenário opcional "lancamentos" (LANCAMENTOS_PARALELOS=1): registra lançamentos em paralelo como operador, para
// o worker projetar e invalidar o cache (versão por conta) durante a carga.
//
// Rodar: docker compose --profile carga run --rm k6   (variáveis abaixo; veja o README)
import http from 'k6/http';
import { check, fail } from 'k6';

// --- Configuração por variável de ambiente ---------------------------------------------------------------
const urlIdentidade = __ENV.URL_IDENTIDADE || 'http://identidade-api:8080';
const urlConsolidado = __ENV.URL_CONSOLIDADO || 'http://consolidado-api:8080';
const urlLancamentos = __ENV.URL_LANCAMENTOS || 'http://lancamentos-api:8080';
const usuarioConsulta = __ENV.USUARIO || 'consulta';
const senhaConsulta = __ENV.SENHA || 'Consulta@123';
const usuarioOperador = __ENV.USUARIO_OPERADOR || 'operador';
const senhaOperador = __ENV.SENHA_OPERADOR || 'Operador@123';
const taxaConsultas = inteiro(__ENV.TAXA, 50);
const duracao = __ENV.DURACAO || '2m';
const lancamentosParalelos = __ENV.LANCAMENTOS_PARALELOS === '1';
const taxaLancamentos = inteiro(__ENV.TAXA_LANCAMENTOS, 10);
// p95 alvo em ms. Padrão 300 (design D9); só o cenário degradado documentado (Redis parado) pode usar outro valor.
const limiteP95Ms = inteiro(__ENV.LIMITE_P95_MS, 300);
const nomeCenario = __ENV.NOME_CENARIO || 'consolidado';
const diretorioResultados = __ENV.DIRETORIO_RESULTADOS || '/resultados';

const contas = ['CC-0001', 'CC-0002', 'CC-0003', 'CC-0004', 'CC-0005'];
const segundosDuracao = duracaoEmSegundos(duracao);
const consultasEsperadas = taxaConsultas * segundosDuracao;
const lancamentosEsperados = lancamentosParalelos ? taxaLancamentos * segundosDuracao : 0;
// Perda tolerada: 5% de tudo que deveria ter sido enviado (erro + iteração descartada somam na meta do spec).
const perdaMaximaIteracoes = Math.floor(0.05 * (consultasEsperadas + lancamentosEsperados));

const cenarios = {
  consultas: {
    executor: 'constant-arrival-rate',
    rate: taxaConsultas,
    timeUnit: '1s',
    duration: duracao,
    // Com latência de dezenas de ms, ~10 VUs dão conta de 50 req/s; a folga absorve picos sem descartar.
    preAllocatedVUs: inteiro(__ENV.VUS_PRE_ALOCADOS, 50),
    maxVUs: inteiro(__ENV.VUS_MAXIMOS, 200),
    exec: 'consultarConsolidado',
  },
};

if (lancamentosParalelos) {
  cenarios.lancamentos = {
    executor: 'constant-arrival-rate',
    rate: taxaLancamentos,
    timeUnit: '1s',
    duration: duracao,
    preAllocatedVUs: 10,
    maxVUs: 50,
    exec: 'registrarLancamento',
  };
}

const limiares = {
  // Meta do spec sobre as consultas de consolidado e saldo.
  'http_req_failed{scenario:consultas}': ['rate<0.05'],
  'http_req_duration{scenario:consultas}': [`p(95)<${limiteP95Ms}`],
  // Gerais (incluem o login do setup e, se habilitados, os lançamentos paralelos).
  http_req_failed: ['rate<0.05'],
  http_req_duration: [`p(95)<${limiteP95Ms}`],
  // Perda: iterações que o k6 não conseguiu iniciar no ritmo pedido (sem VU livre = sistema lento demais).
  dropped_iterations: [`count<=${perdaMaximaIteracoes}`],
  'checks{scenario:consultas}': ['rate>0.95'],
};

if (lancamentosParalelos) {
  limiares['http_req_failed{scenario:lancamentos}'] = ['rate<0.05'];
  // Sem meta de latência para a escrita (o spec mede o consolidado); o limiar trivial só expõe a submétrica no sumário.
  limiares['http_req_duration{scenario:lancamentos}'] = ['max>=0'];
}

// Latência por tipo de consulta, só para análise (limiar trivial expõe a submétrica no sumário; não decide o teste).
const tiposConsulta = ['periodo-30d', 'periodo-90d', 'periodo-365d', 'periodo-mes-passado', 'saldo-atual', 'saldo-em'];
for (const tipo of tiposConsulta) {
  limiares[`http_req_duration{consulta:${tipo}}`] = ['max>=0'];
}

export const options = {
  scenarios: cenarios,
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
  thresholds: limiares,
};

// --- Ciclo de vida ----------------------------------------------------------------------------------------
export function setup() {
  const dados = { tokenConsulta: logar(usuarioConsulta, senhaConsulta) };
  if (lancamentosParalelos) {
    dados.tokenOperador = logar(usuarioOperador, senhaOperador);
  }
  return dados;
}

export function consultarConsolidado(dados) {
  const conta = sortear(contas);
  const parametros = {
    headers: { Authorization: `Bearer ${dados.tokenConsulta}` },
    tags: {},
  };
  const sorteio = Math.random();
  let url;
  if (sorteio < 0.2) {
    url = urlPeriodo(conta, diasAtras(29), hoje(), parametros, 'periodo-30d');
  } else if (sorteio < 0.35) {
    url = urlPeriodo(conta, diasAtras(89), hoje(), parametros, 'periodo-90d');
  } else if (sorteio < 0.45) {
    url = urlPeriodo(conta, diasAtras(364), hoje(), parametros, 'periodo-365d');
  } else if (sorteio < 0.65) {
    // Mês fechado do último ano (só dias passados ⇒ TTL longo no cache).
    const mesesAtras = 1 + Math.floor(Math.random() * 11);
    const inicio = new Date(Date.UTC(agora().getUTCFullYear(), agora().getUTCMonth() - mesesAtras, 1));
    const fim = new Date(Date.UTC(inicio.getUTCFullYear(), inicio.getUTCMonth() + 1, 0));
    url = urlPeriodo(conta, inicio, fim, parametros, 'periodo-mes-passado');
  } else if (sorteio < 0.8) {
    parametros.tags.consulta = 'saldo-atual';
    url = `${urlConsolidado}/api/v1/contas/${conta}/saldo`;
  } else {
    // Instante passado: um dia do último ano, ao fim do expediente (conjunto finito ⇒ reaproveita cache).
    const dia = diasAtras(1 + Math.floor(Math.random() * 364));
    parametros.tags.consulta = 'saldo-em';
    url = `${urlConsolidado}/api/v1/contas/${conta}/saldo?em=${formatarData(dia)}T18:00:00Z`;
  }
  parametros.tags.name = parametros.tags.consulta;

  const resposta = http.get(url, parametros);
  check(resposta, {
    'consulta responde 200': (r) => r.status === 200,
  });
}

export function registrarLancamento(dados) {
  const conta = sortear(contas);
  const corpo = JSON.stringify({
    tipo: Math.random() < 0.5 ? 'CREDITO' : 'DEBITO',
    valor: Number((1 + Math.random() * 99).toFixed(2)),
    descricao: 'Carga k6',
  });
  const resposta = http.post(`${urlLancamentos}/api/v1/contas/${conta}/lancamentos`, corpo, {
    headers: {
      Authorization: `Bearer ${dados.tokenOperador}`,
      'Content-Type': 'application/json',
      'Idempotency-Key': `k6-${crypto.randomUUID()}`,
    },
    tags: { name: 'registrar-lancamento' },
  });
  check(resposta, {
    'lançamento responde 201': (r) => r.status === 201,
  });
}

export function handleSummary(sumario) {
  const texto = relatorioTexto(sumario);
  return {
    stdout: texto,
    [`${diretorioResultados}/${nomeCenario}.json`]: JSON.stringify(sumario, null, 2),
    [`${diretorioResultados}/${nomeCenario}.txt`]: texto,
  };
}

// --- Auxiliares -------------------------------------------------------------------------------------------
function logar(usuario, senha) {
  const resposta = http.post(
    `${urlIdentidade}/api/v1/autenticacao/login`,
    JSON.stringify({ login: usuario, senha }),
    { headers: { 'Content-Type': 'application/json' }, tags: { name: 'login' } },
  );
  if (resposta.status !== 200) {
    fail(`Login de "${usuario}" falhou: HTTP ${resposta.status}`);
  }
  return resposta.json('tokenAcesso');
}

function urlPeriodo(conta, de, ate, parametros, consulta) {
  parametros.tags.consulta = consulta;
  return `${urlConsolidado}/api/v1/contas/${conta}/consolidado-diario?de=${formatarData(de)}&ate=${formatarData(ate)}`;
}

function agora() {
  return new Date();
}

function hoje() {
  return diasAtras(0);
}

function diasAtras(dias) {
  const data = agora();
  data.setUTCDate(data.getUTCDate() - dias);
  return data;
}

function formatarData(data) {
  return data.toISOString().slice(0, 10);
}

function sortear(lista) {
  return lista[Math.floor(Math.random() * lista.length)];
}

function inteiro(texto, padrao) {
  const valor = parseInt(texto, 10);
  return Number.isFinite(valor) && valor > 0 ? valor : padrao;
}

function duracaoEmSegundos(texto) {
  const partes = /^(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)s)?$/.exec(texto);
  if (!partes || texto === '') {
    throw new Error(`DURACAO inválida: "${texto}" (use, por exemplo, 2m, 30s ou 1m30s)`);
  }
  return (Number(partes[1] || 0) * 3600) + (Number(partes[2] || 0) * 60) + Number(partes[3] || 0);
}

function metrica(sumario, nome) {
  return (sumario.metrics[nome] && sumario.metrics[nome].values) || {};
}

function ms(valor) {
  return valor === undefined ? 'n/d' : `${valor.toFixed(1)} ms`;
}

function pct(valor) {
  return valor === undefined ? 'n/d' : `${(valor * 100).toFixed(2)}%`;
}

function relatorioTexto(sumario) {
  const duracaoConsultas = metrica(sumario, 'http_req_duration{scenario:consultas}');
  const falhasConsultas = metrica(sumario, 'http_req_failed{scenario:consultas}');
  const requisicoes = metrica(sumario, 'http_reqs');
  const descartadas = metrica(sumario, 'dropped_iterations').count || 0;
  const segundosReais = sumario.state.testRunDurationMs / 1000;
  const consultasFeitas = (falhasConsultas.passes || 0) + (falhasConsultas.fails || 0);
  const consultasComErro = falhasConsultas.passes || 0; // http_req_failed: "passes" = requisições que falharam
  const perda = consultasEsperadas > 0 ? (consultasComErro + descartadas) / (consultasEsperadas) : 0;

  const linhas = [
    `Teste de carga do consolidado — cenário "${nomeCenario}"`,
    `Configuração: ${taxaConsultas} req/s por ${duracao} (consultas esperadas: ${consultasEsperadas})` +
      (lancamentosParalelos ? ` + lançamentos em paralelo a ${taxaLancamentos} req/s` : '') +
      `; limite p95 ${limiteP95Ms} ms`,
    `Duração real: ${segundosReais.toFixed(1)} s`,
    '',
    'Consultas (consolidado-diario + saldo):',
    `  requisições: ${consultasFeitas} (${(consultasFeitas / segundosReais).toFixed(2)} req/s)`,
    `  taxa de erro: ${pct(falhasConsultas.rate)} (${consultasComErro} com erro)`,
    `  latência: média ${ms(duracaoConsultas.avg)} · mediana ${ms(duracaoConsultas.med)} · p90 ${ms(duracaoConsultas['p(90)'])} · ` +
      `p95 ${ms(duracaoConsultas['p(95)'])} · p99 ${ms(duracaoConsultas['p(99)'])} · máx ${ms(duracaoConsultas.max)}`,
    `  iterações descartadas (dropped_iterations, todos os cenários): ${descartadas}`,
    `  perda total (erros + descartadas) / esperadas: ${pct(perda)}`,
  ];

  if (lancamentosParalelos) {
    const duracaoLancamentos = metrica(sumario, 'http_req_duration{scenario:lancamentos}');
    const falhasLancamentos = metrica(sumario, 'http_req_failed{scenario:lancamentos}');
    const feitos = (falhasLancamentos.passes || 0) + (falhasLancamentos.fails || 0);
    linhas.push(
      '',
      'Lançamentos em paralelo (POST lancamentos):',
      `  requisições: ${feitos} (${(feitos / segundosReais).toFixed(2)} req/s)`,
      `  taxa de erro: ${pct(falhasLancamentos.rate)}`,
      `  latência: p95 ${ms(duracaoLancamentos['p(95)'])} · p99 ${ms(duracaoLancamentos['p(99)'])}`,
    );
  }

  linhas.push('', 'Latência por tipo de consulta (análise):');
  for (const tipo of tiposConsulta) {
    const porTipo = metrica(sumario, `http_req_duration{consulta:${tipo}}`);
    linhas.push(`  ${tipo.padEnd(20)} mediana ${ms(porTipo.med)} · p95 ${ms(porTipo['p(95)'])} · p99 ${ms(porTipo['p(99)'])}`);
  }

  linhas.push('', `Total de requisições HTTP (inclui login): ${requisicoes.count || 0}`, '', 'Thresholds:');
  for (const [nome, dadosMetrica] of Object.entries(sumario.metrics)) {
    if (nome.startsWith('http_req_duration{consulta:')) {
      continue;
    }
    for (const [regra, resultado] of Object.entries(dadosMetrica.thresholds || {})) {
      linhas.push(`  ${resultado.ok ? 'OK   ' : 'FALHA'} ${nome}: ${regra}`);
    }
  }
  return `${linhas.join('\n')}\n`;
}
