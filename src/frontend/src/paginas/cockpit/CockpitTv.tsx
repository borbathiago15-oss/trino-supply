import { useEffect, useState } from 'react';
import {
  lerCockpit, tomDaVariacao, variacao,
  type AlertaDoCockpit, type Cockpit, type EtapaDoFluxo, type NumeroDoCockpit, type Sentido,
} from '@/api/cockpit';
import { moeda, quantidade } from '@/util/formato';

/** De quanto em quanto tempo a leitura é refeita. */
export const INTERVALO_DE_LEITURA = 30_000;
/** Quanto cada tela fica no ar antes de a próxima entrar. */
export const INTERVALO_DE_ROTACAO = 25_000;

export const TELAS = ['EXECUTIVO', 'PRODUTIVIDADE'] as const;
export type Tela = (typeof TELAS)[number];

const ROTULO_DA_TELA: Record<Tela, string> = {
  EXECUTIVO: 'Resultado de Suprimentos',
  PRODUTIVIDADE: 'Operação de Compras',
};

const hora = (d: Date) => d.toLocaleTimeString('pt-BR', { hour12: false });

/** O número grande, com a comparação embaixo. */
function Card({ rotulo, valor, sub, n, sentido, destaque }: {
  rotulo: string;
  valor: string;
  sub?: string | null;
  n?: NumeroDoCockpit;
  sentido?: Sentido;
  destaque?: 'ok' | 'aviso' | 'perigo';
}) {
  const v = n ? variacao(n) : null;
  const tom = tomDaVariacao(v, sentido ?? 'neutro');
  const cor = destaque === 'perigo' ? 'text-red-400'
    : destaque === 'aviso' ? 'text-amber-300'
      : destaque === 'ok' ? 'text-emerald-400' : 'text-white';
  return (
    <div className="flex min-w-0 flex-col justify-center rounded-xl bg-white/5 px-5 py-4"
      data-testid={`card-${rotulo}`}>
      <div className="truncate text-[13px] uppercase tracking-wide text-slate-400">{rotulo}</div>
      <div className={`mt-1 truncate text-[38px] font-bold leading-none ${cor}`}>{valor}</div>
      {sub && <div className="mt-1.5 truncate text-[13px] text-slate-400">{sub}</div>}
      {v != null && (
        <div className={'mt-1 text-[13px] font-semibold '
          + (tom === 'ok' ? 'text-emerald-400' : tom === 'ruim' ? 'text-red-400' : 'text-slate-400')}>
          {v > 0 ? '↑' : '↓'} {quantidade(Math.abs(v))}% vs. mês anterior
        </div>
      )}
    </div>
  );
}

const porcento = (v: number | null) => (v == null ? '—' : `${quantidade(v)}%`);

function TelaExecutivo({ c }: { c: Cockpit }) {
  const e = c.executivo;
  return (
    <div className="grid flex-1 grid-cols-2 gap-3 lg:grid-cols-3" data-testid="tela-executivo">
      <Card rotulo="Valor comprado" valor={moeda(e.valorComprado.valor)}
        n={e.valorComprado} sentido="neutro" />
      <Card rotulo="Economia" valor={moeda(e.economia.valor)} destaque="ok"
        sub={e.economiaPercentual != null ? `${quantidade(e.economiaPercentual)}% do comprado` : null}
        n={e.economia} sentido="maiorMelhor" />
      {/* separado da economia de propósito: metodologias diferentes não se somam */}
      <Card rotulo="Custos evitados" valor={moeda(e.custosEvitados.valor)} destaque="ok"
        sub="Reajuste negociado" n={e.custosEvitados} sentido="maiorMelhor" />
      <Card rotulo="Entregas no prazo" valor={porcento(e.entregasNoPrazoPercent)}
        destaque={e.entregasNoPrazoPercent != null && e.entregasNoPrazoPercent < 90 ? 'aviso' : 'ok'}
        sub={e.entregasConcluidas > 0
          ? `${e.entregasNoPrazo} de ${e.entregasConcluidas} entregas`
          : 'nenhuma entrega concluída no mês'} />
      <Card rotulo="SLA de compras" valor={porcento(e.slaDeComprasPercent)}
        destaque={e.prazoEstourado > 0 ? 'aviso' : 'ok'}
        sub={`${e.prazoEstourado} de ${e.emAndamento} com prazo estourado`} />
      <Card rotulo="Backlog" valor={quantidade(e.backlog)}
        destaque={e.backlog > 0 ? 'aviso' : 'ok'} sub="itens esperando o comprador" />
    </div>
  );
}

