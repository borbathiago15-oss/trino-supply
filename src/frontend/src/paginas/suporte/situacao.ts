import type { Chamado, SituacaoDoChamado } from '@/api/suporte';

/** A cor diz de quem é a vez: a sua pede atenção, a do suporte é espera, resolvido é neutro. */
export const TOM_DA_SITUACAO: Record<SituacaoDoChamado, string> = {
  AGUARDANDO_USUARIO: 'bg-aviso-fundo text-aviso',
  AGUARDANDO_SUPORTE: 'bg-blue-50 text-blue-800',
  RESOLVIDO: 'bg-ok-fundo text-ok',
};

/**
 * A situação dita do ponto de vista de quem olha. "Aguardando o usuário" é a mesma coisa que
 * "aguardando você" — mas só para quem abriu; para o atendente, é a vez do outro.
 */
export function rotuloDaSituacao(c: Pick<Chamado, 'status'>, souDoSuporte: boolean): string {
  switch (c.status) {
    case 'AGUARDANDO_USUARIO': return souDoSuporte ? 'Aguardando quem abriu' : 'Aguardando você';
    case 'AGUARDANDO_SUPORTE': return souDoSuporte ? 'Aguardando o suporte' : 'Com o suporte';
    default: return 'Resolvido';
  }
}
