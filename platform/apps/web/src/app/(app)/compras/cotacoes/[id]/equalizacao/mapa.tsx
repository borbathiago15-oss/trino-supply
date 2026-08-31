'use client';

import { useMemo, useState, useTransition } from 'react';
import { useRouter } from 'next/navigation';
import type { Fornecedor, NotaProposta, Proposta } from '@trino/contratos';
import { dinheiro } from '@/componentes/etiquetas';
import { equalizar, type ResultadoEqualizacao } from './acoes';

const CRITERIOS = [
  { chave: 'preco', rotulo: 'Preço' },
  { chave: 'lead_time', rotulo: 'Lead time' },
  { chave: 'frete', rotulo: 'Frete' },
  { chave: 'cond_pagto', rotulo: 'Pagamento' },
] as const;

const percentual = (v: number) => `${(v * 100).toFixed(0)}%`;
const nota = (v: number) => v.toFixed(3);

/**
 * Mapa comparativo. Cada coluna é um fornecedor; cada linha, um critério com
 * seu peso. A vencedora por nota vem pré-selecionada, mas quem decide é o
 * comprador — e se ele sair do menor preço, a justificativa passa a ser
 * obrigatória no próprio formulário.
 */
export function MapaComparativo({
  cotacaoId,
  propostas,
  fornecedores,
  matriz,
  pesos,
  encerrada,
  vencedoraRegistrada,
  justificativaRegistrada,
}: {
  cotacaoId: string;
  propostas: Proposta[];
  fornecedores: Map<string, Fornecedor>;
  matriz: NotaProposta[];
  pesos: Record<string, number>;
  encerrada: boolean;
  vencedoraRegistrada: string | null;
  justificativaRegistrada: string | null;
}) {
  const router = useRouter();
  const ordenada = useMemo(() => [...matriz].sort((a, b) => a.posicao - b.posicao), [matriz]);

  const [escolhida, setEscolhida] = useState<string>(vencedoraRegistrada ?? ordenada[0]?.propostaId ?? '');
  const [justificativa, setJustificativa] = useState(justificativaRegistrada ?? '');
  const [resultado, setResultado] = useState<ResultadoEqualizacao | null>(null);
  const [pendente, iniciar] = useTransition();

  // A marca `menorPreco` vem do domínio e é TRUE em TODAS as empatadas no menor
  // valor — comparar com um único id faria a tela exigir justificativa num
  // empate, que não é desvio nenhum.
  const linhaEscolhida = ordenada.find((n) => n.propostaId === escolhida);
  const desviaDoMenorPreco = linhaEscolhida !== undefined && !linhaEscolhida.menorPreco;
  const justificativaFaltando = desviaDoMenorPreco && justificativa.trim().length < 3;

  const confirmar = () => {
    iniciar(async () => {
      const resposta = await equalizar(cotacaoId, escolhida, desviaDoMenorPreco ? justificativa : undefined);
      setResultado(resposta);
      if (resposta.ok) router.refresh();
    });
  };

  const nomeFornecedor = (id: string) => fornecedores.get(id)?.razaoSocial ?? id.slice(0, 8);
  const propostaPorId = new Map(propostas.map((p) => [p.id, p]));

  return (
    <div className="space-y-6">
      <div className="card overflow-x-auto">
        <table className="min-w-full divide-y divide-slate-200">
          <thead className="bg-slate-50">
            <tr>
              <th className="cabecalho-tabela">Critério (peso)</th>
              {ordenada.map((linha) => (
                <th key={linha.propostaId} className="cabecalho-tabela">
                  <div className="flex flex-col gap-0.5">
                    <span className="text-slate-800">{nomeFornecedor(linha.fornecedorId)}</span>
                    <span className="font-normal normal-case text-slate-500">
                      {linha.menorPreco ? 'menor preço · ' : ''}
                      {linha.posicao}º na nota
                    </span>
                  </div>
                </th>
              ))}
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100 bg-white">
            {CRITERIOS.map(({ chave, rotulo }) => (
              <tr key={chave}>
                <td className="celula font-medium text-slate-700">
                  {rotulo} <span className="text-slate-400">({percentual(pesos[chave] ?? 0)})</span>
                </td>
                {ordenada.map((linha) => {
                  const valor = linha.notas[chave];
                  const melhor = valor === Math.max(...ordenada.map((l) => l.notas[chave]));
                  return (
                    <td key={linha.propostaId} className={`celula ${melhor ? 'font-semibold text-green-700' : 'text-slate-600'}`}>
                      {nota(valor)}
                    </td>
                  );
                })}
              </tr>
            ))}

            <tr className="bg-slate-50">
              <td className="celula font-semibold text-slate-800">Valor total</td>
              {ordenada.map((linha) => (
                <td key={linha.propostaId} className="celula text-slate-800">
                  {dinheiro(linha.valorTotal)}
                </td>
              ))}
            </tr>
            <tr>
              <td className="celula text-slate-700">Condição de pagamento</td>
              {ordenada.map((linha) => (
                <td key={linha.propostaId} className="celula text-slate-600">
                  {propostaPorId.get(linha.propostaId)?.condicaoPagamento ?? '—'}
                </td>
              ))}
            </tr>
            <tr>
              <td className="celula text-slate-700">Prazo de entrega</td>
              {ordenada.map((linha) => (
                <td key={linha.propostaId} className="celula text-slate-600">
                  {propostaPorId.get(linha.propostaId)?.prazoEntregaDias ?? '—'} dia(s)
                </td>
              ))}
            </tr>
            <tr className="bg-trino-100/60">
              <td className="celula font-semibold text-trino-900">Nota final</td>
              {ordenada.map((linha) => (
                <td key={linha.propostaId} className="celula font-semibold text-trino-900">
                  {nota(linha.notaFinal)}
                </td>
              ))}
            </tr>
            {!encerrada ? (
              <tr>
                <td className="celula text-slate-700">Vencedora</td>
                {ordenada.map((linha) => (
                  <td key={linha.propostaId} className="celula">
                    <label className="flex items-center gap-2">
                      <input
                        type="radio"
                        name="vencedora"
                        value={linha.propostaId}
                        checked={escolhida === linha.propostaId}
                        onChange={() => setEscolhida(linha.propostaId)}
                      />
                      <span className="text-sm">Escolher</span>
                    </label>
                  </td>
                ))}
              </tr>
            ) : null}
          </tbody>
        </table>
      </div>

      {encerrada ? (
        <div className="card p-5 text-sm">
          <p className="font-medium text-slate-800">
            Equalização encerrada — vencedora: {nomeFornecedor(
              ordenada.find((l) => l.propostaId === vencedoraRegistrada)?.fornecedorId ?? '',
            )}
          </p>
          {justificativaRegistrada ? (
            <p className="mt-2 text-slate-600">Justificativa do desvio: {justificativaRegistrada}</p>
          ) : (
            <p className="mt-2 text-slate-600">Escolha recaiu sobre a proposta de menor preço.</p>
          )}
        </div>
      ) : (
        <div className="card space-y-4 p-5">
          {desviaDoMenorPreco ? (
            <div>
              <label className="rotulo" htmlFor="justificativa-desvio">
                Justificativa do desvio (obrigatória — a escolhida não é a de menor preço)
              </label>
              <textarea
                id="justificativa-desvio"
                className="campo mt-1"
                rows={3}
                value={justificativa}
                onChange={(e) => setJustificativa(e.target.value)}
                placeholder="Ex.: entrega em 2 dias atende à parada de manutenção programada"
              />
              {justificativaFaltando ? (
                <p className="mt-1 text-xs text-red-700">Informe ao menos 3 caracteres.</p>
              ) : null}
            </div>
          ) : (
            <p className="text-sm text-slate-600">
              A proposta escolhida é a de menor preço — não é necessário justificar.
            </p>
          )}

          {resultado ? (
            <p
              role="status"
              className={`rounded-md px-3 py-2 text-sm ${
                resultado.ok ? 'bg-green-50 text-green-800' : 'bg-red-50 text-red-800'
              }`}
            >
              {resultado.mensagem}
            </p>
          ) : null}

          <button
            type="button"
            className="botao-primario"
            onClick={confirmar}
            disabled={pendente || escolhida === '' || justificativaFaltando}
          >
            {pendente ? 'Registrando…' : 'Registrar equalização'}
          </button>
        </div>
      )}
    </div>
  );
}
