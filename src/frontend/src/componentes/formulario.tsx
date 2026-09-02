import type { ReactNode } from 'react';

/** Campo rotulado. `dica` explica a regra em uma linha, como no legado. */
export function Campo({ id, rotulo, dica, children, className = '' }:
  { id?: string; rotulo: ReactNode; dica?: ReactNode; children: ReactNode; className?: string }) {
  const conteudo = (
    <>
      {rotulo}
      {dica && <span className="ml-1 font-normal text-texto-suave">{dica}</span>}
    </>
  );
  return (
    <div className={className}>
      {/* sem id não há o que rotular: vira texto, senão o <label> órfão disputa
          o nome acessível com o aria-label do próprio campo */}
      {id
        ? <label htmlFor={id}>{conteudo}</label>
        : <p className="mb-1 text-[12.5px] font-semibold text-texto-suave">{conteudo}</p>}
      {children}
    </div>
  );
}

/** Grade de duas colunas que vira uma só no celular. */
export const Grade2 = ({ children, className = '' }: { children: ReactNode; className?: string }) =>
  <div className={'grid grid-cols-1 gap-3 md:grid-cols-2 ' + className}>{children}</div>;

/** Explicação curta acima ou abaixo de um bloco. */
export const Nota = ({ children }: { children: ReactNode }) =>
  <p className="sub mt-1">{children}</p>;

/** Situação ativo/inativo, o par mais repetido nos cadastros. */
export function BadgeAtivo({ ativo, rotuloAtivo = 'ATIVO', rotuloInativo = 'INATIVO' }:
  { ativo: boolean; rotuloAtivo?: string; rotuloInativo?: string }) {
  return (
    <span className={'badge ' + (ativo ? 'bg-ok-fundo text-ok' : 'bg-slate-100 text-slate-500')}>
      {ativo ? rotuloAtivo : rotuloInativo}
    </span>
  );
}
