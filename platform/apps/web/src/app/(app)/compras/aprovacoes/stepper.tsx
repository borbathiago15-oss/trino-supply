import type { EtapaAprovacao } from '@trino/contratos';

/**
 * Cadeia de aprovação: um degrau por nível, mostrando o que já decidiu, o que
 * está em curso e o que ainda vem. É a resposta visual para "por que isto
 * ainda não chegou em mim?".
 */
export function Stepper({ etapas, nivelFinal }: { etapas: EtapaAprovacao[]; nivelFinal: number }) {
  const ordenadas = [...etapas].sort((a, b) => a.nivel - b.nivel);
  const primeiraPendente = ordenadas.find((e) => e.decisao === 'PENDENTE');

  return (
    <ol className="flex flex-wrap items-center gap-2">
      {ordenadas.map((etapa, indice) => {
        const emCurso = etapa.id === primeiraPendente?.id;
        const aprovada = etapa.decisao === 'APROVADO';
        const rejeitada = etapa.decisao === 'REJEITADO';

        const estilo = rejeitada
          ? 'border-red-300 bg-red-50 text-red-800'
          : aprovada
            ? 'border-green-300 bg-green-50 text-green-800'
            : emCurso
              ? 'border-trino-600 bg-trino-100 text-trino-800'
              : 'border-slate-200 bg-white text-slate-400';

        return (
          <li key={etapa.id} className="flex items-center gap-2">
            <div className={`rounded-md border px-3 py-2 text-xs ${estilo}`}>
              <p className="font-semibold">
                Nível {etapa.nivel}
                {etapa.nivel >= nivelFinal ? ' (final)' : ''}
              </p>
              <p>
                {rejeitada ? 'Rejeitado' : aprovada ? 'Aprovado' : emCurso ? 'Aguardando decisão' : 'Pendente'}
              </p>
            </div>
            {indice < ordenadas.length - 1 ? <span aria-hidden className="text-slate-300">→</span> : null}
          </li>
        );
      })}
    </ol>
  );
}
