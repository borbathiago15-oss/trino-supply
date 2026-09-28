import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Link, MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { ToastProvider } from '@/componentes/Toast';
import { AVISO_FORA_DO_APP, ehTelaDoApp, EscopoDoApp, rotaNoApp } from './EscopoDoApp';

function Onde() {
  const { pathname, search } = useLocation();
  return <p data-testid="onde">{pathname}{search}</p>;
}

/** As telas do app com os links que as páginas de verdade têm: para outra tela do app e para fora. */
const abrir = (inicio: string) => render(
  <MemoryRouter initialEntries={[inicio]}>
    <ToastProvider>
      <EscopoDoApp />
      <Routes>
        <Route path="/app/aprovacoes" element={<>
          <Onde />
          <Link to="/diretoria?periodo=ano">Diretoria</Link>
          <Link to="/cotacoes/q1">Ver processo completo</Link>
          <Link to="/login">Sair</Link>
        </>} />
        <Route path="/app/diretoria" element={<Onde />} />
        <Route path="/app/fornecedores" element={<><Onde /><Link to="/fornecedores">ir</Link></>} />
        <Route path="/fornecedores" element={<Onde />} />
        <Route path="/cotacoes/:id" element={<Onde />} />
        <Route path="/login" element={<Onde />} />
      </Routes>
    </ToastProvider>
  </MemoryRouter>,
);

describe('o escopo do app de bolso', () => {
  it('só a casca e as três abas são o app', () => {
    expect(ehTelaDoApp('/app')).toBe(true);
    expect(ehTelaDoApp('/app/painel')).toBe(true);
    expect(ehTelaDoApp('/app/fornecedores')).toBe(false);   // favorito antigo, não é o app
    expect(rotaNoApp('/aprovacoes')).toBe('/app/aprovacoes');
    expect(rotaNoApp('/torre')).toBeNull();
  });

  it('o link para outra tela do app é reescrito para a rota do app, com a consulta', async () => {
    const usuario = userEvent.setup();
    abrir('/app/aprovacoes');
    await usuario.click(screen.getByRole('link', { name: 'Diretoria' }));
    expect(await screen.findByTestId('onde')).toHaveTextContent('/app/diretoria?periodo=ano');
  });

  it('o link para fora do app fica onde está e diz por quê', async () => {
    const usuario = userEvent.setup();
    abrir('/app/aprovacoes');
    await usuario.click(screen.getByRole('link', { name: 'Ver processo completo' }));
    expect(await screen.findByTestId('toast')).toHaveTextContent(AVISO_FORA_DO_APP);
    expect(screen.getByTestId('onde')).toHaveTextContent('/app/aprovacoes');
  });

  it('sair da sessão continua livre', async () => {
    const usuario = userEvent.setup();
    abrir('/app/aprovacoes');
    await usuario.click(screen.getByRole('link', { name: 'Sair' }));
    expect(await screen.findByTestId('onde')).toHaveTextContent('/login');
  });

  it('o favorito antigo em /app/* não é o app: o redirecionamento dele passa', async () => {
    const usuario = userEvent.setup();
    abrir('/app/fornecedores');
    await usuario.click(screen.getByRole('link', { name: 'ir' }));
    expect(await screen.findByTestId('onde')).toHaveTextContent('/fornecedores');
  });
});
