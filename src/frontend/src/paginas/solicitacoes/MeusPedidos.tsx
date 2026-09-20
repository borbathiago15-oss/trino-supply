import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { abrirBlob } from '@/api/cliente';
import { diasDesde } from '@/api/cotacoes';
import { listarCentrosCusto, type CentroCusto } from '@/api/centrosCusto';
import { baixarDocumento } from '@/api/documentos';
import {
  atualizarSolicitacao, enviarSolicitacao, excluirSolicitacao, listarSolicitacoes, podeEnviar, podeMexer,
  precisaDoSolicitante, ROTULO_PRIORIDADE, scEncerrada, situacaoDaSc,
  type AcompanhamentoDaSc, type Prioridade, type SolicitacaoCompra,
} from '@/api/solicitacoes';
import { Aviso, Badge, Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { Confirmacao } from '@/componentes/Dialogo';
import { Campo, Grade2 } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { useUsuario } from '@/sessao/SessaoProvider';
import { data, hojeIso, moeda, quantidade } from '@/util/formato';
import { rolarPara } from '@/util/rolar';
import { useCarregar } from '@/util/useCarregar';
import { useDebounce } from '@/util/useDebounce';

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

const POR_PAGINA = 50;

/**
 * Onde a SC está no fluxo, em uma frase — o que o legado mostrava abaixo do
 * resumo. `alerta` marca só o caso em que a frase É o problema da alçada, para
 * a cor de aviso não cair sobre um andamento normal.
 */
export function andamentoDaSc(r: SolicitacaoCompra): { texto: string; alerta: boolean } | null {
  if (r.status === 'SUBMITTED')
    return {
      texto: r.assignedToLabel
        ? `Em cotação com ${r.assignedToLabel} — a aprovação vem depois, com os preços.`
        : 'Com Suprimentos — aguardando a designação do comprador que fará a cotação.',
      alerta: false,
    };
  if (r.status === 'IN_APPROVAL')
    return r.approvalIssue
      ? { texto: r.approvalIssue, alerta: true }
      : { texto: `Aguardando aprovação de: ${r.approverLabel ?? 'Gestor de Suprimentos'}`, alerta: false };
  if (r.status === 'APPROVED')
    return {
      texto: r.assignedToLabel ? `Compra aprovada · comprador: ${r.assignedToLabel}` : 'Compra aprovada na alçada.',
      alerta: false,
    };
  return null;
}

/**
 * Data de necessidade que já passou. A submissão recusa (PR-ERR-050) e a
 * recusa só aparecia no clique em "Enviar" — depois de tudo preenchido.
 */
export const dataNoPassado = (iso: string) => !!iso && iso < hojeIso();

/** "há 3 dias", "hoje" — quanto tempo a etapa atual já espera. */
export function esperaDesde(iso: string | null | undefined, agora: Date = new Date()): string | null {
  const dias = diasDesde(iso, agora);
  if (dias == null) return null;
  return dias === 0 ? 'hoje' : dias === 1 ? 'há 1 dia' : `há ${dias} dias`;
}

const CLASSE_ETAPA: Record<string, string> = {
  feita: 'bg-ok text-white border-ok',
  atual: 'bg-marca text-white border-marca',
  parada: 'bg-perigo text-white border-perigo',
  pendente: 'bg-white text-slate-400 border-borda',
};

/**
 * Seis passos, na língua de quem pediu. A cor diz o que já passou, onde está e onde
 * parou; o texto de cada passo é curto porque a frase abaixo explica o passo atual.
 */
export function LinhaDoTempoDaSc({ a }: { a: AcompanhamentoDaSc }) {
  return (
    <ol data-testid="linha-do-tempo-sc" className="flex flex-wrap items-center gap-x-1 gap-y-1">
      {a.etapas.map((e, i) => (
        <li key={e.chave} data-etapa={e.chave} data-situacao={e.situacao} className="flex items-center gap-1"
          title={e.quando ? `${e.rotulo} · ${data(e.quando.slice(0, 10))}${e.quem ? ` · ${e.quem}` : ''}` : e.rotulo}>
          {i > 0 && <span aria-hidden className={`h-px w-3 ${e.situacao === 'pendente' ? 'bg-borda' : 'bg-slate-400'}`} />}
          <span aria-hidden className={`flex h-4 w-4 items-center justify-center rounded-full border text-[9px] leading-none ${CLASSE_ETAPA[e.situacao] ?? CLASSE_ETAPA.pendente}`}>
            {e.situacao === 'feita' ? '✓' : e.situacao === 'parada' ? '!' : i + 1}
          </span>
          <span className={`text-[11px] ${e.situacao === 'atual' ? 'font-bold text-marca' : e.situacao === 'parada' ? 'font-bold text-perigo' : e.situacao === 'pendente' ? 'text-slate-400' : 'text-texto-suave'}`}>
            {e.rotulo}
          </span>
        </li>
      ))}
    </ol>
  );
}

export const resumoDosItens = (r: SolicitacaoCompra) =>
  r.items.map((i) => `${quantidade(i.quantity)}× ${i.catalogCode ? `[${i.catalogCode}] ` : ''}${i.description}`).join(' · ');

/**
 * A tela "Minhas Solicitações (SC)". O nome do arquivo e da função é o antigo,
 * de quando a tela se chamava "Meus Pedidos" — no D7 "pedido" passou a querer
 * dizer só a O.C., e renomear arquivo não muda nada para quem usa o sistema.
 * O que precisa dizer "solicitação" é o que aparece na tela.
 */
export function MeusPedidos() {
  const usuario = useUsuario();
  const { avisar } = useToast();
  const [editando, setEditando] = useState<SolicitacaoCompra | null>(null);
  const [form, setForm] = useState({
    justificativa: '', centroCusto: '', prioridade: 'NORMAL' as Prioridade, necessidade: '',
    urgenciaMotivo: '', urgenciaImpacto: '', orcamento: '',
  });
  const [salvando, setSalvando] = useState(false);
  const [aExcluir, setAExcluir] = useState<SolicitacaoCompra | null>(null);

  const [busca, setBusca] = useState('');
  const [tamanho, setTamanho] = useState(POR_PAGINA);

  // a busca é do servidor: peneirar no navegador esconderia o que não coube na
  // página, e a tela diria "nada encontrado" para SC que existe
  const termo = useDebounce(busca);
  const { dados, erro, carregando, recarregar } = useCarregar(
    async (signal) => ({
      pagina: await listarSolicitacoes({ busca: termo, tamanho }, signal),
      centros: await listarCentrosCusto(false, signal).catch(() => [] as CentroCusto[]),
    }),
    [termo, tamanho],
  );

  const lista = dados?.pagina.itens ?? [];
  const total = dados?.pagina.total ?? 0;
  const centros = dados?.centros ?? [];
  /**
   * O centro da SC pode não estar mais entre os ativos (inativado depois, ou
   * fora dos centros vinculados a quem edita). Sem ele na lista, o campo
   * obrigatório ficaria vazio e travaria o envio sem dizer por quê.
   */
  const centroForaDaLista = !!editando && !!form.centroCusto && !centros.some((c) => c.code === form.centroCusto);

  function editar(r: SolicitacaoCompra) {
    setEditando(r);
    setForm({
      justificativa: r.justification, centroCusto: r.costCenter, prioridade: r.priority,
      necessidade: r.neededBy ?? '', urgenciaMotivo: r.urgencyReason ?? '', urgenciaImpacto: r.urgencyImpact ?? '',
      orcamento: r.budget != null ? String(r.budget) : '',
    });
    rolarPara('form-sc');
  }
  const cancelarEdicao = () => setEditando(null);

  async function salvar(ev: FormEvent) {
    ev.preventDefault();
    if (!editando) return;
    setSalvando(true);
    try {
      await atualizarSolicitacao(editando.id, {
        justification: form.justificativa,
        costCenter: form.centroCusto,
        priority: form.prioridade,
        neededBy: form.necessidade || null,
        clearNeededBy: !form.necessidade,
        urgencyReason: form.urgenciaMotivo || null,
        urgencyImpact: form.urgenciaImpacto || null,
        budget: Number(form.orcamento) > 0 ? Number(form.orcamento) : null,
        clearBudget: !form.orcamento,
      });
      avisar('Solicitação atualizada.');
      setEditando(null);
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao salvar a solicitação.'), 'erro'); }
    finally { setSalvando(false); }
  }

  async function enviar(r: SolicitacaoCompra) {
    try {
      await enviarSolicitacao(r.id);
      avisar(`SC ${r.number} enviada. Suprimentos vai designar o comprador da cotação.`);
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao enviar a solicitação.'), 'erro'); }
  }

  async function excluir(r: SolicitacaoCompra) {
    setAExcluir(null);
    try {
      await excluirSolicitacao(r.id);
      avisar('Rascunho excluído.');
      if (editando?.id === r.id) setEditando(null);
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao excluir.'), 'erro'); }
  }

  async function abrirAnexo(documentId: string) {
    try { abrirBlob(await baixarDocumento(documentId)); }
    catch (e) { avisar(mensagem(e, 'Falha ao baixar o anexo.'), 'erro'); }
  }

  const campo = (k: keyof typeof form) => ({
    value: form[k] as string,
    onChange: (ev: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: ev.target.value })),
  });

  // o resumo do topo é da página carregada: diz o que precisa de quem está olhando
  const minhas = lista.filter((r) => r.requesterId === usuario.id);
  const comigo = minhas.filter(precisaDoSolicitante).length;
  const encerradas = minhas.filter(scEncerrada).length;
  const andando = minhas.length - comigo - encerradas;

  return (
    <>
      <Painel titulo="Minhas Solicitações de Compra (SC)" acoes={
        <>
          <input aria-label="Buscar" placeholder="Buscar por número, item, justificativa ou CC"
            className="!w-[320px]" value={busca}
            onChange={(e) => { setTamanho(POR_PAGINA); setBusca(e.target.value); }} />
          <Link to="/solicitacoes/nova" className="botao">+ Nova solicitação</Link>
        </>
      }>
        {erro && <Erro>{erro}</Erro>}
        {carregando && !dados && <Carregando />}
        {dados && !lista.length && (
          <Vazio>
            {termo.trim()
              ? 'Nenhuma solicitação encontrada para esta busca.'
              : <>Nenhuma SC ainda. <Link className="font-semibold text-marca underline" to="/solicitacoes/nova">Crie a primeira solicitação</Link> — ela nasce como rascunho e você envia quando estiver pronta.</>}
          </Vazio>
        )}
        {minhas.length > 0 && !termo.trim() && (
          <div className="mb-3 flex flex-wrap gap-2 text-[12.5px]" data-testid="resumo-solicitacoes">
            <Badge classe={comigo > 0 ? 'bg-aviso-fundo text-aviso' : 'bg-slate-100 text-slate-600'}>
              {comigo === 0 ? 'nada esperando por você' : `${comigo} esperando por você`}
            </Badge>
            <Badge classe="bg-blue-50 text-blue-800">{andando} em andamento</Badge>
            <Badge classe="bg-slate-100 text-slate-600">{encerradas} encerrada(s)</Badge>
          </div>
        )}
        {lista.length > 0 && (
          <div className="overflow-x-auto">
            <table data-testid="tabela-solicitacoes" className="min-w-[980px]">
              <thead>
                <tr><th>Número</th><th>O que</th><th>Valor est.</th><th>Onde está</th><th>Ações</th></tr>
              </thead>
              <tbody>
                {lista.map((r) => {
                  const marca = situacaoDaSc(r);
                  const andamento = andamentoDaSc(r);
                  const a = r.acompanhamento;
                  const minha = r.requesterId === usuario.id;
                  const devolvida = r.status === 'RETURNED';
                  return (
                    <tr key={r.id} data-solicitacao={r.number} data-etapa={a?.etapaAtual}>
                      <td className="whitespace-nowrap">
                        <span className="font-semibold">{r.number}</span>
                        <div className="sub">ciclo {r.cycle} · {r.kind === 'CATALOGO' ? 'lote' : 'SC'}</div>
                      </td>
                      <td className="min-w-[320px]">
                        {r.justification}
                        <div className="sub">{resumoDosItens(r)}</div>
                        <div className="sub">
                          CC: {r.costCenter}
                          {r.neededBy && ` · até ${data(r.neededBy)}`}
                          {r.priority === 'URGENT' && ` · ${ROTULO_PRIORIDADE.URGENT}`}
                          {r.budget != null && ` · orçamento ${moeda(r.budget)}`}
                        </div>
                        {r.attachments.length > 0 && (
                          <div className="sub">
                            📎 {r.attachments.map((a, i) => (
                              <span key={a.id}>
                                {i > 0 && ' · '}
                                <button type="button" className="text-marca underline" onClick={() => abrirAnexo(a.documentId)}>
                                  {a.fileName}
                                </button>
                              </span>
                            ))}
                          </div>
                        )}
                      </td>
                      <td className="whitespace-nowrap">{moeda(r.totalEstimatedValue)}</td>
                      <td className="min-w-[300px]" data-testid="onde-esta">
                        <Badge classe={marca.classe}>{marca.rotulo}</Badge>
                        {a ? (
                          <div className="mt-1.5">
                            <LinhaDoTempoDaSc a={a} />
                            <div className={`mt-1 text-[12.5px] ${a.motivo ? 'font-semibold text-perigo' : ''}`}>
                              {a.frase}
                              {esperaDesde(a.desde) && !a.motivo && a.etapas.some((e) => e.situacao === 'atual') && (
                                <span className="sub"> · {esperaDesde(a.desde)}</span>
                              )}
                            </div>
                            {a.motivo && <div className="text-[12.5px]">Motivo: {a.motivo}</div>}
                            <div className="sub">
                              {a.previsao && <span>previsão de chegada {data(a.previsao)}</span>}
                              {a.previsao && a.fornecedor && ' · '}
                              {a.fornecedor && <span>fornecedor {a.fornecedor}</span>}
                              {(a.previsao || a.fornecedor) && a.purchaseOrderNumber && ' · '}
                              {a.purchaseOrderNumber && <span>pedido {a.purchaseOrderNumber}</span>}
                            </div>
                          </div>
                        ) : (
                          <>
                            {andamento && (
                              <div className={andamento.alerta ? 'mt-1 text-[12px] text-perigo' : 'sub mt-1'}>{andamento.texto}</div>
                            )}
                            {r.decisionReason && (
                              <div className="sub">Motivo: {r.decisionReason}{r.decidedByLabel ? ` (${r.decidedByLabel})` : ''}</div>
                            )}
                          </>
                        )}
                      </td>
                      <td className="whitespace-nowrap">
                        {minha && (
                          <div className="flex gap-1.5">
                            {podeEnviar(r) && (
                              <button type="button" className="botao" onClick={() => enviar(r)}>
                                {devolvida ? 'Reenviar' : 'Enviar solicitação'}
                              </button>
                            )}
                            {podeMexer(r) && (
                              <>
                                <button type="button" className="botao-secundario" onClick={() => editar(r)}>
                                  {devolvida ? 'Corrigir' : 'Editar'}
                                </button>
                                <button type="button" className="botao-perigo" onClick={() => setAExcluir(r)}>Excluir</button>
                              </>
                            )}
                          </div>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
        {lista.length > 0 && (
          <div className="mt-3 flex items-center justify-between gap-3">
            <span className="sub" data-testid="contagem-solicitacoes">
              Mostrando {lista.length} de {total} solicitação(ões).
            </span>
            {lista.length < total && (
              <button type="button" className="botao-secundario" disabled={carregando}
                onClick={() => setTamanho((t) => t + POR_PAGINA)}>
                {carregando ? 'Carregando…' : 'Carregar mais'}
              </button>
            )}
          </div>
        )}
      </Painel>

      {editando && (
        <Painel id="form-sc" titulo={`Editar a SC ${editando.number}`}
          acoes={<button type="button" className="botao-secundario" onClick={cancelarEdicao}>Fechar</button>}>
          <form onSubmit={salvar}>
            <Campo id="sc-edit-justificativa" rotulo="Justificativa">
              <input id="sc-edit-justificativa" required minLength={3} {...campo('justificativa')} />
            </Campo>
            <Grade2 className="mt-3">
              <Campo id="sc-edit-cc" rotulo="Centro de Custo">
                <select id="sc-edit-cc" required {...campo('centroCusto')}>
                  <option value="">Selecione o centro de custo…</option>
                  {centroForaDaLista && (
                    <option value={form.centroCusto}>{form.centroCusto} (fora da sua lista)</option>
                  )}
                  {centros.map((c) => (
                    <option key={c.id} value={c.code}>{c.code} — {c.name}</option>
                  ))}
                </select>
              </Campo>
              <Campo id="sc-edit-prioridade" rotulo="Prioridade">
                <select id="sc-edit-prioridade" {...campo('prioridade')}>
                  {(Object.keys(ROTULO_PRIORIDADE) as Prioridade[]).map((p) => (
                    <option key={p} value={p}>{ROTULO_PRIORIDADE[p]}</option>
                  ))}
                </select>
              </Campo>
            </Grade2>
            {/*
              Aqui a data não é travada e sim avisada: o rascunho pode ter nascido com uma data
              que já passou, e travar o campo impediria de salvar qualquer outra correção. O
              envio é que recusa (PR-ERR-050) — e é desta tela que se envia.
            */}
            <Campo id="sc-edit-necessidade" rotulo="Data de necessidade" className="mt-3">
              <input id="sc-edit-necessidade" type="date" {...campo('necessidade')} />
            </Campo>
            {/* §17: informar o orçamento aqui dá ao comprador a régua contra a qual o
                saving do fechamento vai ser medido */}
            <Campo id="sc-edit-orcamento" rotulo="Orçamento previsto (R$)"
              dica="(opcional — fechar abaixo dele vira saving)" className="mt-3">
              <input id="sc-edit-orcamento" type="number" min="0" step="0.01" {...campo('orcamento')} />
            </Campo>
            {dataNoPassado(form.necessidade) && (
              <Aviso testid="data-vencida">
                Esta data já passou: com ela o envio da SC é recusado (PR-ERR-050). Ajuste antes de enviar.
              </Aviso>
            )}
            {form.prioridade === 'URGENT' && (
              <Grade2 className="mt-3">
                <Campo id="sc-edit-urg-motivo" rotulo="Justificativa da urgência">
                  <input id="sc-edit-urg-motivo" {...campo('urgenciaMotivo')} />
                </Campo>
                <Campo id="sc-edit-urg-impacto" rotulo="Impacto se não comprar">
                  <input id="sc-edit-urg-impacto" {...campo('urgenciaImpacto')} />
                </Campo>
              </Grade2>
            )}
            <div className="mt-4 flex flex-wrap gap-2">
              <button type="submit" className="botao" disabled={salvando}>{salvando ? 'Salvando…' : 'Salvar alterações'}</button>
              <button type="button" className="botao-secundario" onClick={cancelarEdicao}>Cancelar</button>
            </div>
          </form>
        </Painel>
      )}

      {aExcluir && (
        <Confirmacao titulo="Excluir solicitação" perigo rotuloConfirmar="Excluir"
          mensagem={<>Excluir a solicitação <strong>{aExcluir.number}</strong>? Isso não pode ser desfeito.</>}
          aoConfirmar={() => excluir(aExcluir)} aoFechar={() => setAExcluir(null)} />
      )}
    </>
  );
}