function TelaProdutividade({ c }: { c: Cockpit }) {
  const p = c.produtividade;
  const crescendo = p.saldo > 0;
  return (
    <div className="flex flex-1 flex-col gap-3" data-testid="tela-produtividade">
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <Card rotulo="Entraram hoje" valor={quantidade(p.entraramHoje)} sub="itens de solicitação" />
        <Card rotulo="Concluídos hoje" valor={quantidade(p.concluidosHoje)} sub="itens entregues" />
        {/* o sinal é a informação: o número absoluto não diz para que lado se anda */}
        <Card rotulo="Saldo do dia" valor={`${crescendo ? '+' : ''}${quantidade(p.saldo)}`}
          destaque={crescendo ? 'perigo' : 'ok'}
          sub={p.saldo === 0 ? 'backlog estável' : crescendo ? 'backlog aumentando' : 'backlog reduzindo'} />
        <Card rotulo="Taxa de conclusão" valor={porcento(p.taxaDeConclusao)}
          sub="concluídos sobre entrados" />
      </div>

      <div className="rounded-xl bg-white/5 px-5 py-4" data-testid="fluxo">
        <div className="text-[13px] uppercase tracking-wide text-slate-400">Fluxo de compras</div>
        <div className="mt-3 flex items-stretch gap-2 overflow-x-auto">
          {p.fluxo.map((e, i) => (
            <Etapa key={e.key} etapa={e} ultima={i === p.fluxo.length - 1}
              gargalo={p.gargalo?.key === e.key} />
          ))}
        </div>
      </div>

      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <Card rotulo="Em cotação" valor={quantidade(p.emCotacao)} />
        <Card rotulo="Aguardando aprovação" valor={quantidade(p.aguardandoAprovacao)} />
        <Card rotulo="Aguardando O.C." valor={quantidade(p.aguardandoOc)} />
        <Card rotulo="Urgentes" valor={quantidade(p.urgentes)}
          destaque={p.urgentes > 0 ? 'perigo' : 'ok'} />
      </div>
    </div>
  );
}

function Etapa({ etapa, ultima, gargalo }: {
  etapa: EtapaDoFluxo; ultima: boolean; gargalo: boolean;
}) {
  return (
    <>
      <div className={'min-w-[120px] flex-1 rounded-lg px-3 py-2 text-center '
        + (gargalo ? 'bg-amber-400/15 ring-1 ring-amber-400/50' : 'bg-white/5')}
        data-testid={`etapa-${etapa.key}`}>
        <div className="truncate text-[12px] uppercase tracking-wide text-slate-400">{etapa.label}</div>
        <div className={'text-[28px] font-bold leading-tight ' + (gargalo ? 'text-amber-300' : 'text-white')}>
          {quantidade(etapa.quantidade)}
        </div>
        {gargalo && <div className="text-[11px] font-semibold text-amber-300">gargalo</div>}
      </div>
      {!ultima && <div className="self-center text-slate-600">→</div>}
    </>
  );
}

const CLASSE_DO_TOM: Record<AlertaDoCockpit['tone'], string> = {
  alta: 'bg-red-500/20 text-red-300',
  media: 'bg-amber-500/20 text-amber-200',
  baixa: 'bg-slate-500/20 text-slate-300',
};

