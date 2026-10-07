import type {
  AlertaDoAlmoxarifado, AlmoxarifadoDoCockpit, ExcecaoDoCockpit, FaixaDaMeta, Gargalo, StatusDaDescarga,
  TipoDeAlerta, TipoDeAlertaDoAlmoxarifado,
} from '@/api/torre';

/**
 * A lógica do cockpit, fora do componente: cor, ordem e texto são o que decide se a
 * parede diz a verdade, e é o que precisa de teste. O JSX fica só com o desenho.
 */

/**
 * Quanto tempo cada parada do rodízio fica na tela. Era 2 minutos por unidade; com a tela do
 * material no ciclo (decisão da empresa, 2026-10) passou a 1 minuto por parada, para a volta
 * inteira não dobrar de tamanho.
 */
export const ROTACAO_MS = 60_000;

/** Uma parada do rodízio: qual tela, de qual unidade (nulo é a visão geral). */
export interface Parada { tela: 'compras' | 'material'; unidade: string | null }

/**
 * O rodízio inteiro, num relógio só. A tela do material entra como mais um passo do mesmo
 * ciclo — Compras geral → Material geral → Compras PB → Material PB… — em vez de um segundo
 * cronômetro, que cortaria a unidade pela metade. Ela só entra quando há material para contar:
 * a operação que não pede ao almoxarifado não ganha uma tela em branco a cada minuto.
 *
 * <p>
 * A geral abre o ciclo pelo mesmo motivo de sempre: quem passa e olha três segundos precisa ver
 * a empresa inteira. Com uma unidade só, a compra não gira — mas com material a parede ainda
 * alterna as duas telas da geral, porque são leituras diferentes.
 * </p>
 */
export function cicloDeParadas(unidades: string[], temMaterial: boolean): Parada[] {
  const recortes = cicloDeUnidades(unidades);
  return recortes.flatMap((unidade): Parada[] => temMaterial
    ? [{ tela: 'compras', unidade }, { tela: 'material', unidade }]
    : [{ tela: 'compras', unidade }]);
}

/**
 * Por onde a parede começa. `?tela=material` abre direto na tela do material — é como o E2E a
 * confere sem esperar o minuto da rotação, e como quem liga a TV pode conferir a segunda tela.
 * Sem o parâmetro, pela compra geral, como sempre.
 */
export function paradaInicial(busca: string): Parada {
  const tela = new URLSearchParams(busca).get('tela');
  return { tela: tela === 'material' ? 'material' : 'compras', unidade: null };
}

/** A próxima parada. Parada que não existe mais (unidade que saiu do cadastro) volta ao início. */
export function proximaParada(ciclo: Parada[], atual: Parada): Parada {
  if (ciclo.length === 0) return { tela: 'compras', unidade: null };
  const i = ciclo.findIndex((p) => p.tela === atual.tela && p.unidade === atual.unidade);
  return ciclo[(i + 1) % ciclo.length] ?? ciclo[0];
}

/**
 * O ciclo que a TV percorre: a visão geral primeiro, depois cada unidade. `null` é a geral.
 *
 * <p>
 * A geral abre o ciclo de propósito: quem passa pela sala e olha por três segundos precisa
 * ver a empresa inteira, não a unidade que calhou de estar na vez. Com uma unidade só, não
 * há o que girar — alternar entre "geral" e a própria unidade mostraria o mesmo número duas
 * vezes e faria a tela parecer travada.
 * </p>
 */
export function cicloDeUnidades(unidades: string[]): (string | null)[] {
  return unidades.length <= 1 ? [null] : [null, ...unidades];
}

/** A próxima parada do ciclo. Unidade que saiu do cadastro volta para a geral. */
export function proximaUnidade(ciclo: (string | null)[], atual: string | null): string | null {
  if (ciclo.length === 0) return null;
  const i = ciclo.findIndex((u) => u === atual);
  return ciclo[(i + 1) % ciclo.length] ?? null;
}

