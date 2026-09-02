import { Component, type ErrorInfo, type ReactNode } from 'react';

interface Estado { erro: Error | null }

/** Um erro de renderização mostra uma mensagem em vez de deixar a tela em branco. */
export class LimiteErro extends Component<{ children: ReactNode }, Estado> {
  state: Estado = { erro: null };
  static getDerivedStateFromError(erro: Error): Estado { return { erro }; }
  componentDidCatch(erro: Error, info: ErrorInfo) { console.error('Erro na tela:', erro, info.componentStack); }
  render() {
    if (!this.state.erro) return this.props.children;
    return (
      <div className="m-8 rounded-painel border border-perigo/30 bg-perigo-fundo p-6 text-perigo" role="alert">
        <h1 className="mb-2 text-[16px] font-bold">Algo deu errado nesta tela.</h1>
        <p className="mb-4 text-[13.5px]">{this.state.erro.message}</p>
        <div className="flex gap-2">
          <button type="button" className="botao" onClick={() => location.reload()}>Recarregar</button>
          <a className="botao-secundario" href="/">Abrir o Trino Supply clássico</a>
        </div>
      </div>
    );
  }
}