/**
 * O cockpit — a TV da sala de Suprimentos.
 *
 * Três decisões que a tela de parede impõe e a tela de mesa não:
 *
 * - **ninguém clica.** A informação gira sozinha, e o que não coube numa tela entra na
 *   próxima. Botão que só o instalador usa fica pequeno e fora do caminho;
 * - **ela precisa dizer se está viva.** A hora é a do servidor e aparece no topo: uma TV
 *   congelada com números plausíveis é pior que uma TV apagada, porque ninguém desconfia;
 * - **número sozinho não decide nada.** Cada card carrega a comparação com o período
 *   anterior — e o lado bom da variação depende do indicador, porque backlog subindo não
 *   é a mesma notícia que economia subindo.
 */
export function CockpitTv() {
  const [dados, setDados] = useState<Cockpit | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [tela, setTela] = useState<Tela>('EXECUTIVO');
  const [agora, setAgora] = useState(() => new Date());
  const [girando, setGirando] = useState(true);

  useEffect(() => {
    let vivo = true;
    const carregar = async () => {
      try {
        const c = await lerCockpit();
        if (vivo) { setDados(c); setErro(null); }
      } catch (e) {
        // o erro não apaga a leitura anterior: uma falha de rede não deve esvaziar a
        // parede. Ela mostra o último número bom e diz, no topo, que está velho
        if (vivo) setErro(e instanceof Error ? e.message : 'Falha ao ler o cockpit.');
      }
    };
    carregar();
    const t = setInterval(carregar, INTERVALO_DE_LEITURA);
    return () => { vivo = false; clearInterval(t); };
  }, []);

  useEffect(() => {
    const t = setInterval(() => setAgora(new Date()), 1000);
    return () => clearInterval(t);
  }, []);

  useEffect(() => {
    if (!girando) return undefined;
    const t = setInterval(
      () => setTela((a) => TELAS[(TELAS.indexOf(a) + 1) % TELAS.length]),
      INTERVALO_DE_ROTACAO,
    );
    return () => clearInterval(t);
  }, [girando]);

  return (
    <div className="flex min-h-screen flex-col gap-3 bg-slate-950 p-4 text-white" data-testid="cockpit">
      <header className="flex flex-wrap items-baseline justify-between gap-3">
        <div>
          <h1 className="text-[22px] font-bold tracking-tight">TRINO SUPPLY — AO VIVO</h1>
          <div className="text-[14px] text-slate-400" data-testid="tela-atual">{ROTULO_DA_TELA[tela]}</div>
        </div>
        <div className="flex items-center gap-3">
          {erro && (
            <span className="rounded-lg bg-red-500/20 px-3 py-1 text-[13px] text-red-300" data-testid="cockpit-erro">
              sem atualizar — {erro}
            </span>
          )}
          <div className="text-[30px] font-bold tabular-nums" data-testid="relogio">{hora(agora)}</div>
          <div className="flex gap-1.5">
            {TELAS.map((t) => (
              <button key={t} type="button" aria-label={ROTULO_DA_TELA[t]}
                data-testid={`ir-para-${t}`}
                onClick={() => { setTela(t); setGirando(false); }}
                className={'h-2.5 w-2.5 rounded-full ' + (t === tela ? 'bg-white' : 'bg-white/30')} />
            ))}
          </div>
        </div>
      </header>

      {!dados ? (
        <div className="flex flex-1 items-center justify-center text-slate-500">Carregando…</div>
      ) : tela === 'EXECUTIVO' ? <TelaExecutivo c={dados} /> : <TelaProdutividade c={dados} />}

      {/* a faixa some quando não há nada a fazer: alarme que grita sempre para de ser lido */}
      {dados && dados.alertas.length > 0 && (
        <footer className="flex flex-wrap items-center gap-2 rounded-xl bg-white/5 px-4 py-3"
          data-testid="faixa-alertas">
          <span className="text-[13px] font-bold uppercase tracking-wide text-red-400">Ação imediata</span>
          {dados.alertas.map((a) => (
            <span key={a.code} data-testid={`alerta-${a.code}`}
              className={'rounded-lg px-3 py-1 text-[15px] font-semibold ' + CLASSE_DO_TOM[a.tone]}>
              {quantidade(a.count)} {a.label}
            </span>
          ))}
        </footer>
      )}
    </div>
  );
}
