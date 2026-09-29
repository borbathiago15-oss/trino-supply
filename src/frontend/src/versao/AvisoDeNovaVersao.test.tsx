import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { AvisoDeNovaVersao } from './AvisoDeNovaVersao';
import { haNovaVersao } from './novaVersao';

/** Um servidor cujo commit no ar o teste troca no meio do caminho, como um deploy faz. */
function servidor(inicial: string | null) {
  let commit = inicial;
  return {
    consultar: vi.fn(async () => commit),
    publicar(novo: string | null) { commit = novo; },
  };
}

async function voltarAoApp() {
  await act(async () => {
    document.dispatchEvent(new Event('visibilitychange'));
    await Promise.resolve();
  });
}

describe('a régua da versão nova', () => {
  it('só avisa com as duas versões conhecidas e diferentes', () => {
    expect(haNovaVersao('abc', 'def')).toBe(true);
    expect(haNovaVersao('abc', 'abc')).toBe(false);
    expect(haNovaVersao(null, 'abc')).toBe(false);     // o ambiente não informa o commit
    expect(haNovaVersao('abc', null)).toBe(false);     // a rede falhou: silêncio, não aviso
    expect(haNovaVersao('', '')).toBe(false);
  });
});

describe('o aviso de versão nova', () => {
  it('fica quieto enquanto o servidor responde o mesmo commit', async () => {
    const s = servidor('aaa111');
    render(<AvisoDeNovaVersao consultar={s.consultar} folga={0} />);
    await voltarAoApp();
    await voltarAoApp();
    expect(screen.queryByTestId('aviso-de-nova-versao')).not.toBeInTheDocument();
    expect(s.consultar).toHaveBeenCalledTimes(3);
  });

  it('aparece quando a página volta à vista depois de um deploy, e "Atualizar" recarrega', async () => {
    const s = servidor('aaa111');
    const recarregar = vi.fn();
    render(<AvisoDeNovaVersao consultar={s.consultar} recarregar={recarregar} folga={0} />);
    await voltarAoApp();
    expect(screen.queryByTestId('aviso-de-nova-versao')).not.toBeInTheDocument();

    s.publicar('bbb222');
    await voltarAoApp();
    expect(screen.getByTestId('aviso-de-nova-versao')).toHaveTextContent('Há uma versão nova do Trino Supply.');
    expect(recarregar).not.toHaveBeenCalled();   // nunca recarrega sozinho
    await userEvent.click(screen.getByRole('button', { name: 'Atualizar' }));
    expect(recarregar).toHaveBeenCalledTimes(1);
  });

  it('"Depois" esconde a faixa daquela versão, e o deploy seguinte a reacende', async () => {
    const s = servidor('aaa111');
    render(<AvisoDeNovaVersao consultar={s.consultar} folga={0} />);
    await voltarAoApp();
    s.publicar('bbb222');
    await voltarAoApp();
    await userEvent.click(screen.getByRole('button', { name: 'Depois' }));
    expect(screen.queryByTestId('aviso-de-nova-versao')).not.toBeInTheDocument();
    await voltarAoApp();
    expect(screen.queryByTestId('aviso-de-nova-versao')).not.toBeInTheDocument();

    s.publicar('ccc333');
    await voltarAoApp();
    expect(screen.getByTestId('aviso-de-nova-versao')).toBeInTheDocument();
  });

  it('não avisa quando o ambiente não informa o commit, nem quando a rede falha depois', async () => {
    const semCommit = servidor(null);
    render(<AvisoDeNovaVersao consultar={semCommit.consultar} folga={0} />);
    await voltarAoApp();
    semCommit.publicar('bbb222');   // a primeira resposta válida vira a versão carregada
    await voltarAoApp();
    expect(screen.queryByTestId('aviso-de-nova-versao')).not.toBeInTheDocument();

    semCommit.publicar(null);       // a rede falhou: nulo não vira aviso
    await voltarAoApp();
    expect(screen.queryByTestId('aviso-de-nova-versao')).not.toBeInTheDocument();
  });

  it('voltar à vista duas vezes em seguida consulta uma vez só', async () => {
    const s = servidor('aaa111');
    render(<AvisoDeNovaVersao consultar={s.consultar} folga={60_000} />);
    await voltarAoApp();
    await voltarAoApp();
    expect(s.consultar).toHaveBeenCalledTimes(1);   // a da montagem; as outras caíram na folga
  });
});
