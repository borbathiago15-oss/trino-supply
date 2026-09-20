import { Link } from 'react-router-dom';
import type { Aviso } from '@/api/painel';
import { Painel } from '@/componentes/basicos';
import { Nota } from '@/componentes/formulario';
import { enderecoDoId, itemDaView, itensVisiveis, ehSubgrupo } from '@/layout/menu';
import type { Perfil } from '@/dominio/papeis';

export interface Etapa {
  /** Id do item de menu onde a etapa se cumpre. */
  destino: string;
  rotulo: string;
  /** Avisos que contam como trabalho parado nesta etapa. */
  avisos: string[];
}

/**
 * O ciclo de uma compra, na ordem em que acontece.
 *
 * O menu é organizado por módulo e o processo atravessa três deles, então
 * saber a sequência era conhecimento de cabeça. Aqui ela fica escrita, com o
 * que está parado em cada passo e o caminho para o lugar onde se age.
 */
export const ETAPAS: Etapa[] = [
  { destino: 'pr-mine', rotulo: 'Solicitar', avisos: ['DEVOLVIDO'] },
  { destino: 'pr-approvals', rotulo: 'Aprovar a SC', avisos: ['APROVACAO'] },
  { destino: 'triage', rotulo: 'Triar e designar', avisos: ['TRIAGEM', 'MINHAS_DEMANDAS'] },
  { destino: 'triage', rotulo: 'Encaminhar para compra', avisos: ['DEMANDA'] },
  { destino: 'quotations', rotulo: 'Cotar e escolher', avisos: ['COTACAO_ABERTA', 'COTACAO_ANALISE'] },
  { destino: 'pr-approvals', rotulo: 'Aprovar Níveis 1 e 2', avisos: ['APROVACAO_GERENTE', 'APROVACAO_DIRETOR'] },
  { destino: 'quotations', rotulo: 'Registrar a O.C.', avisos: ['OC_EMITIR'] },
  { destino: 'buy-orders', rotulo: 'Faturar e receber', avisos: ['PO_ATRASO'] },
  { destino: 'wh-queue', rotulo: 'Entregar do estoque', avisos: ['ALMOXARIFADO'] },
];

export interface EtapaNaTela extends Etapa { pendente: number; rota: string; alcancavel: boolean }

/** Ids das telas que este usuário enxerga — quem não alcança a etapa não recebe link. */
export function telasVisiveis(u: Perfil): Set<string> {
  const ids = new Set<string>();
  for (const g of itensVisiveis(u))
    for (const i of g.itens)
      for (const f of ehSubgrupo(i) ? i.filhos : [i]) ids.add(f.id);
  return ids;
}

export function montarTrilha(avisos: Aviso[], visiveis: Set<string>): EtapaNaTela[] {
  const porTipo = new Map(avisos.map((a) => [a.kind, a] as const));
  return ETAPAS.map((e) => ({
    ...e,
    pendente: e.avisos.reduce((t, k) => t + (porTipo.get(k)?.count ?? 0), 0),
    rota: enderecoDoId(e.destino),
    alcancavel: visiveis.has(itemDaView(e.destino)),
  }));
}

function Passo({ etapa, numero }: { etapa: EtapaNaTela; numero: number }) {
  const parado = etapa.pendente > 0;
  // três estados: com trabalho parado (em destaque), ao alcance (neutro) e fora do alcance
  // do usuário (tracejado e apagado — está no mapa, mas não é dele)
  const classe = 'flex min-w-0 flex-1 basis-[176px] items-center gap-2.5 rounded-lg border bg-white px-3 py-2.5 text-left '
    + (parado ? 'border-aviso-borda shadow-sm' : etapa.alcancavel ? 'border-slate-200' : 'border-dashed border-slate-200 opacity-60');

  const conteudo = (
    <>
      <span className={'flex h-6 w-6 shrink-0 items-center justify-center rounded-full text-xs font-bold '
        + (parado ? 'bg-marca text-white' : 'bg-slate-100 text-slate-500')}>
        {numero}
      </span>
      <span className="min-w-0 flex-1 text-[13px] font-semibold leading-tight text-slate-800">{etapa.rotulo}</span>
      {parado && (
        <span data-testid={`parado-${etapa.rotulo}`}
          className="shrink-0 rounded-full bg-amber-100 px-2 py-0.5 text-xs font-semibold text-amber-800">{etapa.pendente}</span>
      )}
    </>
  );

  if (!etapa.alcancavel)
    // etapa que existe no processo mas não é sua: fica no mapa, sem link
    return <span className={classe} data-etapa={etapa.rotulo}>{conteudo}</span>;

  return (
    <Link to={etapa.rota} className={classe + ' transition-colors hover:border-marca/40 hover:bg-slate-50'} data-etapa={etapa.rotulo}>
      {conteudo}
    </Link>
  );
}

export function TrilhaDoProcesso({ avisos, usuario }: { avisos: Aviso[]; usuario: Perfil }) {
  const trilha = montarTrilha(avisos, telasVisiveis(usuario));

  return (
    <Painel titulo="O ciclo da compra, do começo ao fim">
      {/* quebra linha em vez de rolar de lado: os nove passos precisam estar à vista, não atrás de uma barra */}
      <div className="flex flex-wrap items-center gap-2 py-2" data-testid="trilha-processo">
        {trilha.map((e, i) => <Passo key={`${e.rotulo}-${i}`} etapa={e} numero={i + 1} />)}
      </div>
      <Nota>
        A sequência é sempre esta. O número é o que está parado no passo — clique para ir ao
        lugar onde ele anda. Passo sem número não tem nada esperando.
      </Nota>
    </Painel>
  );
}
