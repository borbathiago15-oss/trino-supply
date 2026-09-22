import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import {
  CLASSE_DO_TOM, destinoDaAcao, DIAS_PARA_DESTACAR_ESPERA, ETAPAS, FILTROS_TORRE_VAZIOS,
  SELO_DO_PRAZO, tempoParado, torreDeControle,
  type FiltrosDaTorre, type LinhaDaTorre,
  FAIXAS_DE_FILA,
} from '@/api/torre';
import { Aviso, Badge, Carregando, Erro, FaixaKpis, Painel, Vazio } from '@/componentes/basicos';
import { Campo } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { podeTriar } from '@/dominio/papeis';
import { designar, designarEmLote, listarResponsaveis, rotuloDoResponsavel } from '@/api/triagem';
import { DialogoDePrioridade, type Pleito } from '@/paginas/triagem/DialogoDePrioridade';
import { useUsuario } from '@/sessao/SessaoProvider';
import { data, moeda, quantidade } from '@/util/formato';
import { rolarPara } from '@/util/rolar';
import { useCarregar } from '@/util/useCarregar';

/** Um KPI que também filtra: clicar leva a Torre para aquela etapa. */
function KpiFiltro({ rotulo, valor, detalhe, ativo, aoClicar }: {
  rotulo: string; valor: number | string; detalhe?: string; ativo: boolean; aoClicar: () => void;
}) {
  return (
    <button type="button" onClick={aoClicar} aria-pressed={ativo}
      className={'rounded-xl border px-4 py-3.5 text-left shadow-sm transition-colors '
        + (ativo ? 'border-marca bg-marca/5 ring-2 ring-marca/20' : 'border-slate-200/80 bg-white hover:border-marca/40')}>
      <div className="rotulo">{rotulo}</div>
      <div className="mt-1.5 text-3xl font-extrabold leading-none tracking-tight text-slate-900 tabular-nums">{valor}</div>
      {detalhe && <div className="sub mt-1.5">{detalhe}</div>}
    </button>
  );
}

