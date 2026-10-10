import { useCallback, useEffect, useState } from 'react';
import { HexagonosDaMarca } from '@/componentes/HexagonosDaMarca';
import {
  INTERVALO_DO_COCKPIT, obterCockpit, obterCockpitDoMaterial, type CockpitDados, type CockpitDoMaterial,
} from '@/api/torre';
import { useCarregar } from '@/util/useCarregar';
import {
  cicloDeParadas, cicloDeUnidades, horaDoRelogio, paradaInicial, proximaParada, ROTACAO_MS, temAlmoxarifado,
  type Parada,
} from './cockpit';
import { PulsoAoVivo } from './pecas';
import { TelaDeCompras } from './TelaDeCompras';
import { TelaDoMaterial } from './TelaDoMaterial';

/** O relógio do cabeçalho, de segundo em segundo. */
function useRelogio() {
  const [agora, setAgora] = useState(() => new Date());
  useEffect(() => {
    const id = setInterval(() => setAgora(new Date()), 1000);
    return () => clearInterval(id);
  }, []);
  return agora;
}

export const ROTULO_DA_TELA: Record<Parada['tela'], string> = {
  compras: 'Compras',
  material: 'Material do almoxarifado',
};

/**
 * War Room Cockpit — a TV da sala de suprimentos.
 *
 * <p>
 * Três decisões sustentam a tela. Ela **deriva da Torre**, e não de uma segunda conta: a TV
 * fica na sala onde o comprador trabalha, e duas telas com números diferentes perdem a
 * autoridade juntas. Ela **troca sem piscar**: o valor anterior fica até o novo chegar, porque
 * número que some e volta parece defeito. E ela **não tem barra de rolagem**: ninguém toca
 * nessa tela, então lista longa rola sozinha.
 * </p>
 *
 * <p>
 * A parede tem **duas telas num rodízio só** (decisão da empresa, 2026-10): a da compra e a do
 * material do almoxarifado, uma parada por minuto, no mesmo relógio que já girava as unidades —
 * Compras geral → Material geral → Compras PB → Material PB… Um segundo cronômetro cortaria a
 * unidade pela metade. A tela do material só entra quando a visão geral diz que há material
 * para contar, e os dados das duas telas da parada atual ficam frescos ao mesmo tempo: a faixa
 * da compra no topo da tela do material lê os mesmos números do cockpit de compras.
 * </p>
 */
