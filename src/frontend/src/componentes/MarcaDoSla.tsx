import { ROTULO_DO_SLA, textoDoSla, type SlaDoAtendimento } from '@/api/prazoDeAtendimento';

/**
 * O prazo de atendimento de uma solicitação, como a fila e o painel o mostram.
 *
 * <p>
 * Num lugar só porque <strong>duas telas mostram o mesmo prazo</strong> — a fila do
 * almoxarifado e o Painel de Atendimentos. Duplicar faria uma delas pintar de vermelho o que
 * a outra pinta de amarelo, e quem olhasse as duas não saberia em qual acreditar.
 * </p>
 *
 * <p>
 * A marca vem <strong>com o número</strong>: a cor sozinha não diz contra que prazo a linha
 * ficou vermelha, e como a solicitação responde pela família mais curta, o texto também diz
 * qual família impôs o prazo — senão o almoxarife não tem como conferir.
 * </p>
 */
export function MarcaDoSla({ sla }: { sla: SlaDoAtendimento | null | undefined }) {
  // sem prazo é traço, não "no prazo": zero em todas as famílias quer dizer que esta fila
  // não cobra tempo, e dizer "no prazo" afirmaria um veredito que ninguém deu
  if (!sla) return <span className="sub">—</span>;
  const texto = textoDoSla(sla);
  // prazo sem espera a medir (ainda sem a liberação do Nível 1) mostra só o número
  if (!sla.status) return <span className="sub">{texto}</span>;
  const { rotulo, classe } = ROTULO_DO_SLA[sla.status];
  return (
    <>
      <span className={'badge ' + classe}>{rotulo}</span>
      <div className="sub">{texto}</div>
    </>
  );
}
