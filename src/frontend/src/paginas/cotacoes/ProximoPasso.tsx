import { Link } from 'react-router-dom';
import { conflitoDeSegregacao, type Processo } from '@/api/cotacoes';

export interface Passo {
  /** O que precisa acontecer agora, em uma frase. */
  titulo: string;
  /** Onde se faz — e, quando é fora daqui, o link que leva. */
  detalhe: string;
  rota?: string;
  rotulo?: string;
}

/**
 * O passo seguinte do processo, dito na própria tela.
 *
 * O ciclo atravessa três grupos do menu e o pior salto é a aprovação: quem
 * acaba de escolher o fornecedor aqui precisa da Central de Aprovação, que
 * fica fora de Compras. Em vez de exigir que a pessoa saiba a sequência de
 * cor, a tela diz qual é e leva até lá.
 */
export function proximoPasso(q: Processo, de?: string): Passo | null {
  // Quem já agiu no processo não aprova: dizer isso aqui poupa a ida à Central para
  // descobrir que o botão não existe para ela — e diz que a bola é de outra pessoa.
  const conflito = q.status === 'AGUARDANDO_GERENTE' ? conflitoDeSegregacao(q, de, 'manager')
    : q.status === 'AGUARDANDO_DIRETOR' ? conflitoDeSegregacao(q, de, 'director') : null;
  if (conflito) {
    return {
      titulo: q.status === 'AGUARDANDO_GERENTE' ? 'Aprovação de Nível 1' : 'Aprovação de Nível 2',
      detalhe: conflito,
    };
  }

  switch (q.status) {
    case 'COTACAO_ABERTA':
      return {
        titulo: 'Convidar fornecedores e registrar as propostas',
        detalhe: 'Com as propostas no mapa, encerre a cotação para análise — é o que libera a escolha do vencedor.',
      };
    case 'EM_ANALISE':
      return {
        titulo: 'Escolher o fornecedor vencedor',
        detalhe: 'A escolha é feita nesta tela, com a justificativa. Depois dela o processo segue para as duas aprovações.',
      };
    case 'AGUARDANDO_GERENTE':
      return {
        titulo: 'Aprovação de Nível 1',
        detalhe: 'A decisão é tomada na Central de Aprovação. Quem escolheu o fornecedor não aprova a própria escolha (RFQ-ERR-030).',
        rota: '/aprovacoes', rotulo: 'Ir para a Central de Aprovação',
      };
    case 'AGUARDANDO_DIRETOR':
      return {
        titulo: 'Aprovação de Nível 2',
        detalhe: 'A decisão é tomada na Central de Aprovação. Quem deu o Nível 1 não dá o Nível 2 (RFQ-ERR-030).',
        rota: '/aprovacoes', rotulo: 'Ir para a Central de Aprovação',
      };
    case 'APROVADO_PARA_EMISSAO':
      return {
        titulo: 'Registrar a O.C. fechada no ERP',
        detalhe: 'O registro é feito nesta tela. A O.C. sai do ERP SENIOR: sem ela o processo não fecha, a não ser com a observação dizendo por quê.',
      };
    case 'OC_REGISTRADA': {
      const unica = q.purchaseOrders.length === 1 ? q.purchaseOrders[0] : null;
      return {
        titulo: 'Faturamento e confirmação de entrega',
        detalhe: 'A compra está fechada. O que falta acontece na tela do pedido: nota fiscal, recebimento e entrega.',
        rota: unica ? `/pedidos/${unica.id}` : '/pedidos',
        rotulo: unica ? `Abrir o pedido ${unica.number ?? ''}`.trim() : 'Ir para Pedidos de Compra (O.C.)',
      };
    }
    default:
      // rejeitado, cancelado: não há passo seguinte a apontar
      return null;
  }
}

export function ProximoPasso({ processo, usuarioId }: { processo: Processo; usuarioId?: string }) {
  const passo = proximoPasso(processo, usuarioId);
  if (!passo) return null;

  return (
    <div data-testid="proximo-passo"
      className="mt-3 flex flex-wrap items-center justify-between gap-3 rounded-lg border border-marca/25 bg-marca/5 px-4 py-3">
      <div className="min-w-0">
        <p className="text-[11.5px] font-bold uppercase tracking-wide text-texto-suave">Próximo passo</p>
        <p className="text-[14px] font-bold">{passo.titulo}</p>
        <p className="mt-0.5 text-[12.5px] text-texto-suave">{passo.detalhe}</p>
      </div>
      {passo.rota && (
        <Link to={passo.rota} className="botao-secundario shrink-0">{passo.rotulo} →</Link>
      )}
    </div>
  );
}
