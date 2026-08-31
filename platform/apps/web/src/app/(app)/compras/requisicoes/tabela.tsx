'use client';

import { useState, useTransition } from 'react';
import { useRouter } from 'next/navigation';
import type { LeituraFarol, Requisicao, RequisicaoDetalhada } from '@trino/contratos';
import { FarolSla } from '@/componentes/farol-sla';
import { Modal } from '@/componentes/modal';
import { data, dataHora, dinheiro, EtiquetaPrioridade, EtiquetaStatus } from '@/componentes/etiquetas';
import {
  assumirTriagem,
  detalharRequisicao,
  devolverParaAjuste,
  historicoDaRequisicao,
  type ResultadoAcao,
} from './acoes';

export interface LinhaEsteira {
  requisicao: Requisicao;
  farol: LeituraFarol;
  centroCusto: string;
}

interface Historico {
  acao: string;
  timestampUtc: string;
  antes: string | null;
  depois: string | null;
}

/**
 * Tabela da esteira. O farol já vem calculado do servidor (tempo útil, com
 * feriados e pausas), então a linha não faz conta nem pede nada ao abrir.
 * Clicar abre a gaveta com itens e histórico — que só então são buscados.
 */
export function TabelaEsteira({ linhas }: { linhas: LinhaEsteira[] }) {
  const router = useRouter();
  const [selecionada, setSelecionada] = useState<RequisicaoDetalhada | null>(null);
  const [historico, setHistorico] = useState<Historico[]>([]);
  const [carregando, setCarregando] = useState(false);
  const [motivo, setMotivo] = useState('');
  const [aviso, setAviso] = useState<ResultadoAcao | null>(null);
  const [pendente, iniciar] = useTransition();

  const abrir = async (id: string) => {
    setCarregando(true);
    setAviso(null);
    try {
      const [detalhe, trilha] = await Promise.all([detalharRequisicao(id), historicoDaRequisicao(id)]);
      setSelecionada(detalhe);
      setHistorico(trilha);
    } finally {
      setCarregando(false);
    }
  };

  const executar = (acao: () => Promise<ResultadoAcao>) => {
    iniciar(async () => {
      const resultado = await acao();
      setAviso(resultado);
      if (resultado.ok) {
        setSelecionada(null);
        setMotivo('');
        router.refresh();
      }
    });
  };

  return (
    <>
      <div className="card overflow-hidden">
        <table className="min-w-full divide-y divide-slate-200">
          <thead className="bg-slate-50">
            <tr>
              <th className="cabecalho-tabela">Número</th>
              <th className="cabecalho-tabela">Situação</th>
              <th className="cabecalho-tabela">Prioridade</th>
              <th className="cabecalho-tabela">Centro de custo</th>
              <th className="cabecalho-tabela">Valor</th>
              <th className="cabecalho-tabela">SLA</th>
              <th className="cabecalho-tabela">Criada em</th>
              <th className="cabecalho-tabela sr-only">Ações</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100 bg-white">
            {linhas.length === 0 ? (
              <tr>
                <td colSpan={8} className="px-4 py-10 text-center text-sm text-slate-500">
                  Nenhuma requisição encontrada com esses filtros.
                </td>
              </tr>
            ) : null}
            {linhas.map(({ requisicao, farol, centroCusto }) => (
              <tr key={requisicao.id} className="hover:bg-slate-50">
                <td className="celula font-medium text-slate-800">
                  {requisicao.numero}
                  {requisicao.orcamentoEstourado ? (
                    <span
                      className="etiqueta ml-2 bg-red-100 text-red-800"
                      title="Valor acima do saldo do centro de custo — segue para autorização do aprovador final"
                    >
                      orçamento estourado
                    </span>
                  ) : null}
                </td>
                <td className="celula"><EtiquetaStatus status={requisicao.status} /></td>
                <td className="celula"><EtiquetaPrioridade prioridade={requisicao.prioridade} /></td>
                <td className="celula text-slate-600">{centroCusto}</td>
                <td className="celula text-slate-800">{dinheiro(requisicao.valorEstimado)}</td>
                <td className="celula"><FarolSla leitura={farol} /></td>
                <td className="celula text-slate-500">{data(requisicao.criadoEm)}</td>
                <td className="celula text-right">
                  <button type="button" className="text-sm text-trino-700 hover:underline" onClick={() => abrir(requisicao.id)}>
                    Ver
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <Modal
        lado
        aberto={selecionada !== null}
        titulo={selecionada ? `Requisição ${selecionada.numero}` : ''}
        aoFechar={() => {
          setSelecionada(null);
          setAviso(null);
        }}
        rodape={
          selecionada ? (
            <div className="space-y-3">
              {aviso ? (
                <p
                  role="status"
                  className={`rounded-md px-3 py-2 text-sm ${
                    aviso.ok ? 'bg-green-50 text-green-800' : 'bg-red-50 text-red-800'
                  }`}
                >
                  {aviso.mensagem}
                </p>
              ) : null}

              {selecionada.transicoesDisponiveis.includes('T05_ASSUMIR_TRIAGEM') ? (
                <button
                  type="button"
                  className="botao-primario w-full"
                  disabled={pendente}
                  onClick={() => executar(() => assumirTriagem(selecionada.id, selecionada.version))}
                >
                  Assumir triagem
                </button>
              ) : null}

              {selecionada.transicoesDisponiveis.includes('T06_DEVOLVER_AJUSTE') ? (
                <div className="space-y-2">
                  <label className="rotulo" htmlFor="motivo-devolucao">
                    Motivo da devolução (obrigatório)
                  </label>
                  <textarea
                    id="motivo-devolucao"
                    className="campo"
                    rows={2}
                    value={motivo}
                    onChange={(e) => setMotivo(e.target.value)}
                  />
                  <button
                    type="button"
                    className="botao-secundario w-full"
                    disabled={pendente || motivo.trim().length < 3}
                    onClick={() => executar(() => devolverParaAjuste(selecionada.id, selecionada.version, motivo))}
                  >
                    Devolver para ajuste
                  </button>
                </div>
              ) : null}
            </div>
          ) : null
        }
      >
        {carregando || !selecionada ? (
          <p className="text-sm text-slate-500">Carregando…</p>
        ) : (
          <div className="space-y-6">
            <section className="grid grid-cols-2 gap-4 text-sm">
              <div>
                <span className="rotulo">Situação</span>
                <div className="mt-1"><EtiquetaStatus status={selecionada.status} /></div>
              </div>
              <div>
                <span className="rotulo">Prioridade</span>
                <div className="mt-1"><EtiquetaPrioridade prioridade={selecionada.prioridade} /></div>
              </div>
              <div>
                <span className="rotulo">Valor estimado</span>
                <p className="mt-1">{dinheiro(selecionada.valorEstimado)}</p>
              </div>
              <div>
                <span className="rotulo">Necessidade</span>
                <p className="mt-1">{data(selecionada.dataNecessidade)}</p>
              </div>
              <div className="col-span-2">
                <span className="rotulo">Justificativa</span>
                <p className="mt-1 text-slate-700">{selecionada.justificativa ?? '—'}</p>
              </div>
              {selecionada.motivoRecusa ? (
                <div className="col-span-2">
                  <span className="rotulo">Motivo registrado</span>
                  <p className="mt-1 text-slate-700">{selecionada.motivoRecusa}</p>
                </div>
              ) : null}
            </section>

            {selecionada.orcamentoEstourado && selecionada.orcamentoSnapshot ? (
              <section className="rounded-md border border-red-200 bg-red-50 p-3 text-sm">
                <p className="font-medium text-red-800">Orçamento estourado</p>
                <p className="mt-1 text-red-700">
                  {selecionada.orcamentoSnapshot.motivo === 'SEM_ORCAMENTO'
                    ? `Centro de custo sem orçamento definido para ${selecionada.orcamentoSnapshot.exercicio}.`
                    : `Saldo de ${dinheiro(selecionada.orcamentoSnapshot.saldo ?? 0)} e excedente de ${dinheiro(
                        selecionada.orcamentoSnapshot.excedente ?? 0,
                      )}.`}{' '}
                  Segue para autorização do aprovador final.
                </p>
              </section>
            ) : null}

            <section>
              <h3 className="mb-2 text-sm font-semibold text-slate-700">Itens</h3>
              <table className="min-w-full divide-y divide-slate-200 text-sm">
                <thead>
                  <tr>
                    <th className="cabecalho-tabela">#</th>
                    <th className="cabecalho-tabela">Quantidade</th>
                    <th className="cabecalho-tabela">Preço ref.</th>
                    <th className="cabecalho-tabela">Situação</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {selecionada.itens.map((item) => (
                    <tr key={item.id}>
                      <td className="celula">{item.sequencia}</td>
                      <td className="celula">{Number(item.quantidade)}</td>
                      <td className="celula">{dinheiro(item.precoReferencia)}</td>
                      <td className="celula text-slate-500">{item.status}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </section>

            <section>
              <h3 className="mb-2 text-sm font-semibold text-slate-700">Histórico da esteira</h3>
              {historico.length === 0 ? (
                <p className="text-sm text-slate-500">Sem transições registradas.</p>
              ) : (
                <ol className="space-y-2">
                  {historico.map((evento, indice) => (
                    <li key={`${evento.acao}-${indice}`} className="flex gap-3 text-sm">
                      <span className="mt-1.5 h-2 w-2 shrink-0 rounded-full bg-trino-600" aria-hidden />
                      <div>
                        <p className="font-medium text-slate-700">{evento.acao}</p>
                        <p className="text-slate-500">
                          {dataHora(evento.timestampUtc)}
                          {evento.antes && evento.depois && evento.antes !== evento.depois
                            ? ` · ${evento.antes} → ${evento.depois}`
                            : null}
                        </p>
                      </div>
                    </li>
                  ))}
                </ol>
              )}
            </section>
          </div>
        )}
      </Modal>
    </>
  );
}