/** Acima disto o nó da esteira acende. Os mesmos limites que o servidor usa. */
export const GARGALO_ATENCAO_H = 48;
export const GARGALO_CRITICO_H = 72;

export const CLASSE_DO_GARGALO: Record<Gargalo, string> = {
  NORMAL: 'border-slate-800 text-slate-300',
  ATENCAO: 'border-amber-500/60 text-amber-300',
  CRITICO: 'border-rose-500/70 text-rose-300',
};

export const ROTULO_DO_ALERTA: Record<TipoDeAlerta, string> = {
  ATRASO_CRITICO: 'Atraso',
  COTACAO_VENCENDO: 'Cotação',
  PROPOSTA_UNICA: 'Proposta única',
  OC_PENDENTE: 'Sem O.C.',
};

/**
 * A faixa de cor do alerta, pela gravidade. Vermelho é data estourada; âmbar é cotação
 * em risco; azul é compra aprovada esperando a O.C. — grave, mas ainda dentro do fluxo.
 */
export const CLASSE_DO_ALERTA: Record<TipoDeAlerta, string> = {
  ATRASO_CRITICO: 'border-l-rose-500 bg-rose-950/40 text-rose-200',
  COTACAO_VENCENDO: 'border-l-amber-400 bg-amber-950/30 text-amber-200',
  PROPOSTA_UNICA: 'border-l-amber-400 bg-amber-950/30 text-amber-200',
  OC_PENDENTE: 'border-l-sky-400 bg-sky-950/30 text-sky-200',
};

export const CLASSE_DA_DESCARGA: Record<StatusDaDescarga, string> = {
  NO_PRAZO: 'text-emerald-300',
  ATRASADO: 'text-rose-300',
  DESCARREGANDO: 'text-sky-300',
};

export const ROTULO_DA_DESCARGA: Record<StatusDaDescarga, string> = {
  NO_PRAZO: 'No prazo',
  ATRASADO: 'Atrasado',
  DESCARREGANDO: 'Descarregando',
};

/**
 * A ordem do radar. O servidor já entrega ordenado, mas a tela reordena porque é ela que
 * recebe a atualização de 30 em 30 segundos: o critério de aceite 3 diz que o item urgente
 * que entra assume o topo, e depender da ordem de chegada do JSON deixaria isso ao acaso.
 */
export const ordenarRadar = (linhas: ExcecaoDoCockpit[]): ExcecaoDoCockpit[] =>
  [...linhas].sort((a, b) => a.ordem - b.ordem || a.codigoReferencia.localeCompare(b.codigoReferencia));

/**
 * Quanto do caminho até a meta já foi andado, entre 0 e 1. Meta zero ou ausente devolve
 * nulo: barra cheia sem meta definida seria comemoração de nada.
 */
export const progressoDaMeta = (valor: number, meta: number): number | null =>
  meta > 0 ? Math.max(0, Math.min(1, valor / meta)) : null;

/** O SLA da semana contra a meta institucional. */
export const slaAtingido = (pct: number, meta: number) => pct >= meta;

/**
 * Listas maiores que isto rolam sozinhas: a TV não tem quem arraste a barra.
 * Abaixo disso o auto-scroll só faria o texto tremer sem motivo.
 */
export const ITENS_SEM_ROLAGEM = 5;
export const precisaRolar = (quantidade: number) => quantidade > ITENS_SEM_ROLAGEM;

/** `HH:mm:ss` do relógio do cabeçalho. */
export const horaDoRelogio = (d: Date) =>
  [d.getHours(), d.getMinutes(), d.getSeconds()]
    .map((n) => String(n).padStart(2, '0')).join(':');

