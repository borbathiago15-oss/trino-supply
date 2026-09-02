import { BrowserRouter, Navigate, Outlet, Route, Routes, useLocation } from 'react-router-dom';
import { ToastProvider } from '@/componentes/Toast';
import { AppLayout } from '@/layout/AppLayout';
import { Login } from '@/paginas/login/Login';
import { PedidoDetalhe } from '@/paginas/pedidos/PedidoDetalhe';
import { PedidosLista } from '@/paginas/pedidos/PedidosLista';
import { CentrosCusto } from '@/paginas/centros-custo/CentrosCusto';
import { Familias } from '@/paginas/familias/Familias';
import { Fornecedores } from '@/paginas/fornecedores/Fornecedores';
import { SessaoProvider, useSessao } from '@/sessao/SessaoProvider';
import { Carregando } from '@/componentes/basicos';
import { LimiteErro } from '@/componentes/LimiteErro';

/** Só deixa passar com usuário; sem sessão, manda para o login guardando o destino. */
function Protegida() {
  const { usuario, carregando } = useSessao();
  const { pathname } = useLocation();
  if (carregando) return <div className="p-10"><Carregando texto="Abrindo a sessão…" /></div>;
  if (!usuario) return <Navigate to="/login" replace state={{ de: pathname }} />;
  return <Outlet />;
}

export function Rotas() {
  return (
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route element={<Protegida />}>
        <Route element={<AppLayout />}>
          <Route index element={<Navigate to="/pedidos" replace />} />
          <Route path="/pedidos" element={<PedidosLista />} />
          <Route path="/pedidos/:id" element={<PedidoDetalhe />} />
          <Route path="/fornecedores" element={<Fornecedores />} />
          <Route path="/familias" element={<Familias />} />
          <Route path="/centros-custo" element={<CentrosCusto />} />
        </Route>
      </Route>
      <Route path="*" element={<Navigate to="/pedidos" replace />} />
    </Routes>
  );
}

export default function App() {
  return (
    <LimiteErro>
      <BrowserRouter basename="/app">
        <ToastProvider>
          <SessaoProvider>
            <Rotas />
          </SessaoProvider>
        </ToastProvider>
      </BrowserRouter>
    </LimiteErro>
  );
}
