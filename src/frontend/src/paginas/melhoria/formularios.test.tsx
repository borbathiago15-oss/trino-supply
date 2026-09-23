import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it } from 'vitest';
import { FormularioDaFerramenta, lerDados } from './formularios';

/** Monta o formulário controlado e devolve o que ele produziria ao salvar. */
function montar(chave: string, inicial: Record<string, unknown> = {}) {
  const saida: { dados: Record<string, unknown> } = { dados: inicial };
  function Casca() {
    const [dados, setDados] = useState(inicial);
    saida.dados = dados;
    return <FormularioDaFerramenta chave={chave} dados={dados} aoMudar={setDados} />;
  }
  render(<Casca />);
  return saida;
}

describe('lerDados', () => {
  it('texto vazio, nulo ou ilegível vira objeto vazio, e não quebra a tela', () => {
    expect(lerDados(null)).toEqual({});
    expect(lerDados('')).toEqual({});
    expect(lerDados('{isto não é json')).toEqual({});
    expect(lerDados('[1,2]')).toEqual({});
  });

  it('lê o objeto gravado', () => {
    expect(lerDados('{"efeito":"Avarias"}')).toEqual({ efeito: 'Avarias' });
  });
});

describe('5 Porquês', () => {
  it('guarda os degraus e a causa raiz', async () => {
    const saida = montar('CINCO_PORQUES');
    await userEvent.type(screen.getByLabelText('Problema'), 'Avarias na doca');
    await userEvent.type(screen.getByLabelText('1º por quê?'), 'Por que houve avaria?');
    await userEvent.type(screen.getByLabelText(/Causa raiz/), 'Sem limite afixado');

    expect(saida.dados.problema).toBe('Avarias na doca');
    expect(saida.dados.porque1).toBe('Por que houve avaria?');
    expect(saida.dados.causa_raiz).toBe('Sem limite afixado');
  });

  it('abre o que foi gravado no formato do Trino Intelligence', () => {
    montar('CINCO_PORQUES', { problem: 'Avarias', w1_why: 'Por quê?', root_cause: 'Sem limite' });
    expect(screen.getByLabelText('Problema')).toHaveValue('Avarias');
    expect(screen.getByLabelText('1º por quê?')).toHaveValue('Por quê?');
    expect(screen.getByLabelText(/Causa raiz/)).toHaveValue('Sem limite');
  });
});

describe('Ishikawa', () => {
  it('cada 6M é uma lista de linhas', async () => {
    const saida = montar('ISHIKAWA', { efeito: 'Avarias' });
    await userEvent.type(screen.getByLabelText(/Mão de obra/), 'Sem treinamento\nRotatividade');
    expect(saida.dados.mao_de_obra).toEqual(['Sem treinamento', 'Rotatividade']);
  });

  it('os seis eme estão lá', () => {
    montar('ISHIKAWA');
    for (const m of [/Máquina/, /Método/, /Mão de obra/, /Material/, /Medição/, /Meio ambiente/])
      expect(screen.getByLabelText(m)).toBeInTheDocument();
  });
});

describe('Pareto', () => {
  it('acrescenta e remove causas', async () => {
    const saida = montar('PARETO');
    await userEvent.click(screen.getByRole('button', { name: 'Acrescentar causa' }));
    await userEvent.type(screen.getByLabelText('Causa 1'), 'Manuseio');
    await userEvent.type(screen.getByLabelText('Valor 1'), '50');
    expect(saida.dados.itens).toEqual([{ causa: 'Manuseio', valor: 50 }]);

    await userEvent.click(screen.getByRole('button', { name: 'Remover' }));
    expect(saida.dados.itens).toEqual([]);
  });

  // a ordem e a faixa dos 80% são do servidor: repeti-las aqui daria dois donos
  // para a mesma régua
  it('não ordena nem marca vital antes de salvar', async () => {
    montar('PARETO', { itens: [{ causa: 'Pouco', valor: 5 }, { causa: 'Muito', valor: 95 }] });
    const tabela = screen.getByTestId('form-pareto');
    expect(within(tabela).getByLabelText('Causa 1')).toHaveValue('Pouco');
    expect(within(tabela).queryByText('VITAL')).toBeNull();
    expect(screen.getByText(/quem calcula é o servidor/)).toBeInTheDocument();
  });
});

