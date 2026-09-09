import { useState } from 'react';
import { alterarPrioridade } from '@/api/triagem';
import { Dialogo } from '@/componentes/Dialogo';
import { Campo } from '@/componentes/formulario';

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

export interface Pleito {
  /** Id da solicitação cuja prioridade muda. */
  id: string;
  /** Número exibido no título — é por ele que a pessoa reconhece o que está mudando. */
  numero: string;
  para: 'URGENT' | 'NORMAL';
}

/**
 * Mudança de prioridade com justificativa obrigatória.
 *
 * Mora fora das duas telas de propósito: a triagem de material e a Torre mudam a mesma
 * coisa, e a régua — urgente exige motivo <em>e</em> impacto, voltar a normal exige só o
 * motivo — é a mesma da SC urgente. Duplicada, uma das duas telas acabaria aceitando
 * urgência sem impacto, e a auditoria ficaria com metade da história.
 */
export function DialogoDePrioridade({ pleito, aoFechar, aoSalvar, aoAvisar }: {
  pleito: Pleito;
  aoFechar: () => void;
  aoSalvar: () => void;
  aoAvisar: (t: string, tipo?: 'ok' | 'erro') => void;
}) {
  const [motivo, setMotivo] = useState('');
  const [impacto, setImpacto] = useState('');
  const [salvando, setSalvando] = useState(false);
  const urgente = pleito.para === 'URGENT';
  const incompleto = !motivo.trim() || (urgente && !impacto.trim());

  async function salvar() {
    setSalvando(true);
    try {
      await alterarPrioridade(pleito.id, pleito.para, motivo.trim(), urgente ? impacto.trim() : null);
      aoAvisar('Prioridade alterada com justificativa registrada.');
      aoSalvar();
      aoFechar();
    } catch (e) { aoAvisar(mensagem(e, 'Falha ao alterar a prioridade.'), 'erro'); }
    finally { setSalvando(false); }
  }

  return (
    <Dialogo aoFechar={aoFechar}
      titulo={urgente ? `Tornar a ${pleito.numero} URGENTE` : `Voltar a ${pleito.numero} para Normal`}
      acoes={
        <>
          <button type="button" className="botao-secundario" onClick={aoFechar}>Cancelar</button>
          <button type="button" className="botao" disabled={incompleto || salvando} onClick={salvar}>
            Registrar mudança
          </button>
        </>
      }>
      <Campo id="pri-motivo" rotulo={urgente
        ? 'Por que esta demanda virou urgente?' : 'Por que esta demanda volta a Normal?'}>
        <input id="pri-motivo" value={motivo} onChange={(e) => setMotivo(e.target.value)} autoFocus />
      </Campo>
      {urgente && (
        <Campo id="pri-impacto" rotulo="Qual o impacto de não comprar?"
          dica="mesma régua da SC urgente" className="mt-3">
          <input id="pri-impacto" value={impacto} onChange={(e) => setImpacto(e.target.value)} />
        </Campo>
      )}
      {incompleto && <p className="sub mt-2">A justificativa fica registrada — os dois campos são obrigatórios.</p>}
    </Dialogo>
  );
}
