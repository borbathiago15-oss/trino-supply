import { itensVisiveis, ehSubgrupo, type ItemMenu } from '@/layout/menu';

type Perfil = Parameters<typeof itensVisiveis>[0];

/**
 * As três telas do app, na ordem das abas. Quem pode ver cada uma é a mesma regra do menu do
 * sistema (`itensVisiveis`): o app não tem permissão própria, senão uma aba abriria para quem o
 * menu esconde — ou o contrário — e as duas portas discordariam sobre a mesma pessoa.
 */
export const ABAS_DO_APP = [
  { id: 'pr-approvals', rotulo: 'Aprovar', rota: '/app/aprovacoes' },
  { id: 'director-view', rotulo: 'Diretoria', rota: '/app/diretoria' },
  { id: 'supply-dash', rotulo: 'Dashboard', rota: '/app/painel' },
] as const;

export type AbaDoApp = (typeof ABAS_DO_APP)[number];

export function abasDoApp(u: Perfil): AbaDoApp[] {
  const ids = new Set<string>();
  for (const g of itensVisiveis(u))
    for (const i of g.itens) (ehSubgrupo(i) ? i.filhos : [i as ItemMenu]).forEach((f) => ids.add(f.id));
  return ABAS_DO_APP.filter((a) => ids.has(a.id));
}
