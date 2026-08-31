'use client';

import { useState, useTransition } from 'react';
import { useRouter } from 'next/navigation';
import type { LinhaRecebimentoBody, OcorrenciaRecebimento, Pedido, PedidoDetalhado } from '@trino/contratos';
import { OCORRENCIAS_RECEBIMENTO, ROTULO_OCORRENCIA, validarChaveNfe } from '@trino/contratos';
import { dinheiro } from '@/componentes/etiquetas';
import { carregarPedido, registrarRecebimento, type ResultadoRecebimento } from './acoes';

interface LinhaConferencia {
  itemPedidoId: string;
  sequencia: number;
  qtdPedida: number;
  qtdJaRecebida: number;
  precoUnitario: number;
  qtdRecebida: string;
  qtdAvariada: string;
  ocorrencia: OcorrenciaRecebimento;
  descricaoOcorrencia: string;
}

const numero = (v: string) => {
  const n = Number(String(v).replace(',', '.'));
  return Number.isFinite(n) ? n : 0;
};

/**
 * Conferência de entrada. A chave da NF-e é validada aqui com a MESMA regra do
 * banco (44 dígitos), e a tabela mostra pedido × já recebido × recebendo
 * agora — que é a conta que o conferente faz de cabeça no pátio.
 */
export function Conferencia({ pedidos }: { pedidos: Pedido[] }) {
  const router = useRouter();
  const [pedido, setPedido] = useState<PedidoDetalhado | null>(null);
  const [linhas, setLinhas] = useState<LinhaConferencia[]>([]);
  const [chaveNfe, setChaveNfe] = useState('');
  const [numeroNf, setNumeroNf] = useState('');
  const [serieNf, setSerieNf] = useState('');
  const [valorNf, setValorNf] = useState('');
  const [emissaoNf, setEmissaoNf] = useState('');
  const [observacao, setObservacao] = useState('');
  const [resultado, setResultado] = useState<ResultadoRecebimento | null>(null);
  const [pendente, iniciar] = useTransition();

  const chaveLimpa = chaveNfe.replace(/\D/g, '');
  const chaveInformada = chaveLimpa.length > 0;
  const chaveValida = validarChaveNfe(chaveLimpa);

  const selecionar = async (id: string) => {
    setResultado(null);
    if (!id) {
      setPedido(null);
      setLinhas([]);
      return;
    }
    const detalhe = await carregarPedido(id);
    setPedido(detalhe);
    setLinhas(
      detalhe.itens.map((item) => ({
        itemPedidoId: item.id,
        sequencia: item.sequencia,
        qtdPedida: Number(item.qtdPedida),
        qtdJaRecebida: Number(item.qtdRecebida),
        precoUnitario: Number(item.precoUnitario),
        qtdRecebida: '',
        qtdAvariada: '',
        ocorrencia: 'SEM_OCORRENCIA',
        descricaoOcorrencia: '',
      })),
    );
  };

  const atualizarLinha = (id: string, campo: keyof LinhaConferencia, valor: string) => {
    setLinhas((atuais) =>
      atuais.map((linha) => (linha.itemPedidoId === id ? { ...linha, [campo]: valor } : linha)),
    );
  };

  const linhasPreenchidas = linhas.filter((l) => numero(l.qtdRecebida) > 0);
  const totalRecebendo = linhasPreenchidas.reduce((s, l) => s + numero(l.qtdRecebida) * l.precoUnitario, 0);

  const erros: string[] = [];
  if (chaveInformada && !chaveValida) erros.push('A chave da NF-e precisa ter exatamente 44 dígitos.');
  if (chaveValida && (!numeroNf || !serieNf || !emissaoNf)) {
    erros.push('Com a chave informada, preencha número, série e data de emissão da nota.');
  }
  for (const linha of linhasPreenchidas) {
    if (numero(linha.qtdAvariada) > numero(linha.qtdRecebida)) {
      erros.push(`Item ${linha.sequencia}: avaria maior que a quantidade recebida.`);
    }
    if (linha.ocorrencia !== 'SEM_OCORRENCIA' && linha.descricaoOcorrencia.trim().length < 3) {
      erros.push(`Item ${linha.sequencia}: descreva a ocorrência registrada.`);
    }
  }

  const podeEnviar = !pendente && pedido !== null && linhasPreenchidas.length > 0 && erros.length === 0;

  const enviar = () => {
    if (!pedido) return;
    const itens: LinhaRecebimentoBody[] = linhasPreenchidas.map((linha) => ({
      itemPedidoId: linha.itemPedidoId,
      qtdRecebida: numero(linha.qtdRecebida),
      ...(numero(linha.qtdAvariada) > 0 ? { qtdAvariada: numero(linha.qtdAvariada) } : {}),
      ...(linha.ocorrencia !== 'SEM_OCORRENCIA' ? { ocorrencia: linha.ocorrencia } : {}),
      ...(linha.descricaoOcorrencia.trim() ? { descricaoOcorrencia: linha.descricaoOcorrencia.trim() } : {}),
    }));

    iniciar(async () => {
      const resposta = await registrarRecebimento(pedido.id, {
        itens,
        ...(chaveValida
          ? {
              notaFiscal: {
                chaveAcesso: chaveLimpa,
                numero: numeroNf,
                serie: serieNf,
                valorTotal: numero(valorNf),
                dataEmissao: emissaoNf,
              },
            }
          : {}),
        ...(observacao.trim() ? { observacao: observacao.trim() } : {}),
      });
      setResultado(resposta);
      if (resposta.ok) {
        await selecionar(pedido.id);
        router.refresh();
      }
    });
  };

  return (
    <div className="space-y-6">
      <div className="card p-5">
        <label className="text-sm">
          <span className="rotulo">Pedido de compra</span>
          <select className="campo mt-1 w-full max-w-md" value={pedido?.id ?? ''} onChange={(e) => selecionar(e.target.value)}>
            <option value="">Selecione o pedido a conferir</option>
            {pedidos.map((p) => (
              <option key={p.id} value={p.id}>
                {p.numero} — {dinheiro(p.valorTotal)} ({p.status})
              </option>
            ))}
          </select>
        </label>
      </div>

      {pedido ? (
        <>
          <section className="card space-y-4 p-5">
            <h2 className="text-sm font-semibold text-slate-700">Nota fiscal (opcional, mas exigida para o 3-way)</h2>
            <div className="grid gap-4 md:grid-cols-2">
              <label className="md:col-span-2">
                <span className="rotulo">Chave de acesso da NF-e (44 dígitos)</span>
                <input
                  className={`campo mt-1 font-mono ${chaveInformada && !chaveValida ? 'border-red-400' : ''}`}
                  value={chaveNfe}
                  onChange={(e) => setChaveNfe(e.target.value)}
                  inputMode="numeric"
                  placeholder="00000000000000000000000000000000000000000000"
                  aria-invalid={chaveInformada && !chaveValida}
                />
                <span className={`mt-1 block text-xs ${chaveInformada && !chaveValida ? 'text-red-700' : 'text-slate-500'}`}>
                  {chaveLimpa.length}/44 dígitos
                </span>
              </label>
              <label>
                <span className="rotulo">Número</span>
                <input className="campo mt-1" value={numeroNf} onChange={(e) => setNumeroNf(e.target.value)} />
              </label>
              <label>
                <span className="rotulo">Série</span>
                <input className="campo mt-1" value={serieNf} onChange={(e) => setSerieNf(e.target.value)} />
              </label>
              <label>
                <span className="rotulo">Valor total da nota</span>
                <input className="campo mt-1" value={valorNf} onChange={(e) => setValorNf(e.target.value)} inputMode="decimal" />
              </label>
              <label>
                <span className="rotulo">Data de emissão</span>
                <input type="date" className="campo mt-1" value={emissaoNf} onChange={(e) => setEmissaoNf(e.target.value)} />
              </label>
            </div>
          </section>

          <section className="card overflow-x-auto">
            <table className="min-w-full divide-y divide-slate-200">
              <thead className="bg-slate-50">
                <tr>
                  <th className="cabecalho-tabela">#</th>
                  <th className="cabecalho-tabela">Pedido</th>
                  <th className="cabecalho-tabela">Já recebido</th>
                  <th className="cabecalho-tabela">Recebendo agora</th>
                  <th className="cabecalho-tabela">Avariado</th>
                  <th className="cabecalho-tabela">Ocorrência</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100 bg-white">
                {linhas.map((linha) => {
                  const saldo = linha.qtdPedida - linha.qtdJaRecebida;
                  return (
                    <tr key={linha.itemPedidoId}>
                      <td className="celula">{linha.sequencia}</td>
                      <td className="celula">{linha.qtdPedida}</td>
                      <td className="celula text-slate-500">
                        {linha.qtdJaRecebida} <span className="text-xs">(faltam {saldo > 0 ? saldo : 0})</span>
                      </td>
                      <td className="celula">
                        <input
                          className="campo w-28"
                          value={linha.qtdRecebida}
                          onChange={(e) => atualizarLinha(linha.itemPedidoId, 'qtdRecebida', e.target.value)}
                          inputMode="decimal"
                          aria-label={`Quantidade recebida do item ${linha.sequencia}`}
                        />
                      </td>
                      <td className="celula">
                        <input
                          className="campo w-24"
                          value={linha.qtdAvariada}
                          onChange={(e) => atualizarLinha(linha.itemPedidoId, 'qtdAvariada', e.target.value)}
                          inputMode="decimal"
                          aria-label={`Quantidade avariada do item ${linha.sequencia}`}
                        />
                      </td>
                      <td className="celula">
                        <div className="flex flex-col gap-1">
                          <select
                            className="campo w-56"
                            value={linha.ocorrencia}
                            onChange={(e) => atualizarLinha(linha.itemPedidoId, 'ocorrencia', e.target.value)}
                            aria-label={`Ocorrência do item ${linha.sequencia}`}
                          >
                            {OCORRENCIAS_RECEBIMENTO.map((o) => (
                              <option key={o} value={o}>
                                {ROTULO_OCORRENCIA[o]}
                              </option>
                            ))}
                          </select>
                          {linha.ocorrencia !== 'SEM_OCORRENCIA' ? (
                            <input
                              className="campo w-56"
                              placeholder="Descreva a ocorrência"
                              value={linha.descricaoOcorrencia}
                              onChange={(e) => atualizarLinha(linha.itemPedidoId, 'descricaoOcorrencia', e.target.value)}
                            />
                          ) : null}
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </section>

          <section className="card space-y-4 p-5">
            <label>
              <span className="rotulo">Observação do recebimento</span>
              <textarea className="campo mt-1" rows={2} value={observacao} onChange={(e) => setObservacao(e.target.value)} />
            </label>

            <p className="text-sm text-slate-600">
              Conferindo {linhasPreenchidas.length} item(ns) · {dinheiro(totalRecebendo)} a preço de pedido
              {chaveValida && valorNf ? ` · nota de ${dinheiro(numero(valorNf))}` : ''}
            </p>

            {erros.length > 0 ? (
              <ul className="space-y-1 rounded-md bg-amber-50 px-3 py-2 text-sm text-amber-900">
                {erros.map((erro) => (
                  <li key={erro}>• {erro}</li>
                ))}
              </ul>
            ) : null}

            {resultado ? (
              <div
                role="status"
                className={`space-y-2 rounded-md px-3 py-2 text-sm ${
                  resultado.ok ? 'bg-green-50 text-green-900' : 'bg-red-50 text-red-800'
                }`}
              >
                <p className="font-medium">{resultado.mensagem}</p>
                {resultado.conferencia && resultado.conferencia.divergencias.length > 0 ? (
                  <ul className="space-y-1">
                    {resultado.conferencia.divergencias.map((d, i) => (
                      <li key={`${d.codigo}-${i}`}>
                        • [{d.codigo}] {d.mensagem}
                      </li>
                    ))}
                  </ul>
                ) : null}
                {resultado.conferencia && resultado.conferencia.eventosConta408.length > 0 ? (
                  <p>
                    {resultado.conferencia.eventosConta408.length} lançamento(s) na Conta 408 (avaria/divergência).
                  </p>
                ) : null}
              </div>
            ) : null}

            <button type="button" className="botao-primario" onClick={enviar} disabled={!podeEnviar}>
              {pendente ? 'Registrando…' : 'Registrar recebimento'}
            </button>
          </section>
        </>
      ) : null}
    </div>
  );
}
