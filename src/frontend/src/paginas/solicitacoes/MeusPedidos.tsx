import { useState, type FormEvent } from 'react';
import { abrirBlob } from '@/api/cliente';
import { listarCentrosCusto, type CentroCusto } from '@/api/centrosCusto';
import { baixarDocumento } from '@/api/documentos';
import {
  atualizarSolicitacao, enviarSolicitacao, excluirSolicitacao, listarSolicitacoes, podeEnviar, podeMexer,
  ROTULO_PRIORIDADE, situacaoDaSc, type Prioridade, type SolicitacaoCompra,
} from '@/api/solicitacoes';
import { Badge, Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { Confirmacao } from '@/componentes/Dialogo';
import { Campo, Grade2 } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { useUsuario } from '@/sessao/SessaoProvider';
import { data, moeda, quantidade } from '@/util/formato';
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

export const resumoDosItens = (r: SolicitacaoCompra) =>
  r.items.map((i) => `${quantidade(i.quantity)}× ${i.catalogCode ? `[${i.catalogCode}] ` : ''}${i.description}`).join(' · ');

export function MeusPedidos() {
  const usuario = useUsuario();
  const { avisar } = useToast();
  const [editando, setEditando] = useState<SolicitacaoCompra | null>(null);
  const [form, setForm] = useState({
    justificativa: '', centroCusto: '', prioridade: 'NORMAL' as Prioridade, necessidade: '',
    urgenciaMotivo: '', urgenciaImpacto: '',
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
      });
      avisar('Pedido atualizado.');
      setEditando(null);
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao salvar o pedido.'), 'erro'); }
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

  return (
    <>
      <Painel titulo="Meus Pedidos de Compra" acoes={
        <input aria-label="Buscar" placeholder="Buscar por número, item, justificativa ou CC"
          className="!w-[320px]" value={busca}
          onChange={(e) => { setTamanho(POR_PAGINA); setBusca(e.target.value); }} />
      }>
        {erro && <Erro>{erro}</Erro>}
        {carregando && !dados && <Carregando />}
        {dados && !lista.length && (
          <Vazio>
            {termo.trim()
              ? 'Nenhum pedido encontrado para esta busca.'
              : 'Nenhum pedido ainda. Crie pelo menu “Inclusão de SC” ou “Solicitação em Lote”.'}
          </Vazio>
        )}
        {lista.length > 0 && (
          <div className="overflow-x-auto">
            <table data-testid="tabela-solicitacoes" className="min-w-[980px]">
              <thead>
                <tr><th>Número</th><th>Resumo</th><th>Valor est.</th><th>Situação</th><th>Ações</th></tr>
              </thead>
              <tbody>
                {lista.map((r) => {
                  const marca = situacaoDaSc(r);
                  const andamento = andamentoDaSc(r);
                  const minha = r.requesterId === usuario.id;
                  return (
                    <tr key={r.id} data-solicitacao={r.number}>
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
                        {andamento && (
                          <div className={andamento.alerta ? 'text-[12px] text-perigo' : 'sub'}>{andamento.texto}</div>
                        )}
                        {r.decisionReason && (
                          <div className="sub">Motivo: {r.decisionReason}{r.decidedByLabel ? ` (${r.decidedByLabel})` : ''}</div>
                        )}
                      </td>
                      <td className="whitespace-nowrap">{moeda(r.totalEstimatedValue)}</td>
                      <td><Badge classe={marca.classe}>{marca.rotulo}</Badge></td>
                      <td className="whitespace-nowrap">
                        {minha && (
                          <div className="flex gap-1.5">
                            {podeEnviar(r) && (
                              <button type="button" className="botao" onClick={() => enviar(r)}>Enviar solicitação</button>
                            )}
                            {podeMexer(r) && (
                              <>
                                <button type="button" className="botao-secundario" onClick={() => editar(r)}>Editar</button>
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
              Mostrando {lista.length} de {total} pedido(s).
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
        <Painel id="form-sc" titulo={`Editar pedido ${editando.number}`}
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
            <Campo id="sc-edit-necessidade" rotulo="Data de necessidade" className="mt-3">
              <input id="sc-edit-necessidade" type="date" {...campo('necessidade')} />
            </Campo>
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