/** Números grandes na parede: 12.400 vira "12,4 mil" e 1.240.000 vira "1,2 mi". */
export function compacto(valor: number): string {
  const abs = Math.abs(valor);
  if (abs >= 1_000_000) return `${(valor / 1_000_000).toFixed(1).replace('.', ',')} mi`;
  if (abs >= 1_000) return `${(valor / 1_000).toFixed(1).replace('.', ',')} mil`;
  return String(Math.round(valor));
}

/**
 * O veredito da vazão do dia. O saldo sozinho é um número com sinal; o que o gestor lê de
 * longe é a frase — e ela precisa dizer **para que lado** o dia andou.
 *
 * <p>
 * Zero é "estável", e não "bom": entrar e sair na mesma medida mantém o backlog onde está,
 * que pode ser alto. Quem diz se o nível é confortável é o card de backlog, não este.
 * </p>
 */
export type SentidoDaVazao = 'CRESCENDO' | 'REDUZINDO' | 'ESTAVEL';

export const sentidoDaVazao = (saldo: number): SentidoDaVazao =>
  saldo > 0 ? 'CRESCENDO' : saldo < 0 ? 'REDUZINDO' : 'ESTAVEL';

export const FRASE_DA_VAZAO: Record<SentidoDaVazao, string> = {
  CRESCENDO: 'backlog aumentando',
  REDUZINDO: 'backlog reduzindo',
  ESTAVEL: 'backlog estável',
};

/**
 * A cor do saldo. Vermelho quando o backlog cresce, verde quando encolhe — e **cinza no
 * empate**, porque pintar o estável de verde faria a parede comemorar um dia em que o time
 * apenas não perdeu terreno.
 */
export const CLASSE_DA_VAZAO: Record<SentidoDaVazao, string> = {
  CRESCENDO: 'text-rose-400',
  REDUZINDO: 'text-emerald-400',
  ESTAVEL: 'text-slate-300',
};

/** O saldo com sinal: "+3" conta uma história que "3" não conta. */
export const saldoComSinal = (saldo: number) => (saldo > 0 ? `+${saldo}` : String(saldo));

/**
 * Tempo dito para quem lê de cinco metros. Até dois dias fica em horas; passando disso vira
 * dias, porque "76h" obriga a dividir de cabeça e "3d 4h" não.
 *
 * <p>
 * Nulo é **traço**, nunca zero: "ninguém mediu" e "levou zero hora" são notícias diferentes,
 * e só uma delas é elogio. É a mesma regra do OTIF sem entrega medida.
 * </p>
 */
export function horasNaParede(horas: number | null): string {
  if (horas === null) return '—';
  if (horas < 48) return `${horas.toLocaleString('pt-BR', { maximumFractionDigits: 1 })}h`;
  const dias = Math.floor(horas / 24);
  const resto = Math.round(horas % 24);
  return resto === 0 ? `${dias}d` : `${dias}d ${resto}h`;
}

/**
 * Se o bloco do almoxarifado entra na parede.
 *
 * <p>
 * A altura da TV é disputada, e cinco zeros tirariam espaço da esteira e do radar só para
 * dizer que o módulo de material não é usado. Basta uma fila, uma espera de aprovação ou um
 * atendimento no mês para o bloco existir — dentro de um mês de uso ele nunca pisca, e a
 * operação que não pede material ao almoxarifado nunca o vê.
 * </p>
 */
/**
 * A cor do valor comprado contra o teto. A faixa vem do servidor porque a meta de
 * `poTotalValue` é **"menor é melhor"** — teto, não alvo —, e a parede não tem como saber
 * sozinha para que lado o número é bom: verde dentro do teto, âmbar na tolerância que a
 * régua do catálogo já define, vermelho acima dela.
 *
 * <p>
 * Sem meta cadastrada não há cor, e não há "fora": o número fica branco como qualquer outro.
 * Pintar de vermelho um valor sem teto seria cobrar de uma meta que ninguém definiu.
 * </p>
 */
export const CLASSE_DO_TETO: Record<FaixaDaMeta, string> = {
  ok: 'text-emerald-400',
  atencao: 'text-amber-400',
  fora: 'text-rose-400',
};

