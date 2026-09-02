import { useState } from 'react';
import { Dialogo } from './Dialogo';
import { Campo } from './formulario';

/**
 * Pede um texto antes de concluir uma ação — o que o legado fazia com `prompt()`.
 * Quando `obrigatorio`, o botão só libera com algo escrito.
 */
export function DialogoMotivo({ titulo, rotulo, dica, rotuloConfirmar, obrigatorio, perigo, aoConfirmar, aoFechar }: {
  titulo: string;
  rotulo: string;
  dica?: string;
  rotuloConfirmar: string;
  obrigatorio?: boolean;
  perigo?: boolean;
  aoConfirmar: (texto: string) => void;
  aoFechar: () => void;
}) {
  const [texto, setTexto] = useState('');
  const vazio = !texto.trim();

  return (
    <Dialogo titulo={titulo} aoFechar={aoFechar} acoes={
      <>
        <button type="button" className="botao-secundario" onClick={aoFechar}>Cancelar</button>
        <button type="button" className={perigo ? 'botao-perigo' : 'botao'}
          disabled={obrigatorio && vazio} onClick={() => aoConfirmar(texto.trim())}>
          {rotuloConfirmar}
        </button>
      </>
    }>
      <Campo id="motivo-acao" rotulo={rotulo} dica={dica}>
        <input id="motivo-acao" value={texto} onChange={(e) => setTexto(e.target.value)} autoFocus />
      </Campo>
      {obrigatorio && vazio && <p className="sub mt-1">Este campo é obrigatório.</p>}
    </Dialogo>
  );
}
