import { Component, type ErrorInfo, type ReactNode } from 'react';

interface Estado { erro: Error | null; pilhaDoComponente: string | null; copiado: boolean }

/**
 * O texto que o usuário manda para quem vai corrigir. Junta o que identifica o defeito —
 * a mensagem, a rota, a pilha e o componente que quebrou — porque sozinha a mensagem não
 * diz em qual tela nem em qual linha: `families.join is not a function` apareceu em três
 * lugares possíveis antes de a rota apontar qual era.
 */
export function detalhesDoErro(
  erro: Error, pilhaDoComponente: string | null, rota: string, agente?: string,
): string {
  return [
    `Erro: ${erro.message}`,
    `Rota: ${rota}`,
    erro.stack ? `\nPilha:\n${erro.stack}` : null,
    pilhaDoComponente ? `\nComponente:${pilhaDoComponente}` : null,
    agente ? `\nNavegador: ${agente}` : null,
  ].filter(Boolean).join('\n');
}

/**
 * Um erro de renderização mostra uma mensagem em vez de deixar a tela em branco.
 *
 * <p>
 * A tela também entrega os <b>detalhes técnicos</b>, e isso não é enfeite: quando `/pedidos`
 * caiu em produção, a única pista foi a frase que o usuário conseguiu transcrever da faixa.
 * Pilha e componente iam só para o `console`, onde ninguém que usa o sistema vai buscar.
 * </p>
 */
export class LimiteErro extends Component<{ children: ReactNode }, Estado> {
  state: Estado = { erro: null, pilhaDoComponente: null, copiado: false };

  static getDerivedStateFromError(erro: Error): Partial<Estado> { return { erro }; }

  componentDidCatch(erro: Error, info: ErrorInfo) {
    console.error('Erro na tela:', erro, info.componentStack);
    this.setState({ pilhaDoComponente: info.componentStack ?? null });
  }

  private texto() {
    return detalhesDoErro(
      this.state.erro!, this.state.pilhaDoComponente,
      typeof location === 'undefined' ? '—' : location.pathname + location.search,
      typeof navigator === 'undefined' ? undefined : navigator.userAgent,
    );
  }

  /** Copiar pode falhar (permissão, contexto sem HTTPS): o texto fica à vista de qualquer jeito. */
  private async copiar() {
    try {
      await navigator.clipboard.writeText(this.texto());
      this.setState({ copiado: true });
    } catch {
      this.setState({ copiado: false });
    }
  }

  render() {
    if (!this.state.erro) return this.props.children;
    return (
      <div className="m-8 rounded-painel border border-perigo/30 bg-perigo-fundo p-6 text-perigo" role="alert">
        <h1 className="mb-2 text-[16px] font-bold">Algo deu errado nesta tela.</h1>
        <p className="mb-1 text-[13.5px]">{this.state.erro.message}</p>
        <p className="mb-4 text-[12.5px] opacity-80">
          As outras telas continuam funcionando — o erro é desta.
        </p>
        <div className="flex flex-wrap gap-2">
          <button type="button" className="botao" onClick={() => location.reload()}>Recarregar</button>
          <button type="button" className="botao-secundario" onClick={() => void this.copiar()}>
            {this.state.copiado ? 'Detalhes copiados ✓' : 'Copiar detalhes'}
          </button>
        </div>
        <details className="mt-4">
          <summary className="cursor-pointer text-[12.5px] font-semibold">Detalhes técnicos</summary>
          <pre data-testid="detalhes-do-erro"
            className="mt-2 max-h-64 overflow-auto whitespace-pre-wrap rounded-lg bg-perigo/5 p-3 text-[11.5px]">
            {this.texto()}
          </pre>
        </details>
      </div>
    );
  }
}