function Linha({ i, triando, marcada, aoMarcar, aoLiberar, aoPriorizar }: {
  i: LinhaDaTorre; triando: boolean; marcada: boolean;
  aoMarcar: (scId: string) => void; aoLiberar: (scId: string) => void;
  aoPriorizar: (i: LinhaDaTorre) => void;
}) {
  const urgente = i.priority === 'URGENT';
  const destino = destinoDaAcao(i);
  // parado tempo demais na mesma etapa: o número já está na linha, o destaque é para
  // ele não passar despercebido no meio de cinquenta linhas
  const parado = (i.waitingOn?.days ?? 0) >= DIAS_PARA_DESTACAR_ESPERA;
  // o selo do prazo só existe quando há veredito: etapa sem prazo não ganha cor nenhuma
  const selo = i.sla?.status === 'ATENCAO' || i.sla?.status === 'ESTOURADO'
    ? SELO_DO_PRAZO[i.sla.status] : null;
  return (
    <tr data-testid={`linha-${i.itemId}`} className={i.late ? 'bg-perigo-fundo/40' : undefined}>
      {triando && (
        <td className="whitespace-nowrap">
          {/* a atribuição é da SC inteira, não do item: marcar um item marca a SC,
              e é por isso que a caixa fica desligada nas outras linhas dela */}
          <input type="checkbox" aria-label={`Selecionar ${i.prNumber}`}
            checked={marcada} onChange={() => aoMarcar(i.requisitionId)} />
        </td>
      )}
      <td className="whitespace-nowrap">{i.prNumber}<div className="sub">item {i.sequence}</div></td>
      <td className="min-w-[200px]">
        {i.catalogCode ? `[${i.catalogCode}] ` : ''}{i.description}
        {urgente && <Badge classe="ml-2 bg-perigo-fundo text-perigo">URGENTE</Badge>}
        {/* a prioridade muda aqui, ao lado de onde ela é lida. Veio da tela de triagem
            junto com o resto: era a única coisa que só lá existia, e deixá-la para trás
            teria trocado duas telas em conflito por uma função a menos */}
        {triando && (
          <button type="button" className="sub ml-2 underline"
            onClick={() => aoPriorizar(i)}>
            {urgente ? 'voltar a normal' : 'tornar urgente'}
          </button>
        )}
      </td>
      <td className="whitespace-nowrap">{quantidade(i.quantity)} {i.unitOfMeasure}</td>
      <td className="whitespace-nowrap">{i.requesterLabel}<div className="sub">{i.company ?? '—'}</div></td>
      <td className="whitespace-nowrap">{i.costCenter}</td>
      <td className="whitespace-nowrap">
        {i.buyerLabel ? (
          <>
            {i.buyerLabel}
            {triando && (
              <button type="button" className="sub ml-1 underline"
                title="Devolver à fila de triagem" onClick={() => aoLiberar(i.requisitionId)}>
                liberar
              </button>
            )}
          </>
        ) : <span className="sub">sem responsável</span>}
      </td>
      <td className="whitespace-nowrap">{i.supplierName ?? <span className="sub">—</span>}</td>
      <td className="whitespace-nowrap">{i.stageLabel}</td>
      {/* a situação diz a etapa; a espera diz de quem ela depende e há quanto tempo.
          Sem a segunda metade, "Aguardando Aprovação" mandava abrir o centro de custo
          para descobrir quem aprova, e "Em Cotação" mandava abrir o processo para ver
          qual fornecedor faltava — coisas que o servidor já sabia e não dizia */}
      <td className="min-w-[210px]">
        <Badge classe={CLASSE_DO_TOM[i.statusTone] ?? CLASSE_DO_TOM['']}>{i.statusLabel}</Badge>
        {i.waitingOn && (
          <div className="sub mt-1" data-testid={`espera-${i.itemId}`}>
            {i.waitingOn.who}
            {i.waitingOn.days != null && (
              <>
                {' · '}
                <span className={parado ? 'font-bold text-aviso' : undefined}>
                  {tempoParado(i.waitingOn)}
                </span>
              </>
            )}
            {i.waitingOn.detail && <div className="text-perigo">{i.waitingOn.detail}</div>}
            {/* o veredito do prazo vem junto do tempo, e diz contra que prazo ele saiu:
                "prazo estourado" sem o número faz o comprador ir procurar a régua */}
            {selo && (
              <div className={'font-bold ' + selo.classe} data-testid={`prazo-${i.itemId}`}>
                ⏱ {selo.rotulo} · limite {i.sla!.maxDays} dia{i.sla!.maxDays === 1 ? '' : 's'}
              </div>
            )}
          </div>
        )}
      </td>
      <td className="whitespace-nowrap">
        {data(i.promisedDate ?? i.neededBy)}
        {i.late && <div className="text-[11.5px] font-bold text-perigo">atrasado</div>}
        {/* número de exceção no topo sem o motivo na linha obriga o comprador a
            caçar o processo um a um — a razão vem junto */}
        {i.exceptionReason && (
          <div className="text-[11.5px] font-bold text-aviso" title="Exceção registrada">
            ⚠ {i.exceptionReason}
          </div>
        )}
      </td>
      <td className="whitespace-nowrap">{i.value != null ? moeda(i.value) : <span className="sub">—</span>}</td>
      {/* §5 — a ação rápida: a linha diz o que fazer agora e leva até lá, em vez de
          obrigar o comprador a descobrir a tela certa para cada etapa */}
      <td className="whitespace-nowrap">
        {!i.actionLabel ? <span className="sub">—</span>
          : destino ? (
            <Link className={'botao-secundario inline-block ' + (i.needsBuyer ? 'font-semibold' : '')}
              to={destino}>
              {i.actionLabel}
            </Link>
          ) : (
            // a ação é nesta mesma tela (marcar a linha e atribuir na barra acima):
            // um link para onde já se está não leva a lugar nenhum
            <span className="sub" title="Marque a linha e atribua na barra de triagem">
              {i.actionLabel} aqui ↑
            </span>
          )}
      </td>
      {/* o link leva para onde a ação está: o pedido, se já existe; senão a cotação */}
      <td className="whitespace-nowrap">
        {i.purchaseOrderId ? (
          <Link className="text-marca hover:underline" to={`/pedidos/${i.purchaseOrderId}`}>
            {i.purchaseOrderNumber}
          </Link>
        ) : i.quotationId ? (
          <Link className="text-marca hover:underline" to={`/cotacoes/${i.quotationId}`}>
            {i.quotationNumber}
          </Link>
        ) : <span className="sub">—</span>}
      </td>
    </tr>
  );
}