export const tomDoTeto = (faixa: FaixaDaMeta | null) => (faixa === null ? '' : CLASSE_DO_TETO[faixa]);

/**
 * A frase da compra emergencial. Zero é notícia boa e merece ser dita — "nenhuma emergencial"
 * numa parede vale mais que um espaço vazio, que só significa que ninguém olhou.
 */
export function fraseDaEmergencia(emergenciais: number, pedidos: number): string {
  if (pedidos === 0) return 'nenhum pedido no mês';
  const total = `${pedidos} pedido${pedidos > 1 ? 's' : ''}`;
  if (emergenciais === 0) return `nenhuma emergencial em ${total}`;
  return `${emergenciais} emergencia${emergenciais > 1 ? 'is' : 'l'} de ${total}`;
}

/**
 * O que o estoque não deu conta, no mês. O plural é da frase e não do número: "1 viraram
 * compra" numa parede lida de cinco metros parece defeito da tela, e quem lê passa a
 * desconfiar do número ao lado.
 */
export const fraseDaRotaDeCompra = (quantas: number) =>
  quantas === 0 ? 'nenhuma virou compra no mês'
    : quantas === 1 ? '1 virou compra no mês'
      : `${quantas} viraram compra no mês`;

export const temAlmoxarifado = (a: AlmoxarifadoDoCockpit) =>
  a.filaSolicitacoes > 0 || a.aguardandoAprovacao > 0 || a.atendidasHoje > 0
  || a.atendidoPeloEstoquePct !== null || a.viraramCompraNoMes > 0;

// ---- a tela do material ---------------------------------------------------------

export const ROTULO_DO_ALERTA_DO_ALMOXARIFADO: Record<TipoDeAlertaDoAlmoxarifado, string> = {
  PRAZO_ESTOURADO: 'Prazo estourado',
  PRAZO_ATENCAO: 'Prazo a vencer',
  AGUARDANDO_CENTRO: 'Com o centro',
  ROTA_DE_COMPRA: 'Virou compra',
};

/**
 * A faixa de cor do radar do almoxarifado, pela mesma gramática do radar da compra: vermelho é
 * prazo estourado, âmbar é a vencer ou parada no centro, azul é o que virou compra — que não é
 * falha do estoque, é o caminho normal do que não havia.
 */
export const CLASSE_DO_ALERTA_DO_ALMOXARIFADO: Record<TipoDeAlertaDoAlmoxarifado, string> = {
  PRAZO_ESTOURADO: 'border-l-rose-500 bg-rose-950/40 text-rose-200',
  PRAZO_ATENCAO: 'border-l-amber-400 bg-amber-950/30 text-amber-200',
  AGUARDANDO_CENTRO: 'border-l-amber-400 bg-amber-950/30 text-amber-200',
  ROTA_DE_COMPRA: 'border-l-sky-400 bg-sky-950/30 text-sky-200',
};

/** A ordem do radar do almoxarifado: gravidade primeiro, estável no desempate. */
export const ordenarRadarDoAlmoxarifado = (linhas: AlertaDoAlmoxarifado[]): AlertaDoAlmoxarifado[] =>
  [...linhas].sort((a, b) => a.ordem - b.ordem || a.numero.localeCompare(b.numero));

/**
 * A frase das atendidas no prazo. Nula é traço com a explicação: "0%" diria que todas
 * atrasaram, e "nenhuma medida" é notícia diferente.
 */
export const fraseDasMedidas = (medidas: number) =>
  medidas === 0 ? 'nenhum atendimento medido no mês'
    : medidas === 1 ? '1 atendimento medido no mês'
      : `${medidas} atendimentos medidos no mês`;

/** O ranking sem linhas é dito, não deixado em branco: espaço vazio numa parede é ninguém olhou. */
export const SEM_RANKING = 'nada pedido no mês';
