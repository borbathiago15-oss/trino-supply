import { useEffect, useRef, useState } from 'react';
import { commitNoAr, FOLGA_ENTRE_CONSULTAS, haNovaVersao, INTERVALO_DE_CONSULTA } from './novaVersao';

interface Props {
  /** A consulta ao servidor; os testes trocam por uma que responde o que o cenário pede. */
  consultar?: () => Promise<string | null>;
  /** O que "Atualizar" faz. Recarregar a página é o padrão; o teste observa em vez de recarregar. */
  recarregar?: () => void;
  intervalo?: number;
  folga?: number;
}

/**
 * A faixa "há uma versão nova", nas duas cascas. Consulta ao montar, ao voltar à vista
 * (é o caso do app de bolso: o Android o guarda na memória e a pessoa volta horas depois)
 * e a cada poucos minutos com a página à vista.
 *
 * **Nunca recarrega sozinha**: quem estiver no meio de uma SC ou de uma aprovação perderia o
 * que digitou. "Depois" esconde a faixa para aquela versão; um deploy seguinte a reacende.
 */
export function AvisoDeNovaVersao({
  consultar = commitNoAr, recarregar = () => globalThis.location.reload(),
  intervalo = INTERVALO_DE_CONSULTA, folga = FOLGA_ENTRE_CONSULTAS,
}: Props) {
  const carregada = useRef<string | null>(null);
  const ultimaConsulta = useRef(0);
  const [noAr, setNoAr] = useState<string | null>(null);
  const [dispensada, setDispensada] = useState<string | null>(null);

  useEffect(() => {
    let ativo = true;
    const conferir = async () => {
      ultimaConsulta.current = Date.now();
      const commit = await consultar();
      if (!ativo || !commit) return;
      // a primeira resposta é a versão desta página: é contra ela que as seguintes se comparam
      carregada.current ??= commit;
      setNoAr(commit);
    };
    const aoVoltar = () => {
      if (document.visibilityState !== 'visible') return;
      if (Date.now() - ultimaConsulta.current < folga) return;
      void conferir();
    };
    void conferir();
    document.addEventListener('visibilitychange', aoVoltar);
    const relogio = setInterval(() => { if (document.visibilityState === 'visible') void conferir(); }, intervalo);
    return () => {
      ativo = false;
      document.removeEventListener('visibilitychange', aoVoltar);
      clearInterval(relogio);
    };
  }, [consultar, intervalo, folga]);

  if (!haNovaVersao(carregada.current, noAr) || noAr === dispensada) return null;

  return (
    <div role="status" data-testid="aviso-de-nova-versao"
      className="mb-3 flex flex-wrap items-center justify-between gap-x-3 gap-y-2 rounded-lg border border-marca/40 bg-marca/10 px-3.5 py-2.5 text-[13.5px] text-texto">
      <span><strong>Há uma versão nova do Trino Supply.</strong> Atualize para continuar com a versão atual.</span>
      <span className="flex shrink-0 items-center gap-2">
        <button type="button" className="botao-secundario !py-1.5" onClick={() => setDispensada(noAr)}>Depois</button>
        <button type="button" className="botao !py-1.5" onClick={recarregar}>Atualizar</button>
      </span>
    </div>
  );
}