/**
 * Torre de Controle — a tela do comprador.
 *
 * **Uma linha por item, nunca uma por solicitação.** Uma SC de cinco itens pode
 * ter um entregue, dois em cotação e dois parados esperando alguém; olhar a SC
 * inteira esconde exatamente o que precisa de ação.
 *
 * Os KPIs do topo filtram: o número é a pergunta, e clicar nele é a resposta.
 * Eles contam a base inteira, não a página — KPI que muda ao virar a página não
 * é indicador, é contagem de tela.
 */
export function TorreDeControle() {
  const usuario = useUsuario();
  const { avisar } = useToast();
  const triando = podeTriar(usuario);

  const [rascunho, setRascunho] = useState<FiltrosDaTorre>(FILTROS_TORRE_VAZIOS);
  const [aplicados, setAplicados] = useState<FiltrosDaTorre>(FILTROS_TORRE_VAZIOS);
  const [recarga, setRecarga] = useState(0);
  const [pleito, setPleito] = useState<Pleito | null>(null);
  const { dados, erro, carregando } = useCarregar(
    (signal) => torreDeControle(aplicados, signal), [aplicados, recarga]);

  // ---- triagem, dentro da Torre --------------------------------------------
  // A demanda que chega para o comprador é a etapa de Solicitação da própria Torre:
  // manter as duas telas separadas obrigava a sair daqui para atribuir e voltar para
  // acompanhar. As chamadas são as mesmas de `/api/v1/triage` — a regra de quem pode
  // receber demanda continua num lugar só, no servidor.
  const equipe = useCarregar(
    async (signal) => (triando ? listarResponsaveis(signal).catch(() => []) : []), [triando]);
  const [marcadas, setMarcadas] = useState<Record<string, boolean>>({});
  const [paraQuem, setParaQuem] = useState('');
  const [atribuindo, setAtribuindo] = useState(false);

  // a atribuição é da SC, e a Torre mostra uma linha por item: sem deduplicar, uma SC
  // de cinco itens contaria cinco vezes e o lote mandaria a mesma SC cinco vezes
  const scsMarcadas = useMemo(
    () => Object.entries(marcadas).filter(([, v]) => v).map(([id]) => id), [marcadas]);

  const alternar = (scId: string) =>
    setMarcadas((m) => ({ ...m, [scId]: !m[scId] }));

  async function atribuir() {
    if (!paraQuem || !scsMarcadas.length) return;
    setAtribuindo(true);
    try {
      const r = await designarEmLote(scsMarcadas.map((id) => ({ kind: 'SC' as const, id })), paraQuem);
      const falhas = r.failed?.length ?? 0;
      avisar(falhas
        ? `${r.assigned} solicitação(ões) atribuída(s); ${falhas} recusada(s): ${r.failed[0].message}`
        : `${r.assigned} solicitação(ões) atribuída(s).`, falhas ? 'erro' : 'ok');
      setMarcadas({});
      setParaQuem('');
      setRecarga((n) => n + 1);
    } catch (e) {
      avisar(e instanceof Error ? e.message : 'Falha ao atribuir.', 'erro');
    } finally { setAtribuindo(false); }
  }

  async function tirarResponsavel(scId: string) {
    try {
      await designar('SC', scId, null);
      avisar('Responsável removido: a solicitação volta para a fila.');
      setRecarga((n) => n + 1);
    } catch (e) { avisar(e instanceof Error ? e.message : 'Falha ao liberar.', 'erro'); }
  }

  const campo = (k: keyof FiltrosDaTorre) => ({
    value: String(rascunho[k] ?? ''),
    onChange: (e: { target: { value: string } }) => setRascunho((f) => ({ ...f, [k]: e.target.value })),
  });
  const fo = dados?.filterOptions;

  /** Aplicar sempre volta para a primeira página: filtro novo, contagem nova. */
  const aplicar = (extra: Partial<FiltrosDaTorre> = {}) => {
    const f = { ...rascunho, ...extra, pagina: 1 };
    setRascunho(f);
    setAplicados(f);
  };
  const limpar = () => { setRascunho(FILTROS_TORRE_VAZIOS); setAplicados({ ...FILTROS_TORRE_VAZIOS }); };
  const irPara = (p: number) => setAplicados((f) => ({ ...f, pagina: p }));

  /**
   * Clicar num card do topo é pedir "me mostre estes". Além de filtrar, a tela **rola até
   * a lista**: com os cards, a faixa de fila e a barra de filtros acima dela, o comprador
   * clicava, a tabela mudava fora da vista, e parecia que nada tinha acontecido.
   *
   * <p>
   * O recorte vem inteiro, e não só o campo que muda: o card promete uma lista, e restos
   * de um filtro anterior a fariam ser outra. Só `busca` fica, porque é do comprador.
   * </p>
   */
  const porCard = (recorte: Partial<FiltrosDaTorre>) => {
    aplicar({
      ...FILTROS_TORRE_VAZIOS, busca: rascunho.busca, ...recorte,
    });
    rolarPara('lista-da-torre');
  };
  /** Algum card está recortando a lista? É o que faz "Itens em aberto" ficar aceso ou não. */
  const temRecorte = aplicados.etapa !== '' || aplicados.atrasados || aplicados.excecoes
    || aplicados.minhaFila || aplicados.prioridade !== '' || aplicados.faturamento !== ''
    || aplicados.prazoEstourado;

  /** Card de etapa: clicar de novo no que já está ativo desliga o filtro. */
  const porEtapa = (etapa: string, extra: Partial<FiltrosDaTorre> = {}) =>
    porCard(aplicados.etapa === etapa && aplicados.faturamento === (extra.faturamento ?? '')
      ? {} : { etapa, ...extra });

  return (
    <>
      {dados && (
        <FaixaKpis>
          {/* clicável como os outros: era o único card que não levava a lugar nenhum,
              e "todos os itens" é uma lista tão legítima quanto as demais */}
          <KpiFiltro rotulo="Itens em aberto" valor={quantidade(dados.kpis.total)}
            detalhe={`${moeda(dados.kpis.valor)} estimados`}
            ativo={!temRecorte} aoClicar={() => porCard({})} />
          {/* §5 — a fila prioritária. Vem primeiro entre os filtros porque é por onde
              o comprador começa o dia: o que espera ele, na ordem em que aperta */}
          {/* o número vem do servidor pela mesma regra do filtro: somar etapas aqui
              daria um card que promete 12 e abre uma lista de 14 */}
          <KpiFiltro rotulo="Precisa de você" valor={quantidade(dados.kpis.precisaDeVoce)}
            detalhe="atrasado e urgente primeiro"
            ativo={aplicados.minhaFila}
            aoClicar={() => porCard(aplicados.minhaFila ? {} : { minhaFila: true })} />
          <KpiFiltro rotulo="Novos" valor={quantidade(dados.kpis.novos)} detalhe="aguardando o comprador"
            ativo={aplicados.etapa === 'SOLICITACAO'} aoClicar={() => porEtapa('SOLICITACAO')} />
          <KpiFiltro rotulo="Em cotação" valor={quantidade(dados.kpis.emCotacao)} detalhe="sourcing em andamento"
            ativo={aplicados.etapa === 'COTACAO'} aoClicar={() => porEtapa('COTACAO')} />
          <KpiFiltro rotulo="Aguardando aprovação" valor={quantidade(dados.kpis.aguardandoAprovacao)}
            detalhe="níveis 1 e 2" ativo={aplicados.etapa === 'APROVACAO'} aoClicar={() => porEtapa('APROVACAO')} />
          <KpiFiltro rotulo="Aguardando O.C." valor={quantidade(dados.kpis.aguardandoOc)}
            detalhe="aprovado, sem O.C. registrada"
            ativo={aplicados.etapa === 'ORDEM_DE_COMPRA'} aoClicar={() => porEtapa('ORDEM_DE_COMPRA')} />
          {/* §5: duas filas, e o que as separa é a nota fiscal — sem NF a bola está
              com o fornecedor; com NF e sem entrega, com o almoxarifado */}
          {/* os dois contam em separado e agora abrem listas diferentes: antes caíam no
              mesmo filtro de etapa, e clicar em "Em faturamento: 3" mostrava as dez linhas
              das duas filas — o card mentia sobre a própria lista */}
          <KpiFiltro rotulo="Em faturamento" valor={quantidade(dados.kpis.emFaturamento)}
            detalhe="O.C. emitida, sem NF"
            ativo={aplicados.etapa === 'RECEBIMENTO' && aplicados.faturamento === 'sem-nf'}
            aoClicar={() => porEtapa('RECEBIMENTO', { faturamento: 'sem-nf' })} />
          <KpiFiltro rotulo="Aguardando recebimento" valor={quantidade(dados.kpis.aguardandoRecebimento)}
            detalhe="NF lançada, material a caminho"
            ativo={aplicados.etapa === 'RECEBIMENTO' && aplicados.faturamento === 'com-nf'}
            aoClicar={() => porEtapa('RECEBIMENTO', { faturamento: 'com-nf' })} />
          {/* "atrasado" é sobre a data prometida ao solicitante; "prazo estourado" é sobre
              o tempo da etapa. São duas perguntas: um item pode estar dentro da previsão e
              mesmo assim parado tempo demais numa etapa */}
          <KpiFiltro rotulo="Prazo estourado" valor={quantidade(dados.kpis.prazoEstourado)}
            detalhe="passaram do prazo da etapa"
            ativo={aplicados.prazoEstourado}
            aoClicar={() => porCard(aplicados.prazoEstourado ? {} : { prazoEstourado: true })} />
          <KpiFiltro rotulo="Atrasados" valor={quantidade(dados.kpis.atrasados)} detalhe="passaram da previsão"
            ativo={aplicados.atrasados}
            aoClicar={() => porCard(aplicados.atrasados ? {} : { atrasados: true })} />
          <KpiFiltro rotulo="Urgentes" valor={quantidade(dados.kpis.urgentes)} detalhe="prioridade URGENT"
            ativo={aplicados.prioridade === 'URGENT'}
            aoClicar={() => porCard(aplicados.prioridade === 'URGENT' ? {} : { prioridade: 'URGENT' })} />
          <KpiFiltro rotulo="Exceções" valor={quantidade(dados.kpis.excecoes)}
            detalhe="sem O.C. do ERP, cancelado ou devolvido"
            ativo={aplicados.excecoes}
            aoClicar={() => porCard(aplicados.excecoes ? {} : { excecoes: true })} />
        </FaixaKpis>
      )}

      {/* Tempo na fila. Veio da tela de triagem, que era a única a mostrá-lo — e era
          justamente o que fazia aquela tela existir em paralelo a esta. Fica separado
          dos KPIs de etapa porque responde outra pergunta: não "onde está", mas
          "há quanto tempo está parado aí". */}
      {dados?.kpis.porFaixaDeAging && (
        <div className="mt-3 flex flex-wrap items-center gap-2" data-testid="faixas-de-fila">
          <span className="sub">Tempo na fila:</span>
          {FAIXAS_DE_FILA.map((f, idx) => {
            const ativo = aplicados.faixaDeFila === String(idx);
            return (
              <button key={f.rotulo} type="button"
                className={`rounded-full px-3 py-1 text-[12.5px] font-semibold ${f.classe}`
                  + (ativo ? ' ring-2 ring-marca' : '')}
                aria-pressed={ativo}
                onClick={() => aplicar({ faixaDeFila: ativo ? '' : String(idx) })}>
                {f.rotulo} · {quantidade(dados.kpis.porFaixaDeAging![idx] ?? 0)}
              </button>
            );
          })}
        </div>
      )}

      {/* a porta do Modo TV. O cockpit é tela de parede e fica fora do menu, mas sem um
          caminho a partir daqui ele só abria para quem soubesse digitar a URL — e tela
          que ninguém acha é tela que ninguém usa */}
      <Painel titulo="Filtros" acoes={
        <a className="botao-secundario" href="/cockpit" target="_blank" rel="noopener"
          title="Abre o cockpit em tela cheia, para a TV da sala">Modo TV ↗</a>
      }>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <Campo id="tc-busca" rotulo="Buscar" dica="(SC, produto ou código)">
            <input id="tc-busca" placeholder="PR-2026-000123, luva…" {...campo('busca')}
              onKeyDown={(e) => { if (e.key === 'Enter') aplicar(); }} />
          </Campo>
          <Campo id="tc-etapa" rotulo="Etapa">
            <select id="tc-etapa" {...campo('etapa')}>
              <option value="">Todas</option>
              {ETAPAS.map((e) => <option key={e.key} value={e.key}>{e.label}</option>)}
            </select>
          </Campo>
          <Campo id="tc-cc" rotulo="Centro de custo">
            <select id="tc-cc" {...campo('centroCusto')}>
              <option value="">Todos</option>
              {(fo?.costCenters ?? []).map((c) => (
                <option key={c.code} value={c.code}>{c.code} — {c.name}</option>
              ))}
            </select>
          </Campo>
          <Campo id="tc-empresa" rotulo="Empresa">
            <select id="tc-empresa" {...campo('empresa')}>
              <option value="">Todas</option>
              {(fo?.companies ?? []).map((e) => <option key={e} value={e}>{e}</option>)}
            </select>
          </Campo>
          <Campo id="tc-comprador" rotulo="Comprador">
            <select id="tc-comprador" {...campo('comprador')}>
              <option value="">Todos</option>
              {(fo?.buyers ?? []).map((b) => <option key={b.id} value={b.id}>{b.label}</option>)}
            </select>
          </Campo>
          <Campo id="tc-solicitante" rotulo="Solicitante">
            <select id="tc-solicitante" {...campo('solicitante')}>
              <option value="">Todos</option>
              {(fo?.requesters ?? []).map((r) => <option key={r.id} value={r.id}>{r.label}</option>)}
            </select>
          </Campo>
          <Campo id="tc-familia" rotulo="Família">
            <select id="tc-familia" {...campo('familia')}>
              <option value="">Todas</option>
              {(fo?.families ?? []).map((f) => <option key={f} value={f}>{f}</option>)}
            </select>
          </Campo>
          <Campo id="tc-prioridade" rotulo="Prioridade">
            <select id="tc-prioridade" {...campo('prioridade')}>
              <option value="">Todas</option>
              {['URGENT', 'HIGH', 'NORMAL', 'LOW'].map((p) => <option key={p} value={p}>{p}</option>)}
            </select>
          </Campo>

          {/* §5.1 — os quatro últimos dependem do pedido: item ainda não comprado
              não tem fornecedor, O.C. nem valor fechado, e por isso sai do recorte */}
          <Campo id="tc-fornecedor" rotulo="Fornecedor">
            <input id="tc-fornecedor" placeholder="parte do nome" {...campo('fornecedor')}
              onKeyDown={(e) => { if (e.key === 'Enter') aplicar(); }} />
          </Campo>
          <Campo id="tc-oc" rotulo="Número da O.C." dica="(nossa ou do ERP)">
            <input id="tc-oc" placeholder="PO-2026-000123 ou 4521" {...campo('numeroOc')}
              onKeyDown={(e) => { if (e.key === 'Enter') aplicar(); }} />
          </Campo>
          <Campo id="tc-de" rotulo="SC criada de">
            <input id="tc-de" type="date" {...campo('de')} />
          </Campo>
          <Campo id="tc-ate" rotulo="SC criada até">
            <input id="tc-ate" type="date" {...campo('ate')} />
          </Campo>
          <Campo id="tc-prazo-de" rotulo="Previsão de">
            <input id="tc-prazo-de" type="date" {...campo('prazoDe')} />
          </Campo>
          <Campo id="tc-prazo-ate" rotulo="Previsão até">
            <input id="tc-prazo-ate" type="date" {...campo('prazoAte')} />
          </Campo>
          <Campo id="tc-valor-de" rotulo="Valor de (R$)">
            <input id="tc-valor-de" type="number" min="0" step="0.01" placeholder="0,00"
              {...campo('valorDe')} onKeyDown={(e) => { if (e.key === 'Enter') aplicar(); }} />
          </Campo>
          <Campo id="tc-valor-ate" rotulo="Valor até (R$)">
            <input id="tc-valor-ate" type="number" min="0" step="0.01" placeholder="0,00"
              {...campo('valorAte')} onKeyDown={(e) => { if (e.key === 'Enter') aplicar(); }} />
          </Campo>
        </div>
        <div className="mt-3 flex flex-wrap gap-2">
          <button type="button" className="botao" onClick={() => aplicar()}>Aplicar filtros</button>
          <button type="button" className="botao-secundario" onClick={limpar}>Limpar</button>
        </div>
      </Painel>

      <Painel titulo="Itens de compra">
        <p className="sub mb-3">
          Uma linha por <strong>item</strong>, e não por solicitação: itens da mesma SC podem
          estar em etapas diferentes ao mesmo tempo, e é isso que a Torre mostra.
        </p>
        {erro && <Erro>{erro}</Erro>}
        {dados?.capped && (
          <Aviso testid="torre-teto">
            O filtro alcançou o teto de {quantidade(dados.cap)} itens analisados. Estreite o
            período ou o centro de custo para a contagem fechar.
          </Aviso>
        )}
        {/* §5 — a triagem mora aqui: a demanda que chega para o comprador é a etapa
            de Solicitação desta mesma tela, e sair daqui para atribuir e voltar para
            acompanhar era o caminho longo para a mesma coisa */}
        {triando && !!dados?.items.length && (
          <div data-testid="triagem-torre"
            className="mb-3 flex flex-wrap items-end gap-2 rounded-lg border border-borda bg-superficie-suave p-3">
            <Campo id="tc-responsavel" rotulo="Atribuir as SCs marcadas a" className="min-w-[260px]">
              <select id="tc-responsavel" value={paraQuem} onChange={(e) => setParaQuem(e.target.value)}>
                <option value="">Escolha o responsável…</option>
                {(equipe.dados ?? []).map((r) => (
                  <option key={r.id} value={r.id}>{rotuloDoResponsavel(r)}</option>
                ))}
              </select>
            </Campo>
            <button type="button" className="botao" disabled={!paraQuem || !scsMarcadas.length || atribuindo}
              onClick={atribuir}>
              {atribuindo ? 'Atribuindo…' : `Atribuir ${scsMarcadas.length || ''}`.trim()}
            </button>
            {!!scsMarcadas.length && (
              <button type="button" className="botao-secundario" onClick={() => setMarcadas({})}>
                Limpar seleção
              </button>
            )}
            <span className="sub">
              {scsMarcadas.length
                ? `${scsMarcadas.length} solicitação(ões) marcada(s) — a atribuição vale para todos os itens dela.`
                : 'Marque as solicitações na tabela. A atribuição é da SC inteira, não do item.'}
            </span>
          </div>
        )}
        {carregando && !dados && <Carregando texto="Montando a fila…" />}
        {dados && !dados.items.length && <Vazio>Nenhum item de compra neste recorte.</Vazio>}
        {!!dados?.items.length && (
          <>
            <div className="overflow-x-auto" id="lista-da-torre">
              <table data-testid="tabela-torre" className="min-w-[1180px]">
                <thead>
                  <tr>
                    {triando && <th aria-label="Selecionar" />}
                    <th>SC</th><th>Produto</th><th>Qtd.</th><th>Solicitante</th><th>CC</th>
                    <th>Comprador</th><th>Fornecedor</th><th>Etapa</th><th>Situação</th>
                    <th>Previsão</th><th>Valor</th><th>Ação</th><th>Processo</th>
                  </tr>
                </thead>
                <tbody>{dados.items.map((i) => (
                  <Linha key={i.itemId} i={i} triando={triando}
                    marcada={!!marcadas[i.requisitionId]} aoMarcar={alternar}
                    aoLiberar={tirarResponsavel}
                    aoPriorizar={(l) => setPleito({
                      id: l.requisitionId, numero: l.prNumber,
                      para: l.priority === 'URGENT' ? 'NORMAL' : 'URGENT',
                    })} />
                ))}</tbody>
              </table>
            </div>
            <div className="mt-3 flex flex-wrap items-center justify-between gap-2">
              <span className="sub" data-testid="torre-contagem">
                {quantidade(dados.total)} item(ns) · página {dados.page} de {Math.max(1, dados.pages)}
              </span>
              <div className="flex gap-2">
                <button type="button" className="botao-secundario" disabled={dados.page <= 1}
                  onClick={() => irPara(dados.page - 1)}>Anterior</button>
                <button type="button" className="botao-secundario" disabled={dados.page >= dados.pages}
                  onClick={() => irPara(dados.page + 1)}>Próxima</button>
              </div>
            </div>
          </>
        )}
      </Painel>

      {pleito && (
        <DialogoDePrioridade pleito={pleito} aoFechar={() => setPleito(null)}
          aoSalvar={() => setRecarga((n) => n + 1)} aoAvisar={avisar} />
      )}
    </>
  );
}