export function Cockpit() {
  const agora = useRelogio();
  const [telaCheia, setTelaCheia] = useState(false);
  const [parada, setParada] = useState<Parada>(() => paradaInicial(window.location.search));
  const { dados, erro, recarregar } = useCarregar<CockpitDados>(
    (signal) => obterCockpit(parada.unidade, signal), [parada.unidade]);

  // se há material para contar, quem diz é a visão geral: decidir pela unidade da vez faria a
  // tela do material entrar e sair do ciclo a cada volta
  const [temMaterial, setTemMaterial] = useState(false);
  useEffect(() => {
    if (dados && dados.unidade === null) setTemMaterial(temAlmoxarifado(dados.almoxarifado));
  }, [dados]);
  const material = useCarregar<CockpitDoMaterial | null>(
    (signal) => (temMaterial ? obterCockpitDoMaterial(parada.unidade, signal) : Promise.resolve(null)),
    [parada.unidade, temMaterial]);
  const recarregarMaterial = material.recarregar;

  // o ciclo silencioso: `recarregar` mantém os dados atuais na tela enquanto os novos vêm
  useEffect(() => {
    const id = setInterval(() => { recarregar(); recarregarMaterial(); }, INTERVALO_DO_COCKPIT);
    return () => clearInterval(id);
  }, [recarregar, recarregarMaterial]);

  // o rodízio da parede: a cada minuto a TV passa para a próxima parada sozinha
  const unidades = dados?.unidades ?? [];
  const ciclo = cicloDeParadas(unidades, temMaterial);
  const chaveDoCiclo = ciclo.map((p) => `${p.tela}:${p.unidade ?? ''}`).join('|');
  useEffect(() => {
    if (ciclo.length <= 1) return;
    const id = setInterval(() => setParada((atual) => proximaParada(ciclo, atual)), ROTACAO_MS);
    return () => clearInterval(id);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [chaveDoCiclo]);

  const alternarTelaCheia = useCallback(async () => {
    try {
      if (document.fullscreenElement) { await document.exitFullscreen(); setTelaCheia(false); }
      else { await document.documentElement.requestFullscreen(); setTelaCheia(true); }
    } catch { /* navegador sem permissão: a tela continua servindo do mesmo jeito */ }
  }, []);

  if (!dados) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-fundo text-slate-400">
        {erro ? `Sem sinal — ${erro}` : 'Conectando ao cockpit…'}
      </div>
    );
  }

  // a tela do material sem os dados dela ainda (primeira volta, ou a consulta falhou e não há
  // leitura anterior) mostra a compra: parede em branco é pior que a tela de antes
  const telaNaParede: Parada['tela'] = parada.tela === 'material' && material.dados ? 'material' : 'compras';
  const recortes = cicloDeUnidades(unidades);

  return (
    <div className="relative flex h-screen flex-col overflow-hidden bg-fundo p-5 text-white" data-testid="cockpit"
      data-tela={telaNaParede}>
      <HexagonosDaMarca variante="cantos" opacidade={0.6} />
      {/* NÍVEL 1 — barra de estado */}
      <header className="flex items-center justify-between pb-4">
        <div className="flex items-center gap-4">
          {/* a marca na parede: quem entra na sala tem de saber de quem é o painel antes
              de ler qualquer número. É o mesmo arquivo do menu, que já nasceu para fundo
              escuro — e o alt mantém o título da tela para quem lê por leitor de tela */}
          <h1 className="m-0">
            <img src="/assets/brand/trino-supply-mark.png" width={420} height={108}
              alt="Trino Supply" className="h-9 w-auto" />
          </h1>
          <PulsoAoVivo vivo={!erro} />
          {/* qual recorte está na parede agora — sem isso, quem chega lê o número
              da unidade da vez achando que é o da empresa inteira */}
          <span data-testid="unidade-na-tela"
            className="rounded-full border border-slate-700 px-3 py-1 text-[13px] text-slate-300">
            {dados.unidade ?? 'Visão geral'}
            {recortes.length > 1 && <span className="ml-2 text-slate-500">{recortes.length} recortes</span>}
          </span>
          {/* e qual das duas telas: a compra e o material têm a mesma moldura, e um número
              lido na tela errada é um número errado */}
          <span data-testid="tela-na-parede"
            className={'rounded-full border px-3 py-1 text-[13px] '
              + (telaNaParede === 'material' ? 'border-sky-500/60 text-sky-200' : 'border-slate-700 text-slate-300')}>
            {ROTULO_DA_TELA[telaNaParede]}
            {temMaterial && <span className="ml-2 text-slate-500">rodízio de {ciclo.length} telas</span>}
          </span>
          {(erro || (parada.tela === 'material' && material.erro)) && (
            <span className="text-[13px] text-rose-300">última leitura mantida</span>
          )}
        </div>
        <div className="flex items-center gap-5">
          <span className="font-mono text-3xl font-bold tabular-nums">{horaDoRelogio(agora)}</span>
          <button type="button" onClick={() => void alternarTelaCheia()}
            className="rounded-lg border border-slate-700 px-3 py-1.5 text-[12px] text-slate-400 hover:text-white"
            aria-label="Alternar tela cheia">
            {telaCheia ? '◱' : '⛶'}
          </button>
        </div>
      </header>

      {telaNaParede === 'material' && material.dados
        ? <TelaDoMaterial compras={dados} material={material.dados} />
        : <TelaDeCompras dados={dados} />}
    </div>
  );
}