describe('GUT', () => {
  it('mostra o produto ao digitar, mas não diz quem é vital', async () => {
    // multiplicar é aritmética e pode acontecer aqui; eleger depende de ordenar e do
    // piso de 27, e isso é do servidor
    montar('GUT', { itens: [{ problema: 'Fila', g: 5, u: 4, t: 3 }] });
    const tabela = screen.getByTestId('form-gut');
    expect(within(tabela).getByText('60')).toBeInTheDocument();
    expect(within(tabela).queryByText('VITAL')).toBeNull();
  });

  it('as notas vão de 1 a 5', async () => {
    montar('GUT', { itens: [{ problema: 'Fila', g: 3, u: 3, t: 3 }] });
    const g = screen.getByLabelText('G 1');
    expect(within(g).getAllByRole('option').map((o) => o.textContent))
      .toEqual(['—', '1', '2', '3', '4', '5']);
  });

  it('acrescenta um problema já com notas médias', async () => {
    const saida = montar('GUT');
    await userEvent.click(screen.getByRole('button', { name: 'Acrescentar problema' }));
    expect(saida.dados.itens).toEqual([{ problema: '', g: 3, u: 3, t: 3 }]);
  });
});

describe('5W2H', () => {
  it('tem as sete colunas e guarda a linha', async () => {
    const saida = montar('CINCO_W_DOIS_H');
    await userEvent.click(screen.getByRole('button', { name: 'Acrescentar linha' }));
    for (const c of ['O quê 1', 'Por quê 1', 'Onde 1', 'Quando 1', 'Quem 1', 'Como 1', 'Quanto 1'])
      expect(screen.getByLabelText(c)).toBeInTheDocument();

    await userEvent.type(screen.getByLabelText('O quê 1'), 'Afixar o cartaz');
    await userEvent.type(screen.getByLabelText('Quem 1'), 'Ana');
    expect(saida.dados.linhas).toEqual([{ oque: 'Afixar o cartaz', quem: 'Ana' }]);
  });

  it('abre a linha gravada no formato do Trino Intelligence', () => {
    montar('CINCO_W_DOIS_H', { rows: [{ what: 'Treinar', who: 'Bruno', how_much: 'R$ 0' }] });
    expect(screen.getByLabelText('O quê 1')).toHaveValue('Treinar');
    expect(screen.getByLabelText('Quanto 1')).toHaveValue('R$ 0');
  });
});

describe('Kaizen e Fluxograma', () => {
  it('o Kaizen guarda antes, depois, melhorias e resultado', async () => {
    const saida = montar('KAIZEN');
    await userEvent.type(screen.getByLabelText('Antes'), 'Palete solto');
    await userEvent.type(screen.getByLabelText('Depois'), 'Palete cintado');
    await userEvent.type(screen.getByLabelText(/Melhorias/), 'Cinta plástica');
    expect(saida.dados).toMatchObject({
      antes: 'Palete solto', depois: 'Palete cintado', melhorias: ['Cinta plástica'],
    });
  });

  it('o fluxograma pede os dois fluxos, porque a comparação é a informação', async () => {
    const saida = montar('FLUXOGRAMA');
    await userEvent.type(screen.getByLabelText(/Fluxo atual/), 'Recebe\nEmpilha');
    await userEvent.type(screen.getByLabelText(/Fluxo proposto/), 'Recebe\nConfere\nEmpilha');
    expect(saida.dados.atual).toEqual(['Recebe', 'Empilha']);
    expect(saida.dados.proposto).toEqual(['Recebe', 'Confere', 'Empilha']);
  });
});

describe('a saída de emergência', () => {
  it('ferramenta sem formulário próprio continua editável pelo JSON', () => {
    montar('ALGO_NOVO', { x: 1 });
    expect(screen.getByLabelText(/Dados da ferramenta/)).toHaveValue('{\n  "x": 1\n}');
  });
});
