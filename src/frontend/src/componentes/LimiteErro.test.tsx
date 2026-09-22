import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { detalhesDoErro, LimiteErro } from './LimiteErro';

function Explode(): never { throw new Error('C.families.join is not a function'); }

describe('detalhesDoErro', () => {
  const erro = Object.assign(new Error('quebrou'), { stack: 'Error: quebrou\n  at X' });

  it('junta mensagem, rota, pilha e componente', () => {
    const t = detalhesDoErro(erro, '\n  in Painel', '/painel?de=2026-01', 'Chrome/1.0');
    expect(t).toContain('Erro: quebrou');
    expect(t).toContain('Rota: /painel?de=2026-01');
    expect(t).toContain('at X');
    expect(t).toContain('in Painel');
    expect(t).toContain('Chrome/1.0');
  });

  it('a rota entra mesmo sem pilha: é ela que diz qual tela caiu', () => {
    const semPilha = new Error('x');
    semPilha.stack = undefined;   // erro vindo de outro contexto pode chegar assim
    const t = detalhesDoErro(semPilha, null, '/pedidos');
    expect(t).toContain('Rota: /pedidos');
    expect(t).not.toContain('Pilha:');
    expect(t).not.toContain('Componente:');
  });
});

describe('<LimiteErro />', () => {
  let console_: typeof console.error;
  beforeEach(() => { console_ = console.error; console.error = vi.fn(); });
  afterEach(() => { console.error = console_; });

  it('deixa passar quando não há erro', () => {
    render(<LimiteErro><p>conteúdo</p></LimiteErro>);
    expect(screen.getByText('conteúdo')).toBeInTheDocument();
  });

  it('mostra a mensagem e os detalhes técnicos em vez de tela em branco', async () => {
    render(<LimiteErro><Explode /></LimiteErro>);
    expect(screen.getByRole('alert')).toBeInTheDocument();
    expect(screen.getByText('C.families.join is not a function')).toBeInTheDocument();
    // o que faltava para diagnosticar: rota e pilha à vista, não só no console
    expect(screen.getByTestId('detalhes-do-erro').textContent).toContain('Rota:');
  });

  it('copiar põe os detalhes na área de transferência', async () => {
    const escrever = vi.fn().mockResolvedValue(undefined);
    Object.assign(navigator, { clipboard: { writeText: escrever } });
    render(<LimiteErro><Explode /></LimiteErro>);
    await userEvent.click(screen.getByRole('button', { name: 'Copiar detalhes' }));
    expect(escrever).toHaveBeenCalledWith(expect.stringContaining('C.families.join is not a function'));
    expect(await screen.findByRole('button', { name: /Detalhes copiados/ })).toBeInTheDocument();
  });

  it('copiar que falha não derruba a tela de erro', async () => {
    Object.assign(navigator, { clipboard: { writeText: vi.fn().mockRejectedValue(new Error('negado')) } });
    render(<LimiteErro><Explode /></LimiteErro>);
    await userEvent.click(screen.getByRole('button', { name: 'Copiar detalhes' }));
    // o texto continua à vista, que é o caminho que não depende de permissão
    expect(screen.getByTestId('detalhes-do-erro')).toBeInTheDocument();
  });

  it('o erro de uma tela não contamina a próxima: a chave remonta o limite', () => {
    const { rerender } = render(<LimiteErro key="/painel"><Explode /></LimiteErro>);
    expect(screen.getByRole('alert')).toBeInTheDocument();
    // trocar de rota troca a chave, e o limite nasce limpo — sem isso o React
    // manteria o estado de erro e a tela seguinte também apareceria quebrada
    rerender(<LimiteErro key="/pedidos"><p>outra tela</p></LimiteErro>);
    expect(screen.getByText('outra tela')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('não oferece mais o sistema clássico, que não existe desde a migração', () => {
    render(<LimiteErro><Explode /></LimiteErro>);
    expect(screen.queryByText(/clássico/i)).not.toBeInTheDocument();
  });
});
